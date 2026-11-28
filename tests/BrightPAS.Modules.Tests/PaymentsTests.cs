using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Payments;
using BrightPAS.Modules.Reminders;

namespace BrightPAS.Modules.Tests;

/// <summary>0.30.0 online payments and statements: settings, payment links (date of birth, lockout), simulator and Windcave payments applied exactly
/// once through Billing, part payments, money that cannot be applied, statement runs (self-funded only, email/SMS or post), statement links paying
/// oldest first, and merge.</summary>
public class PaymentsTests
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

    private static async Task SetContactAsync(IServiceProvider sp, Guid patientId, string? mobile, string? email)
    {
        var pdb = sp.GetRequiredService<PatientsDbContext>();
        var p = await pdb.Patients.SingleAsync(x => x.Id == patientId);
        p.MobilePhone = mobile; p.Email = email;
        await pdb.SaveChangesAsync();
    }

    private static SettingsInput Sim(GatewayProvider provider = GatewayProvider.Simulator, string? user = null, string? key = null) =>
        new(provider, "https://pay.example.co.nz", 30, 10m, null, user, key, 5m, null);

    private static string Ref(string? url) => url![(url!.IndexOf("ref=", StringComparison.Ordinal) + 4)..];
    private static string Token(string link) => link[(link.LastIndexOf('/') + 1)..];

    [Fact]
    public async Task Simulator_payment_link_checks_date_of_birth_and_applies_part_and_full_payments_once()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PaymentLinkService>();
        var p = await h.PatientAsync();
        await SetContactAsync(sp, p.Id, "021 555 0123", null);
        var inv = await InvoiceAsync(sp, p.Id); // 200 + 15% GST = 230

        Assert.Contains("Online payments are off", (await svc.CreateForInvoiceAsync(inv.Id, true, "fin")).Error);
        Assert.Contains("The public web address must start with https://.", await svc.SaveSettingsAsync(Sim() with { PublicUrl = "http://x" }, "fin"));
        Assert.Empty(await svc.SaveSettingsAsync(Sim(), "fin"));

        var (link, sent, error) = await svc.CreateForInvoiceAsync(inv.Id, true, "fin");
        Assert.Null(error);
        Assert.Equal(1, sent);
        Assert.StartsWith("https://pay.example.co.nz/pay/", link);
        var msg = await sp.GetRequiredService<RemindersDbContext>().Messages.SingleAsync(m => m.Kind == MessageKind.PaymentLink);
        Assert.Contains("[payment link]", msg.Body, StringComparison.Ordinal);
        var token = Token(link!);
        Assert.DoesNotContain(token, (await sp.GetRequiredService<PaymentsDbContext>().Links.SingleAsync()).TokenHash, StringComparison.Ordinal);

        // A new link replaces the old one.
        var (link2, _, _) = await svc.CreateForInvoiceAsync(inv.Id, false, "fin");
        Assert.Equal("closed", await svc.PublicStatusAsync(token));
        token = Token(link2!);
        Assert.Equal("open", await svc.PublicStatusAsync(token));

        // Wrong date of birth five times locks the link for 30 minutes.
        for (var i = 0; i < 4; i++) Assert.Contains("does not match", (await svc.ViewAsync(token, Dob.AddDays(1))).Error);
        Assert.Contains("does not match", (await svc.ViewAsync(token, Dob.AddDays(1))).Error);
        Assert.Contains("Too many attempts", (await svc.ViewAsync(token, Dob)).Error);
        h.Clock.Advance(TimeSpan.FromMinutes(31));

        var (view, _) = await svc.ViewAsync(token, Dob);
        Assert.Equal(230m, view!.Balance);
        Assert.Equal("Aroha", view.FirstName);
        Assert.True(view.CanPay);
        Assert.True(view.Simulator);
        Assert.Contains("most you can pay", (await svc.StartAsync(token, Dob, 231m)).Error);
        Assert.Contains("smallest payment", (await svc.StartAsync(token, Dob, 5m)).Error);

        // Part payment, approved by the simulator.
        var (url, err) = await svc.StartAsync(token, Dob, 100m);
        Assert.Null(err);
        var reference = Ref(url);
        Assert.StartsWith($"https://pay.example.co.nz/pay/{token}/simulator", url);
        Assert.Equal("Approved", (await svc.SimulateAsync(token, reference, "approve")).Status);
        Assert.Equal("Approved", (await svc.SimulateAsync(token, reference, "approve")).Status); // again: nothing more applied
        var billing = sp.GetRequiredService<BillingService>();
        var after = (await billing.GetAsync(inv.Id, "fin", null))!;
        Assert.Equal(DocumentStatus.PartPaid, after.Status);
        var payment = Assert.Single(after.Payments);
        Assert.Equal(100m, payment.Amount);
        Assert.Equal(PaymentMethod.CreditCard, payment.Method);
        Assert.Equal(PaymentLinkService.PatientUser, payment.RecordedBy);
        Assert.Contains(reference, payment.Reference, StringComparison.Ordinal);

        // Declined, then the rest approved: invoice and link paid.
        (url, _) = await svc.StartAsync(token, Dob, 130m);
        Assert.Equal("Declined", (await svc.SimulateAsync(token, Ref(url), "decline")).Status);
        (url, _) = await svc.StartAsync(token, Dob, 130m);
        Assert.Equal("Approved", (await svc.SimulateAsync(token, Ref(url), "approve")).Status);
        Assert.Equal(DocumentStatus.Paid, (await billing.GetAsync(inv.Id, "fin", null))!.Status);
        Assert.Equal("paid", await svc.PublicStatusAsync(token));
        Assert.Contains("has been paid", (await svc.StartAsync(token, Dob, 10m)).Error);
        Assert.Equal(0m, (await svc.ViewAsync(token, Dob)).View!.Balance);
        var rows = await svc.PaymentsAsync("all", inv.Id);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Visa ••4242", rows.First(r => r.Status == "Approved").Card);
        Assert.Contains(rows, r => r.AppliedTo.Contains($"{inv.Number} $130.00"));
        Assert.Equal(0, await svc.AttentionCountAsync());

        // Abandoned simulator payment is closed after an hour by the background check.
        var inv2 = await InvoiceAsync(sp, p.Id);
        var (l3, _, _) = await svc.CreateForInvoiceAsync(inv2.Id, false, "fin");
        Assert.Null((await svc.StartAsync(Token(l3!), Dob, 50m)).Error);
        h.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(1, await svc.CheckUnfinishedAsync());
        Assert.Equal("Cancelled", (await svc.PaymentsAsync("all", inv2.Id)).Single().Status);
    }

    [Fact]
    public async Task Windcave_result_is_read_from_windcave_applied_once_and_leftover_money_needs_attention()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<PaymentLinkService>();
        var fake = sp.GetRequiredService<TestWindcave>();
        var p = await h.PatientAsync();
        var inv = await InvoiceAsync(sp, p.Id);

        Assert.Contains("Enter the Windcave API key.", await svc.SaveSettingsAsync(Sim(GatewayProvider.Windcave, "tds_rest"), "fin"));
        Assert.Empty(await svc.SaveSettingsAsync(Sim(GatewayProvider.Windcave, "tds_rest", "secret-key"), "fin"));
        var stored = await sp.GetRequiredService<PaymentsDbContext>().Settings.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("secret-key", stored.WindcaveKeyProtected!, StringComparison.Ordinal);
        Assert.Empty(await svc.SaveSettingsAsync(Sim(GatewayProvider.Windcave, "tds_rest"), "fin")); // key kept when left blank

        var token = Token((await svc.CreateForInvoiceAsync(inv.Id, false, "fin")).Link!);
        var (url, err) = await svc.StartAsync(token, Dob, 230m);
        Assert.Null(err);
        Assert.Equal("https://uat.windcave.com/hpp/?id=S1", url);
        var created = Assert.Single(fake.Created);
        Assert.Equal($"https://pay.example.co.nz/pay/{token}/done", created.ReturnUrl);
        Assert.Equal("https://pay.example.co.nz/api/pay-notify", created.NotifyUrl);

        // The browser comes back before Windcave has a result: still waiting; the wrong link cannot check it.
        Assert.Equal("Started", (await svc.CheckAsync(token, "S1")).Status);
        Assert.Equal("Payment not found.", (await svc.CheckAsync("someone-elses-token", "S1")).Error);

        // Paid at the desk meanwhile: Windcave approves, but only 30.00 is still owing – the rest needs attention.
        Assert.Null(await sp.GetRequiredService<BillingService>().RecordPaymentAsync(inv.Id, new NewPayment(new DateOnly(2026, 11, 9), 200m, PaymentMethod.Eftpos, null), "rec"));
        fake.Results["S1"] = new GatewayResult(true, true, false, "visa", "411111......1111", "123456", "APPROVED", 230m);
        Assert.Equal("Approved", (await svc.CheckAsync(null, "S1")).Status);   // the notification
        Assert.Equal("Approved", (await svc.CheckAsync(token, "S1")).Status);  // the return page: already done
        var inv1 = (await sp.GetRequiredService<BillingService>().GetAsync(inv.Id, "fin", null))!;
        Assert.Equal(DocumentStatus.Paid, inv1.Status);
        Assert.Equal(30m, inv1.Payments.Single(x => x.Method == PaymentMethod.CreditCard).Amount);
        var row = Assert.Single(await svc.PaymentsAsync("attention", null));
        Assert.Contains("$200.00 was paid online", row.AttentionReason, StringComparison.Ordinal);
        Assert.Equal("visa ••1111", row.Card);
        Assert.Equal(1, await svc.AttentionCountAsync());
        Assert.Contains("Say what was done", await svc.ResolveAsync(row.Id, " ", "fin"));
        Assert.Null(await svc.ResolveAsync(row.Id, "Refunded $200 in Windcave Payline", "fin"));
        Assert.Equal(0, await svc.AttentionCountAsync());

        // An amount different from what was asked is never applied automatically.
        var inv2 = await InvoiceAsync(sp, p.Id);
        var t2 = Token((await svc.CreateForInvoiceAsync(inv2.Id, false, "fin")).Link!);
        Assert.Null((await svc.StartAsync(t2, Dob, 50m)).Error);
        fake.Results["S2"] = new GatewayResult(true, true, false, "mastercard", "5555", "654321", "APPROVED", 500m);
        Assert.Equal("Approved", (await svc.CheckAsync(t2, "S2")).Status);
        Assert.Empty((await sp.GetRequiredService<BillingService>().GetAsync(inv2.Id, "fin", null))!.Payments);
        Assert.Contains("approved $500.00", (await svc.PaymentsAsync("attention", null)).Single().AttentionReason, StringComparison.Ordinal);

        // Declined result: nothing applied.
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null((await svc.StartAsync(t2, Dob, 50m)).Error);
        fake.Results["S3"] = new GatewayResult(true, false, false, "visa", "0002", null, "DECLINED", 50m);
        h.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, await svc.CheckUnfinishedAsync());
        Assert.Equal("Declined", (await svc.PaymentsAsync("all", inv2.Id)).First().Status);
    }

    [Fact]
    public async Task Statements_cover_self_funded_balances_go_by_email_text_or_post_and_their_link_pays_oldest_first()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var links = sp.GetRequiredService<PaymentLinkService>();
        var st = sp.GetRequiredService<StatementService>();
        var pdf = (TestStatementPdf)sp.GetRequiredService<IStatementPdf>();
        var a = await h.PatientAsync();
        var b = await h.PatientAsync("Smith", "John", "ZAA0024", new DateOnly(1970, 1, 1));
        var c = await h.PatientAsync("Brown", "Lee", null, new DateOnly(1990, 2, 2));
        await SetContactAsync(sp, a.Id, "021 555 0123", "aroha@example.co.nz");
        await SetContactAsync(sp, b.Id, null, null);
        await SetContactAsync(sp, c.Id, "021 555 0999", null);
        var a1 = await InvoiceAsync(sp, a.Id, price: 100m);  // 115
        h.Clock.Advance(TimeSpan.FromDays(30));
        var a2 = await InvoiceAsync(sp, a.Id, price: 200m);  // 230
        await InvoiceAsync(sp, a.Id, Payer.Insurer, 500m);    // claims, not statements
        await InvoiceAsync(sp, b.Id, price: 50m);             // 57.50, posted
        await InvoiceAsync(sp, c.Id, price: 3m);              // 3.45 – under the $5 minimum
        var today = new DateOnly(2026, 12, 9);

        Assert.Contains("future", (await st.PrepareAsync(today.AddDays(1), "fin")).Error);
        var (run, error) = await st.PrepareAsync(today, "fin");
        Assert.Null(error);
        Assert.Equal(2, run!.Count);
        Assert.Equal(345m + 57.50m, run.Total);
        Assert.Contains("not been sent", (await st.PrepareAsync(today, "fin")).Error);
        var rows = await st.StatementsAsync(run.Id);
        var sa = rows.Single(r => r.PatientId == a.Id);
        Assert.Equal(345m, sa.TotalDue);
        Assert.Equal(115m, sa.Overdue); // the first invoice was due 20 days after it was issued
        Assert.Equal("Email", sa.Delivery);
        Assert.Equal("Post", rows.Single(r => r.PatientId == b.Id).Delivery);

        Assert.Contains("Set up online payments", (await st.SendAsync(run.Id, "fin")).Error);
        Assert.Empty(await links.SaveSettingsAsync(Sim(), "fin"));
        Assert.Contains("Send the run first", (await st.PrintBatchAsync(run.Id, false, "fin")).Error);
        var (sent, post, err) = await st.SendAsync(run.Id, "fin");
        Assert.Null(err);
        Assert.Equal(1, sent);
        Assert.Equal(1, post);
        Assert.Contains("already been sent", (await st.SendAsync(run.Id, "fin")).Error);
        Assert.Contains("have been sent", await st.DeleteRunAsync(run.Id, "fin"));
        var messages = await sp.GetRequiredService<RemindersDbContext>().Messages.Where(m => m.Kind == MessageKind.Statement).ToListAsync();
        Assert.Equal(2, messages.Count); // SMS and email for patient A

        var (batch, _) = await st.PrintBatchAsync(run.Id, false, "fin");
        Assert.NotNull(batch);
        var printed = Assert.Single(pdf.Rendered);
        Assert.Equal("John Smith", printed.PatientName);
        Assert.StartsWith("https://pay.example.co.nz/pay/", printed.PayUrl);
        Assert.Contains("no statements to post", (await st.PrintBatchAsync(run.Id, false, "fin")).Error);
        Assert.Equal(1, (await st.RunsAsync()).Single().Printed);

        // Patient B pays the posted statement's link: the statement shows, the payment goes on the invoice.
        var tb = Token(printed.PayUrl!);
        var (view, _) = await links.ViewAsync(tb, new DateOnly(1970, 1, 1));
        Assert.Equal(57.50m, view!.Balance);
        Assert.Equal(rows.Single(r => r.PatientId == b.Id).Number, view.StatementNumber);
        Assert.NotNull((await st.PublicPdfAsync(tb, new DateOnly(1970, 1, 1))).Pdf);
        Assert.Contains("does not match", (await st.PublicPdfAsync(tb, Dob)).Error);

        // Patient A's statement link (made when it was sent): a part payment goes on the oldest invoice first, then the next.
        var linkA = await sp.GetRequiredService<PaymentsDbContext>().Links.SingleAsync(l => l.PatientId == a.Id && l.StatementId != null);
        var tokenA = Token(await links.CreateForStatementAsync(await sp.GetRequiredService<PaymentsDbContext>().Statements.SingleAsync(s => s.PatientId == a.Id), "fin"));
        Assert.Equal(PayLinkStatus.Cancelled, (await sp.GetRequiredService<PaymentsDbContext>().Links.AsNoTracking().SingleAsync(l => l.Id == linkA.Id)).Status);
        var (url, _) = await links.StartAsync(tokenA, Dob, 150m);
        Assert.Equal("Approved", (await links.SimulateAsync(tokenA, Ref(url), "approve")).Status);
        var billing = sp.GetRequiredService<BillingService>();
        Assert.Equal(DocumentStatus.Paid, (await billing.GetAsync(a1.Id, "fin", null))!.Status);
        Assert.Equal(35m, (await billing.GetAsync(a2.Id, "fin", null))!.Payments.Single().Amount);
        Assert.Equal(195m, (await st.StatementsAsync(run.Id)).Single(r => r.PatientId == a.Id).BalanceNow);

        // Merge moves links, payments and statements.
        Assert.True(await links.MergeAsync(b.Id, a.Id) >= 3);
        Assert.Equal(2, (await st.ForPatientAsync(b.Id)).Count);
    }
}
