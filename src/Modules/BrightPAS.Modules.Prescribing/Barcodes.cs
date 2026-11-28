using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BrightPAS.Modules.Prescribing;

/// <summary>What a GS1 medicine pack barcode holds: GTIN (01), expiry (17), batch (10) and serial (21).</summary>
public sealed record Gs1Pack(string Gtin, DateOnly? Expiry, string? Batch, string? Serial);

/// <summary>
/// Reads GS1 barcodes from medicine packs as a scanner types them: the GS1 DataMatrix with its "]d2" prefix and group separators (FNC1, ASCII 29),
/// the bracketed text printed under it ("(01)09421234567890(17)270630(10)AB12"), or a plain GTIN-8/12/13/14 (linear barcodes).
/// </summary>
public static class Gs1
{
    private const char Gs = (char)29;

    public static Gs1Pack? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        foreach (var p in new[] { "]d2", "]C1", "]e0", "]Q3" })
            if (s.StartsWith(p, StringComparison.Ordinal)) { s = s[p.Length..]; break; }
        if (s.All(char.IsAsciiDigit) && s.Length is 8 or 12 or 13 or 14) return Gtin(s) is { } only ? new Gs1Pack(only, null, null, null) : null;
        return s.Contains('(', StringComparison.Ordinal) ? Bracketed(s) : Raw(s);
    }

    private static Gs1Pack? Bracketed(string s)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] != '(') return null;
            var close = s.IndexOf(')', i);
            if (close < 0) return null;
            var ai = s[(i + 1)..close];
            var next = s.IndexOf('(', close);
            var value = next < 0 ? s[(close + 1)..] : s[(close + 1)..next];
            parts[ai] = value.Trim(Gs, ' ');
            i = next < 0 ? s.Length : next;
        }
        return Build(parts);
    }

    private static Gs1Pack? Raw(string s)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == Gs) { i++; continue; }
            if (i + 2 > s.Length) return null;
            var ai = s.Substring(i, 2);
            i += 2;
            int? fixedLen = ai switch { "01" or "02" => 14, "17" or "11" or "15" => 6, _ => null };
            if (fixedLen is { } n)
            {
                if (i + n > s.Length) return null;
                parts[ai] = s.Substring(i, n);
                i += n;
            }
            else if (ai is "10" or "21" or "30" or "91" or "92")
            {
                var end = s.IndexOf(Gs, i);
                parts[ai] = end < 0 ? s[i..] : s[i..end];
                i = end < 0 ? s.Length : end + 1;
            }
            else return parts.Count > 0 ? Build(parts) : null; // an AI BrightPAS does not use – keep what was read so far
        }
        return Build(parts);
    }

    private static Gs1Pack? Build(Dictionary<string, string> parts)
    {
        if (!parts.TryGetValue("01", out var g) || Gtin(g) is not { } gtin) return null;
        DateOnly? expiry = null;
        if (parts.TryGetValue("17", out var e) && e.Length == 6 && e.All(char.IsAsciiDigit))
        {
            var y = 2000 + int.Parse(e[..2], CultureInfo.InvariantCulture);
            var m = int.Parse(e.AsSpan(2, 2), CultureInfo.InvariantCulture);
            var d = int.Parse(e.AsSpan(4, 2), CultureInfo.InvariantCulture);
            if (m is >= 1 and <= 12)
            {
                var last = DateTime.DaysInMonth(y, m);
                if (d == 0) d = last; // GS1: day 00 means the end of the month
                if (d <= last) expiry = new DateOnly(y, m, d);
            }
        }
        return new Gs1Pack(gtin, expiry, Clean(parts.GetValueOrDefault("10")), Clean(parts.GetValueOrDefault("21")));
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(20, v.Trim().Length)];

    /// <summary>GTIN-8/12/13/14 with a correct check digit, padded to 14 digits; null otherwise.</summary>
    public static string? Gtin(string s)
    {
        if (s.Length is not (8 or 12 or 13 or 14) || !s.All(char.IsAsciiDigit)) return null;
        var g = s.PadLeft(14, '0');
        var sum = 0;
        for (var i = 0; i < 13; i++) sum += (g[i] - '0') * (i % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == g[13] - '0' ? g : null;
    }
}

/// <summary>
/// NZePS prescription id: 18 characters – 17 random characters from 0-9 and A-Z and an ISO 7064 Mod 37,2 check character (0-9, A-Z or *).
/// The exact layout is to be confirmed with Health NZ when TDS connects; the check character lets a pharmacy scanner catch a misread.
/// </summary>
public static class NzepsId
{
    private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ*";

    public static string New()
    {
        var sb = new StringBuilder(18);
        for (var i = 0; i < 17; i++) sb.Append(Chars[RandomNumberGenerator.GetInt32(36)]);
        sb.Append(Check(sb.ToString()));
        return sb.ToString();
    }

    public static char Check(string body)
    {
        var p = 0;
        foreach (var c in body)
        {
            var v = Chars.IndexOf(c, StringComparison.Ordinal);
            if (v is < 0 or > 35) throw new ArgumentException("Only 0-9 and A-Z.", nameof(body));
            p = (p + v) * 2 % 37;
        }
        return Chars[(38 - p) % 37];
    }

    public static bool IsValid(string? id) => id is { Length: 18 } && id[..17].All(c => c is >= '0' and <= '9' or >= 'A' and <= 'Z') && Check(id[..17]) == id[17];
}
