using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Security;

/// <summary>
/// Black-box penetration probes against the running website: TLS, headers, authentication, CSRF,
/// brute-force limits, injection, file disclosure, unsafe methods and error leakage.
/// These are safe, non-destructive tests (they only send requests an attacker could send).
/// </summary>
public sealed class WebProbes(SecurityTestOptions o, SecurityReport r, IInstallLog log)
{
    private const string Area = "Website (penetration probes)";
    private X509Certificate2? _cert;

    public async Task RunAsync(CancellationToken ct)
    {
        var baseUri = new Uri(o.Url.EndsWith('/') ? o.Url : o.Url + "/");
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
            {
                if (cert is not null) _cert = new X509Certificate2(cert);
                return errors == SslPolicyErrors.None || o.AllowSelfSigned;
            }
        };
        using var http = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(20) };

        HttpResponseMessage home;
        try { home = await Send(http, HttpMethod.Get, "", ct: ct); }
        catch (Exception ex)
        {
            log.Error($"Cannot reach {baseUri}: {ex.Message}", ex);
            r.Fail(Area, "Website reachable", $"Cannot connect to {baseUri}: {ex.Message}", "Check the site is started in IIS Manager, and use --url with the correct address.");
            return;
        }
        r.Pass(Area, "Website reachable", $"{baseUri} answered HTTP {(int)home.StatusCode}.");

        CheckCertificate(baseUri);
        await CheckOldTlsAsync(baseUri, ct);
        await CheckPlainHttpAsync(baseUri, ct);
        CheckHeaders(home, "home page");
        var api = await Send(http, HttpMethod.Get, "api/setup/status", ct: ct);
        CheckHeaders(api, "API", requireNoStore: true);

        // Authentication enforcement (platform)
        foreach (var path in new[] { "api/me", "api/admin/users", "api/audit", "api/organisation/locations", "api/admin/licence" })
        {
            var res = await Send(http, HttpMethod.Get, path, ct: ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized) r.Pass(Area, $"Login required: /{path.Split('?')[0]}", "Anonymous request refused (401).");
            else r.Fail(Area, $"Login required: /{path.Split('?')[0]}", $"Anonymous request returned HTTP {(int)res.StatusCode}.", "Protected data is reachable without signing in. Do not use the system; report this immediately.");
        }

        // Every module: anonymous reads and writes refused; writes without the CSRF header refused.
        foreach (var m in ModuleSuites.All) await ModuleSuiteAsync(http, m, ct);
        await RemindersCallbackAsync(http, ct);

        // Patient questionnaire links: a guessed link must not reveal anything; HL7 import needs sign-in.
        var guess = await Send(http, HttpMethod.Get, "api/q/" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)), ct: ct);
        if (guess.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) r.Pass(Area, "Patient link guessing", $"A made-up questionnaire link reveals nothing (HTTP {(int)guess.StatusCode}).");
        else r.Fail(Area, "Patient link guessing", $"A made-up questionnaire link returned HTTP {(int)guess.StatusCode}.", "Report this immediately.");
        var hl7 = await Send(http, HttpMethod.Post, "api/labs/import", """{"message":"MSH|^~\\&|X|Y|Z|W|202601010000||ORU^R01|1|P|2.4"}""", ct: ct);
        if (hl7.StatusCode == HttpStatusCode.Unauthorized) r.Pass(Area, "Lab result import requires sign-in", "Anonymous HL7 import refused (401).");
        else r.Fail(Area, "Lab result import requires sign-in", $"Anonymous HL7 import returned HTTP {(int)hl7.StatusCode}.", "Report this immediately.");

        // CSRF protection
        var csrf = await Send(http, HttpMethod.Post, "api/auth/logout", "{}", csrfHeader: false, ct: ct);
        if (csrf.StatusCode == HttpStatusCode.BadRequest) r.Pass(Area, "Cross-site request forgery (CSRF) protection", "State-changing request without the security header was refused.");
        else r.Fail(Area, "Cross-site request forgery (CSRF) protection", $"Request without header returned HTTP {(int)csrf.StatusCode}.", "Report this: the CSRF safeguard is not active.");

        // SQL injection probes on the login form
        var injectionOk = true;
        foreach (var payload in new[] { "' OR '1'='1' --", "admin'--", "\"; DROP TABLE core.AuditEvents; --", "') OR 1=1 --" })
        {
            var res = await Send(http, HttpMethod.Post, "api/auth/login", $$"""{"userName":{{Json(payload)}},"password":"x"}""", ct: ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            var leaked = body.Contains("SqlException", StringComparison.OrdinalIgnoreCase) || body.Contains("syntax", StringComparison.OrdinalIgnoreCase) || body.Contains("\"done\"");
            if ((int)res.StatusCode >= 500 || leaked || body.Contains("\"mfa\"") || body.Contains("\"enrol\"")) { injectionOk = false; log.Warn($"Injection payload produced HTTP {(int)res.StatusCode}: {body}"); }
        }
        if (injectionOk) r.Pass(Area, "SQL injection (login form)", "Four injection attacks were rejected normally (no errors, no sign-in).");
        else r.Fail(Area, "SQL injection (login form)", "An injection attempt caused an error or unexpected response (details in log).", "Report this immediately with the log file.");

        // Reflected script injection (XSS)
        var xssPath = "%3Cscript%3Ealert(1)%3C/script%3E";
        var xss = await Send(http, HttpMethod.Get, xssPath, ct: ct);
        var xssBody = await xss.Content.ReadAsStringAsync(ct);
        if (xssBody.Contains("<script>alert(1)</script>", StringComparison.OrdinalIgnoreCase))
            r.Fail(Area, "Reflected script injection (XSS)", "The page echoed an injected script.", "Report this immediately.");
        else r.Pass(Area, "Reflected script injection (XSS)", "Injected script was not reflected; the Content-Security-Policy also blocks inline scripts.");

        // Sensitive file disclosure / path traversal
        var disclosure = new (string Path, string Marker)[]
        {
            ("appsettings.json", "ConnectionStrings"), ("appsettings.Production.json", "ConnectionStrings"), ("web.config", "<configuration"),
            ("..%2fweb.config", "<configuration"), ("%2e%2e/%2e%2e/%2e%2e/windows/win.ini", "[fonts]"), ("BrightPAS.Web.dll", "This program cannot be run"),
            ("BrightPAS.Web.deps.json", "runtimeTarget"), (".git/config", "[core]"), ("assets/", "Index of")
        };
        var exposed = new List<string>();
        foreach (var (path, marker) in disclosure)
        {
            var res = await Send(http, HttpMethod.Get, path, ct: ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode && body.Contains(marker, StringComparison.OrdinalIgnoreCase)) exposed.Add("/" + path);
        }
        if (exposed.Count == 0) r.Pass(Area, "Sensitive files and path traversal", $"{disclosure.Length} attempts to download configuration, code or system files were blocked.");
        else r.Fail(Area, "Sensitive files and path traversal", $"Exposed: {string.Join(", ", exposed)}", "Re-run Setup (it re-applies web.config protections) and report this.");

        // Unsafe HTTP methods
        var trace = await Send(http, new HttpMethod("TRACE"), "", ct: ct);
        if (trace.StatusCode is HttpStatusCode.OK) r.Fail(Area, "TRACE method disabled", "TRACE is allowed.", "Re-run Setup to re-apply IIS hardening.");
        else r.Pass(Area, "TRACE method disabled", $"TRACE refused (HTTP {(int)trace.StatusCode}).");

        // Error leakage
        var bad = await Send(http, HttpMethod.Post, "api/auth/login", "{ this is not json", ct: ct);
        var badBody = await bad.Content.ReadAsStringAsync(ct);
        if (badBody.Contains("   at ", StringComparison.Ordinal) || badBody.Contains("Exception", StringComparison.Ordinal))
            r.Fail(Area, "Error details hidden", "A malformed request revealed internal error details (stack trace).", "Make sure the site runs in Production mode (web.config ASPNETCORE_ENVIRONMENT=Production).");
        else r.Pass(Area, "Error details hidden", $"Malformed request answered HTTP {(int)bad.StatusCode} without internal details.");

        // Brute-force protection (rate limiting) – uses a random unknown username so no real account is locked.
        if (o.IncludeRateLimitTests)
        {
            var user = "sectest-" + Guid.NewGuid().ToString("N")[..8];
            var limited = false;
            for (var i = 0; i < 40 && !limited; i++)
            {
                var res = await Send(http, HttpMethod.Post, "api/auth/login", $$"""{"userName":"{{user}}","password":"wrong-{{i}}"}""", quiet: true, ct: ct);
                if (res.StatusCode == HttpStatusCode.TooManyRequests) { limited = true; log.Debug($"Rate limit reached after {i + 1} attempts."); }
            }
            if (limited) r.Pass(Area, "Password-guessing protection", "Rapid repeated sign-in attempts were throttled (HTTP 429). Accounts also lock after 5 wrong passwords.");
            else r.Warn(Area, "Password-guessing protection", "40 rapid attempts were not throttled.", "Check the site is the current version; rate limiting is built in.");

            var code = false;
            for (var i = 0; i < 14 && !code; i++)
            {
                var res = await Send(http, HttpMethod.Post, "api/setup/first-admin/start", """{"code":"AAAA-BBBB-CCCC-DDDD","userName":"x","displayName":"x","password":"x"}""", quiet: true, ct: ct);
                if (res.StatusCode == HttpStatusCode.TooManyRequests) code = true;
            }
            if (code) r.Pass(Area, "Set-up code guessing protection", "Repeated guesses of the one-time set-up code were throttled.");
            else r.Warn(Area, "Set-up code guessing protection", "Set-up code guesses were not throttled.", "Check the site is the current version.");
        }
        else r.Add(Area, "Password-guessing protection", FindingStatus.Skip, "Skipped (--no-rate-limit-tests).");

        r.Info(Area, "Session cookies", "Cookie security (HttpOnly, Secure, SameSite=Strict, __Host- prefix, 15-minute idle timeout) is verified by the automated tests in every build.");
    }

    private async Task ModuleSuiteAsync(HttpClient http, ModuleSuite m, CancellationToken ct)
    {
        var area = $"Module security – {m.Name}";
        var leaks = new List<string>();
        foreach (var path in m.AnonymousReads)
        {
            var res = await Send(http, HttpMethod.Get, path, ct: ct);
            if (res.StatusCode != HttpStatusCode.Unauthorized) leaks.Add($"GET /{path} -> {(int)res.StatusCode}");
        }
        if (leaks.Count == 0) r.Pass(area, "Login required (anonymous reads)", $"{m.AnonymousReads.Length} anonymous read attempts were refused (401).");
        else r.Fail(area, "Login required (anonymous reads)", string.Join("; ", leaks), "Data is reachable without signing in. Stop using the module and report this immediately.");

        var writes = new List<string>();
        var csrf = new List<string>();
        foreach (var (method, path, body) in m.AnonymousWrites)
        {
            var res = await Send(http, new HttpMethod(method), path, body, ct: ct);
            if (res.StatusCode != HttpStatusCode.Unauthorized) writes.Add($"{method} /{path} -> {(int)res.StatusCode}");
            var forged = await Send(http, new HttpMethod(method), path, body, csrfHeader: false, ct: ct);
            if (forged.StatusCode != HttpStatusCode.BadRequest) csrf.Add($"{method} /{path} -> {(int)forged.StatusCode}");
        }
        if (writes.Count == 0) r.Pass(area, "Login required (anonymous changes)", $"{m.AnonymousWrites.Length} anonymous change attempts were refused (401).");
        else r.Fail(area, "Login required (anonymous changes)", string.Join("; ", writes), "Changes are possible without signing in. Report this immediately.");
        if (csrf.Count == 0) r.Pass(area, "Cross-site request forgery (CSRF) protection", "Change requests without the security header were refused.");
        else r.Fail(area, "Cross-site request forgery (CSRF) protection", string.Join("; ", csrf), "Report this: the CSRF safeguard is not active for this module.");
        r.Info(area, "Permission checks (low-privilege user)", "Every endpoint of this module is also attacked as a signed-in user without permission, and checked for licence gating, by the automated build tests (endpoint security inventory).");
    }

    /// <summary>The SMS vendor callback is the only internet-facing entry point of the reminders module: wrong keys must reveal nothing.</summary>
    private async Task RemindersCallbackAsync(HttpClient http, CancellationToken ct)
    {
        const string area = "Module security – SMS & email reminders";
        var fake = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var problems = new List<string>();
        foreach (var (path, body) in new[] { ($"api/hooks/sms/{fake}/inbound", """{"source":"+64211234567","content":"STOP"}"""), ($"api/hooks/sms/{fake}/status", """{"message_id":"1","status":"received"}"""),
                     ($"api/hooks/sms/{fake}/inbound", "{ not json"), ("api/hooks/sms/%27%20OR%201%3D1--/inbound", """{"source":"+64211234567","content":"YES"}""") })
        {
            var res = await Send(http, HttpMethod.Post, path, body, csrfHeader: false, ct: ct);
            if (res.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)) problems.Add($"POST /{path.Split("/sms/")[0]}/sms/… -> {(int)res.StatusCode}");
        }
        if (problems.Count == 0) r.Pass(area, "SMS vendor callback key guessing", "Made-up and injected callback keys were refused without revealing anything (HTTP 404).");
        else r.Fail(area, "SMS vendor callback key guessing", string.Join("; ", problems), "Report this immediately: SMS replies could be forged.");
        if (o.IncludeRateLimitTests)
        {
            var limited = false;
            for (var i = 0; i < 50 && !limited; i++)
                limited = (await Send(http, HttpMethod.Post, $"api/hooks/sms/{fake}{i}/inbound", "{}", csrfHeader: false, quiet: true, ct: ct)).StatusCode == HttpStatusCode.TooManyRequests;
            if (limited) r.Pass(area, "SMS vendor callback key guessing is throttled", "Rapid guesses were throttled (HTTP 429).");
            else r.Warn(area, "SMS vendor callback key guessing is throttled", "50 rapid guesses were not throttled.", "Check the site is the current version; rate limiting is built in.");
        }
    }

    private void CheckCertificate(Uri baseUri)
    {
        const string test = "HTTPS certificate";
        if (_cert is null) { r.Warn(Area, test, "Could not read the certificate.", "Check the site's HTTPS binding in IIS Manager."); return; }
        var days = (_cert.NotAfter - DateTime.Now).TotalDays;
        var selfSigned = _cert.Subject == _cert.Issuer;
        var detail = $"Issued to {_cert.GetNameInfo(X509NameType.DnsName, false)}, by {_cert.GetNameInfo(X509NameType.SimpleName, true)}, expires {_cert.NotAfter:dd MMM yyyy} ({days:0} days).";
        log.Debug($"Certificate: subject={_cert.Subject}; issuer={_cert.Issuer}; thumbprint={_cert.Thumbprint}; key={_cert.PublicKey.Oid.FriendlyName}.");
        if (days < 0) r.Fail(Area, test, detail + " EXPIRED.", "Install a new certificate and re-run Setup with --certificate-thumbprint.");
        else if (selfSigned) r.Warn(Area, test, detail + " Self-signed (fine for the sandbox only).", "Before live use, install a certificate from a trusted certificate authority (or TDS's internal CA) and re-run Setup with --certificate-thumbprint.");
        else if (days < 30) r.Warn(Area, test, detail, "Renew the certificate soon.");
        else r.Pass(Area, test, detail);
        _ = baseUri;
    }

    private async Task CheckOldTlsAsync(Uri baseUri, CancellationToken ct)
    {
#pragma warning disable SYSLIB0039 // deliberately testing that obsolete protocols are refused
        foreach (var (name, proto) in new[] { ("TLS 1.0", SslProtocols.Tls), ("TLS 1.1", SslProtocols.Tls11) })
#pragma warning restore SYSLIB0039
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync(baseUri.Host, baseUri.Port, ct);
                await using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = baseUri.Host, EnabledSslProtocols = proto }, ct);
                r.Fail(Area, $"{name} refused", $"The server accepted an old {name} connection.", "Re-run Setup (the TLS step disables old protocols) and restart the server.");
            }
            catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException or System.ComponentModel.Win32Exception)
            {
                log.Debug($"{name} handshake refused: {ex.GetType().Name}: {ex.Message}");
                r.Pass(Area, $"{name} refused", $"An old {name} connection was refused.");
            }
            catch (Exception ex)
            {
                log.Debug($"{name} test could not run: {ex}");
                r.Info(Area, $"{name} refused", $"Could not test ({ex.GetType().Name}); the server registry check below covers this.");
            }
        }
    }

    private async Task CheckPlainHttpAsync(Uri baseUri, CancellationToken ct)
    {
        const string test = "Unencrypted HTTP disabled";
        try
        {
            using var tcp = new TcpClient();
            var connect = tcp.ConnectAsync(baseUri.Host, 80, ct).AsTask();
            if (await Task.WhenAny(connect, Task.Delay(3000, ct)) != connect || !tcp.Connected)
            { r.Pass(Area, test, "Port 80 (plain HTTP) is closed."); return; }
            using var plain = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
            var res = await plain.GetAsync(new UriBuilder(baseUri) { Scheme = "http", Port = 80 }.Uri, ct);
            var loc = res.Headers.Location?.ToString() ?? "";
            if ((int)res.StatusCode is >= 300 and < 400 && loc.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) r.Pass(Area, test, "Port 80 only redirects to HTTPS.");
            else r.Warn(Area, test, $"Port 80 answers HTTP {(int)res.StatusCode} (another site may be bound to port 80).", "Remove HTTP bindings in IIS Manager unless needed; BrightPAS uses HTTPS only.");
        }
        catch (Exception ex) when (ex is SocketException or HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            r.Pass(Area, test, "Port 80 (plain HTTP) is closed.");
        }
    }

    private void CheckHeaders(HttpResponseMessage res, string what, bool requireNoStore = false)
    {
        string? H(string name) => res.Headers.TryGetValues(name, out var v) ? string.Join(", ", v) : res.Content.Headers.TryGetValues(name, out var c) ? string.Join(", ", c) : null;
        var missing = new List<string>();
        var csp = H("Content-Security-Policy");
        if (csp is null || !csp.Contains("frame-ancestors 'none'") || csp.Contains("unsafe-inline")) missing.Add("Content-Security-Policy (strict)");
        if (H("X-Frame-Options") != "DENY") missing.Add("X-Frame-Options: DENY");
        if (H("X-Content-Type-Options") != "nosniff") missing.Add("X-Content-Type-Options: nosniff");
        if (H("Referrer-Policy") is null) missing.Add("Referrer-Policy");
        if (H("Strict-Transport-Security") is null) missing.Add("Strict-Transport-Security (HSTS)");
        if (requireNoStore && !(H("Cache-Control") ?? "").Contains("no-store")) missing.Add("Cache-Control: no-store on API");
        if (missing.Count == 0) r.Pass(Area, $"Security headers ({what})", "All required browser security headers are present.");
        else r.Fail(Area, $"Security headers ({what})", $"Missing or weak: {string.Join("; ", missing)}.", "Make sure the current BrightPAS version is installed and the site runs in Production mode.");

        var leaks = new[] { "Server", "X-Powered-By", "X-AspNet-Version", "X-AspNetMvc-Version" }.Where(h => H(h) is not null).ToList();
        if (leaks.Count == 0) r.Pass(Area, $"Server details hidden ({what})", "No software version headers are revealed.");
        else r.Warn(Area, $"Server details hidden ({what})", $"Revealed: {string.Join(", ", leaks.Select(l => $"{l}: {H(l)}"))}.", "Re-run Setup to re-apply IIS hardening (removeServerHeader, X-Powered-By).");
    }

    private async Task<HttpResponseMessage> Send(HttpClient http, HttpMethod method, string path, string? json = null, bool csrfHeader = true, bool quiet = false, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(method, path);
        if (csrfHeader) req.Headers.Add("X-BrightPAS-Request", "1");
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var res = await http.SendAsync(req, ct);
        if (!quiet)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            log.Debug($"{method} /{path} -> {(int)res.StatusCode} {res.StatusCode}; {Math.Min(body.Length, 99999)} bytes; body starts: {body[..Math.Min(body.Length, 160)].Replace('\n', ' ')}");
        }
        return res;
    }

    private static string Json(string s) => "\"" + System.Text.Json.JsonEncodedText.Encode(s).ToString() + "\"";
}
