using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Payments;
using BrightPAS.Modules.Reminders;

namespace BrightPAS.Modules.Tests;

/// <summary>0.32.0 payments extras: refunds back to the card (unapplied money first, then off invoices), payment plans (schedule, progress from any
/// payment, completion), the daily accounts round (automatic statements, overdue reminders once each, plan reminders) and insurer shortfalls.</summary>
public class PaymentsExtrasTests
{
    private static readonly DateOnly Dob = new(1980, 5, 17);

    private static async Task<BillingDocument> InvoiceAsync(IServiceProvider sp, Guid patientId, Payer payer = Payer.SelfFunded, decimal price = 200m)
    {
        var billing = sp.GetRequiredService<BillingService>();
        var (d, errors) = await billing.CreateAsync(DocumentKind.Invoice, new NewDocument(patientId, payer, payer == Payer.SelfFunded ? null : "Southern Cross", payer == Payer.SelfFunded ? null : "POL1",
            "Dr Ben Hall", null, null, [new(null, "Day surgery facility fee", 1, price)]), "fin");
        Assert.Empty(errors);
        Assert.Null(await billing.SetStatusAsync(d!.Id, DocumentStatus.Issued, null, "fin"));
        return (await billing.GetAsync(d.Id, "fin", null))!;
    }

    private static SettingsInput Settings(GatewayProvider provider = GatewayProvider.Simulator, string? user = null, string? key = null) =>
        new(provider, "https://pay.example.co.nz", 30, 10m, null, user, key, 5m, null);

    private static string Ref(string? url) => url![(url!.IndexOf("ref=", StringComparison.Ordinal) + 4)..];
    private static string Token(string link) => link[(link.LastIndexOf('/') + 1)..];

    private static async Task SetMobileAsync(IServiceProvider sp, Guid patientId)
    {
        var pdb = sp.GetRequiredService<PatientsDbContext>();
        var p = await pdb.Patients.SingleAsync(x => x.Id == patientId);
        p.MobilePhone = "021 555 0123";
        await pdb.SaveChangesAsync();
    }

    [Fact]
    public async Task Refunds_go_back_to_the_card_unapplied_money_first_then_off_the_invoices()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var links = sp.GetRequiredService<PaymentLinkService>();
        var refunds = sp.GetRequiredService<RefundService>();
        var billing = sp.GetRequiredService<BillingService>();
        var p = await h.PatientAsync();
        Assert.Empty(await links.SaveSettingsAsync(Settings(), "fin"));
        var inv = await InvoiceAsync(sp, p.Id); // 230

        var token = Token((await links.CreateForInvoiceAsync(inv.Id, false, "fin")).Link!);
        var (url, _) = await links.StartAsync(token, Dob, 100m);
        Assert.Equal("Approved", (await links.SimulateAsync(token, Ref(url), "approve")).Status);
        var pay = (await links.PaymentsAsync("approved", inv.Id)).Single();

        Assert.Contains("Say why", (await refunds.RefundAsync(pay.Id, 10m, " ", "fin")).Error);
        Assert.Contains("most that can be refunded", (await refunds.RefundAsync(pay.Id, 100.01m, "x", "fin")).Error);
        var (r, err) = await refunds.RefundAsync(pay.Id, 40m, "Procedure cost less than quoted", "fin");
        Assert.Null(err);
        Assert.Equal("RF000001", r!.Number);
        Assert.StartsWith("SIMRF-", r.ProviderReference);
        var doc = (await billing.GetAsync(inv.Id, "fin", null))!;
        Assert.Equal(60m, BillingService.Calculate(doc).Paid);
        Assert.Equal(DocumentStatus.PartPaid, doc.Status);
        Assert.Contains(doc.Payments, x => x.Amount == -40m && x.Reference!.Contains("RF000001", StringComparison.Ordinal));
        Assert.Equal(40m, (await links.PaymentsAsync("approved", inv.Id)).Single().Refunded);

        // Refund the rest: invoice back to nothing paid (issued), and no more can be refunded.
        Assert.Null((await refunds.RefundAsync(pay.Id, 60m, "Cancelled", "fin")).Error);
        doc = (await billing.GetAsync(inv.Id, "fin", null))!;
        Assert.Equal(DocumentStatus.Issued, doc.Status);
        Assert.Contains("most that can be refunded on this payment is $0.00", (await refunds.RefundAsync(pay.Id, 1m, "x", "fin")).Error);
        Assert.Equal(2, (await refunds.ListAsync(pay.Id)).Count);
        Assert.Contains("more than has been paid", await billing.RecordRefundAsync(inv.Id, 1m, PaymentMethod.CreditCard, null, "fin"));

        // Windcave: money that could not be applied is refunded first and clears "needs attention"; a declined refund changes nothing.
        Assert.Empty(await links.SaveSettingsAsync(Settings(GatewayProvider.Windcave, "tds_rest", "secret"), "fin"));
        var fake = sp.GetRequiredService<TestWindcave>();
        var inv2 = await InvoiceAsync(sp, p.Id);
        var t2 = Token((await links.CreateForInvoiceAsync(inv2.Id, false, "fin")).Link!);
        Assert.Null((await links.StartAsync(t2, Dob, 50m)).Error);
        await billing.RecordPaymentAsync(inv2.Id, new NewPayment(NzTime.Today(h.Clock), 200m, PaymentMethod.Eftpos, null), "desk"); // 30 left
        fake.Results["S1"] = new GatewayResult(true, true, false, "visa", "1111", "123456", "APPROVED", 50m, "TXN0001");
        Assert.Equal("Approved", (await links.CheckAsync(t2, "S1")).Status);
        var p2 = (await links.PaymentsAsync("attention", null)).Single();
        fake.RefundOk = false;
        Assert.Contains("did not go through", (await refunds.RefundAsync(p2.Id, 20m, "Paid twice", "fin")).Error);
        Assert.Equal(1, await links.AttentionCountAsync());
        fake.RefundOk = true;
        (r, err) = await refunds.RefundAsync(p2.Id, 25m, "Paid twice", "fin");
        Assert.Null(err);
        Assert.Equal(20m, r!.FromUnapplied);
        Assert.Equal(("TXN0001", 25m), (fake.Refunds[^1].TransactionId, fake.Refunds[^1].Amount));
        Assert.Contains($"{inv2.Number} $5.00", r.AppliedTo, StringComparison.Ordinal);
        Assert.Equal(0, await links.AttentionCountAsync());
        Assert.Equal("Failed", (await refunds.ListAsync(p2.Id)).Last().Status);
    }

    [Fact]
    public async Task Payment_plans_follow_any_payment_complete_themselves_and_remind_before_each_instalment()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var links = sp.GetRequiredService<PaymentLinkService>();
        var plans = sp.GetRequiredService<PlanService>();
        var auto = sp.GetRequiredService<AutomationService>();
        var billing = sp.GetRequiredService<BillingService>();
        var p = await h.PatientAsync();
        await SetMobileAsync(sp, p.Id);
        Assert.Empty(await links.SaveSettingsAsync(Settings(), "fin"));
        var inv = await InvoiceAsync(sp, p.Id, price: 400m); // 460
        await billing.RecordPaymentAsync(inv.Id, new NewPayment(NzTime.Today(h.Clock), 60m, PaymentMethod.Eftpos, null), "desk"); // 400 left
        var today = NzTime.Today(h.Clock);

        Assert.Single(await plans.OptionsAsync(p.Id));
        Assert.Contains((await plans.CreateAsync(new PlanInput(p.Id, null, 10m, PlanFrequency.Weekly, today, null), "fin")).Errors, e => e.Contains("at most 24", StringComparison.Ordinal));
        Assert.Contains((await plans.CreateAsync(new PlanInput(p.Id, null, 100m, PlanFrequency.Monthly, today.AddDays(-1), null), "fin")).Errors, e => e.Contains("next two months", StringComparison.Ordinal));
        var (plan, errors) = await plans.CreateAsync(new PlanInput(p.Id, null, 150m, PlanFrequency.Fortnightly, today.AddDays(3), "Agreed by phone"), "fin");
        Assert.Empty(errors);
        Assert.Equal(("PP000001", 400m, 60m), (plan!.Number, plan.Total, plan.PaidAtStart));
        Assert.Empty(await plans.OptionsAsync(p.Id));
        Assert.Contains((await plans.CreateAsync(new PlanInput(p.Id, null, 100m, PlanFrequency.Monthly, today, null), "fin")).Errors, e => e.Contains("already has an active", StringComparison.Ordinal));

        var d = (await plans.GetAsync(plan.Id))!;
        Assert.Equal([150m, 150m, 100m], d.Progress.Schedule.Select(s => s.Amount));
        Assert.Equal(today.AddDays(31), d.Progress.Schedule[2].DueOn);
        Assert.Equal(0m, d.Progress.Behind);

        // Reminder 2 days before the first instalment, once, with the amount and a plan link.
        var r = await auto.RunAsync(true, "fin");
        Assert.Equal(0, r.PlanReminders);
        h.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, (await auto.RunAsync(true, "fin")).PlanReminders);
        Assert.Equal(0, (await auto.RunAsync(true, "fin")).PlanReminders);
        var msg = await sp.GetRequiredService<RemindersDbContext>().Messages.SingleAsync(m => m.Kind == MessageKind.PlanInstalment);
        Assert.Contains("$150.00", msg.Body, StringComparison.Ordinal);
        Assert.NotNull(await sp.GetRequiredService<PaymentsDbContext>().Links.SingleOrDefaultAsync(l => l.PlanId == plan.Id && l.Status == PayLinkStatus.Open));

        // Behind once the date passes unpaid; paying at the desk counts.
        h.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(150m, (await plans.ListAsync("behind", null)).Single().Behind);
        await billing.RecordPaymentAsync(inv.Id, new NewPayment(NzTime.Today(h.Clock), 150m, PaymentMethod.Eftpos, null), "desk");
        Assert.Empty(await plans.ListAsync("behind", null));
        Assert.Equal(today.AddDays(17), (await plans.GetAsync(plan.Id))!.Progress.NextDue);

        // Paying the rest completes the plan.
        await billing.RecordPaymentAsync(inv.Id, new NewPayment(NzTime.Today(h.Clock), 250m, PaymentMethod.BankTransfer, null), "fin");
        Assert.Equal(1, (await auto.RunAsync(true, "fin")).PlansCompleted);
        Assert.Equal("Completed", (await plans.GetAsync(plan.Id))!.Plan.Status);

        // Cancelling needs a reason.
        var inv2 = await InvoiceAsync(sp, p.Id);
        var (plan2, _) = await plans.CreateAsync(new PlanInput(p.Id, [inv2.Id], 50m, PlanFrequency.Weekly, NzTime.Today(h.Clock).AddDays(7), null), "fin");
        Assert.Contains("Say why", await plans.CancelAsync(plan2!.Id, null, "fin"));
        Assert.Null(await plans.CancelAsync(plan2.Id, "Paying in full", "fin"));
        Assert.Equal(2, (await plans.ListAsync("all", p.Id)).Count);
    }

    [Fact]
    public async Task Daily_round_prepares_statements_on_the_day_sends_overdue_reminders_once_and_includes_shortfalls_when_on()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var links = sp.GetRequiredService<PaymentLinkService>();
        var auto = sp.GetRequiredService<AutomationService>();
        var billing = sp.GetRequiredService<BillingService>();
        var statements = sp.GetRequiredService<StatementService>();
        var p = await h.PatientAsync();
        await SetMobileAsync(sp, p.Id);
        var today = NzTime.Today(h.Clock);
        Assert.Contains("The second overdue reminder must come after the first.", await links.SaveSettingsAsync(Settings() with { OverdueFirstDays = 14, OverdueSecondDays = 7 }, "fin"));
        Assert.Contains(await links.SaveSettingsAsync(Settings() with { AutoStatementDay = 31 }, "fin"), e => e.Contains("choose a day from 1 to 28", StringComparison.Ordinal));
        Assert.Empty(await links.SaveSettingsAsync(Settings() with { AutoStatementDay = today.AddDays(1).Day > 28 ? 1 : today.AddDays(1).Day, AutoSendStatements = true, OverdueFirstDays = 7, OverdueSecondDays = 21 }, "fin"));

        var self = await InvoiceAsync(sp, p.Id);                     // due in 20 days (default terms)
        var ins = await InvoiceAsync(sp, p.Id, Payer.Insurer, 300m); // 345, insurer pays 300
        Assert.Null(await billing.RecordPaymentAsync(ins.Id, new NewPayment(today, 300m, PaymentMethod.Insurer, "SX"), "fin"));

        // Nothing is due yet; off-day: no statements.
        var r = await auto.RunAsync(true, "fin");
        Assert.Equal((0, 0), (r.StatementCount, r.OverdueSent));
        Assert.False((await auto.RunAsync(false, "fin")).Ran); // already run today

        // On the statement day a run is prepared and sent (self-funded only while shortfalls are off).
        h.Clock.Advance(TimeSpan.FromDays(1));
        r = await auto.RunAsync(true, "fin");
        Assert.Equal((1, 1), (r.StatementCount, r.StatementsSent));
        var st = (await statements.ForPatientAsync(p.Id)).Single();
        Assert.Equal(230m, st.TotalDue);

        // Overdue: 7 days after the due date (first), then 21 days (second), each once.
        var due = self.DueOn!.Value;
        h.Clock.Advance(TimeSpan.FromDays(due.DayNumber - NzTime.Today(h.Clock).DayNumber + 7));
        Assert.Equal(1, (await auto.RunAsync(true, "fin")).OverdueSent);
        Assert.Equal(0, (await auto.RunAsync(true, "fin")).OverdueSent);
        var msg = await sp.GetRequiredService<RemindersDbContext>().Messages.SingleAsync(m => m.Kind == MessageKind.OverdueReminder);
        Assert.Contains(self.Number!, msg.Body, StringComparison.Ordinal);
        Assert.Contains("$230.00", msg.Body, StringComparison.Ordinal);
        h.Clock.Advance(TimeSpan.FromDays(14));
        Assert.Equal(1, (await auto.RunAsync(true, "fin")).OverdueSent);
        Assert.Equal(0, (await auto.RunAsync(true, "fin")).OverdueSent);
        Assert.Equal([2, 1], (await auto.OverdueAsync()).Select(o => o.Stage));

        // Shortfalls on: the insurer invoice's $45 joins the next statement.
        Assert.Empty(await links.SaveSettingsAsync(Settings() with { IncludeShortfalls = true }, "fin"));
        var (run, _) = await statements.PrepareAsync(NzTime.Today(h.Clock), "fin");
        var lines = StatementService.ReadSnapshot((await sp.GetRequiredService<PaymentsDbContext>().Statements.SingleAsync(x => x.RunId == run!.Id)).Snapshot);
        Assert.Equal(275m, lines.Sum(l => l.Balance));
        Assert.True(lines.Single(l => l.InvoiceId == ins.Id).Shortfall);

        // Patients keeping to a payment plan are left off statements.
        await statements.DeleteRunAsync(run!.Id, "fin");
        var (plan, errors) = await sp.GetRequiredService<PlanService>().CreateAsync(new PlanInput(p.Id, null, 50m, PlanFrequency.Weekly, NzTime.Today(h.Clock).AddDays(2), null), "fin");
        Assert.Empty(errors);
        Assert.Equal(275m, plan!.Total);
        Assert.Contains("No patient", (await statements.PrepareAsync(NzTime.Today(h.Clock), "fin")).Error);
    }
}
