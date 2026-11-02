using Microsoft.Data.SqlClient;
using Tds.Pas.Setup.Checks;

namespace Tds.Pas.Setup.Install;

public sealed record InstallOptions
{
    public string? SqlServer { get; init; }
    public string Database { get; init; } = "TdsPas";
    public bool IntegratedSecurity { get; init; } = true;
    public string? SqlUser { get; init; }
    public string? SqlPassword { get; init; }
    /// <summary>Only for the sandbox when SQL Server uses a self-signed certificate.</summary>
    public bool TrustServerCertificate { get; init; }
    public bool SkipIis { get; init; }
    public bool WhatIf { get; init; }
    /// <summary>Test labs only: continue even if prerequisite checks fail. Recorded in the log; never for production.</summary>
    public bool IgnorePrerequisites { get; init; }
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
            ApplicationName = "TDS PAS Setup",
            ConnectTimeout = 15
        };
        if (IntegratedSecurity) b.IntegratedSecurity = true;
        else { b.UserID = SqlUser; b.Password = SqlPassword; }
        return b.ConnectionString;
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
public sealed class InstallContext(InstallOptions options, ISystemProbe probe, IProcessRunner processes, IInstallLog log, TimeProvider clock)
{
    public InstallOptions Options { get; } = options;
    public ISystemProbe Probe { get; } = probe;
    public IProcessRunner Processes { get; } = processes;
    public IInstallLog Log { get; } = log;
    public TimeProvider Clock { get; } = clock;
    /// <summary>Set by the bootstrap step so the final summary can show it.</summary>
    public string? BootstrapCode { get; set; }
    public bool RebootRequired { get; set; }
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
}

public sealed class ConsoleInstallLog : IInstallLog
{
    public List<string> Lines { get; } = [];
    public void Info(string message) { Lines.Add(message); Console.WriteLine(message); }
    public void Detail(string message) { Lines.Add("    " + message); Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine("    " + message); Console.ResetColor(); }
}

public sealed record ProcessResult(int ExitCode, string Output);

public interface IProcessRunner
{
    Task<ProcessResult> RunPowerShellAsync(string script, CancellationToken ct);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunPowerShellAsync(string script, CancellationToken ct)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Could not start PowerShell.");
        await p.StandardInput.WriteAsync(script);
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return new ProcessResult(p.ExitCode, ((await stdout) + (await stderr)).Trim());
    }
}
