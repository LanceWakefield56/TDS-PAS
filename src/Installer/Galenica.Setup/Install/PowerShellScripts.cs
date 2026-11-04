namespace Galenica.Setup.Install;

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
                      'Web-Security','Web-Filtering','Web-IP-Security','Web-Mgmt-Console','Web-Scripting-Tools')
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
        Set-WebConfigurationProperty -PSPath $psPath -Filter 'system.webServer/security/requestFiltering/requestLimits' -Name 'maxAllowedContentLength' -Value 52428800
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
    public static string SecureFolders(string installDir, string dataDir, string appPool) => """
        $ErrorActionPreference = 'Stop'
        Import-Module WebAdministration
        $pool = __POOL__
        if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null }
        $identity = "IIS AppPool\$pool"
        & icacls __INSTALL__ /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${identity}:(OI)(CI)RX" /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls failed on web folder ($LASTEXITCODE)" }
        & icacls __DATA__ /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' "${identity}:(OI)(CI)M" /T /C /Q | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "icacls failed on data folder ($LASTEXITCODE)" }
        'Folders secured.'
        """.Replace("__POOL__", Q(appPool)).Replace("__INSTALL__", Q(installDir)).Replace("__DATA__", Q(dataDir));

    /// <summary>Dedicated app pool (no managed code, always running) and an HTTPS-only site.</summary>
    public static string ConfigureSite(string site, string appPool, string installDir, string hostName, int port, string? thumbprint) => """
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
            $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.FriendlyName -eq 'Galenica (self-signed)' -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
            if (-not $cert) {
                $cert = New-SelfSignedCertificate -DnsName $hostName, $env:COMPUTERNAME, 'localhost' -CertStoreLocation Cert:\LocalMachine\My `
                    -FriendlyName 'Galenica (self-signed)' -NotAfter (Get-Date).AddYears(2) -KeyLength 2048 -KeyExportPolicy NonExportable
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
        Get-WebBinding -Name $site | Where-Object { $_.protocol -ne 'https' } | Remove-WebBinding
        if (-not (Get-WebBinding -Name $site -Protocol https)) { New-WebBinding -Name $site -Protocol https -Port $port -IPAddress '*' | Out-Null }
        Remove-Item "IIS:\SslBindings\0.0.0.0!$port" -ErrorAction SilentlyContinue
        (Get-WebBinding -Name $site -Protocol https | Select-Object -First 1).AddSslCertificate($thumb, 'my')
        Start-WebAppPool -Name $pool -ErrorAction SilentlyContinue
        Start-Website -Name $site
        "Site ready: $site on port $port"
        """.Replace("__POOL__", Q(appPool)).Replace("__SITE__", Q(site)).Replace("__PATH__", Q(installDir)).Replace("__HOST__", Q(hostName))
           .Replace("__PORT__", port.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("__THUMB__", thumbprint is null ? "''" : Q(thumbprint));
}
