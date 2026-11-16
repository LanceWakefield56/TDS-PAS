using BrightPAS.Core;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BrightPAS.Modules.Assets;

/// <summary>One sample of a steriliser cycle record: elapsed seconds, chamber temperature (°C) and chamber pressure (kPa absolute).</summary>
public sealed record CurvePoint(int Seconds, decimal TemperatureC, decimal PressureKpa);

/// <summary>What was read from a steriliser cycle record, including the plateau checks used for parametric release.</summary>
public sealed record SteriliserRecord(string Program, string? Site, string? Machine, string? Model, string? Operator, string CycleNumber, DateTimeOffset Start, DateTimeOffset? End,
    decimal? SetTemperatureC, int? SetHoldSeconds, decimal? MaxHoldC, decimal? MinHoldC, string ResultText, bool Passed, string? ErrorCode,
    IReadOnlyList<CurvePoint> Curve, int? PlateauSeconds, decimal? PlateauMinC, decimal? PlateauMaxC, decimal? PlateauPressureKpa, decimal? SaturationC, IReadOnlyList<string> Checks, bool ChecksOk);

/// <summary>
/// The steriliser cycle record TDS's steam steriliser writes to USB / its network folder (CSV, one value per line, then the curve as
/// "temperature;aux;pressure" rows). Layout read from real TDS files (Sterilizer 1, 250 L, cycles 00000217–00000218, June 2026):
/// 1 program, 2 ?, 3 site, 4 steriliser name, 5 model, 6 chamber litres, 7 operator, 8 cycle number, 9 start time, 10 end time, 11 date (dd/MM/yyyy),
/// 12 set temperature (0.1 °C), 13 set hold (mmss), 14 maximum and 15 minimum temperature in the hold (0.1 °C), 16 result text ("[VAL] Cycle Passed"),
/// 17 error code, 18 "IO"; curve rows: temperature (0.1 °C); a second value BrightPAS does not use; chamber pressure (mbar absolute).
/// The steriliser's own result is kept; BrightPAS adds its own plateau checks (AS/NZS 4187 / EN 285 style): time at or above the set temperature,
/// the plateau inside the band (set to set + 3 °C), and that measured temperature agrees with the saturated-steam temperature for the measured
/// pressure within 2 °C (air or superheat would show as a difference). The column meanings should be confirmed with the steriliser supplier.
/// </summary>
public static partial class SteriliserLog
{
    [GeneratedRegex(@"^\s*(\d{3,5});(\d{1,5});(\d{1,5})\s*$")]
    private static partial Regex CurveRow();

    /// <summary>True when the text looks like this record layout (a "[...]" result line and curve rows).</summary>
    public static bool Is(string text)
    {
        var lines = Lines(text);
        return lines.Length > 30 && lines.Take(20).Any(l => l.TrimStart().StartsWith('[')) && lines.Count(l => CurveRow().IsMatch(l)) >= 20
               && DateOnly.TryParseExact(lines[10].Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <remarks>Times are NZ wall-clock times turned into instants by <see cref="NzTime"/> (daylight saving, past midnight); a date that does not exist (e.g. 29/02 in a common year) is not read.</remarks>
    public static SteriliserRecord? Parse(string text)
    {
        if (!Is(text)) return null;
        var l = Lines(text);
        string At(int n) => n < l.Length ? l[n].Trim() : "";
        // The steriliser writes the degree sign in the DOS code page (byte F8): read as Latin-1 it is "ø", read as UTF-8 the replacement character.
        var program = At(0).Replace("\u00F8C", "\u00B0C", StringComparison.Ordinal).Replace("\uFFFDC", "\u00B0C", StringComparison.Ordinal);
        if (!DateOnly.TryParseExact(At(10), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        static TimeOnly? Clock(string s) => TimeOnly.TryParseExact(s, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
        var start = NzTime.FromLocal(date, Clock(At(8)) ?? TimeOnly.MinValue);
        DateTimeOffset? end = Clock(At(9)) is { } et ? NzTime.EndAfter(start, date, et) : null;
        static decimal? Tenths(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v / 10m : null;
        int? ParseMmss(string s) => s.Length == 4 && int.TryParse(s[..2], out var m) && int.TryParse(s[2..], out var sec) ? m * 60 + sec : null;
        var result = At(15);
        var passed = Regex.IsMatch(result, @"\bpass(ed)?\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(result, @"\b(fail|fault|abort|error)", RegexOptions.IgnoreCase);

        var rows = l.Select(x => CurveRow().Match(x)).Where(m => m.Success)
            .Select(m => (T: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) / 10m, P: int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) / 10m)).ToList();
        var span = end is { } e ? (e - start).TotalSeconds : rows.Count * 4.0;
        var step = rows.Count > 1 ? span / (rows.Count - 1) : 0;
        var curve = rows.Select((r, i) => new CurvePoint((int)Math.Round(i * step), r.T, r.P)).ToList();

        var set = Tenths(At(11)); var hold = ParseMmss(At(12));
        var checks = new List<string>();
        var ok = passed;
        int? plateau = null; decimal? pMin = null, pMax = null, pPress = null, sat = null;
        if (set is { } st && curve.Count > 1)
        {
            // The longest continuous run at or above the set temperature is the plateau (hold).
            int bestFrom = -1, bestTo = -1, from = -1;
            for (var i = 0; i <= curve.Count; i++)
            {
                var hot = i < curve.Count && curve[i].TemperatureC >= st;
                if (hot && from < 0) from = i;
                if (!hot && from >= 0) { if (i - 1 - from > bestTo - bestFrom) { bestFrom = from; bestTo = i - 1; } from = -1; }
            }
            if (bestFrom >= 0)
            {
                var run = curve.Skip(bestFrom).Take(bestTo - bestFrom + 1).ToList();
                plateau = run[^1].Seconds - run[0].Seconds + (int)Math.Round(step);
                pMin = run.Min(p => p.TemperatureC); pMax = run.Max(p => p.TemperatureC);
                pPress = Math.Round(run.Average(p => p.PressureKpa), 1);
                sat = SaturationC(pPress.Value);
                var meanT = run.Average(p => p.TemperatureC);
                if (hold is { } h)
                {
                    var holdOk = plateau >= h;
                    checks.Add($"Plateau {Mmss(plateau.Value)} at or above {st:0.0} °C (set hold {Mmss(h)}) – {(holdOk ? "OK" : "TOO SHORT")}.");
                    ok &= holdOk;
                }
                var bandOk = pMax <= st + 3m;
                checks.Add($"Plateau temperature {pMin:0.0}–{pMax:0.0} °C (band {st:0.0}–{st + 3m:0.0} °C) – {(bandOk ? "OK" : "OUT OF BAND")}.");
                ok &= bandOk;
                if (sat is { } s)
                {
                    var diff = Math.Round(meanT - s, 1);
                    var steamOk = Math.Abs(diff) <= 2m;
                    checks.Add($"Saturated steam: measured {meanT:0.0} °C vs {s:0.0} °C from {pPress:0} kPa ({(diff >= 0 ? "+" : "")}{diff:0.0} °C) – {(steamOk ? "OK" : "CHECK – air or superheat?")}.");
                    ok &= steamOk;
                }
            }
            else { checks.Add($"The chamber never reached the set temperature {st:0.0} °C – NOT STERILE."); ok = false; }
        }
        if (!passed) checks.Add($"The steriliser reported: {result}");
        return new SteriliserRecord(program, At(2), At(3), At(4), At(6), At(7), start, end, set, hold, Tenths(At(13)), Tenths(At(14)), result, passed, At(16).Length > 0 ? At(16) : null,
            curve, plateau, pMin, pMax, pPress, sat, checks, ok);
    }

    private static string Mmss(int s) => $"{s / 60}:{s % 60:D2}";

    /// <summary>Saturated steam temperature for an absolute pressure (Antoine equation for water, 100–374 °C), in °C.</summary>
    public static decimal? SaturationC(decimal pressureKpa)
    {
        if (pressureKpa < 101.325m) return null; // below atmospheric the plateau is not steam sterilisation
        var mmHg = (double)pressureKpa * 7.50062;
        var t = 1810.94 / (8.14019 - Math.Log10(mmHg)) - 244.485;
        return Math.Round((decimal)t, 1);
    }
}
