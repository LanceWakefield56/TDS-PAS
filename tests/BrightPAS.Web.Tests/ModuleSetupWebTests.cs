using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Identity;
using BrightPAS.Licensing;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Tests;

/// <summary>Licence &amp; modules screen and module set-up wizards (ADR 0014), with its own database so the licence state is independent of other tests.</summary>
public class ModuleSetupWebTests(PasWebFactory factory) : IClassFixture<PasWebFactory>
{
    private const string Pw = "correct horse battery staple";

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

    private async Task LicenceAsync(string[] modules)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<LicensingService>();
        var pem = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../../licensing/dev/vendor-private-key.DEVELOPMENT-ONLY.pem"));
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(pem);
        var text = LicenceCodec.Sign(new LicenceDocument
        {
            LicenceId = "BP-MOD-" + Guid.NewGuid().ToString("N")[..6],
            Customer = "Test",
            Edition = "Sandbox",
            IssuedAt = DateTimeOffset.UtcNow,
            ExpiresOn = new DateOnly(2030, 1, 1),
            InstallationCode = svc.InstallationCode(await svc.StateAsync()),
            Modules = modules,
            GraceDays = 0
        }, key);
        var r = await svc.InstallAsync(text, "admin", null);
        Assert.True(r.Ok, r.Error);
    }

    private static JsonElement Module(JsonElement list, string id) => list.EnumerateArray().Single(m => m.GetProperty("id").GetString() == id);

    [SqlServerFact]
    public async Task Licence_upload_over_http_then_wizard_guides_module_to_active()
    {
        using var admin = await SignInAsync("admin.mod", DefaultRoles.SystemAdministrator);

        // Not licensed: shown as such, set-up refused, waitlist API blocked.
        var before = await (await admin.GetAsync("/api/admin/modules")).Json();
        Assert.Equal("NotLicensed", Module(before, "referrals").GetProperty("state").GetString());
        Assert.Equal("ComingLater", Module(before, "quality").GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/modules/referrals/setup/complete", new { })).StatusCode);

        // A tampered licence is rejected with a reason; the real one installs through the same API the screen uses.
        var bad = await admin.PostAsJsonAsync("/api/admin/licence", new { text = "-----BEGIN BRIGHTPAS LICENCE-----\nnot a licence\n-----END BRIGHTPAS LICENCE-----" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.NotEmpty((await bad.Json()).GetProperty("errors")[0].GetString()!);
        await LicenceAsync(["referrals", "theatre", "reminders"]);

        var after = await (await admin.GetAsync("/api/admin/modules")).Json();
        var referrals = Module(after, "referrals");
        Assert.Equal("NeedsSetUp", referrals.GetProperty("state").GetString());
        Assert.True(referrals.GetProperty("requiredStepsOpen").GetInt32() > 0);
        var me = await (await admin.GetAsync("/api/me")).Json();
        Assert.False(me.GetProperty("moduleSetup").TryGetProperty("referrals", out _));

        // Wizard: required steps listed with actions; finishing early is refused with the missing steps named.
        var setup = await (await admin.GetAsync("/api/admin/modules/referrals/setup")).Json();
        var checks = setup.GetProperty("checks").EnumerateArray().ToList();
        Assert.Contains(checks, c => c.GetProperty("key").GetString() == "targets" && !c.GetProperty("ok").GetBoolean());
        var early = await admin.PostAsJsonAsync("/api/admin/modules/referrals/setup/complete", new { });
        Assert.Contains("Waiting-time targets", (await early.Json()).GetProperty("errors")[0].GetString());

        // Do the steps: confirm targets (admin), staff accounts exist.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/referrals/settings", new { urgentDays = 30, semiUrgentDays = 90, routineDays = 120 })).StatusCode);
        using (await SignInAsync("dr.triage", DefaultRoles.Surgeon)) { }
        using (await SignInAsync("rec.waitlist", DefaultRoles.Reception)) { }
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/modules/referrals/setup/complete", new { })).StatusCode);

        var done = await (await admin.GetAsync("/api/admin/modules")).Json();
        Assert.Equal("Active", Module(done, "referrals").GetProperty("state").GetString());
        me = await (await admin.GetAsync("/api/me")).Json();
        Assert.True(me.GetProperty("moduleSetup").GetProperty("referrals").GetBoolean());

        // Unknown or coming-later modules have no wizard.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/modules/quality/setup")).StatusCode);
        // Re-open puts it back to needs set-up.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/modules/referrals/setup/reopen", new { })).StatusCode);
        Assert.Equal("NeedsSetUp", Module(await (await admin.GetAsync("/api/admin/modules")).Json(), "referrals").GetProperty("state").GetString());
    }

    [SqlServerFact]
    public async Task Non_admin_cannot_use_module_setup_and_callback_hides_licence_state()
    {
        using var nurse = await SignInAsync("nurse.mod", DefaultRoles.Nurse);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.GetAsync("/api/admin/modules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.PostAsJsonAsync("/api/admin/modules/referrals/setup/complete", new { })).StatusCode);

        // A wrong SMS callback key answers 404 whether or not the module is licensed (the key is checked first).
        using var anon = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var r = await anon.PostAsJsonAsync("/api/hooks/sms/not-the-key/inbound", new { source = "+64211234567", content = "STOP" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.DoesNotContain("licensed", await r.Content.ReadAsStringAsync());
    }

    [SqlServerFact]
    public async Task Roles_can_be_edited_safely_and_removed_permissions_are_not_re_added()
    {
        using var admin = await SignInAsync("admin.roles", DefaultRoles.SystemAdministrator);
        var data = await (await admin.GetAsync("/api/admin/roles")).Json();
        var reception = data.GetProperty("roles").EnumerateArray().Single(r => r.GetProperty("name").GetString() == DefaultRoles.Reception);
        var perms = reception.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        Assert.Contains(Permissions.TheatreManage, perms);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/roles",
            new { name = DefaultRoles.Reception, permissions = perms.Where(p => p != Permissions.TheatreManage).ToArray() })).StatusCode);

        // Restarting (role seeding) must not put the removed permission back.
        await using (var scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<RoleSeeder>().EnsureDefaultRolesAsync();
        data = await (await admin.GetAsync("/api/admin/roles")).Json();
        reception = data.GetProperty("roles").EnumerateArray().Single(r => r.GetProperty("name").GetString() == DefaultRoles.Reception);
        Assert.DoesNotContain(Permissions.TheatreManage, reception.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));

        var lockout = await admin.PutAsJsonAsync("/api/admin/roles", new { name = DefaultRoles.SystemAdministrator, permissions = new[] { Permissions.ModulesManage } });
        Assert.Equal(HttpStatusCode.BadRequest, lockout.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync("/api/admin/roles", new { name = DefaultRoles.Nurse, permissions = new[] { "made.up" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/roles", new { name = "Theatre Coordinator", description = "Lists only" })).StatusCode);

        // Organisation administration, and the reference lists every user may read.
        var org = await (await admin.GetAsync("/api/admin/organisation")).Json();
        Assert.Equal("Tasman Day Surgery", org.GetProperty("organisationName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/organisation/clinicians",
            new { name = "Dr Ben Hall", type = "Surgeon", hpiCpn = "12ABCD", isActive = true })).StatusCode);
        using var nurse = await SignInAsync("nurse.org", DefaultRoles.Nurse);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.GetAsync("/api/admin/organisation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.GetAsync("/api/admin/roles")).StatusCode);
        var list = await (await nurse.GetAsync("/api/organisation/clinicians")).Json();
        Assert.Equal("Dr Ben Hall", list[0].GetProperty("name").GetString());
        Assert.False(list[0].TryGetProperty("hpiCpn", out _)); // reference list shows names only
    }

    [SqlServerFact]
    public async Task Plain_http_only_redirects_to_https_and_never_accepts_data()
    {
        using var http = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://brightpas-srv"), AllowAutoRedirect = false });
        var get = await http.GetAsync("/patients?x=1");
        Assert.Equal(HttpStatusCode.MovedPermanently, get.StatusCode);
        Assert.Equal("https://brightpas-srv/patients?x=1", get.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.MovedPermanently, (await http.GetAsync("/")).StatusCode);
        var post = await http.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "x" });
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Contains("https_required", await post.Content.ReadAsStringAsync());
        // Over HTTPS the same request reaches the app, and HSTS tells the browser to stay on HTTPS.
        using var https = factory.NewClient();
        var home = await https.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("max-age=31536000", home.Headers.GetValues("Strict-Transport-Security").Single());
    }
}
