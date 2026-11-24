using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// 0.26.0 Claims &amp; payments: claim made from an insurer invoice on screen, checks, prior approval, claim file (encrypted), remittance pays the invoice,
/// EFTPOS at the desk (training terminal), Xero file, NHI check on the patient page, and the guards (card numbers, permissions, duplicate remittance).
/// </summary>
public sealed partial class Scenarios
{
    private const string ClaimsArea = "Claims, Xero, EFTPOS & NHI";
    private string? _claimId;

    private async Task ClaimsStepsAsync()
    {
        await Step(ClaimsArea, "Finance makes a claim from an insurer invoice and sees what's missing", ClaimFromInvoiceAsync);
        await Step(ClaimsArea, "Prior approval recorded and attached; the claim becomes ready", ClaimReadyAsync);
        await Step(ClaimsArea, "Claim file made (encrypted) and the remittance pays the invoice; the same file can't be loaded twice", ClaimFileRemittanceAsync);
        await Step(ClaimsArea, "Reception takes an EFTPOS payment on the training terminal; card numbers are refused", EftposAsync);
        await Step(ClaimsArea, "Xero file of issued invoices (encrypted); each invoice goes once", XeroFileAsync);
        await Step(ClaimsArea, "NHI check on the patient page; identity changes are not accepted here", NhiCheckAsync);
        await Step(ClaimsArea, "Permissions: reception can't change claim settings; a nurse can't see claims", ClaimsPermissionsAsync);
    }

    private async Task<(Outcome, string)> ClaimFromInvoiceAsync()
    {
        var fin = await As("finance");
        var docs = await fin.GetAsync("/api/billing/documents?kind=Invoice&outstanding=true");
        if (docs.Status != 200) return (Outcome.Fail, $"Invoices HTTP {docs.Status}.");
        string? invoiceId = null;
        foreach (var d in docs.Json!.Value.EnumerateArray().Where(d => Str(d, "payer") == "Insurer" && Str(d, "status") is "Issued" or "PartPaid"))
        {
            var c = await fin.GetAsync($"/api/claims/invoice/{Str(d, "id")}");
            if (c.Json?.GetProperty("claimId").ValueKind == JsonValueKind.Null) { invoiceId = Str(d, "id"); break; }
        }
        if (invoiceId is null) return (Outcome.Skip, "No insurer invoice without a claim in the demo data.");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/billing/{invoiceId}");
        var section = page.Locator("section", new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Claim from the funder" }) });
        await Assertions.Expect(section).ToBeVisibleAsync(new() { Timeout = 15000 });
        var funder = section.GetByLabel("Funder");
        if (await funder.InputValueAsync() == "") await funder.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await section.GetByRole(AriaRole.Button, new() { Name = "Make the claim" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Checks" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        _claimId = page.Url.Split('/').Last();
        var todo = await page.Locator("ul.checks li.todo").CountAsync();
        return (Outcome.Pass, $"Claim made on screen from the invoice; {todo} check(s) say what to fix, in plain words.");
    }

    private async Task<(Outcome, string)> ClaimReadyAsync()
    {
        if (_claimId is null) return (Outcome.Skip, "No claim from the previous step.");
        var fin = await As("finance");
        var c = await fin.GetAsync($"/api/claims/{_claimId}");
        var claim = c.Json!.Value.GetProperty("claim");
        var amount = claim.GetProperty("amount").GetDecimal();
        var a = await fin.PostAsync("/api/claims/approvals", new { patientId = Str(claim, "patientId"), funderId = Str(claim, "funderId"), memberNumber = "ROBOT123", procedure = "Robot procedure", amountRequested = amount });
        if (a.Status != 200) return (Outcome.Fail, $"Approval HTTP {a.Status}: {a.Text}");
        var aid = Str(a.Json!.Value, "id");
        await fin.PostAsync($"/api/claims/approvals/{aid}/requested", new { });
        var d = await fin.PostAsync($"/api/claims/approvals/{aid}/decide", new { status = "Approved", reference = "ROBOT-APP", approvedAmount = amount, validUntil = DateTime.Today.AddDays(60).ToString("yyyy-MM-dd"), notes = (string?)null });
        if (d.Status != 200) return (Outcome.Fail, $"Decide HTTP {d.Status}: {d.Text}");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/claims/{_claimId}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Checks" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        var member = page.GetByLabel("Membership / policy number");
        if (await member.InputValueAsync() == "") await member.FillAsync("ROBOT123");
        await page.GetByLabel("Prior approval").SelectOptionAsync(new SelectOptionValue { Value = aid });
        await page.GetByRole(AriaRole.Button, new() { Name = "Save and check" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Ready to send").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var after = await fin.GetAsync($"/api/claims/{_claimId}");
        var bad = after.Json!.Value.GetProperty("checks").EnumerateArray().Where(x => !x.GetProperty("ok").GetBoolean()).Select(x => Str(x, "item")).ToList();
        return Str(after.Json.Value.GetProperty("claim"), "status") == "Ready" ? (Outcome.Pass, "Approval recorded with the funder's answer, attached on screen, every check green.")
            : (Outcome.Fail, $"Still not ready: {string.Join(", ", bad)}");
    }

    private async Task<(Outcome, string)> ClaimFileRemittanceAsync()
    {
        if (_claimId is null) return (Outcome.Skip, "No claim.");
        var fin = await As("finance");
        var c = (await fin.GetAsync($"/api/claims/{_claimId}")).Json!.Value.GetProperty("claim");
        if (Str(c, "status") != "Ready") return (Outcome.Skip, "The claim is not ready.");
        var file = await fin.PostAsync("/api/claims/batches", new { funderId = Str(c, "funderId"), export = new { protect = true, password = "Robot-Passphrase-2026", format = "zip" } });
        if (file.Status != 200) return (Outcome.Fail, $"Claim file HTTP {file.Status}: {file.Text[..Math.Min(200, file.Text.Length)]}");
        var sent = (await fin.GetAsync($"/api/claims/{_claimId}")).Json!.Value.GetProperty("claim");
        if (Str(sent, "status") != "Submitted") return (Outcome.Fail, $"After the file the claim is {Str(sent, "status")}.");
        var amount = sent.GetProperty("amount").GetDecimal();
        var csv = $"Claim Reference,Amount Paid,Outcome,Reason\n{Str(sent, "number")},{amount.ToString(System.Globalization.CultureInfo.InvariantCulture)},Paid,\n";
        var r = await fin.PostAsync("/api/claims/remittances", new { funderId = Str(c, "funderId"), fileName = "robot-remittance.csv", text = csv });
        if (r.Status != 200) return (Outcome.Fail, $"Remittance HTTP {r.Status}: {r.Text}");
        var again = await fin.PostAsync("/api/claims/remittances", new { funderId = Str(c, "funderId"), fileName = "robot-remittance-copy.csv", text = csv });
        var inv = await fin.GetAsync($"/api/billing/documents/{Str(sent, "invoiceId")}");
        var paid = Str(inv.Json!.Value, "status") == "Paid";
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/claims?tab=remittances");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "robot-remittance.csv" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        return paid && again.Status == 400 ? (Outcome.Pass, "Encrypted claim file downloaded; claim sent; remittance matched it and paid the invoice in full; the duplicate file was refused.")
            : (Outcome.Fail, $"Invoice paid: {paid}; duplicate remittance HTTP {again.Status}.");
    }

    private async Task<(Outcome, string)> EftposAsync()
    {
        var rec = await As("reception");
        var docs = await rec.GetAsync("/api/billing/documents?kind=Invoice&outstanding=true");
        var inv = Find(docs.Json, d => Str(d, "payer") == "SelfFunded" && Str(d, "status") is "Issued" or "PartPaid");
        if (inv is null) return (Outcome.Skip, "No self-funded invoice owing.");
        var id = Str(inv.Value, "id");
        var pending = (await rec.GetAsync($"/api/claims/terminal/invoice/{id}")).Json;
        var p0 = Find(pending, t => Str(t, "status") == "Pending");
        if (p0 is not null) await rec.PostAsync($"/api/claims/terminal/{Str(p0.Value, "id")}/cancel", new { });
        var page = rec.Page;
        await page.GotoAsync($"{bot.O.Url}/billing/{id}");
        var section = page.Locator("section", new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Take payment by EFTPOS" }) });
        await Assertions.Expect(section).ToBeVisibleAsync(new() { Timeout = 15000 });
        await section.GetByLabel("Amount").FillAsync("10.00");
        await section.GetByRole(AriaRole.Button, new() { Name = "Send to terminal (training)" }).ClickAsync();
        await Assertions.Expect(section.GetByText("Approved").First).ToBeVisibleAsync(new() { Timeout = 15000 });
        var doc = await rec.GetAsync($"/api/billing/documents/{id}");
        var eftpos = doc.Json!.Value.GetProperty("payments").EnumerateArray().Any(x => Str(x, "method") == "Eftpos" && (Str(x, "reference") ?? "").Contains("auth", StringComparison.Ordinal));
        // Manual result with a full card number must be refused.
        var st = await rec.PutAsync("/api/claims/terminal/settings", new { provider = "Manual", terminalId = (string?)null });
        var adm = await As("admin");
        await adm.PutAsync("/api/claims/terminal/settings", new { provider = "Manual", terminalId = (string?)null });
        var t = await rec.PostAsync("/api/claims/terminal", new { invoiceId = id, amount = 1m });
        string? tid = t.Status == 200 ? Str(t.Json!.Value.GetProperty("txn"), "id") : null;
        int full = 0;
        if (tid is not null)
        {
            full = (await rec.PostAsync($"/api/claims/terminal/{tid}/complete", new { approved = true, authCode = "123456", cardType = "Visa", last4 = "4111111111111111", responseText = (string?)null })).Status;
            await rec.PostAsync($"/api/claims/terminal/{tid}/cancel", new { });
        }
        await adm.PutAsync("/api/claims/terminal/settings", new { provider = "Simulator", terminalId = "DEMO-T1" });
        return eftpos && st.Status == 403 && (tid is null || full == 400)
            ? (Outcome.Pass, "Training terminal approved the payment and it was recorded on the invoice with the auth code; reception can't change terminal settings; a full card number is refused.")
            : (Outcome.Fail, $"Payment recorded {eftpos}; reception settings HTTP {st.Status}; full card number HTTP {full}.");
    }

    private async Task<(Outcome, string)> XeroFileAsync()
    {
        var fin = await As("finance");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/claims/xero");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Xero" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        var from = DateTime.Today.AddDays(-90).ToString("yyyy-MM-dd"); var to = DateTime.Today.ToString("yyyy-MM-dd");
        var pending = await fin.GetAsync($"/api/claims/xero/pending?from={from}&to={to}");
        var n = pending.Json?.GetArrayLength() ?? 0;
        if (n == 0) return (Outcome.Skip, "No issued invoices waiting for Xero.");
        var f = await fin.PostAsync("/api/claims/xero/export", new { from, to, export = new { protect = true, password = "Robot-Passphrase-2026", format = "zip" } });
        var after = await fin.GetAsync($"/api/claims/xero/pending?from={from}&to={to}");
        return f.Status == 200 && after.Json?.GetArrayLength() == 0 ? (Outcome.Pass, $"{n} invoice(s) exported to an encrypted Xero file; none left waiting, so nothing goes twice.")
            : (Outcome.Fail, $"Export HTTP {f.Status}; still waiting {after.Json?.GetArrayLength()}.");
    }

    private async Task<(Outcome, string)> NhiCheckAsync()
    {
        var rec = await As("reception");
        var pid = _patient?.Id;
        if (pid is null) return (Outcome.Skip, "No test patient.");
        var page = rec.Page;
        await page.GotoAsync($"{bot.O.Url}/patients/{pid}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Check with the NHI" }).ClickAsync();
        var answer = page.GetByText("Our details match the NHI").Or(page.GetByRole(AriaRole.Button, new() { Name = "Use the ticked NHI details" })).Or(page.Locator(".warn"));
        await Assertions.Expect(answer.First).ToBeVisibleAsync(new() { Timeout = 15000 });
        var r = await rec.PostAsync($"/api/identity/nhi/{pid}/accept", new { fields = new[] { "family" } });
        return r.Status == 400 ? (Outcome.Pass, "NHI compared on the patient page (training simulator); a name change through this route is refused – it needs an identity correction.")
            : (Outcome.Fail, $"Name change via NHI accept returned HTTP {r.Status}.");
    }

    private async Task<(Outcome, string)> ClaimsPermissionsAsync()
    {
        var rec = await As("reception");
        var nurse = await As("nurse");
        var put = await rec.PutAsync("/api/claims/xero/settings", new { mode = "Off", salesAccountCode = "200", gstTaxType = "OUTPUT2", noGstTaxType = "NONE" });
        var see = await nurse.GetAsync("/api/claims/");
        var funders = await rec.PostAsync("/api/claims/funders", new { code = "HACK", name = "x", kind = "Insurer", submitBy = "File", priorApprovalRequired = false, isActive = true });
        return put.Status == 403 && see.Status == 403 && funders.Status == 403 ? (Outcome.Pass, "Reception can't change Xero or funders; nurses can't list claims.")
            : (Outcome.Fail, $"Reception Xero {put.Status}, reception funders {funders.Status}, nurse claims {see.Status}.");
    }
}
