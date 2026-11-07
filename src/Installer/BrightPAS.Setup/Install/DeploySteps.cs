using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace BrightPAS.Setup.Install;

/// <summary>Where the web application is installed and how it is published in IIS.</summary>
public sealed record WebDeployOptions
{
    public string InstallDir { get; init; } = @"C:\Program Files\BrightPAS\Web";
    public string DataDir { get; init; } = @"C:\ProgramData\BrightPAS";
    public string SiteName { get; init; } = "BrightPAS";
    public string AppPoolName { get; init; } = "BrightPAS";
    public string? HostName { get; init; }
    public int HttpsPort { get; init; } = 443;
    public string? CertificateThumbprint { get; init; }
    /// <summary>Windows account the app pool uses to reach SQL Server (e.g. TDS\PAS-WEB$). When null a SQL login is created.</summary>
    public string? AppWindowsLogin { get; init; }
    public string AppSqlLogin { get; init; } = "brightpas_app";
    public string? PackagePath { get; init; }
}

public static class WebPackage
{
    /// <summary>Finds the newest BrightPAS-Web-*.zip next to Setup.exe (or the explicit path).</summary>
    public static string? Find(string? explicitPath, string folder)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) return File.Exists(explicitPath) ? explicitPath : null;
        return Directory.Exists(folder)
            ? Directory.GetFiles(folder, "BrightPAS-Web-*.zip").OrderByDescending(f => Version.TryParse(Path.GetFileNameWithoutExtension(f)["BrightPAS-Web-".Length..], out var v) ? v : new Version(0, 0)).FirstOrDefault()
            : null;
    }

    /// <summary>Generates a strong random password for the application's SQL login (never shown to anyone).</summary>
    public static string GeneratePassword()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%*-_=+";
        var p = new char[32];
        for (var i = 0; i < p.Length; i++) p[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        // Guarantee SQL Server complexity rules: upper, lower, digit, symbol.
        p[0] = 'Q'; p[1] = 'z'; p[2] = '7'; p[3] = '#';
        RandomNumberGenerator.Shuffle(p.AsSpan());
        return new string(p);
    }

    /// <summary>Writes appsettings.Production.json with the app connection string and key storage path.</summary>
    public static void WriteProductionSettings(string installDir, string connectionString, string keysPath)
    {
        // Built with JsonNode (trim-safe) rather than reflection-based serialisation.
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["ConnectionStrings"] = new System.Text.Json.Nodes.JsonObject { ["Pas"] = connectionString },
            ["DataProtection"] = new System.Text.Json.Nodes.JsonObject { ["KeysPath"] = keysPath },
            ["Logging"] = new System.Text.Json.Nodes.JsonObject
            {
                ["LogLevel"] = new System.Text.Json.Nodes.JsonObject { ["Default"] = "Warning", ["Microsoft.AspNetCore"] = "Warning" }
            },
            ["AllowedHosts"] = "*"
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(installDir, "appsettings.Production.json"), json);
    }
}

/// <summary>Installs the .NET 10 Hosting Bundle if it is missing and its installer has been placed next to Setup.exe.</summary>
public sealed class HostingBundleStep : IInstallStep
{
    public string Name => "Install the .NET 10 Hosting Bundle";
    public string Description => "The PAS web application needs the .NET 10 Hosting Bundle (runtime + IIS module), installed after IIS.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (!c.Probe.IsWindows) return StepResult.Skip("Not running on Windows.");
        if (c.Options.SkipWeb) return StepResult.Skip("Skipped by option --skip-web.");
        if (c.Probe.AspNetCoreModuleV2Installed && c.Probe.AspNetCoreRuntimeVersions.Any(v => v.Major == 10))
            return StepResult.Already(".NET 10 Hosting Bundle is installed.");
        var installer = Directory.Exists(c.SetupFolder) ? Directory.GetFiles(c.SetupFolder, "dotnet-hosting-10.*-win.exe").OrderDescending().FirstOrDefault() : null;
        if (installer is null && c.Options.WhatIf)
            return StepResult.Planned("The .NET 10 Hosting Bundle will be needed: place 'dotnet-hosting-10.x.x-win.exe' (from dotnet.microsoft.com) next to Setup.exe.");
        if (installer is null)
            return StepResult.Fail("The .NET 10 Hosting Bundle is not installed. Download 'ASP.NET Core 10 Runtime – Windows Hosting Bundle' from Microsoft (dotnet.microsoft.com), place the file next to Setup.exe or install it, then run Setup again.");
        if (c.Options.WhatIf) return StepResult.Planned($"Would run {Path.GetFileName(installer)} quietly, then restart IIS.");
        var logFile = Path.Combine(Path.GetTempPath(), $"BrightPAS-HostingBundle-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        c.Log.Detail($"Running {Path.GetFileName(installer)} (its own log: {logFile}). This can take a few minutes.");
        var r = await c.Processes.RunAsync(installer, $"/install /quiet /norestart /log \"{logFile}\"", ct);
        c.Log.Debug($"Hosting Bundle installer exit code {r.ExitCode}.");
        if (r.ExitCode is not (0 or 3010)) return StepResult.Fail($"Hosting Bundle installer returned code {r.ExitCode}.");
        if (r.ExitCode == 3010) c.RebootRequired = true;
        var restart = await c.Processes.RunPowerShellAsync("$ErrorActionPreference='Stop'; net stop was /y | Out-Null; net start w3svc | Out-Null; 'IIS restarted.'", ct);
        return restart.ExitCode == 0 ? StepResult.Done("Hosting Bundle installed and IIS restarted.") : StepResult.Fail($"IIS restart failed: {restart.Output}");
    }
}

/// <summary>Creates a least-privilege database login for the web application (read/write data only; no schema changes).</summary>
public sealed class AppDatabaseLoginStep : IInstallStep
{
    public string Name => "Create the application's database login";
    public string Description => "The web application gets its own login that can read and write data but cannot change or drop tables.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (c.Options.SkipWeb) return StepResult.Skip("Skipped by option --skip-web.");
        var w = c.Web;
        if (c.Options.WhatIf)
            return StepResult.Planned(w.AppWindowsLogin is null ? $"Would create/refresh SQL login [{w.AppSqlLogin}] with a new random password." : $"Would create Windows login [{w.AppWindowsLogin}].");

        var db = c.Options.Database;
        // Single-server installation: the website's own Windows identity (IIS APPPOOL\<pool>) logs in to the local
        // SQL Server – no password exists anywhere. The app pool must exist first so that Windows knows the account.
        var windowsLogin = w.AppWindowsLogin;
        if (windowsLogin is null && c.Probe.IsWindows && c.Options.IsLocalSqlServer())
        {
            windowsLogin = $"IIS APPPOOL\\{w.AppPoolName}";
            c.Log.Detail($"SQL Server is on this machine: the website will connect as Windows account '{windowsLogin}' (no password).");
            var ensurePool = AppPoolScript(w.AppPoolName);
            c.Log.Block("PowerShell (create app pool before SQL login)", ensurePool);
            var pr = await c.Processes.RunPowerShellAsync(ensurePool, ct);
            c.Log.Block("PowerShell output", pr.Output);
            if (pr.ExitCode != 0) return StepResult.Fail("Could not create the IIS application pool (is IIS installed?). See the log file.");
        }

        c.Log.Debug($"Connecting: {c.Options.ConnectionString("master")}");
        await using var conn = new SqlConnection(c.Options.ConnectionString("master"));
        await conn.OpenAsync(ct);
        string login;
        if (windowsLogin is not null)
        {
            login = windowsLogin;
            c.Log.Debug($"Ensuring Windows login [{login}] exists.");
            await Exec(conn, $"IF SUSER_ID(@l) IS NULL EXEC('CREATE LOGIN ' + @q + ' FROM WINDOWS WITH DEFAULT_DATABASE = [{db}]');", ct, ("@l", login), ("@q", Quote(login)));
            var b = new SqlConnectionStringBuilder(c.Options.ConnectionString()) { IntegratedSecurity = true, ApplicationName = "BrightPAS" };
            b.Remove("User ID"); b.Remove("Password");
            c.AppConnectionString = b.ConnectionString;
        }
        else
        {
            login = w.AppSqlLogin;
            var pw = WebPackage.GeneratePassword();
            c.Log.AddSecret(pw);
            c.Log.Debug($"Ensuring SQL login [{login}] exists with a newly generated password (not logged).");
            await Exec(conn, """
                IF SUSER_ID(@l) IS NULL EXEC('CREATE LOGIN ' + @q + ' WITH PASSWORD = ' + @p + ', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF')
                ELSE EXEC('ALTER LOGIN ' + @q + ' WITH PASSWORD = ' + @p);
                """, ct, ("@l", login), ("@q", Quote(login)), ("@p", "N'" + pw.Replace("'", "''") + "'"));
            var b = new SqlConnectionStringBuilder(c.Options.ConnectionString()) { IntegratedSecurity = false, UserID = login, Password = pw, ApplicationName = "BrightPAS" };
            c.AppConnectionString = b.ConnectionString;
        }
        c.Log.Debug($"Website connection string: {c.AppConnectionString}");

        await using var dbConn = new SqlConnection(c.Options.ConnectionString());
        await dbConn.OpenAsync(ct);
        await Exec(dbConn, """
            IF USER_ID(@l) IS NULL EXEC('CREATE USER ' + @q + ' FOR LOGIN ' + @q);
            EXEC sp_addrolemember 'db_datareader', @l;
            EXEC sp_addrolemember 'db_datawriter', @l;
            """, ct, ("@l", login), ("@q", Quote(login)));
        c.Log.Debug($"Database user [{login}] has db_datareader + db_datawriter only (no schema rights).");
        return StepResult.Done($"Database login [{login}] ready (read/write data only).");
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary>
    /// Creates the app pool if needed, starts it, and waits (up to 60 s) until Windows can resolve its virtual
    /// account 'IIS APPPOOL\&lt;pool&gt;' – SQL Server cannot create the login before then (error 15401).
    /// </summary>
    public static string AppPoolScript(string pool)
    {
        var q = pool.Replace("'", "''");
        return $$"""
            $ErrorActionPreference = 'Stop'
            Import-Module WebAdministration
            $pool = '{{q}}'
            if (-not (Test-Path "IIS:\AppPools\$pool")) { New-WebAppPool -Name $pool | Out-Null; "Created app pool '$pool'." } else { "App pool '$pool' already exists." }
            Set-ItemProperty "IIS:\AppPools\$pool" -Name processModel.identityType -Value ApplicationPoolIdentity
            Set-ItemProperty "IIS:\AppPools\$pool" -Name managedRuntimeVersion -Value ''
            if ((Get-WebAppPoolState -Name $pool).Value -ne 'Started') { Start-WebAppPool -Name $pool }
            $account = "IIS APPPOOL\$pool"
            $sid = $null
            for ($i = 0; $i -lt 30 -and -not $sid; $i++) {
                try { $sid = ([System.Security.Principal.NTAccount]$account).Translate([System.Security.Principal.SecurityIdentifier]).Value }
                catch { Start-Sleep -Seconds 2 }
            }
            if (-not $sid) { throw "Windows does not recognise the account '$account' after 60 seconds. Check that the Windows Process Activation Service (WAS) and World Wide Web Publishing Service (W3SVC) are running, then run Setup again." }
            "Account '$account' is known to Windows (SID $sid)."
            """;
    }

    private static async Task Exec(SqlConnection conn, string sql, CancellationToken ct, params (string Name, string Value)[] ps)
    {
        await using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>Copies the web application into place, writes its settings and locks down the folders.</summary>
public sealed class DeployWebFilesStep : IInstallStep
{
    public string Name => "Install the PAS web application files";
    public string Description => "Copies the web application, writes its protected settings file and restricts folder access.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (!c.Probe.IsWindows) return StepResult.Skip("Not running on Windows.");
        if (c.Options.SkipWeb) return StepResult.Skip("Skipped by option --skip-web.");
        var pkg = WebPackage.Find(c.Web.PackagePath, c.SetupFolder);
        var w = c.Web;
        if (c.Options.WhatIf)
            return StepResult.Planned(pkg is null ? "Web package (BrightPAS-Web-<version>.zip) will be needed next to Setup.exe." : $"Would install {Path.GetFileName(pkg)} to {w.InstallDir}.");
        if (pkg is null) return StepResult.Fail("Web package (BrightPAS-Web-<version>.zip) not found next to Setup.exe.");
        if (c.AppConnectionString is null) return StepResult.Fail("Application database login was not prepared.");

        // Stop the app pool (if it exists) so files are not locked.
        await c.Processes.RunPowerShellAsync($"Import-Module WebAdministration; if (Test-Path 'IIS:\\AppPools\\{w.AppPoolName}') {{ Stop-WebAppPool -Name '{w.AppPoolName}' -ErrorAction SilentlyContinue; Start-Sleep -Seconds 3 }}", ct);

        Directory.CreateDirectory(w.InstallDir);
        c.Log.Detail($"Package: {pkg} ({new FileInfo(pkg).Length / 1024:N0} KB)");
        ZipFile.ExtractToDirectory(pkg, w.InstallDir, overwriteFiles: true);
        c.Log.Debug($"Extracted {Directory.GetFiles(w.InstallDir, "*", SearchOption.AllDirectories).Length} files to {w.InstallDir}.");
        var keys = Path.Combine(w.DataDir, "keys");
        var logs = Path.Combine(w.DataDir, "logs");
        Directory.CreateDirectory(keys);
        Directory.CreateDirectory(logs);
        WebPackage.WriteProductionSettings(w.InstallDir, c.AppConnectionString, keys);

        var script = PowerShellScripts.SecureFolders(w.InstallDir, w.DataDir, w.AppPoolName);
        c.Log.Block("PowerShell (secure folders)", script);
        var r = await c.Processes.RunPowerShellAsync(script, ct);
        c.Log.Block("PowerShell output", r.Output);
        return r.ExitCode == 0 ? StepResult.Done($"Installed {Path.GetFileName(pkg)} to {w.InstallDir}.") : StepResult.Fail($"Could not secure folders: {r.Output}");
    }
}

/// <summary>Creates the IIS application pool and HTTPS-only site.</summary>
public sealed class ConfigureIisSiteStep : IInstallStep
{
    public string Name => "Publish the PAS website in IIS (HTTPS only)";
    public string Description => "Creates a dedicated application pool and an HTTPS-only site. Uses your certificate, or a self-signed one for the sandbox.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (!c.Probe.IsWindows) return StepResult.Skip("Not running on Windows.");
        if (c.Options.SkipWeb) return StepResult.Skip("Skipped by option --skip-web.");
        var w = c.Web;
        var host = w.HostName ?? System.Net.Dns.GetHostEntry(string.Empty).HostName;
        if (c.Options.WhatIf) return StepResult.Planned($"Would create app pool and site '{w.SiteName}' at https://{host}:{w.HttpsPort}/ ({(w.CertificateThumbprint is null ? "self-signed certificate" : "certificate " + w.CertificateThumbprint)}).");
        var script = PowerShellScripts.ConfigureSite(w.SiteName, w.AppPoolName, w.InstallDir, host, w.HttpsPort, w.CertificateThumbprint);
        c.Log.Block("PowerShell (IIS site)", script);
        var r = await c.Processes.RunPowerShellAsync(script, ct);
        c.Log.Block("PowerShell output", r.Output);
        if (r.ExitCode != 0)
        {
            foreach (var l in r.Output.Split('\n').Where(l => l.StartsWith("ERROR:") || l.StartsWith("WHERE:"))) c.Log.Detail(l.Trim());
            return StepResult.Fail("IIS site set-up failed. The exact error is shown above and in the log file.");
        }
        c.SiteUrl = w.HttpsPort == 443 ? $"https://{host}/" : $"https://{host}:{w.HttpsPort}/";
        if (r.Output.Contains("SELF-SIGNED", StringComparison.Ordinal)) c.SelfSignedCertificate = true;
        return StepResult.Done($"Site published at {c.SiteUrl}");
    }
}

/// <summary>Checks the website answers over HTTPS.</summary>
public sealed class WebHealthCheckStep : IInstallStep
{
    public string Name => "Check the website is running";
    public string Description => "Calls the health page over HTTPS on this server.";

    public async Task<StepResult> RunAsync(InstallContext c, CancellationToken ct)
    {
        if (!c.Probe.IsWindows || c.Options.SkipWeb) return StepResult.Skip("Website not installed on this run.");
        if (c.Options.WhatIf) return StepResult.Planned("Would call https://localhost/health.");
        using var handler = new HttpClientHandler
        {
            // Only for the local health check: the sandbox certificate may be self-signed and issued to the server name.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var url = $"https://localhost:{c.Web.HttpsPort}/health";
        string last = string.Empty;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                var res = await http.GetAsync(url, ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                if (res.IsSuccessStatusCode) return StepResult.Done($"Website is running: {body}");
                last = $"HTTP {(int)res.StatusCode}";
                c.Log.Debug($"Health check attempt {attempt + 1}: {last}: {body}");
            }
            catch (HttpRequestException ex) { last = ex.Message; c.Log.Debug($"Health check attempt {attempt + 1}: {ex.GetType().Name}: {ex.Message}"); }
            catch (TaskCanceledException) { last = "timed out"; }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        return StepResult.Fail($"The website did not respond ({last}). Check the Windows Event Log (Application) for 'IIS AspNetCore Module' errors and send it with the Setup log.");
    }
}
