using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BrightPAS.Core.Modules;
using BrightPAS.Licensing;
using BrightPAS.Modules.Prescribing;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// The barcode medicine check (0.31.0, ADR 0044) as one of the clinical record's medicine checks: runs only when Prescribing is licensed.
/// </summary>
public sealed class BcmaGate(LicenceCache licence, BcmaService bcma) : IMedicineSafetyCheck
{
    public async Task<MedicineCheckOutcome> CheckAsync(MedicineGiveCheck c, CancellationToken ct)
    {
        if (licence.Status.AccessTo(PrescribingModule.Id) != ModuleAccess.Full) return new(true, null, null, []);
        var r = await bcma.CheckAsync(c, ct);
        return new(r.Allowed, r.Error, r.ScanId, [.. r.Findings.Select(f => new MedicineCheckNote("Barcode", f.Severity, f.Text))]);
    }

    public Task LinkAsync(Guid checkId, Guid medicationId, CancellationToken ct) => bcma.LinkAsync(checkId, medicationId, ct);
}

/// <summary>
/// NZ ePrescription Service client: posts the prescription as a FHIR bundle and cancels by prescription id, with the client id and key Health NZ
/// issues. The exact endpoints and authentication are confirmed when TDS signs the NZePS agreement; until then use Print only or the Simulator.
/// </summary>
public sealed class HttpNzepsApi(IHttpClientFactory http) : INzepsApi
{
    private static HttpRequestMessage Req(HttpMethod m, string url, string? clientId, string? key, HttpContent? body)
    {
        var r = new HttpRequestMessage(m, url) { Content = body };
        if (key is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        if (clientId is not null) r.Headers.Add("X-Client-Id", clientId);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        return r;
    }

    public async Task<(bool Ok, string? Reference, string? Error)> SendAsync(string url, string? clientId, string? key, string fhirJson, CancellationToken ct)
    {
        try
        {
            using var c = http.CreateClient("nzeps");
            using var req = Req(HttpMethod.Post, url.TrimEnd('/') + "/Bundle", clientId, key, new StringContent(fhirJson, Encoding.UTF8, "application/fhir+json"));
            using var res = await c.SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) return (false, null, $"NZePS answered {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
            string? reference = res.Headers.Location?.ToString();
            try { using var j = JsonDocument.Parse(text); if (j.RootElement.TryGetProperty("id", out var id)) reference = id.GetString(); } catch (JsonException) { }
            return (true, reference, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (false, null, "Could not reach NZePS: " + ex.Message); }
    }

    public async Task<(bool Ok, string? Error)> CancelAsync(string url, string? clientId, string? key, string nzepsId, string reason, CancellationToken ct)
    {
        try
        {
            using var c = http.CreateClient("nzeps");
            var body = JsonSerializer.Serialize(new { resourceType = "Parameters", parameter = new object[] { new { name = "prescriptionId", valueString = nzepsId }, new { name = "reason", valueString = reason } } }, JsonSerializerOptions.Web);
            using var req = Req(HttpMethod.Post, url.TrimEnd('/') + "/MedicationRequest/$cancel", clientId, key, new StringContent(body, Encoding.UTF8, "application/fhir+json"));
            using var res = await c.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return (true, null);
            var text = await res.Content.ReadAsStringAsync(ct);
            return (false, $"NZePS answered {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return (false, "Could not reach NZePS: " + ex.Message); }
    }
}
