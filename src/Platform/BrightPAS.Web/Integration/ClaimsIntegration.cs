using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BrightPAS.Licensing;
using BrightPAS.Modules.Claims;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>Xero Accounting API over HTTPS (identity.xero.com token endpoint, api.xero.com/api.xro/2.0).</summary>
public sealed class HttpXeroApi(IHttpClientFactory http) : IXeroApi
{
    public const string TokenUrl = "https://identity.xero.com/connect/token";
    public const string ApiUrl = "https://api.xero.com/api.xro/2.0/";

    public async Task<XeroTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct)
    {
        using var c = http.CreateClient("xero");
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        using var res = await c.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Xero refused the connection ({(int)res.StatusCode}) – reconnect to Xero.");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return new(doc.RootElement.GetProperty("access_token").GetString()!, doc.RootElement.GetProperty("refresh_token").GetString()!);
    }

    private static HttpRequestMessage Req(string path, string token, string tenant, object body)
    {
        var r = new HttpRequestMessage(HttpMethod.Put, ApiUrl + path) { Content = JsonContent.Create(body) };
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        r.Headers.Add("xero-tenant-id", tenant);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return r;
    }

    private static async Task<JsonDocument> SendAsync(HttpClient c, HttpRequestMessage r, CancellationToken ct)
    {
        using var res = await c.SendAsync(r, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var msg = text.Length > 300 ? text[..300] : text;
            throw new InvalidOperationException($"Xero said {(int)res.StatusCode}: {msg}");
        }
        return JsonDocument.Parse(text);
    }

    public async Task<string> CreateInvoiceAsync(string accessToken, string tenantId, XeroInvoice i, CancellationToken ct)
    {
        using var c = http.CreateClient("xero");
        var body = new
        {
            Invoices = new[]
            {
                new
                {
                    Type = "ACCREC", Contact = new { Name = i.ContactName, EmailAddress = i.Email }, InvoiceNumber = i.InvoiceNumber, Reference = i.Reference,
                    Date = i.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), DueDate = i.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LineAmountTypes = "Exclusive", Status = "AUTHORISED", CurrencyCode = "NZD",
                    LineItems = i.Lines.Select(l => new { l.Description, l.Quantity, l.UnitAmount, l.AccountCode, l.TaxType, ItemCode = (string?)null }).ToArray(),
                },
            },
        };
        using var doc = await SendAsync(c, Req("Invoices?unitdp=4", accessToken, tenantId, body), ct);
        return doc.RootElement.GetProperty("Invoices")[0].GetProperty("InvoiceID").GetString()!;
    }

    public async Task<string> CreatePaymentAsync(string accessToken, string tenantId, XeroPayment p, CancellationToken ct)
    {
        using var c = http.CreateClient("xero");
        var body = new { Payments = new[] { new { Invoice = new { InvoiceID = p.InvoiceXeroId }, Account = new { Code = p.AccountCode }, Date = p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Amount = p.Amount, Reference = p.Reference } } };
        using var doc = await SendAsync(c, Req("Payments", accessToken, tenantId, body), ct);
        return doc.RootElement.GetProperty("Payments")[0].GetProperty("PaymentID").GetString()!;
    }
}

/// <summary>Daily: expires prior approvals past their valid-until date.</summary>
public sealed class ClaimsWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<ClaimsWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            if (licence.Status.AccessTo(ClaimsModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<ClaimsService>().ExpireApprovalsAsync(stoppingToken);
                if (n > 0) log.LogInformation("Claims: {Count} prior approval(s) expired.", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Claims housekeeping failed; it will run again in an hour."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
