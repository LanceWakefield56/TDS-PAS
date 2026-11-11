using Microsoft.Data.SqlClient;
using BrightPAS.Setup.Checks;

namespace BrightPAS.Setup.Install;

public sealed record InstallOptions
{
    public string? SqlServer { get; init; }
    public string Database { get; init; } = "BrightPAS";
    public bool IntegratedSecurity { get; init; } = true;
    public string? SqlUser { get; init; }
    public string? SqlPassword { get; init; }
    /// <summary>Only for the sandbox when SQL Server uses a self-signed certificate.</summary>
    public bool TrustServerCertificate { get; init; }
    public bool SkipIis { get; init; }
    /// <summary>Install only the platform and database (no web application).</summary>
    public bool SkipWeb { get; init; }
    public bool WhatIf { get; init; }
    /// <summary>Test labs only: continue even if prerequisite checks fail. Recorded in the log; never for production.</summary>
    public bool IgnorePrerequisites { get; init; }
    /// <summary>Demonstration / training system: fill the NEW database with invented demonstration data (0.12.3). Never for production.</summary>
    public bool DemoData { get; init; }
    public string Drive { get; init; } = OperatingSystem.IsWindows() ? @"C:\" : "/";
    public string Version { get; init; } = typeof(InstallOptions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public string ConnectionString(string? database = null)
    {
        var (host, port) = SplitHost(SqlServer);
        var b = new SqlConnectionStringBuilder
        {
            DataSource = port == 1433 ? host : $"{host},{port}",
            InitialCatalog = database ?? Database,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = TrustServerCertificate,
            ApplicationName = "BrightPAS Setup",
            ConnectTimeout = 15
        };
        if (IntegratedSecurity) b.IntegratedSecurity = true;
        else { b.UserID = SqlUser; b.Password = SqlPassword; }
        return b.ConnectionString;
    }

    /// <summary>True when SQL Server runs on this same machine (single-server installation).</summary>
    public bool IsLocalSqlServer()
    {
        var host = SplitHost(SqlServer).Host.Split('\\')[0].Trim().ToLowerInvariant();
        if (host is "localhost" or "." or "(local)" or "127.0.0.1" or "::1") return true;
        var machine = Environment.MachineName.ToLowerInvariant();
        return host == machine || host.StartsWith(machine + ".", StringComparison.Ordinal);
    }

    public static (string Host, int Port) SplitHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return ("localhost", 1433);
        var parts = value.Split(new[] { ':', ',' }, 2);
        return parts.Length == 2 && int.TryParse(parts[1], out var p) ? (parts[0], p) : (value, 1433);
    }
}

public enum StepOutcome { Done, AlreadyDone, Skipped, WhatIf, Failed }

public sealed record StepResult(StepOutcome Outcome, string Message)
{
    public static StepResult Done(string m) => new(StepOutcome.Done, m);
    public static StepResult Already(string m) => new(StepOutcome.AlreadyDone, m);
    public static StepResult Skip(string m) => new(StepOutcome.Skipped, m);
    public static StepResult Planned(string m) => new(StepOutcome.WhatIf, m);
    public static StepResult Fail(string m) => new(StepOutcome.Failed, m);
}

/// <summary>Everything a step needs; external effects go through replaceable interfaces so steps are testable.</summary>
public sealed class InstallContext(InstallOptions options, ISystemProbe probe, IProcessRunner processes, IInstallLog log, TimeProvider clock, WebDeployOptions? web = null, string? setupFolder = null)
{
    public InstallOptions Options { get; } = options;
    public ISystemProbe Probe { get; } = probe;
    public IProcessRunner Processes { get; } = processes;
    public IInstallLog Log { get; } = log;
    public TimeProvider Clock { get; } = clock;
    public WebDeployOptions Web { get; } = web ?? new WebDeployOptions();
    /// <summary>Folder containing Setup.exe (where the web package and Hosting Bundle installer are looked for).</summary>
    public string SetupFolder { get; } = setupFolder ?? AppContext.BaseDirectory;
    /// <summary>Connection string for the web application's own least-privilege login (never logged).</summary>
    public string? AppConnectionString { get; set; }
    public string? SiteUrl { get; set; }
    public bool SelfSignedCertificate { get; set; }
    /// <summary>Set by the bootstrap step so the final summary can show it.</summary>
    public string? BootstrapCode { get; set; }
    public bool RebootRequired { get; set; }
    public bool DemoDataRequested { get; set; }
    public bool DemoDataLoaded { get; set; }
}

public interface IInstallStep
{
    string Name { get; }
    /// <summary>Plain-English description shown before the step runs.</summary>
    string Description { get; }
    Task<StepResult> RunAsync(InstallContext context, CancellationToken ct);
}

public interface IInstallLog
{
    void Info(string message);
    void Detail(string message);
    void Debug(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Block(string title, string text);
    void AddSecret(string? secret);
}

public sealed record ProcessResult(int ExitCode, string Output);

public interface IProcessRunner
{
    Task<ProcessResult> RunPowerShellAsync(string script, CancellationToken ct);
    Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct);
}

public sealed class ProcessRunner(string? powerShellExe = null) : IProcessRunner
{
    /// <summary>Windows PowerShell 5.1 on Windows; PowerShell 7 (pwsh) elsewhere, so tests can run the real thing.</summary>
    private readonly string _ps = powerShellExe ?? (OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh");

    /// <summary>Printed as the very last line of every wrapped script. If it is missing, the script did not run to the end.</summary>
    public const string CompletionMarker = "BRIGHTPAS-SCRIPT-COMPLETED";

    /// <summary>
    /// Runs a script from a temporary .ps1 file with -File (0.5.2). Earlier versions piped the script to
    /// "-Command -", where PowerShell silently skips multi-line blocks such as try { } – scripts "succeeded"
    /// without doing anything. The completion marker now proves the script really ran.
    /// </summary>
    public async Task<ProcessResult> RunPowerShellAsync(string script, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "BrightPASSetup");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"step-{Guid.NewGuid():N}.ps1");
        // UTF-8 with BOM so Windows PowerShell 5.1 reads non-ASCII text (e.g. macrons) correctly.
        await File.WriteAllTextAsync(file, Wrap(script), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(_ps)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", file }) psi.ArgumentList.Add(a);
            using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Could not start PowerShell.");
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return Interpret(p.ExitCode, (await stdout) + (await stderr));
        }
        finally
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }

    /// <summary>Exit code 0 counts as success only when the completion marker was printed; the marker is removed from the output.</summary>
    public static ProcessResult Interpret(int exitCode, string output)
    {
        var completed = output.Contains(CompletionMarker, StringComparison.Ordinal);
        var text = output.Replace(CompletionMarker, "").Trim();
        if (exitCode == 0 && !completed)
            return new ProcessResult(-2, (text + "\nERROR: PowerShell exited without finishing the script (completion marker missing).").Trim());
        return new ProcessResult(exitCode, text);
    }

    /// <summary>
    /// Wraps a script so that any failure reports the exact error, the line it happened on and the script stack,
    /// and exits with code 1. Verbose PowerShell output is included. Success prints the completion marker.
    /// </summary>
    public static string Wrap(string script) =>
        "$VerbosePreference = 'Continue'\n$ProgressPreference = 'SilentlyContinue'\ntry {\n" + script +
        "\n} catch {\n  Write-Output ('ERROR: ' + $_.Exception.Message)\n  Write-Output ('WHERE: ' + $_.InvocationInfo.PositionMessage)\n" +
        "  Write-Output ('CATEGORY: ' + $_.CategoryInfo)\n  Write-Output ('STACK: ' + $_.ScriptStackTrace)\n  exit 1\n}\nWrite-Output '" + CompletionMarker + "'\nexit 0\n";

    public async Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(fileName, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return new ProcessResult(p.ExitCode, ((await stdout) + (await stderr)).Trim());
    }
}
