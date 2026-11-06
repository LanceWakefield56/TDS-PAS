using System.Net;
using System.Text;
using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Security;

public enum FindingStatus { Pass, Warn, Fail, Info, Skip }

public sealed record Finding(string Area, string Test, FindingStatus Status, string Detail, string? Fix = null);

public sealed record SecurityTestOptions
{
    public string Url { get; init; } = "https://localhost/";
    /// <summary>Accept a self-signed website certificate (sandbox). The test still reports it as a warning.</summary>
    public bool AllowSelfSigned { get; init; } = true;
    public bool IncludeRateLimitTests { get; init; } = true;
    public InstallOptions Database { get; init; } = new();
    public WebDeployOptions Web { get; init; } = new();
}

/// <summary>Collects findings and writes the text and HTML reports.</summary>
public sealed class SecurityReport
{
    public List<Finding> Findings { get; } = [];
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
    public string Machine { get; } = Environment.MachineName;
    public int Count(FindingStatus s) => Findings.Count(f => f.Status == s);
    public int ExitCode => Count(FindingStatus.Fail) > 0 ? 2 : Count(FindingStatus.Warn) > 0 ? 1 : 0;

    public void Add(string area, string test, FindingStatus status, string detail, string? fix = null) => Findings.Add(new(area, test, status, detail, fix));
    public void Pass(string area, string test, string detail) => Add(area, test, FindingStatus.Pass, detail);
    public void Warn(string area, string test, string detail, string fix) => Add(area, test, FindingStatus.Warn, detail, fix);
    public void Fail(string area, string test, string detail, string fix) => Add(area, test, FindingStatus.Fail, detail, fix);
    public void Info(string area, string test, string detail) => Add(area, test, FindingStatus.Info, detail);

    public static string Label(FindingStatus s) => s switch
    {
        FindingStatus.Pass => "PASS", FindingStatus.Warn => "WARN", FindingStatus.Fail => "FAIL", FindingStatus.Info => "INFO", _ => "SKIP"
    };

    public string Verdict => Count(FindingStatus.Fail) > 0
        ? $"{Count(FindingStatus.Fail)} serious problem(s) to fix"
        : Count(FindingStatus.Warn) > 0 ? $"No serious problems; {Count(FindingStatus.Warn)} warning(s) to review" : "All checks passed";

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("BRIGHTPAS – SECURITY, PENETRATION AND AUDIT SELF-TEST");
        sb.AppendLine($"Machine: {Machine}   Started: {StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Result: {Verdict}  (pass {Count(FindingStatus.Pass)}, warn {Count(FindingStatus.Warn)}, fail {Count(FindingStatus.Fail)})");
        sb.AppendLine(new string('=', 90));
        foreach (var g in Findings.GroupBy(f => f.Area))
        {
            sb.AppendLine().AppendLine(g.Key.ToUpperInvariant());
            foreach (var f in g)
            {
                sb.AppendLine($"  [{Label(f.Status)}] {f.Test}: {f.Detail}");
                if (f.Fix is not null) sb.AppendLine($"         What to do: {f.Fix}");
            }
        }
        sb.AppendLine().AppendLine(Disclaimer);
        return sb.ToString();
    }

    public const string Disclaimer = "This automated self-test checks common security settings and attacks. It does not replace the independent " +
        "penetration test and security review required before live patient data is used (specification section 11.1).";

    public string ToHtml()
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html><html lang="en-NZ"><head><meta charset="utf-8"><title>BrightPAS security self-test</title>
            <style>
            body{font-family:"Segoe UI",Arial,sans-serif;margin:2rem;color:#1B2730}h1{color:#1F5C7A;margin-bottom:.2rem}
            .sum{display:flex;gap:1rem;margin:1rem 0}.box{padding:.6rem 1rem;border-radius:8px;font-weight:700}
            .PASS{background:#E8F5E9;color:#1B5E20}.WARN{background:#FFF4D6;color:#8A5A00}.FAIL{background:#FDECEA;color:#B3261E}
            .INFO{background:#E3F2FD;color:#0D47A1}.SKIP{background:#ECEFF1;color:#546E7A}
            table{border-collapse:collapse;width:100%;margin-bottom:1.5rem}th,td{text-align:left;padding:.45rem .6rem;border-bottom:1px solid #D5DEE5;vertical-align:top}
            th{background:#F3F6F8}h2{color:#1F5C7A;margin-top:1.5rem}.fix{color:#5B6B77;font-size:.9rem}.tag{display:inline-block;min-width:3.2rem;text-align:center;border-radius:6px;padding:.1rem .3rem;font-weight:700;font-size:.8rem}
            </style></head><body>
            """);
        sb.Append($"<h1>BrightPAS – security, penetration and audit self-test</h1><p>Server <b>{E(Machine)}</b> · {StartedAt:dd MMM yyyy HH:mm} · <b>{E(Verdict)}</b></p>");
        sb.Append("<div class=\"sum\">");
        foreach (var s in new[] { FindingStatus.Pass, FindingStatus.Warn, FindingStatus.Fail, FindingStatus.Info })
            sb.Append($"<div class=\"box {Label(s)}\">{Label(s)}: {Count(s)}</div>");
        sb.Append("</div>");
        foreach (var g in Findings.GroupBy(f => f.Area))
        {
            sb.Append($"<h2>{E(g.Key)}</h2><table><tr><th style=\"width:5rem\">Result</th><th style=\"width:28%\">Test</th><th>Finding</th></tr>");
            foreach (var f in g)
                sb.Append($"<tr><td><span class=\"tag {Label(f.Status)}\">{Label(f.Status)}</span></td><td>{E(f.Test)}</td><td>{E(f.Detail)}" +
                          (f.Fix is null ? "" : $"<div class=\"fix\">What to do: {E(f.Fix)}</div>") + "</td></tr>");
            sb.Append("</table>");
        }
        sb.Append($"<p class=\"fix\">{E(Disclaimer)}</p></body></html>");
        return sb.ToString();
    }
}
