using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;

namespace BrightPAS.Modules.Payments;

public sealed record AutomationResult(bool Ran, string? Skipped, int StatementCount, int StatementsSent, int StatementsToPost, int OverdueSent, int OverdueNotSent, int PlanReminders, int PlansCompleted);
public sealed record OverdueRow(Guid Id, Guid InvoiceId, string? InvoiceNumber, Guid PatientId, string Patient, int Stage, decimal Balance, DateTimeOffset SentAt, int Messages);

/// <summary>
/// The daily accounts round (0.32.0, ADR 0045), run once a day after 9 am NZ time by the payments worker (or by Finance with "Run now"):
/// completes paid-off plans, sends payment plan instalment reminders, prepares (and if set, sends) the month's statements on the chosen day, and sends
/// first and second overdue reminders. Everything is off until Finance sets it up, and each reminder is sent once only.
/// </summary>
public sealed class AutomationService(PaymentsDbContext db, BillingDbContext billingDb, PatientsDbContext patients, PaymentLinkService links, StatementService statements,
    PlanService plans, ReminderService reminders, AuditWriter audit, TimeProvider clock)
{
    public const string SystemUser = "system";
    public const int StartHour = 9;

    public async Task<AutomationResult> RunAsync(bool force, string user = SystemUser, CancellationToken ct = default)
    {
        var s = await links.SettingsAsync(ct);
        var today = NzTime.Today(clock);
        if (!force)
        {
            if (s.LastAutomationOn == today) return new(false, "Already run today.", 0, 0, 0, 0, 0, 0, 0);
            if (clock.GetUtcNow().ToNz().Hour < StartHour) return new(false, "Waiting until 9 am.", 0, 0, 0, 0, 0, 0, 0);
        }
        s.LastAutomationOn = today;
        await db.SaveChangesAsync(ct);
        var online = s.Provider != GatewayProvider.Off && s.PublicUrl is not null;

        // 1. Plans: complete paid-off plans, remind before the next instalment.
        var active = await plans.ActiveAsync(ct);
        var completed = active.Count(x => x.Plan.Status == PlanStatus.Completed);
        var planReminders = 0;
        if (online)
            foreach (var (plan, progress) in active.Where(x => x.Plan.Status == PlanStatus.Active))
            {
                if (progress.NextDue is not { } due || plan.LastReminderFor == due || today < due.AddDays(-s.PlanReminderDays) || today > due.AddDays(7)) continue;
                var url = await links.CreateForPlanAsync(plan.Id, plan.PatientId, user, ct);
                var queued = await reminders.QueuePaymentLinkAsync(plan.PatientId, MessageKind.PlanInstalment, url, $"plan:{plan.Id}:{due:yyyyMMdd}", user, ct,
                    new Dictionary<string, string?> { ["Amount"] = $"${progress.NextAmount:0.00}", ["Date"] = Addresses.FormatDate(due) });
                plan.LastReminderFor = due;
                if (queued.Any(m => m.Status == MessageStatus.Queued)) planReminders++;
            }
        await db.SaveChangesAsync(ct);

        // 2. Statements on the chosen day of the month.
        int stCount = 0, stSent = 0, stPost = 0;
        if (s.AutoStatementDay > 0 && today.Day == s.AutoStatementDay && !await db.Runs.AnyAsync(r => r.StatementDate == today, ct))
        {
            var (run, err) = await statements.PrepareAsync(today, user, ct);
            if (run is not null)
            {
                stCount = run.Count;
                if (s.AutoSendStatements && online) (stSent, stPost, _) = await statements.SendAsync(run.Id, user, ct);
            }
            else if (err is not null) await audit.WriteAsync(user, "AutoStatements", "StatementRun", "auto", reason: err, ct: ct);
        }

        // 3. Overdue reminders.
        int overdueSent = 0, overdueNot = 0;
        if (online && s.OverdueFirstDays > 0)
        {
            var (_, planInvoices) = await plans.CoverageAsync(ct);
            var firstCutoff = today.AddDays(-s.OverdueFirstDays);
            var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Include(d => d.Payments)
                .Where(d => d.Kind == DocumentKind.Invoice && (d.Status == DocumentStatus.Issued || d.Status == DocumentStatus.PartPaid) && d.DueOn != null && d.DueOn <= firstCutoff)
                .OrderBy(d => d.DueOn).Take(500).ToListAsync(ct);
            var ids = docs.Select(d => d.Id).ToList();
            var sent = await db.Overdue.AsNoTracking().Where(o => ids.Contains(o.InvoiceId)).Select(o => new { o.InvoiceId, o.Stage }).ToListAsync(ct);
            var pids = docs.Select(d => d.PatientId).Distinct().ToList();
            var live = (await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id) && p.MergedIntoId == null).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
            foreach (var d in docs)
            {
                if (!live.Contains(d.PatientId) || planInvoices.Contains(d.Id) || !StatementService.PatientOwes(d, s.IncludeShortfalls)) continue;
                var balance = BillingService.Calculate(d).Balance;
                if (balance < s.StatementMinimum || balance <= 0) continue;
                var stage = sent.Any(x => x.InvoiceId == d.Id && x.Stage == 1) ? 2 : 1;
                if (stage == 2 && (s.OverdueSecondDays == 0 || d.DueOn > today.AddDays(-s.OverdueSecondDays) || sent.Any(x => x.InvoiceId == d.Id && x.Stage == 2))) continue;
                var (url, _, err) = await links.CreateForInvoiceAsync(d.Id, false, user, ct);
                if (url is null) { await audit.WriteAsync(user, "OverdueReminder", "Invoice", d.Id.ToString(), d.PatientId.ToString(), reason: err, ct: ct); continue; }
                var queued = await reminders.QueuePaymentLinkAsync(d.PatientId, MessageKind.OverdueReminder, url, $"overdue:{d.Id}:{stage}", user, ct,
                    new Dictionary<string, string?> { ["Amount"] = $"${balance:0.00}", ["Date"] = d.DueOn is { } due ? Addresses.FormatDate(due) : "" , ["InvoiceNumber"] = d.Number });
                db.Overdue.Add(new OverdueNotice { InvoiceId = d.Id, PatientId = d.PatientId, InvoiceNumber = d.Number, Stage = stage, Balance = balance, SentAt = clock.GetUtcNow(), Messages = queued.Count(m => m.Status == MessageStatus.Queued) });
                await db.SaveChangesAsync(ct);
                if (queued.Any(m => m.Status == MessageStatus.Queued)) overdueSent++; else overdueNot++;
            }
        }
        var result = new AutomationResult(true, null, stCount, stSent, stPost, overdueSent, overdueNot, planReminders, completed);
        await audit.WriteAsync(user, "AccountsRound", "PaymentSettings", "1",
            reason: $"Statements {stCount} ({stSent} sent, {stPost} to post); overdue {overdueSent} sent, {overdueNot} not sent; plan reminders {planReminders}; plans completed {completed}", ct: ct);
        return result;
    }

    public async Task<List<OverdueRow>> OverdueAsync(CancellationToken ct = default)
    {
        var list = await db.Overdue.AsNoTracking().OrderByDescending(o => o.SentAt).ThenBy(o => o.InvoiceNumber).Take(300).ToListAsync(ct);
        var ids = list.Select(r => r.PatientId).Distinct().ToList();
        var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FamilyName.ToUpperInvariant() + ", " + p.GivenNames, ct);
        return [.. list.Select(o => new OverdueRow(o.Id, o.InvoiceId, o.InvoiceNumber, o.PatientId, names.GetValueOrDefault(o.PatientId, ""), o.Stage, o.Balance, o.SentAt, o.Messages))];
    }
}
