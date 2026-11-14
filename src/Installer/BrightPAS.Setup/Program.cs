using System.Runtime.InteropServices;
using BrightPAS.Setup.Checks;
using BrightPAS.Setup.Install;
using BrightPAS.Setup.Security;
using BrightPAS.Setup.Support;

// BrightPAS – guided installer and security self-test.
//   Setup.exe                         interactive menu
//   Setup.exe check          [--sql-server NAME[:PORT]] [--drive C:\]
//   Setup.exe install        --sql-server NAME [--database BrightPAS] [--sql-user USER] [--trust-server-certificate]
//                            [--skip-iis] [--skip-web] [--what-if] [--ignore-prerequisites] [--web-package PATH]
//                            [--install-dir PATH] [--host-name NAME] [--https-port 443] [--no-http-redirect] [--certificate-thumbprint THUMB]
//                            [--app-windows-login DOMAIN\ACCOUNT$] [--sms-provider Modica|GenericHttp|TestOutbox|Off]
//                            [--email-provider Smtp|TestOutbox|Off] [--demo-data]   (demo/training: fills a NEW database with invented data)
//   Setup.exe security-test  [--url https://server/] [--sql-server NAME] [--database BrightPAS] [--no-rate-limit-tests]
//   Setup.exe diagnostics    (collects logs and settings into one zip file to send for help)
//   Common: --verbose (show every detail on screen), --quiet (no prompts)
//   SQL password for --sql-user without prompts: environment variable BRIGHTPAS_SQL_PASSWORD (never on the command line).
Console.OutputEncoding = System.Text.Encoding.UTF8;
var cli = Cli.Parse(args);
var interactive = !cli.Quiet && !Console.IsInputRedirected;
var version = cli.Install.Version;

Ui.Title($"BrightPAS Setup {version}");

var command = cli.Command;
if (command is null && interactive)
{
    Console.WriteLine("1. Check this server (changes nothing)");
    Console.WriteLine("2. Install or upgrade BrightPAS");
    Console.WriteLine("3. Preview installation (shows what would happen, changes nothing)");
    Console.WriteLine("4. Security, penetration and audit self-test (changes nothing)");
    Console.WriteLine("5. Collect diagnostics to send for help (changes nothing)");
    Console.Write("\nType 1, 2, 3, 4 or 5 and press Enter: ");
    var choice = Console.ReadLine()?.Trim();
    command = choice switch { "2" or "3" => "install", "4" => "security-test", "5" => "diagnostics", _ => "check" };
    if (choice == "3") cli = cli with { Install = cli.Install with { WhatIf = true } };
}
command ??= "check";

using var log = new InstallLog(InstallLog.DefaultPath(command switch { "install" => cli.Install.WhatIf ? "Preview" : "Install", "security-test" => "SecurityTest", "diagnostics" => "Diagnostics", _ => "Check" }), cli.Verbose);
log.AddSecret(cli.Install.SqlPassword);
WriteHeader(log, command, args);

int exit;
try
{
    exit = command switch
    {
        "install" => await RunInstallAsync(),
        "security-test" => await RunSecurityTestAsync(cli.Install),
        "diagnostics" => await Diagnostics.CollectAsync(new ProcessRunner(), log, cli.Web) is null ? 2 : 0,
        _ => RunCheck()
    };
}
catch (Exception ex)
{
    log.Error($"Setup stopped because of an unexpected error: {ex.Message}", ex);
    exit = 3;
}

log.Debug($"Setup finished with exit code {exit}.");
if (log.FilePath is not null)
{
    Console.WriteLine($"\nFull log file: {log.FilePath}");
    try { File.Copy(log.FilePath, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(log.FilePath)), overwrite: true); Console.WriteLine($"(A copy is also next to Setup.exe.)"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    if (exit != 0 && command != "diagnostics")
    {
        Console.WriteLine("If you need help, send this log file. Passwords and codes are masked in it.");
        if (interactive && Ui.YesNo("Collect a full diagnostics zip (logs, event log, IIS settings) to send as well?", true))
            await Diagnostics.CollectAsync(new ProcessRunner(), log, cli.Web);
    }
}
if (interactive) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
return exit;

int RunCheck()
{
    var sql = cli.Install.SqlServer;
    if (sql is null && interactive) sql = Ui.Ask("SQL Server name to test (on a single server type localhost; press Enter to skip)", "localhost");
    var (host, port) = InstallOptions.SplitHost(sql);
    var probe = SystemProbeBase.Create();
    LogProbe(log, probe, cli.Install.Drive);
    var report = CheckRunner.Run(PrerequisiteChecks.All(), new SetupContext(cli.Install.Drive, sql is null ? null : host, port), probe, TimeProvider.System);
    Ui.PrintReport(report);
    foreach (var o in report.Outcomes) log.Debug($"[{CheckReport.Label(o.Result.Status)}] {o.Check.Category} | {o.Check.Name} | {o.Result.Detail}{(o.Result.Fix is null ? "" : " | Fix: " + o.Result.Fix)}");
    return report.ExitCode;
}

async Task<int> RunInstallAsync()
{
    var o = cli.Install;
    if (interactive)
    {
        Console.WriteLine("Answer the questions below. Press Enter to accept the value in [brackets].\n");
        o = o with { SqlServer = o.SqlServer ?? Ui.Ask("SQL Server name (single server: localhost)", "localhost") };
        if (!o.DemoData)
        {
            Console.WriteLine("\nDemonstration / training system? BrightPAS can fill a NEW database with an invented practice (staff accounts, patients,");
            Console.WriteLine("three months of lists, bookings, invoices, referrals, results, roster and interfaces) so you can try every feature straight away.");
            Console.WriteLine("Never choose this for a real (production) system.");
            o = o with { DemoData = Ui.YesNo("Load demonstration data?", false) };
        }
        o = o with { Database = Ui.Ask("Database name", o.DemoData && o.Database == "BrightPAS" ? "BrightPAS_Demo" : o.Database) ?? o.Database };
        if (o.SqlUser is null && !Ui.YesNo("Connect to SQL Server with your Windows account (recommended)?", true))
            o = o with { IntegratedSecurity = false, SqlUser = Ui.Ask("SQL login name", "sa") };
        if (o.SqlUser is not null && o.SqlPassword is null) o = o with { IntegratedSecurity = false, SqlPassword = Ui.Secret("SQL password (hidden as you type)") };
        if (!o.TrustServerCertificate)
            o = o with { TrustServerCertificate = Ui.YesNo("Is SQL Server using its own self-signed certificate (normal for a new sandbox install)?", true) };
        if (!o.SkipWeb && cli.Web.SmsProvider is null)
        {
            Console.WriteLine("\nSMS vendor for reminders and patient messages:");
            Console.WriteLine("  1. Modica (TDS default – NZ)   2. Another SMS vendor (HTTP API)   3. Test mode – nothing is sent   4. Off");
            var sms = Ui.Ask("Type 1–4, or press Enter to keep the current setting (new installations: Modica)", null)?.Trim();
            var pick = sms switch { "1" => "Modica", "2" => "GenericHttp", "3" => "TestOutbox", "4" => "Off", _ => null };
            Console.WriteLine("Email sender:  1. SMTP server (e.g. Microsoft 365)   2. Test mode – nothing is sent   3. Off");
            var mail = Ui.Ask("Type 1–3, or press Enter to keep the current setting (new installations: SMTP)", null)?.Trim();
            var mailPick = mail switch { "1" => "Smtp", "2" => "TestOutbox", "3" => "Off", _ => null };
            cli = cli with { Web = cli.Web with { SmsProvider = pick, EmailProvider = mailPick } };
            Console.WriteLine("(Vendor passwords are entered later in BrightPAS: SMS & email → Settings. They are never stored by Setup.)");
        }
        if (!o.WhatIf && !Ui.YesNo("\nReady to install. This changes this server's IIS and security settings. Continue?", false))
        {
            log.Info("Cancelled by the user. Nothing was changed.");
            return 0;
        }
    }
    log.AddSecret(o.SqlPassword);
    log.Debug($"Install options: SqlServer={o.SqlServer}; Database={o.Database}; WindowsAuth={o.IntegratedSecurity}; SqlUser={o.SqlUser ?? "-"}; TrustServerCertificate={o.TrustServerCertificate}; " +
              $"LocalSql={o.IsLocalSqlServer()}; SkipIis={o.SkipIis}; SkipWeb={o.SkipWeb}; WhatIf={o.WhatIf}; IgnorePrerequisites={o.IgnorePrerequisites}; DemoData={o.DemoData}");
    log.Debug($"Web options: InstallDir={cli.Web.InstallDir}; DataDir={cli.Web.DataDir}; Site={cli.Web.SiteName}; Pool={cli.Web.AppPoolName}; Host={cli.Web.HostName ?? "(this server)"}; " +
              $"Port={cli.Web.HttpsPort}; HttpRedirect={cli.Web.HttpRedirect}; Certificate={cli.Web.CertificateThumbprint ?? "(self-signed)"}; AppWindowsLogin={cli.Web.AppWindowsLogin ?? "(automatic)"}; Package={cli.Web.PackagePath ?? "(search next to Setup.exe)"}; " +
              $"SmsVendor={cli.Web.SmsProvider ?? "(keep; default Modica)"}; EmailSender={cli.Web.EmailProvider ?? "(keep; default Smtp)"}");

    var probe = SystemProbeBase.Create();
    LogProbe(log, probe, o.Drive);
    var context = new InstallContext(o, probe, new ProcessRunner(), log, TimeProvider.System, cli.Web);
    var records = await Installer.RunAsync(Installer.PlatformSteps(), context);
    if (context.BootstrapCode is not null) log.AddSecret(context.BootstrapCode.Replace("-", string.Empty));
    var failed = records.Any(r => r.Result.Outcome == StepOutcome.Failed);

    Console.WriteLine();
    log.Debug("Summary: " + string.Join(" | ", records.Select(r => $"{r.Step.Name}: {r.Result.Outcome}")));
    if (failed)
    {
        Ui.Colour(ConsoleColor.Red, "INSTALLATION STOPPED – read the red message above. Fix the problem and run Setup again (finished steps are skipped safely).");
        return 2;
    }
    if (o.WhatIf) { Ui.Colour(ConsoleColor.Cyan, "PREVIEW COMPLETE – nothing was changed."); return 0; }

    Ui.Colour(ConsoleColor.Green, "INSTALLATION COMPLETE.");
    if (o.DemoData)
    {
        Ui.Colour(ConsoleColor.Magenta, context.DemoDataLoaded ? "\nDEMONSTRATION SYSTEM – invented data only. Never enter real patient information."
            : "\nDEMONSTRATION SYSTEM – the data loads when the website first starts (open it once and wait a minute or two).");
        Console.WriteLine($"Every demo account uses the password:  {DemoSignIn.Password}");
        Console.WriteLine($"Add this key ONCE to your authenticator app (same for every demo account):  {DemoSignIn.AuthenticatorSecret}");
        Console.WriteLine("(Microsoft/Google Authenticator: Add account › Enter a setup key › Time-based.)  Demo accounts:");
        foreach (var (u, who) in DemoSignIn.Accounts) Console.WriteLine($"   {u,-14} {who}");
        Console.WriteLine("The full list is also on the website's sign-in page.");
    }
    else if (context.BootstrapCode is not null)
    {
        Console.WriteLine("\nOne-time code for setting up the first administrator (valid 7 days):");
        Ui.Colour(ConsoleColor.Yellow, $"\n        {context.BootstrapCode}\n");
        Console.WriteLine("Write it down now. It is NOT saved in the log and cannot be shown again.");
        Console.WriteLine("If it is lost, run Setup again to make a new one (until the first administrator is set up).");
    }
    if (context.SiteUrl is not null)
    {
        Console.WriteLine($"\nOpen BrightPAS from the Windows 11 workstation:  {context.SiteUrl}");
        if (context.SelfSignedCertificate) Console.WriteLine("(Sandbox: the certificate is self-signed – see the installation guide for trusting it on the workstation.)");
    }
    if (context.RebootRequired) Ui.Colour(ConsoleColor.Yellow, "\nRESTART this server when convenient to finish applying the security settings.");

    if (interactive && context.SiteUrl is not null && Ui.YesNo("\nRun the security, penetration and audit self-test now?", true))
        await RunSecurityTestAsync(o, context.SiteUrl.Replace(System.Net.Dns.GetHostName(), "localhost", StringComparison.OrdinalIgnoreCase));
    return 0;
}

async Task<int> RunSecurityTestAsync(InstallOptions db, string? url = null)
{
    if (interactive && cli.Command == "security-test" || (interactive && cli.Command is null && url is null))
    {
        db = db with { SqlServer = db.SqlServer ?? Ui.Ask("SQL Server name (single server: localhost)", "localhost") };
        db = db with { Database = Ui.Ask("Database name", db.Database) ?? db.Database };
        if (db.SqlUser is null && !Ui.YesNo("Connect to SQL Server with your Windows account?", true))
            db = db with { IntegratedSecurity = false, SqlUser = Ui.Ask("SQL login name", "sa") };
        if (db.SqlUser is not null && db.SqlPassword is null) db = db with { IntegratedSecurity = false, SqlPassword = Ui.Secret("SQL password (hidden)") };
        if (!db.TrustServerCertificate) db = db with { TrustServerCertificate = Ui.YesNo("Is SQL Server using its own self-signed certificate?", true) };
        url ??= cli.SecurityUrl ?? Ui.Ask("BrightPAS website address", "https://localhost/");
    }
    log.AddSecret(db.SqlPassword);
    var options = new SecurityTestOptions { Url = url ?? cli.SecurityUrl ?? "https://localhost/", Database = db, Web = cli.Web, IncludeRateLimitTests = !cli.NoRateLimitTests };
    log.Info($"\nSECURITY, PENETRATION AND AUDIT SELF-TEST against {options.Url}");
    log.Detail("Safe and read-only: it sends the same requests an attacker might, but changes nothing. Failed sign-in attempts by 'sectest-…' will appear in the audit log.");
    var report = await SecurityTester.RunAsync(options, new ProcessRunner(), log);

    Console.WriteLine();
    foreach (var g in report.Findings.GroupBy(f => f.Area))
    {
        Ui.Colour(ConsoleColor.White, g.Key);
        foreach (var f in g)
        {
            Console.ForegroundColor = f.Status switch { FindingStatus.Pass => ConsoleColor.Green, FindingStatus.Warn => ConsoleColor.Yellow, FindingStatus.Fail => ConsoleColor.Red, FindingStatus.Info => ConsoleColor.Cyan, _ => ConsoleColor.DarkGray };
            Console.Write($"  [{SecurityReport.Label(f.Status)}] ");
            Console.ResetColor();
            Console.WriteLine($"{f.Test}: {f.Detail}");
            if (f.Fix is not null) Console.WriteLine($"         What to do: {f.Fix}");
        }
    }
    Console.WriteLine();
    Ui.Colour(report.ExitCode == 2 ? ConsoleColor.Red : report.ExitCode == 1 ? ConsoleColor.Yellow : ConsoleColor.Green, $"RESULT: {report.Verdict}.");
    var dir = OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrightPAS", "Reports") : AppContext.BaseDirectory;
    try
    {
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var html = Path.Combine(dir, $"BrightPAS-Security-Report-{stamp}.html");
        File.WriteAllText(html, report.ToHtml());
        File.WriteAllText(Path.ChangeExtension(html, ".txt"), report.ToText());
        Console.WriteLine($"Report saved: {html}  (open it in a web browser)");
        try { File.Copy(html, Path.Combine(AppContext.BaseDirectory, Path.GetFileName(html)), true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Warn($"Could not save the report: {ex.Message}"); }
    Console.WriteLine(SecurityReport.Disclaimer);
    return report.ExitCode;
}

static void WriteHeader(InstallLog log, string command, string[] args)
{
    log.Debug(new string('=', 100));
    log.Debug($"BrightPAS Setup {typeof(Cli).Assembly.GetName().Version} – command '{command}'");
    log.Debug($"Started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} on {Environment.MachineName} as {Environment.UserDomainName}\\{Environment.UserName}");
    log.Debug($"Operating system: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}); .NET {Environment.Version}; process {(Environment.Is64BitProcess ? 64 : 32)}-bit");
    log.Debug($"Setup folder: {AppContext.BaseDirectory}; working folder: {Environment.CurrentDirectory}");
    log.Debug($"Command line: {string.Join(' ', args)}");
    log.Debug($"Time zone: {TimeZoneInfo.Local.Id}; culture: {System.Globalization.CultureInfo.CurrentCulture.Name}");
    log.Debug(new string('=', 100));
}

static void LogProbe(InstallLog log, ISystemProbe p, string drive)
{
    log.Debug($"Probe: IsWindows={p.IsWindows}; OsVersion={p.OsVersion}; InstallationType={p.WindowsInstallationType ?? "-"}; 64bit={p.Is64Bit}; Admin={p.IsAdministrator}; " +
              $"MemoryGB={p.TotalMemoryBytes / 1073741824.0:0.0}; FreeDiskGB({drive})={(p.FreeDiskBytes(drive) ?? 0) / 1073741824.0:0.0}; IIS={p.IisMajorVersion?.ToString() ?? "none"}; " +
              $"ANCMv2={p.AspNetCoreModuleV2Installed}; AspNetCore=[{string.Join(", ", p.AspNetCoreRuntimeVersions)}]; TimeZone={p.TimeZoneId}");
    foreach (var proto in new[] { "SSL 3.0", "TLS 1.0", "TLS 1.1", "TLS 1.2", "TLS 1.3" })
        log.Debug($"Probe: SCHANNEL {proto} server enabled = {p.TlsProtocolServerEnabled(proto)?.ToString() ?? "OS default"}");
}

internal sealed record Cli(string? Command, InstallOptions Install, WebDeployOptions Web, bool Quiet, bool Verbose, string? SecurityUrl, bool NoRateLimitTests)
{
    public static Cli Parse(string[] args)
    {
        string? command = null, url = null;
        var o = new InstallOptions();
        var w = new WebDeployOptions();
        bool quiet = false, verbose = false, noRate = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (a)
            {
                case "check" or "install" or "security-test" or "diagnostics": command = a; break;
                case "--sql-server": o = o with { SqlServer = Next() }; break;
                case "--database": o = o with { Database = Next() }; break;
                case "--sql-user": o = o with { SqlUser = Next(), IntegratedSecurity = false }; break;
                case "--trust-server-certificate": o = o with { TrustServerCertificate = true }; break;
                case "--skip-iis": o = o with { SkipIis = true }; break;
                case "--skip-web": o = o with { SkipWeb = true }; break;
                case "--what-if": o = o with { WhatIf = true }; break;
                case "--ignore-prerequisites": o = o with { IgnorePrerequisites = true }; break;
                case "--demo-data": o = o with { DemoData = true }; break;
                case "--drive": o = o with { Drive = Next() }; break;
                case "--web-package": w = w with { PackagePath = Next() }; break;
                case "--install-dir": w = w with { InstallDir = Next() }; break;
                case "--host-name": w = w with { HostName = Next() }; break;
                case "--https-port": w = w with { HttpsPort = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) }; break;
                case "--no-http-redirect": w = w with { HttpRedirect = false }; break;
                case "--certificate-thumbprint": w = w with { CertificateThumbprint = Next().Replace(" ", "") }; break;
                case "--app-windows-login": w = w with { AppWindowsLogin = Next() }; break;
                case "--sms-provider": w = w with { SmsProvider = MessagingVendors.Normalise(Next(), MessagingVendors.Sms, "SMS vendor") }; break;
                case "--email-provider": w = w with { EmailProvider = MessagingVendors.Normalise(Next(), MessagingVendors.Email, "email sender") }; break;
                case "--url": url = Next(); break;
                case "--no-rate-limit-tests": noRate = true; break;
                case "--verbose": verbose = true; break;
                case "--quiet": quiet = true; break;
            }
        }
        var pw = Environment.GetEnvironmentVariable("BRIGHTPAS_SQL_PASSWORD");
        if (o.SqlUser is not null && pw is not null) o = o with { SqlPassword = pw };
        return new Cli(command, o, w, quiet, verbose, url, noRate);
    }
}

internal static class Ui
{
    public static void Title(string text) { Colour(ConsoleColor.Cyan, text); Console.WriteLine(new string('=', text.Length) + "\n"); }
    public static void Colour(ConsoleColor c, string text) { Console.ForegroundColor = c; Console.WriteLine(text); Console.ResetColor(); }

    public static string? Ask(string prompt, string? defaultValue)
    {
        Console.Write(defaultValue is null ? $"{prompt}: " : $"{prompt} [{defaultValue}]: ");
        var v = Console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(v) ? defaultValue : v;
    }

    public static bool YesNo(string prompt, bool defaultYes)
    {
        Console.Write($"{prompt} {(defaultYes ? "[Y/n]" : "[y/N]")}: ");
        var v = Console.ReadLine()?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(v) ? defaultYes : v.StartsWith('y');
    }

    public static string Secret(string prompt)
    {
        Console.Write($"{prompt}: ");
        var chars = new List<char>();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
            chars.Add(k.KeyChar);
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }

    public static void PrintReport(CheckReport report)
    {
        string? last = null;
        foreach (var o in report.Outcomes)
        {
            if (o.Check.Category != last) { Colour(ConsoleColor.White, $"\n{o.Check.Category}"); last = o.Check.Category; }
            Console.ForegroundColor = o.Result.Status switch
            {
                CheckStatus.Pass => ConsoleColor.Green,
                CheckStatus.Warning => ConsoleColor.Yellow,
                CheckStatus.Fail => ConsoleColor.Red,
                _ => ConsoleColor.DarkGray
            };
            Console.Write($"  [{CheckReport.Label(o.Result.Status)}] ");
            Console.ResetColor();
            Console.WriteLine($"{o.Check.Name}: {o.Result.Detail}");
            if (o.Result.Fix is not null) Console.WriteLine($"         What to do: {o.Result.Fix}");
        }
        Console.WriteLine();
        if (report.ReadyToInstall) Colour(ConsoleColor.Green, report.Warnings > 0 ? $"READY with {report.Warnings} warning(s)." : "READY – all checks passed.");
        else Colour(ConsoleColor.Red, $"NOT READY – {report.Failures} problem(s) must be fixed first.");
    }
}
