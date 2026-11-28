using System.Globalization;
using System.IO.Compression;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Imaging.Codec;

namespace BrightPAS.Modules.Pacs;

/// <summary>What BrightPAS reads from one DICOM object's header.</summary>
public sealed record DicomFacts(
    string SopInstanceUid, string SopClassUid, string StudyInstanceUid, string SeriesInstanceUid, string Modality,
    string? AccessionNumber, DateOnly? StudyDate, string? StudyTime, string? StudyDescription, string? SeriesDescription, int? SeriesNumber, int? InstanceNumber,
    string? BodyPart, string? ReferringPhysician, string? Institution,
    string? PatientId, string? PatientName, DateOnly? BirthDate, string? Sex,
    int Rows, int Columns, int Frames, string? Photometric, string TransferSyntax, bool HasPixels,
    double? WindowCentre, double? WindowWidth, string? PixelSpacing);

/// <summary>A rendered picture for the browser.</summary>
public sealed record RenderedImage(byte[] Bytes, string ContentType, int Width, int Height, bool Adjustable, double? Centre, double? Width2);

/// <summary>
/// Reading and drawing DICOM with fo-dicom and no imaging library: header facts, and greyscale (8–16 bit, signed or unsigned, rescale slope/intercept,
/// window centre/width, MONOCHROME1/2) or 8-bit colour pictures drawn as PNG, scaled down to fit. JPEG baseline pictures are passed through as they are.
/// Other compressions are decoded only if a fo-dicom codec for them is installed; otherwise the image is kept and offered for download.
/// </summary>
public static class ImagingDicom
{
    public static DicomFile Open(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return DicomFile.Open(ms, FileReadOption.ReadAll);
    }

    private static string? S(DicomDataset ds, DicomTag tag)
    {
        var v = ds.GetSingleValueOrDefault<string?>(tag, null)?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static string? Multi(DicomDataset ds, DicomTag tag) =>
        ds.TryGetValues<string>(tag, out var v) && v.Length > 0 ? string.Join('\\', v.Select(x => x.Trim())) : null;

    private static double? D(DicomDataset ds, DicomTag tag) =>
        ds.TryGetValues<string>(tag, out var v) && v.Length > 0 && double.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? I(DicomDataset ds, DicomTag tag) =>
        ds.TryGetValues<string>(tag, out var v) && v.Length > 0 && int.TryParse(v[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    public static DateOnly? Date(string? v) => v is { Length: >= 8 } && DateOnly.TryParseExact(v[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>DICOM person name (FAMILY^Given^Middle) as "FAMILY Given Middle".</summary>
    public static string? Name(string? pn) => pn is null ? null : string.Join(' ', pn.Split('=')[0].Split('^', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string Trunc(string? v, int max) => v is null ? "" : v.Length <= max ? v : v[..max];

    public static DicomFacts Facts(DicomFile file)
    {
        var ds = file.Dataset;
        var hasPixels = ds.Contains(DicomTag.PixelData);
        return new DicomFacts(
            S(ds, DicomTag.SOPInstanceUID) ?? "", S(ds, DicomTag.SOPClassUID) ?? file.FileMetaInfo.MediaStorageSOPClassUID?.UID ?? "",
            S(ds, DicomTag.StudyInstanceUID) ?? "", S(ds, DicomTag.SeriesInstanceUID) ?? "", S(ds, DicomTag.Modality) ?? "OT",
            S(ds, DicomTag.AccessionNumber), Date(S(ds, DicomTag.StudyDate)), S(ds, DicomTag.StudyTime), S(ds, DicomTag.StudyDescription), S(ds, DicomTag.SeriesDescription),
            I(ds, DicomTag.SeriesNumber), I(ds, DicomTag.InstanceNumber), S(ds, DicomTag.BodyPartExamined), Name(S(ds, DicomTag.ReferringPhysicianName)), S(ds, DicomTag.InstitutionName),
            S(ds, DicomTag.PatientID), Name(S(ds, DicomTag.PatientName)), Date(S(ds, DicomTag.PatientBirthDate)), S(ds, DicomTag.PatientSex),
            ds.GetSingleValueOrDefault<ushort>(DicomTag.Rows, 0), ds.GetSingleValueOrDefault<ushort>(DicomTag.Columns, 0), Math.Max(1, I(ds, DicomTag.NumberOfFrames) ?? 1),
            S(ds, DicomTag.PhotometricInterpretation), (ds.InternalTransferSyntax ?? file.FileMetaInfo.TransferSyntax).UID.UID, hasPixels,
            D(ds, DicomTag.WindowCenter), D(ds, DicomTag.WindowWidth), Trunc(Multi(ds, DicomTag.PixelSpacing), 40) is { Length: > 0 } ps ? ps : null);
    }

    /// <summary>Whether BrightPAS can draw this object (tries the render once).</summary>
    public static bool CanRender(DicomFile file)
    {
        if (!file.Dataset.Contains(DicomTag.PixelData)) return false;
        try { return Render(file.Dataset, 0, null, null, false, 64) is not null; }
        catch (Exception ex) when (ex is DicomException or InvalidOperationException or NotSupportedException or ArgumentException or IndexOutOfRangeException or OverflowException) { return false; }
    }

    /// <summary>
    /// Draws one frame. <paramref name="centre"/>/<paramref name="width"/> override the window (in modality units, e.g. Hounsfield for CT);
    /// null uses the file's window or, if none, the full range of the picture. Returns null when the picture cannot be drawn.
    /// </summary>
    public static RenderedImage? Render(DicomDataset ds, int frame, double? centre, double? width, bool invert, int maxSize)
    {
        if (!ds.Contains(DicomTag.PixelData)) return null;
        var ts = ds.InternalTransferSyntax;
        if (ts == DicomTransferSyntax.JPEGProcess1)
        {
            var jpx = DicomPixelData.Create(ds);
            if (frame < 0 || frame >= jpx.NumberOfFrames) return null;
            return new RenderedImage(jpx.GetFrame(frame).Data, "image/jpeg", jpx.Width, jpx.Height, false, null, null);
        }
        if (ts.IsEncapsulated)
        {
            // Decoded only when a codec for this compression is installed (fo-dicom.Codecs can be added later without other changes).
            ds = new DicomTranscoder(ts, DicomTransferSyntax.ExplicitVRLittleEndian).Transcode(ds);
        }
        var px = DicomPixelData.Create(ds);
        if (frame < 0 || frame >= px.NumberOfFrames) return null;
        int w = px.Width, h = px.Height;
        if (w <= 0 || h <= 0) return null;
        var data = px.GetFrame(frame).Data;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Max(w, h) / (double)Math.Max(16, maxSize)));
        int tw = Math.Max(1, w / step), th = Math.Max(1, h / step);
        var pi = px.PhotometricInterpretation;

        if (px.SamplesPerPixel == 3 && px.BitsAllocated == 8 && (pi == PhotometricInterpretation.Rgb || pi == PhotometricInterpretation.YbrFull || pi == PhotometricInterpretation.YbrFull422))
        {
            if (pi == PhotometricInterpretation.YbrFull422) return null;
            var planar = px.PlanarConfiguration == PlanarConfiguration.Planar;
            var rgb = new byte[tw * th * 3];
            for (var y = 0; y < th; y++)
                for (var x = 0; x < tw; x++)
                {
                    var i = (y * step) * w + x * step;
                    byte a = planar ? data[i] : data[i * 3], b = planar ? data[w * h + i] : data[i * 3 + 1], c = planar ? data[2 * w * h + i] : data[i * 3 + 2];
                    if (pi == PhotometricInterpretation.YbrFull)
                    {
                        double yy = a, cb = b - 128, cr = c - 128;
                        a = (byte)Math.Clamp(yy + 1.402 * cr, 0, 255); b = (byte)Math.Clamp(yy - 0.344136 * cb - 0.714136 * cr, 0, 255); c = (byte)Math.Clamp(yy + 1.772 * cb, 0, 255);
                    }
                    if (invert) { a = (byte)(255 - a); b = (byte)(255 - b); c = (byte)(255 - c); }
                    var o = (y * tw + x) * 3;
                    rgb[o] = a; rgb[o + 1] = b; rgb[o + 2] = c;
                }
            return new RenderedImage(Png.Encode(tw, th, rgb, 3), "image/png", tw, th, false, null, null);
        }

        if (px.SamplesPerPixel != 1 || (pi != PhotometricInterpretation.Monochrome1 && pi != PhotometricInterpretation.Monochrome2)) return null;
        if (px.BitsAllocated is not (8 or 16)) return null;
        var bitsStored = Math.Clamp((int)px.BitsStored, 1, px.BitsAllocated);
        var signed = px.PixelRepresentation == PixelRepresentation.Signed;
        var slope = D(ds, DicomTag.RescaleSlope) ?? 1.0;
        if (slope == 0) slope = 1.0;
        var intercept = D(ds, DicomTag.RescaleIntercept) ?? 0.0;
        var mask = (1 << bitsStored) - 1;
        var signBit = 1 << (bitsStored - 1);
        var two = px.BitsAllocated == 16;
        if (data.Length < w * h * (two ? 2 : 1)) return null;

        double Value(int i)
        {
            int raw = two ? data[i * 2] | (data[i * 2 + 1] << 8) : data[i];
            raw &= mask;
            if (signed && (raw & signBit) != 0) raw -= 1 << bitsStored;
            return raw * slope + intercept;
        }

        // Sample (box average) into the target size first, so the window and the range come from what is shown.
        var values = new double[tw * th];
        double min = double.MaxValue, max = double.MinValue;
        for (var y = 0; y < th; y++)
            for (var x = 0; x < tw; x++)
            {
                double sum = 0; var n = 0;
                for (var dy = 0; dy < step && y * step + dy < h; dy++)
                    for (var dx = 0; dx < step && x * step + dx < w; dx++) { sum += Value((y * step + dy) * w + x * step + dx); n++; }
                var v = sum / Math.Max(1, n);
                values[y * tw + x] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        var c0 = centre ?? D(ds, DicomTag.WindowCenter) ?? (min + max) / 2;
        var w0 = width ?? D(ds, DicomTag.WindowWidth) ?? Math.Max(1, max - min);
        if (w0 < 1) w0 = 1;
        var flip = (pi == PhotometricInterpretation.Monochrome1) ^ invert;
        var grey = new byte[tw * th];
        for (var i = 0; i < grey.Length; i++)
        {
            var g = (((values[i] - (c0 - 0.5)) / (w0 - 1 <= 0 ? 1 : w0 - 1)) + 0.5) * 255.0;
            var b = (byte)Math.Clamp(Math.Round(g), 0, 255);
            grey[i] = flip ? (byte)(255 - b) : b;
        }
        return new RenderedImage(Png.Encode(tw, th, grey, 1), "image/png", tw, th, true, c0, w0);
    }

    /// <summary>A DICOM UID derived from a random UUID (2.25 root, PS3.5 B.2).</summary>
    public static string NewUid() => "2.25." + new System.Numerics.BigInteger(Guid.NewGuid().ToByteArray().Append((byte)0).ToArray()).ToString(CultureInfo.InvariantCulture);
}

/// <summary>Minimal PNG writer (8-bit greyscale or RGB).</summary>
public static class Png
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(byte[] type, byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in type) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in data) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    public static byte[] Encode(int width, int height, byte[] pixels, int channels)
    {
        if (channels is not (1 or 3) || pixels.Length < width * height * channels) throw new ArgumentException("Pixel data does not match the size.");
        using var ms = new MemoryStream();
        void Chunk(string t, byte[] d)
        {
            var type = System.Text.Encoding.ASCII.GetBytes(t);
            Span<byte> len = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, d.Length); ms.Write(len);
            ms.Write(type); ms.Write(d);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, Crc(type, d)); ms.Write(len);
        }
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = (byte)(channels == 3 ? 2 : 0);
        Chunk("IHDR", ihdr);
        using (var raw = new MemoryStream())
        {
            using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
            {
                var row = width * channels;
                for (var y = 0; y < height; y++) { z.WriteByte(0); z.Write(pixels, y * row, row); }
            }
            Chunk("IDAT", raw.ToArray());
        }
        Chunk("IEND", []);
        return ms.ToArray();
    }
}

/// <summary>
/// Invented X-ray-like pictures as real DICOM files (16-bit greyscale, MONOCHROME2) for demonstration data, the test robot and tests.
/// Not a real patient's image.
/// </summary>
public static class DemoDicom
{
    public sealed record Patient(string PatientId, string Name, DateOnly BirthDate, string Sex);

    public static byte[] Xray(Patient p, string studyUid, string seriesUid, string? accession, DateOnly studyDate, string modality, string bodyPart, string description,
        int instance = 1, int seed = 1, int width = 512, int height = 512)
    {
        var rng = new Random(seed);
        var px = new ushort[width * height];
        double cx = width / 2.0, cy = height / 2.0;
        var chest = bodyPart.Contains("CHEST", StringComparison.OrdinalIgnoreCase);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                double nx = (x - cx) / (width * 0.45), ny = (y - cy) / (height * 0.47);
                double body = Math.Max(0, 1 - Math.Pow(Math.Abs(nx), 2.5) - Math.Pow(Math.Abs(ny) * 0.9, 4));
                double v = 300 + body * 1400;
                if (chest)
                {
                    // Two darker lung fields, a spine and ribs.
                    double lx = Math.Abs(nx) - 0.42, ly = ny + 0.05;
                    var lung = Math.Max(0, 1 - (lx * lx / 0.07 + ly * ly / 0.45));
                    v -= lung * 900;
                    if (Math.Abs(nx) < 0.08) v += 900 * body;
                    var rib = Math.Sin((ny + Math.Abs(nx) * 0.35) * 26) > 0.82 && Math.Abs(nx) > 0.1 && Math.Abs(nx) < 0.85 && ny > -0.75 && ny < 0.55;
                    if (rib) v += 380 * body;
                }
                else
                {
                    // A long bone (e.g. a knee or forearm view).
                    var shaft = Math.Abs(nx) < 0.18 + 0.12 * Math.Exp(-ny * ny * 18);
                    if (shaft) v += 1500 * body;
                    if (Math.Abs(ny) < 0.015 && Math.Abs(nx) < 0.3) v -= 900;
                }
                v += rng.NextDouble() * 40;
                px[y * width + x] = (ushort)Math.Clamp(v, 0, 4095);
            }
        var bytes = new byte[px.Length * 2];
        Buffer.BlockCopy(px, 0, bytes, 0, bytes.Length);
        var sop = ImagingDicom.NewUid();
        var ds = new DicomDataset(DicomTransferSyntax.ExplicitVRLittleEndian)
        {
            { DicomTag.SpecificCharacterSet, "ISO_IR 192" },
            { DicomTag.SOPClassUID, modality == "CR" ? DicomUID.ComputedRadiographyImageStorage : DicomUID.DigitalXRayImageStorageForPresentation },
            { DicomTag.SOPInstanceUID, sop },
            { DicomTag.StudyInstanceUID, studyUid }, { DicomTag.SeriesInstanceUID, seriesUid },
            { DicomTag.StudyDate, studyDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) }, { DicomTag.StudyTime, "101500" },
            { DicomTag.AccessionNumber, accession ?? "" }, { DicomTag.Modality, modality }, { DicomTag.StudyDescription, description },
            { DicomTag.SeriesDescription, description }, { DicomTag.BodyPartExamined, bodyPart }, { DicomTag.SeriesNumber, "1" },
            { DicomTag.InstanceNumber, instance.ToString(CultureInfo.InvariantCulture) }, { DicomTag.InstitutionName, "Demo Radiology" },
            { DicomTag.ReferringPhysicianName, "DEMO^Referrer" },
            { DicomTag.PatientID, p.PatientId }, { DicomTag.PatientName, p.Name }, { DicomTag.PatientBirthDate, p.BirthDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) },
            { DicomTag.PatientSex, p.Sex },
            { DicomTag.SamplesPerPixel, (ushort)1 }, { DicomTag.PhotometricInterpretation, PhotometricInterpretation.Monochrome2.Value },
            { DicomTag.Rows, (ushort)height }, { DicomTag.Columns, (ushort)width },
            { DicomTag.BitsAllocated, (ushort)16 }, { DicomTag.BitsStored, (ushort)12 }, { DicomTag.HighBit, (ushort)11 }, { DicomTag.PixelRepresentation, (ushort)0 },
            { DicomTag.WindowCenter, "1100" }, { DicomTag.WindowWidth, "2000" }, { DicomTag.PixelSpacing, new[] { "0.2", "0.2" } },
        };
        var pd = DicomPixelData.Create(ds, true);
        pd.AddFrame(new FellowOakDicom.IO.Buffer.MemoryByteBuffer(bytes));
        var file = new DicomFile(ds);
        using var ms = new MemoryStream();
        file.Save(ms);
        return ms.ToArray();
    }
}
