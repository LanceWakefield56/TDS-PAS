using Microsoft.Data.SqlClient;
using Galenica.Setup.Checks;
using Galenica.Setup.Install;

namespace Galenica.Setup.Tests;

public class InstallerTests
{
    private sealed class FakeRunner(int exitCode = 0, string output = "ok") : IProcessRunner
    {
        public List<string> Scripts { get; } = [];
        public Task<ProcessResult> RunPowerShellAsync(string script, CancellationToken ct) { Scripts.Add(script); return Task.FromResult(new ProcessResult(exitCode, output)); }
        public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct) { Scripts.Add(fileName + " " + arguments); return Task.FromResult(new ProcessResult(exitCode, output)); }
    }

    private sealed class ListLog : IInstallLog
    {
        public List<string> Lines { get; } = [];
        public void Info(string m) => Lines.Add(m);
        public void Detail(string m) => Lines.Add(m);
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
        Assert.Equal(["01-core.sql", "02-identity.sql", "03-patients.sql"], names);
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
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL", WhatIf = true }, new WindowsProbe(), runner, new ListLog(), TimeProvider.System);
        var records = await Installer.RunAsync(Installer.PlatformSteps(), ctx);
        Assert.Equal(12, records.Count);
        Assert.Empty(runner.Scripts);
        Assert.All(records.Skip(1), r => Assert.Equal(StepOutcome.WhatIf, r.Result.Outcome));
    }

    [Fact]
    public async Task Installation_stops_at_the_first_failure()
    {
        var runner = new FakeRunner(exitCode: 1, output: "Install-WindowsFeature : access denied");
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL" }, new WindowsProbe(), runner, new ListLog(), TimeProvider.System);
        var records = await Installer.RunAsync(Installer.PlatformSteps(), ctx);
        Assert.Equal(StepOutcome.Failed, records[^1].Result.Outcome);
        Assert.Equal("Install IIS (web server)", records[^1].Step.Name);
        Assert.Contains("access denied", records[^1].Result.Message);
        Assert.Single(runner.Scripts);
    }

    [Fact]
    public async Task Skip_iis_option_skips_iis_steps_but_still_secures_tls()
    {
        var runner = new FakeRunner();
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL", SkipIis = true }, new WindowsProbe(), runner, new ListLog(), TimeProvider.System);
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
        var ctx = new InstallContext(new InstallOptions { SqlServer = "PAS-SQL", Database = "Tds; DROP DATABASE x" }, new WindowsProbe(), new FakeRunner(), new ListLog(), TimeProvider.System);
        var r = await new CreateDatabaseStep().RunAsync(ctx, CancellationToken.None);
        Assert.Equal(StepOutcome.Failed, r.Outcome);
    }

    [Fact]
    public void Web_package_finder_picks_the_highest_version()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        foreach (var v in new[] { "0.3.0", "0.10.0", "0.9.1" }) File.WriteAllText(Path.Combine(dir, $"Galenica-Web-{v}.zip"), "x");
        Assert.EndsWith("Galenica-Web-0.10.0.zip", WebPackage.Find(null, dir));
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
        WebPackage.WriteProductionSettings(dir, "Server=x;Password=a\"b", @"C:\ProgramData\Galenica\keys");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "appsettings.Production.json")));
        Assert.Equal("Server=x;Password=a\"b", doc.RootElement.GetProperty("ConnectionStrings").GetProperty("Pas").GetString());
        Assert.Equal(@"C:\ProgramData\Galenica\keys", doc.RootElement.GetProperty("DataProtection").GetProperty("KeysPath").GetString());
    }

    [Fact]
    public void Site_script_escapes_values_and_uses_https_only()
    {
        var ps = PowerShellScripts.ConfigureSite("Galenica", "Galenica", @"C:\Program Files\Galenica\Web", "o'brien.local", 443, null);
        Assert.Contains("'o''brien.local'", ps);
        Assert.Contains("-Ssl", ps);
        Assert.Contains("Where-Object { $_.protocol -ne 'https' } | Remove-WebBinding", ps);
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
    private sealed class NoLog : IInstallLog { public void Info(string m) { } public void Detail(string m) { } }

    [SqlServerFact]
    public async Task Database_steps_create_schema_and_bootstrap_code_and_can_run_twice()
    {
        var src = new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs);
        var dbName = "GalenicaSetup_" + Guid.NewGuid().ToString("N")[..10];
        var options = new InstallOptions
        {
            SqlServer = src.DataSource.Replace(',', ':'), Database = dbName, IntegratedSecurity = false,
            SqlUser = src.UserID, SqlPassword = src.Password, TrustServerCertificate = true, Version = "0.2.0"
        };
        var steps = new IInstallStep[] { new CreateDatabaseStep(), new ApplySchemaStep(), new BootstrapCodeStep() };
        try
        {
            var first = new InstallContext(options, new NonWindowsSystemProbe(), new ProcessRunner(), new NoLog(), TimeProvider.System);
            var r1 = await Installer.RunAsync(steps, first);
            Assert.All(r1, r => Assert.Equal(StepOutcome.Done, r.Result.Outcome));
            Assert.NotNull(first.BootstrapCode);

            var second = new InstallContext(options, new NonWindowsSystemProbe(), new ProcessRunner(), new NoLog(), TimeProvider.System);
            var r2 = await Installer.RunAsync(steps, second);
            Assert.Equal(StepOutcome.AlreadyDone, r2[0].Result.Outcome);
            Assert.Equal(StepOutcome.Done, r2[1].Result.Outcome); // idempotent re-apply
            Assert.Equal(StepOutcome.Done, r2[2].Result.Outcome); // new code while no admin exists

            await using var conn = new SqlConnection(options.ConnectionString());
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("SELECT BootstrapCodeHash, InstalledVersion FROM core.SetupState WHERE Id = 1; SELECT ledger_type_desc FROM sys.tables WHERE name = 'AuditEvents'; SELECT COUNT(*) FROM patients.MrnCounter;", conn);
            await using var rd = await cmd.ExecuteReaderAsync();
            Assert.True(await rd.ReadAsync());
            Assert.True(Galenica.Shared.BootstrapCode.Verify(second.BootstrapCode!, rd.GetString(0)));
            Assert.False(Galenica.Shared.BootstrapCode.Verify(first.BootstrapCode!, rd.GetString(0))); // old code replaced
            Assert.Equal("0.2.0", rd.GetString(1));
            await rd.NextResultAsync(); await rd.ReadAsync();
            Assert.Equal("APPEND_ONLY_LEDGER_TABLE", rd.GetString(0));
            await rd.NextResultAsync(); await rd.ReadAsync();
            Assert.Equal(1, rd.GetInt32(0));
            await rd.CloseAsync();

            // Application login: can read and write data, cannot change the schema.
            var appCtx = new InstallContext(options, new NonWindowsSystemProbe(), new ProcessRunner(), new NoLog(), TimeProvider.System,
                new WebDeployOptions { AppSqlLogin = "galenica_app_" + dbName[^6..] });
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
