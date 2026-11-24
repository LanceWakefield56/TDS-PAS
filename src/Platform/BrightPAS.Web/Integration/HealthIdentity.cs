using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Organisation;
using BrightPAS.Core.Patients;
using BrightPAS.Modules.Claims;
using BrightPAS.Modules.Letters;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Web.Integration;

public sealed record NhiPerson(string Nhi, bool IsLive, string? LiveNhi, string FamilyName, string GivenNames, DateOnly? DateOfBirth, string? Gender, bool Deceased,
    string? AddressLine1, string? AddressLine2, string? Suburb, string? City, string? Postcode, string? Mobile, string? Email);
public sealed record HpiPractitioner(string Cpn, string Name, string? Registration, string? Scope, bool Active);
public sealed record HpiFacility(string Id, string Name, string? AddressLine1, string? City, string? Postcode, string? Phone);

/// <summary>NHI and HPI lookups. Live: Health NZ FHIR R4 APIs; Simulator: training data. Every lookup is audited by <see cref="HealthIdentityService"/>.</summary>
public interface IHealthIdentityClient
{
    Task<NhiPerson?> NhiAsync(IdentitySettings s, string nhi, string user, CancellationToken ct);
    Task<IReadOnlyList<HpiPractitioner>> PractitionersAsync(IdentitySettings s, string nameOrCpn, string user, CancellationToken ct);
    Task<IReadOnlyList<HpiFacility>> FacilitiesAsync(IdentitySettings s, string nameOrId, string user, CancellationToken ct);
}

/// <summary>Health NZ Te Whatu Ora FHIR APIs with OAuth 2.0 client credentials. URLs come from the integration agreement (test then production).</summary>
public sealed class HealthNzIdentityClient(IHttpClientFactory http, ISecretProtector protector, TimeProvider clock) : IHealthIdentityClient
{
    private string? _token; private DateTimeOffset _expires;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private async Task<HttpClient> ClientAsync(IdentitySettings s, string user, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            if (_token is null || clock.GetUtcNow() > _expires)
            {
                var secret = protector.Unprotect(s.ClientSecretProtected) ?? throw new InvalidOperationException("The NHI/HPI client secret can't be read – enter it again.");
                using var c0 = http.CreateClient("identity");
                using var req = new HttpRequestMessage(HttpMethod.Post, s.TokenUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = s.ClientId!, ["client_secret"] = secret }),
                };
                using var res = await c0.SendAsync(req, ct);
                res.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                _token = doc.RootElement.GetProperty("access_token").GetString();
                var secs = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var n) ? n : 300;
                _expires = clock.GetUtcNow().AddSeconds(Math.Max(30, secs - 60));
            }
        }
        finally { Gate.Release(); }
        var c = http.CreateClient("identity");
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));
        c.DefaultRequestHeaders.Add("userid", user.Length > 100 ? user[..100] : user); // Health NZ requires the end user for its audit
        return c;
    }

    private async Task<JsonDocument?> GetAsync(IdentitySettings s, string url, string user, CancellationToken ct)
    {
        using var c = await ClientAsync(s, user, ct);
        using var res = await c.GetAsync(url, ct);
        if (res.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return null;
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }

    public async Task<NhiPerson?> NhiAsync(IdentitySettings s, string nhi, string user, CancellationToken ct)
    {
        using var doc = await GetAsync(s, $"{s.NhiBaseUrl!.TrimEnd('/')}/Patient/{Uri.EscapeDataString(nhi)}", user, ct);
        return doc is null ? null : Fhir.Patient(doc.RootElement, nhi);
    }

    public async Task<IReadOnlyList<HpiPractitioner>> PractitionersAsync(IdentitySettings s, string q, string user, CancellationToken ct)
    {
        var baseUrl = s.HpiBaseUrl!.TrimEnd('/');
        if (OrganisationService.IsHpiCpn(q))
        {
            using var one = await GetAsync(s, $"{baseUrl}/Practitioner/{Uri.EscapeDataString(q.Trim().ToUpperInvariant())}", user, ct);
            return one is null ? [] : [Fhir.Practitioner(one.RootElement)];
        }
        using var doc = await GetAsync(s, $"{baseUrl}/Practitioner?name={Uri.EscapeDataString(q)}&_count=20", user, ct);
        return doc is null ? [] : [.. Fhir.Entries(doc.RootElement).Select(Fhir.Practitioner)];
    }

    public async Task<IReadOnlyList<HpiFacility>> FacilitiesAsync(IdentitySettings s, string q, string user, CancellationToken ct)
    {
        var baseUrl = s.HpiBaseUrl!.TrimEnd('/');
        if (OrganisationService.IsHpiFacility(q))
        {
            using var one = await GetAsync(s, $"{baseUrl}/Location/{Uri.EscapeDataString(q.Trim().ToUpperInvariant())}", user, ct);
            return one is null ? [] : [Fhir.Location(one.RootElement)];
        }
        using var doc = await GetAsync(s, $"{baseUrl}/Location?name={Uri.EscapeDataString(q)}&_count=20", user, ct);
        return doc is null ? [] : [.. Fhir.Entries(doc.RootElement).Select(Fhir.Location)];
    }
}

/// <summary>FHIR R4 parsing for the NZ Base / NHI / HPI profiles (tolerant of missing elements).</summary>
public static class Fhir
{
    public static IEnumerable<JsonElement> Entries(JsonElement bundle) =>
        bundle.TryGetProperty("entry", out var e) && e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().Where(x => x.TryGetProperty("resource", out _)).Select(x => x.GetProperty("resource")) : [];

    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static IEnumerable<JsonElement> Arr(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : [];

    private static JsonElement? Pick(JsonElement e, string name, string preferUse) =>
        Arr(e, name).OrderBy(x => Str(x, "use") == preferUse ? 0 : 1).Cast<JsonElement?>().FirstOrDefault();

    public static NhiPerson Patient(JsonElement p, string asked)
    {
        var ids = Arr(p, "identifier").ToList();
        var live = ids.FirstOrDefault(i => Str(i, "use") == "official");
        var liveNhi = live.ValueKind == JsonValueKind.Object ? Str(live, "value") : Str(p, "id");
        var name = Pick(p, "name", "usual");
        var family = name is { } n ? Str(n, "family") ?? "" : "";
        var given = name is { } n2 ? string.Join(' ', Arr(n2, "given").Select(g => g.GetString())) : "";
        var addr = Pick(p, "address", "home");
        var lines = addr is { } a ? Arr(a, "line").Select(l => l.GetString()).ToList() : [];
        string? suburb = null;
        if (addr is { } a2) foreach (var x in Arr(a2, "extension")) if ((Str(x, "url") ?? "").EndsWith("suburb", StringComparison.OrdinalIgnoreCase)) suburb = Str(x, "valueString");
        var tel = Arr(p, "telecom").ToList();
        var deceased = (p.TryGetProperty("deceasedBoolean", out var db) && db.ValueKind == JsonValueKind.True) || p.TryGetProperty("deceasedDateTime", out _);
        return new(asked.ToUpperInvariant(), string.Equals(liveNhi, asked, StringComparison.OrdinalIgnoreCase), string.Equals(liveNhi, asked, StringComparison.OrdinalIgnoreCase) ? null : liveNhi,
            family, given, DateOnly.TryParse(Str(p, "birthDate"), System.Globalization.CultureInfo.InvariantCulture, out var dob) ? dob : null, Str(p, "gender"), deceased,
            lines.ElementAtOrDefault(0), lines.ElementAtOrDefault(1), suburb, addr is { } a3 ? Str(a3, "city") : null, addr is { } a4 ? Str(a4, "postalCode") : null,
            tel.Where(t => Str(t, "system") == "phone" && Str(t, "use") == "mobile").Select(t => Str(t, "value")).FirstOrDefault(),
            tel.Where(t => Str(t, "system") == "email").Select(t => Str(t, "value")).FirstOrDefault());
    }

    public static HpiPractitioner Practitioner(JsonElement p)
    {
        var name = Pick(p, "name", "official");
        var text = name is { } n ? Str(n, "text") ?? $"{string.Join(' ', Arr(n, "prefix").Select(x => x.GetString()))} {string.Join(' ', Arr(n, "given").Select(x => x.GetString()))} {Str(n, "family")}".Trim() : "?";
        var q = Arr(p, "qualification").FirstOrDefault();
        string? reg = null, scope = null;
        if (q.ValueKind == JsonValueKind.Object)
        {
            if (q.TryGetProperty("code", out var code)) reg = Str(code, "text") ?? Arr(code, "coding").Select(c => Str(c, "display")).FirstOrDefault();
            foreach (var x in Arr(q, "extension"))
                if ((Str(x, "url") ?? "").Contains("scope", StringComparison.OrdinalIgnoreCase) && x.TryGetProperty("valueCodeableConcept", out var v)) scope = Str(v, "text") ?? Arr(v, "coding").Select(c => Str(c, "display")).FirstOrDefault();
        }
        var active = !p.TryGetProperty("active", out var act) || act.ValueKind != JsonValueKind.False;
        return new(Str(p, "id") ?? "", text, reg, scope, active);
    }

    public static HpiFacility Location(JsonElement l)
    {
        var addr = l.TryGetProperty("address", out var a) && a.ValueKind == JsonValueKind.Object ? a : (JsonElement?)null;
        var phone = Arr(l, "telecom").Where(t => Str(t, "system") == "phone").Select(t => Str(t, "value")).FirstOrDefault();
        return new(Str(l, "id") ?? "", Str(l, "name") ?? "?", addr is { } x ? Arr(x, "line").Select(v => v.GetString()).FirstOrDefault() : null,
            addr is { } y ? Str(y, "city") : null, addr is { } z ? Str(z, "postalCode") : null, phone);
    }
}

/// <summary>Training simulator: test NHIs (Z…) and the demo patients' NHIs answer with a matching person whose address differs slightly; HPI returns a small fixed register.</summary>
public sealed class SimulatorIdentityClient(PatientsDbContext patients) : IHealthIdentityClient
{
    private static readonly HpiPractitioner[] People =
    [
        new("12ABCD", "Dr Ben Hall", "Medical Council of NZ – vocational (general surgery)", "General surgery", true),
        new("23BCDE", "Dr Fiona Grant", "Medical Council of NZ – vocational (anaesthesia)", "Anaesthesia", true),
        new("34CDEF", "Dr Mere Tane", "Medical Council of NZ – vocational (general practice)", "General practice", true),
        new("45DEFG", "Sam Patel", "Nursing Council of NZ – registered nurse", "Registered nurse", true),
        new("56EFGH", "Dr Old Retired", "Medical Council of NZ", null, false),
    ];
    private static readonly HpiFacility[] Places =
    [
        new("F2N060-E", "Stoke Medical Centre", "475 Main Road Stoke", "Nelson", "7011", "03 547 0000"),
        new("F2N061-C", "Richmond Health Centre", "4 Croucher Street", "Richmond", "7020", "03 544 0000"),
        new("F2N062-A", "Brightwater Medical", "17 Ellis Street", "Brightwater", "7022", "03 542 0000"),
        new("F2N063-J", "Motueka Family Health", "33 Pah Street", "Motueka", "7120", "03 528 0000"),
    ];

    public async Task<NhiPerson?> NhiAsync(IdentitySettings s, string nhi, string user, CancellationToken ct)
    {
        var p = await patients.Patients.AsNoTracking().FirstOrDefaultAsync(x => x.Nhi == nhi && x.MergedIntoId == null, ct);
        if (p is null) return nhi.StartsWith('Z') ? new(nhi, true, null, "TESTPATIENT", "Sim", new DateOnly(1970, 1, 1), "unknown", false, "1 Example Street", null, "Stoke", "Nelson", "7011", null, null) : null;
        // Deterministic small difference so the compare screen has something to show for some patients.
        var differs = SHA256.HashData(Encoding.UTF8.GetBytes(nhi))[0] % 3 == 0;
        return new(nhi, true, null, p.FamilyName, p.GivenNames, p.DateOfBirth, p.Sex.ToString().ToLowerInvariant(), false,
            differs ? "88 Updated Road" : p.AddressLine1, differs ? null : p.AddressLine2, p.Suburb, p.City, p.Postcode, p.MobilePhone, p.Email);
    }

    public Task<IReadOnlyList<HpiPractitioner>> PractitionersAsync(IdentitySettings s, string q, string user, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HpiPractitioner>>([.. People.Where(p => p.Cpn.Equals(q.Trim(), StringComparison.OrdinalIgnoreCase) || p.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))]);

    public Task<IReadOnlyList<HpiFacility>> FacilitiesAsync(IdentitySettings s, string q, string user, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<HpiFacility>>([.. Places.Where(p => p.Id.Equals(q.Trim(), StringComparison.OrdinalIgnoreCase) || p.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase) || (p.City ?? "").Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))]);
}

public sealed record IdentitySettingsEdit(IdentityMode Mode, string? NhiBaseUrl, string? HpiBaseUrl, string? TokenUrl, string? ClientId, string? ClientSecret);
public sealed record NhiField(string Key, string Label, string? Ours, string? Theirs, bool Differs, bool Identity);
public sealed record NhiCompare(string Nhi, bool Found, bool IsLive, string? LiveNhi, bool Deceased, IReadOnlyList<NhiField> Fields, string? Error);

/// <summary>
/// NHI check on the patient page (compare, then accept contact details – identity changes go through an identity correction, ADR 0010),
/// HPI practitioner/facility lookup, and the GP practice file import that works without any connection.
/// </summary>
public sealed class HealthIdentityService(ClaimsDbContext db, PatientsDbContext patients, PatientService patientSvc, LettersDbContext letters, LetterService letterSvc,
    ISecretProtector protector, HealthNzIdentityClient live, SimulatorIdentityClient sim, AuditWriter audit, TimeProvider clock)
{
    public async Task<IdentitySettings> SettingsAsync(CancellationToken ct = default) => await db.Identity.SingleAsync(ct);

    public async Task<string?> SaveSettingsAsync(IdentitySettingsEdit e, string user, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(e.Mode)) return "Choose the lookup mode.";
        var s = await SettingsAsync(ct);
        if (e.Mode == IdentityMode.Live)
        {
            foreach (var (u, label) in new[] { (e.NhiBaseUrl, "NHI API address"), (e.HpiBaseUrl, "HPI API address"), (e.TokenUrl, "token address") })
                if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return $"Enter the {label} (https).";
            if (string.IsNullOrWhiteSpace(e.ClientId)) return "Enter the client ID from Health NZ.";
            if (string.IsNullOrWhiteSpace(e.ClientSecret) && s.ClientSecretProtected is null) return "Enter the client secret from Health NZ.";
        }
        s.Mode = e.Mode; s.NhiBaseUrl = Blank(e.NhiBaseUrl); s.HpiBaseUrl = Blank(e.HpiBaseUrl); s.TokenUrl = Blank(e.TokenUrl); s.ClientId = Blank(e.ClientId);
        if (!string.IsNullOrWhiteSpace(e.ClientSecret)) s.ClientSecretProtected = protector.Protect(e.ClientSecret.Trim());
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "IdentitySettings", "1", reason: s.Mode.ToString(), ct: ct);
        return null;
    }

    private IHealthIdentityClient Client(IdentitySettings s) => s.Mode == IdentityMode.Live ? live : sim;

    private async Task<T> CallAsync<T>(IdentitySettings s, Func<IHealthIdentityClient, Task<T>> f, CancellationToken ct)
    {
        try
        {
            var r = await f(Client(s));
            s.LastOkAt = clock.GetUtcNow(); s.LastError = null;
            await db.SaveChangesAsync(ct);
            return r;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            var msg = $"{clock.GetUtcNow():u} {ex.GetType().Name}: {ex.Message}";
            s.LastError = msg.Length > 1000 ? msg[..1000] : msg;
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("The NHI/HPI service didn't answer. Carry on with the details you have and check again later.", ex);
        }
    }

    public async Task<NhiCompare> CompareAsync(Guid patientId, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return new("", false, false, null, false, [], "Patient not found.");
        if (s.Mode == IdentityMode.Off) return new(p.Nhi ?? "", false, false, null, false, [], "NHI lookups are switched off. Ask an administrator to turn them on in Claims & payments › Settings.");
        if (string.IsNullOrEmpty(p.Nhi) || !NhiValidator.Validate(p.Nhi).IsValid) return new(p.Nhi ?? "", false, false, null, false, [], "Record a valid NHI first (an NHI search by name needs the Health NZ portal).");
        NhiPerson? n;
        try { n = await CallAsync(s, c => c.NhiAsync(s, p.Nhi, user, ct), ct); }
        catch (InvalidOperationException ex) { return new(p.Nhi, false, false, null, false, [], ex.Message); }
        await audit.WriteAsync(user, "NhiLookup", "Patient", p.Id.ToString(), p.Id.ToString(), reason: $"{p.Nhi} {(n is null ? "not found" : n.IsLive ? "live" : "dormant → " + n.LiveNhi)}", ct: ct);
        if (n is null) return new(p.Nhi, false, false, null, false, [], "The NHI service has no record of this number – check it with the patient.");
        static bool Diff(string? a, string? b) => !string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        var fields = new List<NhiField>
        {
            new("family", "Family name", p.FamilyName, n.FamilyName, Diff(p.FamilyName, n.FamilyName), true),
            new("given", "Given names", p.GivenNames, n.GivenNames, Diff(p.GivenNames, n.GivenNames), true),
            new("dob", "Date of birth", p.DateOfBirth.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), n.DateOfBirth?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), n.DateOfBirth != p.DateOfBirth, true),
            new("address1", "Address", p.AddressLine1, n.AddressLine1, Diff(p.AddressLine1, n.AddressLine1), false),
            new("address2", "Address line 2", p.AddressLine2, n.AddressLine2, Diff(p.AddressLine2, n.AddressLine2), false),
            new("suburb", "Suburb", p.Suburb, n.Suburb, Diff(p.Suburb, n.Suburb), false),
            new("city", "Town/city", p.City, n.City, Diff(p.City, n.City), false),
            new("postcode", "Postcode", p.Postcode, n.Postcode, Diff(p.Postcode, n.Postcode), false),
            new("mobile", "Mobile", p.MobilePhone, n.Mobile, n.Mobile is not null && Diff(p.MobilePhone, n.Mobile), false),
            new("email", "Email", p.Email, n.Email, n.Email is not null && Diff(p.Email, n.Email), false),
        };
        return new(p.Nhi, true, n.IsLive, n.LiveNhi, n.Deceased, fields, null);
    }

    /// <summary>Accepts the NHI's contact details (address, mobile, email) for the chosen fields. Never changes name, date of birth or NHI.</summary>
    public async Task<IReadOnlyList<string>> AcceptAsync(Guid patientId, IReadOnlyList<string> keys, string user, CancellationToken ct = default)
    {
        var cmp = await CompareAsync(patientId, user, ct);
        if (cmp.Error is not null) return [cmp.Error];
        if (keys.Any(k => cmp.Fields.Any(f => f.Key == k && f.Identity))) return ["Name and date of birth are changed with an identity correction (second-person check), not here."];
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == patientId, ct);
        string? V(string key, string? ours) => keys.Contains(key) ? cmp.Fields.Single(f => f.Key == key).Theirs : ours;
        var addr = keys.Any(k => k is "address1" or "address2" or "suburb" or "city" or "postcode");
        var u = new ContactUpdate(p.PreferredName, V("mobile", p.MobilePhone), p.HomePhone, V("email", p.Email),
            addr ? cmp.Fields.Single(f => f.Key == "address1").Theirs : p.AddressLine1, addr ? cmp.Fields.Single(f => f.Key == "address2").Theirs : p.AddressLine2,
            addr ? cmp.Fields.Single(f => f.Key == "suburb").Theirs : p.Suburb, addr ? cmp.Fields.Single(f => f.Key == "city").Theirs : p.City,
            addr ? cmp.Fields.Single(f => f.Key == "postcode").Theirs : p.Postcode, p.GpPracticeHpiFacilityId);
        var errors = await patientSvc.UpdateContactAsync(patientId, u, user, ct);
        if (errors.Count == 0) await audit.WriteAsync(user, "NhiAccept", "Patient", patientId.ToString(), patientId.ToString(), reason: string.Join(", ", keys), ct: ct);
        return errors;
    }

    public async Task<(IReadOnlyList<HpiPractitioner> Rows, string? Error)> PractitionersAsync(string q, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s.Mode == IdentityMode.Off) return ([], "HPI lookups are switched off.");
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3) return ([], "Type at least three letters of the name, or the HPI number (CPN).");
        try
        {
            var r = await CallAsync(s, c => c.PractitionersAsync(s, q.Trim(), user, ct), ct);
            await audit.WriteAsync(user, "HpiLookup", "Practitioner", q.Trim(), reason: $"{r.Count} found", ct: ct);
            return (r, null);
        }
        catch (InvalidOperationException ex) { return ([], ex.Message); }
    }

    public async Task<(IReadOnlyList<HpiFacility> Rows, string? Error)> FacilitiesAsync(string q, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s.Mode == IdentityMode.Off) return ([], "HPI lookups are switched off – import the GP practice list from a file instead.");
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3) return ([], "Type at least three letters of the practice name or town, or the facility number.");
        try
        {
            var r = await CallAsync(s, c => c.FacilitiesAsync(s, q.Trim(), user, ct), ct);
            await audit.WriteAsync(user, "HpiLookup", "Facility", q.Trim(), reason: $"{r.Count} found", ct: ct);
            return (r, null);
        }
        catch (InvalidOperationException ex) { return ([], ex.Message); }
    }

    /// <summary>Adds an HPI facility as a GP practice (letters recipient). Delivery defaults to post until the HealthLink EDI is entered.</summary>
    public async Task<IReadOnlyList<string>> AddPracticeAsync(HpiFacility f, string user, CancellationToken ct = default)
    {
        var existing = await letters.Practices.AsNoTracking().SingleOrDefaultAsync(p => p.HpiFacilityId == f.Id, ct);
        return await letterSvc.SavePracticeAsync(new PracticeEdit(existing?.Id, f.Name, f.Id, existing?.HealthLinkEdi, f.AddressLine1, existing?.AddressLine2, f.City, f.Postcode, f.Phone, existing?.Email,
            existing?.PreferredChannel ?? DeliveryChannel.Post, true), user, ct);
    }

    /// <summary>
    /// GP practice list from a file (works with no connection): CSV with facility id, name, address, city, postcode, phone, HealthLink EDI.
    /// Existing practices (same facility id) are updated; nothing is deleted.
    /// </summary>
    public async Task<(int Added, int Updated, IReadOnlyList<string> Errors)> ImportPracticesAsync(string csv, string user, CancellationToken ct = default)
    {
        if (csv.Length > 2_000_000) return (0, 0, ["The file is too large (2 MB)."]);
        var rows = ClaimsCsv.Parse(csv);
        if (rows.Count < 2) return (0, 0, ["The file has no practices."]);
        var h = rows[0];
        int cId = ClaimsCsv.Column(h, "FacilityId", "HpiFacilityId", "HPI", "Id"), cName = ClaimsCsv.Column(h, "Name", "PracticeName", "FacilityName"),
            cAddr = ClaimsCsv.Column(h, "Address", "AddressLine1", "Street"), cCity = ClaimsCsv.Column(h, "City", "Town"), cPc = ClaimsCsv.Column(h, "Postcode", "PostCode"),
            cPh = ClaimsCsv.Column(h, "Phone", "Telephone"), cEdi = ClaimsCsv.Column(h, "HealthLinkEdi", "EDI", "HealthLink");
        if (cId < 0 || cName < 0) return (0, 0, ["The file needs a facility ID column and a name column."]);
        var errors = new List<string>(); int added = 0, updated = 0, line = 1;
        var existing = await letters.Practices.AsNoTracking().Where(p => p.HpiFacilityId != null).ToDictionaryAsync(p => p.HpiFacilityId!, ct);
        foreach (var r in rows.Skip(1))
        {
            line++;
            string? Cell(int i) => i >= 0 && i < r.Count && !string.IsNullOrWhiteSpace(r[i]) ? r[i].Trim() : null;
            var id = Cell(cId)?.ToUpperInvariant();
            if (!OrganisationService.IsHpiFacility(id)) { errors.Add($"Line {line}: facility ID '{id}' isn't in the format F12345-A."); continue; }
            existing.TryGetValue(id!, out var ex);
            var edi = Cell(cEdi) ?? ex?.HealthLinkEdi;
            var e = await letterSvc.SavePracticeAsync(new PracticeEdit(ex?.Id, Cell(cName) ?? "", id, edi, Cell(cAddr) ?? ex?.AddressLine1, ex?.AddressLine2, Cell(cCity) ?? ex?.City,
                Cell(cPc) ?? ex?.Postcode, Cell(cPh) ?? ex?.Phone, ex?.Email, edi is null ? (ex?.PreferredChannel == DeliveryChannel.HealthLink ? DeliveryChannel.Post : ex?.PreferredChannel ?? DeliveryChannel.Post) : DeliveryChannel.HealthLink, true), user, ct);
            if (e.Count > 0) { errors.Add($"Line {line}: {e[0]}"); continue; }
            if (ex is null) added++; else updated++;
            if (errors.Count > 50) { errors.Add("Stopped after 50 errors."); break; }
        }
        await audit.WriteAsync(user, "Import", "GpPractices", "file", reason: $"{added} added, {updated} updated, {errors.Count} errors", ct: ct);
        return (added, updated, errors);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
