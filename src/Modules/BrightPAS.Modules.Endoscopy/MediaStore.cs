using System.IO.Compression;
using System.Security.Cryptography;
using BrightPAS.Core.Setup;

namespace BrightPAS.Modules.Endoscopy;

/// <summary>
/// Pictures and video are files in the endoscopy media folder (setting endo.mediaPath; default C:\ProgramData\BrightPAS\Endoscopy on
/// Windows). File names are generated here (yyyy\MM\{media id}.ext) – never taken from the caller – and a SHA-256 is kept so tampering shows.
/// </summary>
public sealed class MediaStore(SettingsService settings, Microsoft.Extensions.Configuration.IConfiguration? config = null)
{
    public const string PathKey = "endo.mediaPath";

    /// <summary>Setup writes Endoscopy:MediaPath = &lt;BrightPAS data folder&gt;\endoscopy (already secured for the app pool); otherwise a per-machine default.</summary>
    public string DefaultRootPath => config?["Endoscopy:MediaPath"] is { Length: > 0 } c ? c : DefaultRoot;

    public static string DefaultRoot => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrightPAS", "Endoscopy")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".brightpas", "endoscopy");

    public async Task<string> RootAsync(CancellationToken ct = default) => await settings.GetAsync(PathKey, ct) is { Length: > 0 } p ? p : DefaultRootPath;

    public static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg", "image/png" => ".png", "video/webm" => ".webm", "video/mp4" => ".mp4", "application/dicom" => ".dcm", _ => ".bin",
    };

    public static string NewFileName(Media m) => Path.Combine(m.CapturedAt.UtcDateTime.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture),
        m.CapturedAt.UtcDateTime.ToString("MM", System.Globalization.CultureInfo.InvariantCulture), m.Id.ToString("N") + Extension(m.ContentType));

    private async Task<string> FullAsync(Media m, CancellationToken ct)
    {
        var root = Path.GetFullPath(await RootAsync(ct));
        var full = Path.GetFullPath(Path.Combine(root, m.FileName));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Media path is outside the media folder.");
        return full;
    }

    public async Task WriteAsync(Media m, byte[] data, CancellationToken ct = default)
    {
        if (m.FileName.Length == 0) m.FileName = NewFileName(m);
        var full = await FullAsync(m, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, data, ct);
        m.Bytes = data.LongLength;
        m.Sha256 = Convert.ToHexString(SHA256.HashData(data));
    }

    public async Task AppendAsync(Media m, Stream chunk, CancellationToken ct = default)
    {
        if (m.FileName.Length == 0) m.FileName = NewFileName(m);
        var full = await FullAsync(m, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using (var f = new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.Read))
            await chunk.CopyToAsync(f, ct);
        m.Bytes = new FileInfo(full).Length;
    }

    public async Task SealAsync(Media m, CancellationToken ct = default)
    {
        var full = await FullAsync(m, ct);
        if (!File.Exists(full)) { m.Bytes = 0; return; }
        await using var f = File.OpenRead(full);
        m.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(f, ct));
        m.Bytes = f.Length;
    }

    public async Task<string?> PathIfExistsAsync(Media m, CancellationToken ct = default)
    {
        if (m.FileName.Length == 0) return null;
        var full = await FullAsync(m, ct);
        return File.Exists(full) ? full : null;
    }

    public async Task<byte[]?> ReadAsync(Media m, CancellationToken ct = default) => await PathIfExistsAsync(m, ct) is { } p ? await File.ReadAllBytesAsync(p, ct) : null;

    public async Task DeleteFileAsync(Media m, CancellationToken ct = default)
    {
        if (await PathIfExistsAsync(m, ct) is { } p) File.Delete(p);
    }

    /// <summary>Set-up check: the folder exists (or can be made) and the website can write to it.</summary>
    public async Task<(bool Ok, string Detail)> CheckAsync(CancellationToken ct = default)
    {
        var root = await RootAsync(ct);
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".write-test-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
            return (true, $"{root} – {free / 1024 / 1024 / 1024} GB free");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return (false, $"{root}: {ex.Message}"); }
    }
}

/// <summary>Small image helpers with no third-party imaging library: file type sniffing, a PNG writer for raw RGB, demo pictures.</summary>
public static class Imaging
{
    public static string? Sniff(ReadOnlySpan<byte> b)
    {
        if (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "image/png";
        if (b.Length > 4 && b[0] == 0x1A && b[1] == 0x45 && b[2] == 0xDF && b[3] == 0xA3) return "video/webm";
        if (b.Length > 12 && b[4] == (byte)'f' && b[5] == (byte)'t' && b[6] == (byte)'y' && b[7] == (byte)'p') return "video/mp4";
        return null;
    }

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

    /// <summary>PNG from 8-bit RGB (3 bytes per pixel) or greyscale (1 byte per pixel).</summary>
    public static byte[] Png(int width, int height, byte[] pixels, int channels = 3)
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

    /// <summary>An invented endoscopy-like picture (pink mucosa with folds in the round endoscope field) for demonstration and tests.</summary>
    public static byte[] DemoPicture(int seed, bool polyp = false, int width = 480, int height = 360)
    {
        var rng = new Random(seed);
        var px = new byte[width * height * 3];
        double cx = width / 2.0, cy = height / 2.0, r = Math.Min(width, height) * 0.48, phase = rng.NextDouble() * 6, folds = 3 + rng.Next(4);
        double hx = cx + (rng.NextDouble() - 0.5) * r * 0.6, hy = cy + (rng.NextDouble() - 0.5) * r * 0.6;
        double px0 = cx + (rng.NextDouble() - 0.5) * r * 0.8, py0 = cy + (rng.NextDouble() - 0.5) * r * 0.8, pr = r * (0.12 + rng.NextDouble() * 0.1);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > r) continue;
                double lx = x - hx, ly = y - hy, ld = Math.Sqrt(lx * lx + ly * ly) / r;
                var fold = 0.5 + 0.5 * Math.Sin(Math.Atan2(ly, lx) * folds + ld * 9 + phase);
                var light = Math.Clamp(1.15 - ld * 0.9, 0.15, 1.0) * (0.75 + 0.25 * fold);
                double red = 205, green = 92, blue = 88;
                if (polyp)
                {
                    double qx = x - px0, qy = y - py0, q = Math.Sqrt(qx * qx + qy * qy) / pr;
                    if (q < 1) { light = Math.Min(1.0, light * (1.25 - 0.35 * q)); red = 225; green = 120; blue = 105; }
                }
                var spec = ld < 0.06 ? 60 : 0;
                px[i] = (byte)Math.Clamp(red * light + spec, 0, 255);
                px[i + 1] = (byte)Math.Clamp(green * light + spec, 0, 255);
                px[i + 2] = (byte)Math.Clamp(blue * light + spec, 0, 255);
            }
        return Png(width, height, px);
    }
}
