using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Pdf;

namespace BrightPAS.Web.Tests;

public class PdfAndOcrTests
{
    private static readonly LetterheadData Head = new("Tasman Day Surgery", "1 Hospital Road, Nelson", "03 000 0000", "info@example.nz");
    private static readonly LetterData Letter = new("L000123", "Dr Mere Smith", "Stoke Medical Centre\nNelson", "Re: Aroha NGĀTAI", "Dear Doctor\n\nYour patient had a laparoscopic cholecystectomy today.\n\nKind regards", null, false, "Dr Ben Hall");

    [Fact]
    public void Protected_letter_pdf_is_aes256_and_needs_the_password()
    {
        var bytes = PdfReports.Letter(Head, Letter, "Tasman-Letter-26");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.ThrowsAny<Exception>(() => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import));
        using var opened = PdfReader.Open(new MemoryStream(bytes), "Tasman-Letter-26", PdfDocumentOpenMode.Import);
        Assert.Equal(1, opened.PageCount);
        var raw = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.Contains("/AESV3", raw); // AES-256 crypt filter (PDF security handler V5)
        Assert.DoesNotContain("cholecystectomy", raw);
    }

    [Fact]
    public void Table_and_transfer_pack_render_without_password_when_allowed()
    {
        var table = PdfReports.Table("Waitlist", "2 entries", [("Patient", 6), ("Procedure", 8)], [["NGĀTAI, Aroha", "Hernia"], ["SMITH, Jo", "Colonoscopy"]], null);
        using var t = PdfReader.Open(new MemoryStream(table), PdfDocumentOpenMode.Import);
        Assert.True(t.PageCount >= 1);
        var pack = new Dictionary<string, object?>
        {
            ["from"] = new { organisation = "TDS" }, ["patient"] = new { familyName = "Ngātai", givenNames = "Aroha", dateOfBirth = "1980-05-17", sex = "Female", nhi = "ZSC21TN", mrn = "TDS1", address = "", mobilePhone = "" },
            ["alerts"] = new[] { new { type = "Allergy", severity = "Critical", description = "Penicillin" } }, ["bookings"] = Array.Empty<object>(), ["preAdmission"] = null, ["labs"] = Array.Empty<object>(), ["gp"] = null,
        };
        Assert.True(PdfReports.TransferPack(pack, null, "Transfer-Pack-26").Length > 1000);
    }

    [Fact]
    public async Task Pdf_text_layer_is_read_without_ocr()
    {
        var bytes = PdfReports.Letter(Head, Letter, null);
        var x = new DocumentTextExtractor(new ThrowingOcr(), NullLogger<DocumentTextExtractor>.Instance);
        var (text, method, problem) = await x.ExtractAsync("application/pdf", bytes, default);
        Assert.Equal("text", method);
        Assert.Null(problem);
        Assert.Contains("cholecystectomy", text);
    }

    [Fact]
    public void Ccitt_g4_is_wrapped_as_tiff()
    {
        var tiff = DocumentTextExtractor.TiffG4([1, 2, 3], 100, 50, false);
        Assert.Equal((byte)'I', tiff[0]);
        Assert.Equal(42, BitConverter.ToInt16(tiff, 2));
        Assert.Equal(3, tiff[^1]);
    }

    /// <summary>Real Tesseract run (Windows natives ship with the package; on Linux the system libtesseract is linked in if installed – otherwise skipped).</summary>
    [Fact]
    public async Task Scanned_image_and_image_only_pdf_are_ocrd()
    {
        if (!PrepareNatives()) return;
        var png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "ocr-sample.png"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ocr:TessdataPath"] = Path.Combine(AppContext.BaseDirectory, "tessdata") }).Build();
        using var engine = new TesseractOcrEngine(config);
        var x = new DocumentTextExtractor(engine, NullLogger<DocumentTextExtractor>.Instance);
        var (text, method, problem) = await x.ExtractAsync("image/png", png, default);
        Assert.True(problem is null, problem);
        Assert.Equal("ocr", method);
        Assert.Contains("penicillin", text, StringComparison.OrdinalIgnoreCase);
        // The same image inside a PDF with no text layer (like a scanner's output).
        var doc = new PdfDocument();
        var page = doc.AddPage();
        using (var g = XGraphics.FromPdfPage(page)) using (var img = XImage.FromStream(new MemoryStream(png))) g.DrawImage(img, 20, 20, 560, 160);
        using var ms = new MemoryStream();
        doc.Save(ms);
        var (t2, m2, p2) = await x.ExtractAsync("application/pdf", ms.ToArray(), default);
        Assert.True(p2 is null, p2);
        Assert.Equal("ocr", m2);
        Assert.Contains("cholecystectomy", t2, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PrepareNatives()
    {
        if (OperatingSystem.IsWindows()) return true;
        var dir = Path.Combine(AppContext.BaseDirectory, "x64");
        Directory.CreateDirectory(dir);
        (string Target, string Name)[] links = [("/usr/lib/x86_64-linux-gnu/libtesseract.so.5", "libtesseract50.so"), ("/usr/lib/x86_64-linux-gnu/liblept.so.5", "libleptonica-1.82.0.so"), ("/usr/lib/x86_64-linux-gnu/libdl.so.2", "../libdl.so")];
        foreach (var (target, name) in links)
        {
            if (!File.Exists(target)) return false;
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) File.CreateSymbolicLink(path, target);
        }
        return true;
    }

    private sealed class ThrowingOcr : IOcrEngine
    {
        public string? Problem => null;
        public string Recognise(byte[] image) => throw new InvalidOperationException("OCR should not be needed for a PDF with text.");
    }
}
