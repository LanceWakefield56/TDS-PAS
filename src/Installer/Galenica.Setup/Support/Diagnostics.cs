using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Galenica.Setup.Install;

namespace Galenica.Setup.Support;

/// <summary>
/// Collects everything needed to diagnose an installation problem into one zip file:
/// Setup logs and security reports, Hosting Bundle installer logs, Windows event log entries for IIS / ASP.NET Core /
/// SQL Server, IIS site and app pool configuration, recent IIS request logs and the deployed configuration files.
/// Read-only. Passwords in connection strings are masked.
/// </summary>
public static partial class Diagnostics
{
    [GeneratedRegex(@"(?i)(password|pwd)\s*=\s*[^;""]*")]
    private static partial Regex PasswordPattern();

    public static string Mask(string text) => PasswordPattern().Replace(text, "$1=********");

    public static async Task<string?> CollectAsync(IProcessRunner runner, IInstallLog log, WebDeployOptions web, CancellationToken ct = default)
    {
        var dataDir = OperatingSystem.IsWindows() ? web.DataDir : Path.Combine(Path.GetTempPath(), "Galenica");
        var outDir = Path.Combine(dataDir, "Support");
        Directory.CreateDirectory(outDir);
        var zipPath = Path.Combine(outDir, $"Galenica-Diagnostics-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        log.Info("Collecting diagnostics (read-only; passwords are masked)…");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            void AddText(string name, string text) { using var w = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), Encoding.UTF8); w.Write(Mask(text)); }
            void AddFiles(string folder, string pattern, string prefix, int newest)
            {
                if (!Directory.Exists(folder)) { log.Detail($"  (not found: {folder})"); return; }
                foreach (var f in new DirectoryInfo(folder).GetFiles(pattern).OrderByDescending(f => f.LastWriteTime).Take(newest))
                {
                    try { AddText($"{prefix}/{f.Name}", File.ReadAllText(f.FullName)); log.Detail($"  + {f.FullName}"); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Detail($"  (could not read {f.FullName}: {ex.Message})"); }
                }
            }

            AddFiles(Path.Combine(dataDir, "Logs"), "Galenica-*.log", "setup-logs", 20);
            AddFiles(Path.Combine(dataDir, "Reports"), "Galenica-Security-Report-*.txt", "security-reports", 5);
            AddFiles(Path.GetTempPath(), "Galenica-HostingBundle-*.log", "hosting-bundle", 3);
            AddFiles(web.InstallDir, "web.config", "web-app", 1);
            AddFiles(web.InstallDir, "appsettings*.json", "web-app", 5);

            if (OperatingSystem.IsWindows())
            {
                foreach (var (name, script) in Scripts(web))
                {
                    log.Detail($"  + {name}");
                    var r = await runner.RunPowerShellAsync(script, ct);
                    AddText(name, $"(exit code {r.ExitCode})\r\n{r.Output}");
                }
                var iisLogs = Path.Combine(Environment.GetEnvironmentVariable("SystemDrive") ?? "C:", @"\inetpub\logs\LogFiles");
                if (Directory.Exists(iisLogs))
                    foreach (var d in Directory.GetDirectories(iisLogs)) AddFiles(d, "*.log", "iis-logs/" + Path.GetFileName(d), 2);
            }
            AddText("README.txt", $"Galenica diagnostics from {Environment.MachineName} at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}.\r\nContains logs and configuration only – no patient data. Passwords are masked.");
        }

        log.Info($"Diagnostics saved: {zipPath}");
        try
        {
            var copy = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(zipPath));
            File.Copy(zipPath, copy, overwrite: true);
            log.Info($"(A copy is also next to Setup.exe: {copy})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return zipPath;
    }

    public static IEnumerable<(string Name, string Script)> Scripts(WebDeployOptions web)
    {
        const string events = """
            $since = (Get-Date).AddDays(-3)
            $providers = 'IIS AspNetCore Module V2','IIS Express AspNetCore Module V2','ASP.NET Core','.NET Runtime','Application Error','W3SVC','WAS','MSSQLSERVER','MSSQL$*','Schannel','MsiInstaller'
            foreach ($log in 'Application','System') {
              "===== $log log (last 3 days) ====="
              Get-WinEvent -FilterHashtable @{ LogName = $log; StartTime = $since } -ErrorAction SilentlyContinue |
                Where-Object { $p = $_.ProviderName; ($providers | Where-Object { $p -like $_ }) -or $_.LevelDisplayName -in 'Error','Critical' } |
                Select-Object -First 300 |
                ForEach-Object { "[{0:yyyy-MM-dd HH:mm:ss}] {1} {2} (id {3})`r`n{4}`r`n" -f $_.TimeCreated, $_.LevelDisplayName, $_.ProviderName, $_.Id, $_.Message }
            }
            """;
        var site = web.SiteName.Replace("'", "''");
        var pool = web.AppPoolName.Replace("'", "''");
        var installDir = web.InstallDir.Replace("'", "''");
        var dataDir = web.DataDir.Replace("'", "''");
        var iis = $$$"""
            $ErrorActionPreference = 'Continue'
            "===== Windows features ====="
            Get-WindowsFeature Web-* -ErrorAction SilentlyContinue | Where-Object Installed | Select-Object Name | Format-Table -HideTableHeaders | Out-String
            "===== Installed .NET runtimes ====="
            if (Get-Command dotnet -ErrorAction SilentlyContinue) { dotnet --list-runtimes } else { 'dotnet command not found' }
            "===== ASP.NET Core Module ====="
            Test-Path "$env:windir\System32\inetsrv\aspnetcorev2.dll"
            Import-Module WebAdministration -ErrorAction SilentlyContinue
            "===== Sites ====="
            Get-Website | Format-List Name, State, PhysicalPath, ApplicationPool, Bindings | Out-String -Width 300
            "===== App pool '{{{pool}}}' ====="
            Get-Item 'IIS:\AppPools\{{{pool}}}' -ErrorAction SilentlyContinue | Format-List Name, State, ManagedRuntimeVersion, @{n='Identity';e={$_.processModel.identityType}} | Out-String
            "===== HTTPS bindings ====="
            netsh http show sslcert
            "===== Certificates (LocalMachine\My) ====="
            Get-ChildItem Cert:\LocalMachine\My | Format-List Subject, FriendlyName, Thumbprint, NotAfter, DnsNameList | Out-String -Width 300
            "===== Folder permissions ====="
            foreach ($f in '{{{installDir}}}','{{{dataDir}}}') { if (Test-Path $f) { "--- $f"; (Get-Acl $f).Access | Format-Table IdentityReference, FileSystemRights, AccessControlType -AutoSize | Out-String -Width 300 } }
            "===== Listening ports 443 / 1433 ====="
            Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object LocalPort -in 443,1433 | Format-Table LocalAddress, LocalPort, OwningProcess -AutoSize | Out-String
            "===== Firewall rules for HTTPS ====="
            Get-NetFirewallRule -DisplayName '*HTTPS*' -ErrorAction SilentlyContinue | Format-Table DisplayName, Enabled, Direction, Action, Profile -AutoSize | Out-String -Width 300
            "===== Network ====="
            Get-NetIPAddress -AddressFamily IPv4 | Format-Table InterfaceAlias, IPAddress -AutoSize | Out-String
            "===== SQL Server services ====="
            Get-Service MSSQL*, SQLBrowser -ErrorAction SilentlyContinue | Format-Table Name, Status, StartType -AutoSize | Out-String
            "===== Computer ====="
            Get-ComputerInfo -Property OsName, OsVersion, CsName, CsDomain, CsTotalPhysicalMemory, TimeZone -ErrorAction SilentlyContinue | Format-List | Out-String
            """;
        yield return ("windows/event-log.txt", events);
        yield return ("windows/iis-and-server.txt", iis);
    }
}
