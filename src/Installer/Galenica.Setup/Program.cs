using Galenica.Setup.Checks;
using Galenica.Setup.Install;

// Galenica – guided platform installer.
//   Setup.exe                      interactive menu
//   Setup.exe check   [--sql-server NAME[:PORT]] [--drive C:\] [--quiet]
//   Setup.exe install --sql-server NAME [--database Galenica] [--sql-user USER] [--trust-server-certificate] [--skip-iis] [--what-if] [--quiet]
//                     [--ignore-prerequisites]   (test labs only)
//                     [--skip-web] [--web-package PATH] [--install-dir PATH] [--host-name NAME] [--https-port 443]
//                     [--certificate-thumbprint THUMB] [--app-windows-login DOMAIN\PAS-WEB$]
//   SQL password for --sql-user in quiet mode: environment variable GALENICA_SQL_PASSWORD
Console.OutputEncoding = System.Text.Encoding.UTF8;
var cli = Cli.Parse(args);
var interactive = !cli.Quiet && !Console.IsInputRedirected;

Ui.Title($"Galenica Setup {cli.Install.Version}");

var command = cli.Command;
if (command is null && interactive)
{
    Console.WriteLine("1. Check this server (changes nothing)");
    Console.WriteLine("2. Install or upgrade the PAS platform");
    Console.WriteLine("3. Preview installation (what would happen, changes nothing)");
    Console.Write("\nChoose 1, 2 or 3: ");
    var choice = Console.ReadLine()?.Trim();
    command = choice switch { "2" => "install", "3" => "install", _ => "check" };
    if (choice == "3") cli = cli with { Install = cli.Install with { WhatIf = true } };
}
command ??= "check";

int exit;
if (command == "install")
{
    var o = cli.Install;
    if (interactive)
    {
        o = o with { SqlServer = o.SqlServer ?? Ui.Ask("SQL Server name (e.g. PAS-SQL)", null) };
        o = o with { Database = Ui.Ask("Database name", o.Database) ?? o.Database };
        if (o.SqlUser is null && !Ui.YesNo("Use Windows authentication to SQL Server?", true))
            o = o with { IntegratedSecurity = false, SqlUser = Ui.Ask("SQL login name", null) };
        if (o.SqlUser is not null && o.SqlPassword is null) o = o with { IntegratedSecurity = false, SqlPassword = Ui.Secret("SQL password") };
        if (!o.TrustServerCertificate)
            o = o with { TrustServerCertificate = Ui.YesNo("Does SQL Server use a self-signed certificate (sandbox only)?", false) };
        if (!o.WhatIf && !Ui.YesNo("\nReady to install. This will change this server's IIS and security settings. Continue?", false))
        {
            Console.WriteLine("Cancelled. Nothing was changed.");
            return 0;
        }
    }
    var log = new ConsoleInstallLog();
    var context = new InstallContext(o, SystemProbeBase.Create(), new ProcessRunner(), log, TimeProvider.System, cli.Web);
    var records = await Installer.RunAsync(Installer.PlatformSteps(), context);
    var failed = records.Any(r => r.Result.Outcome == StepOutcome.Failed);

    Console.WriteLine();
    if (failed) Ui.Colour(ConsoleColor.Red, "INSTALLATION STOPPED – fix the problem above and run Setup again (completed steps are skipped safely).");
    else if (o.WhatIf) Ui.Colour(ConsoleColor.Cyan, "PREVIEW COMPLETE – nothing was changed.");
    else
    {
        Ui.Colour(ConsoleColor.Green, "PLATFORM INSTALLATION COMPLETE.");
        if (context.BootstrapCode is not null)
        {
            Console.WriteLine("\nOne-time code for setting up the first administrator (valid 7 days):");
            Ui.Colour(ConsoleColor.Yellow, $"\n        {context.BootstrapCode}\n");
            Console.WriteLine("Write it down now – it is not saved anywhere and cannot be shown again.");
            Console.WriteLine("If it is lost, run Setup again to create a new one.");
        }
        if (context.SiteUrl is not null)
        {
            Console.WriteLine($"\nOpen the PAS from a staff computer:  {context.SiteUrl}");
            if (context.SelfSignedCertificate)
                Console.WriteLine("(Sandbox: the certificate is self-signed, so browsers will warn. Install a proper certificate before live use.)");
        }
        if (context.RebootRequired) Ui.Colour(ConsoleColor.Yellow, "\nRestart this server to finish applying the security settings.");
    }
    var logPath = Path.Combine(AppContext.BaseDirectory, $"setup-install-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    try { File.WriteAllLines(logPath, log.Lines); Console.WriteLine($"\nLog saved: {logPath}"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    exit = failed ? 2 : 0;
}
else
{
    var (host, port) = InstallOptions.SplitHost(cli.Install.SqlServer);
    var sql = cli.Install.SqlServer;
    if (sql is null && interactive) sql = Ui.Ask("SQL Server name to test (press Enter to skip)", null);
    if (sql is not null) (host, port) = InstallOptions.SplitHost(sql);
    var report = CheckRunner.Run(PrerequisiteChecks.All(), new SetupContext(cli.Install.Drive, sql is null ? null : host, port), SystemProbeBase.Create(), TimeProvider.System);
    Ui.PrintReport(report);
    var reportPath = Path.Combine(AppContext.BaseDirectory, $"setup-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    try { File.WriteAllText(reportPath, report.ToText()); Console.WriteLine($"Report saved: {reportPath}"); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine("The report could not be saved next to Setup."); }
    exit = report.ExitCode;
}

if (interactive) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
return exit;

internal sealed record Cli(string? Command, InstallOptions Install, WebDeployOptions Web, bool Quiet)
{
    public static Cli Parse(string[] args)
    {
        string? command = null;
        var o = new InstallOptions();
        var w = new WebDeployOptions();
        var quiet = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i].ToLowerInvariant();
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (a)
            {
                case "check" or "install": command = a; break;
                case "--sql-server": o = o with { SqlServer = Next() }; break;
                case "--database": o = o with { Database = Next() }; break;
                case "--sql-user": o = o with { SqlUser = Next(), IntegratedSecurity = false }; break;
                case "--trust-server-certificate": o = o with { TrustServerCertificate = true }; break;
                case "--skip-iis": o = o with { SkipIis = true }; break;
                case "--what-if": o = o with { WhatIf = true }; break;
                case "--ignore-prerequisites": o = o with { IgnorePrerequisites = true }; break;
                case "--skip-web": o = o with { SkipWeb = true }; break;
                case "--web-package": w = w with { PackagePath = Next() }; break;
                case "--install-dir": w = w with { InstallDir = Next() }; break;
                case "--host-name": w = w with { HostName = Next() }; break;
                case "--https-port": w = w with { HttpsPort = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) }; break;
                case "--certificate-thumbprint": w = w with { CertificateThumbprint = Next().Replace(" ", "") }; break;
                case "--app-windows-login": w = w with { AppWindowsLogin = Next() }; break;
                case "--drive": o = o with { Drive = Next() }; break;
                case "--quiet": quiet = true; break;
            }
        }
        // Password only from the environment in quiet mode (never on the command line, where it would be logged).
        var pw = Environment.GetEnvironmentVariable("GALENICA_SQL_PASSWORD");
        if (o.SqlUser is not null && pw is not null) o = o with { SqlPassword = pw };
        return new Cli(command, o, w, quiet);
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
                CheckStatus.Pass => ConsoleColor.Green, CheckStatus.Warning => ConsoleColor.Yellow,
                CheckStatus.Fail => ConsoleColor.Red, _ => ConsoleColor.DarkGray
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
