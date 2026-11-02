using Microsoft.Data.SqlClient;
using Tds.Pas.Setup.Checks;

namespace Tds.Pas.Setup.Install;

public sealed class CheckPrerequisitesStep : IInstallStep
{
    public string Name => "Check this server";
    public string Description => "Re-runs the prerequisite checks. IIS problems are allowed because the next step installs IIS.";

    // Failures that later steps fix, or that only matter once the web application is deployed.
    private static readonly HashSet<string> FixedLater = ["IIS (Internet Information Services)", "ASP.NET Core Module for IIS", ".NET 10 ASP.NET Core runtime"];

    public Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        var report = CheckRunner.Run(PrerequisiteChecks.All(), new SetupContext(c.Options.Drive, c.Options.SqlServer is null ? null : InstallOptions.SplitHost(c.Options.SqlServer).Host, InstallOptions.SplitHost(c.Options.SqlServer).Port), c.Probe, c.Clock);
        var blocking = report.Outcomes.Where(o => o.Result.Status == CheckStatus.Fail && !FixedLater.Contains(o.Check.Name)).ToList();
        foreach (var o in report.Outcomes.Where(o => o.Result.Status is CheckStatus.Fail or CheckStatus.Warning))
            c.Log.Detail($"[{CheckReport.Label(o.Result.Status)}] {o.Check.Name}: {o.Result.Detail}");
        if (blocking.Count > 0 && c.Options.IgnorePrerequisites)
            return Task.FromResult(StepResult.Done($"WARNING: {blocking.Count} prerequisite problem(s) ignored (--ignore-prerequisites, test lab only – not supported for production)."));
        return Task.FromResult(blocking.Count == 0
            ? StepResult.Done("Server is ready to continue.")
            : StepResult.Fail($"{blocking.Count} problem(s) must be fixed first: {string.Join("; ", blocking.Select(b => b.Check.Name))}. Run 'Setup.exe check' for details."));
    }
}

public abstract class PowerShellStep(string script) : IInstallStep
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    protected virtual bool ShouldSkip(InstallContext c, out string reason) { reason = string.Empty; return false; }
    protected virtual void AfterSuccess(InstallContext c, string output) { }

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (!c.Probe.IsWindows) return StepResult.Skip("Not running on Windows.");
        if (ShouldSkip(c, out var reason)) return StepResult.Skip(reason);
        if (c.Options.WhatIf)
        {
            foreach (var line in script.Split('\n').Take(6)) c.Log.Detail(line.TrimEnd());
            return StepResult.Planned("Would run the PowerShell script above (first lines shown).");
        }
        var r = await c.Processes.RunPowerShellAsync(script, ct);
        if (r.ExitCode != 0) return StepResult.Fail($"PowerShell reported an error: {r.Output}");
        AfterSuccess(c, r.Output);
        return StepResult.Done(r.Output.Split('\n').LastOrDefault()?.Trim() ?? "Done.");
    }
}

public sealed class InstallIisStep() : PowerShellStep(PowerShellScripts.InstallIis)
{
    public override string Name => "Install IIS (web server)";
    public override string Description => "Installs only the IIS features the PAS needs and removes risky ones (WebDAV, FTP, CGI, directory browsing).";
    protected override bool ShouldSkip(InstallContext c, out string reason) { reason = "Skipped by option --skip-iis."; return c.Options.SkipIis; }
    protected override void AfterSuccess(InstallContext c, string output) { if (output.Contains("RestartNeeded=Yes", StringComparison.OrdinalIgnoreCase)) c.RebootRequired = true; }
}

public sealed class HardenTlsStep() : PowerShellStep(PowerShellScripts.HardenTls)
{
    public override string Name => "Secure network encryption (TLS)";
    public override string Description => "Allows only TLS 1.2 and 1.3; disables SSL 2/3 and TLS 1.0/1.1. A restart is needed afterwards.";
    protected override void AfterSuccess(InstallContext c, string output) => c.RebootRequired = true;
}

public sealed class HardenIisStep() : PowerShellStep(PowerShellScripts.HardenIis)
{
    public override string Name => "Harden IIS";
    public override string Description => "Removes the default site, hides server details, blocks unsafe requests and turns off detailed errors.";
    protected override bool ShouldSkip(InstallContext c, out string reason) { reason = "Skipped by option --skip-iis."; return c.Options.SkipIis; }
}

public sealed class CreateDatabaseStep : IInstallStep
{
    public string Name => "Create the PAS database";
    public string Description => "Creates the database if it does not exist (full recovery model for the standby copy, snapshot isolation for responsiveness).";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Options.SqlServer)) return StepResult.Fail("No SQL Server specified (use --sql-server).");
        var db = c.Options.Database;
        if (!System.Text.RegularExpressions.Regex.IsMatch(db, "^[A-Za-z][A-Za-z0-9_]{0,63}$"))
            return StepResult.Fail("Database name may only contain letters, digits and underscores.");
        if (c.Options.WhatIf) return StepResult.Planned($"Would create database [{db}] on {c.Options.SqlServer} if missing.");

        await using var conn = new SqlConnection(c.Options.ConnectionString("master"));
        await conn.OpenAsync(ct);
        await using (var check = new SqlCommand("SELECT DB_ID(@n)", conn))
        {
            check.Parameters.AddWithValue("@n", db);
            if (await check.ExecuteScalarAsync(ct) is not DBNull and not null)
                return StepResult.Already($"Database [{db}] already exists.");
        }
        await using (var create = new SqlCommand(
            $"CREATE DATABASE [{db}]; ALTER DATABASE [{db}] SET RECOVERY FULL; ALTER DATABASE [{db}] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [{db}] SET ALLOW_SNAPSHOT_ISOLATION ON;", conn))
        {
            create.CommandTimeout = 120;
            await create.ExecuteNonQueryAsync(ct);
        }
        return StepResult.Done($"Database [{db}] created.");
    }
}

public sealed class ApplySchemaStep : IInstallStep
{
    public string Name => "Create or upgrade the database tables";
    public string Description => "Applies the PAS schema scripts. Safe to run again: only missing changes are applied.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        var scripts = SqlScripts.Embedded();
        if (scripts.Count == 0) return StepResult.Fail("No schema scripts are embedded in this Setup build.");
        if (c.Options.WhatIf) return StepResult.Planned($"Would apply {scripts.Count} schema script(s): {string.Join(", ", scripts.Select(s => s.Name))}.");

        await using var conn = new SqlConnection(c.Options.ConnectionString());
        await conn.OpenAsync(ct);
        foreach (var (name, sql) in scripts)
        {
            var batches = SqlScripts.SplitBatches(sql);
            foreach (var batch in batches)
            {
                await using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 300 };
                try { await cmd.ExecuteNonQueryAsync(ct); }
                catch (SqlException ex) { return StepResult.Fail($"{name}: {ex.Message}"); }
            }
            c.Log.Detail($"{name}: {batches.Count} batch(es) applied.");
        }
        return StepResult.Done("Database tables are up to date.");
    }
}

public sealed class BootstrapCodeStep : IInstallStep
{
    public string Name => "Prepare first administrator set-up";
    public string Description => "Creates a one-time code (valid 7 days) for setting up the first administrator account with MFA.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (c.Options.WhatIf) return StepResult.Planned("Would create a one-time first-administrator code (if no administrator has been set up yet).");
        await using var conn = new SqlConnection(c.Options.ConnectionString());
        await conn.OpenAsync(ct);

        await using (var used = new SqlCommand("SELECT BootstrapCodeUsedAt FROM core.SetupState WHERE Id = 1", conn))
        {
            if (await used.ExecuteScalarAsync(ct) is DateTimeOffset)
            {
                await RecordVersionAsync(conn, c, ct);
                return StepResult.Already("The first administrator has already been set up – no code needed.");
            }
        }

        var code = Tds.Pas.Shared.BootstrapCode.Generate();
        const string upsert = """
            MERGE core.SetupState AS t
            USING (SELECT 1 AS Id) AS s ON t.Id = s.Id
            WHEN MATCHED THEN UPDATE SET BootstrapCodeHash = @hash, BootstrapCodeCreatedAt = @now, BootstrapCodeUsedAt = NULL
            WHEN NOT MATCHED THEN INSERT (Id, BootstrapCodeHash, BootstrapCodeCreatedAt) VALUES (1, @hash, @now);
            """;
        await using (var cmd = new SqlCommand(upsert, conn))
        {
            cmd.Parameters.AddWithValue("@hash", Tds.Pas.Shared.BootstrapCode.Hash(code));
            cmd.Parameters.AddWithValue("@now", c.Clock.GetUtcNow());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await RecordVersionAsync(conn, c, ct);
        c.BootstrapCode = code;
        return StepResult.Done("One-time first-administrator code created (shown at the end).");
    }

    private static async Task RecordVersionAsync(SqlConnection conn, InstallContext c, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("UPDATE core.SetupState SET InstalledVersion = @v, InstalledAt = @now WHERE Id = 1", conn);
        cmd.Parameters.AddWithValue("@v", c.Options.Version);
        cmd.Parameters.AddWithValue("@now", c.Clock.GetUtcNow());
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
