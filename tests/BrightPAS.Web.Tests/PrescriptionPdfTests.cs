using BrightPAS.Modules.Prescribing;
using BrightPAS.Web.Pdf;
using PdfSharp.Pdf.IO;

namespace BrightPAS.Web.Tests;

/// <summary>0.31.0 prescription PDF with its NZePS barcode, and the barcode PNG itself.</summary>
public class PrescriptionPdfTests
{
    [Fact]
    public void Prescription_renders_with_barcode()
    {
        var png = BarcodePng.Code128("7A1E2D4F6C8B0A3E4K");
        Assert.Equal([137, 80, 78, 71], png[..4]);
        var id = NzepsId.New();
        var m = new PrescriptionPdfModel("Tasman Day Surgery", ["1 Example Street", "Nelson 7010"], "03 555 0100", "F12345-A", "RX000001", id, new DateOnly(2026, 12, 1),
            "Aroha Ngātai", new DateOnly(1980, 5, 17), "ZSC21TN", ["2 Patient Road", "Nelson 7011"], "Dr Kate Ford", "12ABCD", "12345", "Medical practitioner",
            [new(Guid.NewGuid(), null, "Paracetamol 500 mg tablet", null, "Take 2 tablets every 6 hours when needed for pain. Max 8 in 24 hours.", 32, "tablet", 0, false),
             new(Guid.NewGuid(), null, "Oxycodone 5 mg capsule", null, "Take 1 capsule every 4 hours when needed for severe pain.", 10, "capsule", 0, true)],
            true, true, false, "Post-op pain relief");
        var pdf = new PrescriptionPdf().Render(m);
        using var doc = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        Assert.Equal(1, doc.PageCount);
    }
}
