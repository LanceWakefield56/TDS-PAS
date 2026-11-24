using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Claims;

public sealed record FunderEdit(Guid? Id, string Code, string Name, FunderKind Kind, SubmitBy SubmitBy, bool PriorApprovalRequired, string? ClaimsEmail, string? ProviderNumber, bool IsActive, string? Notes);
public sealed record ApprovalEdit(Guid PatientId, Guid? BookingId, Guid FunderId, string? MemberNumber, string? AccClaimNumber, string Procedure, decimal AmountRequested, string? Notes);
public sealed record ApprovalDecision(ApprovalStatus Status, string? Reference, decimal? ApprovedAmount, DateOnly? ValidUntil, string? Notes);
public sealed record ClaimCheck(string Item, bool Ok, string? Fix);
public sealed record RemittanceResult(Remittance? Remittance, IReadOnlyList<string> Errors);
public sealed record ClaimRow(Claim Claim, string Funder, string Patient, string? Mrn, string? Invoice, int AgeDays);

/// <summary>
/// ACC and insurer claiming (spec 7.12): prior approvals, claims made from invoices, claim files, responses and remittances.
/// Remittance payments are recorded in Billing, so the invoice balance is always the truth.
/// Direct funder APIs need provider agreements; until then claims go by file or portal, which is always the fallback.
/// </summary>
public sealed partial class ClaimsService(ClaimsDbContext db, BillingDbContext billingDb, BillingService billing, PatientsDbContext patients, AuditWriter audit, TimeProvider clock)
{
    [GeneratedRegex("^[A-Z0-9]{6,12}$")] private static partial Regex AccClaimNumberRx();

    // ---------- Funders ----------
    public Task<List<Funder>> FundersAsync(bool activeOnly, CancellationToken ct = default) =>
        db.Funders.AsNoTracking().Where(f => !activeOnly || f.IsActive).OrderBy(f => f.Kind).ThenBy(f => f.Name).ToListAsync(ct);

    public async Task<(Funder? Funder, string? Error)> SaveFunderAsync(FunderEdit e, string user, CancellationToken ct = default)
    {
        var code = (e.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 20 || !code.All(char.IsLetterOrDigit)) return (null, "The code must be 2–20 letters or numbers.");
        if (string.IsNullOrWhiteSpace(e.Name)) return (null, "Enter the funder's name.");
        if (!Enum.IsDefined(e.Kind) || !Enum.IsDefined(e.SubmitBy)) return (null, "Choose the funder type and how claims are sent.");
        if (!string.IsNullOrWhiteSpace(e.ClaimsEmail) && !e.ClaimsEmail.Contains('@', StringComparison.Ordinal)) return (null, "The claims email address doesn't look right.");
        if (await db.Funders.AnyAsync(f => f.Code == code && f.Id != e.Id, ct)) return (null, $"Another funder already uses the code {code}.");
        var f = e.Id is { } id ? await db.Funders.FindAsync([id], ct) : null;
        if (e.Id is not null && f is null) return (null, "Funder not found.");
        if (f is null) { f = new Funder(); db.Funders.Add(f); }
        f.Code = code; f.Name = e.Name.Trim(); f.Kind = e.Kind; f.SubmitBy = e.SubmitBy; f.PriorApprovalRequired = e.PriorApprovalRequired;
        f.ClaimsEmail = Blank(e.ClaimsEmail); f.ProviderNumber = Blank(e.ProviderNumber); f.IsActive = e.IsActive; f.Notes = Blank(e.Notes);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "Funder", f.Id.ToString(), reason: $"{f.Code} {f.Name}", ct: ct);
        return (f, null);
    }

    /// <summary>NZ funders TDS commonly claims from. Provider numbers and claims addresses are entered by Finance.</summary>
    public async Task SeedFundersAsync(CancellationToken ct = default)
    {
        (string Code, string Name, FunderKind Kind, bool Prior)[] list =
        [
            ("ACC", "ACC (Accident Compensation Corporation)", FunderKind.Acc, true),
            ("SX", "Southern Cross Health Insurance", FunderKind.Insurer, true),
            ("NIB", "nib NZ", FunderKind.Insurer, true),
            ("AIA", "AIA NZ", FunderKind.Insurer, true),
            ("PL", "Partners Life", FunderKind.Insurer, true),
            ("ACCURO", "Accuro Health Insurance", FunderKind.Insurer, true),
            ("UNIMED", "UniMed", FunderKind.Insurer, true),
            ("HNZ", "Health New Zealand – Nelson Marlborough (outsourced)", FunderKind.HealthNz, false),
        ];
        foreach (var (code, name, kind, prior) in list)
            if (!await db.Funders.AnyAsync(f => f.Code == code, ct))
                db.Funders.Add(new Funder { Code = code, Name = name, Kind = kind, PriorApprovalRequired = prior, SubmitBy = kind == FunderKind.Acc ? SubmitBy.Portal : SubmitBy.File });
        await db.SaveChangesAsync(ct);
    }

    // ---------- Prior approvals ----------
    public Task<List<PriorApproval>> ApprovalsAsync(Guid? patientId, Guid? bookingId, bool openOnly, CancellationToken ct = default) =>
        db.Approvals.AsNoTracking()
            .Where(a => (patientId == null || a.PatientId == patientId) && (bookingId == null || a.BookingId == bookingId))
            .Where(a => !openOnly || a.Status == ApprovalStatus.Draft || a.Status == ApprovalStatus.Requested)
            .OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync(ct);

    public async Task<(PriorApproval? Approval, IReadOnlyList<string> Errors)> CreateApprovalAsync(ApprovalEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var funder = await db.Funders.FindAsync([e.FunderId], ct);
        if (funder is null || !funder.IsActive) errors.Add("Choose the funder.");
        if (!await patients.Patients.AnyAsync(p => p.Id == e.PatientId, ct)) errors.Add("Patient not found.");
        if (string.IsNullOrWhiteSpace(e.Procedure)) errors.Add("Enter the planned procedure.");
        if (e.AmountRequested <= 0 || e.AmountRequested > 1_000_000) errors.Add("Enter the amount to request.");
        errors.AddRange(IdentifierErrors(funder, e.MemberNumber, e.AccClaimNumber));
        if (errors.Count > 0) return (null, errors);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var s = await db.Settings.SingleAsync(ct);
            var a = new PriorApproval
            {
                Number = $"PA-{NzTime.Today(clock).Year}-{s.NextApproval:D4}",
                PatientId = e.PatientId,
                BookingId = e.BookingId,
                FunderId = e.FunderId,
                MemberNumber = Blank(e.MemberNumber),
                AccClaimNumber = Blank(e.AccClaimNumber)?.ToUpperInvariant(),
                Procedure = e.Procedure.Trim(),
                AmountRequested = BillingService.Money(e.AmountRequested),
                Notes = Blank(e.Notes),
                CreatedAt = clock.GetUtcNow(),
                CreatedBy = user,
            };
            s.NextApproval++;
            db.Approvals.Add(a);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await audit.WriteAsync(user, "Create", "PriorApproval", a.Id.ToString(), a.PatientId.ToString(), reason: $"{a.Number} {funder!.Code} {a.AmountRequested:0.00}", ct: ct);
            return ((PriorApproval?)a, (IReadOnlyList<string>)[]);
        });
    }

    public async Task<string?> MarkRequestedAsync(Guid id, string user, CancellationToken ct = default)
    {
        var a = await db.Approvals.FindAsync([id], ct);
        if (a is null) return "Prior approval not found.";
        if (a.Status != ApprovalStatus.Draft) return "Only a draft can be marked as sent.";
        a.Status = ApprovalStatus.Requested;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Requested", "PriorApproval", a.Id.ToString(), a.PatientId.ToString(), reason: a.Number, ct: ct);
        return null;
    }

    public async Task<string?> DecideApprovalAsync(Guid id, ApprovalDecision d, string user, CancellationToken ct = default)
    {
        var a = await db.Approvals.FindAsync([id], ct);
        if (a is null) return "Prior approval not found.";
        if (a.Status is not (ApprovalStatus.Draft or ApprovalStatus.Requested)) return "This prior approval has already been decided.";
        if (d.Status is not (ApprovalStatus.Approved or ApprovalStatus.PartApproved or ApprovalStatus.Declined)) return "Choose approved, part approved or declined.";
        if (d.Status != ApprovalStatus.Declined)
        {
            if (string.IsNullOrWhiteSpace(d.Reference)) return "Enter the funder's approval reference.";
            if (d.ApprovedAmount is not > 0) return "Enter the approved amount.";
            if (d.Status == ApprovalStatus.Approved && d.ApprovedAmount < a.AmountRequested) return "The approved amount is less than requested – choose part approved.";
            if (d.ValidUntil is null || d.ValidUntil < NzTime.Today(clock)) return "Enter the date the approval is valid until.";
        }
        else if (string.IsNullOrWhiteSpace(d.Notes)) return "Record why the funder declined.";
        a.Status = d.Status; a.ApprovalReference = Blank(d.Reference); a.ApprovedAmount = d.ApprovedAmount is { } m ? BillingService.Money(m) : null;
        a.ValidUntil = d.ValidUntil; a.Notes = Blank(d.Notes) ?? a.Notes; a.DecidedAt = clock.GetUtcNow(); a.DecidedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Decide", "PriorApproval", a.Id.ToString(), a.PatientId.ToString(), reason: $"{a.Number} {a.Status} {a.ApprovedAmount:0.00}", ct: ct);
        return null;
    }

    /// <summary>Approvals past their valid-until date become Expired (daily, by the claims worker).</summary>
    public async Task<int> ExpireApprovalsAsync(CancellationToken ct = default)
    {
        var today = NzTime.Today(clock);
        var list = await db.Approvals.Where(a => (a.Status == ApprovalStatus.Approved || a.Status == ApprovalStatus.PartApproved) && a.ValidUntil < today).ToListAsync(ct);
        foreach (var a in list) a.Status = ApprovalStatus.Expired;
        await db.SaveChangesAsync(ct);
        return list.Count;
    }

    // ---------- Claims ----------
    public async Task<List<ClaimRow>> ClaimsAsync(ClaimStatus? status, Guid? funderId, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Claims.AsNoTracking().Where(c => (status == null || c.Status == status) && (funderId == null || c.FunderId == funderId) && (patientId == null || c.PatientId == patientId));
        var list = await q.OrderByDescending(c => c.CreatedAt).Take(1000).ToListAsync(ct);
        return await RowsAsync(list, ct);
    }

    public async Task<Claim?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Claims.AsNoTracking().Include(c => c.Lines.OrderBy(l => l.Sequence)).SingleOrDefaultAsync(c => c.Id == id, ct);

    private async Task<List<ClaimRow>> RowsAsync(List<Claim> list, CancellationToken ct)
    {
        var funders = await db.Funders.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var pids = list.Select(c => c.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { Name = p.FamilyName.ToUpperInvariant() + ", " + p.GivenNames, p.Mrn }, ct);
        var iids = list.Select(c => c.InvoiceId).Distinct().ToList();
        var inv = await billingDb.Documents.AsNoTracking().Where(d => iids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
        var today = NzTime.Today(clock);
        return [.. list.Select(c => new ClaimRow(c, funders.GetValueOrDefault(c.FunderId) ?? "?", people.GetValueOrDefault(c.PatientId)?.Name ?? "?", people.GetValueOrDefault(c.PatientId)?.Mrn,
            inv.GetValueOrDefault(c.InvoiceId), today.DayNumber - (c.SubmittedAt ?? c.CreatedAt).NzDate().DayNumber))];
    }

    /// <summary>Makes a claim from an issued invoice whose payer is ACC, an insurer or Health NZ. The lines are a snapshot of the invoice.</summary>
    public async Task<(Claim? Claim, IReadOnlyList<string> Errors)> CreateFromInvoiceAsync(Guid invoiceId, Guid funderId, string? memberNumber, string? accClaimNumber, Guid? approvalId, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var d = await billingDb.Documents.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == invoiceId && x.Kind == DocumentKind.Invoice, ct);
        var funder = await db.Funders.FindAsync([funderId], ct);
        if (d is null) return (null, ["Invoice not found."]);
        if (d.Status is not (DocumentStatus.Issued or DocumentStatus.PartPaid)) errors.Add("Issue the invoice before claiming it.");
        if (d.Payer is not (Payer.Acc or Payer.Insurer or Payer.HealthNz)) errors.Add("The invoice's payer is not ACC, an insurer or Health NZ – change the payer on the invoice first.");
        if (funder is null || !funder.IsActive) errors.Add("Choose the funder.");
        else if ((funder.Kind == FunderKind.Acc) != (d.Payer == Payer.Acc)) errors.Add("The funder doesn't match the invoice's payer.");
        if (await db.Claims.AnyAsync(c => c.InvoiceId == invoiceId && c.Status != ClaimStatus.WrittenOff, ct)) errors.Add("This invoice already has a claim.");
        PriorApproval? pa = null;
        if (approvalId is { } aid)
        {
            pa = await db.Approvals.FindAsync([aid], ct);
            if (pa is null || pa.PatientId != d.PatientId || pa.FunderId != funderId) errors.Add("That prior approval is for a different patient or funder.");
        }
        var balance = BillingService.Calculate(d).Balance;
        if (balance <= 0) errors.Add("Nothing is owing on this invoice.");
        if (errors.Count > 0) return (null, errors);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var s = await db.Settings.SingleAsync(ct);
            var c = new Claim
            {
                Number = $"CL-{NzTime.Today(clock).Year}-{s.NextClaim:D5}",
                InvoiceId = d.Id,
                PatientId = d.PatientId,
                FunderId = funderId,
                PriorApprovalId = pa?.Id,
                MemberNumber = Blank(memberNumber) ?? pa?.MemberNumber ?? (d.Payer == Payer.Insurer ? Blank(d.PayerReference) : null),
                AccClaimNumber = (Blank(accClaimNumber) ?? pa?.AccClaimNumber ?? (d.Payer == Payer.Acc ? Blank(d.PayerReference) : null))?.ToUpperInvariant(),
                Amount = balance,
                CreatedAt = clock.GetUtcNow(),
                CreatedBy = user,
            };
            var seq = 1;
            foreach (var l in d.Lines.OrderBy(l => l.Sequence))
            {
                var net = BillingService.Money(l.Quantity * l.UnitPrice);
                var gross = l.GstApplicable ? BillingService.Money(net * (1 + d.GstPercent / 100m)) : net;
                c.Lines.Add(new ClaimLine { Sequence = seq++, Code = l.Code, Description = l.Description.Length > 300 ? l.Description[..300] : l.Description, Amount = gross });
            }
            s.NextClaim++;
            db.Claims.Add(c);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await audit.WriteAsync(user, "Create", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} {d.Number} {funder!.Code} {c.Amount:0.00}", ct: ct);
            return ((Claim?)c, (IReadOnlyList<string>)[]);
        });
    }

    /// <summary>Plain-English checks before a claim goes. Every failing check says how to fix it.</summary>
    public async Task<List<ClaimCheck>> ChecksAsync(Guid claimId, CancellationToken ct = default)
    {
        var c = await db.Claims.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == claimId, ct);
        if (c is null) return [new("Claim", false, "Claim not found.")];
        var f = await db.Funders.AsNoTracking().SingleAsync(x => x.Id == c.FunderId, ct);
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.PatientId, ct);
        var pa = c.PriorApprovalId is { } aid ? await db.Approvals.AsNoTracking().SingleOrDefaultAsync(a => a.Id == aid, ct) : null;
        var today = NzTime.Today(clock);
        var list = new List<ClaimCheck>
        {
            new("Patient NHI recorded", !string.IsNullOrEmpty(p?.Nhi), "Confirm the patient's NHI on the patient page (Check NHI)."),
            new("Claim has lines", c.Lines.Count > 0, "The invoice has no lines – fix the invoice and make the claim again."),
        };
        if (f.Kind == FunderKind.Acc)
            list.Add(new("ACC claim number", c.AccClaimNumber is { } n && AccClaimNumberRx().IsMatch(n), "Enter the ACC claim number from the ACC45 or the approval letter."));
        else if (f.Kind == FunderKind.Insurer)
            list.Add(new("Membership number", !string.IsNullOrWhiteSpace(c.MemberNumber), "Enter the patient's policy or membership number."));
        if (f.PriorApprovalRequired)
        {
            list.Add(new("Prior approval attached", pa is not null, "Attach the funder's prior approval (or record it first under Prior approvals)."));
            if (pa is not null)
            {
                list.Add(new("Prior approval granted", pa.Status is ApprovalStatus.Approved or ApprovalStatus.PartApproved, "The funder hasn't approved this yet – record their decision."));
                list.Add(new("Prior approval still valid", pa.ValidUntil is { } v && v >= (c.CreatedAt.NzDate() < today ? c.CreatedAt.NzDate() : today), "The approval has expired – ask the funder to extend it."));
                list.Add(new("Within the approved amount", pa.ApprovedAmount is { } m && c.Amount <= m, $"The claim ({c.Amount:0.00}) is more than approved ({pa.ApprovedAmount:0.00}) – bill the difference to the patient or ask the funder to increase the approval."));
            }
        }
        return list;
    }

    public async Task<(bool Ready, List<ClaimCheck> Checks, string? Error)> MarkReadyAsync(Guid id, string? memberNumber, string? accClaimNumber, Guid? approvalId, string user, CancellationToken ct = default)
    {
        var c = await db.Claims.FindAsync([id], ct);
        if (c is null) return (false, [], "Claim not found.");
        if (c.Status is not (ClaimStatus.Draft or ClaimStatus.Ready or ClaimStatus.Rejected)) return (false, [], "This claim has already been sent.");
        var f = await db.Funders.FindAsync([c.FunderId], ct);
        var idErrors = IdentifierErrors(f, memberNumber ?? c.MemberNumber, accClaimNumber ?? c.AccClaimNumber);
        if (idErrors.Count > 0) return (false, [], idErrors[0]);
        if (memberNumber is not null) c.MemberNumber = Blank(memberNumber);
        if (accClaimNumber is not null) c.AccClaimNumber = Blank(accClaimNumber)?.ToUpperInvariant();
        if (approvalId is { } aid)
        {
            var pa = await db.Approvals.FindAsync([aid], ct);
            if (pa is null || pa.PatientId != c.PatientId || pa.FunderId != c.FunderId) return (false, [], "That prior approval is for a different patient or funder.");
            c.PriorApprovalId = aid;
        }
        await db.SaveChangesAsync(ct);
        var checks = await ChecksAsync(id, ct);
        var ready = checks.All(x => x.Ok);
        c.Status = ready ? ClaimStatus.Ready : ClaimStatus.Draft;
        await db.SaveChangesAsync(ct);
        if (ready) await audit.WriteAsync(user, "Ready", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: c.Number, ct: ct);
        return (ready, checks, null);
    }

    /// <summary>Claim file (CSV) for every ready claim for one funder; the claims become Submitted. Upload or email the file, or key it into the portal.</summary>
    public async Task<(ClaimBatch? Batch, byte[]? File, string? Error)> BuildBatchAsync(Guid funderId, string user, CancellationToken ct = default)
    {
        var f = await db.Funders.FindAsync([funderId], ct);
        if (f is null) return (null, null, "Funder not found.");
        var claims = await db.Claims.Include(c => c.Lines).Where(c => c.FunderId == funderId && c.Status == ClaimStatus.Ready).OrderBy(c => c.Number).ToListAsync(ct);
        if (claims.Count == 0) return (null, null, "No claims are ready for this funder.");
        var file = await FileAsync(f, claims, ct);
        var now = clock.GetUtcNow();
        var batch = new ClaimBatch
        {
            FunderId = funderId,
            FileName = $"claims-{f.Code}-{now.ToNz():yyyyMMdd-HHmm}.csv",
            Claims = claims.Count,
            Total = claims.Sum(c => c.Amount),
            CreatedAt = now,
            CreatedBy = user,
        };
        db.Batches.Add(batch);
        foreach (var c in claims) { c.Status = ClaimStatus.Submitted; c.BatchId = batch.Id; c.SubmittedAt = now; c.SubmittedBy = user; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Submit", "ClaimBatch", batch.Id.ToString(), reason: $"{f.Code} {claims.Count} claims {batch.Total:0.00}", ct: ct);
        return (batch, file, null);
    }

    /// <summary>The claim file for an earlier batch, rebuilt from the claims in it (for a lost download).</summary>
    public async Task<(ClaimBatch? Batch, byte[]? File)> BatchFileAsync(Guid batchId, CancellationToken ct = default)
    {
        var b = await db.Batches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId, ct);
        if (b is null) return (null, null);
        var f = await db.Funders.AsNoTracking().SingleAsync(x => x.Id == b.FunderId, ct);
        var claims = await db.Claims.AsNoTracking().Include(c => c.Lines).Where(c => c.BatchId == batchId).OrderBy(c => c.Number).ToListAsync(ct);
        return (b, await FileAsync(f, claims, ct));
    }

    private async Task<byte[]> FileAsync(Funder f, List<Claim> claims, CancellationToken ct)
    {
        var pids = claims.Select(c => c.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var iids = claims.Select(c => c.InvoiceId).ToList();
        var docs = await billingDb.Documents.AsNoTracking().Where(d => iids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        var approvals = await db.Approvals.AsNoTracking().Where(a => claims.Select(c => c.PriorApprovalId).Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var sb = new StringBuilder();
        sb.Append(ClaimsCsv.Row("ProviderNumber", "ClaimReference", "InvoiceNumber", "ServiceDate", "NHI", "FamilyName", "GivenNames", "DateOfBirth",
            "MemberNumber", "AccClaimNumber", "PriorApproval", "ItemCode", "Description", "Amount")).Append("\r\n");
        foreach (var c in claims)
        {
            var p = people[c.PatientId]; var d = docs[c.InvoiceId];
            var pa = c.PriorApprovalId is { } aid && approvals.TryGetValue(aid, out var a) ? a.ApprovalReference : null;
            foreach (var l in c.Lines.OrderBy(l => l.Sequence))
                sb.Append(ClaimsCsv.Row(f.ProviderNumber, c.Number, d.Number, ClaimsCsv.Date(d.IssuedOn ?? c.CreatedAt.NzDate()), p.Nhi, p.FamilyName, p.GivenNames,
                    ClaimsCsv.Date(p.DateOfBirth), c.MemberNumber, c.AccClaimNumber, pa, l.Code, l.Description, ClaimsCsv.Money(l.Amount))).Append("\r\n");
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>Mark a single claim as sent through the funder's portal (no file).</summary>
    public async Task<string?> MarkSubmittedAsync(Guid id, string user, CancellationToken ct = default)
    {
        var c = await db.Claims.FindAsync([id], ct);
        if (c is null) return "Claim not found.";
        if (c.Status != ClaimStatus.Ready) return "Only a ready claim can be marked as sent.";
        c.Status = ClaimStatus.Submitted; c.SubmittedAt = clock.GetUtcNow(); c.SubmittedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Submit", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} (portal)", ct: ct);
        return null;
    }

    public async Task<string?> RecordResponseAsync(Guid id, bool accepted, string? text, string user, CancellationToken ct = default)
    {
        var c = await db.Claims.FindAsync([id], ct);
        if (c is null) return "Claim not found.";
        if (c.Status is not (ClaimStatus.Submitted or ClaimStatus.Accepted)) return "Record a response only for a sent claim.";
        if (!accepted && string.IsNullOrWhiteSpace(text)) return "Record the funder's reason for rejecting the claim.";
        c.Status = accepted ? ClaimStatus.Accepted : ClaimStatus.Rejected; c.ResponseAt = clock.GetUtcNow(); c.ResponseText = Blank(text);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, accepted ? "Accepted" : "Rejected", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} {text}", ct: ct);
        return null;
    }

    public async Task<string?> WriteOffAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var c = await db.Claims.FindAsync([id], ct);
        if (c is null) return "Claim not found.";
        if (c.Status is ClaimStatus.Paid or ClaimStatus.WrittenOff) return "This claim is already closed.";
        if (string.IsNullOrWhiteSpace(reason)) return "Record why the claim is being closed (the balance stays on the invoice for the patient or another funder).";
        c.Status = ClaimStatus.WrittenOff; c.ResponseText = reason.Trim(); c.ResponseAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "WriteOff", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} {reason}", ct: ct);
        return null;
    }

    /// <summary>
    /// Imports a funder remittance CSV (columns: claim reference, amount, outcome, reason). Matched paid lines record a payment on the invoice.
    /// The same file can't be imported twice. Unmatched lines are kept for Finance to follow up.
    /// </summary>
    public async Task<RemittanceResult> ImportRemittanceAsync(Guid funderId, string fileName, string text, string user, CancellationToken ct = default)
    {
        var f = await db.Funders.FindAsync([funderId], ct);
        if (f is null) return new(null, ["Choose the funder."]);
        if (text.Length > 5_000_000) return new(null, ["The file is too large (5 MB)."]);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        if (await db.Remittances.AnyAsync(r => r.FileHash == hash, ct)) return new(null, ["This remittance file has already been imported."]);
        var rows = ClaimsCsv.Parse(text);
        if (rows.Count < 2) return new(null, ["The file has no remittance lines."]);
        var h = rows[0];
        int cRef = ClaimsCsv.Column(h, "ClaimReference", "Claim", "ClaimNumber", "YourReference", "ProviderReference"), cAmt = ClaimsCsv.Column(h, "AmountPaid", "Paid", "Amount"),
            cOut = ClaimsCsv.Column(h, "Outcome", "Status", "Result"), cWhy = ClaimsCsv.Column(h, "Reason", "Comment", "DeclineReason");
        if (cRef < 0 || cAmt < 0) return new(null, ["The file needs a claim reference column and an amount paid column."]);
        var rem = new Remittance { FunderId = funderId, FileName = Path.GetFileName(fileName ?? "remittance.csv"), FileHash = hash, ImportedAt = clock.GetUtcNow(), ImportedBy = user };
        if (rem.FileName.Length > 200) rem.FileName = rem.FileName[..200];
        var seq = 0;
        foreach (var r in rows.Skip(1))
        {
            string Cell(int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
            var line = new RemittanceLine { Sequence = ++seq, ClaimReference = Trunc(Cell(cRef), 50), Outcome = Trunc(Cell(cOut), 20), Reason = TruncN(Blank(Cell(cWhy)), 300) };
            if (!ClaimsCsv.TryMoney(Cell(cAmt), out var amt) || amt < 0) { line.Result = "Amount not understood"; rem.Lines.Add(line); continue; }
            line.Amount = BillingService.Money(amt);
            var claim = await db.Claims.SingleOrDefaultAsync(c => c.Number == line.ClaimReference && c.FunderId == funderId, ct);
            if (claim is null) { line.Result = "No matching claim for this funder"; rem.Lines.Add(line); continue; }
            line.ClaimId = claim.Id;
            var declined = line.Amount == 0 || line.Outcome.StartsWith("DECLIN", StringComparison.OrdinalIgnoreCase) || line.Outcome.StartsWith("REJECT", StringComparison.OrdinalIgnoreCase);
            if (declined)
            {
                claim.Status = ClaimStatus.Rejected; claim.ResponseAt = rem.ImportedAt; claim.ResponseText = line.Reason ?? "Declined on remittance";
                line.Result = "Declined – claim marked rejected"; rem.Matched++; rem.Lines.Add(line); continue;
            }
            if (claim.Status is ClaimStatus.Paid or ClaimStatus.WrittenOff) { line.Result = "Claim already closed – check for a duplicate payment"; rem.Lines.Add(line); continue; }
            var err = await billing.RecordPaymentAsync(claim.InvoiceId, new NewPayment(rem.ImportedAt.NzDate(), line.Amount, f.Kind == FunderKind.Acc ? PaymentMethod.Acc : PaymentMethod.Insurer, $"{claim.Number} remit"), user, ct);
            if (err is not null) { line.Result = Trunc(err, 200); rem.Lines.Add(line); continue; }
            claim.Paid += line.Amount;
            claim.Status = claim.Paid >= claim.Amount ? ClaimStatus.Paid : ClaimStatus.PartPaid;
            claim.ResponseAt = rem.ImportedAt; claim.ResponseText = line.Reason ?? claim.ResponseText;
            line.Result = claim.Status == ClaimStatus.Paid ? "Paid in full" : "Part paid – balance stays on the invoice";
            rem.Matched++; rem.Total += line.Amount; rem.Lines.Add(line);
        }
        rem.LineCount = rem.Lines.Count;
        db.Remittances.Add(rem);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Import", "Remittance", rem.Id.ToString(), reason: $"{f.Code} {rem.FileName} {rem.Matched}/{rem.LineCount} matched {rem.Total:0.00}", ct: ct);
        return new(rem, []);
    }

    public Task<List<Remittance>> RemittancesAsync(CancellationToken ct = default) =>
        db.Remittances.AsNoTracking().OrderByDescending(r => r.ImportedAt).Take(200).ToListAsync(ct);

    public Task<Remittance?> RemittanceAsync(Guid id, CancellationToken ct = default) =>
        db.Remittances.AsNoTracking().Include(r => r.Lines.OrderBy(l => l.Sequence)).SingleOrDefaultAsync(r => r.Id == id, ct);

    public Task<List<ClaimBatch>> BatchesAsync(CancellationToken ct = default) =>
        db.Batches.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(200).ToListAsync(ct);

    /// <summary>Claims still open, by age band (spec 7.12 aged claims).</summary>
    public async Task<List<(string Funder, int Open, decimal Owing, int Over30, int Over60)>> AgedAsync(CancellationToken ct = default)
    {
        var open = await db.Claims.AsNoTracking().Where(c => c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.Accepted || c.Status == ClaimStatus.PartPaid || c.Status == ClaimStatus.Rejected).ToListAsync(ct);
        var rows = await RowsAsync(open, ct);
        return [.. rows.GroupBy(r => r.Funder).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, g.Count(), g.Sum(r => r.Claim.Amount - r.Claim.Paid), g.Count(r => r.AgeDays > 30), g.Count(r => r.AgeDays > 60)))];
    }

    // ---------- helpers ----------
    private static List<string> IdentifierErrors(Funder? f, string? member, string? acc)
    {
        var e = new List<string>();
        if (f?.Kind == FunderKind.Acc && !string.IsNullOrWhiteSpace(acc) && !AccClaimNumberRx().IsMatch(acc.Trim().ToUpperInvariant()))
            e.Add("The ACC claim number should be 6–12 letters and numbers.");
        if (!string.IsNullOrWhiteSpace(member) && member.Trim().Length > 50) e.Add("The membership number is too long.");
        return e;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;
    private static string? TruncN(string? s, int n) => s is null ? null : s.Length > n ? s[..n] : s;
}
