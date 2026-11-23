using System.Globalization;
using System.Reflection;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using BrightPAS.Modules.Estimates;

namespace BrightPAS.Web.Pdf;

/// <summary>
/// The printed estimate – the same layout as TDS Estimates v1.5 and the specialty workbooks: logo, date / reference / valid until, patient details,
/// title with the anaesthetic, fee lines (surgeon base fee, then each required add-on, then the anaesthetist), subtotal, "Additional costs IF
/// required", notes, terms, the payment box (deposit for self-funding) and sign-off, with the TDS footer. Liberation Sans is metric-compatible
/// with Arial, so line breaks match. With a password the PDF is AES-256 encrypted.
/// </summary>
public sealed class EstimatePdf : IEstimatePdf
{
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");
    private static readonly Lazy<string?> Logo = new(() => Image("logo.png"));
    private static readonly Lazy<string?> Footer = new(() => Image("footer.png"));

    private static string? Image(string name)
    {
        var asm = typeof(EstimatePdf).Assembly;
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("Estimates." + name, StringComparison.Ordinal));
        if (res is null) return null;
        using var s = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return "base64:" + Convert.ToBase64String(ms.ToArray());
    }

    private static string Money(decimal v) => v.ToString("$#,##0.00", Nz);
    private static string Dt(DateOnly? d) => d?.ToString("dd.MM.yyyy", Nz) ?? "";

    public byte[] Render(Estimate e, EstSettings s, string? password)
    {
        PdfReports.EnsureFontsReady();
        var navy = Color.FromRgb(0x1F, 0x4E, 0x79);
        var teal = Color.FromRgb(0x3A, 0x9F, 0xB0);
        var doc = new Document();
        doc.Info.Title = e.Title ?? "Estimate";
        doc.Info.Author = "Tasman Day Surgery";
        var st = doc.Styles[StyleNames.Normal]!;
        st.Font.Name = EmbeddedFontResolver.Family; st.Font.Size = 9.5;
        var sec = doc.AddSection();
        sec.PageSetup = doc.DefaultPageSetup.Clone();
        sec.PageSetup.PageFormat = PageFormat.A4;
        sec.PageSetup.LeftMargin = sec.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        sec.PageSetup.TopMargin = Unit.FromCentimeter(1.2);
        sec.PageSetup.BottomMargin = Unit.FromCentimeter(3.2);
        sec.PageSetup.FooterDistance = Unit.FromCentimeter(0.6);
        var width = Unit.FromCentimeter(21 - 3.6);

        if (Logo.Value is { } logo) { var img = sec.AddImage(logo); img.Width = Unit.FromCentimeter(6.5); img.LockAspectRatio = true; }
        if (Footer.Value is { } footer) { var fi = sec.Footers.Primary.AddImage(footer); fi.Width = width; fi.LockAspectRatio = true; }

        var t = sec.AddTable(); t.Borders.Visible = false;
        t.AddColumn(width * 0.24); t.AddColumn(width * 0.46); t.AddColumn(width * 0.30);
        var hr = t.AddRow(); hr.TopPadding = 8;
        hr.Cells[0].AddParagraph($"Date: {Dt(e.EstimateDate)}");
        hr.Cells[1].AddParagraph($"Estimate ref: {e.Ref}").Format.Alignment = ParagraphAlignment.Center;
        hr.Cells[2].AddParagraph($"Valid until: {Dt(e.ValidUntil)}").Format.Alignment = ParagraphAlignment.Right;
        if (e.Status != EstimateStatus.Issued)
        {
            var d = sec.AddParagraph(e.Status == EstimateStatus.Cancelled ? "CANCELLED – NOT VALID" : "DRAFT – NOT ISSUED");
            d.Format.Font.Bold = true; d.Format.Font.Color = Colors.DarkRed; d.Format.SpaceBefore = 4;
        }

        Heading(sec, "PATIENT DETAILS", navy);
        var pt = sec.AddTable(); pt.AddColumn(width * 0.42); pt.AddColumn(width * 0.33); pt.AddColumn(width * 0.25);
        var r1 = pt.AddRow();
        r1.Cells[0].AddParagraph($"Name: {e.FirstName} {e.LastName}");
        r1.Cells[1].AddParagraph($"Date of birth: {Dt(e.DateOfBirth)}");
        r1.Cells[2].AddParagraph($"NHI: {e.Nhi}");
        var r2 = pt.AddRow();
        r2.Cells[0].AddParagraph($"Phone: {e.Phone}");
        r2.Cells[1].MergeRight = 1; r2.Cells[1].AddParagraph($"Email: {e.Email}");
        var r3 = pt.AddRow(); r3.Cells[0].MergeRight = 2;
        r3.Cells[0].AddParagraph($"Postal address: {string.Join(", ", new[] { e.Address, $"{e.Town} {e.Postcode}".Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)))}");
        var r4 = pt.AddRow(); r4.Cells[0].MergeRight = 2;
        var fund = e.Funding == Funding.Insurance
            ? $"Funding: Insurance – {e.Insurer}{(string.IsNullOrWhiteSpace(e.PolicyNumber) ? "" : $" (policy {e.PolicyNumber})")}{(string.IsNullOrWhiteSpace(e.PriorApproval) ? "" : $" · Prior approval {e.PriorApproval}")}"
            : $"Funding: {(e.Funding == Funding.SelfFunding ? "Self-funding" : "Not yet known")}";
        r4.Cells[0].AddParagraph(fund);

        var title = sec.AddParagraph(e.Title ?? "");
        title.Format.Font.Size = 12.5; title.Format.Font.Bold = true; title.Format.Font.Color = navy; title.Format.SpaceBefore = 12;
        var sub = sec.AddParagraph($"Surgeon: {e.SurgeonName ?? "–"}   ·   Proposed date: {(e.ProcedureDate is null ? "to be confirmed" : Dt(e.ProcedureDate))}");
        sub.Format.SpaceAfter = 6; sub.Format.Font.Color = Colors.DimGray;

        var ft = sec.AddTable();
        ft.AddColumn(width * 0.62); ft.AddColumn(width * 0.18); ft.AddColumn(width * 0.20);
        foreach (var l in e.Lines.Where(l => l.Kind == LineKind.Fee).OrderBy(l => l.Sequence))
        {
            var row = ft.AddRow(); row.TopPadding = 2; row.BottomPadding = 2;
            row.Cells[0].AddParagraph(l.Label);
            row.Cells[1].AddParagraph(l.DisplayText ?? Money(l.Amount ?? 0)).Format.Alignment = ParagraphAlignment.Right;
            row.Borders.Bottom.Color = Colors.LightGray; row.Borders.Bottom.Width = 0.5;
        }
        var sr = ft.AddRow(); sr.TopPadding = 4;
        sr.Cells[0].AddParagraph("SUBTOTAL").Format.Font.Bold = true;
        var sa = sr.Cells[1].AddParagraph(Money(e.Subtotal)); sa.Format.Alignment = ParagraphAlignment.Right; sa.Format.Font.Bold = true;
        sr.Borders.Top.Width = 1; sr.Borders.Top.Color = navy;
        var nr = ft.AddRow(); nr.Cells[0].AddParagraph("Please note: This fee is an estimate only. GST is included.").Format.Font.Italic = true; nr.Cells[0].MergeRight = 2;

        var add = e.Lines.Where(l => l.Kind == LineKind.IfRequired).OrderBy(l => l.Sequence).ToList();
        if (add.Count > 0)
        {
            var ah = sec.AddParagraph("Additional costs IF required, below:"); ah.Format.Font.Bold = true; ah.Format.SpaceBefore = 8; ah.Format.SpaceAfter = 2;
            var at = sec.AddTable(); at.AddColumn(width * 0.62); at.AddColumn(width * 0.18); at.AddColumn(width * 0.20);
            foreach (var l in add)
            {
                var row = at.AddRow();
                row.Cells[0].AddParagraph(l.Label);
                row.Cells[1].AddParagraph(Money(l.Amount ?? 0)).Format.Alignment = ParagraphAlignment.Right;
                var n = row.Cells[2].AddParagraph(l.Note ?? ""); n.Format.Font.Size = 8; n.Format.Font.Color = Colors.DimGray; n.Format.LeftIndent = 6;
            }
        }

        if (!string.IsNullOrWhiteSpace(e.Notes))
        {
            var nh = sec.AddParagraph("Notes:"); nh.Format.Font.Bold = true; nh.Format.SpaceBefore = 10;
            var nt = sec.AddTable(); nt.AddColumn(width); nt.Borders.Width = 0.5; nt.Borders.Color = Colors.LightGray;
            var nr0 = nt.AddRow(); nr0.Height = Unit.FromCentimeter(2.6); nr0.HeightRule = RowHeightRule.AtLeast;
            nr0.TopPadding = 4; nr0.BottomPadding = 4; nr0.Cells[0].AddParagraph(e.Notes);
        }

        sec.AddParagraph().Format.SpaceBefore = 10;
        foreach (var line in s.Terms.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = sec.AddParagraph("•  " + EstimateMail.Fill(line, e, s)); p.Format.Font.Size = 8.5; p.Format.SpaceAfter = 3; p.Format.LeftIndent = 8; p.Format.FirstLineIndent = -8;
        }

        var pay = sec.AddTable(); pay.Format.Font.Size = 8.5; pay.AddColumn(width); pay.Borders.Visible = true; pay.Borders.Width = 0.75; pay.Borders.Color = teal;
        var prow = pay.AddRow(); prow.TopPadding = 4; prow.BottomPadding = 4; prow.Cells[0].Shading.Color = Color.FromRgb(0xF2, 0xF8, 0xFA);
        var c = prow.Cells[0];
        c.AddParagraph(EstimateMail.Fill(s.SelfFundingText, e, s)).Format.SpaceAfter = 3;
        if (e.Funding == Funding.SelfFunding)
        {
            var dp = c.AddParagraph($"Deposit required: {Money(e.Deposit)}{(e.ProcedureDate is null ? "" : $" by {Dt(e.ProcedureDate.Value.AddDays(-s.DepositDaysBefore))}")}        Balance: {Money(e.Balance)}");
            dp.Format.Font.Bold = true; dp.Format.SpaceAfter = 3;
        }
        c.AddParagraph(EstimateMail.Fill(s.InsuranceText, e, s)).Format.SpaceAfter = 3;
        c.AddParagraph($"Account: Tasman Day Surgery {s.BankAccount}").Format.Font.Bold = true;
        c.AddParagraph($"Reference: {e.LastName}, {Surname(e.SurgeonName)}, {(e.ProcedureDate is null ? "surgery date" : Dt(e.ProcedureDate))}");
        c.AddParagraph(EstimateMail.Fill(s.AcceptanceText, e, s)).Format.SpaceBefore = 3;

        sec.AddParagraph("Kind Regards").Format.SpaceBefore = 10;
        sec.AddParagraph(e.PreparedBy ?? "").Format.Font.Bold = true;
        sec.AddParagraph("Tasman Day Surgery");

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        if (!string.IsNullOrEmpty(password)) PdfReports.Protect(renderer.PdfDocument, password);
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    private static void Heading(Section sec, string text, Color c)
    {
        var p = sec.AddParagraph(text);
        p.Format.Font.Bold = true; p.Format.Font.Color = c; p.Format.SpaceBefore = 10; p.Format.SpaceAfter = 3;
        p.Format.Borders.Bottom.Width = 0.75; p.Format.Borders.Bottom.Color = c;
    }

    private static string Surname(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "your surgeon’s surname";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts[^1];
    }
}
