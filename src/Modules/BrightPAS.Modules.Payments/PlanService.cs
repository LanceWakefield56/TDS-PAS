using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Payments;

public sealed record PlanInput(Guid PatientId, IReadOnlyList<Guid>? InvoiceIds, decimal Instalment, PlanFrequency Frequency, DateOnly FirstDue, string? Note);
public sealed record PlanInstalment(int Number, DateOnly DueOn, decimal Amount, decimal CumulativeDue, bool Paid);
/// <summary>A plan's progress today. Behind = less paid than the instalments already due.</summary>
public sealed record PlanProgress(decimal PaidSoFar, decimal Remaining, decimal DueToDate, decimal Behind, DateOnly? NextDue, decimal NextAmount, IReadOnlyList<PlanInstalment> Schedule);
public sealed record PlanRow(Guid Id, string Number, Guid PatientId, string Patient, decimal Total, decimal Instalment, string Frequency, DateOnly FirstDue, string Status,
    decimal PaidSoFar, decimal Remaining, decimal Behind, DateOnly? NextDue, string? Note, DateTimeOffset CreatedAt, string CreatedBy, string? EndReason, IReadOnlyList<string> Invoices);
public sealed record PlanDetail(PlanRow Plan, PlanProgress Progress);

/// <summary>
/// Payment plans (0.32.0, ADR 0045). Finance agrees with a patient to pay a self-funded balance (or insurer shortfall) in weekly, fortnightly or
/// monthly instalments. Progress counts every payment made on the plan's invoices since the plan began (desk, online or bank), so nothing has to be
/// matched by hand. Reminders with a payment link go before each instalment; a plan completes itself when the balance is paid.
/// </summary>
public sealed class PlanService(PaymentsDbContext db, BillingDbContext billingDb, PatientsDbContext patients, PaymentLinkService links, AuditWriter audit, TimeProvider clock)
{
    public const int MaxInstalments = 24;
    private DateOnly Today => NzTime.Today(clock);

    public static DateOnly DueDate(PaymentPlan p, int index) => p.Frequency switch
    {
        PlanFrequency.Weekly => p.FirstDue.AddDays(7 * index),
        PlanFrequency.Fortnightly => p.FirstDue.AddDays(14 * index),
        _ => p.FirstDue.AddMonths(index), // from the first date each time, so 31 Jan → 28/29 Feb → 31 Mar
    };

    public static List<PlanInstalment> Schedule(PaymentPlan p, decimal paidSoFar)
    {
        var list = new List<PlanInstalment>();
        decimal cumulative = 0;
        for (var i = 0; cumulative < p.Total && i < MaxInstalments + 1; i++)
        {
            var amt = Math.Min(p.Instalment, p.Total - cumulative);
            cumulative += amt;
            list.Add(new PlanInstalment(i + 1, DueDate(p, i), amt, cumulative, paidSoFar >= cumulative));
        }
        return list;
    }

    public static PlanProgress Progress(PaymentPlan p, decimal paidNow, DateOnly today)
    {
        var paid = Math.Max(0, paidNow - p.PaidAtStart);
        var schedule = Schedule(p, paid);
        var dueToDate = schedule.Where(s => s.DueOn <= today).Select(s => s.CumulativeDue).DefaultIfEmpty(0).Max();
        var next = schedule.FirstOrDefault(s => !s.Paid);
        return new PlanProgress(paid, Math.Max(0, p.Total - paid), dueToDate, Math.Max(0, dueToDate - paid), next?.DueOn, next is null ? 0 : Math.Min(next.CumulativeDue - paid, p.Total - paid), schedule);
    }

    /// <summary>Total paid so far on each plan's invoices (all payments, any method).</summary>
    private async Task<Dictionary<Guid, (decimal Paid, decimal Balance)>> InvoiceTotalsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Include(d => d.Payments).Where(d => invoiceIds.Contains(d.Id)).ToListAsync(ct);
        return docs.ToDictionary(d => d.Id, d =>
        {
            var t = BillingService.Calculate(d);
            return (t.Paid, d.Status is DocumentStatus.Issued or DocumentStatus.PartPaid ? t.Balance : 0m);
        });
    }

    /// <summary>Invoices a plan can cover for this patient: issued self-funded invoices with money owing (and insurer shortfalls when switched on), not on another active plan.</summary>
    public async Task<List<InvoiceDue>> OptionsAsync(Guid patientId, CancellationToken ct = default)
    {
        var s = await links.SettingsAsync(ct);
        var onPlan = await db.PlanInvoices.AsNoTracking().Join(db.Plans.Where(p => p.Status == PlanStatus.Active), i => i.PlanId, p => p.Id, (i, p) => i.InvoiceId).ToListAsync(ct);
        var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Include(d => d.Payments)
            .Where(d => d.PatientId == patientId && d.Kind == DocumentKind.Invoice && (d.Status == DocumentStatus.Issued || d.Status == DocumentStatus.PartPaid)).ToListAsync(ct);
        var ids = docs.Where(d => !onPlan.Contains(d.Id) && StatementService.PatientOwes(d, s.IncludeShortfalls)).Select(d => d.Id).ToList();
        return [.. (await links.DueAsync(ids, ct)).Where(d => d.Balance > 0)];
    }

    public async Task<(PaymentPlan? Plan, IReadOnlyList<string> Errors)> CreateAsync(PlanInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var options = await OptionsAsync(r.PatientId, ct);
        var chosen = r.InvoiceIds is { Count: > 0 } ? options.Where(o => r.InvoiceIds.Contains(o.InvoiceId)).ToList() : options;
        if (r.InvoiceIds is { Count: > 0 } && chosen.Count != r.InvoiceIds.Distinct().Count()) errors.Add("One or more invoices cannot go on a plan (paid, not self-funded, or already on a plan).");
        if (chosen.Count == 0) errors.Add("This patient has no balance that can go on a payment plan.");
        if (await db.Plans.AnyAsync(p => p.PatientId == r.PatientId && p.Status == PlanStatus.Active, ct)) errors.Add("This patient already has an active payment plan. Cancel it first.");
        var total = chosen.Sum(c => c.Balance);
        var instalment = BillingService.Money(r.Instalment);
        var s = await links.SettingsAsync(ct);
        if (!Enum.IsDefined(r.Frequency)) errors.Add("Choose how often the patient pays.");
        if (instalment <= 0) errors.Add("Enter the instalment amount.");
        else if (instalment < Math.Min(s.MinimumPayment, total)) errors.Add($"Each instalment must be at least ${s.MinimumPayment:0.00}.");
        else if (instalment > total && total > 0) errors.Add("The instalment is more than the balance – take a normal payment instead.");
        else if (total > 0 && Math.Ceiling(total / instalment) > MaxInstalments) errors.Add($"A plan can have at most {MaxInstalments} instalments – increase the instalment.");
        if (r.FirstDue < Today || r.FirstDue > Today.AddDays(62)) errors.Add("The first instalment must be due within the next two months.");
        if (errors.Count > 0) return (null, errors);
        var totals = await InvoiceTotalsAsync([.. chosen.Select(c => c.InvoiceId)], ct);
        var plan = new PaymentPlan
        {
            Number = $"PP{s.NextPlanNumber++:D6}",
            PatientId = r.PatientId,
            Total = total,
            Instalment = instalment,
            Frequency = r.Frequency,
            FirstDue = r.FirstDue,
            PaidAtStart = totals.Values.Sum(t => t.Paid),
            Note = string.IsNullOrWhiteSpace(r.Note) ? null : Trunc(r.Note.Trim(), 300),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
        };
        foreach (var c in chosen) plan.Invoices.Add(new PlanInvoice { PlanId = plan.Id, InvoiceId = c.InvoiceId });
        db.Plans.Add(plan);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "PaymentPlan", plan.Id.ToString(), plan.PatientId.ToString(),
            reason: $"{plan.Number} {total:0.00} as {instalment:0.00} {r.Frequency} from {r.FirstDue:yyyy-MM-dd}", ct: ct);
        return (plan, []);
    }

    public async Task<string?> CancelAsync(Guid id, string? reason, string user, CancellationToken ct = default)
    {
        var p = await db.Plans.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return "Plan not found.";
        if (p.Status != PlanStatus.Active) return "This plan has already ended.";
        if (string.IsNullOrWhiteSpace(reason)) return "Say why the plan is being cancelled.";
        (p.Status, p.EndedAt, p.EndedBy, p.EndReason) = (PlanStatus.Cancelled, clock.GetUtcNow(), user, Trunc(reason.Trim(), 300));
        var now = clock.GetUtcNow();
        foreach (var l in await db.Links.Where(l => l.PlanId == id && l.Status == PayLinkStatus.Open).ToListAsync(ct)) (l.Status, l.CancelledAt, l.CancelledBy) = (PayLinkStatus.Cancelled, now, user);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "PaymentPlan", p.Id.ToString(), p.PatientId.ToString(), reason: $"{p.Number}: {p.EndReason}", ct: ct);
        return null;
    }

    /// <summary>Progress for active plans (with the plan's invoices), completing any whose balance is now paid.</summary>
    public async Task<List<(PaymentPlan Plan, PlanProgress Progress)>> ActiveAsync(CancellationToken ct = default)
    {
        var plans = await db.Plans.Include(p => p.Invoices).Where(p => p.Status == PlanStatus.Active).ToListAsync(ct);
        return await WithProgressAsync(plans, ct);
    }

    private async Task<List<(PaymentPlan Plan, PlanProgress Progress)>> WithProgressAsync(List<PaymentPlan> plans, CancellationToken ct)
    {
        var totals = await InvoiceTotalsAsync([.. plans.SelectMany(p => p.Invoices.Select(i => i.InvoiceId)).Distinct()], ct);
        var list = new List<(PaymentPlan, PlanProgress)>();
        var changed = false;
        foreach (var p in plans)
        {
            var mine = p.Invoices.Select(i => totals.GetValueOrDefault(i.InvoiceId)).ToList();
            var progress = Progress(p, mine.Sum(t => t.Paid), Today);
            if (p.Status == PlanStatus.Active && (progress.Remaining <= 0 || mine.Sum(t => t.Balance) <= 0))
            {
                (p.Status, p.EndedAt, p.EndedBy, p.EndReason) = (PlanStatus.Completed, clock.GetUtcNow(), "system", "Balance paid");
                changed = true;
                await audit.WriteAsync("system", "Complete", "PaymentPlan", p.Id.ToString(), p.PatientId.ToString(), reason: p.Number, ct: ct);
            }
            list.Add((p, progress));
        }
        if (changed) await db.SaveChangesAsync(ct);
        return list;
    }

    /// <summary>Patients whose active plan is up to date (they get instalment reminders, not statements or overdue reminders) and the invoices on active plans.</summary>
    public async Task<(HashSet<Guid> OnTrackPatients, HashSet<Guid> PlanInvoices)> CoverageAsync(CancellationToken ct = default)
    {
        var active = (await ActiveAsync(ct)).Where(x => x.Plan.Status == PlanStatus.Active).ToList();
        return ([.. active.Where(x => x.Progress.Behind <= 0).Select(x => x.Plan.PatientId)], [.. active.SelectMany(x => x.Plan.Invoices.Select(i => i.InvoiceId))]);
    }

    public async Task<List<PlanRow>> ListAsync(string? view, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Plans.Include(p => p.Invoices).AsQueryable();
        if (patientId is not null) q = q.Where(p => p.PatientId == patientId);
        if (view is null or "active" or "behind") q = q.Where(p => p.Status == PlanStatus.Active);
        var plans = await q.OrderByDescending(p => p.CreatedAt).Take(300).ToListAsync(ct);
        var rows = await RowsAsync(await WithProgressAsync(plans, ct), ct);
        return view == "behind" ? [.. rows.Where(r => r.Behind > 0 && r.Status == nameof(PlanStatus.Active))] : rows;
    }

    public async Task<PlanDetail?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Plans.Include(x => x.Invoices).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return null;
        var withProgress = await WithProgressAsync([p], ct);
        return new PlanDetail((await RowsAsync(withProgress, ct))[0], withProgress[0].Progress);
    }

    private async Task<List<PlanRow>> RowsAsync(List<(PaymentPlan Plan, PlanProgress Progress)> list, CancellationToken ct)
    {
        var ids = list.Select(x => x.Plan.PatientId).Distinct().ToList();
        var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FamilyName.ToUpperInvariant() + ", " + p.GivenNames, ct);
        var invIds = list.SelectMany(x => x.Plan.Invoices.Select(i => i.InvoiceId)).Distinct().ToList();
        var numbers = await billingDb.Documents.AsNoTracking().Where(d => invIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number ?? "", ct);
        return [.. list.Select(x => new PlanRow(x.Plan.Id, x.Plan.Number, x.Plan.PatientId, names.GetValueOrDefault(x.Plan.PatientId, ""), x.Plan.Total, x.Plan.Instalment,
            x.Plan.Frequency.ToString(), x.Plan.FirstDue, x.Plan.Status.ToString(), x.Progress.PaidSoFar, x.Progress.Remaining, x.Plan.Status == PlanStatus.Active ? x.Progress.Behind : 0,
            x.Plan.Status == PlanStatus.Active ? x.Progress.NextDue : null, x.Plan.Note, x.Plan.CreatedAt, x.Plan.CreatedBy, x.Plan.EndReason,
            [.. x.Plan.Invoices.Select(i => numbers.GetValueOrDefault(i.InvoiceId, "")).Order(StringComparer.Ordinal)]))];
    }

    private static string Trunc(string v, int max) => v.Length <= max ? v : v[..max];
}
