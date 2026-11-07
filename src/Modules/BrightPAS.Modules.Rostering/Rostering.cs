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
    public bool IsActive { get; set; } = true;
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

public sealed record StaffEdit(Guid? Id, string Name, StaffRole Role, string? UserName, int? Colour, decimal? ContractedHours, string? Phone, bool IsActive);
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
}
