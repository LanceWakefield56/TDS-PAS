using System.Text;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Claims;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BrightPAS.Modules.Tests;

/// <summary>0.34.0 direct insurer claiming, integrated EFTPOS (Windcave), credit notes and Xero contacts (ADR 0047).</summary>
public class DirectClaimingTests
{
    private sealed class FakeFunder : IFunderApi
    {
        public List<FunderClaim> Claims { get; } = [];
        public FunderReply Next { get; set; } = new(true, "Pending", null, null, null, null, null);
        public Task<FunderSent> SubmitClaimAsync(string url, string key, FunderClaim claim, CancellationToken ct)
        {
            Claims.Add(claim);
            return Task.FromResult(key == "k1" ? new FunderSent(true, "FHIR-77", null) : new FunderSent(false, null, "bad key"));
        }
        public Task<FunderSent> RequestApprovalAsync(string url, string key, FunderApproval approval, CancellationToken ct) => Task.FromResult(new FunderSent(true, "PA-77", null));
        public Task<FunderReply> ClaimStatusAsync(string url, string key, string reference, CancellationToken ct) => Task.FromResult(Next);
        public Task<FunderReply> ApprovalStatusAsync(string url, string key, string reference, CancellationToken ct) => Task.FromResult(Next);
    }

    private sealed class FakeTerminal : ITerminalApi
    {
        public List<string> Started { get; } = [];
        public TerminalPoll Next { get; set; } = new(false, null);
        public Task<TerminalStart> StartAsync(string url, string user, string key, string station, bool refund, decimal amount, string txnRef, CancellationToken ct)
        {
            Started.Add($"{(refund ? "refund" : "purchase")} {station} {amount:0.00} {key}");
            return Task.FromResult(new TerminalStart(true, null));
        }
        public Task<TerminalPoll> StatusAsync(string url, string user, string key, string station, string txnRef, CancellationToken ct) => Task.FromResult(Next);
    }

    private static async Task<(Claim Claim, Funder Funder)> ReadyClaimAsync(IServiceProvider sp, Guid patientId)
    {
        var claims = sp.GetRequiredService<ClaimsService>();
        await claims.SeedFundersAsync();
        var sx = (await claims.FundersAsync(true)).Single(f => f.Code == "SX");
        Assert.Null((await claims.SaveFunderAsync(new FunderEdit(sx.Id, "SX", sx.Name, sx.Kind, SubmitBy.File, false, null, "TDS-99", true, null), "fin")).Error);
        var inv = await ClaimsTests.InvoiceAsync(sp, patientId, Payer.Insurer, "Southern Cross", "SX123456");
        var (c, errors) = await claims.CreateFromInvoiceAsync(inv.Id, sx.Id, null, null, null, "fin");
        Assert.Empty(errors);
        Assert.True((await claims.MarkReadyAsync(c!.Id, null, null, null, "fin")).Ready);
        return (c, sx);
    }

    [Fact]
    public async Task Simulator_claims_are_accepted_then_paid_and_approvals_decided()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var (c, sx) = await ReadyClaimAsync(sp, p.Id);
        var direct = sp.GetRequiredService<DirectClaimService>();
        var db = sp.GetRequiredService<ClaimsDbContext>();

        Assert.Contains("not set up for direct claiming", await direct.SubmitAsync(c.Id, "fin"));
        Assert.Null(await direct.SaveApiAsync(sx.Id, new FunderApiEdit(FunderApi.Simulator, null, null), "admin"));
        Assert.Equal(SubmitBy.Api, (await db.Funders.AsNoTracking().SingleAsync(f => f.Id == sx.Id)).SubmitBy);
        Assert.Null(await direct.SubmitAsync(c.Id, "fin"));
        Assert.Contains("Only a ready claim", await direct.SubmitAsync(c.Id, "fin"));

        var r1 = await direct.CheckAsync("system");
        Assert.Equal(1, r1.Changed);
        Assert.Equal(ClaimStatus.Accepted, (await db.Claims.AsNoTracking().SingleAsync(x => x.Id == c.Id)).Status);
        var r2 = await direct.CheckAsync("system");
        Assert.Equal(1, r2.Paid);
        var after = await db.Claims.AsNoTracking().SingleAsync(x => x.Id == c.Id);
        Assert.Equal(ClaimStatus.Paid, after.Status);
        var inv = await sp.GetRequiredService<BillingService>().GetAsync(after.InvoiceId, "fin", null);
        Assert.Equal(0m, BillingService.Calculate(inv!).Balance);
        Assert.Equal(0, (await direct.CheckAsync("system")).Checked); // nothing left open

        // Approvals: .13 is declined by the simulator, anything else approved for 90 days.
        var claims = sp.GetRequiredService<ClaimsService>();
        var (ok, _) = await claims.CreateApprovalAsync(new ApprovalEdit(p.Id, null, sx.Id, "SX123456", null, "Colonoscopy", 2500m, null), "rec");
        var (no, _) = await claims.CreateApprovalAsync(new ApprovalEdit(p.Id, null, sx.Id, "SX123456", null, "Gastroscopy", 1000.13m, null), "rec");
        Assert.Null(await direct.RequestApprovalAsync(ok!.Id, "rec"));
        Assert.Null(await direct.RequestApprovalAsync(no!.Id, "rec"));
        Assert.Equal(2, (await direct.CheckAsync("system")).Changed);
        var list = await claims.ApprovalsAsync(p.Id, null, false);
        Assert.Equal(ApprovalStatus.Approved, list.Single(a => a.Id == ok.Id).Status);
        Assert.Equal(new DateOnly(2027, 2, 7), list.Single(a => a.Id == ok.Id).ValidUntil);
        Assert.Equal(ApprovalStatus.Declined, list.Single(a => a.Id == no.Id).Status);
    }

    [Fact]
    public async Task Fhir_funder_gets_the_claim_and_a_part_payment_is_recorded()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var (c, sx) = await ReadyClaimAsync(sp, p.Id);
        var fake = new FakeFunder();
        var direct = new DirectClaimService(sp.GetRequiredService<ClaimsDbContext>(), sp.GetRequiredService<BillingDbContext>(), sp.GetRequiredService<BillingService>(),
            sp.GetRequiredService<PatientsDbContext>(), sp.GetRequiredService<AuditWriter>(), h.Clock, fake, sp.GetRequiredService<ISecretProtector>());
        Assert.Contains("https://", await direct.SaveApiAsync(sx.Id, new FunderApiEdit(FunderApi.Fhir, "http://sx.example", "k1"), "admin"));
        Assert.Contains("API key", await direct.SaveApiAsync(sx.Id, new FunderApiEdit(FunderApi.Fhir, "https://sx.example/fhir", null), "admin"));
        Assert.Null(await direct.SaveApiAsync(sx.Id, new FunderApiEdit(FunderApi.Fhir, "https://sx.example/fhir/", "wrong"), "admin"));
        Assert.Contains("bad key", await direct.SubmitAsync(c.Id, "fin")); // still ready – can go by file
        Assert.Null(await direct.SaveApiAsync(sx.Id, new FunderApiEdit(FunderApi.Fhir, null, "k1"), "admin"));
        Assert.Null(await direct.SubmitAsync(c.Id, "fin"));
        var sent = fake.Claims[^1];
        Assert.Equal(("TDS-99", "ZSC21TN", "SX123456", 2438m, 3), (sent.ProviderNumber, sent.Nhi, sent.MemberNumber, sent.Amount, sent.Lines.Count));

        Assert.Equal(0, (await direct.CheckAsync("system")).Changed); // pending
        fake.Next = new(true, "PartPaid", 2000m, "EFT-1", null, "Excess applies", null);
        Assert.Equal(1, (await direct.CheckAsync("system")).Paid);
        var db = sp.GetRequiredService<ClaimsDbContext>();
        var after = await db.Claims.AsNoTracking().SingleAsync(x => x.Id == c.Id);
        Assert.Equal((ClaimStatus.PartPaid, 2000m, "FHIR-77"), (after.Status, after.Paid, after.ExternalReference));
        var pay = await sp.GetRequiredService<BillingDbContext>().Payments.AsNoTracking().SingleAsync(x => x.DocumentId == after.InvoiceId);
        Assert.Equal(PaymentMethod.Insurer, pay.Method);
    }

    [Fact]
    public async Task Windcave_terminal_purchase_and_refund_keep_only_the_last_four_digits()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var inv = await ClaimsTests.InvoiceAsync(sp, p.Id, Payer.SelfFunded, null, null);
        var fake = new FakeTerminal();
        var term = new TerminalService(sp.GetRequiredService<ClaimsDbContext>(), sp.GetRequiredService<BillingDbContext>(), sp.GetRequiredService<BillingService>(),
            sp.GetRequiredService<AuditWriter>(), h.Clock, fake, sp.GetRequiredService<ISecretProtector>());
        var billing = sp.GetRequiredService<BillingService>();
        Assert.Contains("station ID", await term.SaveSettingsAsync(new TerminalSettingsEdit(TerminalProvider.Windcave, null, "https://uat.windcave.com", "hit", "key1"), "admin"));
        Assert.Null(await term.SaveSettingsAsync(new TerminalSettingsEdit(TerminalProvider.Windcave, "ST-1", "https://uat.windcave.com", "hit", "key1"), "admin"));

        var (t, err) = await term.StartAsync(inv.Id, 400m, "rec");
        Assert.Null(err);
        Assert.Equal("purchase ST-1 400.00 key1", fake.Started.Single());
        Assert.Equal("Pending", (await term.CheckAsync(t!.Id, "rec")).Status);
        fake.Next = new(true, new TerminalResult(true, "A1B2C3", "VISA", "411111......1111", "APPROVED"));
        Assert.Equal("Approved", (await term.CheckAsync(t.Id, "rec")).Status);
        var done = (await term.ForInvoiceAsync(inv.Id)).Single();
        Assert.Equal(("Visa", "1111"), (done.CardType, done.Last4));
        Assert.Equal(2038m, BillingService.Calculate((await billing.GetAsync(inv.Id, "rec", null))!).Balance);

        // Refunds: no more than was paid, and need a reason.
        Assert.Contains("most that can be refunded", (await term.RefundAsync(t.Id, 500m, "x", "fin")).Error);
        Assert.Contains("why", (await term.RefundAsync(t.Id, 50m, " ", "fin")).Error);
        var (rf, rerr) = await term.RefundAsync(t.Id, 50m, "Dressing not used", "fin");
        Assert.Null(rerr);
        Assert.Equal("refund ST-1 50.00 key1", fake.Started[^1]);
        fake.Next = new(true, new TerminalResult(true, "R1", "VISA", "1111", "REFUND APPROVED"));
        Assert.Equal("Approved", (await term.CheckAsync(rf!.Id, "fin")).Status);
        Assert.Equal(2088m, BillingService.Calculate((await billing.GetAsync(inv.Id, "rec", null))!).Balance);
        var day = await term.SettlementAsync(new DateOnly(2026, 11, 9));
        Assert.Equal((1, 50m), (day.Refunds, day.RefundTotal));
    }

    [Fact]
    public async Task Credit_notes_reduce_the_invoice_and_go_to_xero_with_matched_contacts()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var billing = sp.GetRequiredService<BillingService>();
        var inv = await ClaimsTests.InvoiceAsync(sp, p.Id, Payer.SelfFunded, null, null);
        Assert.Contains("why", (await billing.CreditNoteAsync(inv.Id, 38m, "", "fin")).Error);
        Assert.Contains("more than the balance", (await billing.CreditNoteAsync(inv.Id, 5000m, "Goodwill", "fin")).Error);
        Assert.Contains("Credit note", await billing.RecordPaymentAsync(inv.Id, new NewPayment(new DateOnly(2026, 11, 9), 1m, PaymentMethod.CreditNote, null), "fin"));
        var (cn, _) = await billing.CreditNoteAsync(inv.Id, 38m, "Goodwill – late start", "fin");
        Assert.Equal("CN000001", cn);
        Assert.Equal(2400m, BillingService.Calculate((await billing.GetAsync(inv.Id, "fin", null))!).Balance);

        // File mode: the credit note goes as a negative line (Xero imports it as a credit note).
        var xero = sp.GetRequiredService<XeroService>();
        var today = new DateOnly(2026, 11, 9);
        Assert.Null(await xero.SaveSettingsAsync(new XeroSettingsEdit(XeroMode.File, "200", "OUTPUT2", "NONE", null, null, null, null, null), "fin"));
        var run = await xero.ExportAsync(today, today, "fin");
        var csv = Encoding.UTF8.GetString(run.File!);
        Assert.Contains("CN000001", csv, StringComparison.Ordinal);
        Assert.Contains(",-", csv, StringComparison.Ordinal);
        Assert.Equal(1, run.Export.Payments);

        // API mode: a payment, a credit note and a refund on a second invoice; contacts matched once; the refund is listed to do by hand.
        var inv2 = await ClaimsTests.InvoiceAsync(sp, p.Id, Payer.Insurer, "Southern Cross", "POL9");
        var inv3 = await ClaimsTests.InvoiceAsync(sp, p.Id, Payer.SelfFunded, null, null);
        Assert.Null(await billing.RecordPaymentAsync(inv2.Id, new NewPayment(today, 100m, PaymentMethod.Eftpos, null), "fin"));
        Assert.Null((await billing.CreditNoteAsync(inv2.Id, 10m, "Price match", "fin")).Error);
        Assert.Null(await billing.RecordRefundAsync(inv2.Id, 20m, PaymentMethod.Eftpos, "RF", "fin"));
        Assert.Null(await xero.SaveSettingsAsync(new XeroSettingsEdit(XeroMode.Api, "200", "OUTPUT2", "NONE", "090", "tenant", "client", "secret", "rt1"), "fin"));
        var fake = new ClaimsTests.FakeXero();
        var api = new XeroService(sp.GetRequiredService<ClaimsDbContext>(), sp.GetRequiredService<BillingDbContext>(), sp.GetRequiredService<PatientsDbContext>(),
            sp.GetRequiredService<ISecretProtector>(), fake, sp.GetRequiredService<AuditWriter>(), h.Clock);
        var r = await api.ExportAsync(today, today, "fin");
        Assert.Empty(r.Errors);
        Assert.Equal(2, r.Export.Invoices);
        Assert.Contains(fake.Calls, c => c == $"credit CN000002 X-{inv2.Number} 10.00 C-SX");
        Assert.Single(fake.Calls, c => c == "find Aroha Mere Ngātai");
        Assert.Single(fake.Calls, c => c.StartsWith("contact ", StringComparison.Ordinal));
        Assert.Contains(r.ByHand!, x => x.Contains("refund of 20.00", StringComparison.Ordinal));
        Assert.Empty((await api.ExportAsync(today, today, "fin")).ByHand!); // listed once
        Assert.Equal(2, await sp.GetRequiredService<ClaimsDbContext>().XeroContacts.CountAsync());
    }
}
