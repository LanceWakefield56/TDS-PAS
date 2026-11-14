using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;
using BrightPAS.Core.Organisation;

namespace BrightPAS.Modules.Credentialing;

public sealed class CredentialingModule : IPasModule
{
    public const string Id = "credentialing";
    public ModuleManifest Manifest { get; } = new(Id, "Credentialing", typeof(CredentialingModule).Assembly.GetName().Version ?? new Version(0, 0),
        CredDbContext.Schema, [], "Clinician credentials (APC, scope, indemnity, approval), expiry alerts and scope of practice checks at booking.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<CredDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, CredDbContext.Schema));
        services.AddScoped<CredentialingService>();
    }
}

public enum CredentialType { Apc = 1, VocationalScope = 2, Indemnity = 3, CredentialingApproval = 4, Resuscitation = 5, Other = 6 }

/// <summary>A credential held by a clinician: annual practising certificate, vocational scope, indemnity cover, TDS credentialing approval…</summary>
public sealed class Credential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicianId { get; set; }
    public CredentialType Type { get; set; }
    /// <summary>Certificate / policy number or committee minute reference.</summary>
    public string? Reference { get; set; }
    public string? Issuer { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? Notes { get; set; }
    public string VerifiedBy { get; set; } = "";
    public DateTimeOffset VerifiedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Scope of practice granted at TDS: a whole service (Surgical/Endoscopy) or individual procedures from the catalogue.</summary>
public sealed class Privilege
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicianId { get; set; }
    /// <summary>"Surgical", "Endoscopy" or "Anaesthesia" – applies to every procedure in that service when <see cref="ProcedureCode"/> is empty.</summary>
    public string Service { get; set; } = "Surgical";
    public string? ProcedureCode { get; set; }
    public DateOnly ApprovedOn { get; set; }
    public DateOnly? ReviewDue { get; set; }
    public string? Conditions { get; set; }
    public string ApprovedBy { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class CredSettings
{
    public int Id { get; set; } = 1;
    public int WarningDays { get; set; } = 60;
    /// <summary>When true, bookings for procedures outside the clinician's privileges are warned about.</summary>
    public bool CheckPrivileges { get; set; } = true;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>A credential type every active clinician must hold (one row per type).</summary>
public sealed class RequiredCredential
{
    public CredentialType Type { get; set; }
}

public sealed class CredDbContext(DbContextOptions<CredDbContext> options) : DbContext(options)
{
    public const string Schema = "cred";
    public DbSet<Credential> Credentials => Set<Credential>();
    public DbSet<Privilege> Privileges => Set<Privilege>();
    public DbSet<CredSettings> Settings => Set<CredSettings>();
    public DbSet<RequiredCredential> Required => Set<RequiredCredential>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<Credential>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Reference).HasMaxLength(80);
            e.Property(x => x.Issuer).HasMaxLength(120);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.VerifiedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.ClinicianId, x.Type });
        });
        b.Entity<Privilege>(e =>
        {
            e.Property(x => x.Service).HasMaxLength(20);
            e.Property(x => x.ProcedureCode).HasMaxLength(30);
            e.Property(x => x.Conditions).HasMaxLength(300);
            e.Property(x => x.ApprovedBy).HasMaxLength(100);
            e.HasIndex(x => x.ClinicianId);
        });
        b.Entity<RequiredCredential>(e =>
        {
            e.ToTable("RequiredTypes");
            e.HasKey(x => x.Type);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.HasData(new RequiredCredential { Type = CredentialType.Apc }, new RequiredCredential { Type = CredentialType.Indemnity }, new RequiredCredential { Type = CredentialType.CredentialingApproval });
        });
        b.Entity<CredSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });
    }
}

public sealed class CredDbContextFactory : IDesignTimeDbContextFactory<CredDbContext>
{
    public CredDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CredDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", CredDbContext.Schema)).Options);
}

public sealed record CredentialEdit(Guid? Id, Guid ClinicianId, CredentialType Type, string? Reference, string? Issuer, DateOnly? IssuedOn, DateOnly? ExpiresOn, string? Notes, bool IsActive);
public sealed record PrivilegeEdit(Guid? Id, Guid ClinicianId, string Service, string? ProcedureCode, DateOnly ApprovedOn, DateOnly? ReviewDue, string? Conditions, bool IsActive);
public sealed record CredSettingsEdit(IReadOnlyList<CredentialType> RequiredTypes, int WarningDays, bool CheckPrivileges);
public sealed record CredentialView(Guid Id, Guid ClinicianId, string Type, string? Reference, string? Issuer, DateOnly? IssuedOn, DateOnly? ExpiresOn, string? Notes,
    string VerifiedBy, DateTimeOffset VerifiedAt, bool IsActive, string State);
public sealed record ClinicianStatus(Guid ClinicianId, string Name, string Type, string State, IReadOnlyList<string> Problems, DateOnly? NextExpiry);

/// <summary>Credentials register, expiry tracking and scope-of-practice checks.</summary>
public sealed class CredentialingService(CredDbContext db, PasDbContext core, AuditWriter audit, TimeProvider clock)
{
    public static readonly string[] Services = ["Surgical", "Endoscopy", "Anaesthesia"];
    public static string Label(CredentialType t) => t switch
    {
        CredentialType.Apc => "Annual practising certificate",
        CredentialType.VocationalScope => "Vocational scope (MCNZ)",
        CredentialType.Indemnity => "Indemnity insurance",
        CredentialType.CredentialingApproval => "TDS credentialing approval",
        CredentialType.Resuscitation => "Resuscitation (CPR/ACLS)",
        _ => "Other",
    };

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<CredSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new CredSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<CredentialType>> RequiredAsync(CancellationToken ct = default) =>
        (await db.Required.AsNoTracking().Select(r => r.Type).ToListAsync(ct)).OrderBy(t => t).ToList();

    public async Task<IReadOnlyList<string>> UpdateSettingsAsync(CredSettingsEdit e, string user, CancellationToken ct = default)
    {
        if (e.WarningDays is < 7 or > 365) return ["Warn between 7 and 365 days before expiry."];
        var s = await SettingsAsync(ct);
        var want = e.RequiredTypes.Where(Enum.IsDefined).Distinct().ToList();
        db.Required.RemoveRange(await db.Required.Where(r => !want.Contains(r.Type)).ToListAsync(ct));
        var have = await db.Required.Select(r => r.Type).ToListAsync(ct);
        foreach (var t in want.Where(t => !have.Contains(t))) db.Required.Add(new RequiredCredential { Type = t });
        s.WarningDays = e.WarningDays; s.CheckPrivileges = e.CheckPrivileges;
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "CredSettings", "1", reason: $"Required: {string.Join(",", want)}; warn {s.WarningDays} days; privileges checked: {s.CheckPrivileges}", ct: ct);
        return [];
    }

    public string State(Credential c, int warningDays) =>
        !c.IsActive ? "Inactive" : c.ExpiresOn is not { } exp ? "Current" : exp < Today ? "Expired" : exp <= Today.AddDays(warningDays) ? "ExpiringSoon" : "Current";

    public async Task<IReadOnlyList<CredentialView>> CredentialsAsync(Guid clinicianId, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        return (await db.Credentials.AsNoTracking().Where(c => c.ClinicianId == clinicianId).ToListAsync(ct)).OrderBy(c => c.Type).ThenByDescending(c => c.ExpiresOn)
            .Select(c => new CredentialView(c.Id, c.ClinicianId, c.Type.ToString(), c.Reference, c.Issuer, c.IssuedOn, c.ExpiresOn, c.Notes, c.VerifiedBy, c.VerifiedAt, c.IsActive, State(c, s.WarningDays)))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> SaveCredentialAsync(CredentialEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await core.Clinicians.AnyAsync(c => c.Id == e.ClinicianId, ct)) errors.Add("Choose the clinician.");
        if (!Enum.IsDefined(e.Type)) errors.Add("Choose the credential type.");
        if (e.IssuedOn is { } i && e.ExpiresOn is { } x && x < i) errors.Add("The expiry date must be after the issue date.");
        if (e.Type is CredentialType.Apc or CredentialType.Indemnity && e.ExpiresOn is null) errors.Add($"Enter the expiry date for the {Label(e.Type)}.");
        if (errors.Count > 0) return errors;
        Credential row;
        if (e.Id is { } id) row = await db.Credentials.SingleAsync(c => c.Id == id, ct);
        else { row = new Credential(); db.Credentials.Add(row); }
        row.ClinicianId = e.ClinicianId; row.Type = e.Type; row.Reference = Blank(e.Reference, 80); row.Issuer = Blank(e.Issuer, 120); row.IssuedOn = e.IssuedOn; row.ExpiresOn = e.ExpiresOn;
        row.Notes = Blank(e.Notes, 500); row.IsActive = e.IsActive; row.VerifiedBy = user; row.VerifiedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "Credential", row.Id.ToString(), reason: $"{e.ClinicianId}: {row.Type} {row.Reference} expires {row.ExpiresOn:yyyy-MM-dd}", ct: ct);
        return errors;
    }

    public async Task<IReadOnlyList<Privilege>> PrivilegesAsync(Guid clinicianId, CancellationToken ct = default) =>
        (await db.Privileges.AsNoTracking().Where(p => p.ClinicianId == clinicianId).ToListAsync(ct)).OrderBy(p => p.Service).ThenBy(p => p.ProcedureCode).ToList();

    public async Task<IReadOnlyList<string>> SavePrivilegeAsync(PrivilegeEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!await core.Clinicians.AnyAsync(c => c.Id == e.ClinicianId, ct)) errors.Add("Choose the clinician.");
        if (!Services.Contains(e.Service)) errors.Add("Choose Surgical, Endoscopy or Anaesthesia.");
        var code = Blank(e.ProcedureCode, 30)?.ToUpperInvariant();
        if (code is not null && !await core.Procedures.AnyAsync(p => p.Code == code, ct)) errors.Add($"Procedure code {code} is not in the procedure catalogue.");
        if (e.ReviewDue is { } r && r < e.ApprovedOn) errors.Add("The review date must be after the approval date.");
        if (errors.Count > 0) return errors;
        Privilege row;
        if (e.Id is { } id) row = await db.Privileges.SingleAsync(p => p.Id == id, ct);
        else { row = new Privilege(); db.Privileges.Add(row); }
        row.ClinicianId = e.ClinicianId; row.Service = e.Service; row.ProcedureCode = code; row.ApprovedOn = e.ApprovedOn; row.ReviewDue = e.ReviewDue;
        row.Conditions = Blank(e.Conditions, 300); row.IsActive = e.IsActive; row.ApprovedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, e.Id is null ? "Create" : "Update", "Privilege", row.Id.ToString(), reason: $"{e.ClinicianId}: {row.Service} {row.ProcedureCode ?? "(all)"}{(row.IsActive ? "" : " inactive")}", ct: ct);
        return errors;
    }

    /// <summary>One line per active clinician: Current, ExpiringSoon, Expired or Missing (a required credential is absent).</summary>
    public async Task<IReadOnlyList<ClinicianStatus>> StatusAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var required = await RequiredAsync(ct);
        var clinicians = await core.Clinicians.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
        var creds = await db.Credentials.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
        var privs = await db.Privileges.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        return clinicians.OrderBy(c => c.Name).Select(c =>
        {
            var mine = creds.Where(x => x.ClinicianId == c.Id).ToList();
            var problems = new List<string>();
            var state = "Current";
            foreach (var t in required)
            {
                var best = mine.Where(x => x.Type == t).OrderByDescending(x => x.ExpiresOn ?? DateOnly.MaxValue).FirstOrDefault();
                if (best is null) { problems.Add($"{Label(t)}: not recorded"); state = "Missing"; continue; }
                var st = State(best, s.WarningDays);
                if (st == "Expired") { problems.Add($"{Label(t)}: expired {best.ExpiresOn:d MMM yyyy}"); if (state != "Missing") state = "Expired"; }
                else if (st == "ExpiringSoon") { problems.Add($"{Label(t)}: expires {best.ExpiresOn:d MMM yyyy}"); if (state == "Current") state = "ExpiringSoon"; }
            }
            foreach (var p in privs.Where(p => p.ClinicianId == c.Id && p.ReviewDue is { } r && r <= Today.AddDays(s.WarningDays)))
            {
                problems.Add($"Privilege review due {p.ReviewDue:d MMM yyyy} ({p.Service} {p.ProcedureCode})".TrimEnd());
                if (state == "Current") state = "ExpiringSoon";
            }
            if (s.CheckPrivileges && c.Type != ClinicianType.Other && !privs.Any(p => p.ClinicianId == c.Id)) { problems.Add("No scope of practice recorded"); if (state == "Current") state = "ExpiringSoon"; }
            var next = mine.Where(x => x.ExpiresOn is not null && x.ExpiresOn >= Today).Select(x => x.ExpiresOn).Min();
            return new ClinicianStatus(c.Id, c.Name, c.Type.ToString(), state, problems, next);
        }).ToList();
    }

    /// <summary>
    /// Warnings for a theatre case on a date: each clinician (matched by register name) must hold the required credentials, unexpired on that date,
    /// and the surgeon/endoscopist must have a privilege covering the procedure (by catalogue name or code).
    /// </summary>
    public async Task<IReadOnlyList<string>> CheckCaseAsync(DateOnly date, Guid? surgeonId, Guid? anaesthetistId, string? procedure, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var required = await RequiredAsync(ct);
        var warnings = new List<string>();
        var wanted = new[] { surgeonId, anaesthetistId }.Where(x => x is not null && x != Guid.Empty).Select(x => x!.Value).ToList();
        var clinicians = await core.Clinicians.AsNoTracking().Where(c => wanted.Contains(c.Id)).ToListAsync(ct);
        var ids = clinicians.Select(c => c.Id).ToList();
        var creds = await db.Credentials.AsNoTracking().Where(c => ids.Contains(c.ClinicianId) && c.IsActive).ToListAsync(ct);
        foreach (var c in clinicians)
            foreach (var t in required)
            {
                var ok = creds.Any(x => x.ClinicianId == c.Id && x.Type == t && (x.ExpiresOn is null || x.ExpiresOn >= date));
                if (!ok) warnings.Add($"Credentialing: {c.Name} – {Label(t)} {(creds.Any(x => x.ClinicianId == c.Id && x.Type == t) ? "expires before this date" : "not recorded")}.");
            }
        if (s.CheckPrivileges && !string.IsNullOrWhiteSpace(procedure) && clinicians.FirstOrDefault(c => c.Id == surgeonId) is { } surg)
        {
            var proc = await core.Procedures.AsNoTracking().FirstOrDefaultAsync(p => p.Name == procedure.Trim() || p.Code == procedure.Trim(), ct);
            var privs = await db.Privileges.AsNoTracking().Where(p => p.ClinicianId == surg.Id && p.IsActive && p.ApprovedOn <= date).ToListAsync(ct);
            var covered = proc is null
                ? privs.Count > 0 // free-text procedure: any privilege counts
                : privs.Any(p => (p.ProcedureCode == null && p.Service == proc.Service) || p.ProcedureCode == proc.Code);
            if (!covered) warnings.Add($"Credentialing: {procedure.Trim()} is outside {surg.Name}'s recorded scope of practice at TDS.");
        }
        return warnings;
    }

    private static string? Blank(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
}
