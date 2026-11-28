using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Estimates;

public sealed record FollowUpInput(EstFollowUp Status, string? Note, DateOnly? NextActionOn, string? ApprovalNumber, decimal? DepositAmount, DateOnly? DepositPaidOn, Guid? BookingId);
public sealed record FollowUpRow(Guid Id, string Ref, string Patient, Guid? PatientId, string? Procedure, string? Surgeon, string Funding, string? Insurer, decimal Subtotal, decimal Deposit,
    DateTimeOffset? IssuedAt, DateOnly? ValidUntil, string? FollowUp, DateTimeOffset? FollowUpAt, DateOnly? NextActionOn, int? DaysLeft, string Why, string? InvoiceNumber, Guid? InvoiceId);
public sealed record EventRow(string Status, DateTimeOffset At, string By, string? Note);
public sealed record ReportGroup(string Name, int Issued, int Accepted, int Declined, int Booked, int Expired, int Open, decimal IssuedValue, decimal WonValue, double? AcceptRate, double? AvgDaysToAccept);
public sealed record FollowUpReport(DateOnly From, DateOnly To, ReportGroup Total, IReadOnlyList<ReportGroup> BySurgeon, IReadOnlyList<ReportGroup> ByProcedure, IReadOnlyList<ReportGroup> ByFunding, IReadOnlyList<ReportGroup> ByMonth);

/// <summary>
/// Estimate follow-up (0.33.0, ADR 0046). An issued estimate is Waiting until the patient (or their insurer) answers: Accepted or Declined; for insured
/// patients Approval requested → Approved (with the insurer's number) or Approval declined; then Deposit paid, Booked and Invoiced. Lists: to chase
/// (nothing heard after the follow-up days, or the next-action date reached), running out (valid-until within the warning days) and expired. A daily
/// round marks estimates past their valid-until date as Expired unless they were declined, booked or invoiced. A report shows how many estimates
/// were accepted and booked, the value won and how long patients took, by surgeon, procedure, funding and month.
/// </summary>
public sealed class EstimateFollowUpService(EstDbContext db, AuditWriter audit, TimeProvider clock)
{
    private DateOnly Today => NzTime.Today(clock);
    private static readonly EstFollowUp[] Finished = [EstFollowUp.Declined, EstFollowUp.ApprovalDeclined, EstFollowUp.Booked, EstFollowUp.Invoiced, EstFollowUp.Expired];
    private static readonly EstFollowUp[] Won = [EstFollowUp.Accepted, EstFollowUp.Approved, EstFollowUp.DepositPaid, EstFollowUp.Booked, EstFollowUp.Invoiced];

    public static string Text(EstFollowUp f) => f switch
    {
        EstFollowUp.Waiting => "Waiting to hear",
        EstFollowUp.Accepted => "Accepted",
        EstFollowUp.Declined => "Declined",
        EstFollowUp.ApprovalRequested => "Insurer approval requested",
        EstFollowUp.Approved => "Insurer approved",
        EstFollowUp.ApprovalDeclined => "Insurer declined",
        EstFollowUp.DepositPaid => "Deposit paid",
        EstFollowUp.Booked => "Booked",
        EstFollowUp.Invoiced => "Invoiced",
        _ => "Expired",
    };

    public async Task<string?> SetAsync(Guid id, FollowUpInput r, string user, CancellationToken ct = default)
    {
        var e = await db.Estimates.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return "Estimate not found.";
        if (e.Status != EstimateStatus.Issued) return "Only an issued estimate can be followed up.";
        if (!Enum.IsDefined(r.Status) || r.Status is EstFollowUp.Invoiced or EstFollowUp.Expired) return "Choose what happened.";
        if (e.FollowUp == EstFollowUp.Invoiced) return "This estimate has been invoiced.";
        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        if (note is { Length: > 500 }) return "Keep the note under 500 characters.";
        if (r.NextActionOn is { } next && (next < Today || next > Today.AddDays(366))) return "The next follow-up date must be within the next year.";
        switch (r.Status)
        {
            case EstFollowUp.Declined or EstFollowUp.ApprovalDeclined when note is null:
                return "Say why it was declined.";
            case EstFollowUp.ApprovalRequested or EstFollowUp.Approved or EstFollowUp.ApprovalDeclined when e.Funding != Funding.Insurance:
                return "Insurer approval is only for insured patients – change the funding on the estimate first.";
            case EstFollowUp.Approved when string.IsNullOrWhiteSpace(r.ApprovalNumber) && string.IsNullOrWhiteSpace(e.PriorApproval):
                return "Enter the insurer's approval number.";
            case EstFollowUp.DepositPaid when r.DepositAmount is not > 0:
                return "Enter the deposit paid.";
            case EstFollowUp.DepositPaid when r.DepositAmount > e.Subtotal:
                return "The deposit is more than the estimate.";
            case EstFollowUp.DepositPaid when r.DepositPaidOn is { } on && on > Today:
                return "The deposit date cannot be in the future.";
        }
        var now = clock.GetUtcNow();
        if (r.Status == EstFollowUp.Approved && r.ApprovalNumber?.Trim() is { Length: > 0 } approval) e.PriorApproval = approval[..Math.Min(100, approval.Length)];
        if (r.Status is EstFollowUp.Declined or EstFollowUp.ApprovalDeclined) e.DeclineReason = note is null ? null : note[..Math.Min(300, note.Length)];
        if (r.Status == EstFollowUp.DepositPaid) (e.DepositPaid, e.DepositPaidOn) = (Math.Round(r.DepositAmount!.Value, 2), r.DepositPaidOn ?? Today);
        if (r.Status == EstFollowUp.Booked && r.BookingId is not null) e.BookingId = r.BookingId;
        (e.FollowUp, e.FollowUpAt, e.FollowUpBy, e.NextActionOn) = (r.Status, now, user, Finished.Contains(r.Status) ? null : r.NextActionOn);
        var detail = r.Status switch
        {
            EstFollowUp.Approved => $"Approval {e.PriorApproval}",
            EstFollowUp.DepositPaid => $"${e.DepositPaid:0.00} on {e.DepositPaidOn:d MMM yyyy}",
            _ => null,
        };
        db.Events.Add(new EstimateEvent { EstimateId = e.Id, Status = r.Status, At = now, By = user, Note = string.Join(" – ", new[] { detail, note }.Where(x => x is not null)) is { Length: > 0 } n ? n[..Math.Min(500, n.Length)] : null });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "FollowUp", "Estimate", e.Id.ToString(), e.PatientId?.ToString(), reason: $"{e.Ref}: {r.Status}{(detail is null ? "" : " " + detail)}", ct: ct);
        return null;
    }

    /// <summary>Called when a Billing invoice has been made from the estimate.</summary>
    public async Task MarkInvoicedAsync(Guid id, Guid invoiceId, string? number, string user, CancellationToken ct = default)
    {
        var e = await db.Estimates.SingleAsync(x => x.Id == id, ct);
        var now = clock.GetUtcNow();
        (e.InvoiceId, e.InvoiceNumber, e.FollowUp, e.FollowUpAt, e.FollowUpBy, e.NextActionOn) = (invoiceId, number, EstFollowUp.Invoiced, now, user, null);
        db.Events.Add(new EstimateEvent { EstimateId = e.Id, Status = EstFollowUp.Invoiced, At = now, By = user, Note = number is null ? "Invoice made" : $"Invoice {number}" });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<EventRow>> HistoryAsync(Guid id, CancellationToken ct = default) =>
        [.. (await db.Events.AsNoTracking().Where(x => x.EstimateId == id).OrderBy(x => x.At).ToListAsync(ct)).Select(x => new EventRow(x.Status.ToString(), x.At, x.By, x.Note))];

    /// <summary>The daily round: issued estimates past their valid-until date become Expired, unless declined, booked or invoiced.</summary>
    public async Task<int> ExpireAsync(string user, CancellationToken ct = default)
    {
        var today = Today;
        var list = await db.Estimates.Where(e => e.Status == EstimateStatus.Issued && e.ValidUntil != null && e.ValidUntil < today).ToListAsync(ct);
        list = [.. list.Where(e => e.FollowUp is null || !Finished.Contains(e.FollowUp.Value))];
        var now = clock.GetUtcNow();
        foreach (var e in list)
        {
            (e.FollowUp, e.FollowUpAt, e.FollowUpBy, e.NextActionOn) = (EstFollowUp.Expired, now, user, null);
            db.Events.Add(new EstimateEvent { EstimateId = e.Id, Status = EstFollowUp.Expired, At = now, By = user, Note = $"Valid until {e.ValidUntil:d MMM yyyy}" });
        }
        if (list.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "Expire", "Estimate", "daily", reason: $"{list.Count} estimate(s) expired", ct: ct);
        }
        return list.Count;
    }

    /// <summary>Work lists: "chase" (nothing heard after the follow-up days or the next-action date reached), "expiring" (running out soon), "expired", "open" (all not finished).</summary>
    public async Task<List<FollowUpRow>> ListAsync(string view, CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new EstSettings();
        var today = Today;
        var issued = await db.Estimates.AsNoTracking().Where(e => e.Status == EstimateStatus.Issued).OrderBy(e => e.ValidUntil).Take(2000).ToListAsync(ct);
        IEnumerable<(Estimate e, string why)> rows = view switch
        {
            "chase" => issued.Where(e => e.FollowUp is null || !Finished.Contains(e.FollowUp.Value) && e.FollowUp != EstFollowUp.DepositPaid || e.FollowUp == EstFollowUp.DepositPaid && e.NextActionOn <= today)
                .Select(e => (e, why: ChaseReason(e, s, today))).Where(x => x.why.Length > 0),
            "expiring" => issued.Where(e => (e.FollowUp is null || !Finished.Contains(e.FollowUp.Value)) && e.ValidUntil is { } v && v >= today && v <= today.AddDays(s.ExpiryWarningDays))
                .Select(e => (e, why: $"Valid until {e.ValidUntil:d MMM}")),
            "expired" => issued.Where(e => e.FollowUp == EstFollowUp.Expired).Select(e => (e, why: $"Expired {e.ValidUntil:d MMM yyyy}")),
            "won" => issued.Where(e => e.FollowUp is { } f && Won.Contains(f) && f != EstFollowUp.Invoiced).Select(e => (e, why: e.FollowUp == EstFollowUp.Booked ? "Booked – make the invoice" : "Accepted – book it")),
            _ => issued.Where(e => e.FollowUp is null || !Finished.Contains(e.FollowUp.Value)).Select(e => (e, why: "")),
        };
        return [.. rows.Take(500).Select(x => Row(x.e, x.why, today))];
    }

    private static string ChaseReason(Estimate e, EstSettings s, DateOnly today)
    {
        if (e.NextActionOn is { } next) return next <= today ? $"Follow-up date {next:d MMM}" : "";
        var since = (e.FollowUpAt ?? e.IssuedAt)?.NzDate();
        if (since is null) return "";
        var due = since.Value.AddDays(s.FollowUpDays);
        return due > today ? "" : e.FollowUp switch
        {
            null or EstFollowUp.Waiting => $"Nothing heard for {today.DayNumber - since.Value.DayNumber} days",
            EstFollowUp.ApprovalRequested => $"Insurer approval requested {today.DayNumber - since.Value.DayNumber} days ago",
            EstFollowUp.Accepted or EstFollowUp.Approved => "Accepted but not booked",
            _ => "",
        };
    }

    private static FollowUpRow Row(Estimate e, string why, DateOnly today) => new(e.Id, e.Ref, $"{e.LastName}, {e.FirstName}", e.PatientId, e.ProcedureName, e.SurgeonName, e.Funding.ToString(), e.Insurer,
        e.Subtotal, e.Deposit, e.IssuedAt, e.ValidUntil, e.FollowUp?.ToString(), e.FollowUpAt, e.NextActionOn, e.ValidUntil is { } v ? v.DayNumber - today.DayNumber : null, why, e.InvoiceNumber, e.InvoiceId);

    /// <summary>Estimates issued between the dates, grouped. Accept rate = won ÷ (won + declined + expired); average days from issue to the first acceptance.</summary>
    public async Task<FollowUpReport> ReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = NzTime.StartOfDay(from);
        var end = NzTime.StartOfDay(to.AddDays(1));
        var list = await db.Estimates.AsNoTracking().Where(e => e.IssuedAt != null && e.IssuedAt >= start && e.IssuedAt < end).ToListAsync(ct);
        var ids = list.Select(e => e.Id).ToList();
        var accepted = (await db.Events.AsNoTracking().Where(x => ids.Contains(x.EstimateId)).ToListAsync(ct))
            .Where(x => x.Status is EstFollowUp.Accepted or EstFollowUp.Approved or EstFollowUp.DepositPaid or EstFollowUp.Booked)
            .GroupBy(x => x.EstimateId).ToDictionary(g => g.Key, g => g.Min(x => x.At));
        ReportGroup Group(string name, IEnumerable<Estimate> items)
        {
            var l = items.ToList();
            bool IsWon(Estimate e) => e.FollowUp is { } f && Won.Contains(f);
            var won = l.Count(IsWon);
            var lost = l.Count(e => e.FollowUp is EstFollowUp.Declined or EstFollowUp.ApprovalDeclined or EstFollowUp.Expired);
            var days = l.Where(e => IsWon(e) && accepted.ContainsKey(e.Id)).Select(e => (accepted[e.Id] - e.IssuedAt!.Value).TotalDays).ToList();
            return new ReportGroup(name, l.Count, won, l.Count(e => e.FollowUp is EstFollowUp.Declined or EstFollowUp.ApprovalDeclined), l.Count(e => e.FollowUp is EstFollowUp.Booked or EstFollowUp.Invoiced),
                l.Count(e => e.FollowUp == EstFollowUp.Expired), l.Count(e => e.Status == EstimateStatus.Issued && (e.FollowUp is null || !Finished.Contains(e.FollowUp.Value) && !IsWon(e))),
                l.Sum(e => e.Subtotal), l.Where(IsWon).Sum(e => e.Subtotal), won + lost == 0 ? null : Math.Round(100.0 * won / (won + lost), 1), days.Count == 0 ? null : Math.Round(days.Average(), 1));
        }
        static List<ReportGroup> By(IEnumerable<Estimate> l, Func<Estimate, string> key, Func<string, IEnumerable<Estimate>, ReportGroup> g) =>
            [.. l.GroupBy(key).Select(x => g(x.Key, x)).OrderByDescending(x => x.Issued).ThenBy(x => x.Name, StringComparer.Ordinal)];
        return new FollowUpReport(from, to, Group("All", list), By(list, e => e.SurgeonName ?? "(no surgeon)", Group), By(list, e => e.ProcedureName ?? "(no procedure)", Group),
            By(list, e => e.Funding switch { Funding.Insurance => "Insurance", Funding.SelfFunding => "Self-funding", _ => "Not yet known" }, Group),
            [.. list.GroupBy(e => e.IssuedAt!.Value.ToNz().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => Group(g.Key, g))]);
    }

}
