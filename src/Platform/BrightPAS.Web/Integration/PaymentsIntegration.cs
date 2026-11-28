using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BrightPAS.Licensing;
using BrightPAS.Modules.Payments;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Windcave REST API, hosted payment page (https://{uat|sec}.windcave.com/api/v1/sessions). BrightPAS creates a purchase session and sends the patient
/// to Windcave's page; afterwards it reads the session back from Windcave to learn the result (never trusting the browser's return address).
/// </summary>
public sealed class HttpWindcaveApi(IHttpClientFactory http) : IWindcaveApi
{
    private static HttpRequestMessage Req(HttpMethod m, string url, string user, string key, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        if (body is not null) r.Content = JsonContent.Create(body);
        r.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{key}")));
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return r;
    }

    public async Task<GatewayStart> CreateSessionAsync(string baseUrl, string user, string key, decimal amount, string merchantReference, string returnUrl, string notificationUrl, CancellationToken ct)
    {
        using var c = http.CreateClient("windcave");
        using var req = Req(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/v1/sessions", user, key, new
        {
            type = "purchase",
            amount = amount.ToString("0.00", CultureInfo.InvariantCulture),
            currency = "NZD",
            merchantReference,
            callbackUrls = new { approved = returnUrl, declined = returnUrl, cancelled = returnUrl },
            notificationUrl,
        });
        using var res = await c.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return new(null, null, $"Windcave answered {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.TryGetProperty("id", out var i) ? i.GetString() : null;
        string? hpp = null;
        if (doc.RootElement.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
            foreach (var l in links.EnumerateArray())
                if (l.TryGetProperty("rel", out var rel) && rel.GetString() == "hpp" && l.TryGetProperty("href", out var href)) hpp = href.GetString();
        return hpp is not null && Uri.TryCreate(hpp, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? new(hpp, id, null) : new(null, id, "Windcave did not return a payment page.");
    }

    public async Task<GatewayResult> QuerySessionAsync(string baseUrl, string user, string key, string sessionId, CancellationToken ct)
    {
        using var c = http.CreateClient("windcave");
        using var req = Req(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/api/v1/sessions/{Uri.EscapeDataString(sessionId)}", user, key);
        using var res = await c.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Windcave answered {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return Read(doc.RootElement);
    }

    /// <summary>Reads a Windcave session: no transaction yet = not complete; the last transaction says approved or declined.</summary>
    public static GatewayResult Read(JsonElement session)
    {
        var state = session.TryGetProperty("state", out var s) ? s.GetString() : null;
        if (!session.TryGetProperty("transactions", out var txns) || txns.ValueKind != JsonValueKind.Array || txns.GetArrayLength() == 0)
            return state is "cancelled" or "expired" ? new(true, false, true, null, null, null, $"Payment page {state}", null) : new(false, false, false, null, null, null, null, null);
        var t = txns[txns.GetArrayLength() - 1];
        var approved = t.TryGetProperty("authorised", out var a) && a.ValueKind == JsonValueKind.True;
        string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        decimal? amount = decimal.TryParse(Str(t, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amt) ? amt : null;
        string? cardType = null, last4 = null;
        if (t.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object)
        {
            cardType = Str(card, "type");
            var number = Str(card, "cardNumber");
            if (number is { Length: >= 4 } && number[^4..].All(char.IsAsciiDigit)) last4 = number[^4..];
        }
        return new(true, approved, false, cardType, last4, Str(t, "authCode"), Str(t, "responseText") ?? Str(t, "reCo"), amount, Str(t, "id"));
    }

    /// <summary>Refund transaction (0.32.0): POST /api/v1/transactions type "refund" against the original transaction id.</summary>
    public async Task<GatewayRefund> RefundAsync(string baseUrl, string user, string key, string transactionId, decimal amount, string merchantReference, CancellationToken ct)
    {
        using var c = http.CreateClient("windcave");
        using var req = Req(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/v1/transactions", user, key, new
        {
            type = "refund",
            amount = amount.ToString("0.00", CultureInfo.InvariantCulture),
            currency = "NZD",
            merchantReference,
            transactionId,
        });
        using var res = await c.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode && (int)res.StatusCode != 202) return new(false, null, $"Windcave answered {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var ok = root.TryGetProperty("authorised", out var a) && a.ValueKind == JsonValueKind.True;
        var id = root.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
        var msg = root.TryGetProperty("responseText", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
        return ok ? new(true, id, null) : new(false, id, msg ?? "Windcave declined the refund.");
    }
}

/// <summary>Every 10 minutes: asks the provider about payments the patient started but BrightPAS has not heard back about (browser closed before returning).</summary>
public sealed class PaymentsWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<PaymentsWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        do
        {
            if (licence.Status.AccessTo(PaymentsModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<PaymentLinkService>().CheckUnfinishedAsync(stoppingToken);
                if (n > 0) log.LogInformation("Online payments: {Count} unfinished payment(s) settled.", n);
                var r = await scope.ServiceProvider.GetRequiredService<AutomationService>().RunAsync(false, ct: stoppingToken);
                if (r.Ran) log.LogInformation("Accounts round: {Statements} statements, {Overdue} overdue reminders, {Plans} plan reminders.", r.StatementCount, r.OverdueSent, r.PlanReminders);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Online payment check failed; it will run again in 10 minutes."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
