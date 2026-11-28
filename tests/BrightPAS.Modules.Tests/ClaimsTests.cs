using System.Text;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Claims;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BrightPAS.Modules.Tests;

/// <summary>0.26.0 Claims &amp; payments (spec 7.12, ADR 0039): approvals, claims, claim files, remittances, Xero and EFTPOS.</summary>
public class ClaimsTests
{
    internal static async Task<BillingDocument> InvoiceAsync(IServiceProvider sp, Guid patientId, Payer payer, string? payerName, string? reference)
    {
        var billing = sp.GetRequiredService<BillingService>();
        await billing.SeedExampleFeesAsync();
        var (d, errors) = await billing.CreateAsync(DocumentKind.Invoice, new NewDocument(patientId, payer, payerName, reference, "Dr Ben Hall", "Dr Kate Ford", null,
            [new("COLON", null, 1, null), new("FAC-DAY", null, 1, null), new(null, "=HYPERLINK(\"x\") pot", 2, 10m)]), "fin");
        Assert.Empty(errors);
        Assert.Null(await billing.SetStatusAsync(d!.Id, DocumentStatus.Issued, null, "fin"));
        return (await billing.GetAsync(d.Id, "fin", null))!;
    }

    [Fact]
    public async Task Insurer_claim_from_prior_approval_to_file_to_remittance_pays_the_invoice()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var claims = sp.GetRequiredService<ClaimsService>();
        await claims.SeedFundersAsync();
        await claims.SeedFundersAsync(); // idempotent
        var funders = await claims.FundersAsync(true);
        Assert.Equal(8, funders.Count);
        var sx = funders.Single(f => f.Code == "SX");
        var (_, ferr) = await claims.SaveFunderAsync(new FunderEdit(sx.Id, "sx", sx.Name, sx.Kind, SubmitBy.File, true, "claims@example.nz", "TDS-99", true, null), "fin");
        Assert.Null(ferr);
        Assert.Contains("already uses", (await claims.SaveFunderAsync(new FunderEdit(null, "SX", "Copy", FunderKind.Insurer, SubmitBy.File, false, null, null, true, null), "fin")).Error);

        // Prior approval: requested, then approved for more than the invoice.
        var (pa, perr) = await claims.CreateApprovalAsync(new ApprovalEdit(p.Id, null, sx.Id, "SX123456", null, "Colonoscopy", 2500m, null), "rec");
        Assert.Empty(perr);
        Assert.StartsWith("PA-2026-", pa!.Number, StringComparison.Ordinal);
        Assert.Null(await claims.MarkRequestedAsync(pa.Id, "rec"));
        Assert.Contains("part approved", await claims.DecideApprovalAsync(pa.Id, new ApprovalDecision(ApprovalStatus.Approved, "SXA-1", 2000m, new DateOnly(2027, 1, 31), null), "fin"));
        Assert.Null(await claims.DecideApprovalAsync(pa.Id, new ApprovalDecision(ApprovalStatus.Approved, "SXA-1", 2500m, new DateOnly(2027, 1, 31), null), "fin"));

        var inv = await InvoiceAsync(sp, p.Id, Payer.Insurer, "Southern Cross", "SX123456");
        Assert.Equal(2438m, BillingService.Calculate(inv).Total);

        // Wrong funder type refused; duplicate refused.
        var acc = funders.Single(f => f.Code == "ACC");
        Assert.Contains((await claims.CreateFromInvoiceAsync(inv.Id, acc.Id, null, null, null, "fin")).Errors, e => e.Contains("doesn't match", StringComparison.Ordinal));
        var (claim, cerr) = await claims.CreateFromInvoiceAsync(inv.Id, sx.Id, null, null, null, "fin");
        Assert.Empty(cerr);
        Assert.Equal("SX123456", claim!.MemberNumber); // taken from the invoice's payer reference
        Assert.Equal(2438m, claim.Amount);
        Assert.Equal(3, claim.Lines.Count);
        Assert.Contains((await claims.CreateFromInvoiceAsync(inv.Id, sx.Id, null, null, null, "fin")).Errors, e => e.Contains("already has a claim", StringComparison.Ordinal));

        // Not ready until the approval is attached; then ready.
        var (ready, checks, _) = await claims.MarkReadyAsync(claim.Id, null, null, null, "fin");
        Assert.False(ready);
        Assert.Contains(checks, c => !c.Ok && c.Item == "Prior approval attached" && c.Fix is not null);
        (ready, checks, _) = await claims.MarkReadyAsync(claim.Id, null, null, pa.Id, "fin");
        Assert.True(ready, string.Join("; ", checks.Where(c => !c.Ok).Select(c => c.Item)));

        // Claim file: one row per line, provider number, formula injection neutralised.
        var (batch, file, berr) = await claims.BuildBatchAsync(sx.Id, "fin");
        Assert.Null(berr);
        var csv = Encoding.UTF8.GetString(file!);
        Assert.Contains("TDS-99", csv, StringComparison.Ordinal);
        Assert.Contains(claim.Number, csv, StringComparison.Ordinal);
        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.Equal(4, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(1, batch!.Claims);
        Assert.Equal("No claims are ready for this funder.", (await claims.BuildBatchAsync(sx.Id, "fin")).Error);
        Assert.Equal(Encoding.UTF8.GetString((await claims.BatchFileAsync(batch.Id)).File!), csv);

        // Remittance: part payment, an unmatched line, then the rest; the same file can't be loaded twice.
        var r1 = await claims.ImportRemittanceAsync(sx.Id, "remit1.csv", $"Claim Reference,Amount Paid,Outcome,Reason\n{claim.Number},\"$2,000.00\",Paid,Benefit limit\nCL-2026-99999,50,Paid,\n", "fin");
        Assert.Empty(r1.Errors);
        Assert.Equal(1, r1.Remittance!.Matched);
        Assert.Contains(r1.Remittance.Lines, l => l.Result.Contains("No matching claim", StringComparison.Ordinal));
        var billing = sp.GetRequiredService<BillingService>();
        Assert.Equal(DocumentStatus.PartPaid, (await billing.GetAsync(inv.Id, "fin", null))!.Status);
        Assert.Equal(ClaimStatus.PartPaid, (await claims.GetAsync(claim.Id))!.Status);
        Assert.Contains("already been imported", (await claims.ImportRemittanceAsync(sx.Id, "again.csv", $"Claim Reference,Amount Paid,Outcome,Reason\n{claim.Number},\"$2,000.00\",Paid,Benefit limit\nCL-2026-99999,50,Paid,\n", "fin")).Errors[0]);
        await claims.ImportRemittanceAsync(sx.Id, "remit2.csv", $"ClaimNumber,Paid\n{claim.Number},438\n", "fin");
        Assert.Equal(DocumentStatus.Paid, (await billing.GetAsync(inv.Id, "fin", null))!.Status);
        Assert.Equal(ClaimStatus.Paid, (await claims.GetAsync(claim.Id))!.Status);
        Assert.Empty(await claims.AgedAsync());
    }

    [Fact]
    public async Task Acc_numbers_declines_write_offs_and_expiry()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var claims = sp.GetRequiredService<ClaimsService>();
        await claims.SeedFundersAsync();
        var acc = (await claims.FundersAsync(true)).Single(f => f.Code == "ACC");
        Assert.Contains((await claims.CreateApprovalAsync(new ApprovalEdit(p.Id, null, acc.Id, null, "bad!", "Knee arthroscopy", 3000m, null), "fin")).Errors, e => e.Contains("ACC claim number", StringComparison.Ordinal));
        var (pa, _) = await claims.CreateApprovalAsync(new ApprovalEdit(p.Id, null, acc.Id, null, "ab12345", "Knee arthroscopy", 3000m, null), "fin");
        Assert.Equal("AB12345", pa!.AccClaimNumber);
        Assert.Contains("why", await claims.DecideApprovalAsync(pa.Id, new ApprovalDecision(ApprovalStatus.Declined, null, null, null, null), "fin"));
        Assert.Null(await claims.DecideApprovalAsync(pa.Id, new ApprovalDecision(ApprovalStatus.PartApproved, "ACC-77", 2000m, new DateOnly(2026, 11, 20), null), "fin"));

        var inv = await InvoiceAsync(sp, p.Id, Payer.Acc, "ACC", "AB12345");
        var (claim, _) = await claims.CreateFromInvoiceAsync(inv.Id, acc.Id, null, null, pa.Id, "fin");
        Assert.Equal("AB12345", claim!.AccClaimNumber);
        var checks = await claims.ChecksAsync(claim.Id);
        Assert.Contains(checks, c => c.Item == "Within the approved amount" && !c.Ok); // 2438 > 2000 approved

        // Portal route: ready is refused until fixed; write-off needs a reason and leaves the invoice owing.
        Assert.Equal("Only a ready claim can be marked as sent.", await claims.MarkSubmittedAsync(claim.Id, "fin"));
        Assert.NotNull(await claims.WriteOffAsync(claim.Id, "", "fin"));
        Assert.Null(await claims.WriteOffAsync(claim.Id, "Patient to pay the difference; reclaiming 2000", "fin"));
        Assert.Empty((await claims.CreateFromInvoiceAsync(inv.Id, acc.Id, null, null, pa.Id, "fin")).Errors); // a new claim once the old one is closed

        // Expiry after the valid-until date (NZ date).
        h.Clock.Advance(TimeSpan.FromDays(12));
        Assert.Equal(1, await claims.ExpireApprovalsAsync());
        Assert.Equal(ApprovalStatus.Expired, (await claims.ApprovalsAsync(p.Id, null, false)).Single().Status);
    }

    internal sealed class FakeXero : IXeroApi
    {
        public List<string> Calls { get; } = [];
        public Task<XeroTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct)
        {
            Calls.Add($"refresh {clientId} {clientSecret} {refreshToken}");
            return Task.FromResult(new XeroTokens("access", refreshToken + "+"));
        }
        public Task<string> CreateInvoiceAsync(string accessToken, string tenantId, XeroInvoice invoice, CancellationToken ct)
        {
            Calls.Add($"invoice {tenantId} {invoice.InvoiceNumber} {invoice.ContactName} {invoice.Lines.Count}");
            return Task.FromResult("X-" + invoice.InvoiceNumber);
        }
        public Task<string> CreatePaymentAsync(string accessToken, string tenantId, XeroPayment payment, CancellationToken ct)
        {
            Calls.Add($"payment {payment.InvoiceXeroId} {payment.Amount:0.00}");
            return Task.FromResult("P-1");
        }
        public Task<string?> FindContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct)
        {
            Calls.Add($"find {name}");
            return Task.FromResult<string?>(name == "Southern Cross" ? "C-SX" : null);
        }
        public Task<string> CreateContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct)
        {
            Calls.Add($"contact {name}");
            return Task.FromResult("C-" + name.Length);
        }
        public Task<string> CreateCreditNoteAsync(string accessToken, string tenantId, XeroCreditNote note, CancellationToken ct)
        {
            Calls.Add($"credit {note.Number} {note.InvoiceXeroId} {note.Amount:0.00} {note.ContactId}");
            return Task.FromResult("CN-1");
        }
    }

    [Fact]
    public async Task Xero_file_export_once_then_api_mode_sends_invoices_and_payments_and_rotates_the_token()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var inv = await InvoiceAsync(sp, p.Id, Payer.SelfFunded, null, null);
        var xero = sp.GetRequiredService<XeroService>();
        var today = new DateOnly(2026, 11, 9);
        Assert.Contains("turned off", (await xero.ExportAsync(today, today, "fin")).Errors[0]);
        Assert.Null(await xero.SaveSettingsAsync(new XeroSettingsEdit(XeroMode.File, "200", "OUTPUT2", "NONE", null, null, null, null, null), "fin"));
        var run = await xero.ExportAsync(today.AddDays(-1), today, "fin");
        var csv = Encoding.UTF8.GetString(run.File!);
        Assert.Contains("*ContactName", csv, StringComparison.Ordinal);
        Assert.Contains("Aroha Mere Ngātai", csv, StringComparison.Ordinal);
        Assert.Contains(inv.Number!, csv, StringComparison.Ordinal);
        Assert.Contains("9/11/2026", csv, StringComparison.Ordinal);
        Assert.Contains("OUTPUT2", csv, StringComparison.Ordinal);
        Assert.Contains("everything in this period", (await xero.ExportAsync(today.AddDays(-1), today, "fin")).Errors[0]);
        Assert.Equal(csv, Encoding.UTF8.GetString((await xero.ExportFileAsync(run.Export.Id)).File!));

        // API mode with a fake Xero: a second invoice goes, with its payment; the refresh token is rotated and stored protected.
        var inv2 = await InvoiceAsync(sp, p.Id, Payer.Insurer, "Southern Cross", "POL9");
        Assert.Null(await sp.GetRequiredService<BillingService>().RecordPaymentAsync(inv2.Id, new NewPayment(today, 100m, PaymentMethod.Eftpos, null), "fin"));
        Assert.NotNull(await xero.SaveSettingsAsync(new XeroSettingsEdit(XeroMode.Api, "200", "OUTPUT2", "NONE", null, "tenant", "client", "secret", "rt1"), "fin"));
        Assert.Null(await xero.SaveSettingsAsync(new XeroSettingsEdit(XeroMode.Api, "200", "OUTPUT2", "NONE", "090", "tenant", "client", "secret", "rt1"), "fin"));
        var fake = new FakeXero();
        var api = new XeroService(sp.GetRequiredService<ClaimsDbContext>(), sp.GetRequiredService<BillingDbContext>(), sp.GetRequiredService<PatientsDbContext>(),
            sp.GetRequiredService<ISecretProtector>(), fake, sp.GetRequiredService<AuditWriter>(), h.Clock);
        var r = await api.ExportAsync(today.AddDays(-1), today, "fin");
        Assert.Empty(r.Errors);
        Assert.Equal(1, r.Export.Invoices);
        Assert.Equal(1, r.Export.Payments);
        Assert.Contains(fake.Calls, c => c == "refresh client secret rt1");
        Assert.Contains(fake.Calls, c => c.StartsWith($"invoice tenant {inv2.Number} Southern Cross 3", StringComparison.Ordinal));
        Assert.Contains(fake.Calls, c => c == $"payment X-{inv2.Number} 100.00");
        var stored = await sp.GetRequiredService<ClaimsDbContext>().Xero.AsNoTracking().SingleAsync();
        Assert.Equal("rt1+", sp.GetRequiredService<ISecretProtector>().Unprotect(stored.RefreshTokenProtected));
        Assert.DoesNotContain("rt1", stored.RefreshTokenProtected!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Eftpos_simulator_and_manual_results_record_payments_never_card_numbers()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var inv = await InvoiceAsync(sp, p.Id, Payer.SelfFunded, null, null);
        var term = sp.GetRequiredService<TerminalService>();
        var billing = sp.GetRequiredService<BillingService>();

        // Manual terminal: waiting until staff record the receipt; a full card number is refused.
        var (t, err) = await term.StartAsync(inv.Id, 400m, "rec");
        Assert.Null(err);
        Assert.Equal(TerminalStatus.Pending, t!.Status);
        Assert.Contains("already waiting", (await term.StartAsync(inv.Id, 10m, "rec")).Error);
        Assert.Contains("last four digits", await term.CompleteAsync(t.Id, new TerminalResult(true, "123456", "Visa", "4111111111111111", null), "rec"));
        Assert.Contains("authorisation code", await term.CompleteAsync(t.Id, new TerminalResult(true, null, "Visa", "1111", null), "rec"));
        Assert.Null(await term.CompleteAsync(t.Id, new TerminalResult(true, "123456", "Visa", "1111", null), "rec"));
        Assert.Equal(2038m, BillingService.Calculate((await billing.GetAsync(inv.Id, "rec", null))!).Balance);

        // Simulator: .51 declines (no payment), otherwise approved and recorded.
        Assert.Null(await term.SaveSettingsAsync(TerminalProvider.Simulator, "T-01", "admin"));
        var (d, _) = await term.StartAsync(inv.Id, 100.51m, "rec");
        Assert.Equal(TerminalStatus.Declined, d!.Status);
        var (ok, _) = await term.StartAsync(inv.Id, 38m, "rec");
        Assert.Equal(TerminalStatus.Approved, ok!.Status);
        Assert.True(ok.PaymentRecorded);
        Assert.Contains("more than the balance", (await term.StartAsync(inv.Id, 5000m, "rec")).Error);

        var day = await term.SettlementAsync(new DateOnly(2026, 11, 9));
        Assert.Equal(2, day.Approved);
        Assert.Equal(438m, day.Total);
        Assert.Equal(1, day.Declined);
        Assert.All(day.Transactions, x => Assert.True(x.Last4 is null || x.Last4.Length == 4));
    }
}
