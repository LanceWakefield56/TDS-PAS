using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Portal;

public sealed class PortalModule : IPasModule
{
    public const string Id = "portal";
    public ModuleManifest Manifest { get; } = new(Id, "Specialist, PA & patient portals", typeof(PortalModule).Assembly.GetName().Version ?? new Version(0, 0),
        PortalDbContext.Schema, [], "Read-only portal for specialists and their practice assistants; secure patient portal links (appointments, requests, document upload).");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<PortalDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, PortalDbContext.Schema));
        services.AddScoped<PortalService>();
    }
}

/// <summary>A patient's secure portal link (the token is only ever stored as a SHA-256 hash). The patient also confirms their date of birth.</summary>
public sealed class PortalAccess
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public int UploadsToday { get; set; }
    public DateOnly? UploadDay { get; set; }
}

public enum PortalRequestKind { ChangeAppointment = 1, Cancel = 2, UpdateDetails = 3, Question = 4 }
public enum PortalRequestStatus { Open = 1, Done = 2, Declined = 3 }

public sealed class PortalRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid AccessId { get; set; }
    public PortalRequestKind Kind { get; set; }
    public Guid? BookingId { get; set; }
    public string Message { get; set; } = "";
    public PortalRequestStatus Status { get; set; } = PortalRequestStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public string? HandledBy { get; set; }
    /// <summary>Reply shown to the patient in the portal.</summary>
    public string? Response { get; set; }
}

/// <summary>A practice assistant (user account) working for a specialist (clinician register) – sees that specialist's patients in the portal.</summary>
public sealed class Delegate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserName { get; set; } = "";
    public Guid ClinicianId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : DbContext(options)
{
    public const string Schema = "portal";
    public DbSet<PortalAccess> Access => Set<PortalAccess>();
    public DbSet<PortalRequest> Requests => Set<PortalRequest>();
    public DbSet<Delegate> Delegates => Set<Delegate>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<PortalAccess>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.RevokedBy).HasMaxLength(100);
        });
        b.Entity<PortalRequest>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.Response).HasMaxLength(1000);
            e.Property(x => x.HandledBy).HasMaxLength(100);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.PatientId);
        });
        b.Entity<Delegate>(e =>
        {
            e.Property(x => x.UserName).HasMaxLength(100);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.UserName, x.ClinicianId }).IsUnique();
        });
    }
}

public sealed class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", PortalDbContext.Schema)).Options);
}

public enum PortalOpenError { None, NotFound, Expired, Locked, WrongDetails }
public sealed record PortalRequestCreate(PortalRequestKind Kind, Guid? BookingId, string Message);
public sealed record PortalRequestRow(Guid Id, Guid PatientId, string PatientName, string Mrn, string Kind, Guid? BookingId, string Message, string Status, DateTimeOffset CreatedAt,
    string? HandledBy, DateTimeOffset? HandledAt, string? Response);

/// <summary>Patient portal links and requests, and specialist/PA delegation.</summary>
public sealed class PortalService(PortalDbContext db, PatientsDbContext patients, AuditWriter audit, TimeProvider clock)
{
    public const int MaxFailedAttempts = 5;
    public const int LinkDays = 30;
    public const int UploadsPerDay = 5;

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Creates a new link (any earlier links for the patient are revoked). Returns the one-time token to put in the URL.</summary>
    public async Task<(string? Token, DateTimeOffset ExpiresAt, string? Error)> CreateLinkAsync(Guid patientId, string user, int days = LinkDays, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId && x.MergedIntoId == null, ct);
        if (p is null) return (null, default, "Patient not found.");
        var now = clock.GetUtcNow();
        foreach (var old in await db.Access.Where(a => a.PatientId == patientId && a.RevokedAt == null).ToListAsync(ct)) { old.RevokedAt = now; old.RevokedBy = user; }
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var row = new PortalAccess { PatientId = patientId, TokenHash = Hash(token), CreatedAt = now, CreatedBy = user, ExpiresAt = now.AddDays(Math.Clamp(days, 1, 90)) };
        db.Access.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "PortalLinkCreated", "PortalAccess", row.Id.ToString(), patientId.ToString(), reason: $"Expires {row.ExpiresAt:yyyy-MM-dd}", ct: ct);
        return (token, row.ExpiresAt, null);
    }

    public async Task<string?> RevokeAsync(Guid patientId, string user, CancellationToken ct = default)
    {
        var rows = await db.Access.Where(a => a.PatientId == patientId && a.RevokedAt == null).ToListAsync(ct);
        if (rows.Count == 0) return "This patient has no active portal link.";
        foreach (var r in rows) { r.RevokedAt = clock.GetUtcNow(); r.RevokedBy = user; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "PortalLinkRevoked", "PortalAccess", patientId.ToString(), patientId.ToString(), ct: ct);
        return null;
    }

    public async Task<object?> LinkStatusAsync(Guid patientId, CancellationToken ct = default)
    {
        var a = (await db.Access.AsNoTracking().Where(x => x.PatientId == patientId).ToListAsync(ct)).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return a is null ? null : new { a.CreatedAt, a.CreatedBy, a.ExpiresAt, a.RevokedAt, a.LastUsedAt, active = a.RevokedAt is null && a.ExpiresAt > clock.GetUtcNow(), locked = a.LockedUntil > clock.GetUtcNow() };
    }

    /// <summary>Checks token + date of birth. Wrong dates of birth count towards a 30-minute lock-out. Every opening is audited.</summary>
    public async Task<(PortalAccess? Access, Patient? Patient, PortalOpenError Error)> OpenAsync(string token, DateOnly dateOfBirth, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 64) return (null, null, PortalOpenError.NotFound);
        var hash = Hash(token);
        var a = await db.Access.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (a is null || a.RevokedAt is not null) return (null, null, PortalOpenError.NotFound);
        var now = clock.GetUtcNow();
        if (a.ExpiresAt <= now) return (null, null, PortalOpenError.Expired);
        if (a.LockedUntil > now) return (null, null, PortalOpenError.Locked);
        var p = await patients.Patients.AsNoTracking().Include(x => x.Alerts).SingleOrDefaultAsync(x => x.Id == a.PatientId, ct);
        if (p is not null && p.MergedIntoId is { } survivor) p = await patients.Patients.AsNoTracking().Include(x => x.Alerts).SingleOrDefaultAsync(x => x.Id == survivor, ct);
        if (p is null) return (null, null, PortalOpenError.NotFound);
        if (p.DateOfBirth != dateOfBirth)
        {
            a.FailedAttempts++;
            if (a.FailedAttempts >= MaxFailedAttempts) { a.LockedUntil = now.AddMinutes(30); a.FailedAttempts = 0; }
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("patient-portal", "PortalOpenFailed", "PortalAccess", a.Id.ToString(), a.PatientId.ToString(), reason: $"Wrong date of birth{(a.LockedUntil > now ? "; locked 30 minutes" : "")}", workstation: ip, ct: ct);
            return (null, null, a.LockedUntil > now ? PortalOpenError.Locked : PortalOpenError.WrongDetails);
        }
        a.FailedAttempts = 0; a.LastUsedAt = now;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("patient-portal", "PortalOpened", "PortalAccess", a.Id.ToString(), p.Id.ToString(), workstation: ip, ct: ct);
        return (a, p, PortalOpenError.None);
    }

    public async Task<bool> TryCountUploadAsync(PortalAccess a, CancellationToken ct = default)
    {
        var row = await db.Access.SingleAsync(x => x.Id == a.Id, ct);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (row.UploadDay != today) { row.UploadDay = today; row.UploadsToday = 0; }
        if (row.UploadsToday >= UploadsPerDay) return false;
        row.UploadsToday++;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<(PortalRequest? Request, string? Error)> AddRequestAsync(PortalAccess a, Guid patientId, PortalRequestCreate r, CancellationToken ct = default)
    {
        var msg = (r.Message ?? "").Trim();
        if (!Enum.IsDefined(r.Kind)) return (null, "Choose what you need.");
        if (msg.Length < 3) return (null, "Please tell us a little more.");
        if (msg.Length > 1000) msg = msg[..1000];
        var since = clock.GetUtcNow().AddDays(-1);
        if (await db.Requests.CountAsync(x => x.AccessId == a.Id && x.CreatedAt > since, ct) >= 10) return (null, "You have sent several requests today – we will be in touch, or please phone us.");
        var row = new PortalRequest { PatientId = patientId, AccessId = a.Id, Kind = r.Kind, BookingId = r.BookingId, Message = msg, CreatedAt = clock.GetUtcNow() };
        db.Requests.Add(row);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("patient-portal", "PortalRequest", "PortalRequest", row.Id.ToString(), patientId.ToString(), reason: row.Kind.ToString(), ct: ct);
        return (row, null);
    }

    public async Task<IReadOnlyList<PortalRequest>> PatientRequestsAsync(Guid patientId, CancellationToken ct = default) =>
        (await db.Requests.AsNoTracking().Where(x => x.PatientId == patientId).ToListAsync(ct)).OrderByDescending(x => x.CreatedAt).Take(20).ToList();

    public async Task<IReadOnlyList<PortalRequestRow>> RequestsAsync(bool openOnly, CancellationToken ct = default)
    {
        var rows = (await db.Requests.AsNoTracking().Where(x => !openOnly || x.Status == PortalRequestStatus.Open).ToListAsync(ct)).OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt).Take(300).ToList();
        var ids = rows.Select(r => r.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return rows.Select(r =>
        {
            var p = people.GetValueOrDefault(r.PatientId);
            return new PortalRequestRow(r.Id, r.PatientId, p is null ? "?" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Mrn ?? "", r.Kind.ToString(), r.BookingId, r.Message,
                r.Status.ToString(), r.CreatedAt, r.HandledBy, r.HandledAt, r.Response);
        }).ToList();
    }

    public async Task<string?> HandleRequestAsync(Guid id, PortalRequestStatus status, string? response, string user, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Request not found.";
        if (status == PortalRequestStatus.Open) return "Choose done or declined.";
        r.Status = status; r.HandledAt = clock.GetUtcNow(); r.HandledBy = user;
        r.Response = string.IsNullOrWhiteSpace(response) ? null : (response.Trim().Length > 1000 ? response.Trim()[..1000] : response.Trim());
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "PortalRequest" + status, "PortalRequest", r.Id.ToString(), r.PatientId.ToString(), reason: r.Response, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ specialist / PA delegation

    public async Task<IReadOnlyList<Delegate>> DelegatesAsync(CancellationToken ct = default) => await db.Delegates.AsNoTracking().OrderBy(d => d.UserName).ToListAsync(ct);
    public async Task<IReadOnlyList<Guid>> DelegatedCliniciansAsync(string userName, CancellationToken ct = default) =>
        await db.Delegates.AsNoTracking().Where(d => d.UserName == userName).Select(d => d.ClinicianId).ToListAsync(ct);

    public async Task<string?> AddDelegateAsync(string userName, Guid clinicianId, string user, CancellationToken ct = default)
    {
        userName = (userName ?? "").Trim();
        if (userName.Length == 0) return "Choose the PA's user account.";
        if (await db.Delegates.AnyAsync(d => d.UserName == userName && d.ClinicianId == clinicianId, ct)) return "Already linked.";
        db.Delegates.Add(new Delegate { UserName = userName, ClinicianId = clinicianId, CreatedAt = clock.GetUtcNow(), CreatedBy = user });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "PortalDelegate", clinicianId.ToString(), reason: $"{userName} works for clinician {clinicianId}", ct: ct);
        return null;
    }

    public async Task<int> RemoveDelegatesAsync(string userName, Guid? clinicianId, string user, CancellationToken ct = default)
    {
        var rows = await db.Delegates.Where(d => d.UserName == userName && (clinicianId == null || d.ClinicianId == clinicianId)).ToListAsync(ct);
        db.Delegates.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        if (rows.Count > 0) await audit.WriteAsync(user, "Delete", "PortalDelegate", userName, reason: $"{rows.Count} link(s) removed", ct: ct);
        return rows.Count;
    }
}
