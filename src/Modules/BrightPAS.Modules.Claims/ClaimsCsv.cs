using System.Globalization;
using System.Text;

namespace BrightPAS.Modules.Claims;

/// <summary>RFC 4180 CSV read/write. Written fields are guarded against spreadsheet formula injection.</summary>
public static class ClaimsCsv
{
    public static string Field(string? v)
    {
        var s = v ?? "";
        if (s.Length > 0 && (s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : s;
    }

    public static string Row(params string?[] fields) => string.Join(',', fields.Select(Field));
    public static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); if (row.Count > 1 || row[0].Length > 0) rows.Add(row); row = []; break;
                case '﻿' when i == 0: break;
                default: field.Append(ch); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); if (row.Count > 1 || row[0].Length > 0) rows.Add(row); }
        return rows;
    }

    /// <summary>Finds a header column by any of its accepted names (case-insensitive, spaces/underscores ignored).</summary>
    public static int Column(List<string> header, params string[] names)
    {
        static string Norm(string s) => new([.. s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
        for (var i = 0; i < header.Count; i++)
            if (names.Any(n => Norm(n) == Norm(header[i]))) return i;
        return -1;
    }

    public static bool TryMoney(string s, out decimal v) =>
        decimal.TryParse(s.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out v);
}
