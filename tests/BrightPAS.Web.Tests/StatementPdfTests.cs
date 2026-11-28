using BrightPAS.Modules.Payments;
using BrightPAS.Web.Pdf;
using PdfSharp.Pdf.IO;

namespace BrightPAS.Web.Tests;

/// <summary>0.30.0 statements render: one for download, and a posting batch with each statement on its own pages.</summary>
public class StatementPdfTests
{
    private static StatementPdfModel Model(string number, string? payUrl, int lines) =>
        new("Tasman Day Surgery", ["1 Example Street", "Nelson 7010"], "03 555 0100", "accounts@example.co.nz", "123-456-789", "12-3456-7890123-00",
            number, new DateOnly(2026, 12, 1), "Aroha Ngātai", ["2 Patient Road", "Stoke", "Nelson 7011"], "ZSC21TN",
            [.. Enumerable.Range(1, lines).Select(i => new StatementLine(Guid.NewGuid(), $"INV-{i:D6}", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 21), 230m, 30m, 200m))],
            200m * lines, 200m * lines, payUrl, "Thank you for choosing TDS.");

    [Fact]
    public void Statements_render_singly_and_as_a_posting_batch()
    {
        var pdf = new StatementPdf();
        var one = pdf.Render([Model("ST000001", null, 2)]);
        using (var doc = PdfReader.Open(new MemoryStream(one), PdfDocumentOpenMode.Import)) Assert.Equal(1, doc.PageCount);
        var batch = pdf.Render([Model("ST000002", "https://pay.example.co.nz/pay/abc", 1), Model("ST000003", "https://pay.example.co.nz/pay/def", 40)]);
        using var b = PdfReader.Open(new MemoryStream(batch), PdfDocumentOpenMode.Import);
        Assert.True(b.PageCount >= 3); // the long one spills onto a second page; each statement starts a new page
    }
}
