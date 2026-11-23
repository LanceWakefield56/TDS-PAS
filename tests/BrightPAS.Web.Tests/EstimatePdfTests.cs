using System.Text;
using BrightPAS.Modules.Estimates;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Pdf;
using PdfSharp.Pdf.IO;

namespace BrightPAS.Web.Tests;

/// <summary>0.21.0 the printed estimate (same layout as TDS Estimates v1.5) and the Outlook draft.</summary>
public class EstimatePdfTests
{
    private static Estimate Sample(EstimateStatus status = EstimateStatus.Issued) => new()
    {
        Ref = "EST-261109-ZZZ0016",
        EstimateDate = new DateOnly(2026, 11, 9),
        ValidUntil = new DateOnly(2027, 2, 9),
        Status = status,
        FirstName = "Aroha",
        LastName = "Ngātai",
        DateOfBirth = new DateOnly(1970, 1, 1),
        Nhi = "ZZZ0016",
        Email = "aroha@example.nz",
        Phone = "021 555 0101",
        Address = "1 Test Street",
        Town = "Richmond",
        Postcode = "7020",
        Funding = Funding.SelfFunding,
        Title = "Estimate – Colonoscopy with Sedation by an Anaesthetist",
        SurgeonName = "Dr Ana Lee",
        ProcedureDate = new DateOnly(2026, 12, 9),
        Subtotal = 5040,
        Deposit = 2520,
        Balance = 2520,
        PreparedBy = "Giorgia Leonard",
        Notes = "Bowel preparation instructions are enclosed.",
        Lines =
        [
            new() { Kind = LineKind.Fee, Sequence = 0, Label = "Facility Fee", Amount = 2600 },
            new() { Kind = LineKind.Fee, Sequence = 1, Label = "Consultant Fee", Amount = 1250 },
            new() { Kind = LineKind.Fee, Sequence = 2, Label = "Add-on – Biopsies", Amount = 290 },
            new() { Kind = LineKind.Fee, Sequence = 3, Label = "Anaesthetist Fee", Amount = 900 },
            new() { Kind = LineKind.IfRequired, Sequence = 4, Label = "Polypectomy", Amount = 820, Note = "Surgeon fee – only if required" },
        ]
    };

    [Fact]
    public void Renders_the_estimate_and_encrypts_with_the_password()
    {
        var pdf = new EstimatePdf();
        var plain = pdf.Render(Sample(), new EstSettings(), null);
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(plain, 0, 4), StringComparison.Ordinal);
        Assert.True(plain.Length > 50_000, "logo and footer images embedded");
        using (var doc = PdfReader.Open(new MemoryStream(plain), PdfDocumentOpenMode.Import)) Assert.Equal(1, doc.PageCount);

        var enc = pdf.Render(Sample(), new EstSettings(), "010170");
        Assert.Contains("/Encrypt", Encoding.ASCII.GetString(enc), StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => PdfReader.Open(new MemoryStream(enc), "wrong", PdfDocumentOpenMode.Import));
        using var opened = PdfReader.Open(new MemoryStream(enc), "010170", PdfDocumentOpenMode.Import);
        Assert.Equal(1, opened.PageCount);
    }

    [Fact]
    public void Draft_and_cancelled_estimates_render_and_long_estimates_flow_onto_a_second_page()
    {
        var pdf = new EstimatePdf();
        var e = Sample(EstimateStatus.Draft);
        for (var i = 0; i < 40; i++) e.Lines.Add(new() { Kind = LineKind.IfRequired, Sequence = 10 + i, Label = $"Extra item {i}", Amount = 10 });
        using var doc = PdfReader.Open(new MemoryStream(pdf.Render(e, new EstSettings(), null)), PdfDocumentOpenMode.Import);
        Assert.True(doc.PageCount >= 2);
        Assert.NotEmpty(pdf.Render(Sample(EstimateStatus.Cancelled), new EstSettings(), null));
    }

    [Fact]
    public void Outlook_draft_is_marked_unsent_with_the_pdf_attached()
    {
        var mailer = new EstimateMailer(null!, null!);
        var eml = Encoding.UTF8.GetString(mailer.Draft(new OutgoingEmail("enquiries@tasmandaysurgery.co.nz", "Tasman Day Surgery", "aroha@example.nz", null,
            "Your estimate from Tasman Day Surgery – EST-261109-ZZZ0016", "Dear Aroha,\n\nPlease find attached your estimate.", "Estimate_EST-261109-ZZZ0016.pdf", "%PDF-1.7 test"u8.ToArray())));
        Assert.Contains("X-Unsent: 1", eml, StringComparison.Ordinal);
        Assert.Contains("aroha@example.nz", eml, StringComparison.Ordinal);
        Assert.Contains("Estimate_EST-261109-ZZZ0016.pdf", eml, StringComparison.Ordinal);
        Assert.Contains("application/pdf", eml, StringComparison.Ordinal);
        Assert.Contains("Please find attached your estimate.", eml.Replace("=\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal); // quoted-printable keeps the body readable
    }
}
