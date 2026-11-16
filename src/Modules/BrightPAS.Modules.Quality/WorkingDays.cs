namespace BrightPAS.Modules.Quality;

/// <summary>
/// "Working day" as the Code of Health and Disability Services Consumers' Rights defines it (clause 4): any day except Saturday, Sunday,
/// New Zealand public holidays and the period 25 December – 15 January. Used for the Right 10 complaint timeframes.
/// Public holidays follow the Holidays Act 2003 (with Mondayisation) and the Te Kāhui o Matariki Public Holiday Act 2022 dates.
/// Regional anniversary days and TDS closure days are added through Quality › Settings.
/// </summary>
public static class WorkingDays
{
    // Te Kāhui o Matariki Public Holiday Act 2022, Schedule 1 (2022–2052). Check the schedule when it is next amended.
    private static readonly Dictionary<int, DateOnly> Matariki = new()
    {
        [2022] = new(2022, 6, 24),
        [2023] = new(2023, 7, 14),
        [2024] = new(2024, 6, 28),
        [2025] = new(2025, 6, 20),
        [2026] = new(2026, 7, 10),
        [2027] = new(2027, 6, 25),
        [2028] = new(2028, 7, 14),
        [2029] = new(2029, 7, 6),
        [2030] = new(2030, 6, 21),
        [2031] = new(2031, 7, 11),
        [2032] = new(2032, 7, 2),
        [2033] = new(2033, 6, 24),
        [2034] = new(2034, 7, 7),
        [2035] = new(2035, 6, 29),
        [2036] = new(2036, 7, 18),
        [2037] = new(2037, 7, 10),
        [2038] = new(2038, 6, 25),
        [2039] = new(2039, 7, 15),
        [2040] = new(2040, 7, 6),
        [2041] = new(2041, 7, 19),
        [2042] = new(2042, 7, 11),
        [2043] = new(2043, 7, 3),
        [2044] = new(2044, 6, 24),
        [2045] = new(2045, 7, 7),
        [2046] = new(2046, 6, 29),
        [2047] = new(2047, 7, 19),
        [2048] = new(2048, 7, 3),
        [2049] = new(2049, 6, 25),
        [2050] = new(2050, 7, 15),
        [2051] = new(2051, 6, 30),
        [2052] = new(2052, 6, 21),
    };

    /// <summary>National public holidays for a year (observed dates).</summary>
    public static IReadOnlySet<DateOnly> PublicHolidays(int year)
    {
        var h = new HashSet<DateOnly>();
        // 1 and 2 January: if either falls on a weekend the holiday moves to the next weekday(s).
        var jan1 = new DateOnly(year, 1, 1); var jan2 = new DateOnly(year, 1, 2);
        if (jan1.DayOfWeek == DayOfWeek.Saturday) { h.Add(jan1.AddDays(2)); h.Add(jan1.AddDays(3)); }
        else if (jan1.DayOfWeek == DayOfWeek.Sunday) { h.Add(jan1.AddDays(1)); h.Add(jan1.AddDays(2)); }
        else if (jan1.DayOfWeek == DayOfWeek.Friday) { h.Add(jan1); h.Add(jan1.AddDays(3)); }
        else { h.Add(jan1); h.Add(jan2); }
        h.Add(Mondayise(new DateOnly(year, 2, 6)));  // Waitangi Day
        h.Add(Mondayise(new DateOnly(year, 4, 25))); // ANZAC Day
        var easter = EasterSunday(year);
        h.Add(easter.AddDays(-2)); h.Add(easter.AddDays(1)); // Good Friday, Easter Monday
        h.Add(NthWeekday(year, 6, DayOfWeek.Monday, 1));  // King's Birthday
        if (Matariki.TryGetValue(year, out var m)) h.Add(m);
        h.Add(NthWeekday(year, 10, DayOfWeek.Monday, 4)); // Labour Day
        // Christmas and Boxing Day with Mondayisation.
        var xmas = new DateOnly(year, 12, 25);
        switch (xmas.DayOfWeek)
        {
            case DayOfWeek.Saturday: h.Add(xmas.AddDays(2)); h.Add(xmas.AddDays(3)); break;
            case DayOfWeek.Sunday: h.Add(xmas.AddDays(1)); h.Add(xmas.AddDays(2)); break;
            case DayOfWeek.Friday: h.Add(xmas); h.Add(xmas.AddDays(3)); break;
            default: h.Add(xmas); h.Add(xmas.AddDays(1)); break;
        }
        return h;
    }

    private static DateOnly Mondayise(DateOnly d) => d.DayOfWeek switch { DayOfWeek.Saturday => d.AddDays(2), DayOfWeek.Sunday => d.AddDays(1), _ => d };

    private static DateOnly NthWeekday(int year, int month, DayOfWeek day, int n)
    {
        var d = new DateOnly(year, month, 1);
        while (d.DayOfWeek != day) d = d.AddDays(1);
        return d.AddDays(7 * (n - 1));
    }

    /// <summary>Western Easter Sunday (anonymous Gregorian algorithm).</summary>
    public static DateOnly EasterSunday(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30,
            i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451, month = (h + l - 7 * m + 114) / 31, day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(y, month, day);
    }

    public static bool IsWorkingDay(DateOnly d, IReadOnlySet<DateOnly>? extra = null)
    {
        if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if ((d.Month == 12 && d.Day >= 25) || (d.Month == 1 && d.Day <= 15)) return false; // Code of Rights clause 4
        if (PublicHolidays(d.Year).Contains(d)) return false;
        return extra is null || !extra.Contains(d);
    }

    /// <summary>The date <paramref name="days"/> working days after <paramref name="from"/> (the day itself is not counted).</summary>
    public static DateOnly Add(DateOnly from, int days, IReadOnlySet<DateOnly>? extra = null)
    {
        var d = from;
        for (var n = 0; n < days;)
        {
            d = d.AddDays(1);
            if (IsWorkingDay(d, extra)) n++;
        }
        return d;
    }

    /// <summary>Parses the extra non-working days setting (ISO dates separated by commas, semicolons or new lines); bad entries are ignored.</summary>
    public static IReadOnlySet<DateOnly> ParseExtra(string? text) =>
        (text ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : (DateOnly?)null).Where(d => d is not null).Select(d => d!.Value).ToHashSet();
}
