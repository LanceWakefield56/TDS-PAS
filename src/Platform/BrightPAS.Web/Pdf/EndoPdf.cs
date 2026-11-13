using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using BrightPAS.Modules.Endoscopy;
using static BrightPAS.Modules.Endoscopy.Terminology;

namespace BrightPAS.Web.Pdf;

public sealed record EndoPicture(byte[] Bytes, string Caption, bool InReport, bool InPatientReport, int? PolypNumber);

public sealed record EndoReportData(Procedure P, Guid BookingId, Guid PatientId, string Accession, string KindLabel, DateOnly Date,
    string Organisation, string? Address, string? Phone, string PatientName, string FirstName, string? Nhi, string Mrn, DateOnly DateOfBirth, string Sex,
    IReadOnlyList<string> Indications, IReadOnlyList<Finding> Findings, IReadOnlyList<Polyp> Polyps, IReadOnlyList<Tattoo> Tattoos, IReadOnlyList<Intervention> Therapy,
    IReadOnlyList<EndoPicture> Pictures, IReadOnlyList<string> Scopes, IReadOnlyList<string> Medicines, IReadOnlyDictionary<string, (string Title, string Body)> Advice,
    string? Suggestion, bool Signed);

/// <summary>Endoscopy reports with pictures: the clinical report (for the record, GP and referrer) and a plain-language patient report.</summary>
public static class EndoPdf
{
    private static string Yn(bool? b) => b is null ? "—" : b.Value ? "Yes" : "No";
    private static string T(DateTimeOffset? t) => t?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
    private static string Enum(string? s) => s is null ? "—" : System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z0-9])", " ");

    private static string SedationLabel(SedationKind s) => s switch
    {
        SedationKind.None => "None", SedationKind.ThroatSpray => "Throat spray only", SedationKind.Conscious => "Conscious sedation",
        SedationKind.Propofol => "Propofol (anaesthetist)", _ => "General anaesthetic",
    };

    private static void Letterhead(Section sec, EndoReportData d, string title)
    {
        var head = sec.AddParagraph();
        head.Format.Font.Size = 14; head.Format.Font.Bold = true; head.AddText(d.Organisation);
        var sub = sec.AddParagraph(string.Join(" · ", new[] { d.Address, d.Phone }.Where(x => !string.IsNullOrWhiteSpace(x))));
        sub.Format.Font.Size = 8; sub.Format.Font.Color = Colors.DimGray; sub.Format.SpaceAfter = 8;
        sub.Format.Borders.Bottom.Width = 0.5; sub.Format.Borders.Bottom.Color = Colors.Gray;
        var h = sec.AddParagraph(title, StyleNames.Heading1);
        h.Format.SpaceBefore = 6;
        if (!d.Signed)
        {
            var w = sec.AddParagraph("DRAFT – NOT YET SIGNED BY THE ENDOSCOPIST");
            w.Format.Font.Bold = true; w.Format.Font.Color = Colors.DarkRed; w.Format.SpaceAfter = 6;
        }
    }

    private static Table KeyValues(Section sec, IEnumerable<(string K, string? V)> rows, double keyCm = 4.5)
    {
        var t = sec.AddTable();
        t.Borders.Width = 0; t.Format.Font.Size = 9;
        t.AddColumn(Unit.FromCentimeter(keyCm)); t.AddColumn(Unit.FromCentimeter(17 - keyCm));
        foreach (var (k, v) in rows.Where(r => !string.IsNullOrWhiteSpace(r.V)))
        {
            var r = t.AddRow();
            r.Cells[0].AddParagraph(k).Format.Font.Bold = true;
            r.Cells[1].AddParagraph(v!);
        }
        return t;
    }

    private static Table Grid(Section sec, string[] headers, double[] widths, IEnumerable<string?[]> rows)
    {
        var t = sec.AddTable();
        t.Borders.Width = 0.25; t.Borders.Color = Colors.Gray; t.Format.Font.Size = 8.5;
        foreach (var w in widths) t.AddColumn(Unit.FromCentimeter(w));
        var hr = t.AddRow(); hr.HeadingFormat = true; hr.Shading.Color = Colors.WhiteSmoke;
        for (var i = 0; i < headers.Length; i++) hr.Cells[i].AddParagraph(headers[i]).Format.Font.Bold = true;
        foreach (var row in rows)
        {
            var r = t.AddRow();
            for (var i = 0; i < row.Length; i++) r.Cells[i].AddParagraph(row[i] ?? "");
        }
        return t;
    }

    private static void Pictures(Section sec, IReadOnlyList<EndoPicture> pics, int perRow, double widthCm, bool numbered)
    {
        if (pics.Count == 0) return;
        var t = sec.AddTable();
        t.Borders.Width = 0;
        for (var i = 0; i < perRow; i++) t.AddColumn(Unit.FromCentimeter(17.0 / perRow));
        Row? row = null;
        for (var i = 0; i < pics.Count; i++)
        {
            if (i % perRow == 0) { row = t.AddRow(); row.KeepWith = 0; }
            var cell = row!.Cells[i % perRow];
            var img = cell.AddParagraph().AddImage("base64:" + Convert.ToBase64String(pics[i].Bytes));
            img.Width = Unit.FromCentimeter(widthCm); img.LockAspectRatio = true;
            var cap = cell.AddParagraph((numbered ? $"{i + 1}. " : "") + pics[i].Caption);
            cap.Format.Font.Size = 8; cap.Format.SpaceAfter = 6;
        }
    }

    public static byte[] ClinicalReport(EndoReportData d, string? password)
    {
        var p = d.P;
        var doc = PdfReports.NewDocument($"Endoscopy report {d.Accession}");
        var sec = doc.LastSection;
        Letterhead(sec, d, $"{d.KindLabel} report");
        KeyValues(sec, [
            ("Patient", $"{d.PatientName}   NHI {d.Nhi ?? "—"}   {d.Mrn}   born {d.DateOfBirth:d MMM yyyy}   {d.Sex}"),
            ("Date", $"{d.Date:dddd d MMMM yyyy}   Accession {d.Accession}"),
            ("Endoscopist", p.EndoscopistName), ("Assisting", p.Assistants),
            ("Indication", string.Join("; ", d.Indications) + (p.NbspScreening ? $"   (National Bowel Screening Programme{(p.NbspNumber is null ? "" : " " + p.NbspNumber)})" : "")),
            ("Sedation", SedationLabel(p.Sedation) + (d.Medicines.Count > 0 ? " – " + string.Join(", ", d.Medicines) : "") + (p.SedationNotes is null ? "" : $" ({p.SedationNotes})")),
            ("Equipment", string.Join("; ", d.Scopes)),
            ("Consent", p.ConsentConfirmed ? "Confirmed" : "Not recorded"),
        ]);
        if (HasUpper(p.Kind))
        {
            sec.AddParagraph("Gastroscopy", StyleNames.Heading2);
            KeyValues(sec, [
                ("Extent", Enum(p.UpperExtent?.ToString())), ("Times", $"{T(p.UpperStartAt)} – {T(p.UpperEndAt)}" + (EndoService.UpperMinutes(p) is { } um ? $"  ({um:0.0} min)" : "")),
                ("Retroflexion", Yn(p.Retroflexion)), ("Photo-documentation", Yn(p.UpperPhotoDocumentation)),
            ]);
        }
        if (HasLower(p.Kind))
        {
            sec.AddParagraph(p.Kind == ProcedureKind.FlexibleSigmoidoscopy ? "Flexible sigmoidoscopy" : "Colonoscopy", StyleNames.Heading2);
            var landmarks = string.Join(", ", new[] { p.AppendicealOrifice == true ? "appendiceal orifice" : null, p.IleocaecalValve == true ? "ileocaecal valve" : null,
                p.TerminalIleum == true ? "terminal ileum intubated" : null, p.CaecumPhotographed == true ? "photographed" : null }.Where(x => x is not null));
            KeyValues(sec, [
                ("Extent", Enum(p.LowerExtent?.ToString()) + (p.IncompleteReason is null ? "" : $" – incomplete: {p.IncompleteReason}")),
                ("Landmarks", landmarks.Length > 0 ? landmarks : null),
                ("Bowel preparation", EndoService.BbpsTotal(p) is { } b ? $"BBPS {b} (right {p.BbpsRight}, transverse {p.BbpsTransverse}, left {p.BbpsLeft}) – {(EndoService.AdequatePrep(p) ? "adequate" : "inadequate")}" : null),
                ("Times", IsColonoscopy(p.Kind) ? $"in {T(p.InsertedAt)}, caecum {T(p.CaecumAt)}, withdrawal from {T(p.WithdrawalStartAt ?? p.CaecumAt)}, out {T(p.EndedAt)}" : $"in {T(p.InsertedAt)}, out {T(p.EndedAt)}"),
                ("Withdrawal time", IsColonoscopy(p.Kind) && EndoService.WithdrawalMinutes(p) is { } w ? $"{w:0.0} minutes" : null),
                ("Rectal retroflexion", Yn(p.RectalRetroflexion)),
                ("Technique", string.Join(", ", new[] { p.Co2 ? "CO₂" : "air", p.WaterAssisted ? "water-assisted" : null, p.ScopeGuide ? "ScopeGuide" : null, p.PositionChange ? "position change" : null, p.AbdominalPressure ? "abdominal pressure" : null }.Where(x => x is not null))),
            ]);
        }
        if (p.Comfort is { } c) KeyValues(sec, [("Comfort (Gloucester)", $"{c} – {c switch { 1 => "comfortable", 2 => "minimal discomfort", 3 => "mild discomfort", 4 => "moderate discomfort", _ => "severe discomfort" }}")]);

        sec.AddParagraph("Findings", StyleNames.Heading2);
        if (d.Findings.Count == 0 && d.Polyps.Count == 0) sec.AddParagraph("No abnormality recorded.");
        foreach (var f in d.Findings)
            sec.AddParagraph($"{f.Number}. {SiteLabel(f.Site)}: {FindingLabel(f.Kind)}{(f.Grade is null ? "" : $" ({f.Grade})")}{(f.SizeMm is null ? "" : $", {f.SizeMm} mm")}{(f.Description is null ? "" : $" – {f.Description}")}").Format.Font.Size = 9;
        if (d.Polyps.Count > 0)
        {
            sec.AddParagraph("Polyps", StyleNames.Heading2);
            Grid(sec, ["#", "Site", "cm", "mm", "Paris", "NICE", "Removal", "Retrieved", "Pot", "Complete", "Histology"], [0.6, 2.4, 0.8, 0.8, 1.2, 1.0, 2.6, 1.5, 1.2, 1.5, 3.4],
                d.Polyps.Select(x => new[] { x.Number.ToString(CultureInfo.InvariantCulture), SiteLabel(x.Site), x.DistanceCm?.ToString(CultureInfo.InvariantCulture), x.SizeMm.ToString(CultureInfo.InvariantCulture), x.Paris, x.Nice,
                    Label(Techniques, x.Technique) + (x.Resection == "Piecemeal" ? ", piecemeal" : "") + (x.Clips > 0 ? $", {x.Clips} clip(s)" : "") + (x.Tattooed ? ", tattooed" : ""),
                    x.Retrieved, x.Pot, x.Complete, x.Histology is null ? "Awaited" : Label(Histologies, x.Histology) + (x.Dysplasia is { } dy && dy != "None" ? $", {dy.ToLowerInvariant()}-grade dysplasia" : "") }));
        }
        if (d.Tattoos.Count > 0)
        {
            sec.AddParagraph("Tattoos", StyleNames.Heading2);
            foreach (var t in d.Tattoos)
                sec.AddParagraph($"{SiteLabel(t.Site)}{(t.DistanceCm is null ? "" : $" at {t.DistanceCm} cm")}: {t.Ink}{(t.VolumeMl is null ? "" : $" {t.VolumeMl} mL")}{(t.Injections is null ? "" : $", {t.Injections} injection(s)")}, {t.Position.ToLowerInvariant()} to the lesion{(t.PolypId is { } pid && d.Polyps.FirstOrDefault(x => x.Id == pid) is { } lp ? $" (polyp {lp.Number})" : "")}{(t.Reason is null ? "" : $" – {t.Reason}")}").Format.Font.Size = 9;
        }
        if (d.Therapy.Count > 0)
        {
            sec.AddParagraph("Biopsies and therapy", StyleNames.Heading2);
            Grid(sec, ["What", "Site", "No.", "Pot", "Purpose / details", "Result"], [3, 3, 1, 1.5, 6, 2.5],
                d.Therapy.Select(x => new[] { Label(InterventionKinds, x.Kind), SiteLabel(x.Site), x.Count?.ToString(CultureInfo.InvariantCulture), x.Pot, string.Join(" – ", new[] { x.Purpose, x.Details }.Where(v => v is not null)), x.Result }));
        }
        sec.AddParagraph("Complications", StyleNames.Heading2);
        sec.AddParagraph(p.Complication == "None" ? "None." : $"{p.Complication}: {p.ComplicationDetails}");
        sec.AddParagraph("Conclusion", StyleNames.Heading2);
        sec.AddParagraph(p.Diagnosis ?? "—");
        if (p.Recommendations is not null) { sec.AddParagraph("Recommendations", StyleNames.Heading2); sec.AddParagraph(p.Recommendations); }
        if (p.FollowUp is not null || p.SurveillanceMonths is not null)
        {
            sec.AddParagraph("Follow-up", StyleNames.Heading2);
            sec.AddParagraph(string.Join(" ", new[] { p.FollowUp, p.SurveillanceMonths is { } sm ? $"Next colonoscopy in {(sm % 12 == 0 ? $"{sm / 12} year(s)" : $"{sm} months")}." : null }.Where(x => x is not null)));
        }
        var pics = d.Pictures.Where(x => x.InReport).ToList();
        if (pics.Count > 0) { sec.AddParagraph("Images", StyleNames.Heading2); Pictures(sec, pics, 3, 5.4, true); }
        var sig = sec.AddParagraph(d.Signed ? $"Electronically signed by {p.SignedBy} on {p.SignedAt?.ToLocalTime():d MMM yyyy HH:mm}" : "Not signed.");
        sig.Format.SpaceBefore = 14; sig.Format.Font.Italic = true;
        return PdfReports.Render(doc, password);
    }

    public static byte[] PatientReport(EndoReportData d, string? password)
    {
        var p = d.P;
        var doc = PdfReports.NewDocument($"Your {d.KindLabel.ToLowerInvariant()}");
        doc.Styles[StyleNames.Normal]!.Font.Size = 11;
        var sec = doc.LastSection;
        Letterhead(sec, d, $"Your {d.KindLabel.ToLowerInvariant()}");
        var intro = sec.AddParagraph($"{d.PatientName}   NHI {d.Nhi ?? "—"}   {d.Date:d MMMM yyyy}   Doctor: {p.EndoscopistName}");
        intro.Format.Font.Size = 9; intro.Format.SpaceAfter = 8;

        void Advice(string key)
        {
            if (!d.Advice.TryGetValue(key, out var a)) return;
            sec.AddParagraph(a.Title, StyleNames.Heading2);
            sec.AddParagraph(a.Body);
        }
        if (HasUpper(p.Kind)) Advice("Gastroscopy");
        if (p.Kind == ProcedureKind.FlexibleSigmoidoscopy) Advice("FlexibleSigmoidoscopy"); else if (HasLower(p.Kind)) Advice("Colonoscopy");

        sec.AddParagraph("What we found", StyleNames.Heading2);
        var abnormal = d.Findings.Where(f => f.Kind != "Normal").ToList();
        if (abnormal.Count == 0 && d.Polyps.Count == 0) sec.AddParagraph("Everything we looked at appeared normal.");
        foreach (var f in abnormal)
        {
            var k = FindingKinds.FirstOrDefault(x => x.Code == f.Kind);
            var para = sec.AddParagraph();
            para.AddFormattedText($"{FindingLabel(f.Kind)} – {SitePatient(f.Site)}. ", TextFormat.Bold);
            para.AddText(k?.Patient is { Length: > 0 } pt ? pt : f.Description ?? "");
            para.Format.SpaceAfter = 4;
        }
        var removed = d.Polyps.Where(x => x.Technique != "NotRemoved").ToList();
        if (removed.Count > 0)
        {
            var para = sec.AddParagraph();
            para.AddFormattedText($"Polyps – {removed.Count} removed. ", TextFormat.Bold);
            para.AddText($"A polyp is a small growth on the bowel lining. Most are harmless, but some can turn into cancer over many years, so they are removed and checked in the laboratory. "
                + $"Sizes: {string.Join(", ", removed.Select(x => $"{x.SizeMm} mm ({SitePatient(x.Site)})"))}.");
            para.Format.SpaceAfter = 4;
        }
        if (d.Polyps.Any(x => x.Technique == "NotRemoved"))
            sec.AddParagraph("A polyp was seen that was not removed today. Your doctor will explain the plan.").Format.SpaceAfter = 4;

        var pics = d.Pictures.Where(x => x.InPatientReport).ToList();
        if (pics.Count > 0) { sec.AddParagraph("Pictures from your procedure", StyleNames.Heading2); Pictures(sec, pics, 2, 7.5, false); }

        if (removed.Count > 0) Advice("Polypectomy");
        if (d.Therapy.Any(x => x.Kind == "Biopsy") || removed.Any(x => x.Retrieved == "Yes")) Advice("Biopsy");
        if (d.Tattoos.Count > 0) Advice("Tattoo");

        sec.AddParagraph("What happens next", StyleNames.Heading2);
        foreach (var line in new[] { p.PatientAdvice, p.FollowUp, p.SurveillanceMonths is { } sm ? $"We recommend your next colonoscopy in about {(sm % 12 == 0 ? $"{sm / 12} year(s)" : $"{sm} months")}. You will be sent a reminder." : null }.Where(x => !string.IsNullOrWhiteSpace(x)))
            sec.AddParagraph(line!).Format.SpaceAfter = 4;
        if (p.PatientAdvice is null && p.FollowUp is null && p.SurveillanceMonths is null) sec.AddParagraph("Your doctor will send a report to your GP.");
        if (p.Sedation is SedationKind.Conscious or SedationKind.Propofol or SedationKind.GeneralAnaesthetic) Advice("Sedation");
        Advice("Diet");
        Advice("WhenToSeekHelp");
        if (d.Phone is not null) sec.AddParagraph($"{d.Organisation}: {d.Phone}").Format.Font.Bold = true;
        return PdfReports.Render(doc, password);
    }
}
