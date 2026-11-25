using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace BrightPAS.Modules.Medicines;

/// <summary>Reads the text lines of a PDF with their position and type (PdfPig, Apache-2.0) – the input to <see cref="NzfParser"/>.</summary>
public static class NzfPdf
{
    public const long MaxBytes = 120L * 1024 * 1024;

    public static List<NzfLine> ReadLines(byte[] pdf, CancellationToken ct = default)
    {
        var lines = new List<NzfLine>();
        using var doc = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        for (var n = 1; n <= doc.NumberOfPages; n++)
        {
            ct.ThrowIfCancellationRequested();
            Page page;
            try { page = doc.GetPage(n); }
            catch (InvalidOperationException) { continue; } // an unreadable page is skipped, not fatal
            var words = page.GetWords().Where(w => w.Letters.Count > 0 && !string.IsNullOrWhiteSpace(w.Text))
                .OrderByDescending(w => Math.Round(w.BoundingBox.Bottom, 1)).ThenBy(w => w.BoundingBox.Left).ToList();
            var group = new List<Word>();
            double bottom = double.NaN;
            foreach (var w in words)
            {
                if (group.Count > 0 && Math.Abs(w.BoundingBox.Bottom - bottom) > 1.8) { lines.Add(Line(n, group)); group.Clear(); }
                if (group.Count == 0) bottom = w.BoundingBox.Bottom;
                group.Add(w);
            }
            if (group.Count > 0) lines.Add(Line(n, group));
        }
        return lines;
    }

    private static NzfLine Line(int page, List<Word> words)
    {
        var ws = words.OrderBy(w => w.BoundingBox.Left).ToList();
        var sb = new StringBuilder();
        for (var i = 0; i < ws.Count; i++)
        {
            if (i > 0)
            {
                var gap = ws[i].BoundingBox.Left - ws[i - 1].BoundingBox.Right;
                var size = ws[i].Letters[0].PointSize;
                sb.Append(gap > size * 2.5 ? "   " : " ");
            }
            sb.Append(ws[i].Text);
        }
        var first = ws[0].Letters[0];
        var font = first.FontName ?? "";
        return new NzfLine(page, Math.Round(ws[0].BoundingBox.Left, 1), Math.Round(first.PointSize, 1), font.Contains("Bold", StringComparison.OrdinalIgnoreCase), sb.ToString());
    }
}
