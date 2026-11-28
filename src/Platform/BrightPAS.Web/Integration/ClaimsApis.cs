using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using BrightPAS.Modules.Claims;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Windcave HIT (Host Initiated Transactions) for an integrated EFTPOS terminal (0.34.0). BrightPAS posts a Purchase or Refund to
/// {url}/pxmi3/pos.aspx and then asks for its Status until the terminal finishes. Card numbers come back masked; only the last four are kept.
/// Needs a Windcave merchant agreement and HIT user/key; test against uat.windcave.com first.
/// </summary>
public sealed class HttpTerminalApi(IHttpClientFactory http) : ITerminalApi
{
    private static async Task<XElement> PostAsync(HttpClient c, string url, XElement body, CancellationToken ct)
    {
        using var content = new StringContent(body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml");
        using var res = await c.PostAsync(url.TrimEnd('/') + "/pxmi3/pos.aspx", content, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Windcave said {(int)res.StatusCode}.");
        return XElement.Parse(text);
    }

    private static XElement Scr(string user, string key, string station, string txnType, string txnRef, params object[] extra) =>
        new("Scr", new XAttribute("action", "doScrHIT"), new XAttribute("user", user), new XAttribute("key", key),
            new XElement("Station", station), new XElement("TxnType", txnType), new XElement("TxnRef", txnRef),
            new XElement("DeviceId", "BrightPAS"), new XElement("PosName", "BrightPAS"), new XElement("VendorId", "BrightPAS"), extra);

    public async Task<TerminalStart> StartAsync(string url, string user, string key, string station, bool refund, decimal amount, string txnRef, CancellationToken ct)
    {
        using var c = http.CreateClient("windcave");
        var x = await PostAsync(c, url, Scr(user, key, station, refund ? "Refund" : "Purchase", txnRef,
            new XElement("Amount", amount.ToString("0.00", CultureInfo.InvariantCulture)), new XElement("Cur", "NZD"), new XElement("MRef", txnRef)), ct);
        var ok = (string?)x.Element("Success") is null or "1" && (string?)x.Element("Error") is null or "";
        return new(ok, ok ? null : (string?)x.Element("Error") ?? (string?)x.Element("ReCo") ?? "The terminal did not accept the transaction.");
    }

    public async Task<TerminalPoll> StatusAsync(string url, string user, string key, string station, string txnRef, CancellationToken ct)
    {
        using var c = http.CreateClient("windcave");
        var x = await PostAsync(c, url, Scr(user, key, station, "Status", txnRef), ct);
        if ((string?)x.Element("Complete") != "1") return new(false, null);
        var r = x.Element("Result");
        var approved = (string?)r?.Element("AP") == "1";
        var card = (string?)r?.Element("CN");
        return new(true, new TerminalResult(approved, (string?)r?.Element("AC"), (string?)r?.Element("CT"), card, (string?)r?.Element("RT") ?? (string?)r?.Element("DS")));
    }
}

/// <summary>
/// Funder claiming over FHIR R4 (0.34.0): Claim (use "claim" or "preauthorization") posted to {url}/Claim, answers read from
/// {url}/ClaimResponse?request=Claim/{id}. ACC, Southern Cross and nib each need a provider agreement and give the address and key.
/// </summary>
public sealed class FhirFunderApi(IHttpClientFactory http) : IFunderApi
{
    private static HttpRequestMessage Req(HttpMethod m, string url, string key, JsonNode? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        if (body is not null) r.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/fhir+json");
        return r;
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static JsonObject Money(decimal v) => new() { ["value"] = v, ["currency"] = "NZD" };

    private static JsonObject Patient(string? nhi, string family, string given, DateOnly? dob)
    {
        var p = new JsonObject { ["resourceType"] = "Patient", ["id"] = "p", ["name"] = new JsonArray(new JsonObject { ["family"] = family, ["given"] = new JsonArray(given) }) };
        if (nhi is not null) p["identifier"] = new JsonArray(new JsonObject { ["system"] = "https://standards.digital.health.nz/ns/nhi-id", ["value"] = nhi });
        if (dob is { } b) p["birthDate"] = D(b);
        return p;
    }

    private static JsonObject Claim(string use, string number, string? provider, JsonObject patient, string? member, string? acc, DateOnly created, JsonArray items, decimal total, string? preAuth)
    {
        var c = new JsonObject
        {
            ["resourceType"] = "Claim",
            ["status"] = "active",
            ["use"] = use,
            ["identifier"] = new JsonArray(new JsonObject { ["system"] = "urn:brightpas:claim", ["value"] = number }),
            ["type"] = new JsonObject { ["coding"] = new JsonArray(new JsonObject { ["system"] = "http://terminology.hl7.org/CodeSystem/claim-type", ["code"] = "institutional" }) },
            ["contained"] = new JsonArray(patient),
            ["patient"] = new JsonObject { ["reference"] = "#p" },
            ["created"] = D(created),
            ["provider"] = new JsonObject { ["identifier"] = new JsonObject { ["value"] = provider ?? "" } },
            ["priority"] = new JsonObject { ["coding"] = new JsonArray(new JsonObject { ["code"] = "normal" }) },
            ["insurance"] = new JsonArray(new JsonObject { ["sequence"] = 1, ["focal"] = true, ["identifier"] = new JsonObject { ["value"] = member ?? acc ?? "" }, ["coverage"] = new JsonObject { ["display"] = acc is null ? "Policy" : "ACC claim" } }),
            ["item"] = items,
            ["total"] = Money(total),
        };
        if (preAuth is not null) ((JsonObject)((JsonArray)c["insurance"]!)[0]!)["preAuthRef"] = new JsonArray(preAuth);
        return c;
    }

    private async Task<FunderSent> PostAsync(string url, string key, JsonObject claim, CancellationToken ct)
    {
        using var c = http.CreateClient("funder");
        using var res = await c.SendAsync(Req(HttpMethod.Post, url.TrimEnd('/') + "/Claim", key, claim), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            var msg = text.Length > 300 ? text[..300] : text;
            return new(false, null, $"The funder said {(int)res.StatusCode}: {msg}");
        }
        var id = res.Headers.Location?.Segments.SkipWhile(s => s != "Claim/").Skip(1).FirstOrDefault()?.TrimEnd('/');
        if (id is null && text.Length > 0) id = JsonNode.Parse(text)?["id"]?.GetValue<string>();
        return id is null ? new(false, null, "The funder did not return a claim reference.") : new(true, id, null);
    }

    public Task<FunderSent> SubmitClaimAsync(string url, string key, FunderClaim c, CancellationToken ct)
    {
        var items = new JsonArray([.. c.Lines.Select((l, i) => (JsonNode)new JsonObject
        {
            ["sequence"] = i + 1, ["productOrService"] = new JsonObject { ["coding"] = new JsonArray(new JsonObject { ["code"] = l.Code }), ["text"] = l.Description },
            ["servicedDate"] = D(c.ServiceDate), ["net"] = Money(l.Amount),
        })]);
        return PostAsync(url, key, Claim("claim", c.ClaimNumber, c.ProviderNumber, Patient(c.Nhi, c.FamilyName, c.GivenNames, c.DateOfBirth), c.MemberNumber, c.AccClaimNumber, c.ServiceDate, items, c.Amount, c.ApprovalReference), ct);
    }

    public Task<FunderSent> RequestApprovalAsync(string url, string key, FunderApproval a, CancellationToken ct)
    {
        var items = new JsonArray(new JsonObject { ["sequence"] = 1, ["productOrService"] = new JsonObject { ["text"] = a.Procedure }, ["net"] = Money(a.Amount) });
        return PostAsync(url, key, Claim("preauthorization", a.Number, a.ProviderNumber, Patient(a.Nhi, a.FamilyName, a.GivenNames, a.DateOfBirth), a.MemberNumber, a.AccClaimNumber, DateOnly.FromDateTime(DateTime.UtcNow), items, a.Amount, null), ct);
    }

    private async Task<(JsonNode? Response, string? Error)> ResponseAsync(string url, string key, string reference, CancellationToken ct)
    {
        using var c = http.CreateClient("funder");
        using var res = await c.SendAsync(Req(HttpMethod.Get, $"{url.TrimEnd('/')}/ClaimResponse?request=Claim/{Uri.EscapeDataString(reference)}&_sort=-created&_count=1", key), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return (null, $"The funder said {(int)res.StatusCode}.");
        var entry = (JsonNode.Parse(text)?["entry"] as JsonArray)?.FirstOrDefault();
        return (entry?["resource"], null);
    }

    private static decimal? Value(JsonNode? m) => m?["value"]?.GetValue<decimal>();

    public async Task<FunderReply> ClaimStatusAsync(string url, string key, string reference, CancellationToken ct)
    {
        var (r, err) = await ResponseAsync(url, key, reference, ct);
        if (err is not null) return new(false, "Pending", null, null, null, null, err);
        if (r is null) return new(true, "Pending", null, null, null, null, null);
        var outcome = r["outcome"]?.GetValue<string>();
        var text = r["disposition"]?.GetValue<string>();
        var paid = Value(r["payment"]?["amount"]);
        var payRef = r["payment"]?["identifier"]?["value"]?.GetValue<string>();
        var status = outcome switch
        {
            "error" => "Rejected",
            "partial" when paid is > 0 => "PartPaid",
            "complete" when paid is > 0 => "Paid",
            "complete" or "partial" => "Accepted",
            _ => "Pending",
        };
        return new(true, status, paid, payRef, null, text, null);
    }

    public async Task<FunderReply> ApprovalStatusAsync(string url, string key, string reference, CancellationToken ct)
    {
        var (r, err) = await ResponseAsync(url, key, reference, ct);
        if (err is not null) return new(false, "Pending", null, null, null, null, err);
        if (r is null) return new(true, "Pending", null, null, null, null, null);
        var outcome = r["outcome"]?.GetValue<string>();
        var benefit = (r["total"] as JsonArray)?.Select(t => Value(t?["amount"])).FirstOrDefault(v => v is not null);
        DateOnly? until = r["preAuthPeriod"]?["end"]?.GetValue<string>() is { Length: >= 10 } e && DateOnly.TryParse(e[..10], CultureInfo.InvariantCulture, out var d) ? d : null;
        var status = outcome switch { "error" => "Declined", "partial" => "PartApproved", "complete" => "Approved", _ => "Pending" };
        return new(true, status, benefit, r["preAuthRef"]?.GetValue<string>(), until, r["disposition"]?.GetValue<string>(), null);
    }
}
