using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Estimates;

/// <summary>Renders the printed estimate (implemented in the web project with the embedded fonts; the tests use a stand-in).</summary>
public interface IEstimatePdf
{
    byte[] Render(Estimate e, EstSettings s, string? password);
}

/// <summary>What staff fill in on the estimate builder.</summary>
public sealed class EstimateForm
{
    public Guid? PatientId { get; set; }
    public DateOnly? EstimateDate { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly? DateOfBirth { get; set; }
    public string? Nhi { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Town { get; set; }
    public string? Postcode { get; set; }
    public Funding Funding { get; set; } = Funding.SelfFunding;
    public string? Insurer { get; set; }
    public string? PolicyNumber { get; set; }
    public string? PriorApproval { get; set; }
    public Guid? ProcedureId { get; set; }
    public Guid? SurgeonId { get; set; }
    public string? SurgeonName { get; set; }
    public DateOnly? ProcedureDate { get; set; }
    public Anaesthetic Anaesthetic { get; set; } = Anaesthetic.General;
    public int? PlannedMinutes { get; set; }
    public bool UseSpecial { get; set; }
    public decimal Multiplier { get; set; } = 1m;
    public decimal? FacilityOverride { get; set; }
    public decimal? SurgeonOverride { get; set; }
    public decimal? AnaesthetistOverride { get; set; }
    public EstimateInputs Inputs { get; set; } = new();
    public string? Notes { get; set; }
    public string? PreparedBy { get; set; }
}

public sealed record EstimateListItem(Guid Id, string Ref, DateOnly EstimateDate, EstimateStatus Status, string Patient, string? Nhi, string? Procedure, string? Surgeon,
    decimal Subtotal, Funding Funding, DateTimeOffset? EmailedAt, DateTimeOffset? SentToIncisiveAt, Guid? PatientId, EstFollowUp? FollowUp = null);

public sealed record SaveResult(Estimate? Estimate, IReadOnlyList<string> Errors, string? ArchiveMessage, int StoppedEmails);

/// <summary>Builds, saves, issues, duplicates and cancels estimates; templates; archive copies; the Incisive export.</summary>
public sealed class EstimateService(EstDbContext db, PatientsDbContext patients, IEstimatePdf pdf, IDocumentCipher cipher, AuditWriter audit, TimeProvider clock)
{
    // ------------------------------------------------------------------ settings
    public async Task<EstSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new EstSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    // ------------------------------------------------------------------ calculation
    public async Task<CalcContext> ContextAsync(Guid? procedureId, CancellationToken ct = default)
    {
        var ctx = new CalcContext();
        if (procedureId is null) return ctx;
        ctx.Procedure = await db.Procedures.AsNoTracking().SingleOrDefaultAsync(p => p.Id == procedureId, ct);
        if (ctx.Procedure is null) return ctx;
        ctx.Fees = await db.SurgeonFees.AsNoTracking().Where(f => f.ProcedureId == procedureId).ToListAsync(ct);
        ctx.AddOns = await db.AddOns.AsNoTracking().Where(a => a.SpecialtyId == ctx.Procedure.SpecialtyId).ToListAsync(ct);
        var costing = await (from c in db.Consumables.AsNoTracking()
                             where c.ProcedureId == procedureId
                             join st in db.Stock.AsNoTracking() on c.StockItemId equals st.Id into sj
                             from st in sj.DefaultIfEmpty()
                             select new { c.Quantity, Unit = st != null ? st.UnitPrice : c.UnitCost }).ToListAsync(ct);
        ctx.Costing = [.. costing.Select(c => (c.Quantity, c.Unit))];
        return ctx;
    }

    private static void Apply(Estimate e, EstimateForm f)
    {
        e.PatientId = f.PatientId;
        e.FirstName = f.FirstName.Trim(); e.LastName = f.LastName.Trim();
        e.DateOfBirth = f.DateOfBirth; e.Nhi = Blank(f.Nhi)?.ToUpperInvariant();
        e.Phone = Blank(f.Phone); e.Email = Blank(f.Email)?.ToLowerInvariant(); e.Address = Blank(f.Address); e.Town = Blank(f.Town); e.Postcode = Blank(f.Postcode);
        e.Funding = f.Funding; e.Insurer = Blank(f.Insurer); e.PolicyNumber = Blank(f.PolicyNumber); e.PriorApproval = Blank(f.PriorApproval);
        e.ProcedureId = f.ProcedureId; e.SurgeonId = f.SurgeonId; e.SurgeonName = Blank(f.SurgeonName); e.ProcedureDate = f.ProcedureDate;
        e.Anaesthetic = f.Anaesthetic; e.PlannedMinutes = f.PlannedMinutes is > 0 ? f.PlannedMinutes : null; e.UseSpecial = f.UseSpecial;
        e.Multiplier = f.Multiplier is 1m or 2m ? f.Multiplier : 1m;
        e.FacilityOverride = f.FacilityOverride is > 0 ? f.FacilityOverride : null;
        e.SurgeonOverride = f.SurgeonOverride is > 0 ? f.SurgeonOverride : null;
        e.AnaesthetistOverride = f.AnaesthetistOverride is > 0 ? f.AnaesthetistOverride : null;
        e.Notes = Blank(f.Notes); e.PreparedBy = Blank(f.PreparedBy);
        if (f.EstimateDate is not null) e.EstimateDate = f.EstimateDate.Value;
    }

    public static EstimateForm FormOf(Estimate e) => new()
    {
        PatientId = e.PatientId,
        EstimateDate = e.EstimateDate,
        FirstName = e.FirstName,
        LastName = e.LastName,
        DateOfBirth = e.DateOfBirth,
        Nhi = e.Nhi,
        Phone = e.Phone,
        Email = e.Email,
        Address = e.Address,
        Town = e.Town,
        Postcode = e.Postcode,
        Funding = e.Funding,
        Insurer = e.Insurer,
        PolicyNumber = e.PolicyNumber,
        PriorApproval = e.PriorApproval,
        ProcedureId = e.ProcedureId,
        SurgeonId = e.SurgeonId,
        SurgeonName = e.SurgeonName,
        ProcedureDate = e.ProcedureDate,
        Anaesthetic = e.Anaesthetic,
        PlannedMinutes = e.PlannedMinutes,
        UseSpecial = e.UseSpecial,
        Multiplier = e.Multiplier,
        FacilityOverride = e.FacilityOverride,
        SurgeonOverride = e.SurgeonOverride,
        AnaesthetistOverride = e.AnaesthetistOverride,
        Inputs = EstimateInputs.Read(e.InputsJson),
        Notes = e.Notes,
        PreparedBy = e.PreparedBy
    };

    /// <summary>Live totals and warnings while staff type (nothing is saved).</summary>
    public async Task<(CalcResult Result, List<EstAddOn> AvailableAddOns, EstProcedure? Procedure)> PreviewAsync(EstimateForm f, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var ctx = await ContextAsync(f.ProcedureId, ct);
        var e = new Estimate { EstimateDate = f.EstimateDate ?? NzTime.Today(clock) };
        Apply(e, f);
        return (EstimateCalculator.Calculate(e, f.Inputs, ctx, s), EstimateCalculator.AvailableAddOns(ctx, f.SurgeonId), ctx.Procedure);
    }

    // ------------------------------------------------------------------ reference
    /// <summary>EST-yymmdd-NHI, or the last name in capitals (letters and digits only) when there is no NHI; -2, -3 … if needed.</summary>
    public static string BaseRef(DateOnly date, string? nhi, string? lastName)
    {
        var who = !string.IsNullOrWhiteSpace(nhi) ? nhi.Trim().ToUpperInvariant()
            : new string([.. (lastName ?? "").Where(char.IsLetterOrDigit)]).ToUpperInvariant();
        if (who.Length == 0) who = "PATIENT";
        if (who.Length > 24) who = who[..24];
        return $"EST-{date:yyMMdd}-{who}";
    }

    private async Task<string> MakeRefAsync(Estimate e, CancellationToken ct)
    {
        var baseRef = BaseRef(e.EstimateDate, e.Nhi, e.LastName);
        var taken = await db.Estimates.AsNoTracking().Where(x => x.Id != e.Id && (x.Ref == baseRef || x.Ref.StartsWith(baseRef + "-"))).Select(x => x.Ref).ToListAsync(ct);
        var r = baseRef;
        for (var n = 2; taken.Contains(r, StringComparer.Ordinal); n++) r = $"{baseRef}-{n}";
        return r;
    }

    private static string Signature(Estimate e) =>
        $"{e.FirstName}|{e.LastName}|{e.DateOfBirth}|{e.Nhi}|{e.Email}|{e.Address}|{e.Town}|{e.Funding}|{e.Insurer}|{e.PolicyNumber}|{e.SurgeonName}|{e.ProcedureDate}|{e.Title}|{e.Notes}|{e.Subtotal}|" +
        string.Join(";", e.Lines.OrderBy(l => l.Sequence).Select(l => $"{l.Label}={l.Amount}{l.DisplayText}"));

    // ------------------------------------------------------------------ save / issue
    public async Task<SaveResult> SaveAsync(Guid? id, EstimateForm f, bool issue, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (f.Multiplier is not (1m or 2m)) errors.Add("The multiplier must be 100% or 200%.");
        if (f.PlannedMinutes is < 0 or > 1440) errors.Add("Check the planned minutes.");
        if (f.FacilityOverride is < 0 || f.SurgeonOverride is < 0 || f.AnaesthetistOverride is < 0) errors.Add("Manual fees cannot be negative.");
        if (f.Inputs.AddOns.Count > 40 || f.Inputs.OtherCharges.Count > 10 || f.Inputs.IfRequired.Count > 20) errors.Add("Too many lines on one estimate.");
        if (issue && f.ProcedureId is null) errors.Add("Choose a procedure before issuing.");
        if (issue && (string.IsNullOrWhiteSpace(f.FirstName) || string.IsNullOrWhiteSpace(f.LastName))) errors.Add("Enter the patient's first and last name before issuing.");
        if (string.IsNullOrWhiteSpace(f.LastName) && string.IsNullOrWhiteSpace(f.Nhi) && f.PatientId is null) errors.Add("Enter at least the patient's last name or NHI.");
        if (f.PatientId is not null && !await patients.Patients.AnyAsync(p => p.Id == f.PatientId, ct)) errors.Add("Patient not found.");
        if (f.ProcedureId is not null && !await db.Procedures.AnyAsync(p => p.Id == f.ProcedureId, ct)) errors.Add("Procedure not found.");
        if (errors.Count > 0) return new(null, errors, null, 0);

        var s = await SettingsAsync(ct);
        await ApplyDuePricesAsync(user, ct);
        var ctx = await ContextAsync(f.ProcedureId, ct);
        var now = clock.GetUtcNow();
        Estimate e;
        string? before = null;
        var wasIssued = false;
        if (id is null)
        {
            e = new Estimate { CreatedBy = user, CreatedAt = now, EstimateDate = f.EstimateDate ?? NzTime.Today(clock) };
            Apply(e, f);
            db.Estimates.Add(e);
        }
        else
        {
            e = await db.Estimates.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Estimate not found.");
            if (e.Status == EstimateStatus.Cancelled) return new(null, ["This estimate has been cancelled. Duplicate it to make a new one."], null, 0);
            before = Signature(e);
            wasIssued = e.Status == EstimateStatus.Issued;
            Apply(e, f);
            db.Lines.RemoveRange(e.Lines);
            e.Lines.Clear();
        }
        var res = EstimateCalculator.Calculate(e, f.Inputs, ctx, s);
        if (ctx.Procedure is not null)
        {
            e.ProcedureCode = ctx.Procedure.Code; e.ProcedureName = ctx.Procedure.Name;
            e.SpecialtyName = await db.Specialties.Where(x => x.Id == ctx.Procedure.SpecialtyId).Select(x => x.Name).SingleOrDefaultAsync(ct);
        }
        else { e.ProcedureCode = null; e.ProcedureName = null; e.SpecialtyName = null; }
        if (e.SurgeonId is not null) e.SurgeonName = await db.Surgeons.Where(x => x.Id == e.SurgeonId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? e.SurgeonName;
        e.Title = res.Title;
        e.InputsJson = f.Inputs.Write();
        (e.Subtotal, e.Deposit, e.Balance, e.ValidUntil) = (res.Subtotal, res.Deposit, res.Balance, res.ValidUntil);
        e.UpdatedBy = user; e.UpdatedAt = now;
        if (string.IsNullOrWhiteSpace(e.Ref) || e.Status == EstimateStatus.Draft) e.Ref = await MakeRefAsync(e, ct);
        if (issue && e.Status == EstimateStatus.Draft)
        {
            e.Status = EstimateStatus.Issued; e.IssuedAt = now;
            (e.FollowUp, e.FollowUpAt, e.FollowUpBy) = (EstFollowUp.Waiting, now, user);
            db.Events.Add(new EstimateEvent { EstimateId = e.Id, Status = EstFollowUp.Waiting, At = now, By = user, Note = "Issued" });
        }
        var seq = 0;
        foreach (var l in res.Lines) e.Lines.Add(new EstimateLine { EstimateId = e.Id, Kind = LineKind.Fee, Sequence = seq++, Label = l.Label, Amount = l.Amount, DisplayText = l.Display, Note = l.Note });
        foreach (var l in res.Additional) e.Lines.Add(new EstimateLine { EstimateId = e.Id, Kind = LineKind.IfRequired, Sequence = seq++, Label = l.Label, Amount = l.Amount, Note = l.Note });
        foreach (var l in e.Lines) db.Lines.Add(l);

        // An estimate that changes while an email is held must not go out with the old PDF.
        var held = id is null ? [] : await db.Outbox.Where(o => o.EstimateId == e.Id && o.Status == OutboxStatus.Held).ToListAsync(ct);
        foreach (var o in held) { o.Status = OutboxStatus.Stopped; o.StoppedAt = now; o.StoppedBy = user + " (estimate changed)"; }

        // Archive a PDF when issued, and again whenever an issued estimate actually changes ("_updated"); never overwritten.
        string? archiveMessage = null;
        var changed = before is not null && before != Signature(e);
        var newlyIssued = e.Status == EstimateStatus.Issued && !wasIssued;
        if (s.ArchiveIssued && e.Status == EstimateStatus.Issued && (newlyIssued || changed))
        {
            var name = PdfFileName(e, newlyIssued ? null : "updated_" + now.ToNz().ToString("yyyyMMdd-HHmm"));
            db.Archive.Add(new EstimateArchive { EstimateId = e.Id, CreatedAt = now, CreatedBy = user, Reason = newlyIssued ? "Issued" : "Changed after issue", FileName = name, PdfCipher = cipher.Protect(pdf.Render(e, s, null)) });
            archiveMessage = "PDF copy kept: " + name;
        }
        await db.SaveChangesAsync(ct);
        if (held.Count > 0) await audit.WriteAsync(user, "EmailStopped", "Estimate", e.Id.ToString(), e.PatientId?.ToString(), reason: $"{e.Ref}: held email stopped – estimate changed ({string.Join(", ", held.Select(h => h.ToAddress))})", ct: ct);
        await audit.WriteAsync(user, issue && newlyIssued ? "Issue" : id is null ? "Create" : "Update", "Estimate", e.Id.ToString(), e.PatientId?.ToString(),
            reason: $"{e.Ref} {e.ProcedureCode} {e.Subtotal:0.00}", ct: ct);
        return new(e, [], archiveMessage, held.Count);
    }

    public async Task<Estimate?> GetAsync(Guid id, string user, string? workstation, CancellationToken ct = default)
    {
        var e = await db.Estimates.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        e.Lines = [.. e.Lines.OrderBy(l => l.Sequence)];
        await audit.WriteAsync(user, "View", "Estimate", e.Id.ToString(), e.PatientId?.ToString(), workstation: workstation, ct: ct);
        return e;
    }

    public async Task<List<EstimateListItem>> ListAsync(string? search, EstimateStatus? status, Guid? surgeonId, DateOnly? from, DateOnly? to, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Estimates.AsNoTracking();
        if (status is not null) q = q.Where(e => e.Status == status);
        if (surgeonId is not null) q = q.Where(e => e.SurgeonId == surgeonId);
        if (from is not null) q = q.Where(e => e.EstimateDate >= from);
        if (to is not null) q = q.Where(e => e.EstimateDate <= to);
        if (patientId is not null) q = q.Where(e => e.PatientId == patientId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim();
            q = q.Where(e => e.Ref.Contains(t) || e.LastName.Contains(t) || e.FirstName.Contains(t) || (e.Nhi != null && e.Nhi.Contains(t)) ||
                             (e.ProcedureName != null && e.ProcedureName.Contains(t)) || (e.Email != null && e.Email.Contains(t)));
        }
        return await q.OrderByDescending(e => e.EstimateDate).ThenByDescending(e => e.CreatedAt).Take(300)
            .Select(e => new EstimateListItem(e.Id, e.Ref, e.EstimateDate, e.Status, e.LastName + ", " + e.FirstName, e.Nhi, e.ProcedureName, e.SurgeonName,
                e.Subtotal, e.Funding, e.EmailedAt, e.SentToIncisiveAt, e.PatientId, e.FollowUp))
            .ToListAsync(ct);
    }

    /// <summary>Starts a new estimate from a BrightPAS patient record (name, DOB, NHI, contact and address).</summary>
    public async Task<EstimateForm> FromPatientAsync(Guid patientId, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct) ?? throw new KeyNotFoundException("Patient not found.");
        var f = new EstimateForm
        {
            PatientId = p.Id,
            FirstName = p.GivenNames,
            LastName = p.FamilyName,
            DateOfBirth = p.DateOfBirth,
            Nhi = p.Nhi,
            Phone = p.MobilePhone ?? p.HomePhone,
            Email = p.Email,
            Address = string.Join(", ", new[] { p.AddressLine1, p.AddressLine2, p.Suburb }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Town = p.City,
            Postcode = p.Postcode,
            EstimateDate = NzTime.Today(clock)
        };
        // Returning patient: carry over funding and insurer from their latest estimate.
        var last = await db.Estimates.AsNoTracking().Where(e => e.PatientId == patientId || (p.Nhi != null && e.Nhi == p.Nhi))
            .OrderByDescending(e => e.CreatedAt).Select(e => new { e.Funding, e.Insurer, e.PolicyNumber }).FirstOrDefaultAsync(ct);
        if (last is not null) (f.Funding, f.Insurer, f.PolicyNumber) = (last.Funding, last.Insurer, last.PolicyNumber);
        return f;
    }

    public async Task<SaveResult> DuplicateAsync(Guid id, string user, CancellationToken ct = default)
    {
        var src = await db.Estimates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Estimate not found.");
        var f = FormOf(src);
        f.EstimateDate = NzTime.Today(clock);
        return await SaveAsync(null, f, false, user, ct);
    }

    public async Task<string?> CancelAsync(Guid id, string? reason, string user, CancellationToken ct = default)
    {
        var e = await db.Estimates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "Estimate not found.";
        if (e.Status == EstimateStatus.Cancelled) return "Already cancelled.";
        if (string.IsNullOrWhiteSpace(reason)) return "Enter the reason.";
        e.Status = EstimateStatus.Cancelled; e.CancelReason = reason.Trim(); e.UpdatedAt = clock.GetUtcNow(); e.UpdatedBy = user;
        var now = clock.GetUtcNow();
        foreach (var o in await db.Outbox.Where(o => o.EstimateId == id && o.Status == OutboxStatus.Held).ToListAsync(ct)) { o.Status = OutboxStatus.Stopped; o.StoppedAt = now; o.StoppedBy = user + " (estimate cancelled)"; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "Estimate", e.Id.ToString(), e.PatientId?.ToString(), reason: $"{e.Ref}: {reason}", ct: ct);
        return null;
    }

    /// <summary>Links an estimate made before the patient was registered to their BrightPAS record.</summary>
    public async Task<string?> LinkPatientAsync(Guid id, Guid patientId, string user, CancellationToken ct = default)
    {
        var e = await db.Estimates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "Estimate not found.";
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return "Patient not found.";
        if (!string.IsNullOrWhiteSpace(e.Nhi) && !string.IsNullOrWhiteSpace(p.Nhi) && !string.Equals(e.Nhi, p.Nhi, StringComparison.OrdinalIgnoreCase))
            return $"The estimate's NHI ({e.Nhi}) is not this patient's NHI ({p.Nhi}).";
        e.PatientId = patientId;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "LinkPatient", "Estimate", e.Id.ToString(), patientId.ToString(), reason: e.Ref, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ PDF, archive, Incisive
    public static string PdfFileName(Estimate e, string? suffix = null) =>
        Safe($"Estimate_{e.Ref}_{e.LastName}_{e.FirstName}{(suffix is null ? "" : "_" + suffix)}") + ".pdf";

    public static string IncisiveFileName(Estimate e, EstSettings s) =>
        Safe(s.IncisiveFileName.Replace("{NHI}", e.Nhi ?? "NONHI", StringComparison.Ordinal).Replace("{LAST}", e.LastName.ToUpperInvariant(), StringComparison.Ordinal)
            .Replace("{FIRST}", e.FirstName, StringComparison.Ordinal).Replace("{DATE}", e.EstimateDate.ToString("yyyyMMdd"), StringComparison.Ordinal)
            .Replace("{REF}", e.Ref, StringComparison.Ordinal).Replace("{CODE}", e.ProcedureCode ?? "", StringComparison.Ordinal)) + ".pdf";

    private static string Safe(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_' ? c : '_').ToArray();
        var r = new string(chars);
        while (r.Contains("__", StringComparison.Ordinal)) r = r.Replace("__", "_", StringComparison.Ordinal);
        return r.Trim('_');
    }

    /// <summary>The estimate without writing a "view" audit event (for internal steps such as building a PDF or email).</summary>
    public async Task<Estimate> FindAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.Estimates.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Estimate not found.");
        e.Lines = [.. e.Lines.OrderBy(l => l.Sequence)];
        return e;
    }

    public async Task<(byte[] Pdf, string Name)> PdfAsync(Guid id, string? password, CancellationToken ct = default)
    {
        var e = await FindAsync(id, ct);
        return (pdf.Render(e, await SettingsAsync(ct), password), PdfFileName(e));
    }

    public byte[] Pdf(Estimate e, EstSettings s, string? password) => pdf.Render(e, s, password);

    public async Task<List<EstimateArchive>> ArchiveListAsync(Guid estimateId, CancellationToken ct = default) =>
        await db.Archive.AsNoTracking().Where(a => a.EstimateId == estimateId).OrderBy(a => a.CreatedAt)
            .Select(a => new EstimateArchive { Id = a.Id, EstimateId = a.EstimateId, CreatedAt = a.CreatedAt, CreatedBy = a.CreatedBy, Reason = a.Reason, FileName = a.FileName }).ToListAsync(ct);

    public async Task<(byte[] Pdf, string Name, Guid? PatientId)> ArchivedPdfAsync(Guid archiveId, CancellationToken ct = default)
    {
        var a = await db.Archive.AsNoTracking().SingleOrDefaultAsync(x => x.Id == archiveId, ct) ?? throw new KeyNotFoundException("Archived PDF not found.");
        var pid = await db.Estimates.Where(e => e.Id == a.EstimateId).Select(e => e.PatientId).SingleOrDefaultAsync(ct);
        return (cipher.Unprotect(a.PdfCipher), a.FileName, pid);
    }

    /// <summary>Saves the PDF into the Incisive scans folder (a share on the server). Returns the file path.</summary>
    public async Task<string> SendToIncisiveAsync(Guid id, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (string.IsNullOrWhiteSpace(s.IncisiveFolder)) throw new InvalidOperationException("Set the Incisive scans folder in Estimates › Admin › Settings & wording.");
        var e = await db.Estimates.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Estimate not found.");
        if (e.Status != EstimateStatus.Issued) throw new InvalidOperationException("Only issued estimates can be saved to Incisive – click ‘Save & issue’ first.");
        e.Lines = [.. e.Lines.OrderBy(l => l.Sequence)];
        var folder = Path.GetFullPath(s.IncisiveFolder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, IncisiveFileName(e, s));
        if (!Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The Incisive file name pattern is not valid.");
        await File.WriteAllBytesAsync(path, pdf.Render(e, s, null), ct);
        e.SentToIncisiveAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Export", "Estimate", e.Id.ToString(), e.PatientId?.ToString(), reason: $"{e.Ref} saved to the Incisive folder: {Path.GetFileName(path)}", ct: ct);
        return path;
    }

    // ------------------------------------------------------------------ scheduled price changes
    /// <summary>Applies imported price changes whose date has come. Runs before each save and nightly.</summary>
    public async Task<int> ApplyDuePricesAsync(string user, CancellationToken ct = default)
    {
        var today = NzTime.Today(clock);
        var due = await db.PendingPrices.Where(x => x.AppliedAt == null && x.EffectiveFrom <= today).OrderBy(x => x.EffectiveFrom).ToListAsync(ct);
        if (due.Count == 0) return 0;
        var ids = due.Select(d => d.ProcedureId).Distinct().ToList();
        var procs = await db.Procedures.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var now = clock.GetUtcNow();
        foreach (var d in due)
        {
            if (procs.TryGetValue(d.ProcedureId, out var p))
            {
                switch (d.Field)
                {
                    case "Facility": p.Facility = d.NewValue; break;
                    case "FacilitySpecial": p.FacilitySpecial = d.NewValue; break;
                    case "Anaesthetist": p.Anaesthetist = d.NewValue; break;
                    case "AnaesthetistSpecial": p.AnaesthetistSpecial = d.NewValue; break;
                    case "ConsumablesLine": p.ConsumablesLine = d.NewValue; break;
                }
                p.EffectiveFrom = d.EffectiveFrom;
            }
            d.AppliedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "PriceChange", "EstProcedure", string.Join(",", ids.Take(5)), reason: $"{due.Count} scheduled price change(s) applied", ct: ct);
        return due.Count;
    }

    // ------------------------------------------------------------------ templates
    public sealed record TemplateItem(Guid Id, string Name, string? Description, Guid ProcedureId, string ProcedureCode, string ProcedureName, string Specialty, bool Favourite,
        bool Visible, int SortOrder, int UsedCount, IReadOnlyList<Guid> SurgeonIds, bool ProcedureActive);

    public async Task<List<TemplateItem>> TemplatesAsync(bool forStaff, Guid? surgeonId, CancellationToken ct = default)
    {
        var rows = await (from t in db.Templates.AsNoTracking()
                          join p in db.Procedures.AsNoTracking() on t.ProcedureId equals p.Id
                          join sp in db.Specialties.AsNoTracking() on p.SpecialtyId equals sp.Id
                          select new { t, p.Code, p.Name, Spec = sp.Name, p.IsActive }).ToListAsync(ct);
        var links = await db.TemplateSurgeons.AsNoTracking().ToListAsync(ct);
        var list = rows.Select(r => new TemplateItem(r.t.Id, r.t.Name, r.t.Description, r.t.ProcedureId, r.Code, r.Name, r.Spec, r.t.Favourite, r.t.Visible, r.t.SortOrder, r.t.UsedCount,
            [.. links.Where(l => l.TemplateId == r.t.Id).Select(l => l.SurgeonId)], r.IsActive));
        if (forStaff) list = list.Where(t => t.Visible && t.ProcedureActive && (surgeonId is null || t.SurgeonIds.Count == 0 || t.SurgeonIds.Contains(surgeonId.Value)));
        return [.. list.OrderByDescending(t => t.Favourite).ThenBy(t => t.Specialty, StringComparer.Ordinal).ThenBy(t => t.SortOrder).ThenBy(t => t.Name, StringComparer.Ordinal)];
    }

    /// <summary>Copies a template onto the form (patient details are left alone) and counts the use.</summary>
    public async Task<EstimateForm> ApplyTemplateAsync(Guid templateId, EstimateForm f, CancellationToken ct = default)
    {
        var t = await db.Templates.SingleOrDefaultAsync(x => x.Id == templateId, ct) ?? throw new KeyNotFoundException("Template not found.");
        var p = await db.Procedures.AsNoTracking().SingleAsync(x => x.Id == t.ProcedureId, ct);
        f.ProcedureId = t.ProcedureId;
        if (t.SurgeonId is not null) f.SurgeonId = t.SurgeonId;
        f.Anaesthetic = t.Anaesthetic ?? p.DefaultAnaesthetic;
        f.PlannedMinutes = t.PlannedMinutes; f.UseSpecial = t.UseSpecial;
        (f.FacilityOverride, f.SurgeonOverride, f.AnaesthetistOverride) = (t.FacilityOverride, t.SurgeonOverride, t.AnaesthetistOverride);
        if (!string.IsNullOrWhiteSpace(t.Notes)) f.Notes = t.Notes;
        f.Inputs = EstimateInputs.Read(t.InputsJson);
        t.UsedCount++; t.LastUsedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task<(Guid? Id, string? Error)> SaveAsTemplateAsync(Guid? templateId, string? name, string? description, EstimateForm f, string user, CancellationToken ct = default)
    {
        if (f.ProcedureId is null) return (null, "Choose a procedure first.");
        EstTemplate t;
        if (templateId is null)
        {
            if (string.IsNullOrWhiteSpace(name)) return (null, "Give the template a name.");
            t = new EstTemplate { Name = name.Trim(), Description = Blank(description), CreatedBy = user, CreatedAt = clock.GetUtcNow(), SortOrder = (await db.Templates.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 10 };
            db.Templates.Add(t);
        }
        else
        {
            t = await db.Templates.SingleOrDefaultAsync(x => x.Id == templateId, ct) ?? throw new KeyNotFoundException("Template not found.");
            (t.UpdatedBy, t.UpdatedAt) = (user, clock.GetUtcNow());
        }
        t.ProcedureId = f.ProcedureId.Value; t.SurgeonId = f.SurgeonId; t.Anaesthetic = f.Anaesthetic; t.PlannedMinutes = f.PlannedMinutes; t.UseSpecial = f.UseSpecial;
        (t.FacilityOverride, t.SurgeonOverride, t.AnaesthetistOverride) = (f.FacilityOverride, f.SurgeonOverride, f.AnaesthetistOverride);
        t.InputsJson = f.Inputs.Write(); t.Notes = Blank(f.Notes);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, templateId is null ? "Create" : "Update", "EstTemplate", t.Id.ToString(), reason: t.Name, ct: ct);
        return (t.Id, null);
    }

    public sealed record TemplateProps(string Name, string? Description, bool Visible, bool Favourite, int SortOrder, IReadOnlyList<Guid>? SurgeonIds);

    public async Task<string?> UpdateTemplateAsync(Guid id, TemplateProps p, string user, CancellationToken ct = default)
    {
        var t = await db.Templates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return "Template not found.";
        if (string.IsNullOrWhiteSpace(p.Name)) return "Give the template a name.";
        (t.Name, t.Description, t.Visible, t.Favourite, t.SortOrder, t.UpdatedBy, t.UpdatedAt) = (p.Name.Trim(), Blank(p.Description), p.Visible, p.Favourite, p.SortOrder, user, clock.GetUtcNow());
        db.TemplateSurgeons.RemoveRange(await db.TemplateSurgeons.Where(x => x.TemplateId == id).ToListAsync(ct));
        foreach (var sid in (p.SurgeonIds ?? []).Distinct())
            if (await db.Surgeons.AnyAsync(x => x.Id == sid, ct)) db.TemplateSurgeons.Add(new EstTemplateSurgeon { TemplateId = id, SurgeonId = sid });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "EstTemplate", id.ToString(), reason: t.Name, ct: ct);
        return null;
    }

    /// <summary>Safe delete: a template that has been used can only be hidden.</summary>
    public async Task<string?> DeleteTemplateAsync(Guid id, string user, CancellationToken ct = default)
    {
        var t = await db.Templates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return "Template not found.";
        if (t.UsedCount > 0) return $"This template has been used {t.UsedCount} time(s) – hide it instead.";
        db.Templates.Remove(t);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "EstTemplate", id.ToString(), reason: t.Name, ct: ct);
        return null;
    }

    /// <summary>The seven starter templates, added once (not again if they are deleted).</summary>
    public async Task SeedTemplatesOnceAsync(string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s.TemplatesSeeded) return;
        var procs = await db.Procedures.AsNoTracking().ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);
        if (procs.Count == 0) return;
        var addOns = await db.AddOns.AsNoTracking().ToDictionaryAsync(a => a.Code, StringComparer.OrdinalIgnoreCase, ct);
        var starters = new (string Name, string Code, string[] Required, string[] IfReq)[]
        {
            ("Colonoscopy + biopsies (polypectomy if required)", "END-001", ["END-A01"], ["END-A02", "END-A03"]),
            ("Colonoscopy & gastroscopy + biopsies", "END-005", ["END-A01"], ["END-A02"]),
            ("Gastroscopy + biopsies", "END-003", ["END-A10"], ["END-A11"]),
            ("Hysteroscopy + sharp resection", "GYN-001", ["GYN-A01"], []),
            ("Diagnostic laparoscopy – endometriosis (stage I–II)", "GYN-008", ["GYN-A10"], ["GYN-A13"]),
            ("Open inguinal hernia repair (mesh if required)", "GEN-011", [], ["GEN-A05"]),
            ("Removal of 4 wisdom teeth", "DEN-004", [], []),
        };
        var order = 10;
        foreach (var st in starters)
        {
            if (!procs.TryGetValue(st.Code, out var p)) continue;
            var inp = new EstimateInputs();
            foreach (var c in st.Required.Where(addOns.ContainsKey)) inp.AddOns.Add(new AddOnChoice { Code = c, Name = addOns[c].Name });
            foreach (var c in st.IfReq.Where(addOns.ContainsKey)) inp.AddOns.Add(new AddOnChoice { Code = c, Name = addOns[c].Name, IfRequired = true });
            db.Templates.Add(new EstTemplate
            {
                Name = st.Name,
                Description = "Starter template – edit, hide or delete as you like.",
                ProcedureId = p.Id,
                Anaesthetic = p.DefaultAnaesthetic,
                InputsJson = inp.Write(),
                SortOrder = order,
                CreatedBy = user + " (starter)",
                CreatedAt = clock.GetUtcNow()
            });
            order += 10;
        }
        s.TemplatesSeeded = true;
        await db.SaveChangesAsync(ct);
    }

    internal static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
