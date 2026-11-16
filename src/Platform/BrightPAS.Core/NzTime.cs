namespace BrightPAS.Core;

/// <summary>
/// New Zealand time, the same on every server whatever its regional settings. Times are stored as UTC instants
/// (<see cref="DateTimeOffset"/>); a wall-clock time is only turned into an instant here, so daylight saving is handled in one place:
/// <list type="bullet">
/// <item>NZDT starts on the last Sunday of September (02:00 becomes 03:00 – the hour does not exist) and ends on the first Sunday of
/// April (03:00 becomes 02:00 – the hour happens twice). Rules come from the operating system's time-zone database, so a law change
/// arrives with Windows Update.</item>
/// <item>A wall-clock time in the missing hour (e.g. 02:30 on the spring-forward night) is moved forward by the gap (03:30 NZDT),
/// which is what a clock that was not changed would have shown.</item>
/// <item>A wall-clock time in the repeated hour is taken as the first (daylight) occurrence unless the caller says the later one
/// (e.g. the end of a cycle that started before the change).</item>
/// <item>Durations are always worked out from instants, never from wall-clock times, so an overnight shift or a steriliser cycle
/// across the change is 1 hour shorter or longer, as it really was.</item>
/// </list>
/// Calendar arithmetic uses <see cref="DateOnly"/>: leap years are built in (29 February exists in leap years only; adding a year to
/// 29 February gives 28 February), and ages turn over on the birthday, or on 1 March for a 29 February birthday in a common year.
/// </summary>
public static class NzTime
{
    public static readonly TimeZoneInfo Zone = Find();

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "Pacific/Auckland", "New Zealand Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    /// <summary>The instant shown as NZ wall-clock time (offset +12:00 or +13:00).</summary>
    public static DateTimeOffset ToNz(this DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Zone);

    /// <summary>The NZ calendar day an instant falls on.</summary>
    public static DateOnly NzDate(this DateTimeOffset t) => DateOnly.FromDateTime(t.ToNz().DateTime);

    public static DateOnly Today(TimeProvider clock) => clock.GetUtcNow().NzDate();

    /// <summary>Turns an NZ wall-clock time into an instant (see the class notes for the missing and repeated hours).</summary>
    public static DateTimeOffset FromLocal(DateTime local, bool laterIfRepeated = false)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(local))
        {
            var rule = Zone.GetAdjustmentRules().FirstOrDefault(r => r.DateStart <= local.Date && r.DateEnd >= local.Date);
            var gap = rule?.DaylightDelta is { } d && d > TimeSpan.Zero ? d : TimeSpan.FromHours(1);
            local = local.Add(gap);
            return new DateTimeOffset(local, Zone.GetUtcOffset(local));
        }
        if (Zone.IsAmbiguousTime(local))
        {
            var offsets = Zone.GetAmbiguousTimeOffsets(local).OrderByDescending(o => o).ToArray(); // daylight (larger offset) first
            return new DateTimeOffset(local, laterIfRepeated ? offsets[^1] : offsets[0]);
        }
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    public static DateTimeOffset FromLocal(DateOnly date, TimeOnly time, bool laterIfRepeated = false) => FromLocal(date.ToDateTime(time), laterIfRepeated);

    /// <summary>Midnight at the start of an NZ day, as an instant (for "from" filters; use the next day's start as the exclusive "to").</summary>
    public static DateTimeOffset StartOfDay(DateOnly date) => FromLocal(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// An end time read from a machine that records only wall-clock times: the first instant at or after <paramref name="start"/>
    /// (the next day if the clock passed midnight; the second 02:xx if the cycle ran through the end of daylight saving).
    /// </summary>
    public static DateTimeOffset EndAfter(DateTimeOffset start, DateOnly date, TimeOnly time)
    {
        var end = FromLocal(date, time);
        if (end < start) end = FromLocal(date, time, laterIfRepeated: true);
        if (end < start) end = FromLocal(date.AddDays(1), time);
        return end;
    }

    /// <summary>Age in whole years on a date. A 29 February birthday counts from 1 March in a common year.</summary>
    public static int Age(DateOnly dateOfBirth, DateOnly on)
    {
        var age = on.Year - dateOfBirth.Year;
        if (on.Month < dateOfBirth.Month || (on.Month == dateOfBirth.Month && on.Day < dateOfBirth.Day)) age--;
        return age;
    }
}
