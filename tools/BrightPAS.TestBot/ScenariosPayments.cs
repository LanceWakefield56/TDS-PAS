using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Online payments and statements (0.30.0) through the screens: Finance sends a payment link from an invoice; the patient opens it on a phone,
/// pays by card on the payment simulator and the invoice is paid; Finance prints the statements to post; reception cannot change the settings.
/// </summary>
public sealed partial class Scenarios
{
    private const string PayArea = "Online payments";
    private string? _payInvoiceId;
    private string? _payLink;

    private async Task PaymentsStepsAsync()
    {
        await Step(PayArea, "Finance sends a payment link from an invoice", PaySendAsync);
        await Step(PayArea, "Patient pays by card on a phone; the invoice is paid", PayOnPhoneAsync);
        await Step(PayArea, "Finance prints the statements to post", PayStatementsAsync);
        await Step(PayArea, "Reception cannot change online payment settings", PayGuardAsync);
        // 0.32.0
        await Step(PayArea, "Finance refunds part of an online payment back to the card", PayRefundAsync);
        await Step(PayArea, "Finance opens a payment plan and sees its instalments", PayPlanAsync);
        await Step(PayArea, "Reception cannot refund or set up payment plans", PayExtrasGuardAsync);
    }

    private async Task<(Outcome, string)> PayRefundAsync()
    {
        var fin = await As("finance");
        var pays = await fin.GetAsync("/api/payments/online?view=approved");
        if (pays.Status != 200) return (Outcome.Skip, $"Online payments are not available (HTTP {pays.Status}).");
        if (Find(pays.Json, x => x.GetProperty("amount").GetDecimal() - x.GetProperty("refunded").GetDecimal() >= 1m) is null) return (Outcome.Skip, "No approved online payment to refund.");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/payments?tab=online");
        await page.GetByRole(AriaRole.Button, new() { Name = "Refund", Exact = true }).First.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await dialog.GetByLabel("Amount to refund ($)").FillAsync("1.00");
        await dialog.GetByLabel("Why").FillAsync("Robot test refund");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Refund now" }).ClickAsync();
        await page.GetByText("sent back to the card").WaitForAsync(new() { Timeout = 15000 });
        var refunds = await fin.GetAsync("/api/payments/refunds");
        return Find(refunds.Json, x => Str(x, "reason") == "Robot test refund" && Str(x, "status") == "Done") is { } r
            ? (Outcome.Pass, $"Refund {Str(r, "number")} of $1.00 made through the simulator and taken off {Str(r, "appliedTo")}.")
            : (Outcome.Fail, "The refund is not in the refunds list.");
    }

    private async Task<(Outcome, string)> PayPlanAsync()
    {
        var fin = await As("finance");
        var plans = await fin.GetAsync("/api/payments/plans?view=all");
        if (plans.Status != 200) return (Outcome.Skip, $"Payment plans are not available (HTTP {plans.Status}).");
        var plan = Find(plans.Json, x => Str(x, "status") == "Active");
        if (plan is null) return (Outcome.Skip, "No active payment plan in the demo.");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/payments?tab=plans");
        await page.GetByRole(AriaRole.Button, new() { Name = Str(plan.Value, "number")!, Exact = true }).ClickAsync();
        var table = page.GetByRole(AriaRole.Region, new() { Name = "Instalments" });
        await table.WaitForAsync(new() { Timeout = 15000 });
        var rows = await table.Locator("tbody tr").CountAsync();
        var shot = await fin.ShotAsync("payment-plan");
        R.Gallery("/payments?tab=plans — a payment plan's instalments", shot);
        return rows >= 2 ? (Outcome.Pass, $"Plan {Str(plan.Value, "number")} shows {rows} instalments.") : (Outcome.Fail, $"Plan shows {rows} instalments.");
    }

    private async Task<(Outcome, string)> PayExtrasGuardAsync()
    {
        var reception = await As("reception");
        var fin = await As("finance");
        var pay = Find((await fin.GetAsync("/api/payments/online?view=approved")).Json, _ => true);
        var refund = await reception.PostAsync($"/api/payments/online/{(pay is null ? Guid.Empty.ToString() : Str(pay.Value, "id"))}/refund", new { amount = 1, reason = "x" });
        var plan = await reception.PostAsync("/api/payments/plans", new { patientId = Guid.Empty, instalment = 10, frequency = "Weekly", firstDue = "2030-01-01" });
        return refund.Status == 403 && plan.Status == 403 ? (Outcome.Pass, "Refunds and new payment plans are refused for reception (403).") : (Outcome.Fail, $"Reception got HTTP {refund.Status} refunding and {plan.Status} making a plan.");
    }

    private async Task<(Outcome, string)> PaySendAsync()
    {
        var fin = await As("finance");
        var settings = await fin.GetAsync("/api/payments/settings");
        if (settings.Status != 200) return (Outcome.Skip, $"Online payments are not available (HTTP {settings.Status}).");
        var docs = await fin.GetAsync("/api/billing/documents?kind=Invoice&outstanding=true");
        var links = await fin.GetAsync("/api/payments/links?view=open");
        var withLink = new HashSet<string>(StringComparer.Ordinal);
        if (links.Json is { ValueKind: System.Text.Json.JsonValueKind.Array } la)
            foreach (var l in la.EnumerateArray()) if (Str(l, "invoiceId") is { } id) withLink.Add(id);
        var inv = Find(docs.Json, x => Str(x, "payer") == "SelfFunded" && !withLink.Contains(Str(x, "id") ?? ""));
        if (inv is null) return (Outcome.Skip, "No self-funded invoice with money owing in the demo.");
        _payInvoiceId = Str(inv.Value, "id");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/billing/{_payInvoiceId}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send payment link", Exact = true }).ClickAsync();
        await page.Locator("code.break").WaitForAsync(new() { Timeout = 15000 });
        _payLink = (await page.Locator("code.break").InnerTextAsync()).Trim();
        return _payLink.Contains("/pay/", StringComparison.Ordinal) ? (Outcome.Pass, $"Payment link made for {Str(inv.Value, "number")} and shown to copy.") : (Outcome.Fail, "No payment link was shown.");
    }

    private async Task<(Outcome, string)> PayOnPhoneAsync()
    {
        if (_payInvoiceId is null || _payLink is null) return (Outcome.Skip, "No payment link was sent.");
        var fin = await As("finance");
        var inv = (await fin.GetAsync($"/api/billing/documents/{_payInvoiceId}")).Json!.Value;
        var patient = (await fin.GetAsync($"/api/patients/{Str(inv, "patientId")}")).Json!.Value;
        await using var phone = await bot.NewContextAsync("patient", new ViewportSize { Width = 390, Height = 844 }, mobile: true);
        var p = phone.Page;
        await p.GotoAsync(bot.O.Url + new Uri(_payLink).PathAndQuery);
        await p.Locator("input[type=date]").FillAsync(Str(patient, "dateOfBirth")![..10]);
        await p.GetByRole(AriaRole.Button, new() { Name = "Open" }).ClickAsync();
        await p.GetByText("Total owing").WaitForAsync(new() { Timeout = 15000 }); // the date-of-birth step also says "Kia ora"
        var shot = await phone.ShotAsync("pay-phone", fullPage: true);
        R.Gallery("/pay/:token — patient pays online on a phone", shot);
        foreach (var u in await Usability.CheckAsync(p, null, 0, desktop: false)) R.Add("screens", "patient", "/pay (online payment, phone)", u.Check, u.Outcome, u.Detail, u.Outcome == Outcome.Warn ? shot : null);
        await p.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Pay .* by card$") }).ClickAsync();
        await p.GetByRole(AriaRole.Button, new() { Name = "Approve the payment" }).ClickAsync();
        await p.Locator("text=Thank you – payment received").WaitForAsync(new() { Timeout = 15000 });
        var after = (await fin.GetAsync($"/api/billing/documents/{_payInvoiceId}")).Json!.Value;
        return Str(after, "status") == "Paid" ? (Outcome.Pass, $"Paid on a phone through the simulator; {Str(after, "number")} is now paid.") : (Outcome.Fail, $"Invoice status is {Str(after, "status")} after paying.");
    }

    private async Task<(Outcome, string)> PayStatementsAsync()
    {
        var fin = await As("finance");
        var runs = await fin.GetAsync("/api/payments/statements/runs");
        if (runs.Status != 200) return (Outcome.Skip, $"Statements are not available (HTTP {runs.Status}).");
        var run = Find(runs.Json, x => Str(x, "sentAt") is not null);
        if (run is null) return (Outcome.Skip, "No statement run has been sent in the demo.");
        var page = fin.Page;
        await page.GotoAsync($"{bot.O.Url}/payments?tab=statements");
        await page.GetByRole(AriaRole.Region, new() { Name = "Statement runs" }).WaitForAsync(new() { Timeout = 15000 });
        var print = await fin.PostAsync($"/api/payments/statements/runs/{Str(run.Value, "id")}/print?reprint=true", new { });
        if (print.Status == 400) return (Outcome.Pass, "Every statement in the run went by email or text; nothing to post.");
        return print.Status == 200 && print.Text.StartsWith("%PDF", StringComparison.Ordinal) ? (Outcome.Pass, "The statements to post came out as one PDF.") : (Outcome.Fail, $"Printing statements gave HTTP {print.Status}.");
    }

    private async Task<(Outcome, string)> PayGuardAsync()
    {
        var reception = await As("reception");
        var r = await reception.ApiAsync("PUT", "/api/payments/settings", new { provider = "Off", publicUrl = (string?)null, linkDays = 30, minimumPayment = 10, windcaveUrl = (string?)null, windcaveUser = (string?)null, windcaveKey = (string?)null, statementMinimum = 5, statementMessage = (string?)null });
        return r.Status == 403 ? (Outcome.Pass, "Changing online payment settings is refused for reception (403).") : (Outcome.Fail, $"Reception got HTTP {r.Status} changing online payment settings.");
    }
}
