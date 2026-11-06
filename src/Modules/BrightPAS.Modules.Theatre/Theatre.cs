using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;
using BrightPAS.Core.Organisation;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Theatre;

public sealed class TheatreModule : IPasModule
{
    public const string Id = "theatre";
    public ModuleManifest Manifest { get; } = new(Id, "Theatre bookings", typeof(TheatreModule).Assembly.GetName().Version ?? new Version(0, 0),
        TheatreDbContext.Schema, ["patients"], "Theatre lists, procedure bookings and the day's list.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<TheatreDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, TheatreDbContext.Schema));
        services.AddScoped<TheatreService>();
    }
}

/// <summary>A theatre list: one theatre or procedure room, one date, a time window and the lead clinician.</summary>
public sealed class TheatreSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LocationId { get; set; }
    public string LocationName { get; set; } = "";
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public SessionType Type { get; set; }
    public string Surgeon { get; set; } = "";
    public string? Anaesthetist { get; set; }
    public string? Notes { get; set; }
    public bool IsCancelled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<TheatreBooking> Bookings { get; set; } = [];
}

public enum SessionType { Surgical = 1, Endoscopy = 2, Mixed = 3 }

public sealed class TheatreBooking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid PatientId { get; set; }
    public int Order { get; set; }
    public string Procedure { get; set; } = "";
    public Laterality Laterality { get; set; }
    public int DurationMinutes { get; set; }
    public AnaestheticType Anaesthetic { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Booked;
    public DateTimeOffset StatusAt { get; set; }
    public string? CancelReason { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public enum Laterality { NotApplicable = 0, Left = 1, Right = 2, Bilateral = 3 }
public enum AnaestheticType { General = 1, Sedation = 2, Regional = 3, Local = 4, None = 5 }
public enum BookingStatus { Booked = 1, Confirmed = 2, Arrived = 3, InTheatre = 4, Recovery = 5, Discharged = 6, Cancelled = 7, DidNotAttend = 8 }

public sealed class TheatreDbContext(DbContextOptions<TheatreDbContext> options) : DbContext(options)
{
    public const string Schema = "theatre";
    public DbSet<TheatreSession> Sessions => Set<TheatreSession>();
    public DbSet<TheatreBooking> Bookings => Set<TheatreBooking>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<TheatreSession>(e =>
        {
            e.Property(x => x.LocationName).HasMaxLength(100);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Surgeon).HasMaxLength(150);
            e.Property(x => x.Anaesthetist).HasMaxLength(150);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.Date, x.LocationId });
            e.HasMany(x => x.Bookings).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TheatreBooking>(e =>
        {
            e.Property(x => x.Procedure).HasMaxLength(300);
            e.Property(x => x.Laterality).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Anaesthetic).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
        });
    }
}

public sealed class TheatreDbContextFactory : IDesignTimeDbContextFactory<TheatreDbContext>
{
    public TheatreDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TheatreDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", TheatreDbContext.Schema)).Options);
}

public sealed record NewSession(Guid LocationId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, SessionType Type, string Surgeon, string? Anaesthetist, string? Notes);
public sealed record NewBooking(Guid SessionId, Guid PatientId, string Procedure, Laterality Laterality, int DurationMinutes, AnaestheticType Anaesthetic,
    string? Notes, bool AcceptWarnings = false);
public sealed record BookingResult(TheatreBooking? Booking, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record BookingView(Guid Id, Guid PatientId, string PatientName, string? Nhi, string Mrn, DateOnly DateOfBirth, int Order, string Procedure,
    string Laterality, int DurationMinutes, string Anaesthetic, string Status, string? CancelReason, string? Notes, TimeOnly PlannedStart, TimeOnly PlannedEnd);
public sealed record SessionView(Guid Id, Guid LocationId, string LocationName, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Type, string Surgeon,
    string? Anaesthetist, string? Notes, bool IsCancelled, int BookedMinutes, int AvailableMinutes, bool Overrun, IReadOnlyList<BookingView> Bookings);

public sealed class TheatreService(TheatreDbContext db, PasDbContext core, PatientsDbContext patients, AuditWriter audit, TimeProvider clock)
{
    /// <summary>Minutes allowed between cases for cleaning and set-up when planning start times.</summary>
    public const int TurnoverMinutes = 10;

    public async Task<IReadOnlyList<object>> BookableLocationsAsync(CancellationToken ct = default) =>
        await core.Locations.AsNoTracking().Where(l => l.IsActive && (l.Type == LocationType.Theatre || l.Type == LocationType.ProcedureRoom))
            .OrderBy(l => l.SortOrder).Select(l => (object)new { l.Id, l.Code, l.Name, type = l.Type.ToString() }).ToListAsync(ct);

    public async Task<(TheatreSession? Session, string? Error)> CreateSessionAsync(NewSession s, string user, CancellationToken ct = default)
    {
        var loc = await core.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == s.LocationId, ct);
        if (loc is null || !loc.IsActive || loc.Type is not (LocationType.Theatre or LocationType.ProcedureRoom))
            return (null, "Choose an active theatre or procedure room.");
        if (s.EndTime <= s.StartTime) return (null, "The list must finish after it starts.");
        if (string.IsNullOrWhiteSpace(s.Surgeon)) return (null, "Enter the surgeon or endoscopist for the list.");
        var clash = await db.Sessions.AnyAsync(x => x.LocationId == s.LocationId && x.Date == s.Date && !x.IsCancelled
            && x.StartTime < s.EndTime && s.StartTime < x.EndTime, ct);
        if (clash) return (null, $"{loc.Name} already has a list at that time on {s.Date:d MMM yyyy}.");
        var session = new TheatreSession
        {
            LocationId = loc.Id, LocationName = loc.Name, Date = s.Date, StartTime = s.StartTime, EndTime = s.EndTime, Type = s.Type,
            Surgeon = s.Surgeon.Trim(), Anaesthetist = Blank(s.Anaesthetist), Notes = Blank(s.Notes), CreatedAt = clock.GetUtcNow(), CreatedBy = user
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "TheatreSession", session.Id.ToString(), reason: $"{loc.Name} {s.Date:yyyy-MM-dd} {s.StartTime:HH\\:mm}-{s.EndTime:HH\\:mm} {session.Surgeon}", ct: ct);
        return (session, null);
    }

    public async Task<string?> CancelSessionAsync(Guid id, string user, CancellationToken ct = default)
    {
        var s = await db.Sessions.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null) return "List not found.";
        if (s.Bookings.Any(b => b.Status is not (BookingStatus.Cancelled or BookingStatus.DidNotAttend)))
            return "Move or cancel the bookings on this list first.";
        s.IsCancelled = true;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "TheatreSession", s.Id.ToString(), ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<SessionView>> SessionsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var sessions = await db.Sessions.AsNoTracking().Include(s => s.Bookings)
            .Where(s => s.Date >= from && s.Date <= to && !s.IsCancelled).OrderBy(s => s.Date).ThenBy(s => s.StartTime).ToListAsync(ct);
        var ids = sessions.SelectMany(s => s.Bookings).Select(b => b.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return sessions.Select(s => View(s, people)).ToList();
    }

    public async Task<IReadOnlyList<object>> PatientBookingsAsync(Guid patientId, CancellationToken ct = default)
    {
        var rows = await (from b in db.Bookings.AsNoTracking()
                          join s in db.Sessions.AsNoTracking() on b.SessionId equals s.Id
                          where b.PatientId == patientId
                          orderby s.Date descending
                          select new { b.Id, s.Date, s.LocationName, s.Surgeon, b.Procedure, laterality = b.Laterality.ToString(), status = b.Status.ToString(), sessionId = s.Id })
            .ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    public async Task<BookingResult> BookAsync(NewBooking r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var s = await db.Sessions.Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == r.SessionId, ct);
        if (s is null || s.IsCancelled) errors.Add("Choose an open theatre list.");
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.PatientId, ct);
        if (p is null) errors.Add("Patient not found.");
        if (string.IsNullOrWhiteSpace(r.Procedure)) errors.Add("Enter the procedure.");
        if (r.DurationMinutes is < 5 or > 720) errors.Add("Enter an expected duration between 5 and 720 minutes.");
        if (errors.Count > 0) return new(null, errors, warnings);

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (s!.Date < today) warnings.Add("This list is in the past.");
        var active = s.Bookings.Where(b => b.Status is not (BookingStatus.Cancelled or BookingStatus.DidNotAttend)).ToList();
        if (active.Any(b => b.PatientId == r.PatientId)) warnings.Add("This patient is already booked on this list.");
        else
        {
            var sameDay = await (from b in db.Bookings join x in db.Sessions on b.SessionId equals x.Id
                                 where b.PatientId == r.PatientId && x.Date == s.Date && x.Id != s.Id && !x.IsCancelled
                                       && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.DidNotAttend
                                 select x.LocationName).ToListAsync(ct);
            if (sameDay.Count > 0) warnings.Add($"This patient is already booked on {s.Date:d MMM} ({string.Join(", ", sameDay)}).");
        }
        var booked = active.Sum(b => b.DurationMinutes + TurnoverMinutes);
        var available = (int)(s.EndTime - s.StartTime).TotalMinutes;
        if (booked + r.DurationMinutes > available)
            warnings.Add($"The list will run over: {booked + r.DurationMinutes} of {available} minutes booked.");
        if (warnings.Count > 0 && !r.AcceptWarnings) return new(null, errors, warnings);

        var now = clock.GetUtcNow();
        var booking = new TheatreBooking
        {
            SessionId = s.Id, PatientId = r.PatientId, Order = (active.Count == 0 ? 0 : active.Max(b => b.Order)) + 1, Procedure = r.Procedure.Trim(),
            Laterality = r.Laterality, DurationMinutes = r.DurationMinutes, Anaesthetic = r.Anaesthetic, Notes = Blank(r.Notes),
            StatusAt = now, CreatedAt = now, CreatedBy = user
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "TheatreBooking", booking.Id.ToString(), r.PatientId.ToString(),
            reason: $"{s.LocationName} {s.Date:yyyy-MM-dd}: {booking.Procedure}{(warnings.Count > 0 ? " (warnings accepted: " + string.Join("; ", warnings) + ")" : "")}", ct: ct);
        return new(booking, errors, warnings);
    }

    private static readonly Dictionary<BookingStatus, BookingStatus[]> Allowed = new()
    {
        [BookingStatus.Booked] = [BookingStatus.Confirmed, BookingStatus.Arrived, BookingStatus.Cancelled, BookingStatus.DidNotAttend],
        [BookingStatus.Confirmed] = [BookingStatus.Booked, BookingStatus.Arrived, BookingStatus.Cancelled, BookingStatus.DidNotAttend],
        [BookingStatus.Arrived] = [BookingStatus.InTheatre, BookingStatus.Cancelled],
        [BookingStatus.InTheatre] = [BookingStatus.Recovery],
        [BookingStatus.Recovery] = [BookingStatus.Discharged],
        [BookingStatus.Discharged] = [],
        [BookingStatus.Cancelled] = [BookingStatus.Booked],
        [BookingStatus.DidNotAttend] = [BookingStatus.Booked],
    };

    public async Task<string?> SetStatusAsync(Guid id, BookingStatus status, string? reason, string user, CancellationToken ct = default)
    {
        var b = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return "Booking not found.";
        if (!Allowed[b.Status].Contains(status)) return $"A booking that is '{Label(b.Status)}' cannot be changed to '{Label(status)}'.";
        if (status is BookingStatus.Cancelled && string.IsNullOrWhiteSpace(reason)) return "Enter the reason for cancelling.";
        var from = b.Status;
        b.Status = status;
        b.StatusAt = clock.GetUtcNow();
        b.CancelReason = status == BookingStatus.Cancelled ? reason!.Trim() : null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "StatusChange", "TheatreBooking", b.Id.ToString(), b.PatientId.ToString(), reason: $"{from} -> {status}{(reason is null ? "" : ": " + reason)}", ct: ct);
        return null;
    }

    public async Task<string?> ReorderAsync(Guid sessionId, IReadOnlyList<Guid> bookingIds, string user, CancellationToken ct = default)
    {
        var bookings = await db.Bookings.Where(b => b.SessionId == sessionId).ToListAsync(ct);
        if (bookingIds.Count == 0 || bookingIds.Any(id => bookings.All(b => b.Id != id))) return "The list order does not match the bookings on this list.";
        var order = 1;
        foreach (var id in bookingIds) bookings.Single(b => b.Id == id).Order = order++;
        foreach (var b in bookings.Where(b => !bookingIds.Contains(b.Id)).OrderBy(b => b.Order)) b.Order = order++;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Reorder", "TheatreSession", sessionId.ToString(), ct: ct);
        return null;
    }

    private static SessionView View(TheatreSession s, Dictionary<Guid, Patient> people)
    {
        var t = s.StartTime;
        var list = new List<BookingView>();
        foreach (var b in s.Bookings.OrderBy(b => b.Order))
        {
            var p = people.GetValueOrDefault(b.PatientId);
            var active = b.Status is not (BookingStatus.Cancelled or BookingStatus.DidNotAttend);
            var start = t;
            var end = t.AddMinutes(b.DurationMinutes);
            if (active) t = end.AddMinutes(TurnoverMinutes);
            list.Add(new BookingView(b.Id, b.PatientId, p is null ? "(unknown)" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, p?.Mrn ?? "",
                p?.DateOfBirth ?? default, b.Order, b.Procedure, b.Laterality.ToString(), b.DurationMinutes, b.Anaesthetic.ToString(), b.Status.ToString(),
                b.CancelReason, b.Notes, start, end));
        }
        var booked = s.Bookings.Where(b => b.Status is not (BookingStatus.Cancelled or BookingStatus.DidNotAttend)).Sum(b => b.DurationMinutes + TurnoverMinutes);
        var available = (int)(s.EndTime - s.StartTime).TotalMinutes;
        return new SessionView(s.Id, s.LocationId, s.LocationName, s.Date, s.StartTime, s.EndTime, s.Type.ToString(), s.Surgeon, s.Anaesthetist, s.Notes,
            s.IsCancelled, booked, available, booked > available + TurnoverMinutes, list);
    }

    public static string Label(BookingStatus s) => s switch
    {
        BookingStatus.InTheatre => "In theatre", BookingStatus.DidNotAttend => "Did not attend", _ => s.ToString()
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
