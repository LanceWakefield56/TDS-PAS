namespace BrightPAS.TestBot;

/// <summary>Command-line options. Run with --help for the list.</summary>
public sealed class Options
{
    public string Url { get; set; } = "https://localhost";
    public string Out { get; set; } = Path.Combine(Environment.CurrentDirectory, "TestBot-results-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));
    public string Browser { get; set; } = OperatingSystem.IsWindows() ? "msedge" : "chromium";
    public bool Headed { get; set; }
    /// <summary>quick = every page for every account; full = also tabs, buttons, forms, screen checks and phone/tablet sizes.</summary>
    public string Depth { get; set; } = "full";
    public bool Simulators { get; set; } = true;
    public bool TrustCertificate { get; set; } = true;
    public bool Restore { get; set; } = true;
    public string AppPool { get; set; } = "BrightPAS";
    public string WorkFolder { get; set; } = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrightPAS-TestBot")
        : "/tmp/brightpas-testbot";
    public HashSet<string> Only { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Skip { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int SlowMo { get; set; }
    public int ModicaPort { get; set; } = 9443;
    /// <summary>Virtual speech server and language model for BrightPAS Scribe (OpenAI-compatible and Claude-compatible APIs, http on loopback).</summary>
    public int AiPort { get; set; } = 9460;
    public int SmtpPort { get; set; } = 2525;
    public int ZebraPort { get; set; } = 9150;
    public bool Full => Depth.Equals("full", StringComparison.OrdinalIgnoreCase);
    public bool Wants(string area) => (Only.Count == 0 || Only.Contains(area)) && !Skip.Contains(area);

    public const string Help = """
        BrightPAS Test Robot – tests a DEMONSTRATION system by using it like staff would.

        Run it ON THE BRIGHTPAS SERVER, from an administrator command prompt:
            BrightPAS-TestBot.exe --url https://localhost

        Options:
          --url <address>        BrightPAS address (default https://localhost)
          --out <folder>         where the report goes (default .\TestBot-results-<date>)
          --depth quick|full     quick = open every page as every account; full (default) = also tabs, buttons,
                                 forms, screen checks, phone/tablet sizes and the virtual devices
          --only <areas>         comma list: signin,pages,buttons,forms,screens,permissions,devices,sizes
          --skip <areas>         comma list of areas to leave out
          --no-simulators        do not start the virtual printers, phones, email, HealthLink and lab
          --no-trust             do not trust the virtual SMS company's certificate (SMS vendor test is skipped)
          --keep-settings        leave BrightPAS pointed at the virtual devices afterwards
          --app-pool <name>      IIS app pool that must write to the virtual HealthLink folders (default BrightPAS)
          --browser msedge|chrome|chromium   (default msedge on Windows)
          --headed               show the browser while it works
          --slow <ms>            slow every action down (with --headed, to watch)

        It refuses to run unless the site is a demonstration system (Setup --demo-data).
        Results: report.html (open in a browser), results.json, screenshots\, devices\ and a zip of all of it.
        """;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value.");
            switch (args[i].ToLowerInvariant())
            {
                case "--url": o.Url = Next().TrimEnd('/'); break;
                case "--out": o.Out = Path.GetFullPath(Next()); break;
                case "--depth": o.Depth = Next(); break;
                case "--only": foreach (var a in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) o.Only.Add(a); break;
                case "--skip": foreach (var a in Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) o.Skip.Add(a); break;
                case "--no-simulators": o.Simulators = false; break;
                case "--no-trust": o.TrustCertificate = false; break;
                case "--keep-settings": o.Restore = false; break;
                case "--app-pool": o.AppPool = Next(); break;
                case "--browser": o.Browser = Next().ToLowerInvariant(); break;
                case "--headed": o.Headed = true; break;
                case "--slow": o.SlowMo = int.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--work": o.WorkFolder = Path.GetFullPath(Next()); break;
                case "--help" or "-h" or "/?": throw new HelpRequested();
                default: throw new ArgumentException($"Unknown option {args[i]}. Use --help.");
            }
        }
        return o;
    }
}

public sealed class HelpRequested : Exception;
