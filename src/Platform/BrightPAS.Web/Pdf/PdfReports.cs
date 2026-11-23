using BrightPAS.Core;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Security;

namespace BrightPAS.Web.Pdf;

/// <summary>Liberation Sans (SIL OFL 1.1, embedded) for every PDF, so output is identical on Windows Server and in tests.</summary>
public sealed class EmbeddedFontResolver : IFontResolver
{
    public const string Family = "Liberation Sans";
    private static readonly Assembly Asm = typeof(EmbeddedFontResolver).Assembly;

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch { (true, true) => "LS-BoldItalic", (true, false) => "LS-Bold", (false, true) => "LS-Italic", _ => "LS-Regular" });

    public byte[] GetFont(string faceName)
    {
        var file = faceName.Replace("LS-", "LiberationSans-") + ".ttf";
        var name = Asm.GetManifestResourceNames().Single(n => n.EndsWith(file, StringComparison.Ordinal));
        using var s = Asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

public sealed record LetterheadData(string Organisation, string? Address, string? Phone, string? Email);
public sealed record LetterData(string Number, string RecipientName, string? RecipientAddress, string Subject, string Body, DateTimeOffset? Date, bool IsDraft, string? Author);

/// <summary>
/// PDF letters and reports. With a password the PDF is AES-256 encrypted (PDF 2.0 "V5" standard security): it opens in Edge, Adobe Reader
/// or any modern PDF reader after typing the password – no extra software. Editing is not permitted; printing is.
/// </summary>
public static class PdfReports
{
    private static readonly object Gate = new();
    private static bool _fontsReady;

    private static void EnsureFonts()
    {
        lock (Gate)
        {
            if (_fontsReady) return;
            GlobalFontSettings.FontResolver ??= new EmbeddedFontResolver();
            _fontsReady = true;
        }
    }

    /// <summary>Registers the embedded fonts (for PDFs that build their own layout, e.g. the printed estimate).</summary>
    internal static void EnsureFontsReady() => EnsureFonts();

    internal static Document NewDocument(string title, bool landscape = false)
    {
        EnsureFonts();
        var d = new Document();
        d.Info.Title = title;
        d.Info.Author = "BrightPAS";
        var normal = d.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.Family;
        normal.Font.Size = 10;
        var h = d.Styles[StyleNames.Heading1]!;
        h.Font.Size = 14; h.Font.Bold = true; h.ParagraphFormat.SpaceAfter = 6;
        var h2 = d.Styles[StyleNames.Heading2]!;
        h2.Font.Size = 11; h2.Font.Bold = true; h2.ParagraphFormat.SpaceBefore = 10; h2.ParagraphFormat.SpaceAfter = 3;
        var sec = d.AddSection();
        sec.PageSetup = d.DefaultPageSetup.Clone();
        sec.PageSetup.PageFormat = PageFormat.A4;
        sec.PageSetup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        sec.PageSetup.TopMargin = Unit.FromCentimeter(2); sec.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        sec.PageSetup.LeftMargin = Unit.FromCentimeter(2); sec.PageSetup.RightMargin = Unit.FromCentimeter(2);
        var footer = sec.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7; footer.Format.Font.Color = Colors.Gray;
        footer.AddText($"{title} · BrightPAS {DateTimeOffset.UtcNow.ToNz():d MMM yyyy HH:mm} · Contains health information – handle under the Health Information Privacy Code · Page ");
        footer.AddPageField(); footer.AddText(" of "); footer.AddNumPagesField();
        return d;
    }

    /// <summary>Renders and (when a password is given) encrypts with AES-256; the owner password is random and never stored.</summary>
    public static byte[] Render(Document d, string? password)
    {
        var r = new PdfDocumentRenderer { Document = d };
        r.RenderDocument();
        var pdf = r.PdfDocument;
        if (!string.IsNullOrEmpty(password)) Protect(pdf, password);
        using var ms = new MemoryStream();
        pdf.Save(ms, false);
        return ms.ToArray();
    }

    public static void Protect(PdfDocument pdf, string password)
    {
        var s = pdf.SecuritySettings;
        s.UserPassword = password;
        s.OwnerPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        s.PermitModifyDocument = false; s.PermitAssembleDocument = false; s.PermitAnnotations = false; s.PermitFormsFill = false;
        s.PermitPrint = true; s.PermitFullQualityPrint = true; s.PermitExtractContent = true;
        pdf.SecurityHandler.SetEncryption(PdfDefaultEncryption.V5); // AES-256
    }

    public static byte[] Letter(LetterheadData lh, LetterData l, string? password)
    {
        var d = NewDocument($"Letter {l.Number}");
        var sec = d.LastSection;
        var head = sec.AddParagraph();
        head.Format.Font.Size = 14; head.Format.Font.Bold = true; head.AddText(lh.Organisation);
        var sub = sec.AddParagraph(string.Join(" · ", new[] { lh.Address, lh.Phone, lh.Email }.Where(x => !string.IsNullOrWhiteSpace(x))));
        sub.Format.Font.Size = 8; sub.Format.Font.Color = Colors.DimGray; sub.Format.SpaceAfter = 18;
        sub.Format.Borders.Bottom.Width = 0.5; sub.Format.Borders.Bottom.Color = Colors.Gray;
        if (l.IsDraft)
        {
            var w = sec.AddParagraph("DRAFT – NOT SENT");
            w.Format.Font.Bold = true; w.Format.Font.Color = Colors.DarkRed; w.Format.SpaceAfter = 6;
        }
        sec.AddParagraph((l.Date ?? DateTimeOffset.UtcNow).ToNz().ToString("d MMMM yyyy")).Format.SpaceAfter = 12;
        var to = sec.AddParagraph();
        to.AddText(l.RecipientName);
        foreach (var line in (l.RecipientAddress ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) { to.AddLineBreak(); to.AddText(line); }
        to.Format.SpaceAfter = 12;
        var subj = sec.AddParagraph(l.Subject);
        subj.Format.Font.Bold = true; subj.Format.SpaceAfter = 10;
        foreach (var para in l.Body.Replace("\r", "").Split("\n\n"))
        {
            var p = sec.AddParagraph();
            var lines = para.Split('\n');
            for (var i = 0; i < lines.Length; i++) { if (i > 0) p.AddLineBreak(); p.AddText(lines[i]); }
            p.Format.SpaceAfter = 8;
        }
        if (!string.IsNullOrWhiteSpace(l.Author)) sec.AddParagraph(l.Author).Format.SpaceBefore = 12;
        var reff = sec.AddParagraph($"Ref {l.Number}");
        reff.Format.Font.Size = 7; reff.Format.Font.Color = Colors.Gray; reff.Format.SpaceBefore = 18;
        return Render(d, password);
    }

    /// <summary>A titled table report (waitlist, theatre lists…).</summary>
    public static byte[] Table(string title, string? subtitle, IReadOnlyList<(string Header, double WidthCm)> columns, IEnumerable<string?[]> rows, string? password, bool landscape = true,
        IReadOnlyList<(string Heading, IEnumerable<string?[]> Rows)>? groups = null)
    {
        var d = NewDocument(title, landscape);
        var sec = d.LastSection;
        sec.AddParagraph(title, StyleNames.Heading1);
        if (!string.IsNullOrWhiteSpace(subtitle)) sec.AddParagraph(subtitle).Format.SpaceAfter = 8;
        void AddTable(IEnumerable<string?[]> data)
        {
            var t = sec.AddTable();
            t.Borders.Width = 0.25; t.Borders.Color = Colors.LightGray; t.Format.Font.Size = 8;
            foreach (var (_, w) in columns) t.AddColumn(Unit.FromCentimeter(w));
            var hr = t.AddRow();
            hr.HeadingFormat = true; hr.Format.Font.Bold = true; hr.Shading.Color = Colors.WhiteSmoke;
            for (var i = 0; i < columns.Count; i++) hr.Cells[i].AddParagraph(columns[i].Header);
            var n = 0;
            foreach (var row in data)
            {
                var r = t.AddRow();
                for (var i = 0; i < columns.Count && i < row.Length; i++) r.Cells[i].AddParagraph(row[i] ?? "");
                n++;
            }
            if (n == 0) { var r = t.AddRow(); r.Cells[0].MergeRight = columns.Count - 1; r.Cells[0].AddParagraph("None."); }
        }
        if (groups is null) AddTable(rows);
        else foreach (var (heading, data) in groups) { sec.AddParagraph(heading, StyleNames.Heading2); AddTable(data); }
        return Render(d, password);
    }

    /// <summary>The emergency transfer pack (same content as the printable page).</summary>
    public static byte[] TransferPack(Dictionary<string, object?> pack, BrightPAS.Modules.Patients.TransferEdit? t, string? password)
    {
        var json = JsonSerializer.SerializeToElement(pack, JsonSerializerOptions.Web);
        static string V(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
        var p = json.GetProperty("patient");
        var d = NewDocument("Emergency transfer pack");
        var sec = d.LastSection;
        sec.AddParagraph($"Emergency transfer pack – {V(json.GetProperty("from"), "organisation")}", StyleNames.Heading1);
        var id = sec.AddParagraph();
        id.AddFormattedText($"{V(p, "familyName").ToUpperInvariant()}, {V(p, "givenNames")}", TextFormat.Bold);
        id.AddText($"  ·  DOB {V(p, "dateOfBirth")}  ·  {V(p, "sex")}  ·  NHI {(V(p, "nhi") is { Length: > 0 } nhi ? nhi : "not recorded")}  ·  MRN {V(p, "mrn")}");
        id.AddLineBreak(); id.AddText($"{V(p, "address")}  {V(p, "mobilePhone")}");
        if (t is not null)
        {
            sec.AddParagraph("Transfer", StyleNames.Heading2);
            sec.AddParagraph($"To: {t.ReceivingFacility} · Transport: {t.TransportMode} · Escort: {t.Escort}");
            sec.AddParagraph($"Reason: {t.Reason}");
            var tb = sec.AddTable(); tb.Borders.Width = 0.25; tb.AddColumn(Unit.FromCentimeter(3.5)); tb.AddColumn(Unit.FromCentimeter(13.5));
            foreach (var (k, v) in new[] { ("Situation", t.Situation), ("Background", t.Background), ("Assessment", t.Assessment), ("Recommendation", t.Recommendation) })
            { var r = tb.AddRow(); r.Cells[0].AddParagraph(k).Format.Font.Bold = true; r.Cells[1].AddParagraph(v ?? ""); }
        }
        sec.AddParagraph("Alerts and allergies", StyleNames.Heading2);
        var alerts = json.GetProperty("alerts").EnumerateArray().ToList();
        if (alerts.Count == 0) sec.AddParagraph("No alerts recorded – confirm with the patient.");
        foreach (var a in alerts) { var ap = sec.AddParagraph($"{V(a, "type")} ({V(a, "severity")}): {V(a, "description")}"); ap.Format.Font.Bold = true; ap.Format.Font.Color = Colors.DarkRed; }
        sec.AddParagraph("Procedure", StyleNames.Heading2);
        var bt = sec.AddTable(); bt.Borders.Width = 0.25; bt.Format.Font.Size = 8;
        foreach (var w in new[] { 2.2, 5.0, 3.0, 3.0, 2.0, 1.8 }) bt.AddColumn(Unit.FromCentimeter(w));
        var bh = bt.AddRow(); bh.Format.Font.Bold = true;
        string[] heads = ["Date", "Procedure", "Surgeon", "Anaesthetist", "Anaesthetic", "In / out"];
        for (var i = 0; i < heads.Length; i++) bh.Cells[i].AddParagraph(heads[i]);
        foreach (var b in json.GetProperty("bookings").EnumerateArray())
        {
            var r = bt.AddRow();
            string[] cells = [V(b, "date"), $"{V(b, "procedure")} {(V(b, "laterality") == "NotApplicable" ? "" : V(b, "laterality"))}", V(b, "surgeon"), V(b, "anaesthetist"), V(b, "anaesthetic"), $"{V(b, "theatreIn")}–{V(b, "theatreOut")}"];
            for (var i = 0; i < cells.Length; i++) r.Cells[i].AddParagraph(cells[i]);
        }
        var pre = json.GetProperty("preAdmission");
        if (pre.ValueKind == JsonValueKind.Object)
        {
            sec.AddParagraph("Pre-admission questionnaire", StyleNames.Heading2);
            sec.AddParagraph($"Triage: {V(pre, "triage")} {V(pre, "triageOutcome")}");
            foreach (var f in pre.GetProperty("flags").EnumerateArray()) sec.AddParagraph($"• {V(f, "level")}: {V(f, "text")}");
        }
        sec.AddParagraph("Recent lab results", StyleNames.Heading2);
        var labs = json.GetProperty("labs").EnumerateArray().ToList();
        if (labs.Count == 0) sec.AddParagraph("None.");
        foreach (var l in labs) sec.AddParagraph($"{V(l, "testName")} – {V(l, "reportedAt")}{(V(l, "isAbnormal") == "True" ? " – ABNORMAL" : "")} {V(l, "comment")}");
        var gp = json.GetProperty("gp");
        sec.AddParagraph("GP", StyleNames.Heading2);
        sec.AddParagraph(gp.ValueKind == JsonValueKind.Object ? $"{V(gp, "name")} {V(gp, "phone")}" : "Not recorded");
        return Render(d, password);
    }
}
