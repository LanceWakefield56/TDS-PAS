using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Claims;

public sealed record FunderLine(string Code, string Description, decimal Amount);
public sealed record FunderClaim(string? ProviderNumber, string ClaimNumber, string InvoiceNumber, DateOnly ServiceDate, string? Nhi, string FamilyName, string GivenNames, DateOnly? DateOfBirth,
    string? MemberNumber, string? AccClaimNumber, string? ApprovalReference, decimal Amount, IReadOnlyList<FunderLine> Lines);
public sealed record FunderApproval(string? ProviderNumber, string Number, string? Nhi, string FamilyName, string GivenNames, DateOnly? DateOfBirth, string? MemberNumber, string? AccClaimNumber,
    string Procedure, decimal Amount);
public sealed record FunderSent(bool Ok, string? Reference, string? Error);
/// <summary>What the funder says now. Status: Pending, Accepted, Rejected, Paid, PartPaid (claims); Pending, Approved, PartApproved, Declined (approvals).</summary>
public sealed record FunderReply(bool Ok, string Status, decimal? Amount, string? Reference, DateOnly? ValidUntil, string? Text, string? Error);
public sealed record FunderApiEdit(FunderApi Api, string? ApiUrl, string? ApiKey);
public sealed record DirectRun(int Checked, int Changed, int Paid, IReadOnlyList<string> Errors);

/// <summary>A funder's claiming API (0.34.0). The web project has a FHIR R4 Claim/ClaimResponse client; tests fake it.</summary>
public interface IFunderApi
{
    Task<FunderSent> SubmitClaimAsync(string url, string key, FunderClaim claim, CancellationToken ct);
    Task<FunderSent> RequestApprovalAsync(string url, string key, FunderApproval approval, CancellationToken ct);
    Task<FunderReply> ClaimStatusAsync(string url, string key, string reference, CancellationToken ct);
    Task<FunderReply> ApprovalStatusAsync(string url, string key, string reference, CancellationToken ct);
}

/// <summary>Placeholder until the web app registers the FHIR client.</summary>
public sealed class NoFunderApi : IFunderApi
{
    private static InvalidOperationException Off() => new("Direct claiming is not available in this host.");
    public Task<FunderSent> SubmitClaimAsync(string url, string key, FunderClaim claim, CancellationToken ct) => throw Off();
    public Task<FunderSent> RequestApprovalAsync(string url, string key, FunderApproval approval, CancellationToken ct) => throw Off();
    public Task<FunderReply> ClaimStatusAsync(string url, string key, string reference, CancellationToken ct) => throw Off();
    public Task<FunderReply> ApprovalStatusAsync(string url, string key, string reference, CancellationToken ct) => throw Off();
}

/// <summary>
/// Direct claiming with ACC, Southern Cross and nib (0.34.0, ADR 0047). Ready claims and draft prior approvals go straight to the funder;
/// the claims worker asks for answers every 15 minutes and records payments in Billing. Simulator: for training and the robot –
/// a claim is accepted on the first check and paid on the next; amounts ending .13 are rejected (claims) or declined (approvals).
/// File and portal stay as the fallback when a funder has no API or it is down.
/// </summary>
public sealed class DirectClaimService(ClaimsDbContext db, BillingDbContext billingDb, BillingService billing, PatientsDbContext patients, AuditWriter audit, TimeProvider clock,
    IFunderApi api, BrightPAS.Modules.Reminders.ISecretProtector protector)
{
    private static bool Rejects(decimal amount) => decimal.Remainder(amount * 100m, 100m) == 13m;

    public async Task<string?> SaveApiAsync(Guid funderId, FunderApiEdit e, string user, CancellationToken ct = default)
    {
        var f = await db.Funders.FindAsync([funderId], ct);
        if (f is null) return "Funder not found.";
        if (!Enum.IsDefined(e.Api)) return "Choose how BrightPAS talks to this funder.";
        if (e.Api == FunderApi.Fhir)
        {
            var url = e.ApiUrl?.Trim().TrimEnd('/') ?? f.ApiUrl;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return "The funder's API address must start with https://.";
            if (string.IsNullOrWhiteSpace(e.ApiKey) && f.ApiKeyProtected is null) return "Enter the API key the funder gave you.";
            if (string.IsNullOrWhiteSpace(f.ProviderNumber)) return "Enter the provider number for this funder first.";
            f.ApiUrl = url.Length > 300 ? url[..300] : url;
        }
        if (!string.IsNullOrWhiteSpace(e.ApiKey)) f.ApiKeyProtected = protector.Protect(e.ApiKey.Trim());
        f.Api = e.Api;
        if (e.Api != FunderApi.Off) f.SubmitBy = SubmitBy.Api;
        else if (f.SubmitBy == SubmitBy.Api) f.SubmitBy = SubmitBy.File;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "FunderApi", f.Id.ToString(), reason: $"{f.Code} {f.Api}", ct: ct);
        return null;
    }

    private (string Url, string Key)? Live(Funder f) =>
        f.Api == FunderApi.Fhir && f.ApiUrl is not null && protector.Unprotect(f.ApiKeyProtected) is { } k ? (f.ApiUrl, k) : null;

    private static bool Down(Exception ex) => ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException;

    /// <summary>Sends a ready claim straight to the funder.</summary>
    public async Task<string?> SubmitAsync(Guid claimId, string user, CancellationToken ct = default)
    {
        var c = await db.Claims.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == claimId, ct);
        if (c is null) return "Claim not found.";
        if (c.Status != ClaimStatus.Ready) return "Only a ready claim can be sent.";
        var f = await db.Funders.SingleAsync(x => x.Id == c.FunderId, ct);
        if (f.Api == FunderApi.Off) return $"{f.Name} is not set up for direct claiming – send it by file or portal.";
        FunderSent r;
        if (f.Api == FunderApi.Simulator)
            r = new(true, $"SIM-{c.Number}", null);
        else if (Live(f) is not { } live) r = new(false, null, "The funder's API is not set up.");
        else
        {
            var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == c.PatientId, ct);
            var d = await billingDb.Documents.AsNoTracking().SingleAsync(x => x.Id == c.InvoiceId, ct);
            var pa = c.PriorApprovalId is { } aid ? await db.Approvals.AsNoTracking().Where(a => a.Id == aid).Select(a => a.ApprovalReference).SingleOrDefaultAsync(ct) : null;
            var payload = new FunderClaim(f.ProviderNumber, c.Number, d.Number ?? "", d.IssuedOn ?? c.CreatedAt.NzDate(), p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth,
                c.MemberNumber, c.AccClaimNumber, pa, c.Amount, [.. c.Lines.OrderBy(l => l.Sequence).Select(l => new FunderLine(l.Code, l.Description, l.Amount))]);
            try { r = await api.SubmitClaimAsync(live.Url, live.Key, payload, ct); }
            catch (Exception ex) when (Down(ex)) { r = new(false, null, $"{f.Name} could not be reached: {ex.Message}"); }
        }
        f.LastApiAt = clock.GetUtcNow();
        if (!r.Ok)
        {
            f.LastApiError = Trunc(r.Error ?? "The funder refused the claim.", 300);
            await db.SaveChangesAsync(ct);
            return f.LastApiError + " The claim is still ready – try again or send it by file.";
        }
        f.LastApiError = null;
        c.Status = ClaimStatus.Submitted; c.SubmittedAt = clock.GetUtcNow(); c.SubmittedBy = user; c.ExternalReference = Trunc(r.Reference ?? c.Number, 60);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Submit", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} (direct {f.Code} {c.ExternalReference})", ct: ct);
        return null;
    }

    /// <summary>Sends a draft prior approval request straight to the funder.</summary>
    public async Task<string?> RequestApprovalAsync(Guid approvalId, string user, CancellationToken ct = default)
    {
        var a = await db.Approvals.FindAsync([approvalId], ct);
        if (a is null) return "Prior approval not found.";
        if (a.Status != ApprovalStatus.Draft) return "Only a draft can be sent.";
        var f = await db.Funders.SingleAsync(x => x.Id == a.FunderId, ct);
        if (f.Api == FunderApi.Off) return $"{f.Name} is not set up for direct requests – send it by portal or email.";
        FunderSent r;
        if (f.Api == FunderApi.Simulator) r = new(true, $"SIM-{a.Number}", null);
        else if (Live(f) is not { } live) r = new(false, null, "The funder's API is not set up.");
        else
        {
            var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == a.PatientId, ct);
            try { r = await api.RequestApprovalAsync(live.Url, live.Key, new FunderApproval(f.ProviderNumber, a.Number, p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth, a.MemberNumber, a.AccClaimNumber, a.Procedure, a.AmountRequested), ct); }
            catch (Exception ex) when (Down(ex)) { r = new(false, null, $"{f.Name} could not be reached: {ex.Message}"); }
        }
        f.LastApiAt = clock.GetUtcNow();
        if (!r.Ok)
        {
            f.LastApiError = Trunc(r.Error ?? "The funder refused the request.", 300);
            await db.SaveChangesAsync(ct);
            return f.LastApiError;
        }
        f.LastApiError = null;
        a.Status = ApprovalStatus.Requested; a.ExternalReference = Trunc(r.Reference ?? a.Number, 60);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Requested", "PriorApproval", a.Id.ToString(), a.PatientId.ToString(), reason: $"{a.Number} (direct {f.Code} {a.ExternalReference})", ct: ct);
        return null;
    }

    /// <summary>Asks the funders for answers on everything sent directly and still open (claims worker, every 15 minutes; or the Check now button).</summary>
    public async Task<DirectRun> CheckAsync(string user, CancellationToken ct = default)
    {
        var funders = await db.Funders.Where(f => f.Api != FunderApi.Off).ToDictionaryAsync(f => f.Id, ct);
        var ids = funders.Keys.ToList();
        var claims = await db.Claims.Where(c => c.ExternalReference != null && ids.Contains(c.FunderId) && (c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.Accepted || c.Status == ClaimStatus.PartPaid))
            .OrderBy(c => c.LastCheckedAt).Take(200).ToListAsync(ct);
        var approvals = await db.Approvals.Where(a => a.ExternalReference != null && ids.Contains(a.FunderId) && a.Status == ApprovalStatus.Requested).Take(200).ToListAsync(ct);
        int changed = 0, paid = 0; var errors = new List<string>();
        foreach (var c in claims)
        {
            var f = funders[c.FunderId];
            FunderReply r;
            if (f.Api == FunderApi.Simulator)
                r = Rejects(c.Amount) ? new(true, "Rejected", null, null, null, "Simulator: item not covered by the policy.", null)
                    : c.Status == ClaimStatus.Submitted ? new(true, "Accepted", null, null, null, "Simulator: claim accepted.", null)
                    : new(true, "Paid", c.Amount - c.Paid, $"SIMPAY-{c.Number}", null, "Simulator: paid in full.", null);
            else if (Live(f) is not { } live) { errors.Add($"{f.Name}: the API is not set up."); continue; }
            else
            {
                try { r = await api.ClaimStatusAsync(live.Url, live.Key, c.ExternalReference!, ct); }
                catch (Exception ex) when (Down(ex)) { f.LastApiError = Trunc($"Could not be reached: {ex.Message}", 300); errors.Add($"{f.Name}: {f.LastApiError}"); continue; }
            }
            f.LastApiAt = clock.GetUtcNow();
            c.LastCheckedAt = clock.GetUtcNow();
            if (!r.Ok) { errors.Add($"{c.Number}: {r.Error}"); continue; }
            var (ch, pd, err) = await ApplyClaimAsync(c, f, r, user, ct);
            if (err is not null) errors.Add($"{c.Number}: {err}");
            if (ch) changed++;
            if (pd) paid++;
        }
        foreach (var a in approvals)
        {
            var f = funders[a.FunderId];
            FunderReply r;
            if (f.Api == FunderApi.Simulator)
                r = Rejects(a.AmountRequested) ? new(true, "Declined", null, null, null, "Simulator: not covered by the policy.", null)
                    : new(true, "Approved", a.AmountRequested, $"SIMPA-{a.Number}", NzTime.Today(clock).AddDays(90), null, null);
            else if (Live(f) is not { } live) { errors.Add($"{f.Name}: the API is not set up."); continue; }
            else
            {
                try { r = await api.ApprovalStatusAsync(live.Url, live.Key, a.ExternalReference!, ct); }
                catch (Exception ex) when (Down(ex)) { f.LastApiError = Trunc($"Could not be reached: {ex.Message}", 300); errors.Add($"{f.Name}: {f.LastApiError}"); continue; }
            }
            f.LastApiAt = clock.GetUtcNow();
            if (!r.Ok) { errors.Add($"{a.Number}: {r.Error}"); continue; }
            var status = r.Status switch { "Approved" => ApprovalStatus.Approved, "PartApproved" => ApprovalStatus.PartApproved, "Declined" => ApprovalStatus.Declined, _ => ApprovalStatus.Requested };
            if (status == ApprovalStatus.Requested) continue;
            if (status == ApprovalStatus.Approved && r.Amount < a.AmountRequested) status = ApprovalStatus.PartApproved;
            a.Status = status; a.ApprovalReference = Trunc(r.Reference ?? a.ExternalReference!, 50);
            a.ApprovedAmount = status == ApprovalStatus.Declined ? null : BillingService.Money(r.Amount ?? a.AmountRequested);
            a.ValidUntil = status == ApprovalStatus.Declined ? null : r.ValidUntil ?? NzTime.Today(clock).AddDays(90);
            if (r.Text is not null) a.Notes = Trunc(r.Text, 1000);
            a.DecidedAt = clock.GetUtcNow(); a.DecidedBy = $"{f.Code} (direct)";
            changed++;
            await audit.WriteAsync(user, "Decide", "PriorApproval", a.Id.ToString(), a.PatientId.ToString(), reason: $"{a.Number} {a.Status} {a.ApprovedAmount:0.00} (direct)", ct: ct);
        }
        await db.SaveChangesAsync(ct);
        return new(claims.Count + approvals.Count, changed, paid, errors);
    }

    private async Task<(bool Changed, bool Paid, string? Error)> ApplyClaimAsync(Claim c, Funder f, FunderReply r, string user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        switch (r.Status)
        {
            case "Accepted" when c.Status == ClaimStatus.Submitted:
                c.Status = ClaimStatus.Accepted; c.ResponseAt = now; c.ResponseText = Trunc(r.Text, 1000);
                await audit.WriteAsync(user, "Accepted", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} (direct)", ct: ct);
                return (true, false, null);
            case "Rejected":
                c.Status = ClaimStatus.Rejected; c.ResponseAt = now; c.ResponseText = Trunc(r.Text ?? "Rejected by the funder", 1000);
                await audit.WriteAsync(user, "Rejected", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} {c.ResponseText} (direct)", ct: ct);
                return (true, false, null);
            case "Paid" or "PartPaid" when r.Amount is > 0:
                var amount = BillingService.Money(Math.Min(r.Amount.Value, c.Amount - c.Paid));
                if (amount <= 0) return (false, false, null);
                var err = await billing.RecordPaymentAsync(c.InvoiceId, new NewPayment(now.NzDate(), amount, f.Kind == FunderKind.Acc ? PaymentMethod.Acc : PaymentMethod.Insurer,
                    Trunc($"{c.Number} {r.Reference}".Trim(), 50)), user, ct);
                if (err is not null) return (false, false, err);
                c.Paid += amount;
                c.Status = c.Paid >= c.Amount ? ClaimStatus.Paid : ClaimStatus.PartPaid; c.ResponseAt = now; c.ResponseText = Trunc(r.Text, 1000) ?? c.ResponseText;
                await audit.WriteAsync(user, "Paid", "Claim", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} {amount:0.00} (direct)", ct: ct);
                return (true, true, null);
            default:
                return (false, false, null);
        }
    }

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length > n ? s[..n] : s;
}
