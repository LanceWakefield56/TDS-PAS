using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BrightPAS.TestBot;

public enum Outcome { Fail, Warn, Pass, Skip, Info }

/// <summary>One check result. Area = signin/pages/buttons/forms/screens/permissions/devices/sizes; Where = page or device.</summary>
public sealed record Finding(string Area, string Account, string Where, string Check, Outcome Outcome, string Detail, string? Screenshot, DateTimeOffset At);

/// <summary>Something a virtual device received (a label, an SMS, an email, a HealthLink file, a lab message, an export).</summary>
public sealed record DeviceItem(string Device, string Title, string Body, string? Image, DateTimeOffset At);

public sealed class Report
{
    private readonly object _gate = new();
    private readonly List<Finding> _findings = [];
    private readonly List<DeviceItem> _devices = [];
    private readonly Dictionary<string, string> _gallery = new(StringComparer.Ordinal);
    private int _shot;
    public string Folder { get; }
    public string ShotFolder => Path.Combine(Folder, "screenshots");
    public string DeviceFolder => Path.Combine(Folder, "devices");
    public DateTimeOffset Started { get; } = DateTimeOffset.Now;
    public Dictionary<string, string> Facts { get; } = new(StringComparer.Ordinal);

    public Report(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(ShotFolder);
        Directory.CreateDirectory(DeviceFolder);
    }

    public IReadOnlyList<Finding> Findings { get { lock (_gate) return _findings.ToList(); } }
    public IReadOnlyList<DeviceItem> Devices { get { lock (_gate) return _devices.ToList(); } }

    public void Add(string area, string account, string where, string check, Outcome outcome, string detail = "", string? shot = null)
    {
        var f = new Finding(area, account, where, check, outcome, detail, shot, DateTimeOffset.Now);
        lock (_gate) _findings.Add(f);
        if (outcome is Outcome.Fail or Outcome.Warn)
        {
            var c = Console.ForegroundColor;
            Console.ForegroundColor = outcome == Outcome.Fail ? ConsoleColor.Red : ConsoleColor.Yellow;
            Console.WriteLine($"  {outcome.ToString().ToUpperInvariant()} [{area}] {account} {where}: {check} – {Trim(detail, 200)}");
            Console.ForegroundColor = c;
        }
    }

    public void Device(string device, string title, string body, string? image = null)
    {
        lock (_gate) _devices.Add(new DeviceItem(device, title, body, image, DateTimeOffset.Now));
    }

    public string NextShotName(string label)
    {
        var n = Interlocked.Increment(ref _shot);
        var safe = new string(label.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (safe.Length > 60) safe = safe[..60];
        return $"{n:0000}-{safe}.png";
    }

    /// <summary>Adds a screenshot to the page gallery (one per page pattern and size), for reviewing how screens look.</summary>
    public void Gallery(string key, string file) { lock (_gate) _gallery.TryAdd(key, file); }

    public static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public (int Fail, int Warn, int Pass, int Skip) Counts()
    {
        var f = Findings;
        return (f.Count(x => x.Outcome == Outcome.Fail), f.Count(x => x.Outcome == Outcome.Warn), f.Count(x => x.Outcome == Outcome.Pass), f.Count(x => x.Outcome == Outcome.Skip));
    }

    public string Write()
    {
        var json = JsonSerializer.Serialize(new { started = Started, finished = DateTimeOffset.Now, facts = Facts, findings = Findings, devices = Devices }, new JsonSerializerOptions { WriteIndented = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        File.WriteAllText(Path.Combine(Folder, "results.json"), json, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Folder, "report.html"), Html(), new UTF8Encoding(false));
        var zip = Folder.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(Folder, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zip;
    }

    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private string Html()
    {
        var f = Findings;
        var (fail, warn, pass, skip) = Counts();
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>BrightPAS Test Robot report</title><style>
            body{font-family:Segoe UI,Arial,sans-serif;margin:0;color:#1f2a37;background:#f5f7fa}
            header{background:#123b5d;color:#fff;padding:18px 28px}header h1{margin:0;font-size:22px}header p{margin:4px 0 0;opacity:.85}
            main{padding:20px 28px;max-width:1500px}section{background:#fff;border:1px solid #dde3ea;border-radius:8px;padding:16px 20px;margin-bottom:18px}
            h2{font-size:18px;margin:0 0 10px}.tiles{display:flex;gap:12px;flex-wrap:wrap}.tile{flex:1;min-width:140px;border-radius:8px;padding:12px 16px;color:#fff}
            .tile b{display:block;font-size:28px}.t-fail{background:#b42318}.t-warn{background:#b54708}.t-pass{background:#067647}.t-skip{background:#475467}
            table{border-collapse:collapse;width:100%;font-size:13px}th,td{border-bottom:1px solid #e4e7ec;padding:6px 8px;text-align:left;vertical-align:top}
            th{background:#f2f4f7;position:sticky;top:0}.o-Fail{color:#b42318;font-weight:600}.o-Warn{color:#b54708;font-weight:600}.o-Pass{color:#067647}.o-Skip{color:#475467}
            details{margin:6px 0}summary{cursor:pointer;font-weight:600}.grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:12px}
            .grid figure{margin:0;border:1px solid #dde3ea;border-radius:6px;overflow:hidden;background:#fff}.grid img{width:100%;display:block}
            .grid figcaption{font-size:12px;padding:4px 8px}pre{white-space:pre-wrap;background:#f2f4f7;padding:8px;border-radius:6px;font-size:12px;max-height:260px;overflow:auto}
            .dev{border:1px solid #dde3ea;border-radius:6px;padding:8px 12px;margin:8px 0}.dev img{max-width:420px;border:1px solid #ccc}
            .muted{color:#667085}a{color:#175cd3}
            </style></head><body>
            """);
        sb.Append($"<header><h1>BrightPAS Test Robot report</h1><p>{H(Facts.GetValueOrDefault("site"))} · version {H(Facts.GetValueOrDefault("version"))} · {Started:ddd d MMM yyyy h:mm tt} · took {(DateTimeOffset.Now - Started).TotalMinutes:0} min</p></header><main>");
        sb.Append($"<section><div class=\"tiles\"><div class=\"tile t-fail\"><b>{fail}</b>problems</div><div class=\"tile t-warn\"><b>{warn}</b>warnings</div><div class=\"tile t-pass\"><b>{pass}</b>passed</div><div class=\"tile t-skip\"><b>{skip}</b>skipped</div></div>");
        sb.Append("<p class=\"muted\">Problems = something did not work. Warnings = works, but may confuse staff or fall short of accessibility/usability good practice. Send the zip file next to this report to Claude.</p></section>");

        // By area
        sb.Append("<section><h2>By area</h2><table><tr><th>Area</th><th>Problems</th><th>Warnings</th><th>Passed</th><th>Skipped</th></tr>");
        foreach (var g in f.GroupBy(x => x.Area).OrderBy(g => g.Key))
            sb.Append($"<tr><td>{H(g.Key)}</td><td class=\"o-Fail\">{g.Count(x => x.Outcome == Outcome.Fail)}</td><td class=\"o-Warn\">{g.Count(x => x.Outcome == Outcome.Warn)}</td><td class=\"o-Pass\">{g.Count(x => x.Outcome == Outcome.Pass)}</td><td>{g.Count(x => x.Outcome == Outcome.Skip)}</td></tr>");
        sb.Append("</table></section>");

        void Table(string title, IEnumerable<Finding> rows, bool open)
        {
            var list = rows.ToList();
            sb.Append($"<section><details {(open ? "open" : "")}><summary>{H(title)} ({list.Count})</summary><table><tr><th>Result</th><th>Area</th><th>Account</th><th>Where</th><th>Check</th><th>Detail</th><th>Picture</th></tr>");
            foreach (var x in list)
                sb.Append($"<tr><td class=\"o-{x.Outcome}\">{x.Outcome}</td><td>{H(x.Area)}</td><td>{H(x.Account)}</td><td>{H(x.Where)}</td><td>{H(x.Check)}</td><td>{H(Trim(x.Detail, 1500))}</td><td>{(x.Screenshot is null ? "" : $"<a href=\"screenshots/{H(x.Screenshot)}\">view</a>")}</td></tr>");
            sb.Append("</table></details></section>");
        }
        Table("Problems", f.Where(x => x.Outcome == Outcome.Fail), true);
        Table("Warnings", f.Where(x => x.Outcome == Outcome.Warn), true);

        var dev = Devices;
        sb.Append($"<section><details open><summary>Virtual devices and outside systems ({dev.Count} items)</summary>");
        foreach (var g in dev.GroupBy(d => d.Device))
        {
            sb.Append($"<h3>{H(g.Key)}</h3>");
            foreach (var d in g)
                sb.Append($"<div class=\"dev\"><b>{H(d.Title)}</b> <span class=\"muted\">{d.At:h:mm:ss tt}</span>{(d.Image is null ? "" : $"<div><img src=\"devices/{H(d.Image)}\" alt=\"{H(d.Title)}\"></div>")}<pre>{H(Trim(d.Body, 4000))}</pre></div>");
        }
        sb.Append("</details></section>");

        Table("Passed and skipped checks", f.Where(x => x.Outcome is Outcome.Pass or Outcome.Skip or Outcome.Info), false);

        List<KeyValuePair<string, string>> gal; lock (_gate) gal = _gallery.ToList();
        sb.Append($"<section><details><summary>Screens ({gal.Count}) – how each page looked</summary><div class=\"grid\">");
        foreach (var (k, v) in gal.OrderBy(x => x.Key, StringComparer.Ordinal))
            sb.Append($"<figure><a href=\"screenshots/{H(v)}\"><img loading=\"lazy\" src=\"screenshots/{H(v)}\" alt=\"{H(k)}\"></a><figcaption>{H(k)}</figcaption></figure>");
        sb.Append("</div></details></section>");
        sb.Append("<section><h2>Run details</h2><table>");
        foreach (var (k, v) in Facts.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append($"<tr><th>{H(k)}</th><td>{H(v)}</td></tr>");
        sb.Append("</table></section></main></body></html>");
        return sb.ToString();
    }
}
