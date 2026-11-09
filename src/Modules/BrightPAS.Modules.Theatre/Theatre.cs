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
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public SessionType Type { get; set; }
    /// <summary>Surgeon or endoscopist (core.Clinicians).</summary>
    public Guid SurgeonId { get; set; }
    /// <summary>Anaesthetist (core.Clinicians), if any.</summary>
    public Guid? AnaesthetistId { get; set; }
    public string? Notes { get; set; }
    public bool IsCancelled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public List<TheatreBooking> Bookings { get; set; } = [];
}

public enum SessionType { Surgical = 1, Endoscopy = 2, Mixed = 3 }

/// <summary>A recurring theatre list, e.g. "Dr Hall, Theatre 1, Tuesday 08:00–12:30, every week". Lists are generated from templates for a date range.</summary>
public sealed class SessionTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LocationId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public SessionType Type { get; set; }
    public Guid SurgeonId { get; set; }
    public Guid? AnaesthetistId { get; set; }
    /// <summary>1 = every week, 2 = every second week, … counted from <see cref="StartsOn"/>.</summary>
    public int EveryWeeks { get; set; } = 1;
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

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
    // Booking details (fields TDS used in TeamUp).
    /// <summary>Booked but not yet confirmed (shown striped on the calendar).</summary>
    public bool IsTentative { get; set; }
    public PaymentType Payment { get; set; }
    /// <summary>Deposit required (insurance shortfall or self-funding) – amount or note.</summary>
    public string? DepositNote { get; set; }
    /// <summary>Completed pre-booking checks (one row per item; keys from <see cref="TheatreService.ChecklistItems"/>).</summary>
    public List<BookingCheck> Checks { get; set; } = [];
    /// <summary>Office only: entered in the external billing systems (e.g. Incisive and Xero).</summary>
    public bool BilledExternally { get; set; }
    public TimeOnly? TheatreIn { get; set; }
    public TimeOnly? TheatreOut { get; set; }
    public string? TraysNeeded { get; set; }
    public string? TraysUsed { get; set; }
    public string? LoanItems { get; set; }
    /// <summary>Endoscope used (traceability).</summary>
    public string? ScopeId { get; set; }
}

/// <summary>A completed pre-booking check for a booking (normalised from the old comma-separated list, 0.10.1).</summary>
public sealed class BookingCheck
{
    public Guid BookingId { get; set; }
    public string ItemKey { get; set; } = "";
    public DateTimeOffset CheckedAt { get; set; }
    public string CheckedBy { get; set; } = "";
}

public enum PaymentType { NotSet = 0, SelfFunded = 1, Insurer = 2, Acc = 3, HealthNz = 4, NoCharge = 5 }

public enum AppointmentKind { Consultation = 1, FollowUp = 2, PhoneCall = 3, PreAdmission = 4, Other = 5 }
public enum AppointmentStatus { Booked = 1, Confirmed = 2, Arrived = 3, Completed = 4, Cancelled = 5, DidNotAttend = 6 }

/// <summary>A clinic appointment that is not a theatre case: consultation, follow-up, phone call or pre-admission visit.</summary>
public sealed class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public int DurationMinutes { get; set; } = 15;
    public AppointmentKind Kind { get; set; }
    /// <summary>Clinician seen (core.Clinicians).</summary>
    public Guid ClinicianId { get; set; }
    public Guid? LocationId { get; set; }
    public bool NoCharge { get; set; }
    public bool IsTentative { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;
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
    public DbSet<SessionTemplate> Templates => Set<SessionTemplate>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<BookingCheck> Checks => Set<BookingCheck>();

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
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.SurgeonId, x.Date });
            e.HasIndex(x => x.AnaesthetistId);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.Date, x.LocationId });
            e.HasIndex(x => x.LocationId);
            e.HasMany(x => x.Bookings).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SessionTemplate>(e =>
        {
            e.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.SurgeonId);
            e.HasIndex(x => x.AnaesthetistId);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.LocationId);
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
            e.Property(x => x.Payment).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.DepositNote).HasMaxLength(200);
            e.Property(x => x.TraysNeeded).HasMaxLength(500);
            e.Property(x => x.TraysUsed).HasMaxLength(500);
            e.Property(x => x.LoanItems).HasMaxLength(500);
            e.Property(x => x.ScopeId).HasMaxLength(50);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.ScopeId);
            e.HasMany(x => x.Checks).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Checks).AutoInclude();
        });
        b.Entity<BookingCheck>(e =>
        {
            e.ToTable("BookingChecks");
            e.HasKey(x => new { x.BookingId, x.ItemKey });
            e.Property(x => x.ItemKey).HasMaxLength(30);
            e.Property(x => x.CheckedBy).HasMaxLength(100);
        });
        b.Entity<Appointment>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.ClinicianId, x.Date });
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.Date);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.LocationId);
        });
    }
}

public sealed class TheatreDbContextFactory : IDesignTimeDbContextFactory<TheatreDbContext>
{
    public TheatreDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TheatreDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", TheatreDbContext.Schema)).Options);
}

public sealed record NewSession(Guid LocationId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, SessionType Type, Guid SurgeonId, Guid? AnaesthetistId, string? Notes);
public sealed record TemplateEdit(Guid? Id, Guid LocationId, DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, SessionType Type, Guid SurgeonId,
    Guid? AnaesthetistId, int EveryWeeks, DateOnly StartsOn, DateOnly? EndsOn, bool IsActive);
public sealed record GenerateResult(int Created, IReadOnlyList<string> Skipped);

public sealed record NewBooking(Guid SessionId, Guid PatientId, string Procedure, Laterality Laterality, int DurationMinutes, AnaestheticType Anaesthetic,
    string? Notes, bool AcceptWarnings = false, PaymentType Payment = PaymentType.NotSet, bool IsTentative = false);
public sealed record BookingDetails(bool IsTentative, PaymentType Payment, string? DepositNote, IReadOnlyList<string>? Checklist, bool BilledExternally,
    TimeOnly? TheatreIn, TimeOnly? TheatreOut, string? TraysNeeded, string? TraysUsed, string? LoanItems, string? ScopeId, string? Notes);
public sealed record NewAppointment(Guid PatientId, DateOnly Date, TimeOnly StartTime, int DurationMinutes, AppointmentKind Kind, Guid ClinicianId, Guid? LocationId,
    bool NoCharge, bool IsTentative, string? Notes, bool AcceptWarnings = false);
public sealed record AppointmentView(Guid Id, Guid PatientId, string PatientName, string? Nhi, string Mrn, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    int DurationMinutes, string Kind, Guid ClinicianId, string Clinician, string? LocationName, bool NoCharge, bool IsTentative, string Status, string? CancelReason, string? Notes);
public sealed record BookingResult(TheatreBooking? Booking, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record BookingView(Guid Id, Guid PatientId, string PatientName, string? Nhi, string Mrn, DateOnly DateOfBirth, int Order, string Procedure,
    string Laterality, int DurationMinutes, string Anaesthetic, string Status, string? CancelReason, string? Notes, TimeOnly PlannedStart, TimeOnly PlannedEnd,
    bool IsTentative, string Payment, string? DepositNote, IReadOnlyList<string> Checklist, bool BilledExternally, TimeOnly? TheatreIn, TimeOnly? TheatreOut,
    string? TraysNeeded, string? TraysUsed, string? LoanItems, string? ScopeId, string? Phone);
public sealed record SessionView(Guid Id, Guid LocationId, string LocationName, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Type, Guid SurgeonId, string Surgeon,
    Guid? AnaesthetistId, string? Anaesthetist, string? Notes, bool IsCancelled, int BookedMinutes, int AvailableMinutes, bool Overrun, IReadOnlyList<BookingView> Bookings);

public sealed class TheatreService(TheatreDbContext db, PasDbContext core, PatientsDbContext patients, AuditWriter audit, TimeProvider clock)
{
    /// <summary>Minutes allowed between cases for cleaning and set-up when planning start times.</summary>
    public const int TurnoverMinutes = 10;

    /// <summary>Names of every clinician (active or not) – names are looked up, never stored on lists or appointments (3NF, 0.10.2).</summary>
    public async Task<Dictionary<Guid, string>> ClinicianNamesAsync(CancellationToken ct = default) =>
        await core.Clinicians.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    public async Task<Dictionary<Guid, string>> LocationNamesAsync(CancellationToken ct = default) =>
        await core.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);

    /// <summary>Error text when the clinician is missing or inactive (null = fine).</summary>
    public async Task<string?> ClinicianProblemAsync(Guid? id, bool required, string what, CancellationToken ct = default)
    {
        if (id is null || id == Guid.Empty) return required ? $"Choose the {what}." : null;
        var c = await core.Clinicians.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? $"Choose the {what} from the clinician register." : !c.IsActive ? $"{c.Name} is not active in the clinician register." : null;
    }

    public async Task<IReadOnlyList<object>> BookableLocationsAsync(CancellationToken ct = default) =>
        await core.Locations.AsNoTracking().Where(l => l.IsActive && (l.Type == LocationType.Theatre || l.Type == LocationType.ProcedureRoom))
            .OrderBy(l => l.SortOrder).Select(l => (object)new { l.Id, l.Code, l.Name, type = l.Type.ToString() }).ToListAsync(ct);

    public async Task<(TheatreSession? Session, string? Error)> CreateSessionAsync(NewSession s, string user, CancellationToken ct = default)
    {
        var loc = await core.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == s.LocationId, ct);
        if (loc is null || !loc.IsActive || loc.Type is not (LocationType.Theatre or LocationType.ProcedureRoom))
            return (null, "Choose an active theatre or procedure room.");
        if (s.EndTime <= s.StartTime) return (null, "The list must finish after it starts.");
        if (await ClinicianProblemAsync(s.SurgeonId, true, "surgeon or endoscopist", ct) is { } e1) return (null, e1);
        if (await ClinicianProblemAsync(s.AnaesthetistId, false, "anaesthetist", ct) is { } e2) return (null, e2);
        var clash = await db.Sessions.AnyAsync(x => x.LocationId == s.LocationId && x.Date == s.Date && !x.IsCancelled
            && x.StartTime < s.EndTime && s.StartTime < x.EndTime, ct);
        if (clash) return (null, $"{loc.Name} already has a list at that time on {s.Date:d MMM yyyy}.");
        var session = new TheatreSession
        {
            LocationId = loc.Id, Date = s.Date, StartTime = s.StartTime, EndTime = s.EndTime, Type = s.Type,
            SurgeonId = s.SurgeonId, AnaesthetistId = s.AnaesthetistId == Guid.Empty ? null : s.AnaesthetistId, Notes = Blank(s.Notes), CreatedAt = clock.GetUtcNow(), CreatedBy = user
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "TheatreSession", session.Id.ToString(), reason: $"{loc.Name} {s.Date:yyyy-MM-dd} {s.StartTime:HH\\:mm}-{s.EndTime:HH\\:mm} surgeon {session.SurgeonId}", ct: ct);
        return (session, null);
    }

    public async Task<IReadOnlyList<object>> TemplatesAsync(CancellationToken ct = default)
    {
        var names = await LocationNamesAsync(ct);
        var people = await ClinicianNamesAsync(ct);
        // Sorted in memory: the day is stored as text, so SQL cannot sort it Monday-first.
        return (await db.Templates.AsNoTracking().ToListAsync(ct)).OrderBy(t => t.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)t.DayOfWeek).ThenBy(t => t.StartTime)
            .Select(t => (object)new { t.Id, t.LocationId, locationName = names.GetValueOrDefault(t.LocationId, "?"), dayOfWeek = t.DayOfWeek.ToString(), t.StartTime, t.EndTime,
                type = t.Type.ToString(), t.SurgeonId, surgeon = people.GetValueOrDefault(t.SurgeonId, "?"), t.AnaesthetistId,
                anaesthetist = t.AnaesthetistId is { } a ? people.GetValueOrDefault(a) : null, t.EveryWeeks, t.StartsOn, t.EndsOn, t.IsActive }).ToList();
    }

    public async Task<(SessionTemplate? Template, string? Error)> SaveTemplateAsync(TemplateEdit t, string user, CancellationToken ct = default)
    {
        var loc = await core.Locations.AsNoTracking().SingleOrDefaultAsync(l => l.Id == t.LocationId, ct);
        if (loc is null || loc.Type is not (LocationType.Theatre or LocationType.ProcedureRoom)) return (null, "Choose a theatre or procedure room.");
        if (t.EndTime <= t.StartTime) return (null, "The list must finish after it starts.");
        if (await ClinicianProblemAsync(t.SurgeonId, true, "surgeon or endoscopist", ct) is { } e1) return (null, e1);
        if (await ClinicianProblemAsync(t.AnaesthetistId, false, "anaesthetist", ct) is { } e2) return (null, e2);
        if (t.EveryWeeks is < 1 or > 8) return (null, "Repeat every 1 to 8 weeks.");
        if (t.EndsOn is { } end && end < t.StartsOn) return (null, "The end date must be after the start date.");
        var clash = await db.Templates.AnyAsync(x => x.Id != t.Id && x.IsActive && t.IsActive && x.LocationId == t.LocationId && x.DayOfWeek == t.DayOfWeek
            && x.StartTime < t.EndTime && t.StartTime < x.EndTime && x.EveryWeeks == 1 && t.EveryWeeks == 1, ct);
        if (clash) return (null, $"Another weekly template already uses {loc.Name} at that time on {t.DayOfWeek}s.");
        SessionTemplate row;
        if (t.Id is { } id) row = await db.Templates.SingleAsync(x => x.Id == id, ct);
        else { row = new SessionTemplate { CreatedAt = clock.GetUtcNow(), CreatedBy = user }; db.Templates.Add(row); }
        row.LocationId = t.LocationId; row.DayOfWeek = t.DayOfWeek; row.StartTime = t.StartTime; row.EndTime = t.EndTime; row.Type = t.Type;
        row.SurgeonId = t.SurgeonId; row.AnaesthetistId = t.AnaesthetistId == Guid.Empty ? null : t.AnaesthetistId; row.EveryWeeks = t.EveryWeeks; row.StartsOn = t.StartsOn; row.EndsOn = t.EndsOn; row.IsActive = t.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, t.Id is null ? "Create" : "Update", "SessionTemplate", row.Id.ToString(),
            reason: $"{loc.Name} {row.DayOfWeek} {row.StartTime:HH\\:mm}-{row.EndTime:HH\\:mm} surgeon {row.SurgeonId} every {row.EveryWeeks} week(s){(row.IsActive ? "" : " (inactive)")}", ct: ct);
        return (row, null);
    }

    /// <summary>Creates theatre lists from the active templates for a date range (up to 26 weeks). Existing or clashing lists are skipped, never changed.</summary>
    public async Task<GenerateResult> GenerateFromTemplatesAsync(DateOnly from, DateOnly to, string user, CancellationToken ct = default)
    {
        if (to < from) return new(0, ["The end date is before the start date."]);
        if (to.DayNumber - from.DayNumber > 182) return new(0, ["Generate at most 26 weeks at a time."]);
        var templates = await db.Templates.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        var locs = await core.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, ct);
        var created = 0;
        var skipped = new List<string>();
        for (var d = from; d <= to; d = d.AddDays(1))
            foreach (var t in templates.Where(t => t.DayOfWeek == d.DayOfWeek && d >= t.StartsOn && (t.EndsOn is null || d <= t.EndsOn)
                         && ((d.DayNumber - t.StartsOn.DayNumber) / 7) % t.EveryWeeks == 0))
            {
                var loc = locs.GetValueOrDefault(t.LocationId);
                if (loc is null || !loc.IsActive) { skipped.Add($"{d:ddd d MMM}: {loc?.Name ?? "room"} is not active."); continue; }
                var clash = await db.Sessions.AnyAsync(x => x.LocationId == t.LocationId && x.Date == d && !x.IsCancelled && x.StartTime < t.EndTime && t.StartTime < x.EndTime, ct);
                if (clash) { skipped.Add($"{d:ddd d MMM}: {loc.Name} already has a list at that time."); continue; }
                db.Sessions.Add(new TheatreSession { LocationId = loc.Id, Date = d, StartTime = t.StartTime, EndTime = t.EndTime, Type = t.Type,
                    SurgeonId = t.SurgeonId, AnaesthetistId = t.AnaesthetistId, Notes = "From template", CreatedAt = clock.GetUtcNow(), CreatedBy = user });
                await db.SaveChangesAsync(ct);
                created++;
            }
        await audit.WriteAsync(user, "Generate", "TheatreSession", $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}", reason: $"{created} list(s) created from templates, {skipped.Count} skipped", ct: ct);
        return new(created, skipped);
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
        var clinicians = await ClinicianNamesAsync(ct);
        var rooms = await LocationNamesAsync(ct);
        return sessions.Select(s => View(s, people, clinicians, rooms)).ToList();
    }

    public async Task<IReadOnlyList<object>> PatientBookingsAsync(Guid patientId, CancellationToken ct = default)
    {
        var rows = await (from b in db.Bookings.AsNoTracking()
                          join s in db.Sessions.AsNoTracking() on b.SessionId equals s.Id
                          where b.PatientId == patientId
                          orderby s.Date descending
                          select new { b.Id, s.Date, s.LocationId, s.SurgeonId, b.Procedure, laterality = b.Laterality.ToString(), status = b.Status.ToString(), sessionId = s.Id })
            .ToListAsync(ct);
        var clinicians = await ClinicianNamesAsync(ct);
        var rooms = await LocationNamesAsync(ct);
        return rows.Select(r => (object)new { r.Id, r.Date, locationName = rooms.GetValueOrDefault(r.LocationId, "?"), r.SurgeonId, surgeon = clinicians.GetValueOrDefault(r.SurgeonId, "?"),
            r.Procedure, r.laterality, r.status, r.sessionId }).ToList();
    }

    /// <param name="validateOnly">Run every check and return errors and warnings without booking (used by the waitlist to show all warnings at once).</param>
    public async Task<BookingResult> BookAsync(NewBooking r, string user, CancellationToken ct = default, bool validateOnly = false)
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
                                 select x.LocationId).ToListAsync(ct);
            var rooms = await LocationNamesAsync(ct);
            if (sameDay.Count > 0) warnings.Add($"This patient is already booked on {s.Date:d MMM} ({string.Join(", ", sameDay.Select(l => rooms.GetValueOrDefault(l, "?")))}).");
        }
        var booked = active.Sum(b => b.DurationMinutes + TurnoverMinutes);
        var available = (int)(s.EndTime - s.StartTime).TotalMinutes;
        if (booked + r.DurationMinutes > available)
            warnings.Add($"The list will run over: {booked + r.DurationMinutes} of {available} minutes booked.");
        if (warnings.Count > 0 && !r.AcceptWarnings) return new(null, errors, warnings);
        if (validateOnly) return new(null, errors, warnings);

        var now = clock.GetUtcNow();
        var booking = new TheatreBooking
        {
            SessionId = s.Id, PatientId = r.PatientId, Order = (active.Count == 0 ? 0 : active.Max(b => b.Order)) + 1, Procedure = r.Procedure.Trim(),
            Laterality = r.Laterality, DurationMinutes = r.DurationMinutes, Anaesthetic = r.Anaesthetic, Notes = Blank(r.Notes),
            Payment = r.Payment, IsTentative = r.IsTentative, StatusAt = now, CreatedAt = now, CreatedBy = user
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "TheatreBooking", booking.Id.ToString(), r.PatientId.ToString(),
            reason: $"list {s.Id} {s.Date:yyyy-MM-dd}: {booking.Procedure}{(warnings.Count > 0 ? " (warnings accepted: " + string.Join("; ", warnings) + ")" : "")}", ct: ct);
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

    /// <summary>Pre-booking checks ticked on a booking (TeamUp "Check List").</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> ChecklistItems =
    [
        ("consent", "Consent form signed"), ("form", "Pre-admission questionnaire done"), ("estimate", "Estimate accepted / deposit paid"),
        ("approval", "Insurer or ACC approval"), ("tests", "Bloods / tests done"), ("anaesthetic", "Anaesthetic review"), ("adult", "Responsible adult confirmed"),
    ];


    public async Task<string?> UpdateDetailsAsync(Guid id, BookingDetails d, string user, CancellationToken ct = default)
    {
        var b = await db.Bookings.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (b is null) return "Booking not found.";
        if ((d.TheatreIn is null) != (d.TheatreOut is null) && d.TheatreOut is not null) return "Enter the time into theatre as well as the time out.";
        if (d.TheatreIn is { } tin && d.TheatreOut is { } tout && tout <= tin) return "Time out of theatre must be after time in.";
        var unknown = (d.Checklist ?? []).Where(k => ChecklistItems.All(c => c.Key != k)).ToList();
        if (unknown.Count > 0) return "Unknown checklist item.";
        var changes = new List<string>();
        void Track(string name, object? a, object? c) { if (!Equals(a, c)) changes.Add(name); }
        var wanted = (d.Checklist ?? []).Distinct().OrderBy(k => k).ToList();
        var checklist = string.Join(",", wanted);
        Track("tentative", b.IsTentative, d.IsTentative); Track("payment", b.Payment, d.Payment); Track("deposit", b.DepositNote, Blank(d.DepositNote));
        Track("checklist", string.Join(",", b.Checks.Select(c => c.ItemKey).OrderBy(k => k)), checklist); Track("billed", b.BilledExternally, d.BilledExternally); Track("theatre times", (b.TheatreIn, b.TheatreOut), (d.TheatreIn, d.TheatreOut));
        Track("trays", (b.TraysNeeded, b.TraysUsed), (Blank(d.TraysNeeded), Blank(d.TraysUsed))); Track("loan items", b.LoanItems, Blank(d.LoanItems));
        Track("scope", b.ScopeId, Blank(d.ScopeId)); Track("notes", b.Notes, Blank(d.Notes));
        b.IsTentative = d.IsTentative; b.Payment = d.Payment; b.DepositNote = Blank(d.DepositNote);
        foreach (var gone in b.Checks.Where(c => !wanted.Contains(c.ItemKey)).ToList()) { b.Checks.Remove(gone); db.Checks.Remove(gone); }
        foreach (var k in wanted.Where(k => b.Checks.All(c => c.ItemKey != k))) db.Checks.Add(new BookingCheck { BookingId = b.Id, ItemKey = k, CheckedAt = clock.GetUtcNow(), CheckedBy = user });
        b.BilledExternally = d.BilledExternally; b.TheatreIn = d.TheatreIn; b.TheatreOut = d.TheatreOut; b.TraysNeeded = Blank(d.TraysNeeded); b.TraysUsed = Blank(d.TraysUsed);
        b.LoanItems = Blank(d.LoanItems); b.ScopeId = Blank(d.ScopeId)?.ToUpperInvariant(); b.Notes = Blank(d.Notes);
        if (changes.Count == 0) return null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "UpdateDetails", "TheatreBooking", b.Id.ToString(), b.PatientId.ToString(), reason: string.Join(", ", changes), ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ clinic appointments

    public async Task<(Appointment? Appointment, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> BookAppointmentAsync(NewAppointment a, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!await patients.Patients.AnyAsync(p => p.Id == a.PatientId, ct)) errors.Add("Choose the patient.");
        if (await ClinicianProblemAsync(a.ClinicianId, true, "clinician", ct) is { } ce) errors.Add(ce);
        if (a.DurationMinutes is < 5 or > 240) errors.Add("Enter a length between 5 and 240 minutes.");
        if (a.StartTime.AddMinutes(a.DurationMinutes) < a.StartTime) errors.Add("The appointment must finish on the same day.");
        if (errors.Count > 0) return (null, errors, warnings);
        var end = a.StartTime.AddMinutes(a.DurationMinutes);
        var clinician = (await ClinicianNamesAsync(ct)).GetValueOrDefault(a.ClinicianId, "The clinician");
        var sameClinician = await db.Appointments.AsNoTracking().Where(x => x.Date == a.Date && x.ClinicianId == a.ClinicianId && x.Status != AppointmentStatus.Cancelled
            && x.Status != AppointmentStatus.DidNotAttend).ToListAsync(ct);
        if (sameClinician.Any(x => x.StartTime < end && a.StartTime < x.StartTime.AddMinutes(x.DurationMinutes))) warnings.Add($"{clinician} already has an appointment at that time.");
        var lists = await db.Sessions.AsNoTracking().Where(s => s.Date == a.Date && !s.IsCancelled && (s.SurgeonId == a.ClinicianId || s.AnaesthetistId == a.ClinicianId)).ToListAsync(ct);
        if (lists.Any(s => s.StartTime < end && a.StartTime < s.EndTime)) warnings.Add($"{clinician} has a theatre list at that time.");
        if (a.Date < DateOnly.FromDateTime(clock.GetLocalNow().DateTime)) warnings.Add("This date is in the past.");
        if (warnings.Count > 0 && !a.AcceptWarnings) return (null, errors, warnings);
        var row = new Appointment
        {
            PatientId = a.PatientId, Date = a.Date, StartTime = a.StartTime, DurationMinutes = a.DurationMinutes, Kind = a.Kind, ClinicianId = a.ClinicianId, LocationId = a.LocationId,
            NoCharge = a.NoCharge, IsTentative = a.IsTentative, Notes = Blank(a.Notes), CreatedAt = clock.GetUtcNow(), CreatedBy = user
        };
        db.Appointments.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Appointment", row.Id.ToString(), row.PatientId.ToString(),
            reason: $"{row.Kind} {row.Date:yyyy-MM-dd} {row.StartTime:HH\\:mm} with {clinician}{(row.NoCharge ? " (no charge)" : "")}", ct: ct);
        return (row, errors, warnings);
    }

    public async Task<string?> UpdateAppointmentAsync(Guid id, AppointmentStatus status, string? reason, bool isTentative, string? notes, string user, CancellationToken ct = default)
    {
        var a = await db.Appointments.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return "Appointment not found.";
        if (status == AppointmentStatus.Cancelled && a.Status != AppointmentStatus.Cancelled && string.IsNullOrWhiteSpace(reason)) return "Enter the reason for cancelling.";
        var from = a.Status;
        a.Status = status; a.IsTentative = isTentative; a.Notes = Blank(notes);
        if (status == AppointmentStatus.Cancelled && reason is not null) a.CancelReason = reason.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "Appointment", a.Id.ToString(), a.PatientId.ToString(), reason: $"{from} -> {status}{(reason is null ? "" : ": " + reason)}", ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<AppointmentView>> AppointmentsAsync(DateOnly from, DateOnly to, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Appointments.AsNoTracking().AsQueryable();
        q = patientId is { } pid ? q.Where(a => a.PatientId == pid) : q.Where(a => a.Date >= from && a.Date <= to);
        var list = (await q.ToListAsync(ct)).OrderBy(a => a.Date).ThenBy(a => a.StartTime).ToList();
        var ids = list.Select(a => a.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var rooms = await LocationNamesAsync(ct);
        var clinicians = await ClinicianNamesAsync(ct);
        return list.Select(a =>
        {
            var p = people.GetValueOrDefault(a.PatientId);
            return new AppointmentView(a.Id, a.PatientId, p is null ? "(unknown)" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, p?.Mrn ?? "", a.Date, a.StartTime,
                a.StartTime.AddMinutes(a.DurationMinutes), a.DurationMinutes, a.Kind.ToString(), a.ClinicianId, clinicians.GetValueOrDefault(a.ClinicianId, "?"), a.LocationId is { } l ? rooms.GetValueOrDefault(l) : null, a.NoCharge,
                a.IsTentative, a.Status.ToString(), a.CancelReason, a.Notes);
        }).ToList();
    }

    private static SessionView View(TheatreSession s, Dictionary<Guid, Patient> people, Dictionary<Guid, string> clinicians, Dictionary<Guid, string> rooms)
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
                b.CancelReason, b.Notes, start, end, b.IsTentative, b.Payment.ToString(), b.DepositNote, b.Checks.Select(c => c.ItemKey).OrderBy(k => k).ToList(), b.BilledExternally, b.TheatreIn, b.TheatreOut,
                b.TraysNeeded, b.TraysUsed, b.LoanItems, b.ScopeId, p?.MobilePhone ?? p?.HomePhone));
        }
        var booked = s.Bookings.Where(b => b.Status is not (BookingStatus.Cancelled or BookingStatus.DidNotAttend)).Sum(b => b.DurationMinutes + TurnoverMinutes);
        var available = (int)(s.EndTime - s.StartTime).TotalMinutes;
        return new SessionView(s.Id, s.LocationId, rooms.GetValueOrDefault(s.LocationId, "?"), s.Date, s.StartTime, s.EndTime, s.Type.ToString(), s.SurgeonId,
            clinicians.GetValueOrDefault(s.SurgeonId, "?"), s.AnaesthetistId, s.AnaesthetistId is { } an ? clinicians.GetValueOrDefault(an) : null, s.Notes,
            s.IsCancelled, booked, available, booked > available + TurnoverMinutes, list);
    }

    public static string Label(BookingStatus s) => s switch
    {
        BookingStatus.InTheatre => "In theatre", BookingStatus.DidNotAttend => "Did not attend", _ => s.ToString()
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
