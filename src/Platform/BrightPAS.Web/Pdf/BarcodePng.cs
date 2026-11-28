using System.IO.Compression;
using System.Text;

namespace BrightPAS.Web.Pdf;

/// <summary>A Code 128 barcode as a small black-and-white PNG (one pixel per module), for MigraDoc documents (prescriptions, 0.31.0).</summary>
public static class BarcodePng
{
    public static byte[] Code128(string text, int height = 40)
    {
        var (bars, width) = BrightPAS.Modules.Patients.Code128.Encode(text);
        var row = new byte[width];
        Array.Fill(row, (byte)255);
        foreach (var b in bars) for (var x = b[0]; x < b[0] + b[1]; x++) row[x] = 0;
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++) { raw.WriteByte(0); raw.Write(row); }
        using var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw.ToArray());
        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BE(ihdr, 0, width); BE(ihdr, 4, height);
        ihdr[8] = 8; ihdr[9] = 0; // 8-bit greyscale
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", z.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void BE(byte[] b, int at, int v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; BE(len, 0, data.Length); s.Write(len);
        var t = Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        var crc = Crc(t.Concat(data));
        var c = new byte[4]; BE(c, 0, (int)crc); s.Write(c);
    }

    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(IEnumerable<byte> bytes)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in bytes) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
