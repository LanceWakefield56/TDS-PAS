using System.Buffers.Binary;

namespace BrightPAS.Modules.Endoscopy;

/// <summary>
/// Baseline TIFF reader (no third-party imaging library) so TIFF pictures from towers, capture boxes and USB exports can be shown in the browser
/// and printed in reports: first image only, 8-bit greyscale/RGB/RGBA, strips, uncompressed, LZW (with or without horizontal predictor) or PackBits.
/// The original TIFF is kept unchanged beside the PNG made from it.
/// </summary>
public static class Tiff
{
    public static bool IsTiff(ReadOnlySpan<byte> b) => b.Length > 8 && ((b[0] == 'I' && b[1] == 'I' && b[2] == 42 && b[3] == 0) || (b[0] == 'M' && b[1] == 'M' && b[2] == 0 && b[3] == 42));

    /// <summary>Returns a PNG, or an error in plain English when the TIFF uses a feature this reader does not support.</summary>
    public static (byte[]? Png, string? Error) ToPng(byte[] data)
    {
        try
        {
            var (w, h, ch, px) = Decode(data);
            return (Imaging.Png(w, h, px, ch), null);
        }
        catch (NotSupportedException ex) { return (null, ex.Message); }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException) { return (null, "The TIFF file is damaged or not a baseline TIFF."); }
    }

    public static (int Width, int Height, int Channels, byte[] Pixels) Decode(byte[] d)
    {
        if (!IsTiff(d)) throw new InvalidDataException("Not a TIFF.");
        var le = d[0] == 'I';
        ushort U16(int o) => le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o));
        uint U32(int o) => le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(o)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o));
        var ifd = (int)U32(4);
        var n = U16(ifd);
        var tags = new Dictionary<int, uint[]>();
        for (var i = 0; i < n; i++)
        {
            var e = ifd + 2 + i * 12;
            int tag = U16(e), type = U16(e + 2);
            var count = (int)U32(e + 4);
            var size = type switch { 3 => 2, 4 => 4, 1 or 2 or 7 => 1, 5 => 8, _ => 0 };
            if (size == 0 || count <= 0 || count > 1_000_000) continue;
            var at = size * count <= 4 ? e + 8 : (int)U32(e + 8);
            var vals = new uint[type == 5 ? count : count];
            for (var k = 0; k < count; k++)
                vals[k] = type switch { 3 => U16(at + k * 2), 4 => U32(at + k * 4), 5 => U32(at + k * 8) / Math.Max(1u, U32(at + k * 8 + 4)), _ => d[at + k] };
            tags[tag] = vals;
        }
        uint T(int tag, uint def) => tags.TryGetValue(tag, out var v) && v.Length > 0 ? v[0] : def;
        var width = (int)T(256, 0); var height = (int)T(257, 0);
        if (width <= 0 || height <= 0 || (long)width * height > 50_000_000) throw new NotSupportedException("The TIFF picture size is not supported.");
        if (tags.ContainsKey(322)) throw new NotSupportedException("Tiled TIFF files are not supported – export as JPEG or strip TIFF.");
        var compression = T(259, 1); var photometric = T(262, 2); var spp = (int)T(277, 1); var planar = T(284, 1); var predictor = T(317, 1);
        var bits = tags.TryGetValue(258, out var b) ? b : [8];
        if (bits.Any(x => x != 8)) throw new NotSupportedException("Only 8-bit TIFF files are supported (this one is " + bits[0] + "-bit).");
        if (planar != 1) throw new NotSupportedException("Planar TIFF files are not supported.");
        if (compression is not (1 or 5 or 32773)) throw new NotSupportedException($"TIFF compression {compression} is not supported (use uncompressed, LZW or PackBits, or JPEG).");
        if (photometric is not (0 or 1 or 2)) throw new NotSupportedException("Only greyscale and RGB TIFF files are supported.");
        if (!tags.TryGetValue(273, out var offsets) || !tags.TryGetValue(279, out var counts)) throw new InvalidDataException("No strips.");
        var rowsPerStrip = (int)Math.Min(T(278, (uint)height), (uint)height);
        var rowBytes = width * spp;
        var raw = new byte[rowBytes * height];
        var pos = 0;
        for (var s = 0; s < offsets.Length && pos < raw.Length; s++)
        {
            var src = d.AsSpan((int)offsets[s], (int)Math.Min(counts[s], (uint)(d.Length - (int)offsets[s])));
            var expect = Math.Min(rowsPerStrip * rowBytes, raw.Length - pos);
            var strip = compression switch { 5 => Lzw(src, expect), 32773 => PackBits(src, expect), _ => src[..Math.Min(src.Length, expect)].ToArray() };
            if (predictor == 2)
                for (var r = 0; r < strip.Length / rowBytes; r++)
                    for (var x = spp; x < rowBytes; x++) strip[r * rowBytes + x] += strip[r * rowBytes + x - spp];
            strip.AsSpan(0, Math.Min(strip.Length, expect)).CopyTo(raw.AsSpan(pos));
            pos += expect;
        }
        if (photometric is 0 or 1)
        {
            var g = new byte[width * height];
            for (var i = 0; i < g.Length; i++) g[i] = photometric == 0 ? (byte)(255 - raw[i * spp]) : raw[i * spp];
            return (width, height, 1, g);
        }
        if (spp < 3) throw new NotSupportedException("RGB TIFF needs 3 samples per pixel.");
        if (spp == 3) return (width, height, 3, raw);
        var rgb = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++) { rgb[i * 3] = raw[i * spp]; rgb[i * 3 + 1] = raw[i * spp + 1]; rgb[i * 3 + 2] = raw[i * spp + 2]; }
        return (width, height, 3, rgb);
    }

    private static byte[] PackBits(ReadOnlySpan<byte> s, int expect)
    {
        var o = new List<byte>(expect);
        var i = 0;
        while (i < s.Length && o.Count < expect)
        {
            var n = (sbyte)s[i++];
            if (n >= 0) { for (var k = 0; k <= n && i < s.Length; k++) o.Add(s[i++]); }
            else if (n != -128 && i < s.Length) { var v = s[i++]; for (var k = 0; k < 1 - n; k++) o.Add(v); }
        }
        return [.. o];
    }

    /// <summary>TIFF LZW: MSB-first codes, 9–12 bits, Clear 256, EOI 257, "early change" code-width switch.</summary>
    private static byte[] Lzw(ReadOnlySpan<byte> s, int expect)
    {
        var o = new List<byte>(expect);
        var table = new List<byte[]>(4096);
        void Reset() { table.Clear(); for (var k = 0; k < 256; k++) table.Add([(byte)k]); table.Add([]); table.Add([]); }
        Reset();
        int width = 9, bitPos = 0;
        byte[]? prev = null;
        while (o.Count < expect)
        {
            if (bitPos + width > s.Length * 8) break;
            var code = 0;
            for (var k = 0; k < width; k++) { var bit = (s[(bitPos + k) >> 3] >> (7 - ((bitPos + k) & 7))) & 1; code = (code << 1) | bit; }
            bitPos += width;
            if (code == 257) break;
            if (code == 256) { Reset(); width = 9; prev = null; continue; }
            byte[] entry;
            if (code < table.Count) entry = table[code];
            else if (code == table.Count && prev is not null) entry = [.. prev, prev[0]];
            else throw new InvalidDataException("Bad LZW code.");
            o.AddRange(entry);
            if (prev is not null && table.Count < 4096) table.Add([.. prev, entry[0]]);
            prev = entry;
            width = table.Count + 1 >= 2048 ? 12 : table.Count + 1 >= 1024 ? 11 : table.Count + 1 >= 512 ? 10 : 9;
        }
        return [.. o];
    }

    /// <summary>Uncompressed baseline TIFF writer – used by tests and the robot's virtual capture box.</summary>
    public static byte[] Write(int width, int height, byte[] rgb)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        const int entries = 9;
        var dataOffset = 8 + 2 + entries * 12 + 4 + 6; // header + IFD + next + BitsPerSample values
        w.Write("II"u8); w.Write((ushort)42); w.Write(8u);
        w.Write((ushort)entries);
        void E(ushort tag, ushort type, uint count, uint value) { w.Write(tag); w.Write(type); w.Write(count); w.Write(value); }
        E(256, 4, 1, (uint)width); E(257, 4, 1, (uint)height);
        E(258, 3, 3, (uint)(8 + 2 + entries * 12 + 4)); E(259, 3, 1, 1); E(262, 3, 1, 2);
        E(273, 4, 1, (uint)dataOffset); E(277, 3, 1, 3); E(278, 4, 1, (uint)height); E(279, 4, 1, (uint)rgb.Length);
        w.Write(0u);
        w.Write((ushort)8); w.Write((ushort)8); w.Write((ushort)8);
        w.Write(rgb);
        return ms.ToArray();
    }
}
