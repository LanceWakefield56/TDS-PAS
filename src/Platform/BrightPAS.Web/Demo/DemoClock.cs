namespace BrightPAS.Web.Demo;

/// <summary>
/// The application clock. Normally the real time; the demonstration-data loader can wind it back for its own work only
/// (an async-local override), so history it creates – past bookings, issued invoices, audit entries – carries realistic dates.
/// Nothing else in the website is affected while it runs.
/// </summary>
public sealed class DemoClock(TimeProvider inner) : TimeProvider
{
    private static readonly AsyncLocal<DateTimeOffset?> Override = new();

    public override DateTimeOffset GetUtcNow() => Override.Value ?? inner.GetUtcNow();
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;
    public override long GetTimestamp() => inner.GetTimestamp();
    public override long TimestampFrequency => inner.TimestampFrequency;

    /// <summary>Until disposed, this async flow sees <paramref name="at"/> as the current time.</summary>
    public static IDisposable At(DateTimeOffset at)
    {
        var previous = Override.Value;
        Override.Value = at.ToUniversalTime();
        return new Restore(previous);
    }

    private sealed class Restore(DateTimeOffset? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }
}
