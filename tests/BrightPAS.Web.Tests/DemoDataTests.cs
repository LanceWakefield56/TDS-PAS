using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Data;
using BrightPAS.Web.Demo;

namespace BrightPAS.Web.Tests;

/// <summary>
/// A database built exactly as Setup builds it (the installer's SQL scripts, including the cross-schema foreign keys),
/// with licence keys switched off like a test build – used to load the 0.12.3 demonstration data end to end.
/// </summary>
public sealed class InstallerScriptWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _db = string.Empty;
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(SqlServerFactAttribute.Cs)) return;
        _db = "BrightPASDemo_" + Guid.NewGuid().ToString("N")[..10];
        ConnectionString = new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs) { InitialCatalog = _db }.ConnectionString;
        await using (var master = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await master.OpenAsync();
            await new SqlCommand($"CREATE DATABASE [{_db}]; ALTER DATABASE [{_db}] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [{_db}] SET ALLOW_SNAPSHOT_ISOLATION ON;", master).ExecuteNonQueryAsync();
        }
        var dir = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(dir, "src", "Installer", "BrightPAS.Setup", "Database"))) dir = Path.GetDirectoryName(dir)!;
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        foreach (var file in Directory.GetFiles(Path.Combine(dir, "src", "Installer", "BrightPAS.Setup", "Database"), "*.sql").OrderBy(f => f, StringComparer.Ordinal))
            foreach (var batch in System.Text.RegularExpressions.Regex.Split(await File.ReadAllTextAsync(file), @"^\s*GO\s*$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                if (!string.IsNullOrWhiteSpace(batch)) await new SqlCommand(batch, conn) { CommandTimeout = 300 }.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); // no background workers: the test runs the loader itself
        builder.UseSetting("ConnectionStrings:Pas", ConnectionString);
        builder.UseSetting("Licensing:MachineId", "test-machine");
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(Path.GetTempPath(), _db, "keys"));
    }

    public HttpClient NewClient()
    {
        var c = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        c.DefaultRequestHeaders.Add("X-BrightPAS-Request", "1");
        return c;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (string.IsNullOrEmpty(_db)) return;
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs) { InitialCatalog = "master" }.ConnectionString);
        await conn.OpenAsync();
        await new SqlCommand($"ALTER DATABASE [{_db}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_db}];", conn).ExecuteNonQueryAsync();
    }
}

public class DemoDataTests(InstallerScriptWebFactory factory) : IClassFixture<InstallerScriptWebFactory>
{
    [SqlServerFact]
    public async Task Demo_data_fills_a_new_database_through_the_real_services_and_every_module_is_set_up()
    {
        DemoDataSeeder.Summary summary;
        await using (var scope = factory.Services.CreateAsyncScope())
            summary = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().RunAsync();
        Assert.Equal(DemoAccounts.All.Count, summary.Users);
        Assert.True(summary.Patients >= 80, $"patients {summary.Patients}");
        Assert.True(summary.Sessions > 100 && summary.Bookings > 200 && summary.Invoices > 80 && summary.Referrals >= 20 && summary.LabReports >= 10 && summary.Messages > 10, summary.ToString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var core = scope.ServiceProvider.GetRequiredService<PasDbContext>();
            var setUp = await core.ModuleSetup.Select(m => m.ModuleId).ToListAsync();
            foreach (var m in BrightPAS.Web.Licensing.ModuleSetupService.WithWizard) Assert.Contains(m, setUp);
            Assert.NotNull((await core.SetupState.SingleAsync()).BootstrapCodeUsedAt);
            Assert.True((await scope.ServiceProvider.GetRequiredService<BrightPAS.Core.Audit.AuditQueryService>().VerifyAsync()).Intact, "audit chain");
            // A test SMS (no patient) is stored – the 0.12.3 fix for the reminders foreign key.
            Assert.True(await scope.ServiceProvider.GetRequiredService<BrightPAS.Modules.Reminders.RemindersDbContext>().Messages.AnyAsync(m => m.PatientId == null));

            // 0.23.0: the patient journey reads every module for every demo patient without a SQL translation error, and the demo covers every stage.
            var journey = scope.ServiceProvider.GetRequiredService<BrightPAS.Web.Journey.JourneyService>();
            var ids = await scope.ServiceProvider.GetRequiredService<BrightPAS.Modules.Patients.PatientsDbContext>().Patients.Select(p => p.Id).ToListAsync();
            var states = new HashSet<string>(StringComparer.Ordinal);
            var withNext = 0;
            foreach (var id in ids)
            {
                var v = await journey.ForPatientAsync(id);
                Assert.NotNull(v);
                foreach (var jc in v!.Cases)
                {
                    if (jc.Next is not null) withNext++;
                    foreach (var st in jc.Stages) states.Add($"{st.Key}:{st.State}");
                    Assert.Equal(jc.Stages.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count(), jc.Stages.Count);
                }
            }
            Assert.True(withNext > 20, $"cases with a next step: {withNext}");
            foreach (var expected in new[] { "triage:Now", "booking:Now", "booking:Done", "discharge:Done", "billing:Done", "questionnaire:Waiting", "referral:Now" })
                Assert.Contains(expected, states);
            var today = await journey.TodayAsync(_ => true);
            Assert.Contains(today, t => t.Group == "Before surgery");
            Assert.Contains(today, t => t.Group == "After surgery");
            Assert.Empty(await journey.TodayAsync(_ => false));
            // Today's patients appear on the Today list while they are still in the building (demo lists run on weekdays).
            var day = await journey.DayAsync(BrightPAS.Core.NzTime.Today(scope.ServiceProvider.GetRequiredService<TimeProvider>()));
            Assert.Equal(day.Any(c => c.Column != "Gone home"), today.Any(t => t.Group == "Theatre today"));
        }

        // Demo accounts sign in with the shared password and authenticator key; the demo flag reaches the screens.
        var c = factory.NewClient();
        var status = await (await c.GetAsync("/api/setup/status")).Json();
        Assert.False(status.GetProperty("firstAdminNeeded").GetBoolean());
        Assert.Equal("done", status.GetProperty("demo").GetProperty("state").GetString());
        var login = await (await c.PostAsJsonAsync("/api/auth/login", new { userName = "reception", password = DemoAccounts.Password })).Json();
        Assert.Equal("mfa", login.GetProperty("next").GetString());
        var mfa = await (await c.PostAsJsonAsync("/api/auth/mfa", new { code = TestAuthenticator.Code(DemoAccounts.TotpSecret) })).Json();
        Assert.Equal("done", mfa.GetProperty("next").GetString());
        var me = await (await c.GetAsync("/api/me")).Json();
        Assert.True(me.GetProperty("demo").GetBoolean());

        // 0.12.4 (found by the test robot): "Send test" answered 500 although the message was sent – test messages have no patient.
        var a = factory.NewClient();
        await a.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = DemoAccounts.Password });
        Assert.Equal(HttpStatusCode.OK, (await a.PostAsJsonAsync("/api/auth/mfa", new { code = TestAuthenticator.Code(DemoAccounts.TotpSecret) })).StatusCode);
        var test = await a.PostAsJsonAsync("/api/reminders/test", new { channel = "Sms", to = "021 555 0101" });
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.Equal("Sent", (await test.Json()).GetProperty("status").GetString());
        var noPatient = await (await a.GetAsync($"/api/reminders/messages?patientId={Guid.Empty}")).Json();
        Assert.True(noPatient.GetArrayLength() > 0);
        // 0.12.4 (test robot): anaesthetists triage pre-admission forms, so they must be able to list them.
        var an = factory.NewClient();
        await an.PostAsJsonAsync("/api/auth/login", new { userName = "bkowalski", password = DemoAccounts.Password });
        Assert.Equal(HttpStatusCode.OK, (await an.PostAsJsonAsync("/api/auth/mfa", new { code = TestAuthenticator.Code(DemoAccounts.TotpSecret) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await an.GetAsync("/api/forms?view=to-triage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await an.PostAsJsonAsync("/api/forms", new { patientId = Guid.NewGuid() })).StatusCode); // sending still needs forms.send

        // Never twice, never on a database with users.
        await using (var scope = factory.Services.CreateAsyncScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().RunAsync());
    }
}
