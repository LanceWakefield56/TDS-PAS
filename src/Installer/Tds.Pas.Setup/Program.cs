using Tds.Pas.Setup.Checks;

// TDS PAS – guided installer, step 1: prerequisite checks.
// Usage: Setup.exe [--sql-server NAME[:PORT]] [--drive C:\] [--quiet]
var options = SetupOptions.Parse(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;
Write(ConsoleColor.Cyan, "TDS PAS Setup – step 1 of the platform installer: checking this server");
Console.WriteLine("Nothing is installed or changed in this step.\n");

if (!options.Quiet && options.SqlServer is null && !Console.IsInputRedirected)
{
    Console.Write("SQL Server name for the PAS database (press Enter to skip): ");
    var answer = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(answer)) options = options with { SqlServer = answer };
}

var (host, port) = SetupOptions.SplitHost(options.SqlServer);
var context = new SetupContext(options.Drive, host, port);
var report = CheckRunner.Run(PrerequisiteChecks.All(), context, SystemProbeBase.Create(), TimeProvider.System);

string? lastCategory = null;
foreach (var o in report.Outcomes)
{
    if (o.Check.Category != lastCategory) { Write(ConsoleColor.White, $"\n{o.Check.Category}"); lastCategory = o.Check.Category; }
    var colour = o.Result.Status switch
    {
        CheckStatus.Pass => ConsoleColor.Green, CheckStatus.Warning => ConsoleColor.Yellow,
        CheckStatus.Fail => ConsoleColor.Red, _ => ConsoleColor.DarkGray
    };
    Console.ForegroundColor = colour;
    Console.Write($"  [{CheckReport.Label(o.Result.Status)}] ");
    Console.ResetColor();
    Console.WriteLine($"{o.Check.Name}: {o.Result.Detail}");
    if (o.Result.Fix is not null) Console.WriteLine($"         What to do: {o.Result.Fix}");
}

Console.WriteLine();
if (report.ReadyToInstall)
    Write(ConsoleColor.Green, report.Warnings > 0 ? $"READY with {report.Warnings} warning(s)." : "READY – all checks passed.");
else
    Write(ConsoleColor.Red, $"NOT READY – {report.Failures} problem(s) must be fixed first.");

var reportPath = Path.Combine(AppContext.BaseDirectory, $"setup-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
try { File.WriteAllText(reportPath, report.ToText()); Console.WriteLine($"Report saved: {reportPath}"); }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine("The report could not be saved next to Setup."); }

if (!options.Quiet && !Console.IsInputRedirected) { Console.WriteLine("\nPress Enter to close."); Console.ReadLine(); }
return report.ExitCode;

static void Write(ConsoleColor colour, string text) { Console.ForegroundColor = colour; Console.WriteLine(text); Console.ResetColor(); }

internal sealed record SetupOptions(string Drive, string? SqlServer, bool Quiet)
{
    public static SetupOptions Parse(string[] args)
    {
        var o = new SetupOptions(OperatingSystem.IsWindows() ? @"C:\" : "/", null, false);
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--sql-server" when i + 1 < args.Length: o = o with { SqlServer = args[++i] }; break;
                case "--drive" when i + 1 < args.Length: o = o with { Drive = args[++i] }; break;
                case "--quiet": o = o with { Quiet = true }; break;
            }
        }
        return o;
    }

    public static (string? Host, int Port) SplitHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return (null, 1433);
        var parts = value.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[1], out var p) ? (parts[0], p) : (value, 1433);
    }
}
