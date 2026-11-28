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
                    Type = "ACCREC", Contact = i.ContactId is null ? (object)new { Name = i.ContactName, EmailAddress = i.Email } : new { ContactID = i.ContactId }, InvoiceNumber = i.InvoiceNumber, Reference = i.Reference,
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

    public async Task<string?> FindContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct)
    {
        using var c = http.CreateClient("xero");
        using var r = new HttpRequestMessage(HttpMethod.Get, ApiUrl + "Contacts?summaryOnly=true&searchTerm=" + Uri.EscapeDataString(name));
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        r.Headers.Add("xero-tenant-id", tenantId);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var doc = await SendAsync(c, r, ct);
        string? byName = null;
        foreach (var x in doc.RootElement.GetProperty("Contacts").EnumerateArray())
        {
            var id = x.GetProperty("ContactID").GetString();
            var mail = x.TryGetProperty("EmailAddress", out var e) ? e.GetString() : null;
            if (email is not null && string.Equals(mail, email, StringComparison.OrdinalIgnoreCase)) return id;
            if (byName is null && string.Equals(x.GetProperty("Name").GetString(), name, StringComparison.OrdinalIgnoreCase)) byName = id;
        }
        return byName;
    }

    public async Task<string> CreateContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct)
    {
        using var c = http.CreateClient("xero");
        var body = new { Contacts = new[] { new { Name = name, EmailAddress = email } } };
        using var doc = await SendAsync(c, Req("Contacts", accessToken, tenantId, body), ct);
        return doc.RootElement.GetProperty("Contacts")[0].GetProperty("ContactID").GetString()!;
    }

    public async Task<string> CreateCreditNoteAsync(string accessToken, string tenantId, XeroCreditNote n, CancellationToken ct)
    {
        using var c = http.CreateClient("xero");
        var date = n.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var body = new
        {
            CreditNotes = new[]
            {
                new
                {
                    Type = "ACCRECCREDIT", Contact = n.ContactId is null ? (object)new { Name = n.ContactName } : new { ContactID = n.ContactId }, CreditNoteNumber = n.Number, Reference = n.Reference,
                    Date = date, LineAmountTypes = "Inclusive", Status = "AUTHORISED", CurrencyCode = "NZD",
                    LineItems = new[] { new { Description = n.Reference ?? n.Number, Quantity = 1m, UnitAmount = n.Amount, n.AccountCode, n.TaxType } },
                },
            },
        };
        using var doc = await SendAsync(c, Req("CreditNotes", accessToken, tenantId, body), ct);
        var id = doc.RootElement.GetProperty("CreditNotes")[0].GetProperty("CreditNoteID").GetString()!;
        var alloc = new { Allocations = new[] { new { Invoice = new { InvoiceID = n.InvoiceXeroId }, Amount = n.Amount, Date = date } } };
        using var _ = await SendAsync(c, Req($"CreditNotes/{id}/Allocations", accessToken, tenantId, alloc), ct);
        return id;
    }
}

/// <summary>Hourly: expires prior approvals past their valid-until date. Every 15 minutes: asks funders for answers on claims sent directly (0.34.0).</summary>
public sealed class ClaimsWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<ClaimsWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        var tick = 0;
        do
        {
            if (licence.Status.AccessTo(ClaimsModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (tick++ % 4 == 0)
                {
                    var n = await scope.ServiceProvider.GetRequiredService<ClaimsService>().ExpireApprovalsAsync(stoppingToken);
                    if (n > 0) log.LogInformation("Claims: {Count} prior approval(s) expired.", n);
                }
                var r = await scope.ServiceProvider.GetRequiredService<DirectClaimService>().CheckAsync("system", stoppingToken);
                if (r.Changed > 0 || r.Errors.Count > 0) log.LogInformation("Claims: direct check {Checked} checked, {Changed} changed, {Paid} paid, {Errors} errors.", r.Checked, r.Changed, r.Paid, r.Errors.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Claims housekeeping failed; it will run again in 15 minutes."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
