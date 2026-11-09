using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Tests;

/// <summary>
/// Security inventory of EVERY API endpoint, including endpoints added by future modules (ADR 0013).
/// A new endpoint that is not protected, not licence-gated, or public without being on the reviewed allow-list fails the build.
/// Then every endpoint is attacked: anonymously, without the CSRF header, and as a low-privilege user.
/// </summary>
public class EndpointSecurityTests(PasWebFactory factory) : IClassFixture<PasWebFactory>
{
    private const string Pw = "correct horse battery staple";

    /// <summary>Reviewed public endpoints: (route pattern, reason, must be rate-limited).</summary>
    public static readonly (string Route, string Why, bool RateLimited)[] PublicAllowList =
    [
        ("/health", "Liveness check for the installer and monitoring; returns only the version.", false),
        ("/api/setup/status", "Tells the browser whether first-administrator set-up is needed; no data.", false),
        ("/api/setup/first-admin/start", "One-time installer code + MFA set-up.", true),
        ("/api/setup/first-admin/complete", "One-time installer code + MFA set-up.", true),
        ("/api/auth/login", "Sign-in step 1.", true),
        ("/api/auth/mfa", "Sign-in step 2 (checks the pending sign-in cookie).", true),
        ("/api/auth/enrol/begin", "MFA enrolment (checks the pending sign-in cookie).", true),
        ("/api/auth/enrol/complete", "MFA enrolment (checks the pending sign-in cookie).", true),
        ("/api/auth/logout", "Signing out must always work.", true),
        ("/api/q/{token}/", "Patient questionnaire: secret link + date of birth, lockout after 5 attempts.", true),
        ("/api/q/{token}/open", "Patient questionnaire.", true),
        ("/api/q/{token}/save", "Patient questionnaire.", true),
        ("/api/q/{token}/submit", "Patient questionnaire.", true),
        ("/api/hooks/sms/{key}/inbound", "SMS vendor callback: secret key in URL, constant-time check.", true),
        ("/api/hooks/sms/{key}/status", "SMS vendor callback: secret key in URL, constant-time check.", true),
        ("/api/my/{token}/open", "Patient portal: secret link + date of birth, lockout after 5 attempts.", true),
        ("/api/my/{token}/requests", "Patient portal: secret link + date of birth on every call.", true),
        ("/api/my/{token}/documents", "Patient portal upload: link + date of birth, 5 files a day, 10 MB, PDF/images only.", true),
        ("/api/my/{token}/questionnaire", "Patient portal: link + date of birth; issues a pre-admission form link for the next booking.", true),
        ("/api/my/{token}/estimates/{id:guid}/accept", "Patient portal: link + date of birth; accepts only that patient's issued estimate.", true),
        ("/api/integration/in/{code}", "Inbound REST interface: per-interface key in X-BrightPAS-Key (constant-time check), 5 MB limit, licence-gated.", true),
        ("/api/mcp", "MCP server for AI tools: bearer token (SHA-256 stored, expiring, revocable), read-only, never message content; audited.", true),
        ("/api/{**rest}", "Unknown API routes answer 404.", false),
        ("/{*path:nonfile}", "The browser app shell (index.html); holds no data.", false),
    ];

    /// <summary>Signed-in users without a specific permission are allowed only here.</summary>
    private static readonly string[] AnySignedInUser = ["/api/me", "/api/export-policy", "/api/organisation/locations", "/api/organisation/clinicians", "/api/organisation/procedures"];

    /// <summary>Each module's URL prefix and the licence module it must be gated by.</summary>
    private static readonly (string Prefix, string? Module)[] Areas =
    [
        ("/api/labs", "labs"), ("/api/theatre", "theatre"), ("/api/forms", "forms"), ("/api/q/", "forms"), ("/api/billing", "billing"),
        ("/api/reminders", "reminders"), ("/api/hooks/sms", "reminders"), ("/api/referrals", "referrals"), ("/api/waitlist", "referrals"), ("/api/letters", "letters"), ("/api/roster", "rostering"), ("/api/credentialing", "credentialing"), ("/api/portal", "portal"), ("/api/my/", "portal"), ("/api/messaging", "messaging"), ("/api/integration/in", "messaging"), ("/api/mcp", "messaging"), ("/api/export-policy", null),
        ("/api/patients", null), ("/api/admin", null), ("/api/audit", null), ("/api/auth", null), ("/api/setup", null), ("/api/me", null),
        ("/api/organisation", null), ("/api/{**rest}", null), ("/health", null), ("/{*path", null),
    ];

    private IReadOnlyList<RouteEndpoint> Endpoints()
    {
        using var _ = factory.CreateClient();
        return factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
    }

    private static string Route(RouteEndpoint e) => "/" + (e.RoutePattern.RawText ?? "").TrimStart('/');
    private static string[] Methods(RouteEndpoint e) => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.ToArray() ?? ["GET"];
    private static bool IsPublic(RouteEndpoint e) => e.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    [SqlServerFact]
    public void Every_endpoint_is_protected_licence_gated_and_reviewed()
    {
        var problems = new List<string>();
        var eps = Endpoints();
        Assert.True(eps.Count > 60, $"Only {eps.Count} endpoints found – inventory is not seeing the app.");
        foreach (var e in eps)
        {
            var route = Route(e);
            var name = $"{string.Join(",", Methods(e))} {route}";
            var area = Areas.FirstOrDefault(a => route.StartsWith(a.Prefix, StringComparison.OrdinalIgnoreCase));
            if (area.Prefix is null) { problems.Add($"{name}: not in any known area – review its security and add it to the inventory."); continue; }
            if (area.Module is { } module && e.Metadata.GetMetadata<RequiresModuleAttribute>()?.ModuleId != module)
                problems.Add($"{name}: must be gated by the '{module}' licence (RequireModule).");
            if (IsPublic(e))
            {
                var allowed = PublicAllowList.FirstOrDefault(p => string.Equals(p.Route.TrimEnd('/'), route.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                if (allowed.Route is null) problems.Add($"{name}: is public but not on the reviewed allow-list.");
                else if (allowed.RateLimited && e.Metadata.GetMetadata<EnableRateLimitingAttribute>() is null) problems.Add($"{name}: public endpoint must be rate-limited.");
                continue;
            }
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Where(p => p is not null).ToList();
            if (policies.Count == 0 && !AnySignedInUser.Contains(route)) problems.Add($"{name}: needs a specific permission (RequireAuthorization(Permissions.X)).");
            foreach (var p in policies) if (!Permissions.All.Contains(p!)) problems.Add($"{name}: unknown permission '{p}'.");
        }
        Assert.True(problems.Count == 0, "Endpoint security problems:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Every_available_module_has_a_route_area()
    {
        foreach (var m in ProductModules.All.Where(m => m.Available))
            Assert.True(Areas.Any(a => a.Module == m.Id), $"Module '{m.Id}' is available but has no entry in the endpoint security inventory.");
    }

    private static string Fill(string route) => Regex.Replace(route, @"\{([^}:*]+)(:[^}]*)?\}", m =>
        m.Groups[2].Value.Contains("guid") ? Guid.NewGuid().ToString() : m.Groups[2].Value.Contains("int") ? "1" : "zz" + Guid.NewGuid().ToString("N"));

    private IEnumerable<(string Method, string Url, RouteEndpoint E)> Probeable() =>
        Endpoints().Where(e => Route(e).StartsWith("/api/") && !Route(e).Contains("**")).SelectMany(e => Methods(e).Select(m => (m, Fill(Route(e)), e)));

    [SqlServerFact]
    public async Task Anonymous_attacker_gets_nothing_from_any_endpoint()
    {
        await LicenceEverythingAsync();
        using var c = factory.NewClient();
        var problems = new List<string>();
        foreach (var (method, url, e) in Probeable())
        {
            if (url.StartsWith("/api/auth/") || url.StartsWith("/api/setup/")) continue; // covered by the sign-in tests
            using var req = new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json") };
            var res = await c.SendAsync(req);
            var ok = IsPublic(e) ? res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.TooManyRequests or HttpStatusCode.UnsupportedMediaType or HttpStatusCode.Unauthorized
                                 : res.StatusCode == HttpStatusCode.Unauthorized;
            if (!ok) problems.Add($"{method} {url} -> {(int)res.StatusCode}");
        }
        Assert.True(problems.Count == 0, "Anonymous requests that were not refused:\n" + string.Join("\n", problems));
    }

    [SqlServerFact]
    public async Task State_changing_requests_without_the_csrf_header_are_refused()
    {
        // A signed-in administrator's browser tricked by another website: the cookie is sent, the custom header is not.
        using var c = await SignInAsync("csrf.sec", DefaultRoles.SystemAdministrator);
        c.DefaultRequestHeaders.Remove("X-BrightPAS-Request");
        var problems = new List<string>();
        // Machine-to-machine endpoints ignore cookies and authenticate with their own keys, so CSRF does not apply to them.
        foreach (var (method, url, _) in Probeable().Where(p => p.Method != "GET" && !p.Url.StartsWith("/api/hooks/") && !p.Url.StartsWith("/api/integration/in/") && p.Url != "/api/mcp"))
        {
            var res = await c.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
            if (res.StatusCode != HttpStatusCode.BadRequest) problems.Add($"{method} {url} -> {(int)res.StatusCode}");
        }
        Assert.True(problems.Count == 0, "Requests without the CSRF header that were not refused:\n" + string.Join("\n", problems));
    }

    [SqlServerFact]
    public async Task Low_privilege_user_is_refused_everything_outside_their_permissions()
    {
        await LicenceEverythingAsync();
        using var c = await SignInAsync("cssd.sec", DefaultRoles.CssdTechnician); // permissions: patient.view only
        var mine = DefaultRoles.Definitions[DefaultRoles.CssdTechnician].Permissions;
        var problems = new List<string>();
        foreach (var (method, url, e) in Probeable().Where(p => !IsPublic(p.E)))
        {
            var policies = e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Where(p => p is not null).ToList();
            if (policies.Count == 0 || policies.All(mine.Contains)) continue;
            using var req = new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json") };
            var res = await c.SendAsync(req);
            if (res.StatusCode != HttpStatusCode.Forbidden) problems.Add($"{method} {url} -> {(int)res.StatusCode}");
        }
        Assert.True(problems.Count == 0, "Requests by a user without permission that were not refused (403):\n" + string.Join("\n", problems));
    }

    [SqlServerFact]
    public async Task Sms_vendor_callback_rejects_wrong_keys_and_oversized_bodies()
    {
        await LicenceEverythingAsync();
        using var c = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var wrong = await c.PostAsJsonAsync("/api/hooks/sms/not-the-key/inbound", new { source = "+64211234567", content = "STOP" });
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        string key;
        await using (var scope = factory.Services.CreateAsyncScope())
            key = await scope.ServiceProvider.GetRequiredService<BrightPAS.Modules.Reminders.ReminderService>().WebhookKeyAsync();
        var big = await c.PostAsync($"/api/hooks/sms/{key}/inbound", new StringContent("{\"source\":\"+64211234567\",\"content\":\"" + new string('x', 20_000) + "\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, big.StatusCode);
        var ok = await c.PostAsJsonAsync($"/api/hooks/sms/{key}/inbound", new { id = 99, source = "+64211234567", destination = "1234", content = "hello" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    private async Task LicenceEverythingAsync()
    {
        if (factory.Services.GetRequiredService<LicenceCache>().Status.AccessTo("reminders") == ModuleAccess.Full) return;
        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<LicensingService>();
        var pem = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../licensing/dev/vendor-private-key.DEVELOPMENT-ONLY.pem"));
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(pem);
        var text = LicenceCodec.Sign(new LicenceDocument
        {
            LicenceId = "BP-SEC-" + Guid.NewGuid().ToString("N")[..6], Customer = "Test", Edition = "Sandbox", IssuedAt = DateTimeOffset.UtcNow,
            ExpiresOn = new DateOnly(2030, 1, 1), InstallationCode = svc.InstallationCode(await svc.StateAsync()), Modules = ["*"], GraceDays = 0
        }, key);
        var r = await svc.InstallAsync(text, "admin", null);
        Assert.True(r.Ok, r.Error);
    }

    private async Task<HttpClient> SignInAsync(string userName, string role)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PasUser>>();
            if (await users.FindByNameAsync(userName) is null)
            {
                var u = new PasUser { UserName = userName, DisplayName = userName };
                Assert.True((await users.CreateAsync(u, Pw)).Succeeded);
                await users.AddToRoleAsync(u, role);
            }
        }
        var c = factory.NewClient();
        await c.PostAsJsonAsync("/api/auth/login", new { userName, password = Pw });
        var begin = await (await c.PostAsJsonAsync("/api/auth/enrol/begin", new { })).Json();
        var done = await (await c.PostAsJsonAsync("/api/auth/enrol/complete", new { code = TestAuthenticator.Code(begin.GetProperty("sharedKey").GetString()!) })).Json();
        Assert.Equal("done", done.GetProperty("next").GetString());
        return c;
    }
}
