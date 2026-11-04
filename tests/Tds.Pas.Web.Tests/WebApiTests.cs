using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tds.Pas.Core.Data;
using Tds.Pas.Core.Setup;
using Tds.Pas.Identity;
using Tds.Pas.Modules.Patients;
using Tds.Pas.Shared;

namespace Tds.Pas.Web.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public static string? Cs => Environment.GetEnvironmentVariable("PAS_TEST_SQLSERVER");
    public SqlServerFactAttribute() { if (string.IsNullOrWhiteSpace(Cs)) Skip = "Set PAS_TEST_SQLSERVER to run web integration tests."; }
}

/// <summary>Runs the real web app (in memory) against a throw-away SQL Server database.</summary>
public sealed class PasWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _db = string.Empty;
    public string ConnectionString { get; private set; } = string.Empty;
    public string BootstrapCode { get; } = Shared.BootstrapCode.Generate();

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(SqlServerFactAttribute.Cs)) return;
        _db = "TdsPasWeb_" + Guid.NewGuid().ToString("N")[..10];
        ConnectionString = new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs) { InitialCatalog = _db }.ConnectionString;
        void Sql(DbContextOptionsBuilder o, string schema) => o.UseSqlServer(ConnectionString, s => s.MigrationsHistoryTable("__EFMigrationsHistory", schema));
        await using (var core = new PasDbContext(new DbContextOptionsBuilder<PasDbContext>().Also(o => Sql(o, "core")).Options))
        {
            await core.Database.MigrateAsync();
            core.SetupState.Add(new SetupState { BootstrapCodeHash = Shared.BootstrapCode.Hash(BootstrapCode), BootstrapCodeCreatedAt = DateTimeOffset.UtcNow });
            await core.SaveChangesAsync();
        }
        await using (var id = new PasIdentityDbContext(new DbContextOptionsBuilder<PasIdentityDbContext>().Also(o => Sql(o, "identity")).Options))
            await id.Database.MigrateAsync();
        await using (var pt = new PatientsDbContext(new DbContextOptionsBuilder<PatientsDbContext>().Also(o => Sql(o, "patients")).Options))
            await pt.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:Pas", ConnectionString);
    }

    public HttpClient NewClient()
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add("X-PAS-Request", "1");
        return c;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (string.IsNullOrEmpty(_db)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs) { InitialCatalog = "master" }.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"ALTER DATABASE [{_db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_db}];", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

internal static class Ext
{
    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
    public static async Task<JsonElement> Json(this HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
}

public class WebApiTests(PasWebFactory factory) : IClassFixture<PasWebFactory>
{
    private const string Pw = "correct horse battery staple";

    private async Task EnsureAdminAsync()
    {
        using var c = factory.NewClient();
        var status = await (await c.GetAsync("/api/setup/status")).Json();
        if (!status.GetProperty("firstAdminNeeded").GetBoolean()) return;
        var start = await (await c.PostAsJsonAsync("/api/setup/first-admin/start", new { code = factory.BootstrapCode, userName = "admin", displayName = "Admin", password = Pw })).Json();
        var key = start.GetProperty("sharedKey").GetString()!;
        var done = await c.PostAsJsonAsync("/api/setup/first-admin/complete", new { code = factory.BootstrapCode, userName = "admin", totpCode = TestAuthenticator.Code(key) });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        AdminKey = key;
    }

    private static string? AdminKey;

    private async Task<HttpClient> SignInNewUserAsync(string userName, string role)
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
        var login = await (await c.PostAsJsonAsync("/api/auth/login", new { userName, password = Pw })).Json();
        Assert.Equal("enrol", login.GetProperty("next").GetString());
        var begin = await (await c.PostAsJsonAsync("/api/auth/enrol/begin", new { })).Json();
        var complete = await (await c.PostAsJsonAsync("/api/auth/enrol/complete", new { code = TestAuthenticator.Code(begin.GetProperty("sharedKey").GetString()!) })).Json();
        Assert.Equal("done", complete.GetProperty("next").GetString());
        Assert.Equal(10, complete.GetProperty("recoveryCodes").GetArrayLength());
        return c;
    }

    [SqlServerFact]
    public async Task Anonymous_requests_to_protected_apis_are_refused_and_csrf_header_is_required()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/patients?q=smith")).StatusCode);
        var noHeader = await c.PostAsJsonAsync("/api/auth/login", new { userName = "x", password = "y" });
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/does-not-exist")).StatusCode);
    }

    [SqlServerFact]
    public async Task Responses_carry_security_headers_and_hide_the_server()
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var r = await c.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(r.Headers.Contains("Server"));
    }

    [SqlServerFact]
    public async Task First_admin_signs_in_with_password_and_code_but_has_no_clinical_access()
    {
        await EnsureAdminAsync();
        using var c = factory.NewClient();
        var login = await (await c.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = Pw })).Json();
        Assert.Equal("mfa", login.GetProperty("next").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode); // not signed in until MFA

        var wrong = await (await c.PostAsJsonAsync("/api/auth/mfa", new { code = "000000" })).Json();
        Assert.Equal("retry", wrong.GetProperty("next").GetString());
        var ok = await (await c.PostAsJsonAsync("/api/auth/mfa", new { code = TestAuthenticator.Code(AdminKey!) })).Json();
        Assert.Equal("done", ok.GetProperty("next").GetString());

        var me = await (await c.GetAsync("/api/me")).Json();
        Assert.Contains(DefaultRoles.SystemAdministrator, me.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/patients?q=a")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode);
    }

    [SqlServerFact]
    public async Task Wrong_password_does_not_reveal_whether_the_user_exists()
    {
        using var c = factory.NewClient();
        var unknown = await (await c.PostAsJsonAsync("/api/auth/login", new { userName = "nobody.here", password = Pw })).Json();
        Assert.Equal("failed", unknown.GetProperty("next").GetString());
    }

    [SqlServerFact]
    public async Task Nurse_enrols_mfa_then_registers_searches_views_and_adds_alert()
    {
        await EnsureAdminAsync();
        using var c = await SignInNewUserAsync("nurse.web", DefaultRoles.Nurse);

        var reg = await c.PostAsJsonAsync("/api/patients", new { familyName = "Ngātai", givenNames = "Aroha", dateOfBirth = "1980-05-17", sex = "Female", nhi = "ZSC21TN", ethnicityCodes = new[] { "21111" } });
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        var patient = await reg.Json();
        var id = patient.GetProperty("id").GetString();
        Assert.StartsWith("TDS", patient.GetProperty("mrn").GetString());

        var dup = await c.PostAsJsonAsync("/api/patients", new { familyName = "Ngatai", givenNames = "Aroha", dateOfBirth = "1980-05-17", sex = "Female" });
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Equal(1, (await dup.Json()).GetProperty("possibleDuplicates").GetArrayLength());

        var found = await (await c.GetAsync("/api/patients?q=ngatai")).Json();
        Assert.Equal(1, found.GetArrayLength());

        var alert = await c.PostAsJsonAsync($"/api/patients/{id}/alerts", new { type = "Allergy", severity = "Critical", description = "Penicillin – anaphylaxis" });
        Assert.Equal(HttpStatusCode.OK, alert.StatusCode);
        var detail = await (await c.GetAsync($"/api/patients/{id}")).Json();
        Assert.Equal("Critical", detail.GetProperty("alerts")[0].GetProperty("severity").GetString());
    }

    [SqlServerFact]
    public async Task Cssd_technician_cannot_register_patients()
    {
        await EnsureAdminAsync();
        using var c = await SignInNewUserAsync("cssd.web", DefaultRoles.CssdTechnician);
        var r = await c.PostAsJsonAsync("/api/patients", new { familyName = "X", givenNames = "Y", dateOfBirth = "1980-01-01", sex = "Male" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
