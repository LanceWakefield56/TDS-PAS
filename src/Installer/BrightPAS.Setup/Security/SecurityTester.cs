using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Security;

/// <summary>Runs every security self-test and returns the combined report.</summary>
public static class SecurityTester
{
    public static async Task<SecurityReport> RunAsync(SecurityTestOptions o, IProcessRunner processes, IInstallLog log, CancellationToken ct = default)
    {
        var report = new SecurityReport();
        log.Info("Running server checks…");
        try { await new ServerProbes(o, report, processes, log).RunAsync(ct); }
        catch (Exception ex) { log.Error($"Server checks failed: {ex.Message}", ex); report.Fail("Server (Windows)", "Server checks", ex.Message, "See the log file."); }
        log.Info("Running website penetration probes…");
        try { await new WebProbes(o, report, log).RunAsync(ct); }
        catch (Exception ex) { log.Error($"Website probes failed: {ex.Message}", ex); report.Fail("Website (penetration probes)", "Website probes", ex.Message, "See the log file."); }
        log.Info("Running database and audit checks…");
        try { await new DatabaseProbes(o, report, log).RunAsync(ct); }
        catch (Exception ex) { log.Error($"Database checks failed: {ex.Message}", ex); report.Fail("Database (SQL Server)", "Database checks", ex.Message, "See the log file."); }
        foreach (var f in report.Findings) log.Debug($"[{SecurityReport.Label(f.Status)}] {f.Area} | {f.Test} | {f.Detail}{(f.Fix is null ? "" : " | Fix: " + f.Fix)}");
        return report;
    }
}
