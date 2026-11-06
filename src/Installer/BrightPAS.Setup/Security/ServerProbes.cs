using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Security;

/// <summary>Windows, IIS and file-permission checks gathered with one read-only PowerShell script.</summary>
public sealed class ServerProbes(SecurityTestOptions o, SecurityReport r, IProcessRunner processes, IInstallLog log)
{
    private const string Os = "Server (Windows)";
    private const string Iis = "Web server (IIS)";
    private const string Files = "Files and permissions";

    public static string Script(string site, string pool, string installDir, string dataDir) => """
        $out = [ordered]@{}
        function Put($k, $v) { $out[$k] = "$v" }
        $base = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'
        foreach ($p in 'SSL 2.0','SSL 3.0','TLS 1.0','TLS 1.1','TLS 1.2','TLS 1.3') {
            $v = (Get-ItemProperty -Path "$base\$p\Server" -Name Enabled -ErrorAction SilentlyContinue).Enabled
            Put "tls.$p" $(if ($null -eq $v) { 'default' } else { $v })
        }
        try { Get-NetFirewallProfile | ForEach-Object { Put "fw.$($_.Name)" $_.Enabled } } catch { Put 'fw.error' $_.Exception.Message }
        try { Put 'smb1' (Get-SmbServerConfiguration).EnableSMB1Protocol } catch { Put 'smb1' 'unknown' }
        try { $mp = Get-MpComputerStatus; Put 'av.realtime' $mp.RealTimeProtectionEnabled; Put 'av.sigage' $mp.AntivirusSignatureAge } catch { Put 'av.realtime' 'unknown' }
        try { $hf = Get-HotFix | Where-Object InstalledOn | Sort-Object InstalledOn -Descending | Select-Object -First 1; Put 'updates.last' $hf.InstalledOn.ToString('yyyy-MM-dd') } catch { Put 'updates.last' 'unknown' }
        try { Put 'guest.enabled' (Get-LocalUser -Name Guest -ErrorAction Stop).Enabled } catch { Put 'guest.enabled' 'unknown' }
        try { Put 'rdp.nla' (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp' -Name UserAuthentication).UserAuthentication } catch { Put 'rdp.nla' 'unknown' }
        try {
            Import-Module WebAdministration -ErrorAction Stop
            Put 'iis.installed' 'True'
            Put 'iis.defaultsite' ([bool](Get-Website -Name 'Default Web Site' -ErrorAction SilentlyContinue))
            Put 'iis.dirbrowse' (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/directoryBrowse' -Name enabled).Value
            Put 'iis.removeserverheader' (Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter 'system.webServer/security/requestFiltering' -Name removeServerHeader).Value
            foreach ($f in 'Web-DAV-Publishing','Web-Dir-Browsing','Web-Ftp-Server','Web-CGI','Web-ASP') { try { Put "feature.$f" (Get-WindowsFeature -Name $f).Installed } catch { Put "feature.$f" 'unknown' } }
            $site = Get-Website -Name __SITE__ -ErrorAction SilentlyContinue
            Put 'site.exists' ([bool]$site)
            if ($site) {
                Put 'site.state' $site.State
                Put 'site.bindings' (($site.Bindings.Collection | ForEach-Object { $_.protocol + ' ' + $_.bindingInformation }) -join '; ')
                Put 'pool.identity' (Get-ItemProperty "IIS:\AppPools\$(__POOL__)" -Name processModel.identityType)
                Put 'pool.runtime' (Get-ItemProperty "IIS:\AppPools\$(__POOL__)" -Name managedRuntimeVersion).Value
            }
        } catch { Put 'iis.installed' 'False' }
        function Writers($path) {
            if (-not (Test-Path $path)) { return 'missing' }
            $acl = Get-Acl $path
            ($acl.Access | Where-Object { $_.AccessControlType -eq 'Allow' -and ($_.FileSystemRights.ToString() -match 'Write|Modify|FullControl') -and
                ($_.IdentityReference -match 'Users|Everyone|Authenticated Users') } | ForEach-Object { $_.IdentityReference.Value + ':' + $_.FileSystemRights }) -join '; '
        }
        function Readers($path) {
            if (-not (Test-Path $path)) { return 'missing' }
            ((Get-Acl $path).Access | Where-Object { $_.AccessControlType -eq 'Allow' -and ($_.IdentityReference -match '\\Users$|Everyone|Authenticated Users') } |
                ForEach-Object { $_.IdentityReference.Value }) -join '; '
        }
        Put 'acl.install.writers' (Writers __INSTALL__)
        Put 'acl.settings.readers' (Readers (Join-Path __INSTALL__ 'appsettings.Production.json'))
        Put 'acl.data.readers' (Readers __DATA__)
        foreach ($k in $out.Keys) { Write-Output ("$k=" + $out[$k]) }
        """.Replace("__SITE__", Q(site)).Replace("$(__POOL__)", pool.Replace("'", "''").Replace("\"", "")).Replace("__INSTALL__", Q(installDir)).Replace("__DATA__", Q(dataDir));

    private static string Q(string v) => "'" + v.Replace("'", "''") + "'";

    public async Task RunAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
        {
            r.Add(Os, "Windows and IIS checks", FindingStatus.Skip, "Not running on Windows.");
            return;
        }
        var script = Script(o.Web.SiteName, o.Web.AppPoolName, o.Web.InstallDir, o.Web.DataDir);
        log.Block("PowerShell (security facts)", script);
        var res = await processes.RunPowerShellAsync(script, ct);
        log.Block("PowerShell output", res.Output);
        var f = res.Output.Split('\n').Select(l => l.Trim()).Where(l => l.Contains('='))
            .Select(l => l.Split('=', 2)).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1], StringComparer.OrdinalIgnoreCase);
        string V(string k) => f.TryGetValue(k, out var v) ? v : "unknown";

        // TLS protocols
        var weak = new[] { "SSL 2.0", "SSL 3.0", "TLS 1.0", "TLS 1.1" }.Where(p => V($"tls.{p}") != "0").ToList();
        if (weak.Count == 0) r.Pass(Os, "Old encryption protocols disabled", "SSL 2/3 and TLS 1.0/1.1 are disabled in Windows.");
        else r.Fail(Os, "Old encryption protocols disabled", $"Not disabled: {string.Join(", ", weak)}.", "Run Setup's install (TLS step) and restart the server.");
        if (V("tls.TLS 1.2") is "1" or "default") r.Pass(Os, "TLS 1.2 enabled", "TLS 1.2 is available.");
        else r.Fail(Os, "TLS 1.2 enabled", "TLS 1.2 is disabled.", "Run Setup's install (TLS step).");

        var fwOff = f.Where(kv => kv.Key.StartsWith("fw.", StringComparison.OrdinalIgnoreCase) && kv.Key != "fw.error" && !kv.Value.Equals("True", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key[3..]).ToList();
        if (f.Keys.Any(k => k.StartsWith("fw.") && k != "fw.error") && fwOff.Count == 0) r.Pass(Os, "Windows Firewall on", "Firewall enabled for Domain, Private and Public networks.");
        else r.Fail(Os, "Windows Firewall on", $"Firewall off for: {(fwOff.Count > 0 ? string.Join(", ", fwOff) : "unknown")}.", "Turn the firewall on: Windows Security → Firewall & network protection.");

        if (V("smb1").Equals("False", StringComparison.OrdinalIgnoreCase)) r.Pass(Os, "SMBv1 disabled", "The obsolete SMBv1 file-sharing protocol (used by WannaCry) is off.");
        else r.Warn(Os, "SMBv1 disabled", $"SMBv1 state: {V("smb1")}.", "Run in PowerShell as admin: Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force");

        if (V("av.realtime").Equals("True", StringComparison.OrdinalIgnoreCase)) r.Pass(Os, "Antivirus real-time protection", $"Microsoft Defender real-time protection is on (signatures {V("av.sigage")} day(s) old).");
        else r.Warn(Os, "Antivirus real-time protection", $"Defender real-time protection: {V("av.realtime")}.", "Make sure Defender or TDS's endpoint protection (EDR) is running.");

        if (DateTime.TryParse(V("updates.last"), out var last))
        {
            if (DateTime.Now - last > TimeSpan.FromDays(45)) r.Warn(Os, "Windows updates", $"Last update installed {last:dd MMM yyyy}.", "Run Windows Update (Settings → Windows Update) and restart.");
            else r.Pass(Os, "Windows updates", $"Last update installed {last:dd MMM yyyy}.");
        }
        else r.Info(Os, "Windows updates", "Could not read the update history.");

        if (V("guest.enabled").Equals("True", StringComparison.OrdinalIgnoreCase)) r.Fail(Os, "Guest account disabled", "The Windows Guest account is enabled.", "Disable it: Computer Management → Local Users and Groups → Users → Guest → Account is disabled.");
        else r.Pass(Os, "Guest account disabled", "The Windows Guest account is disabled.");
        if (V("rdp.nla") == "1" || V("rdp.nla") == "unknown") r.Pass(Os, "Remote Desktop requires network-level authentication", "Remote Desktop (if used) requires authentication before connecting.");
        else r.Warn(Os, "Remote Desktop requires network-level authentication", "NLA is off for Remote Desktop.", "System Properties → Remote → tick 'Allow connections only from computers running Remote Desktop with Network Level Authentication'.");

        // IIS
        if (V("iis.installed") != "True") { r.Fail(Iis, "IIS installed", "IIS is not installed or cannot be read.", "Run Setup's install."); }
        else
        {
            if (V("iis.defaultsite") == "True") r.Warn(Iis, "Default Web Site removed", "The IIS 'Default Web Site' still exists.", "Run Setup's install (IIS hardening step).");
            else r.Pass(Iis, "Default Web Site removed", "The sample IIS site has been removed.");
            if (V("iis.dirbrowse") == "False") r.Pass(Iis, "Directory browsing off", "Folder listings are disabled.");
            else r.Fail(Iis, "Directory browsing off", "Directory browsing is enabled.", "Run Setup's install (IIS hardening step).");
            if (V("iis.removeserverheader") == "True") r.Pass(Iis, "Server header removed", "IIS does not announce its version.");
            else r.Warn(Iis, "Server header removed", "IIS still sends a Server header.", "Run Setup's install (IIS hardening step).");
            var risky = new[] { "Web-DAV-Publishing", "Web-Dir-Browsing", "Web-Ftp-Server", "Web-CGI", "Web-ASP" }.Where(x => V($"feature.{x}") == "True").ToList();
            if (risky.Count == 0) r.Pass(Iis, "Risky IIS features removed", "WebDAV, FTP, CGI, classic ASP and directory browsing are not installed.");
            else r.Fail(Iis, "Risky IIS features removed", $"Installed: {string.Join(", ", risky)}.", "Run Setup's install (IIS step removes them).");
            if (V("site.exists") != "True") r.Warn(Iis, "BrightPAS site", $"IIS site '{o.Web.SiteName}' not found.", "Run Setup's install with the web package.");
            else
            {
                var bindings = V("site.bindings");
                if (bindings.Split(';').All(b => b.Trim().StartsWith("https", StringComparison.OrdinalIgnoreCase))) r.Pass(Iis, "HTTPS-only site", $"Bindings: {bindings}.");
                else r.Fail(Iis, "HTTPS-only site", $"Bindings: {bindings}.", "Remove HTTP bindings in IIS Manager → Sites → BrightPAS → Bindings.");
                if (V("pool.identity").Contains("ApplicationPoolIdentity", StringComparison.OrdinalIgnoreCase)) r.Pass(Iis, "Website runs with its own limited identity", "Application pool uses ApplicationPoolIdentity (not an administrator).");
                else r.Warn(Iis, "Website runs with its own limited identity", $"App pool identity: {V("pool.identity")}.", "Set the BrightPAS application pool identity to ApplicationPoolIdentity.");
            }
        }

        // Files
        var writers = V("acl.install.writers");
        if (writers == "missing") r.Info(Files, "Program folder permissions", $"{o.Web.InstallDir} not found (website not installed yet).");
        else if (string.IsNullOrEmpty(writers)) r.Pass(Files, "Program folder permissions", "Ordinary users cannot change the program files.");
        else r.Fail(Files, "Program folder permissions", $"Ordinary users can write: {writers}.", "Re-run Setup's install (it locks down the folder).");
        var readers = V("acl.settings.readers");
        if (readers == "missing") r.Info(Files, "Settings file permissions", "Settings file not found (website not installed yet).");
        else if (string.IsNullOrEmpty(readers)) r.Pass(Files, "Settings file permissions", "Only administrators and the website can read the settings file (database connection).");
        else r.Fail(Files, "Settings file permissions", $"Readable by: {readers}.", "Re-run Setup's install (it locks down the folder).");
        var dataReaders = V("acl.data.readers");
        if (dataReaders == "missing") r.Info(Files, "Key and log folder permissions", $"{o.Web.DataDir} not found yet.");
        else if (string.IsNullOrEmpty(dataReaders)) r.Pass(Files, "Key and log folder permissions", "Encryption keys and logs are not readable by ordinary users.");
        else r.Fail(Files, "Key and log folder permissions", $"Readable by: {dataReaders}.", "Re-run Setup's install (it locks down the folder).");
    }
}
