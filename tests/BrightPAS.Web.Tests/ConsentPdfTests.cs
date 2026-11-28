using BrightPAS.Modules.Consent;
using BrightPAS.Web.Pdf;
using PdfSharp.Pdf.IO;

namespace BrightPAS.Web.Tests;

/// <summary>0.29.0 the signed consent PDF renders (online with a drawn signature, paper, unsigned).</summary>
public class ConsentPdfTests
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static ConsentPdfModel Model(string method, byte[]? signature, DateTimeOffset? signed, DateTimeOffset? confirmed)
    {
        var sheet = new SheetSnapshot("LAPCHOLE", 2, "Laparoscopic cholecystectomy", "About\nSecond line", "Benefits", "Alternatives", "Without it", "GA", null, "Recovery",
            [new("Bile leak", "Uncommon (1 in 100 to 1 in 1,000)", true), new("Pain", "Common (more than 1 in 100)", false)], [new("Video", "https://example.nz/v")], "drhall");
        return new("CN000001", "Aroha Ngātai", "ZSC21TN", new DateOnly(1980, 5, 17), "Laparoscopic cholecystectomy", "", "Dr Ben Hall", sheet, ["Warfarin – bleeding"],
            [("Can I drive?", "Not for 24 hours.")], method, "Patient", "Aroha Ngātai", null, "Te reo Māori – Hemi Walker", signature, signed, confirmed is null ? null : "nurse", confirmed, null, "Tasman Day Surgery");
    }

    [Fact]
    public void Consent_pdfs_render()
    {
        var pdf = new ConsentPdf();
        var t = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(13));
        foreach (var m in new[] { Model("Online", Png, t, t.AddDays(3)), Model("Paper", null, t, null), Model("Online", null, null, null) })
        {
            var bytes = pdf.Render(m);
            Assert.True(bytes.Length > 1000);
            using var doc = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);
            Assert.True(doc.PageCount >= 1);
        }
    }
}
