using BrightPAS.Core;
using BrightPAS.Modules.Assets;
using BrightPAS.Modules.Quality;
using BrightPAS.Modules.Rostering;

namespace BrightPAS.Modules.Tests;

/// <summary>
/// NZ daylight saving (NZDT from 02:00 on the last Sunday of September to 03:00 on the first Sunday of April) and leap years.
/// 2026: NZDT ends Sunday 5 April and starts Sunday 27 September.
/// </summary>
public class DaylightSavingAndLeapYearTests
{
    private static readonly TimeSpan Nzst = TimeSpan.FromHours(12), Nzdt = TimeSpan.FromHours(13);

    [Fact]
    public void Wall_clock_times_become_the_right_instants_either_side_of_the_changes()
    {
        Assert.Equal(Nzst, NzTime.FromLocal(new DateTime(2026, 6, 3, 9, 34, 9)).Offset);
        Assert.Equal(Nzdt, NzTime.FromLocal(new DateTime(2026, 11, 16, 9, 0, 0)).Offset);
        // Spring forward: 02:30 does not exist – read as 03:30 NZDT.
        var gap = NzTime.FromLocal(new DateTime(2026, 9, 27, 2, 30, 0));
        Assert.Equal(new DateTimeOffset(2026, 9, 26, 14, 30, 0, TimeSpan.Zero), gap.ToUniversalTime());
        // Fall back: 02:30 happens twice – first (NZDT) unless the later is asked for.
        var first = NzTime.FromLocal(new DateTime(2026, 4, 5, 2, 30, 0));
        var second = NzTime.FromLocal(new DateTime(2026, 4, 5, 2, 30, 0), laterIfRepeated: true);
        Assert.Equal(Nzdt, first.Offset);
        Assert.Equal(Nzst, second.Offset);
        Assert.Equal(TimeSpan.FromHours(1), second - first);
        // The NZ day of an instant, not the UTC day.
        Assert.Equal(new DateOnly(2026, 11, 17), new DateTimeOffset(2026, 11, 16, 11, 30, 0, TimeSpan.Zero).NzDate());
        Assert.Equal(TimeSpan.FromHours(23), NzTime.StartOfDay(new DateOnly(2026, 9, 28)) - NzTime.StartOfDay(new DateOnly(2026, 9, 27)));
        Assert.Equal(TimeSpan.FromHours(25), NzTime.StartOfDay(new DateOnly(2026, 4, 6)) - NzTime.StartOfDay(new DateOnly(2026, 4, 5)));
    }

    [Fact]
    public void An_end_time_after_the_start_uses_the_repeated_hour_or_the_next_day()
    {
        var start = NzTime.FromLocal(new DateTime(2026, 4, 5, 2, 50, 0)); // first 02:50 (NZDT)
        Assert.Equal(TimeSpan.FromMinutes(20), NzTime.EndAfter(start, new DateOnly(2026, 4, 5), new TimeOnly(2, 10)) - start);
        var late = NzTime.FromLocal(new DateTime(2026, 6, 3, 23, 40, 0));
        Assert.Equal(TimeSpan.FromMinutes(50), NzTime.EndAfter(late, new DateOnly(2026, 6, 3), new TimeOnly(0, 30)) - late);
    }

    [Theory]
    [InlineData("2008-02-29", "2026-02-28", 17)] // 29 February birthday in a common year: not until 1 March
    [InlineData("2008-02-29", "2026-03-01", 18)]
    [InlineData("2008-02-29", "2028-02-29", 20)]
    [InlineData("1990-03-01", "2028-02-29", 37)]
    [InlineData("1990-03-01", "2028-03-01", 38)]
    [InlineData("1966-11-16", "2026-11-16", 60)] // birthday today
    public void Ages_count_leap_days_correctly(string dob, string on, int age) =>
        Assert.Equal(age, NzTime.Age(DateOnly.Parse(dob, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(on, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void Night_shifts_through_the_change_are_paid_for_the_hours_really_worked()
    {
        var start = new TimeOnly(22, 0); var end = new TimeOnly(7, 0);
        Assert.Equal(9m, RosterService.Hours(new DateOnly(2026, 6, 3), false, start, end, 0));
        Assert.Equal(8m, RosterService.Hours(new DateOnly(2026, 9, 26), false, start, end, 0));  // clocks go forward overnight
        Assert.Equal(10m, RosterService.Hours(new DateOnly(2026, 4, 4), false, start, end, 0));  // clocks go back overnight
        Assert.Equal(7.5m, RosterService.Hours(new DateOnly(2026, 9, 27), false, new TimeOnly(8, 0), new TimeOnly(16, 0), 30));
    }

    [Fact]
    public void A_steriliser_cycle_through_the_change_keeps_its_real_duration_and_plateau()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "steriliser-00000217.csv"), System.Text.Encoding.Latin1);
        var normal = SteriliserLog.Parse(text)!;
        string Shift(string date, string from, string to) => text.Replace("03/06/2026", date, StringComparison.Ordinal)
            .Replace("09:34:09", from, StringComparison.Ordinal).Replace("10:25:08", to, StringComparison.Ordinal);
        var autumn = SteriliserLog.Parse(Shift("05/04/2026", "02:34:09", "02:25:08"))!; // ran through the repeated hour
        var spring = SteriliserLog.Parse(Shift("27/09/2026", "01:34:09", "03:25:08"))!; // ran through the missing hour
        foreach (var r in new[] { autumn, spring })
        {
            Assert.Equal(normal.End!.Value - normal.Start, r.End!.Value - r.Start);
            Assert.Equal(normal.PlateauSeconds, r.PlateauSeconds);
            Assert.True(r.ChecksOk);
        }
        Assert.Null(SteriliserLog.Parse(text.Replace("03/06/2026", "29/02/2026", StringComparison.Ordinal))); // no such day
        Assert.NotNull(SteriliserLog.Parse(text.Replace("03/06/2026", "29/02/2028", StringComparison.Ordinal)));
    }

    [Fact]
    public void Working_days_cross_29_February_and_Mondayised_holidays()
    {
        // 2028 is a leap year: Friday 25 Feb + 5 working days = Friday 3 March (29 Feb is a Tuesday working day).
        Assert.Equal(new DateOnly(2028, 3, 3), WorkingDays.Add(new DateOnly(2028, 2, 25), 5));
        Assert.Equal(new DateOnly(2027, 3, 4), WorkingDays.Add(new DateOnly(2027, 2, 25), 5));
        // Waitangi Day 2027 is a Saturday – Mondayised to 8 February.
        Assert.Contains(new DateOnly(2027, 2, 8), WorkingDays.PublicHolidays(2027));
    }
}
