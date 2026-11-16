using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using BrightPAS.Modules.Quality;

namespace BrightPAS.Web.Pdf;

public sealed record QualityPackData(string Organisation, DateOnly Month, IReadOnlyList<IndicatorSeries> Indicators, IReadOnlyList<Incident> Incidents,
    IReadOnlyList<Complaint> Complaints, int Compliments, IReadOnlyList<Risk> Risks, IReadOnlyList<CorrectiveAction> Actions, IReadOnlyList<PolicyDocument> PoliciesDue, Func<Complaint, string?> Overdue, DateOnly Today);

/// <summary>
/// Clinical Governance quality pack (spec 7.14.9): indicators with RAG and SPC signals, incidents by SAC with escalations, complaints against
/// Right 10 timeframes, compliments, high and extreme risks, overdue corrective actions and policies due for review. Aggregate only –
/// no patient names (incidents and complaints are listed by number and category).
/// </summary>
public static class QualityPdf
{
    private static string Rate(decimal? v, int scale) => v is null ? "—" : scale == 1 ? v.Value.ToString("0", CultureInfo.InvariantCulture) : v.Value.ToString("0.0", CultureInfo.InvariantCulture) + (scale == 100 ? "%" : " /1,000");

    private static Table Grid(Section sec, string[] headers, double[] widths, IEnumerable<string?[]> rows, Func<int, int, Color?>? shade = null)
    {
        var t = sec.AddTable();
        t.Borders.Width = 0.25; t.Borders.Color = Colors.Gray; t.Format.Font.Size = 8;
        foreach (var w in widths) t.AddColumn(Unit.FromCentimeter(w));
        var hr = t.AddRow(); hr.HeadingFormat = true; hr.Shading.Color = Colors.WhiteSmoke;
        for (var i = 0; i < headers.Length; i++) hr.Cells[i].AddParagraph(headers[i]).Format.Font.Bold = true;
        var n = 0;
        foreach (var row in rows)
        {
            var r = t.AddRow();
            for (var i = 0; i < row.Length; i++)
            {
                r.Cells[i].AddParagraph(row[i] ?? "");
                if (shade?.Invoke(n, i) is { } c) r.Cells[i].Shading.Color = c;
            }
            n++;
        }
        sec.AddParagraph().Format.SpaceAfter = 4;
        return t;
    }

    private static Color? RagColour(string rag) => rag switch { "green" => new Color(0xD5, 0xF0, 0xD8), "amber" => new Color(0xFF, 0xEB, 0xB3), "red" => new Color(0xF8, 0xC9, 0xC4), _ => null };

    public static byte[] Pack(QualityPackData d, string? password)
    {
        var doc = PdfReports.NewDocument($"Quality pack {d.Month:MMMM yyyy}", landscape: true);
        var sec = doc.LastSection;
        var head = sec.AddParagraph(d.Organisation); head.Format.Font.Size = 14; head.Format.Font.Bold = true;
        sec.AddParagraph($"Clinical Governance quality pack – {d.Month:MMMM yyyy}", StyleNames.Heading1);
        var note = sec.AddParagraph("Aggregate report: incidents and complaints are shown by number and category only. Indicators are calculated from live BrightPAS data; SPC signals: ● outside 3-sigma limits, ▲ shift (8 on one side), ↗ trend (6 rising or falling).");
        note.Format.Font.Size = 8; note.Format.Font.Color = Colors.DimGray; note.Format.SpaceAfter = 6;

        sec.AddParagraph("Quality indicators", StyleNames.Heading2);
        var inds = d.Indicators.ToList();
        Grid(sec, ["Indicator", "Target", "This month", "Last 3 months", "12-month centre", "Signals", "Owner"], [6.5, 2.2, 2.4, 4.6, 2.6, 2.6, 4.0],
            inds.Select(s =>
            {
                var pts = s.Points.Where(p => p.Rate is not null).ToList();
                var last3 = string.Join("  ", pts.TakeLast(3).Select(p => $"{p.Month:MMM} {Rate(p.Rate, s.Indicator.Scale)}"));
                var latest = pts.LastOrDefault();
                var signals = string.Join(" ", new[] { pts.Any(p => p.OutsideLimits) ? "●" : null, pts.Any(p => p.Shift) ? "▲" : null, pts.Any(p => p.Trend) ? "↗" : null }.Where(x => x is not null));
                return new string?[] { s.Indicator.Name, s.Indicator.Target is { } t ? Rate(t, s.Indicator.Scale) : "—", Rate(latest?.Rate, s.Indicator.Scale), last3, Rate(s.Centre, s.Indicator.Scale), signals, s.Indicator.Owner };
            }), (row, col) => col == 2 ? RagColour(inds[row].Rag) : null);

        var month = d.Incidents.Where(i => i.ReportedAt.Year == d.Month.Year && i.ReportedAt.Month == d.Month.Month).ToList();
        sec.AddParagraph($"Incidents – {month.Count} reported this month, {d.Incidents.Count(i => i.Status != IncidentStatus.Closed)} open", StyleNames.Heading2);
        Grid(sec, ["SAC", "Reported", "Open", "Escalated (SAC 1–2 / Always Report)"], [3, 3, 3, 8],
            Enumerable.Range(1, 4).Select(s => new string?[] { $"SAC {s}", month.Count(i => i.Sac == s).ToString(CultureInfo.InvariantCulture), d.Incidents.Count(i => i.Sac == s && i.Status != IncidentStatus.Closed).ToString(CultureInfo.InvariantCulture),
                s <= 2 ? string.Join(", ", d.Incidents.Where(i => i.Sac == s && i.Status != IncidentStatus.Closed).Select(i => i.Number)) : "" })
            .Append(["Not yet triaged", month.Count(i => i.Sac is null).ToString(CultureInfo.InvariantCulture), d.Incidents.Count(i => i.Sac is null && i.Status != IncidentStatus.Closed).ToString(CultureInfo.InvariantCulture), ""]));
        if (month.Count > 0)
            Grid(sec, ["Category", "Reported this month"], [6, 4], month.GroupBy(i => i.Category).OrderByDescending(g => g.Count()).Select(g => new string?[] { Spaced(g.Key.ToString()), g.Count().ToString(CultureInfo.InvariantCulture) }));
        var learning = d.Incidents.Where(i => i.Status == IncidentStatus.Closed && i.ClosedAt is { } c && c.Year == d.Month.Year && c.Month == d.Month.Month && !string.IsNullOrWhiteSpace(i.LearningSummary)).ToList();
        if (learning.Count > 0)
        {
            sec.AddParagraph("Learning from incidents closed this month", StyleNames.Heading2);
            foreach (var i in learning) sec.AddParagraph($"{i.Number} ({Spaced(i.Category.ToString())}): {i.LearningSummary}").Format.Font.Size = 8.5;
        }

        sec.AddParagraph($"Complaints (Right 10) – {d.Complaints.Count(c => c.ReceivedOn.Year == d.Month.Year && c.ReceivedOn.Month == d.Month.Month)} received this month; compliments: {d.Compliments}", StyleNames.Heading2);
        var openC = d.Complaints.Where(c => c.Status != ComplaintStatus.Closed).ToList();
        if (openC.Count == 0) sec.AddParagraph("No open complaints.").Format.Font.Size = 8.5;
        else Grid(sec, ["Number", "Received", "Category", "Status", "Acknowledge by", "Decide by", "Timeframe"], [3, 2.5, 4, 3, 3, 3, 6],
            openC.Select(c => new string?[] { c.Number, c.ReceivedOn.ToString("d MMM", CultureInfo.InvariantCulture), c.Category, c.Status.ToString(), c.AcknowledgedOn is null ? c.AcknowledgeBy.ToString("d MMM", CultureInfo.InvariantCulture) : "done",
                c.DecideBy?.ToString("d MMM", CultureInfo.InvariantCulture) ?? "—", d.Overdue(c) ?? "On time" }), (row, col) => col == 6 && d.Overdue(openC[row]) is not null ? RagColour("red") : null);

        sec.AddParagraph("High and extreme risks", StyleNames.Heading2);
        var hi = d.Risks.Where(r => r.Status != RiskStatus.Closed && QualityService.Score(r.Likelihood, r.Consequence) >= 10).OrderByDescending(r => r.Likelihood * r.Consequence).ToList();
        if (hi.Count == 0) sec.AddParagraph("None.").Format.Font.Size = 8.5;
        else Grid(sec, ["Number", "Risk", "Owner", "Inherent", "Residual", "Review by"], [2.5, 9, 4, 3, 3, 3],
            hi.Select(r => new string?[] { r.Number, r.Title, r.Owner, $"{QualityService.Score(r.Likelihood, r.Consequence)} {QualityService.Band(QualityService.Score(r.Likelihood, r.Consequence))}",
                r.ResidualLikelihood is { } rl && r.ResidualConsequence is { } rc ? $"{rl * rc} {QualityService.Band(rl * rc)}" : "—", r.ReviewBy.ToString("d MMM yyyy", CultureInfo.InvariantCulture) }));

        sec.AddParagraph("Corrective actions overdue", StyleNames.Heading2);
        var over = d.Actions.Where(a => a.Status == ActionStatus.Open && a.DueOn < d.Today).ToList();
        if (over.Count == 0) sec.AddParagraph("None overdue.").Format.Font.Size = 8.5;
        else Grid(sec, ["Number", "From", "Action", "Owner", "Due"], [2.5, 3.5, 11, 4, 3], over.Select(a => new string?[] { a.Number, $"{a.Source} {a.SourceRef}", a.Title, a.Owner, a.DueOn.ToString("d MMM", CultureInfo.InvariantCulture) }));

        if (d.PoliciesDue.Count > 0)
        {
            sec.AddParagraph("Policies due for review", StyleNames.Heading2);
            Grid(sec, ["Code", "Title", "Version", "Owner", "Review by"], [3, 11, 2.5, 4, 3], d.PoliciesDue.Select(p => new string?[] { p.Code, p.Title, p.Version, p.Owner, p.ReviewBy.ToString("d MMM yyyy", CultureInfo.InvariantCulture) }));
        }
        return PdfReports.Render(doc, password);
    }

    private static string Spaced(string s) => System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
}
