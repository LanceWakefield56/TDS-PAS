using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.Filters;
using PdfSharp.Pdf.IO;
using BrightPAS.Modules.Patients;
using Tesseract;

namespace BrightPAS.Web.Integration;

/// <summary>Optical character recognition. Tesseract 5 (Apache 2.0) with the English model in &lt;app&gt;/tessdata.</summary>
public interface IOcrEngine
{
    /// <summary>Recognises text in an image the engine can read (JPEG, PNG, TIFF, PNM). Throws when the engine cannot start.</summary>
    string Recognise(byte[] image);
    string? Problem { get; }
}

/// <summary>
/// The engine the website uses: each image is recognised in a short-lived child process ("dotnet BrightPAS.Web.dll --ocr-worker"),
/// so a native crash in Tesseract/Leptonica (e.g. the Visual C++ Redistributable missing) can never stop the website.
/// After a failure it pauses for 30 minutes, then tries again (or straight away when an administrator presses Retry).
/// </summary>
public sealed class OutOfProcessOcrEngine(IConfiguration config, ILogger<OutOfProcessOcrEngine> log) : IOcrEngine
{
    private readonly object _gate = new();
    private DateTimeOffset _pausedUntil;
    public string? Problem { get; private set; }

    private string DataPath => config["Ocr:TessdataPath"] is { Length: > 0 } p ? p : Path.Combine(AppContext.BaseDirectory, "tessdata");

    public static string DotnetHost()
    {
        var root = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
        var exe = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(exe) ? exe : "dotnet";
    }

    public void Resume() { lock (_gate) { _pausedUntil = default; Problem = null; } }

    public string Recognise(byte[] image)
    {
        lock (_gate)
        {
            if (DateTimeOffset.UtcNow < _pausedUntil) throw new InvalidOperationException(Problem ?? "Text recognition is paused after a problem.");
            var psi = new System.Diagnostics.ProcessStartInfo(DotnetHost())
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "BrightPAS.Web.dll"));
            psi.ArgumentList.Add("--ocr-worker");
            psi.ArgumentList.Add(DataPath);
            using var p = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("The text recognition helper could not be started.");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            using (var input = p.StandardInput.BaseStream) input.Write(image);
            if (!p.WaitForExit(TimeSpan.FromMinutes(2)))
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return Fail("Text recognition took longer than 2 minutes on one page and was stopped.", pause: false);
            }
            if (p.ExitCode == 0) { Problem = null; return stdout.Result; }
            var why = stderr.Result.Trim();
            log.LogWarning("OCR helper exited with code {Code}: {Error}", p.ExitCode, why);
            return Fail(p.ExitCode == 2
                ? why
                : $"The text recognition engine stopped unexpectedly (exit code {p.ExitCode}). On the server install the Microsoft Visual C++ Redistributable 2015–2022 (x64), then press Try again. The website itself is not affected.", pause: true);
        }
    }

    private string Fail(string message, bool pause)
    {
        Problem = message;
        if (pause) _pausedUntil = DateTimeOffset.UtcNow.AddMinutes(30);
        throw new InvalidOperationException(message);
    }

    /// <summary>Child-process entry: image bytes on stdin, text on stdout. Exit 0 = ok, 2 = engine could not start (message on stderr).</summary>
    public static int RunWorker(string[] args)
    {
        try
        {
            using var ms = new MemoryStream();
            Console.OpenStandardInput().CopyTo(ms);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ocr:TessdataPath"] = args.Length > 1 ? args[1] : null }).Build();
            using var engine = new TesseractOcrEngine(config);
            var text = engine.Recognise(ms.ToArray());
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            stdout.Write(text);
            return 0;
        }
        catch (InvalidOperationException ex) { Console.Error.Write(ex.Message); return 2; }
        catch (TesseractException ex) { Console.Error.Write("The image could not be read: " + ex.Message); return 2; }
    }
}

/// <summary>Tesseract in this process (used by the child worker and tests).</summary>
public sealed class TesseractOcrEngine(IConfiguration config) : IOcrEngine, IDisposable
{
    private readonly object _gate = new();
    private TesseractEngine? _engine;
    public string? Problem { get; private set; }

    private string DataPath => config["Ocr:TessdataPath"] is { Length: > 0 } p ? p : Path.Combine(AppContext.BaseDirectory, "tessdata");

    private TesseractEngine Engine()
    {
        if (_engine is not null) return _engine;
        if (!File.Exists(Path.Combine(DataPath, "eng.traineddata")))
            throw new InvalidOperationException($"OCR language data not found in {DataPath} (eng.traineddata).");
        try { _engine = new TesseractEngine(DataPath, "eng", EngineMode.LstmOnly); Problem = null; return _engine; }
        catch (Exception ex) when (ex is DllNotFoundException or TesseractException or TypeInitializationException or System.Reflection.TargetInvocationException or BadImageFormatException)
        {
            Problem = "The OCR engine could not start. On the server install the Microsoft Visual C++ Redistributable (x64), then retry. " + ex.GetBaseException().Message;
            throw new InvalidOperationException(Problem, ex);
        }
    }

    public string Recognise(byte[] image)
    {
        lock (_gate) // TesseractEngine is not thread-safe; documents are processed one at a time
        {
            var engine = Engine();
            Pix pixLoaded;
            try { pixLoaded = Pix.LoadFromMemory(image); }
            catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException or System.Reflection.TargetInvocationException or BadImageFormatException)
            {
                Problem = "The OCR engine could not start. On the server install the Microsoft Visual C++ Redistributable (x64), then retry. " + ex.GetBaseException().Message;
                throw new InvalidOperationException(Problem, ex);
            }
            using var pix = pixLoaded;
            using var page = engine.Process(pix, PageSegMode.Auto);
            return page.GetText() ?? "";
        }
    }

    public void Dispose() => _engine?.Dispose();
}

/// <summary>
/// Text for searching: a PDF's own text layer when it has one; otherwise the scanned page images (JPEG, 8-bit grey/RGB, CCITT G4) are OCR'd.
/// Photos and image scans (JPEG/PNG/TIFF) are OCR'd directly.
/// </summary>
public sealed class DocumentTextExtractor(IOcrEngine ocr, ILogger<DocumentTextExtractor> log) : IDocumentTextExtractor
{
    public const int MaxPages = 30;

    public Task<(string? Text, string Method, string? Problem)> ExtractAsync(string contentType, byte[] content, CancellationToken ct) => Task.Run(() =>
    {
        if (contentType != "application/pdf") return Ocr([content], ct);
        PdfDocument pdf;
        try { pdf = PdfReader.Open(new MemoryStream(content), PdfDocumentOpenMode.Import); }
        catch (Exception ex) { return ((string?)null, "text", $"The PDF could not be read: {ex.Message}"); }
        var layer = new StringBuilder();
        var images = new List<byte[]>();
        foreach (var page in pdf.Pages.Cast<PdfPage>().Take(MaxPages))
        {
            ct.ThrowIfCancellationRequested();
            try { ExtractText(ContentReader.ReadContent(page), layer); layer.AppendLine(); } catch (Exception ex) { log.LogDebug(ex, "PDF text layer"); }
            images.AddRange(PageImages(page));
        }
        var text = layer.ToString();
        if (text.Count(char.IsLetter) >= 40) return ((string?)text, "text", (string?)null);
        if (images.Count == 0) return ((string?)null, "text", (string?)null);
        return Ocr(images, ct);
    }, ct);

    private (string? Text, string Method, string? Problem) Ocr(IEnumerable<byte[]> images, CancellationToken ct)
    {
        var sb = new StringBuilder();
        string? problem = null;
        foreach (var img in images)
        {
            ct.ThrowIfCancellationRequested();
            try { sb.AppendLine(ocr.Recognise(img)); }
            catch (InvalidOperationException ex) { return (null, "ocr", ex.Message); }
            catch (Exception ex) { problem = $"Some pages could not be read: {ex.GetBaseException().Message}"; }
        }
        var text = sb.ToString();
        return (!text.Any(char.IsLetterOrDigit) ? null : text, "ocr", problem);
    }

    /// <summary>Text-showing operators (Tj, TJ, ', ") from the page content; good for PDFs made by software, not perfect for every font encoding.</summary>
    private static void ExtractText(CObject obj, StringBuilder sb)
    {
        switch (obj)
        {
            case COperator op when op.OpCode.Name is "Tj" or "'" or "\"" or "TJ":
                foreach (var operand in op.Operands) ExtractText(operand, sb);
                if (op.OpCode.Name != "TJ") sb.Append(' ');
                break;
            case COperator op when op.OpCode.Name is "T*" or "Td" or "TD" or "ET":
                sb.Append(' ');
                break;
            case CArray arr:
                foreach (var item in arr) ExtractText(item, sb);
                break;
            case CString s:
                sb.Append(s.Value);
                break;
            case CNumber n when n is CInteger { Value: < -200 } or CReal { Value: < -200 }:
                sb.Append(' ');
                break;
            case CSequence seq:
                foreach (var item in seq) ExtractText(item, sb);
                break;
        }
    }

    /// <summary>Image XObjects on the page, converted to something Leptonica can read.</summary>
    private static IEnumerable<byte[]> PageImages(PdfPage page)
    {
        var xobjects = page.Resources?.Elements.GetDictionary("/XObject");
        if (xobjects is null) yield break;
        foreach (var item in xobjects.Elements.Values)
        {
            if ((item as PdfReference)?.Value is not PdfDictionary img || img.Elements.GetName("/Subtype") != "/Image") continue;
            var bytes = ImageBytes(img);
            if (bytes is not null) yield return bytes;
        }
    }

    public static byte[]? ImageBytes(PdfDictionary img)
    {
        var raw = img.Stream?.Value;
        if (raw is null || raw.Length == 0) return null;
        var filter = img.Elements["/Filter"] is PdfArray fa ? (fa.Elements.Count == 1 ? fa.Elements.GetName(0) : "") : img.Elements.GetName("/Filter");
        var width = img.Elements.GetInteger("/Width");
        var height = img.Elements.GetInteger("/Height");
        var bpc = img.Elements.GetInteger("/BitsPerComponent");
        switch (filter)
        {
            case "/DCTDecode": return raw; // JPEG as-is
            case "/CCITTFaxDecode":
                var parms = img.Elements.GetDictionary("/DecodeParms");
                var k = parms?.Elements.GetInteger("/K") ?? 0;
                var black1 = parms?.Elements.GetBoolean("/BlackIs1") ?? false;
                return k < 0 ? TiffG4(raw, width, height, black1) : null;
            case "/FlateDecode":
            case "":
                var data = filter == "/FlateDecode" ? new FlateDecode().Decode(raw) : raw;
                var cs = img.Elements.GetName("/ColorSpace");
                var channels = cs == "/DeviceRGB" ? 3 : cs == "/DeviceGray" ? 1 : 0;
                if (channels == 0 || bpc != 8 || data.Length < width * height * channels) return null;
                var header = Encoding.ASCII.GetBytes($"{(channels == 1 ? "P5" : "P6")}\n{width} {height}\n255\n");
                return [.. header, .. data.AsSpan(0, width * height * channels)];
            default: return null;
        }
    }

    /// <summary>Wraps a CCITT Group 4 stream in a minimal single-strip TIFF.</summary>
    public static byte[] TiffG4(byte[] data, int width, int height, bool blackIs1)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        const int entries = 8;
        var dataOffset = 8 + 2 + entries * 12 + 4;
        w.Write((byte)'I'); w.Write((byte)'I'); w.Write((short)42); w.Write(8);
        w.Write((short)entries);
        void Tag(short tag, short type, int count, int value) { w.Write(tag); w.Write(type); w.Write(count); w.Write(value); }
        Tag(256, 4, 1, width);              // ImageWidth
        Tag(257, 4, 1, height);             // ImageLength
        Tag(258, 3, 1, 1);                  // BitsPerSample
        Tag(259, 3, 1, 4);                  // Compression = CCITT G4
        Tag(262, 3, 1, blackIs1 ? 1 : 0);   // Photometric: 0 = WhiteIsZero (PDF default BlackIs1=false)
        Tag(273, 4, 1, dataOffset);         // StripOffsets
        Tag(278, 4, 1, height);             // RowsPerStrip
        Tag(279, 4, 1, data.Length);        // StripByteCounts
        w.Write(0);                         // next IFD
        w.Write(data);
        return ms.ToArray();
    }
}

/// <summary>Reads text from newly uploaded documents in the background (one at a time, a few every 20 seconds).</summary>
public sealed class DocumentTextWorker(IServiceScopeFactory scopes, ILogger<DocumentTextWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stop);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var docs = scope.ServiceProvider.GetRequiredService<DocumentService>();
                var extractor = scope.ServiceProvider.GetRequiredService<IDocumentTextExtractor>();
                foreach (var id in await docs.PendingTextAsync(5, stop)) await docs.ProcessTextAsync(id, extractor, stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Document text extraction cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(20), stop);
        }
    }
}
