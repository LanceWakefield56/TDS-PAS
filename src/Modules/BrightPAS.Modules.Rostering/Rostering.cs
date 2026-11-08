using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Rostering;

public sealed class RosteringModule : IPasModule
{
    public const string Id = "rostering";
    public ModuleManifest Manifest { get; } = new(Id, "Rostering", typeof(RosteringModule).Assembly.GetName().Version ?? new Version(0, 0),
        RosterDbContext.Schema, [], "Staff rosters: shifts, regular shifts, leave and hours (replaces TeamUp).");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<RosterDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, RosterDbContext.Schema));
        services.AddScoped<RosterService>();
    }
}

public enum StaffRole { Nurse = 1, AnaestheticTechnician = 2, HealthCareAssistant = 3, Reception = 4, Cssd = 5, Administration = 6, Manager = 7, Other = 8 }
public enum LeaveType { Annual = 1, Sick = 2, Training = 3, Parental = 4, Other = 5 }
public enum LeaveStatus { Requested = 1, Approved = 2, Declined = 3, Cancelled = 4 }

public sealed class StaffMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public StaffRole Role { get; set; }
    /// <summary>Optional link to a BrightPAS account (so staff can see their own shifts and request leave).</summary>
    public string? UserName { get; set; }
    public int Colour { get; set; }
    public decimal? ContractedHours { get; set; }
    public string? Phone { get; set; }
    /// <summary>Employee identifier used by payroll (Xero employee / payroll number).</summary>
    public string? PayrollCode { get; set; }
    /// <summary>Xero earnings rate name for rostered hours (default "Ordinary Hours").</summary>
    public string? EarningsRate { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum SwapStatus { Requested = 1, Accepted = 2, Approved = 3, Declined = 4, Cancelled = 5 }

/// <summary>A shift swap or give-away: requested by the rostered person, accepted by a colleague, approved by the roster manager.</summary>
public sealed class SwapRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public Guid FromStaffId { get; set; }
    /// <summary>Colleague asked to take the shift; null = offered to anyone.</summary>
    public Guid? ToStaffId { get; set; }
    /// <summary>For a straight swap: the colleague's shift that comes back in exchange.</summary>
    public Guid? OfferedShiftId { get; set; }
    public SwapStatus Status { get; set; } = SwapStatus.Requested;
    public string? Note { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? AcceptedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>A shift, or an all-day entry (e.g. office day, study day). Tentative shifts are shown striped.</summary>
public sealed class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffId { get; set; }
    public DateOnly Date { get; set; }
    public bool AllDay { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    public int BreakMinutes { get; set; }
    /// <summary>Where or what: Theatre, Recovery, Endoscopy, CSSD, Reception, Office…</summary>
    public string? Area { get; set; }
    public bool IsTentative { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class ShiftTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool AllDay { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    public int BreakMinutes { get; set; }
    public string? Area { get; set; }
    public int EveryWeeks { get; set; } = 1;
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Leave
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StaffId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public LeaveType Type { get; set; }
    public LeaveStatus Status { get; set; } = LeaveStatus.Requested;
    public string? Note { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string RequestedBy { get; set; } = "";
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
}

public sealed class RosterDbContext(DbContextOptions<RosterDbContext> options) : DbContext(options)
{
    public const string Schema = "roster";
    public DbSet<StaffMember> Staff => Set<StaffMember>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftTemplate> Templates => Set<ShiftTemplate>();
    public DbSet<Leave> Leave => Set<Leave>();
    public DbSet<SwapRequest> Swaps => Set<SwapRequest>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<StaffMember>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.UserName).HasMaxLength(100);
            e.Property(x => x.ContractedHours).HasPrecision(5, 2);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.PayrollCode).HasMaxLength(50);
            e.Property(x => x.EarningsRate).HasMaxLength(80);
        });
        b.Entity<SwapRequest>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.Property(x => x.AcceptedBy).HasMaxLength(100);
            e.Property(x => x.DecidedBy).HasMaxLength(100);
            e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.HasIndex(x => new { x.Status, x.ShiftId });
        });
        b.Entity<Shift>(e =>
        {
            e.Property(x => x.Area).HasMaxLength(50);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.Date, x.StaffId });
        });
        b.Entity<ShiftTemplate>(e =>
        {
            e.Property(x => x.DayOfWeek).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Area).HasMaxLength(50);
        });
        b.Entity<Leave>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.Property(x => x.DecidedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.StaffId, x.From });
        });
    }
}

public sealed class RosterDbContextFactory : IDesignTimeDbContextFactory<RosterDbContext>
{
    public RosterDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<RosterDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", RosterDbContext.Schema)).Options);
}

public sealed record StaffEdit(Guid? Id, string Name, StaffRole Role, string? UserName, int? Colour, decimal? ContractedHours, string? Phone, bool IsActive,
    string? PayrollCode = null, string? EarningsRate = null);
public sealed record SwapCreate(Guid ShiftId, Guid? ToStaffId, Guid? OfferedShiftId, string? Note);
public sealed record SwapView(Guid Id, Guid ShiftId, string ShiftText, Guid FromStaffId, string FromName, Guid? ToStaffId, string? ToName, Guid? OfferedShiftId, string? OfferedText,
    string Status, string? Note, DateTimeOffset RequestedAt, string RequestedBy, string? AcceptedBy, string? DecidedBy, string? DecisionNote);
public sealed record ShiftEdit(Guid? Id, Guid StaffId, DateOnly Date, bool AllDay, TimeOnly? Start, TimeOnly? End, int BreakMinutes, string? Area, bool IsTentative,
    string? Note, bool AcceptWarnings = false);
public sealed record ShiftTemplateEdit(Guid? Id, Guid StaffId, DayOfWeek DayOfWeek, bool AllDay, TimeOnly? Start, TimeOnly? End, int BreakMinutes, string? Area,
    int EveryWeeks, DateOnly StartsOn, DateOnly? EndsOn, bool IsActive);
public sealed record LeaveRequest(Guid StaffId, DateOnly From, DateOnly To, LeaveType Type, string? Note);
public sealed record ShiftResult(Shift? Shift, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed record ShiftView(Guid Id, Guid StaffId, string StaffName, string Role, int Colour, DateOnly Date, bool AllDay, TimeOnly? Start, TimeOnly? End,
    int BreakMinutes, decimal Hours, string? Area, bool IsTentative, string? Note, bool OnLeave);
public sealed record LeaveView(Guid Id, Guid StaffId, string StaffName, DateOnly From, DateOnly To, string Type, string Status, string? Note,
    DateTimeOffset RequestedAt, string RequestedBy, string? DecidedBy);
public sealed record HoursRow(Guid StaffId, string StaffName, string Role, decimal Rostered, decimal? Contracted, int Shifts, int LeaveDays);

/// <summary>Staff roster: staff list, shifts and all-day entries, regular shifts, leave requests and approval, and rostered hours.</summary>
public sealed class RosterService(RosterDbContext db, AuditWriter audit, TimeProvider clock)
{
    public static readonly string[] Areas = ["Theatre", "Anaesthetics", "Recovery", "Endoscopy", "CSSD", "Reception", "Office", "On call"];

    public async Task<IReadOnlyList<StaffMember>> StaffAsync(bool activeOnly, CancellationToken ct = default) =>
        (await db.Staff.AsNoTracking().Where(s => !activeOnly || s.IsActive).ToListAsync(ct)).OrderBy(s => s.Role).ThenBy(s => s.Name).ToList();

    public async Task<(StaffMember? Staff, IReadOnlyList<string> Errors)> SaveStaffAsync(StaffEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var name = e.Name?.Trim() ?? "";
        if (name.Length < 2) errors.Add("Enter the staff member's name.");
        if (e.ContractedHours is < 0 or > 80) errors.Add("Contracted hours must be between 0 and 80 a week.");
        if (await db.Staff.AnyAsync(x => x.Name == name && x.Id != e.Id, ct)) errors.Add($"{name} is already on the staff list.");
        if (errors.Count > 0) return (null, errors);
        StaffMember row;
        if (e.Id is { } id) row = await db.Staff.SingleAsync(x => x.Id == id, ct);
        else
        {
            var used = await db.Staff.Select(x => x.Colour).ToListAsync(ct);
            row = new StaffMember { Colour = Enumerable.Range(0, 12).FirstOrDefault(i => !used.Contains(i), used.Count % 12) };
            db.Staff.Add(row);
        }
        row.Name = name; row.Role = e.Role; row.UserName = string.IsNullOrWhiteSpace(e.UserName) ? null : e.UserName.Trim();
        if (e.Colour is >= 0 and < 12) row.Colour = e.Colour.Value;
        row.ContractedHours = e.ContractedHours; row.Phone = string.IsNullOrWhiteSpace(e.Phone) ? null : e.Phone.Trim(); row.IsActive = e.IsActive;
        row.PayrollCode = string.IsNullOrWhiteSpace(e.PayrollCode) ? null : e.PayrollCode.Trim(); row.EarningsRate = string.IsNullOrWhiteSpace(e.EarningsRate) ? null : e.EarningsRate.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "StaffMember", row.Id.ToString(), reason: $"{row.Name} ({row.Role}){(row.IsActive ? "" : " inactive")}", ct: ct);
        return (row, errors);
    }

    public static decimal Hours(bool allDay, TimeOnly? start, TimeOnly? end, int breakMinutes) =>
        allDay || start is null || end is null ? 0 : Math.Max(0, Math.Round((decimal)((end.Value - start.Value).TotalMinutes - breakMinutes) / 60m, 2));

    public async Task<ShiftResult> SaveShiftAsync(ShiftEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var staff = await db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == e.StaffId, ct);
        if (staff is null || !staff.IsActive) errors.Add("Choose an active staff member.");
        if (!e.AllDay && (e.Start is null || e.End is null)) errors.Add("Enter the start and finish times, or tick 'All day'.");
        if (!e.AllDay && e.Start is { } s0 && e.End is { } e0 && e0 <= s0) errors.Add("The shift must finish after it starts (overnight shifts: split at midnight).");
        if (e.BreakMinutes is < 0 or > 120) errors.Add("Break must be 0 to 120 minutes.");
        if (errors.Count > 0) return new(null, errors, warnings);
        var others = await db.Shifts.AsNoTracking().Where(x => x.StaffId == e.StaffId && x.Date == e.Date && x.Id != e.Id).ToListAsync(ct);
        if (!e.AllDay && others.Any(x => !x.AllDay && x.Start < e.End && e.Start < x.End))
            return new(null, [$"{staff!.Name} already has a shift that overlaps this time."], warnings);
        var leave = await db.Leave.AsNoTracking().FirstOrDefaultAsync(l => l.StaffId == e.StaffId && l.Status == LeaveStatus.Approved && l.From <= e.Date && l.To >= e.Date, ct);
        if (leave is not null) warnings.Add($"{staff!.Name} is on approved {leave.Type.ToString().ToLowerInvariant()} leave that day.");
        if (staff!.ContractedHours is { } contracted)
        {
            var (weekStart, weekEnd) = Week(e.Date);
            var week = await db.Shifts.AsNoTracking().Where(x => x.StaffId == e.StaffId && x.Date >= weekStart && x.Date <= weekEnd && x.Id != e.Id).ToListAsync(ct);
            var total = week.Sum(x => Hours(x.AllDay, x.Start, x.End, x.BreakMinutes)) + Hours(e.AllDay, e.Start, e.End, e.BreakMinutes);
            if (total > contracted + 8) warnings.Add($"{staff.Name} would be rostered {total:0.##} hours that week (contracted {contracted:0.##}).");
        }
        if (warnings.Count > 0 && !e.AcceptWarnings) return new(null, errors, warnings);
        Shift row;
        if (e.Id is { } id) row = await db.Shifts.SingleAsync(x => x.Id == id, ct);
        else { row = new Shift { CreatedAt = clock.GetUtcNow(), CreatedBy = user }; db.Shifts.Add(row); }
        row.StaffId = e.StaffId; row.Date = e.Date; row.AllDay = e.AllDay; row.Start = e.AllDay ? null : e.Start; row.End = e.AllDay ? null : e.End;
        row.BreakMinutes = e.AllDay ? 0 : e.BreakMinutes; row.Area = string.IsNullOrWhiteSpace(e.Area) ? null : e.Area.Trim(); row.IsTentative = e.IsTentative;
        row.Note = string.IsNullOrWhiteSpace(e.Note) ? null : e.Note.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "Shift", row.Id.ToString(),
            reason: $"{staff.Name} {row.Date:yyyy-MM-dd} {(row.AllDay ? "all day" : $"{row.Start:HH\\:mm}-{row.End:HH\\:mm}")} {row.Area}{(row.IsTentative ? " (tentative)" : "")}", ct: ct);
        return new(row, errors, warnings);
    }

    public async Task<string?> DeleteShiftAsync(Guid id, string user, CancellationToken ct = default)
    {
        var row = await db.Shifts.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return "Shift not found.";
        db.Shifts.Remove(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "Shift", id.ToString(), reason: $"{row.Date:yyyy-MM-dd} staff {row.StaffId}", ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<ShiftView>> ShiftsAsync(DateOnly from, DateOnly to, Guid? staffId, CancellationToken ct = default)
    {
        var staff = await db.Staff.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var shifts = await db.Shifts.AsNoTracking().Where(x => x.Date >= from && x.Date <= to && (staffId == null || x.StaffId == staffId)).ToListAsync(ct);
        var leave = await db.Leave.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.From <= to && l.To >= from).ToListAsync(ct);
        return shifts.OrderBy(x => x.Date).ThenBy(x => x.AllDay ? 0 : 1).ThenBy(x => x.Start).Select(x =>
        {
            var s = staff.GetValueOrDefault(x.StaffId);
            return new ShiftView(x.Id, x.StaffId, s?.Name ?? "?", s?.Role.ToString() ?? "", s?.Colour ?? 0, x.Date, x.AllDay, x.Start, x.End, x.BreakMinutes,
                Hours(x.AllDay, x.Start, x.End, x.BreakMinutes), x.Area, x.IsTentative, x.Note, leave.Any(l => l.StaffId == x.StaffId && l.From <= x.Date && l.To >= x.Date));
        }).ToList();
    }

    public static (DateOnly Start, DateOnly End) Week(DateOnly d)
    {
        var offset = ((int)d.DayOfWeek + 6) % 7; // Monday-based week
        var start = d.AddDays(-offset);
        return (start, start.AddDays(6));
    }

    public async Task<IReadOnlyList<HoursRow>> HoursAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var staff = await StaffAsync(activeOnly: true, ct);
        var shifts = await db.Shifts.AsNoTracking().Where(x => x.Date >= from && x.Date <= to).ToListAsync(ct);
        var leave = await db.Leave.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.From <= to && l.To >= from).ToListAsync(ct);
        var weeks = Math.Max(1m, (to.DayNumber - from.DayNumber + 1) / 7m);
        return staff.Select(s =>
        {
            var mine = shifts.Where(x => x.StaffId == s.Id).ToList();
            var leaveDays = leave.Where(l => l.StaffId == s.Id).Sum(l => Math.Min(l.To.DayNumber, to.DayNumber) - Math.Max(l.From.DayNumber, from.DayNumber) + 1);
            return new HoursRow(s.Id, s.Name, s.Role.ToString(), mine.Sum(x => Hours(x.AllDay, x.Start, x.End, x.BreakMinutes)),
                s.ContractedHours is { } c ? Math.Round(c * weeks, 2) : null, mine.Count(x => !x.AllDay), leaveDays);
        }).ToList();
    }

    // ------------------------------------------------------------------ regular shifts

    public async Task<IReadOnlyList<ShiftTemplate>> TemplatesAsync(CancellationToken ct = default) =>
        (await db.Templates.AsNoTracking().ToListAsync(ct)).OrderBy(t => t.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)t.DayOfWeek).ThenBy(t => t.Start).ToList();

    public async Task<IReadOnlyList<string>> SaveTemplateAsync(ShiftTemplateEdit t, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await db.Staff.AnyAsync(s => s.Id == t.StaffId, ct)) errors.Add("Choose the staff member.");
        if (!t.AllDay && (t.Start is null || t.End is null || t.End <= t.Start)) errors.Add("Enter a start and a later finish time, or tick 'All day'.");
        if (t.EveryWeeks is < 1 or > 8) errors.Add("Repeat every 1 to 8 weeks.");
        if (errors.Count > 0) return errors;
        ShiftTemplate row;
        if (t.Id is { } id) row = await db.Templates.SingleAsync(x => x.Id == id, ct);
        else { row = new ShiftTemplate(); db.Templates.Add(row); }
        row.StaffId = t.StaffId; row.DayOfWeek = t.DayOfWeek; row.AllDay = t.AllDay; row.Start = t.AllDay ? null : t.Start; row.End = t.AllDay ? null : t.End;
        row.BreakMinutes = t.BreakMinutes; row.Area = string.IsNullOrWhiteSpace(t.Area) ? null : t.Area.Trim(); row.EveryWeeks = t.EveryWeeks; row.StartsOn = t.StartsOn;
        row.EndsOn = t.EndsOn; row.IsActive = t.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, t.Id is null ? "Create" : "Update", "ShiftTemplate", row.Id.ToString(), reason: $"{row.DayOfWeek} every {row.EveryWeeks} week(s)", ct: ct);
        return errors;
    }

    /// <summary>Creates shifts from active regular shifts for a date range (up to 13 weeks). Skips days that already have an overlapping shift or approved leave.</summary>
    public async Task<(int Created, IReadOnlyList<string> Skipped)> GenerateAsync(DateOnly from, DateOnly to, string user, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 91) return (0, ["Choose a range of up to 13 weeks."]);
        var templates = await db.Templates.AsNoTracking().Where(t => t.IsActive).ToListAsync(ct);
        var staff = await db.Staff.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var existing = await db.Shifts.AsNoTracking().Where(x => x.Date >= from && x.Date <= to).ToListAsync(ct);
        var leave = await db.Leave.AsNoTracking().Where(l => l.Status == LeaveStatus.Approved && l.From <= to && l.To >= from).ToListAsync(ct);
        var created = 0;
        var skipped = new List<string>();
        for (var d = from; d <= to; d = d.AddDays(1))
            foreach (var t in templates.Where(t => t.DayOfWeek == d.DayOfWeek && d >= t.StartsOn && (t.EndsOn is null || d <= t.EndsOn) && ((d.DayNumber - t.StartsOn.DayNumber) / 7) % t.EveryWeeks == 0))
            {
                var s = staff.GetValueOrDefault(t.StaffId);
                if (s is null || !s.IsActive) continue;
                if (leave.Any(l => l.StaffId == t.StaffId && l.From <= d && l.To >= d)) { skipped.Add($"{d:ddd d MMM}: {s.Name} is on leave."); continue; }
                var clash = existing.Any(x => x.StaffId == t.StaffId && x.Date == d && (x.AllDay == t.AllDay && (t.AllDay || (x.Start < t.End && t.Start < x.End))));
                if (clash) { skipped.Add($"{d:ddd d MMM}: {s.Name} already rostered."); continue; }
                var shift = new Shift { StaffId = t.StaffId, Date = d, AllDay = t.AllDay, Start = t.Start, End = t.End, BreakMinutes = t.BreakMinutes, Area = t.Area,
                    CreatedAt = clock.GetUtcNow(), CreatedBy = user };
                db.Shifts.Add(shift);
                existing.Add(shift);
                created++;
            }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Generate", "Shift", $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}", reason: $"{created} shift(s) from regular shifts, {skipped.Count} skipped", ct: ct);
        return (created, skipped);
    }

    // ------------------------------------------------------------------ leave

    public async Task<(Leave? Leave, IReadOnlyList<string> Errors)> RequestLeaveAsync(LeaveRequest r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await db.Staff.AnyAsync(s => s.Id == r.StaffId && s.IsActive, ct)) errors.Add("Choose the staff member.");
        if (r.To < r.From) errors.Add("The last day must be on or after the first day.");
        if (r.To.DayNumber - r.From.DayNumber > 365) errors.Add("Leave requests are limited to one year.");
        if (await db.Leave.AnyAsync(l => l.StaffId == r.StaffId && (l.Status == LeaveStatus.Requested || l.Status == LeaveStatus.Approved) && l.From <= r.To && l.To >= r.From, ct))
            errors.Add("This overlaps leave already requested or approved.");
        if (errors.Count > 0) return (null, errors);
        var row = new Leave { StaffId = r.StaffId, From = r.From, To = r.To, Type = r.Type, Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim(), RequestedAt = clock.GetUtcNow(), RequestedBy = user };
        db.Leave.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Request", "Leave", row.Id.ToString(), reason: $"{row.Type} {row.From:yyyy-MM-dd}..{row.To:yyyy-MM-dd}", ct: ct);
        return (row, errors);
    }

    public async Task<(string? Error, int ShiftsAffected)> DecideLeaveAsync(Guid id, LeaveStatus decision, string user, CancellationToken ct = default)
    {
        var l = await db.Leave.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return ("Leave not found.", 0);
        if (decision is not (LeaveStatus.Approved or LeaveStatus.Declined or LeaveStatus.Cancelled)) return ("Choose approve, decline or cancel.", 0);
        if (l.Status is LeaveStatus.Declined or LeaveStatus.Cancelled) return ("This request is already closed.", 0);
        l.Status = decision; l.DecidedAt = clock.GetUtcNow(); l.DecidedBy = user;
        await db.SaveChangesAsync(ct);
        var affected = decision == LeaveStatus.Approved ? await db.Shifts.CountAsync(s => s.StaffId == l.StaffId && s.Date >= l.From && s.Date <= l.To, ct) : 0;
        await audit.WriteAsync(user, decision.ToString(), "Leave", l.Id.ToString(), reason: $"{l.From:yyyy-MM-dd}..{l.To:yyyy-MM-dd}{(affected > 0 ? $"; {affected} rostered shift(s) need cover" : "")}", ct: ct);
        return (null, affected);
    }

    public async Task<IReadOnlyList<LeaveView>> LeaveAsync(string view, Guid? staffId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var q = db.Leave.AsNoTracking().AsQueryable();
        if (staffId is { } sid) q = q.Where(l => l.StaffId == sid);
        q = view switch
        {
            "requested" => q.Where(l => l.Status == LeaveStatus.Requested),
            "upcoming" => q.Where(l => l.Status == LeaveStatus.Approved && l.To >= today),
            _ => q.Where(l => l.To >= today.AddDays(-90)),
        };
        var staff = await db.Staff.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return (await q.ToListAsync(ct)).OrderBy(l => l.From).Select(l => new LeaveView(l.Id, l.StaffId, staff.GetValueOrDefault(l.StaffId, "?"), l.From, l.To, l.Type.ToString(),
            l.Status.ToString(), l.Note, l.RequestedAt, l.RequestedBy, l.DecidedBy)).ToList();
    }

    public async Task<StaffMember?> StaffForUserAsync(string userName, CancellationToken ct = default) =>
        await db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserName == userName && s.IsActive, ct);

    // ------------------------------------------------------------------ shift swaps (0.10.0)

    private static string ShiftText(Shift s) => $"{s.Date:ddd d MMM} {(s.AllDay ? "all day" : $"{s.Start:HH\\:mm}–{s.End:HH\\:mm}")}{(s.Area is null ? "" : " " + s.Area)}";

    /// <summary>Checks the person can work the shift: no overlapping shift that day and not on approved leave.</summary>
    private async Task<string?> AvailableAsync(Guid staffId, Shift shift, Guid? ignoreShiftId, CancellationToken ct)
    {
        var staff = await db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == staffId, ct);
        if (staff is null || !staff.IsActive) return "That person is not on the active staff list.";
        var same = await db.Shifts.AsNoTracking().Where(x => x.StaffId == staffId && x.Date == shift.Date && x.Id != shift.Id && x.Id != ignoreShiftId).ToListAsync(ct);
        if (!shift.AllDay && same.Any(x => !x.AllDay && x.Start < shift.End && shift.Start < x.End)) return $"{staff.Name} already has an overlapping shift on {shift.Date:ddd d MMM}.";
        if (await db.Leave.AnyAsync(l => l.StaffId == staffId && l.Status == LeaveStatus.Approved && l.From <= shift.Date && l.To >= shift.Date, ct)) return $"{staff.Name} is on leave on {shift.Date:ddd d MMM}.";
        return null;
    }

    public async Task<(SwapRequest? Swap, IReadOnlyList<string> Errors)> RequestSwapAsync(SwapCreate r, Guid? actingStaffId, string user, CancellationToken ct = default)
    {
        var shift = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(s => s.Id == r.ShiftId, ct);
        if (shift is null) return (null, ["Shift not found."]);
        if (actingStaffId is { } me && shift.StaffId != me) return (null, ["You can only offer your own shifts."]);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (shift.Date < today) return (null, ["That shift has already happened."]);
        if (r.ToStaffId == shift.StaffId) return (null, ["Choose a colleague, not the person already rostered."]);
        if (await db.Swaps.AnyAsync(x => x.ShiftId == r.ShiftId && (x.Status == SwapStatus.Requested || x.Status == SwapStatus.Accepted), ct))
            return (null, ["This shift already has a swap request in progress."]);
        Shift? offered = null;
        if (r.OfferedShiftId is { } oid)
        {
            offered = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(s => s.Id == oid, ct);
            if (offered is null || r.ToStaffId is null || offered.StaffId != r.ToStaffId) return (null, ["The shift offered in exchange must belong to the colleague you chose."]);
            if (offered.Date < today) return (null, ["The shift offered in exchange has already happened."]);
        }
        if (r.ToStaffId is { } to && await AvailableAsync(to, shift, offered?.Id, ct) is { } e1) return (null, [e1]);
        if (offered is not null && await AvailableAsync(shift.StaffId, offered, shift.Id, ct) is { } e2) return (null, [e2]);
        var row = new SwapRequest { ShiftId = shift.Id, FromStaffId = shift.StaffId, ToStaffId = r.ToStaffId, OfferedShiftId = r.OfferedShiftId,
            Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim(), RequestedAt = clock.GetUtcNow(), RequestedBy = user };
        db.Swaps.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "SwapRequested", "Shift", shift.Id.ToString(), reason: $"{ShiftText(shift)}{(offered is null ? "" : " for " + ShiftText(offered))}", ct: ct);
        return (row, []);
    }

    /// <summary>A colleague accepts (actingStaffId = their staff record). Open offers are taken by whoever accepts first.</summary>
    public async Task<string?> AcceptSwapAsync(Guid id, Guid actingStaffId, string user, CancellationToken ct = default)
    {
        var sw = await db.Swaps.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (sw is null) return "Swap request not found.";
        if (sw.Status != SwapStatus.Requested) return "This request is no longer open.";
        if (sw.FromStaffId == actingStaffId) return "You cannot accept your own request.";
        if (sw.ToStaffId is { } to && to != actingStaffId) return "This request was sent to someone else.";
        var shift = await db.Shifts.AsNoTracking().SingleAsync(s => s.Id == sw.ShiftId, ct);
        if (await AvailableAsync(actingStaffId, shift, sw.OfferedShiftId, ct) is { } e) return e;
        sw.ToStaffId = actingStaffId; sw.Status = SwapStatus.Accepted; sw.AcceptedAt = clock.GetUtcNow(); sw.AcceptedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "SwapAccepted", "Shift", sw.ShiftId.ToString(), reason: ShiftText(shift), ct: ct);
        return null;
    }

    /// <summary>Roster manager approves (shifts change hands) or declines; the requester can cancel; the colleague can decline.</summary>
    public async Task<string?> DecideSwapAsync(Guid id, SwapStatus decision, string? note, bool manager, Guid? actingStaffId, string user, CancellationToken ct = default)
    {
        var sw = await db.Swaps.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (sw is null) return "Swap request not found.";
        if (sw.Status is not (SwapStatus.Requested or SwapStatus.Accepted)) return "This request is already closed.";
        switch (decision)
        {
            case SwapStatus.Approved:
                if (!manager) return "Only the roster manager can approve swaps.";
                if (sw.Status != SwapStatus.Accepted || sw.ToStaffId is null) return "A colleague must accept the swap before it can be approved.";
                var shift = await db.Shifts.SingleOrDefaultAsync(s => s.Id == sw.ShiftId, ct);
                if (shift is null || shift.StaffId != sw.FromStaffId) return "The shift has changed since the request – ask for a new swap.";
                Shift? offered = null;
                if (sw.OfferedShiftId is { } oid)
                {
                    offered = await db.Shifts.SingleOrDefaultAsync(s => s.Id == oid, ct);
                    if (offered is null || offered.StaffId != sw.ToStaffId) return "The exchanged shift has changed since the request – ask for a new swap.";
                }
                if (await AvailableAsync(sw.ToStaffId.Value, shift, offered?.Id, ct) is { } e1) return e1;
                if (offered is not null && await AvailableAsync(sw.FromStaffId, offered, shift.Id, ct) is { } e2) return e2;
                shift.StaffId = sw.ToStaffId.Value;
                shift.Note = Trunc($"Swapped from {(await db.Staff.AsNoTracking().SingleAsync(s => s.Id == sw.FromStaffId, ct)).Name}{(shift.Note is null ? "" : "; " + shift.Note)}", 300);
                if (offered is not null) offered.StaffId = sw.FromStaffId;
                break;
            case SwapStatus.Declined:
                if (!manager && actingStaffId != sw.ToStaffId) return "Only the colleague asked or the roster manager can decline.";
                break;
            case SwapStatus.Cancelled:
                if (!manager && actingStaffId != sw.FromStaffId) return "Only the person who asked can cancel.";
                break;
            default: return "Choose approve, decline or cancel.";
        }
        sw.Status = decision; sw.DecidedAt = clock.GetUtcNow(); sw.DecidedBy = user; sw.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : Trunc(note.Trim(), 300);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Swap" + decision, "Shift", sw.ShiftId.ToString(), reason: sw.DecisionNote, ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<SwapView>> SwapsAsync(bool openOnly, Guid? staffId, CancellationToken ct = default)
    {
        var q = db.Swaps.AsNoTracking().Where(x => !openOnly || x.Status == SwapStatus.Requested || x.Status == SwapStatus.Accepted);
        if (staffId is { } sid) q = q.Where(x => x.FromStaffId == sid || x.ToStaffId == sid || (x.ToStaffId == null && x.Status == SwapStatus.Requested));
        var rows = (await q.ToListAsync(ct)).OrderByDescending(x => x.RequestedAt).Take(200).ToList();
        var ids = rows.Select(r => r.ShiftId).Concat(rows.Where(r => r.OfferedShiftId != null).Select(r => r.OfferedShiftId!.Value)).Distinct().ToList();
        var shifts = await db.Shifts.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var staff = await db.Staff.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return rows.Select(r => new SwapView(r.Id, r.ShiftId, shifts.TryGetValue(r.ShiftId, out var sh) ? ShiftText(sh) : "(removed)", r.FromStaffId, staff.GetValueOrDefault(r.FromStaffId, "?"),
            r.ToStaffId, r.ToStaffId is { } t ? staff.GetValueOrDefault(t, "?") : null, r.OfferedShiftId,
            r.OfferedShiftId is { } o && shifts.TryGetValue(o, out var os) ? ShiftText(os) : null, r.Status.ToString(), r.Note, r.RequestedAt, r.RequestedBy, r.AcceptedBy, r.DecidedBy, r.DecisionNote)).ToList();
    }

    // ------------------------------------------------------------------ payroll export (0.10.0)

    public const string DefaultEarningsRate = "Ordinary Hours";

    /// <summary>
    /// Timesheet for payroll (Xero): one row per staff member and earnings rate with hours per day, plus a detail sheet.
    /// Confirmed (not tentative) timed shifts only; all-day entries (office days, study days) are listed in the detail with 0 hours.
    /// </summary>
    public async Task<(string Timesheet, string Detail, int People, decimal Hours)> PayrollAsync(DateOnly from, DateOnly to, string user, CancellationToken ct = default)
    {
        var staff = await db.Staff.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var shifts = (await db.Shifts.AsNoTracking().Where(x => x.Date >= from && x.Date <= to && !x.IsTentative).ToListAsync(ct))
            .OrderBy(x => staff.GetValueOrDefault(x.StaffId)?.Name).ThenBy(x => x.Date).ThenBy(x => x.Start).ToList();
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => from.AddDays(i)).ToList();
        var ts = new System.Text.StringBuilder();
        ts.AppendLine(string.Join(",", new[] { "Employee", "Payroll ID", "Earnings Rate" }.Concat(days.Select(d => d.ToString("yyyy-MM-dd"))).Append("Total").Select(Csv)));
        decimal grand = 0; var people = 0;
        foreach (var g in shifts.Where(x => !x.AllDay).GroupBy(x => x.StaffId))
        {
            var s = staff.GetValueOrDefault(g.Key);
            var perDay = days.Select(d => g.Where(x => x.Date == d).Sum(x => Hours(false, x.Start, x.End, x.BreakMinutes))).ToList();
            var total = perDay.Sum();
            if (total == 0) continue;
            grand += total; people++;
            ts.AppendLine(string.Join(",", new[] { s?.Name ?? "?", s?.PayrollCode ?? "", s?.EarningsRate ?? DefaultEarningsRate }
                .Concat(perDay.Select(h => h == 0 ? "" : h.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)))
                .Append(total.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Select(Csv)));
        }
        var dt = new System.Text.StringBuilder();
        dt.AppendLine("Employee,Payroll ID,Date,Start,Finish,Break (min),Hours,Area,All day,Note");
        foreach (var x in shifts)
        {
            var s = staff.GetValueOrDefault(x.StaffId);
            dt.AppendLine(string.Join(",", new[] { s?.Name ?? "?", s?.PayrollCode ?? "", x.Date.ToString("yyyy-MM-dd"), x.Start?.ToString("HH:mm") ?? "", x.End?.ToString("HH:mm") ?? "",
                x.BreakMinutes.ToString(), Hours(x.AllDay, x.Start, x.End, x.BreakMinutes).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), x.Area ?? "", x.AllDay ? "Yes" : "", x.Note ?? "" }.Select(Csv)));
        }
        await audit.WriteAsync(user, "Export", "Payroll", $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}", reason: $"{people} staff, {grand:0.##} hours", ct: ct);
        return (ts.ToString(), dt.ToString(), people, grand);
    }

    /// <summary>CSV cell: quoted, with spreadsheet formula injection neutralised.</summary>
    public static string Csv(string v)
    {
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"") + "\"";
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
