using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using BrightPAS.Modules.Prescribing;

namespace BrightPAS.Web.Pdf;

/// <summary>
/// Prescription (0.31.0, ADR 0044): prescriber and facility, patient (name, address, date of birth, NHI), date, each medicine with directions,
/// quantity and repeats, the NZePS barcode with the prescription id, and the signature line. Controlled drugs, and any prescription not sent to
/// NZePS, need the prescriber's own signature. Reprints are marked.
/// </summary>
public sealed class PrescriptionPdf : IPrescriptionPdf
{
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");
    private static string D(DateOnly d) => d.ToString("d MMM yyyy", Nz);

    public byte[] Render(PrescriptionPdfModel m)
    {
        var doc = PdfReports.NewDocument($"Prescription {m.Number}");
        var sec = doc.LastSection;
        sec.Footers.Primary.Elements.Clear();
        var footer = sec.Footers.Primary.AddParagraph($"Prescription {m.Number} · NZePS {m.NzepsId} · {m.ClinicName}");
        footer.Format.Font.Size = 7; footer.Format.Font.Color = Colors.Gray;

        var head = sec.AddTable();
        head.AddColumn(Unit.FromCentimeter(9.5)); head.AddColumn(Unit.FromCentimeter(7.5));
        var hr = head.AddRow();
        var name = hr.Cells[0].AddParagraph(m.ClinicName); name.Format.Font.Size = 13; name.Format.Font.Bold = true;
        foreach (var l in m.ClinicAddress) hr.Cells[0].AddParagraph(l);
        if (m.ClinicPhone is not null) hr.Cells[0].AddParagraph($"Phone {m.ClinicPhone}");
        if (m.FacilityHpi is not null) hr.Cells[0].AddParagraph($"HPI facility {m.FacilityHpi}");
        var title = hr.Cells[1].AddParagraph(m.Reprint ? "PRESCRIPTION – COPY" : "PRESCRIPTION");
        title.Format.Font.Size = 15; title.Format.Font.Bold = true; title.Format.Alignment = ParagraphAlignment.Right;
        foreach (var t in new[] { $"Number {m.Number}", $"Date {D(m.Date)}" }) hr.Cells[1].AddParagraph(t).Format.Alignment = ParagraphAlignment.Right;
        var bc = hr.Cells[1].AddParagraph(); bc.Format.Alignment = ParagraphAlignment.Right; bc.Format.SpaceBefore = 4;
        var img = bc.AddImage("base64:" + Convert.ToBase64String(BarcodePng.Code128(m.NzepsId)));
        img.Width = Unit.FromCentimeter(6.5); img.Height = Unit.FromCentimeter(1.2);
        var id = hr.Cells[1].AddParagraph(m.NzepsId); id.Format.Alignment = ParagraphAlignment.Right; id.Format.Font.Size = 9;

        var who = sec.AddTable();
        who.Borders.Width = 0.25; who.Borders.Color = Colors.Gray; who.TopPadding = 3; who.BottomPadding = 3;
        who.AddColumn(Unit.FromCentimeter(8.5)); who.AddColumn(Unit.FromCentimeter(8.5));
        var wr = who.AddRow();
        var pt = wr.Cells[0].AddParagraph(); pt.AddFormattedText("Patient", TextFormat.Bold); pt.AddLineBreak(); pt.AddText(m.Patient);
        foreach (var l in m.PatientAddress) { pt.AddLineBreak(); pt.AddText(l); }
        pt.AddLineBreak(); pt.AddText($"Date of birth {D(m.DateOfBirth)}"); if (m.Nhi is not null) { pt.AddLineBreak(); pt.AddText($"NHI {m.Nhi}"); }
        var pr = wr.Cells[1].AddParagraph(); pr.AddFormattedText("Prescriber", TextFormat.Bold); pr.AddLineBreak(); pr.AddText(m.Prescriber);
        pr.AddLineBreak(); pr.AddText(m.Scope); pr.AddLineBreak(); pr.AddText($"HPI CPN {m.HpiCpn}");
        if (m.Registration is not null) { pr.AddLineBreak(); pr.AddText($"Registration {m.Registration}"); }
        who.Format.SpaceAfter = 6;

        sec.AddParagraph().Format.SpaceAfter = 4;
        var t2 = sec.AddTable();
        t2.Borders.Width = 0.25; t2.Borders.Color = Colors.Gray; t2.TopPadding = 3; t2.BottomPadding = 3;
        foreach (var w in new[] { 5.5, 7.0, 2.5, 2.0 }) t2.AddColumn(Unit.FromCentimeter(w));
        var h = t2.AddRow(); h.HeadingFormat = true; h.Shading.Color = Colors.WhiteSmoke;
        string[] heads = ["Medicine", "Directions", "Supply", "Repeats"];
        for (var i = 0; i < heads.Length; i++) h.Cells[i].AddParagraph(heads[i]).Format.Font.Bold = true;
        foreach (var i in m.Items)
        {
            var r = t2.AddRow();
            var med = r.Cells[0].AddParagraph(i.Medicine); med.Format.Font.Bold = true;
            if (i.Controlled) r.Cells[0].AddParagraph("Controlled drug").Format.Font.Size = 8;
            r.Cells[1].AddParagraph(i.Directions);
            r.Cells[2].AddParagraph($"{i.Quantity.ToString("0.##", Nz)} {i.Unit}");
            r.Cells[3].AddParagraph(i.Repeats.ToString(Nz));
        }
        if (m.Note is not null) sec.AddParagraph($"Note: {m.Note}").Format.SpaceBefore = 6;

        var sign = sec.AddParagraph();
        sign.Format.SpaceBefore = Unit.FromCentimeter(1.2);
        if (m.NeedsSignature)
        {
            sign.AddText("Signature ______________________________     Date ______________");
            sec.AddParagraph(m.Sent ? "Contains a controlled drug – the prescriber's own signature is needed." : "Not sent electronically – the prescriber's own signature is needed.").Format.Font.Size = 8;
        }
        else sign.AddText($"Signed electronically by {m.Prescriber} and sent to the NZ ePrescription Service. Any pharmacy can dispense by scanning the barcode.");
        var hint = sec.AddParagraph("Take this to any pharmacy. Keep your medicines out of reach of children.");
        hint.Format.SpaceBefore = 8; hint.Format.Font.Size = 8; hint.Format.Font.Color = Colors.Gray;
        return PdfReports.Render(doc, null);
    }
}
