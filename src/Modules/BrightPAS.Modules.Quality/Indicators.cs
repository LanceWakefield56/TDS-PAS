using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Audit;

namespace BrightPAS.Modules.Quality;

/// <summary>
/// One case behind an indicator: a denominator case (an episode, a complaint …) and/or numerator events, with the patient's equity group
/// (Māori, Pacific, Other, Not stated) and a link for drill-down. Built-in calculations produce these from live PAS data – no manual counting.
/// </summary>
public sealed record IndicatorCase(DateOnly On, bool InDenominator, int NumeratorCount, string Label, string? Link, string Group = "Not stated");

/// <summary>Built-in indicator calculations, provided by the web host (they read other modules' data, and only for licensed modules).</summary>
public interface IIndicatorCalculators
{
    IReadOnlyList<(string Key, string Name)> Available { get; }
    Task<IReadOnlyList<IndicatorCase>?> CasesAsync(string key, DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed record SeriesPoint(DateOnly Month, decimal Numerator, decimal? Denominator, decimal? Rate, decimal? Ucl, decimal? Lcl, bool OutsideLimits, bool Shift, bool Trend);
public sealed record IndicatorSeries(Indicator Indicator, IReadOnlyList<SeriesPoint> Points, decimal? Centre, string Chart, string Rag, decimal? Latest,
    IReadOnlyList<(string Group, decimal Numerator, decimal Denominator, decimal? Rate)> Equity);
public sealed record IndicatorEdit(string Code, string Name, string Definition, string Numerator, string Denominator, string? Calculator, int Scale, IndicatorDirection Direction,
    decimal? Target, decimal? AlertAt, string Owner, string? Source, bool IsActive);

/// <summary>
/// Quality indicator library (spec 7.14.1): definitions with targets, monthly series from built-in calculations or manual entry, statistical
/// process control (p-chart for proportions, u-chart for rates per 1,000, c-chart for counts; signals for points outside 3-sigma limits, a shift
/// of 8 points on one side of the centre line and a trend of 6 rising or falling), red/amber/green against the target and an equity split.
/// </summary>
public sealed class IndicatorService(QualityDbContext db, IServiceProvider sp, AuditWriter audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo Nz = TryZone("Pacific/Auckland");
    private static TimeZoneInfo TryZone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; } }
    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Nz).DateTime);
    private IIndicatorCalculators? Calculators => sp.GetService<IIndicatorCalculators>();

    public IReadOnlyList<(string Key, string Name)> AvailableCalculators => Calculators?.Available ?? [];

    /// <summary>The starter library (HQSC/ACHS-style definitions). TDS adjusts targets and adds its own by configuration.</summary>
    public static readonly IReadOnlyList<IndicatorEdit> Starter =
    [
        new("DOSC", "Day-of-surgery cancellation", "Theatre bookings cancelled on the day of surgery.", "Bookings cancelled on the day of their list", "Bookings on lists in the month (not cancelled before the day)", "dosc", 100, IndicatorDirection.LowerIsBetter, 2m, 4m, "Theatre manager", "Theatre bookings", true),
        new("TRANSFER", "Unplanned transfer to hospital", "Episodes ending in transfer to hospital.", "Episodes transferred", "Episodes discharged or transferred", "transfer", 100, IndicatorDirection.LowerIsBetter, 0.5m, 1m, "Clinical nurse manager", "Clinical record", true),
        new("SSC", "Surgical Safety Checklist complete", "Theatre episodes with Sign In, Time Out and Sign Out all recorded.", "Episodes with all three phases", "Episodes that went to theatre", "ssc", 100, IndicatorDirection.HigherIsBetter, 100m, 98m, "Theatre manager", "Clinical record", true),
        new("COUNT", "Count discrepancies", "Theatre episodes with a surgical count that did not match.", "Episodes with any count discrepancy", "Episodes with counts", "count", 100, IndicatorDirection.LowerIsBetter, 0m, 1m, "Theatre manager", "Clinical record", true),
        new("EWS", "Early warning escalations", "Episodes with an early warning score of 5 or more.", "Episodes with EWS ≥ 5", "Episodes with observations", "ews", 100, IndicatorDirection.LowerIsBetter, null, null, "Clinical nurse manager", "Clinical record", true),
        new("INC1000", "Incidents per 1,000 episodes", "Incidents reported (any category) per 1,000 episodes.", "Incidents reported", "Episodes admitted", "inc1000", 1000, IndicatorDirection.LowerIsBetter, null, null, "Quality manager", "Incidents", true),
        new("FALL1000", "Falls per 1,000 episodes", "Falls reported per 1,000 episodes (HQSC falls marker).", "Falls reported", "Episodes admitted", "fall1000", 1000, IndicatorDirection.LowerIsBetter, 1m, 2m, "Quality manager", "Incidents", true),
        new("SAC12", "SAC 1–2 events", "Incidents rated SAC 1 or 2.", "Incidents rated SAC 1–2", "–", "sac12", 1, IndicatorDirection.LowerIsBetter, 0m, 0m, "General manager", "Incidents", true),
        new("CMPACK", "Complaints acknowledged in 5 working days", "Complaints acknowledged in writing within 5 working days (Right 10(6)).", "Acknowledged on time", "Complaints received", "cmpack", 100, IndicatorDirection.HigherIsBetter, 100m, 90m, "Quality manager", "Complaints", true),
        new("CIR", "Caecal intubation rate", "Colonoscopies reaching the caecum (BSG/NBSP standard ≥ 90%, aspirational 95%).", "Caecum reached", "Colonoscopies", "cir", 100, IndicatorDirection.HigherIsBetter, 95m, 90m, "Endoscopy lead", "Endoscopy", true),
        new("ADR", "Adenoma detection rate", "Colonoscopies with at least one adenoma (≥ 25% expected in a screening population).", "Colonoscopies with an adenoma", "Colonoscopies", "adr", 100, IndicatorDirection.HigherIsBetter, 25m, 20m, "Endoscopy lead", "Endoscopy", true),
        new("PSAT", "Patient satisfaction", "Patients rating their care good or excellent (survey).", "Good or excellent", "Survey responses", null, 100, IndicatorDirection.HigherIsBetter, 95m, 90m, "Quality manager", "Patient survey (entered monthly until surveys are built in)", true),
    ];

    public async Task<int> SeedStarterAsync(CancellationToken ct = default)
    {
        var have = await db.Indicators.Select(i => i.Code).ToListAsync(ct);
        var n = 0;
        foreach (var (e, sort) in Starter.Select((e, i) => (e, i)).Where(x => !have.Contains(x.e.Code)))
        {
            db.Indicators.Add(Apply(new Indicator(), e, sort)); n++;
        }
        await db.SaveChangesAsync(ct);
        return n;
    }

    private static Indicator Apply(Indicator i, IndicatorEdit e, int? sort = null)
    {
        i.Code = e.Code.Trim().ToUpperInvariant(); i.Name = e.Name.Trim(); i.Definition = e.Definition.Trim(); i.Numerator = e.Numerator.Trim(); i.Denominator = e.Denominator.Trim();
        i.Calculator = string.IsNullOrWhiteSpace(e.Calculator) ? null : e.Calculator.Trim(); i.Scale = e.Scale; i.Direction = e.Direction; i.Target = e.Target; i.AlertAt = e.AlertAt;
        i.Owner = e.Owner.Trim(); i.Source = e.Source; i.IsActive = e.IsActive;
        if (sort is { } s) i.Sort = s;
        return i;
    }

    public async Task<IReadOnlyList<Indicator>> ListAsync(bool activeOnly, CancellationToken ct = default) =>
        await db.Indicators.AsNoTracking().Where(i => !activeOnly || i.IsActive).OrderBy(i => i.Sort).ThenBy(i => i.Code).ToListAsync(ct);

    public async Task<(Indicator? Indicator, IReadOnlyList<string> Errors)> SaveAsync(Guid? id, IndicatorEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = (e.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is 0 or > 20 || !code.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')) errors.Add("Code: up to 20 letters, digits, - or _.");
        if (string.IsNullOrWhiteSpace(e.Name)) errors.Add("Give the indicator a name.");
        if (e.Scale is not (1 or 100 or 1000)) errors.Add("Show as a count, a percentage or per 1,000.");
        if (e.Calculator is { Length: > 0 } c && AvailableCalculators.All(a => a.Key != c)) errors.Add("Unknown built-in calculation.");
        if (e.Target is not null && e.AlertAt is not null && (e.Direction == IndicatorDirection.LowerIsBetter ? e.AlertAt < e.Target : e.AlertAt > e.Target))
            errors.Add(e.Direction == IndicatorDirection.LowerIsBetter ? "When lower is better, the alert level must be at or above the target." : "When higher is better, the alert level must be at or below the target.");
        if (await db.Indicators.AnyAsync(i => i.Code == code && i.Id != (id ?? Guid.Empty), ct)) errors.Add($"Another indicator already uses {code}.");
        if (errors.Count > 0) return (null, errors);
        Indicator? ind;
        if (id is { } iid) ind = await db.Indicators.SingleOrDefaultAsync(i => i.Id == iid, ct) ?? throw new KeyNotFoundException("Indicator not found.");
        else { ind = new Indicator { Sort = await db.Indicators.CountAsync(ct) }; db.Indicators.Add(ind); }
        Apply(ind, e with { Code = code });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", "Indicator", ind.Id.ToString(), reason: $"{ind.Code} target {ind.Target}", ct: ct);
        return (ind, []);
    }

    public async Task<IReadOnlyList<string>> EnterValueAsync(Guid id, DateOnly month, decimal numerator, decimal? denominator, string user, CancellationToken ct = default)
    {
        var ind = await db.Indicators.SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw new KeyNotFoundException("Indicator not found.");
        if (ind.Calculator is not null) return ["This indicator is calculated from BrightPAS data – it cannot be entered by hand."];
        month = new DateOnly(month.Year, month.Month, 1);
        if (month > Today) return ["The month cannot be in the future."];
        if (numerator < 0 || denominator < 0) return ["Numbers cannot be negative."];
        if (ind.Scale != 1 && (denominator is null or 0)) return ["Enter the denominator (e.g. the number of survey responses)."];
        if (denominator is { } d && numerator > d && ind.Scale == 100) return ["The numerator cannot be larger than the denominator for a percentage."];
        var v = await db.IndicatorValues.SingleOrDefaultAsync(x => x.IndicatorId == id && x.Month == month, ct);
        if (v is null) { v = new IndicatorValue { IndicatorId = id, Month = month }; db.IndicatorValues.Add(v); }
        v.NumeratorValue = numerator; v.DenominatorValue = denominator; v.EnteredBy = user; v.EnteredAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "IndicatorValue", "Indicator", id.ToString(), reason: $"{ind.Code} {month:yyyy-MM}: {numerator}/{denominator}", ct: ct);
        return [];
    }

    /// <summary>Monthly series for the last <paramref name="months"/> months (including the current one), with SPC limits and signals.</summary>
    public async Task<IndicatorSeries> SeriesAsync(Guid id, int months = 12, CancellationToken ct = default)
    {
        var ind = await db.Indicators.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw new KeyNotFoundException("Indicator not found.");
        var first = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-(months - 1));
        var to = Today;
        var monthList = Enumerable.Range(0, months).Select(m => first.AddMonths(m)).ToList();
        var num = monthList.ToDictionary(m => m, _ => 0m);
        var den = monthList.ToDictionary(m => m, _ => (decimal?)null);
        var equity = new Dictionary<string, (decimal N, decimal D)>();
        if (ind.Calculator is { } key)
        {
            var cases = await (Calculators?.CasesAsync(key, first, to, ct) ?? Task.FromResult<IReadOnlyList<IndicatorCase>?>(null)) ?? [];
            foreach (var c in cases)
            {
                var m = new DateOnly(c.On.Year, c.On.Month, 1);
                if (!num.ContainsKey(m)) continue;
                num[m] += c.NumeratorCount;
                if (c.InDenominator) den[m] = (den[m] ?? 0) + 1;
                var g = equity.GetValueOrDefault(c.Group);
                equity[c.Group] = (g.N + c.NumeratorCount, g.D + (c.InDenominator ? 1 : 0));
            }
            if (ind.Scale == 1) foreach (var m in monthList) den[m] = null;
            else foreach (var m in monthList) den[m] ??= 0;
        }
        else
        {
            var values = await db.IndicatorValues.AsNoTracking().Where(v => v.IndicatorId == id && v.Month >= first).ToListAsync(ct);
            foreach (var v in values) { num[v.Month] = v.NumeratorValue; den[v.Month] = v.DenominatorValue; }
        }
        var (points, centre, chart) = Spc(monthList.Select(m => (m, num[m], den[m])).ToList(), ind.Scale, ind.Calculator is null && !await db.IndicatorValues.AnyAsync(v => v.IndicatorId == id, ct));
        var latest = points.LastOrDefault(p => p.Rate is not null)?.Rate;
        return new(ind, points, centre, chart, Rag(ind, latest), latest,
            equity.OrderBy(e => GroupOrder(e.Key)).Select(e => (e.Key, e.Value.N, e.Value.D, ind.Scale == 1 ? (decimal?)e.Value.N : e.Value.D > 0 ? Math.Round(e.Value.N / e.Value.D * ind.Scale, 2) : null)).ToList());
    }

    private static int GroupOrder(string g) => g switch { "Māori" => 0, "Pacific" => 1, "Other" => 2, _ => 3 };

    /// <summary>Drill-down: the cases behind a month.</summary>
    public async Task<IReadOnlyList<IndicatorCase>> CasesAsync(Guid id, DateOnly month, CancellationToken ct = default)
    {
        var ind = await db.Indicators.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw new KeyNotFoundException("Indicator not found.");
        if (ind.Calculator is null || Calculators is null) return [];
        month = new DateOnly(month.Year, month.Month, 1);
        var cases = await Calculators.CasesAsync(ind.Calculator, month, month.AddMonths(1).AddDays(-1), ct) ?? [];
        return cases.OrderByDescending(c => c.NumeratorCount).ThenBy(c => c.On).ToList();
    }

    public static string Rag(Indicator i, decimal? v)
    {
        if (v is null || i.Target is null) return "none";
        var t = i.Target.Value; var a = i.AlertAt ?? t;
        return i.Direction == IndicatorDirection.LowerIsBetter ? (v <= t ? "green" : v <= a ? "amber" : "red") : (v >= t ? "green" : v >= a ? "amber" : "red");
    }

    /// <summary>
    /// SPC: proportions (scale 100) use a p-chart, rates per 1,000 a u-chart and counts a c-chart. Limits are centre ± 3 sigma, recalculated per
    /// month from that month's denominator. Signals: a point outside the limits, 8 in a row on one side of the centre line (shift), 6 in a row rising or falling (trend).
    /// </summary>
    public static (IReadOnlyList<SeriesPoint> Points, decimal? Centre, string Chart) Spc(IReadOnlyList<(DateOnly Month, decimal Num, decimal? Den)> data, int scale, bool noData = false)
    {
        var chart = scale == 1 ? "c" : scale == 100 ? "p" : "u";
        var used = data.Where(d => scale == 1 || d.Den is > 0).ToList();
        if (noData || used.Count == 0) return (data.Select(d => new SeriesPoint(d.Month, d.Num, d.Den, null, null, null, false, false, false)).ToList(), null, chart);
        double centre = scale == 1 ? (double)used.Average(d => d.Num) : (double)(used.Sum(d => d.Num) / used.Sum(d => d.Den!.Value));
        var pts = new List<SeriesPoint>();
        foreach (var d in data)
        {
            if (scale != 1 && d.Den is not > 0) { pts.Add(new(d.Month, d.Num, d.Den, null, null, null, false, false, false)); continue; }
            double rate, sigma;
            if (scale == 1) { rate = (double)d.Num; sigma = Math.Sqrt(centre); }
            else
            {
                var n = (double)d.Den!.Value;
                rate = (double)d.Num / n;
                sigma = chart == "p" ? Math.Sqrt(centre * (1 - centre) / n) : Math.Sqrt(centre / n);
            }
            var ucl = centre + 3 * sigma; var lcl = Math.Max(0, centre - 3 * sigma);
            if (chart == "p") ucl = Math.Min(1, ucl);
            var k = scale == 1 ? 1 : scale;
            pts.Add(new(d.Month, d.Num, d.Den, R(rate * k), R(ucl * k), R(lcl * k), rate > ucl + 1e-12 || rate < lcl - 1e-12, false, false));
        }
        // Shift (8 on one side) and trend (6 rising/falling) over the points that have a rate.
        var idx = pts.Select((p, i) => (p, i)).Where(x => x.p.Rate is not null).Select(x => x.i).ToList();
        var c = R(centre * (scale == 1 ? 1 : scale));
        for (var s = 0; s + 8 <= idx.Count; s++)
        {
            var run = idx.Skip(s).Take(8).Select(i => pts[i].Rate!.Value).ToList();
            if (run.All(r => r > c) || run.All(r => r < c)) foreach (var i in idx.Skip(s).Take(8)) pts[i] = pts[i] with { Shift = true };
        }
        for (var s = 0; s + 6 <= idx.Count; s++)
        {
            var run = idx.Skip(s).Take(6).Select(i => pts[i].Rate!.Value).ToList();
            var up = run.Zip(run.Skip(1)).All(p => p.Second > p.First); var down = run.Zip(run.Skip(1)).All(p => p.Second < p.First);
            if (up || down) foreach (var i in idx.Skip(s).Take(6)) pts[i] = pts[i] with { Trend = true };
        }
        return (pts, c, chart);
    }

    private static decimal R(double v) => Math.Round((decimal)v, 2);

    /// <summary>Prioritised ethnicity group (Statistics NZ / HISO 10001 codes, level 2 or 4): Māori, then Pacific, then Other; residual codes (9x) are Not stated.</summary>
    public static string EquityGroup(IEnumerable<string> codes)
    {
        var list = codes.Where(c => c.Length > 0).ToList();
        if (list.Any(c => c.StartsWith("21", StringComparison.Ordinal))) return "Māori";
        if (list.Any(c => c.StartsWith('3'))) return "Pacific";
        if (list.Any(c => !c.StartsWith('9'))) return "Other";
        return "Not stated";
    }
}
