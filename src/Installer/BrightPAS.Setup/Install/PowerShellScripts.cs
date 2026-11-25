namespace BrightPAS.Setup.Install;

/// <summary>
/// PowerShell used by the installer. Each script is safe to run more than once and throws on error
/// ($ErrorActionPreference = 'Stop'), which makes PowerShell exit with a non-zero code.
/// </summary>
public static class PowerShellScripts
{
    /// <summary>Minimal IIS role: only what the PAS needs. No WebDAV, FTP, CGI, ASP or directory browsing.</summary>
    public const string InstallIis = """
        $ErrorActionPreference = 'Stop'
        Import-Module ServerManager
        $features = @('Web-Server','Web-WebServer','Web-Common-Http','Web-Default-Doc','Web-Http-Errors','Web-Static-Content',
                      'Web-Health','Web-Http-Logging','Web-Request-Monitor','Web-Performance','Web-Stat-Compression',
                      'Web-Security','Web-Filtering','Web-IP-Security','Web-Mgmt-Console','Web-Scripting-Tools','Web-AppInit')
        $result = Install-WindowsFeature -Name $features
        if (-not $result.Success) { throw 'IIS feature installation failed.' }
        foreach ($unwanted in @('Web-DAV-Publishing','Web-Dir-Browsing','Web-Ftp-Server','Web-CGI','Web-ASP')) {
            if ((Get-WindowsFeature -Name $unwanted).Installed) { Uninstall-WindowsFeature -Name $unwanted | Out-Null }
        }
        "RestartNeeded=$($result.RestartNeeded)"
        """;

    /// <summary>Only TLS 1.2 and 1.3; old protocols disabled for server and client; .NET uses strong crypto.</summary>
    public const string HardenTls = """
        $ErrorActionPreference = 'Stop'
        $base = 'HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols'
        function Set-Protocol($name, $enabled) {
            foreach ($side in 'Server','Client') {
                $key = Join-Path (Join-Path $base $name) $side
                New-Item -Path $key -Force | Out-Null
                New-ItemProperty -Path $key -Name 'Enabled' -Value ([int]$enabled) -PropertyType DWord -Force | Out-Null
                New-ItemProperty -Path $key -Name 'DisabledByDefault' -Value ([int](-not $enabled)) -PropertyType DWord -Force | Out-Null
            }
        }
        foreach ($p in 'SSL 2.0','SSL 3.0','TLS 1.0','TLS 1.1') { Set-Protocol $p $false }
        Set-Protocol 'TLS 1.2' $true
        if ([Environment]::OSVersion.Version.Build -ge 20348) { Set-Protocol 'TLS 1.3' $true }  # Windows Server 2022+
        foreach ($k in 'HKLM:\SOFTWARE\Microsoft\.NETFramework\v4.0.30319','HKLM:\SOFTWARE\WOW6432Node\Microsoft\.NETFramework\v4.0.30319') {
            if (Test-Path $k) {
                New-ItemProperty -Path $k -Name 'SchUseStrongCrypto' -Value 1 -PropertyType DWord -Force | Out-Null
                New-ItemProperty -Path $k -Name 'SystemDefaultTlsVersions' -Value 1 -PropertyType DWord -Force | Out-Null
            }
        }
        'TLS settings applied (restart required).'
        """;

    /// <summary>Server-level IIS hardening (CIS IIS 10 benchmark items that apply before the PAS site exists).</summary>
    public const string HardenIis = """
        $ErrorActionPreference = 'Stop'
        Import-Module WebAdministration
        if (Get-Website -Name 'Default Web Site' -ErrorAction SilentlyContinue) { Remove-Website -Name 'Default Web Site' }
        if (Test-Path 'IIS:\AppPools\DefaultAppPool') { Remove-WebAppPool -Name 'DefaultAppPool' }
        $psPath = 'MACHINE/WEBROOT/APPHOST'
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/directoryBrowse' -Name 'enabled' -Value $false
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering' -Name 'removeServerHeader' -Value $true
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering' -Name 'allowDoubleEscaping' -Value $false
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering' -Name 'allowHighBitCharacters' -Value $false
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering/requestLimits' -Name 'maxAllowedContentLength' -Value 136314880
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering/requestLimits' -Name 'maxUrl' -Value 4096
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering/requestLimits' -Name 'maxQueryString' -Value 2048
        foreach ($verb in 'TRACE','TRACK','OPTIONS') {
            $existing = Get-WebConfiguration -PSPath $psPath -Filter "system.webServer/security/requestFiltering/verbs/add[@verb='$verb']"
            if (-not $existing) { Add-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering/verbs' -Name '.' -Value @{ verb = $verb; allowed = 'False' } }
        }
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/httpErrors' -Name 'errorMode' -Value 'Custom'
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.web/trace' -Name 'enabled' -Value $false -ErrorAction SilentlyContinue
        $headers = Get-WebConfiguration -PSPath $psPath -Filter "system.webServer/httpProtocol/customHeaders/add[@name='X-Powered-By']"
        if ($headers) { Remove-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/httpProtocol/customHeaders' -Name '.' -AtElement @{ name = 'X-Powered-By' } }
        'IIS server-level hardening applied.'
        """;

    private static string Q(string v) => "'" + v.Replace("'", "''") + "'";

    /// <summary>Restricts the web folder to Administrators, SYSTEM and the app pool (read-only); data folder writable by the app pool.</summary>
    /// <summary>
    /// 0.5.6: locks down the folders the correct way. Permissions are set ONCE on each top folder (with inheritance
    /// flags) and every file and sub-folder is reset to inherit them. 0.5.0–0.5.5 applied the folder rule to every
    /// file with /T, which left existing files without an Administrators entry ("Access denied" for Setup itself).
    /// Afterwards it checks that Administrators and the app pool account really can reach a file in each folder.
    /// </summary>
    public static string SecureFolders(string installDir, string dataDir, string appPool) => """
        $ErrorActionPreference = 'Stop'
        Import-Module WebAdministration
        $pool = __POOL__
        if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
        $identity = "IIS AppPool\$pool"
        function Invoke-Icacls([string[]] $arguments) {
            $out = & icacls.exe @arguments 2>&1
            $code = $LASTEXITCODE
            $out | Where-Object { $_ -and $_ -notmatch '^processed (file|dir)' } | ForEach-Object { "  icacls: $_" }
            if ($code -ne 0) { throw "icacls $($arguments -join ' ') failed with exit code $code" }
        }
        foreach ($item in @(@{ Path = __INSTALL__; Right = 'RX' }, @{ Path = __DATA__; Right = 'M' })) {
            $dir = $item.Path
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            # Top folder only: Administrators + SYSTEM full control, app pool account read (web) or modify (data); inherited by everything below.
            Invoke-Icacls @($dir, '/inheritance:r', '/grant:r', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F', "${identity}:(OI)(CI)$($item.Right)", '/C')
            # Everything below: drop any explicit entries and inherit from the top folder.
            Get-ChildItem -LiteralPath $dir -Force | ForEach-Object { Invoke-Icacls @($_.FullName, '/reset', '/T', '/C', '/Q') }
            # Prove it: Administrators can read a file here, and the app pool account has the expected right.
            $probe = Join-Path $dir '.brightpas-access-check'
            Set-Content -LiteralPath $probe -Value 'ok'; $null = Get-Content -LiteralPath $probe; Remove-Item -LiteralPath $probe -Force
            $acl = Get-Acl -LiteralPath $dir
            if (-not ($acl.Access | Where-Object { $_.IdentityReference -like "*\$pool" })) { throw "The app pool account is missing from the permissions of $dir" }
            "Secured: $dir (Administrators/SYSTEM full control; $identity $($item.Right); files inherit)."
        }
        'Folders secured.'
        """.Replace("__POOL__", Q(appPool)).Replace("__INSTALL__", Q(installDir)).Replace("__DATA__", Q(dataDir));

    /// <summary>
    /// 0.5.6: repairs folders damaged by older versions before files are replaced: takes ownership for
    /// Administrators and resets every file to inherit, so Setup can overwrite them. Safe to run every time.
    /// </summary>
    public static string RepairFolderAccess(string installDir, string dataDir) => """
        $ErrorActionPreference = 'Stop'
        foreach ($dir in @(__INSTALL__, __DATA__)) {
            if (-not (Test-Path -LiteralPath $dir)) { "Not present yet: $dir"; continue }
            & takeown.exe /F $dir /A /R /D Y 2>&1 | Where-Object { $_ -match 'ERROR|FEHLER|INFO: ' } | ForEach-Object { "  takeown: $_" }
            if ($LASTEXITCODE -ne 0) { throw "takeown failed on $dir (exit code $LASTEXITCODE)" }
            & icacls.exe $dir /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' /C /Q | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "icacls grant failed on $dir (exit code $LASTEXITCODE)" }
            Get-ChildItem -LiteralPath $dir -Force | ForEach-Object {
                & icacls.exe $_.FullName /reset /T /C /Q | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "icacls reset failed on $($_.FullName) (exit code $LASTEXITCODE)" }
            }
            Get-ChildItem -LiteralPath $dir -Recurse -File -Force | Where-Object { $_.IsReadOnly } | ForEach-Object { $_.IsReadOnly = $false }
            "Access repaired: $dir (owner Administrators; all files inherit)."
        }
        """.Replace("__INSTALL__", Q(installDir)).Replace("__DATA__", Q(dataDir));

    /// <summary>
    /// 0.5.5: takes the site offline before files are replaced. Drops app_offline.htm (the ASP.NET Core Module then
    /// unloads the app), stops the pool, waits until its worker process (w3wp) has really exited – up to 90 s – and
    /// ends it if it is still running. Files of a running site are locked, which caused "Access to the path is denied".
    /// </summary>
    public static string StopSiteForUpdate(string appPool, string installDir) => """
        $ErrorActionPreference = 'Stop'
        Import-Module WebAdministration
        $pool = __POOL__; $path = __PATH__
        if (Test-Path $path) {
            Set-Content -Path (Join-Path $path 'app_offline.htm') -Value '<!doctype html><title>BrightPAS</title><p>BrightPAS is being updated. Please try again in a few minutes.</p>' -Encoding UTF8
            'Site taken offline (app_offline.htm).'
        }
        if (-not (Test-Path "IIS:\AppPools\$pool")) { "App pool '$pool' does not exist yet - nothing to stop."; return }
        if ((Get-WebAppPoolState -Name $pool).Value -ne 'Stopped') { Stop-WebAppPool -Name $pool; "Stopping app pool '$pool'..." }
        function Get-PoolProcesses { @(Get-CimInstance Win32_Process -Filter "Name='w3wp.exe'" | Where-Object { $_.CommandLine -match ('-ap "' + [regex]::Escape($pool) + '"') }) }
        for ($i = 0; $i -lt 45; $i++) {
            if ((Get-WebAppPoolState -Name $pool).Value -eq 'Stopped' -and (Get-PoolProcesses).Count -eq 0) { break }
            Start-Sleep -Seconds 2
        }
        $left = Get-PoolProcesses
        if ($left.Count -gt 0) {
            foreach ($p in $left) { "Worker process $($p.ProcessId) still running after 90 s - ending it."; Stop-Process -Id $p.ProcessId -Force }
            Start-Sleep -Seconds 2
        }
        "App pool '$pool' is stopped; no worker process is running."
        """.Replace("__POOL__", Q(appPool)).Replace("__PATH__", Q(installDir));

    /// <summary>
    /// Dedicated app pool (no managed code, always running) and an HTTPS site. With <paramref name="httpRedirect"/>, port 80 is bound too, but BrightPAS
    /// answers every plain-HTTP request with a redirect to https:// (never a page); without it, port 80 is not bound at all.
    /// </summary>
    public static string ConfigureSite(string site, string appPool, string installDir, string hostName, int port, string? thumbprint, bool httpRedirect = true) => """
        $ErrorActionPreference = 'Stop'
        Import-Module WebAdministration
        $pool = __POOL__; $site = __SITE__; $path = __PATH__; $hostName = __HOST__; $port = __PORT__; $thumb = __THUMB__
        if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
        Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''
        Set-ItemProperty "IIS:\AppPools\$pool" -Name startMode -Value 'AlwaysRunning'
        Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
        Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.identityType -Value 'ApplicationPoolIdentity'
        Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.loadUserProfile -Value $true
        if (-not $thumb) {
            $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.FriendlyName -eq 'BrightPAS (self-signed)' -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
            if (-not $cert) {
                $cert = New-SelfSignedCertificate -DnsName $hostName, $env:COMPUTERNAME, 'localhost' -CertStoreLocation Cert:\LocalMachine\My `
                    -FriendlyName 'BrightPAS (self-signed)' -NotAfter (Get-Date).AddYears(2) -KeyLength 2048 -KeyExportPolicy NonExportable
            }
            $thumb = $cert.Thumbprint
            'SELF-SIGNED certificate in use (sandbox only).'
        }
        if (-not (Get-Website -Name $site -ErrorAction SilentlyContinue)) {
            New-Website -Name $site -PhysicalPath $path -ApplicationPool $pool -Port $port -Ssl | Out-Null
        } else {
            Set-ItemProperty "IIS:\Sites\$site" -Name physicalPath -Value $path
            Set-ItemProperty "IIS:\Sites\$site" -Name applicationPool -Value $pool
        }
        # Keep BrightPAS running (background jobs such as the lab-results listener and licence checks), even with no web traffic.
        try { Set-ItemProperty "IIS:\Sites\$site" -Name applicationDefaults.preloadEnabled -Value $true } catch { "Note: preload not set ($($_.Exception.Message))" }
        Get-WebBinding -Name $site | Where-Object { $_.protocol -ne 'https' } | Remove-WebBinding
        if (-not (Get-WebBinding -Name $site -Protocol https)) { New-WebBinding -Name $site -Protocol https -Port $port -IPAddress '*' | Out-Null }
        if (__REDIRECT__) {
            $taken = Get-Website | Where-Object { $_.Name -ne $site } | ForEach-Object { Get-WebBinding -Name $_.Name -Protocol http } | Where-Object { $_.bindingInformation -like '*:80:*' }
            if ($taken) { "NOTE: port 80 is used by another IIS site, so http:// is not redirected. Staff must type https://$hostName" }
            else { New-WebBinding -Name $site -Protocol http -Port 80 -IPAddress '*' | Out-Null; "http://$hostName will redirect to https:// (port 80 answers only with a redirect)." }
        } else { 'Port 80 not used: only https:// works.' }
        Remove-Item "IIS:\SslBindings\0.0.0.0!$port" -ErrorAction SilentlyContinue
        (Get-WebBinding -Name $site -Protocol https | Select-Object -First 1).AddSslCertificate($thumb, 'my')
        Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue
        Start-Website -Name $site
        "Site ready: $site on port $port"
        """.Replace("__POOL__", Q(appPool)).Replace("__SITE__", Q(site)).Replace("__PATH__", Q(installDir)).Replace("__HOST__", Q(hostName))
           .Replace("__PORT__", port.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("__THUMB__", thumbprint is null ? "''" : Q(thumbprint)).Replace("__REDIRECT__", httpRedirect ? "$true" : "$false");
}
