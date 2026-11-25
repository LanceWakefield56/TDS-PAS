using System.Globalization;
using MigraDoc.DocumentObjectModel;
using BrightPAS.Core;
using BrightPAS.Modules.Consent;

namespace BrightPAS.Web.Pdf;

/// <summary>The signed consent (0.29.0, ADR 0042): patient and procedure, the information sheet as the patient read it, their questions and answers,
/// the signature (drawn image when given, typed name always), who signed and any interpreter, and the clinician's confirmation on the day.</summary>
public sealed class ConsentPdf : IConsentPdf
{
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");
    private static string When(DateTimeOffset? t) => t is { } v ? v.ToNz().ToString("d MMMM yyyy, h:mm tt", Nz) : "—";

    public byte[] Render(ConsentPdfModel m)
    {
        var doc = PdfReports.NewDocument($"Consent {m.Number}");
        var sec = doc.LastSection;
        var head = sec.AddParagraph(m.ClinicName);
        head.Format.Font.Size = 14; head.Format.Font.Bold = true;
        sec.AddParagraph($"Consent to treatment – {m.Number}", StyleNames.Heading1);
        var status = m.ConfirmedAt is not null ? "Signed and confirmed on the day" : m.SignedAt is not null ? "Signed – to be confirmed with the patient on the day" : "NOT SIGNED";
        var sp = sec.AddParagraph(status);
        sp.Format.Font.Bold = true; sp.Format.SpaceAfter = 6;
        if (m.SignedAt is null) sp.Format.Font.Color = Colors.DarkRed;

        var t = sec.AddTable();
        t.Borders.Width = 0.25; t.Borders.Color = Colors.Gray;
        t.AddColumn(Unit.FromCentimeter(4.5)); t.AddColumn(Unit.FromCentimeter(12.5));
        void Row(string k, string? v)
        {
            var r = t.AddRow();
            r.Cells[0].AddParagraph(k).Format.Font.Bold = true;
            r.Cells[1].AddParagraph(string.IsNullOrWhiteSpace(v) ? "—" : v);
        }
        Row("Patient", m.Patient);
        Row("NHI", m.Nhi);
        Row("Date of birth", m.DateOfBirth.ToString("d MMMM yyyy", Nz));
        Row("Procedure", m.Side.Length > 0 ? $"{m.Procedure} – {m.Side}" : m.Procedure);
        Row("Surgeon / proceduralist", m.Clinician);
        Row("Information sheet", $"{m.Sheet.Name} (version {m.Sheet.Version.ToString(CultureInfo.InvariantCulture)}{(m.Sheet.ApprovedBy is null ? "" : $", approved by {m.Sheet.ApprovedBy}")})");

        void Section(string title, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            sec.AddParagraph(title, StyleNames.Heading2);
            foreach (var line in text.Split('\n')) sec.AddParagraph(line.TrimEnd());
        }
        Section("What the procedure is", m.Sheet.About);
        Section("Benefits", m.Sheet.Benefits);
        sec.AddParagraph("Risks", StyleNames.Heading2);
        foreach (var r in m.Sheet.Risks)
        {
            var p = sec.AddParagraph();
            p.Format.LeftIndent = Unit.FromCentimeter(0.4);
            p.AddText("• ");
            if (r.Serious) p.AddFormattedText("Serious: ", TextFormat.Bold);
            p.AddText($"{r.Text} – {r.Frequency}");
        }
        foreach (var x in m.ExtraRisks)
        {
            var p = sec.AddParagraph();
            p.Format.LeftIndent = Unit.FromCentimeter(0.4);
            p.AddText("• ");
            p.AddFormattedText("Applies to this patient: ", TextFormat.Bold);
            p.AddText(x);
        }
        Section("Alternatives", m.Sheet.Alternatives);
        Section("If the patient does not have the procedure", m.Sheet.NoTreatment);
        Section("Anaesthetic", m.Sheet.Anaesthetic);
        Section("Getting ready", m.Sheet.Preparation);
        Section("After the procedure", m.Sheet.Recovery);
        if (m.Sheet.Links.Count > 0)
        {
            sec.AddParagraph("Videos and web pages offered", StyleNames.Heading2);
            foreach (var l in m.Sheet.Links) sec.AddParagraph($"• {l.Title} – {l.Url}");
        }
        if (m.Questions.Count > 0)
        {
            sec.AddParagraph("Questions asked", StyleNames.Heading2);
            foreach (var (q, a) in m.Questions)
            {
                var p = sec.AddParagraph(); p.AddFormattedText("Q: ", TextFormat.Bold); p.AddText(q);
                var pa = sec.AddParagraph(); pa.AddFormattedText("A: ", TextFormat.Bold); pa.AddText(a ?? "(not answered)");
                pa.Format.SpaceAfter = 4;
            }
        }

        sec.AddParagraph("Consent", StyleNames.Heading2);
        if (m.SignedAt is not null)
        {
            if (m.Method == nameof(ConsentMethod.Online))
            {
                sec.AddParagraph("The person signing confirmed online that: they have read the information about the procedure, including the risks, benefits and alternatives, and understand it; " +
                                 "they had the chance to ask questions and are satisfied with the answers; and they agree to the procedure described above. They can withdraw consent at any time before the procedure.");
            }
            else sec.AddParagraph("Signed on a paper consent form; the scanned form is filed in the patient record.");
            var st = sec.AddTable();
            st.Borders.Width = 0.25; st.Borders.Color = Colors.Gray;
            st.AddColumn(Unit.FromCentimeter(4.5)); st.AddColumn(Unit.FromCentimeter(12.5));
            void SRow(string k, string? v) { var r = st.AddRow(); r.Cells[0].AddParagraph(k).Format.Font.Bold = true; r.Cells[1].AddParagraph(string.IsNullOrWhiteSpace(v) ? "—" : v); }
            SRow("Signed by", m.Signer == nameof(SignerKind.Patient) ? $"{m.SignerName} (the patient)" : $"{m.SignerName} ({m.Relationship})");
            if (m.Signature is { Length: > 0 } img)
            {
                var r = st.AddRow();
                r.Cells[0].AddParagraph("Signature").Format.Font.Bold = true;
                var pic = r.Cells[1].AddImage("base64:" + Convert.ToBase64String(img));
                pic.Height = Unit.FromCentimeter(2); pic.LockAspectRatio = true;
            }
            SRow("Signed", When(m.SignedAt));
            SRow("Interpreter", m.Interpreter ?? "None");
            SRow("Confirmed on the day by", m.ConfirmedBy is null ? "Not yet" : $"{m.ConfirmedBy}, {When(m.ConfirmedAt)}");
            if (m.ConfirmNote is not null) SRow("Note", m.ConfirmNote);
        }
        else sec.AddParagraph("Not signed yet.");
        return PdfReports.Render(doc, null);
    }
}
