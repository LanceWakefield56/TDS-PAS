using Microsoft.Data.SqlClient;
using BrightPAS.Setup.Checks;
using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Tests;

public class InstallerTests
{
    private sealed class FakeRunner(int exitCode = 0, string output = "ok") : IProcessRunner
    {
        public List<string> Scripts { get; } = [];
        public Task<ProcessResult> RunPowerShellAsync(string script, CancellationToken ct) { Scripts.Add(script); return Task.FromResult(new ProcessResult(exitCode, output)); }
        public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct) { Scripts.Add(fileName + " " + arguments); return Task.FromResult(new ProcessResult(exitCode, output)); }
    }


    private sealed class WindowsProbe : ISystemProbe
    {
        public bool IsWindows => true;
        public Version OsVersion => new(10, 0, 20348);
        public string? WindowsInstallationType => "Server";
        public bool Is64Bit => true;
        public bool IsAdministrator => true;
        public long TotalMemoryBytes => 16 * PrerequisiteChecks.GiB;
        public long? FreeDiskBytes(string drive) => 100 * PrerequisiteChecks.GiB;
        public int? IisMajorVersion => null; // not installed yet – allowed
        public bool AspNetCoreModuleV2Installed => false;
        public IReadOnlyList<Version> AspNetCoreRuntimeVersions => [];
        public bool? TlsProtocolServerEnabled(string protocol) => null;
        public string TimeZoneId => "New Zealand Standard Time";
        public bool CanConnectTcp(string host, int port, TimeSpan timeout) => true;
    }

    [Fact]
    public void Schema_scripts_are_embedded_in_order()
    {
        var names = SqlScripts.Embedded().Select(s => s.Name).ToList();
        Assert.Equal(["01-core.sql", "02-identity.sql", "03-patients.sql", "04-labs.sql", "05-theatre.sql", "06-forms.sql", "07-billing.sql", "08-reminders.sql", "09-referrals.sql", "10-letters.sql", "11-roster.sql", "12-credentialing.sql", "13-portal.sql", "14-messaging.sql", "90-reporting.sql"], names);
    }

    [Fact]
    public void Scripts_split_on_go_lines_only()
    {
        var batches = SqlScripts.SplitBatches("﻿SELECT 1;\nGO\n  go  \nSELECT 'GOOD';\nGO;\n");
        Assert.Equal(["SELECT 1;", "SELECT 'GOOD';"], batches);
    }

    [Fact]
    public async Task Preview_changes_nothing_and_plans_every_step()
    {
        var runner = new FakeRunner();
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL", WhatIf = true, DemoData = true }, new WindowsProbe(), runner, new InstallLog(null, false, console: false), TimeProvider.System);
        var records = await Installer.RunAsync(Installer.PlatformSteps(), ctx);
        Assert.Equal(14, records.Count);
        Assert.Empty(runner.Scripts);
        Assert.All(records.Skip(1), r => Assert.Equal(StepOutcome.WhatIf, r.Result.Outcome));
    }

    [Fact]
    public async Task Installation_stops_at_the_first_failure()
    {
        var runner = new FakeRunner(exitCode: 1, output: "ERROR: Install-WindowsFeature : access denied\nWHERE: line 5");
        var log = new InstallLog(null, false, console: false);
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL" }, new WindowsProbe(), runner, log, TimeProvider.System);
        var records = await Installer.RunAsync(Installer.PlatformSteps(), ctx);
        Assert.Equal(StepOutcome.Failed, records[^1].Result.Outcome);
        Assert.Equal("Install IIS (web server)", records[^1].Step.Name);
        Assert.Contains("exit code 1", records[^1].Result.Message);
        Assert.Contains(log.Lines, l => l.Contains("ERROR: Install-WindowsFeature : access denied") && l.Contains("[INFO ]"));
        Assert.Contains(log.Lines, l => l.Contains("[DEBUG]") && l.Contains("Install-WindowsFeature -Name $features"));
        Assert.Single(runner.Scripts);
    }

    [Fact]
    public async Task Skip_iis_option_skips_iis_steps_but_still_secures_tls()
    {
        var runner = new FakeRunner();
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL", SkipIis = true }, new WindowsProbe(), runner, new InstallLog(null, false, console: false), TimeProvider.System);
        var steps = Installer.PlatformSteps().Take(4);
        var records = await Installer.RunAsync(steps, ctx);
        Assert.Equal(StepOutcome.Skipped, records[1].Result.Outcome);
        Assert.Equal(StepOutcome.Done, records[2].Result.Outcome);
        Assert.Equal(StepOutcome.Skipped, records[3].Result.Outcome);
        Assert.True(ctx.RebootRequired);
    }

    [Fact]
    public async Task Invalid_database_name_is_refused()
    {
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL", Database = "Tds; DROP DATABASE x" }, new WindowsProbe(), new FakeRunner(), new InstallLog(null, false, console: false), TimeProvider.System);
        var r = await new CreateDatabaseStep().RunAsync(ctx, CancellationToken.None);
        Assert.Equal(StepOutcome.Failed, r.Outcome);
    }

    [Fact]
    public void Log_masks_registered_secrets_and_connection_string_passwords()
    {
        var log = new InstallLog(null, false, console: false);
        log.AddSecret("K7QM3XRPWD9TH2BN");
        log.Info("Server=PAS;User ID=sa;Password=Sup3r$ecret;Encrypt=True and code K7QM3XRPWD9TH2BN");
        var line = log.Lines.Single();
        Assert.DoesNotContain("Sup3r$ecret", line);
        Assert.DoesNotContain("K7QM3XRPWD9TH2BN", line);
        Assert.Contains("Password=********", line);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} .* \[INFO \]", line);
    }

    [Fact]
    public void Log_writes_debug_detail_to_file_but_not_console_unless_verbose()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "setup.log");
        using (var log = new InstallLog(path, verbose: false, console: false))
        {
            log.Block("PowerShell output", "line one\nERROR: boom");
            log.Error("It failed", new InvalidOperationException("inner detail"));
        }
        var text = File.ReadAllText(path);
        Assert.Contains("| ERROR: boom", text);
        Assert.Contains("[DEBUG]", text);
        Assert.Contains("[ERROR] It failed", text);
    }

    [Fact]
    public void PowerShell_wrapper_reports_error_location_and_exit_code()
    {
        var w = ProcessRunner.Wrap("throw 'x'");
        Assert.Contains("try {", w);
        Assert.Contains("WHERE: ", w);
        Assert.Contains("exit 1", w);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData(".", true)]
    [InlineData("(local)", true)]
    [InlineData("127.0.0.1:1433", true)]
    [InlineData("PAS-SQL", false)]
    public void Detects_single_server_sql(string server, bool local) =>
        Assert.Equal(local, new InstallOptions { SqlServer = server }.IsLocalSqlServer());

    [Fact]
    public void Security_report_counts_findings_and_renders_html_safely()
    {
        var r = new BrightPAS.Setup.Security.SecurityReport();
        r.Pass("Area", "A", "ok");
        r.Warn("Area", "B", "<script>bad</script>", "fix it");
        Assert.Equal(1, r.ExitCode);
        r.Fail("Area", "C", "broken", "fix");
        Assert.Equal(2, r.ExitCode);
        var html = r.ToHtml();
        Assert.DoesNotContain("<script>bad</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("FAIL", r.ToText());
    }

    [Fact]
    public void Web_package_finder_picks_the_highest_version()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        foreach (var v in new[] { "0.3.0", "0.10.0", "0.9.1" }) File.WriteAllText(Path.Combine(dir, $"BrightPAS-Web-{v}.zip"), "x");
        Assert.EndsWith("BrightPAS-Web-0.10.0.zip", WebPackage.Find(null, dir));
        Assert.Null(WebPackage.Find(Path.Combine(dir, "missing.zip"), dir));
    }

    [Fact]
    public void Generated_app_password_is_long_and_complex()
    {
        var p = WebPackage.GeneratePassword();
        Assert.Equal(32, p.Length);
        Assert.Contains(p, char.IsUpper); Assert.Contains(p, char.IsLower); Assert.Contains(p, char.IsDigit);
        Assert.Contains(p, ch => !char.IsLetterOrDigit(ch));
        Assert.NotEqual(p, WebPackage.GeneratePassword());
    }

    [Fact]
    public void Production_settings_contain_connection_string_and_key_path()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        WebPackage.WriteProductionSettings(dir, "Server=x;Password=a\"b", @"C:\ProgramData\BrightPAS\keys");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "appsettings.Production.json")));
        Assert.Equal("Server=x;Password=a\"b", doc.RootElement.GetProperty("ConnectionStrings").GetProperty("Pas").GetString());
        Assert.Equal(@"C:\ProgramData\BrightPAS\keys", doc.RootElement.GetProperty("DataProtection").GetProperty("KeysPath").GetString());
    }

    [Fact]
    public void Site_script_escapes_values_and_uses_https_only()
    {
        var ps = PowerShellScripts.ConfigureSite("BrightPAS", "BrightPAS", @"C:\Program Files\BrightPAS\Web", "o'brien.local", 443, null);
        Assert.Contains("'o''brien.local'", ps);
        Assert.Contains("-Ssl", ps);
        Assert.Contains("Where-Object { $_.protocol -ne 'https' } | Remove-WebBinding", ps);
        Assert.Contains("if ($true)", ps);
        Assert.Contains("New-WebBinding -Name $site -Protocol http -Port 80", ps);
        Assert.Contains("if ($false)", PowerShellScripts.ConfigureSite("BrightPAS", "BrightPAS", @"C:\x", "h", 443, null, httpRedirect: false));
    }

    [Fact]
    public void Connection_string_requires_encryption_and_uses_custom_port()
    {
        var o = new InstallOptions { SqlServer = "PAS-SQL:14330", IntegratedSecurity = false, SqlUser = "pas_setup", SqlPassword = "x" };
        var b = new SqlConnectionStringBuilder(o.ConnectionString());
        Assert.Equal("PAS-SQL,14330", b.DataSource);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, b.Encrypt);
        Assert.False(b.TrustServerCertificate);
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public static string? Cs => Environment.GetEnvironmentVariable("PAS_TEST_SQLSERVER");
    public SqlServerFactAttribute() { if (string.IsNullOrWhiteSpace(Cs)) Skip = "Set PAS_TEST_SQLSERVER to run SQL Server integration tests."; }
}

public class InstallerDatabaseTests
{

    [SqlServerFact]
    public async Task Database_steps_create_schema_and_bootstrap_code_and_can_run_twice()
    {
        var src = new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs);
        var dbName = "BrightPASSetup_" + Guid.NewGuid().ToString("N")[..10];
        var options = new InstallOptions
        {
            SqlServer = src.DataSource.Replace(',', ':'), Database = dbName, IntegratedSecurity = false,
            SqlUser = src.UserID, SqlPassword = src.Password, TrustServerCertificate = true, Version = "0.2.0"
        };
        var steps = new IInstallStep[] { new CreateDatabaseStep(), new ApplySchemaStep(), new BootstrapCodeStep() };
        try
        {
            var first = new InstallContext(options, new NonWindowsSystemProbe(), new ProcessRunner(), new InstallLog(null, false, console: false), TimeProvider.System);
            var r1 = await Installer.RunAsync(steps, first);
            Assert.All(r1, r => Assert.Equal(StepOutcome.Done, r.Result.Outcome));
            Assert.NotNull(first.BootstrapCode);

            var second = new InstallContext(options, new NonWindowsSystemProbe(), new ProcessRunner(), new InstallLog(null, false, console: false), TimeProvider.System);
            var r2 = await Installer.RunAsync(steps, second);
            Assert.Equal(StepOutcome.AlreadyDone, r2[0].Result.Outcome);
            Assert.Equal(StepOutcome.Done, r2[1].Result.Outcome); // idempotent re-apply
            Assert.Equal(StepOutcome.Done, r2[2].Result.Outcome); // new code while no admin exists

            await using var conn = new SqlConnection(options.ConnectionString());
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT BootstrapCodeHash, InstalledVersion FROM core.SetupState WHERE Id = 1; SELECT ledger_type_desc FROM sys.tables WHERE name = 'AuditEvents'; SELECT COUNT(*) FROM patients.MrnCounter;", conn);
            await using var rd = await cmd.ExecuteReaderAsync();
            Assert.True(await rd.ReadAsync());
            Assert.True(BrightPAS.Shared.BootstrapCode.Verify(second.BootstrapCode!, rd.GetString(0)));
            Assert.False(BrightPAS.Shared.BootstrapCode.Verify(first.BootstrapCode!, rd.GetString(0))); // old code replaced
            Assert.Equal("0.2.0", rd.GetString(1));
            await rd.NextResultAsync(); await rd.ReadAsync();
            Assert.Equal("APPEND_ONLY_LEDGER_TABLE", rd.GetString(0));
            await rd.NextResultAsync(); await rd.ReadAsync();
            Assert.Equal(1, rd.GetInt32(0));
            await rd.CloseAsync();

            // Application login: can read and write data, cannot change the schema.
            var appCtx = new InstallContext(options, new NonWindowsSystemProbe(), new ProcessRunner(), new InstallLog(null, false, console: false), TimeProvider.System,
                new WebDeployOptions { AppSqlLogin = "brightpas_app_" + dbName[^6..] });
            Assert.Equal(StepOutcome.Done, (await new AppDatabaseLoginStep().RunAsync(appCtx, CancellationToken.None)).Outcome);
            await using var app = new SqlConnection(appCtx.AppConnectionString);
            await app.OpenAsync();
            await using (var read = new SqlCommand("SELECT COUNT(*) FROM core.Locations", app)) await read.ExecuteScalarAsync();
            await using (var write = new SqlCommand("INSERT core.Organisations (Id, Name) VALUES (NEWID(), 'Test org')", app)) Assert.Equal(1, await write.ExecuteNonQueryAsync());
            await Assert.ThrowsAsync<SqlException>(async () => { await using var ddl = new SqlCommand("CREATE TABLE core.Hack (Id int)", app); await ddl.ExecuteNonQueryAsync(); });
            await Assert.ThrowsAsync<SqlException>(async () => { await using var del = new SqlCommand("DELETE FROM core.AuditEvents", app); await del.ExecuteNonQueryAsync(); });
            // Running again refreshes the password; the new connection string works.
            Assert.Equal(StepOutcome.Done, (await new AppDatabaseLoginStep().RunAsync(appCtx, CancellationToken.None)).Outcome);
            await using var app2 = new SqlConnection(appCtx.AppConnectionString);
            await app2.OpenAsync();
            await app.CloseAsync(); await app2.CloseAsync();
            SqlConnection.ClearAllPools();
            await using var dropLogin = new SqlCommand($"DROP USER [{appCtx.Web.AppSqlLogin}]; USE master; DROP LOGIN [{appCtx.Web.AppSqlLogin}];", conn);
            await dropLogin.ExecuteNonQueryAsync();
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var master = new SqlConnection(options.ConnectionString("master"));
            await master.OpenAsync();
            await using var drop = new SqlCommand($"IF DB_ID('{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END", master);
            await drop.ExecuteNonQueryAsync();
        }
    }
}

public class DemoDataStepTests
{
    [Fact]
    public async Task Demo_steps_do_nothing_unless_asked()
    {
        var ctx = new InstallContext(new InstallOptions { SqlServer = "nowhere" }, new NonWindowsSystemProbe(), new ProcessRunner(), new InstallLog(null, false, console: false), TimeProvider.System);
        Assert.Equal(StepOutcome.Skipped, (await new RequestDemoDataStep().RunAsync(ctx, CancellationToken.None)).Outcome);
        Assert.Equal(StepOutcome.Skipped, (await new WaitForDemoDataStep().RunAsync(ctx, CancellationToken.None)).Outcome);
        var steps = Installer.PlatformSteps().Select(s => s.GetType()).ToList();
        Assert.True(steps.IndexOf(typeof(RequestDemoDataStep)) > steps.IndexOf(typeof(ApplySchemaStep)));
        Assert.True(steps.IndexOf(typeof(WaitForDemoDataStep)) > steps.IndexOf(typeof(WebHealthCheckStep)));
    }

    [SqlServerFact]
    public async Task Demo_data_is_only_requested_for_a_database_without_users()
    {
        var src = new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs);
        var dbName = "BrightPASDemoStep_" + Guid.NewGuid().ToString("N")[..10];
        var options = new InstallOptions { SqlServer = src.DataSource.Replace(',', ':'), Database = dbName, IntegratedSecurity = false, SqlUser = src.UserID, SqlPassword = src.Password, TrustServerCertificate = true, DemoData = true };
        InstallContext Ctx() => new(options, new NonWindowsSystemProbe(), new ProcessRunner(), new InstallLog(null, false, console: false), TimeProvider.System);
        try
        {
            var r = await Installer.RunAsync([new CreateDatabaseStep(), new ApplySchemaStep(), new RequestDemoDataStep()], Ctx());
            Assert.All(r, x => Assert.Equal(StepOutcome.Done, x.Result.Outcome));
            Assert.Equal(StepOutcome.AlreadyDone, (await new RequestDemoDataStep().RunAsync(Ctx(), CancellationToken.None)).Outcome);

            await using var conn = new SqlConnection(options.ConnectionString());
            await conn.OpenAsync();
            await new SqlCommand("UPDATE core.Settings SET Value = 'done' WHERE [Key] = 'demo.state'; INSERT core.Settings([Key], Value, UpdatedAt, UpdatedBy) VALUES ('demo.detail', '{}', SYSDATETIMEOFFSET(), 'x');", conn).ExecuteNonQueryAsync();
            var waitCtx = new InstallContext(options, new FakeWindowsProbe(), new ProcessRunner(), new InstallLog(null, false, console: false), TimeProvider.System);
            Assert.Equal(StepOutcome.Done, (await new WaitForDemoDataStep(TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(5)).RunAsync(waitCtx, CancellationToken.None)).Outcome);
            Assert.True(waitCtx.DemoDataLoaded);

            // A database that already has people in it is refused (a real installation is never touched).
            await new SqlCommand("DELETE core.Settings WHERE [Key] LIKE 'demo.%'; INSERT [identity].[AspNetUsers] (Id, DisplayName, IsActive, CreatedAt, UserName, NormalizedUserName, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) " +
                                 "VALUES (NEWID(), 'Real person', 1, SYSDATETIMEOFFSET(), 'real', 'REAL', 0, 0, 1, 1, 0);", conn).ExecuteNonQueryAsync();
            var refused = await new RequestDemoDataStep().RunAsync(Ctx(), CancellationToken.None);
            Assert.Equal(StepOutcome.Failed, refused.Outcome);
            Assert.Contains("NEW database", refused.Message);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using var m = new SqlConnection(options.ConnectionString("master"));
            await m.OpenAsync();
            await new SqlCommand($"IF DB_ID('{dbName}') IS NOT NULL BEGIN ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}]; END", m).ExecuteNonQueryAsync();
        }
    }
}

internal sealed class FakeWindowsProbe : ISystemProbe
{
    public bool IsWindows => true;
    public Version OsVersion => new(10, 0, 20348);
    public string? WindowsInstallationType => "Server";
    public bool Is64Bit => true;
    public bool IsAdministrator => true;
    public long TotalMemoryBytes => 16 * PrerequisiteChecks.GiB;
    public long? FreeDiskBytes(string drive) => 100 * PrerequisiteChecks.GiB;
    public int? IisMajorVersion => 10;
    public bool AspNetCoreModuleV2Installed => true;
    public IReadOnlyList<Version> AspNetCoreRuntimeVersions => [];
    public bool? TlsProtocolServerEnabled(string protocol) => null;
    public string TimeZoneId => "New Zealand Standard Time";
    public bool CanConnectTcp(string host, int port, TimeSpan timeout) => true;
}
