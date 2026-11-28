using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using BrightPAS.Core;
using BrightPAS.Modules.Payments;

namespace BrightPAS.Web.Pdf;

/// <summary>Patient statement (0.30.0, ADR 0043): clinic details, the patient's postal address (fits a DL window envelope), the invoices still owing,
/// the total and overdue amounts, and how to pay (online link when printed for posting, bank account, phone). Each statement is its own section with
/// its own page numbers, so a posting batch can be split by page.</summary>
public sealed class StatementPdf : IStatementPdf
{
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");
    private static string D(DateOnly? d) => d is { } v ? v.ToString("d MMM yyyy", Nz) : "—";
    private static string M(decimal v) => v.ToString("$#,##0.00", Nz);

    public byte[] Render(IReadOnlyList<StatementPdfModel> statements)
    {
        var doc = PdfReports.NewDocument(statements.Count == 1 ? $"Statement {statements[0].Number}" : "Statements");
        var first = true;
        foreach (var m in statements)
        {
            Section sec;
            if (first) { sec = doc.LastSection; sec.Footers.Primary.Elements.Clear(); first = false; }
            else { sec = doc.AddSection(); sec.PageSetup = doc.Sections[0].PageSetup.Clone(); }
            sec.PageSetup.StartingNumber = 1;
            var footer = sec.Footers.Primary.AddParagraph();
            footer.Format.Font.Size = 7; footer.Format.Font.Color = Colors.Gray;
            footer.AddText($"Statement {m.Number} · {m.ClinicName} · Page ");
            footer.AddPageField(); footer.AddText(" of "); footer.AddSectionPagesField();
            Write(sec, m);
        }
        return PdfReports.Render(doc, null);
    }

    private static void Write(Section sec, StatementPdfModel m)
    {
        var head = sec.AddTable();
        head.AddColumn(Unit.FromCentimeter(10)); head.AddColumn(Unit.FromCentimeter(7));
        var hr = head.AddRow();
        var name = hr.Cells[0].AddParagraph(m.ClinicName);
        name.Format.Font.Size = 14; name.Format.Font.Bold = true;
        foreach (var l in m.ClinicAddress) hr.Cells[0].AddParagraph(l);
        if (m.ClinicPhone is not null) hr.Cells[0].AddParagraph($"Phone {m.ClinicPhone}");
        if (m.ClinicEmail is not null) hr.Cells[0].AddParagraph(m.ClinicEmail);
        if (m.GstNumber is not null) hr.Cells[0].AddParagraph($"GST number {m.GstNumber}");
        var title = hr.Cells[1].AddParagraph("STATEMENT");
        title.Format.Font.Size = 16; title.Format.Font.Bold = true; title.Format.Alignment = ParagraphAlignment.Right;
        foreach (var t in new[] { $"Number {m.Number}", $"Date {D(m.StatementDate)}" })
            hr.Cells[1].AddParagraph(t).Format.Alignment = ParagraphAlignment.Right;

        // Address block positioned for a DL window envelope (about 4.5 cm from the top of the page).
        var to = sec.AddParagraph();
        to.Format.SpaceBefore = Unit.FromCentimeter(1); to.Format.SpaceAfter = Unit.FromCentimeter(1);
        to.AddFormattedText(m.PatientName, TextFormat.Bold);
        foreach (var l in m.PatientAddress) { to.AddLineBreak(); to.AddText(l); }
        if (m.Nhi is not null) { to.AddLineBreak(); to.AddText($"NHI {m.Nhi}"); }

        var t2 = sec.AddTable();
        t2.Borders.Width = 0.25; t2.Borders.Color = Colors.Gray;
        foreach (var w in new[] { 3.0, 2.8, 2.8, 2.8, 2.8, 2.8 }) t2.AddColumn(Unit.FromCentimeter(w));
        var h = t2.AddRow(); h.HeadingFormat = true; h.Shading.Color = Colors.WhiteSmoke;
        string[] heads = ["Invoice", "Date", "Due", "Amount", "Paid", "Owing"];
        for (var i = 0; i < heads.Length; i++) { var p = h.Cells[i].AddParagraph(heads[i]); p.Format.Font.Bold = true; if (i >= 3) p.Format.Alignment = ParagraphAlignment.Right; }
        foreach (var l in m.Lines)
        {
            var r = t2.AddRow();
            r.Cells[0].AddParagraph(l.Number);
            r.Cells[1].AddParagraph(D(l.IssuedOn));
            var due = r.Cells[2].AddParagraph(D(l.DueOn));
            if (l.DueOn < m.StatementDate) due.Format.Font.Color = Colors.DarkRed;
            r.Cells[3].AddParagraph(M(l.Total)).Format.Alignment = ParagraphAlignment.Right;
            r.Cells[4].AddParagraph(M(l.Paid)).Format.Alignment = ParagraphAlignment.Right;
            r.Cells[5].AddParagraph(M(l.Balance)).Format.Alignment = ParagraphAlignment.Right;
        }
        void Total(string label, decimal v, bool bold)
        {
            var r = t2.AddRow();
            r.Cells[0].MergeRight = 4;
            var a = r.Cells[0].AddParagraph(label); a.Format.Alignment = ParagraphAlignment.Right; a.Format.Font.Bold = bold;
            var b = r.Cells[5].AddParagraph(M(v)); b.Format.Alignment = ParagraphAlignment.Right; b.Format.Font.Bold = bold;
        }
        Total("Total owing", m.TotalDue, true);
        if (m.Overdue > 0) Total("of which overdue", m.Overdue, false);

        sec.AddParagraph("How to pay", StyleNames.Heading2);
        if (m.PayUrl is not null)
        {
            var p = sec.AddParagraph();
            p.AddFormattedText("Online by card: ", TextFormat.Bold);
            p.AddText(m.PayUrl);
            sec.AddParagraph("You will need your date of birth to open the link. Please keep it private.").Format.Font.Size = 9;
        }
        if (m.BankAccount is not null)
        {
            var p = sec.AddParagraph();
            p.AddFormattedText("Bank transfer: ", TextFormat.Bold);
            p.AddText($"{m.BankAccount} – please use reference {m.Number} or your invoice number.");
        }
        if (m.ClinicPhone is not null) sec.AddParagraph($"By phone or at reception (EFTPOS or credit card): {m.ClinicPhone}.");
        if (m.Overdue > 0) sec.AddParagraph("Some of this account is overdue. If you are having trouble paying, please phone us – we can arrange a payment plan.").Format.SpaceBefore = 6;
        if (m.Message is not null) sec.AddParagraph(m.Message).Format.SpaceBefore = 6;
        var thanks = sec.AddParagraph("If you have paid in the last few days, thank you – please ignore this statement.");
        thanks.Format.SpaceBefore = 6; thanks.Format.Font.Size = 9; thanks.Format.Font.Color = Colors.Gray;
    }
}
