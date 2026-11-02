namespace Tds.Pas.Setup.Install;

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
}
