using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Organisation;
using BrightPAS.Core.Setup;

namespace BrightPAS.Core.Data;

/// <summary>Core platform database context (schema "core"). Each module has its own schema and context.</summary>
public sealed class PasDbContext(DbContextOptions<PasDbContext> options) : DbContext(options)
{
    public const string Schema = "core";

    public DbSet<Organisation.Organisation> Organisations => Set<Organisation.Organisation>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SetupState> SetupState => Set<SetupState>();
    public DbSet<LicenceState> LicenceState => Set<LicenceState>();
    public DbSet<ModuleSetupState> ModuleSetup => Set<ModuleSetupState>();
    public DbSet<Clinician> Clinicians => Set<Clinician>();
    public DbSet<ProcedureItem> Procedures => Set<ProcedureItem>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite (used only for fast tests) cannot compare or sort DateTimeOffset; store as sortable binary there.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        // 0.10.1: indexes on lookup columns and referential integrity within the schema.
        b.Entity<AuditEvent>().HasIndex(x => new { x.UserId, x.OccurredAt });
        b.Entity<AuditEvent>().HasIndex(x => new { x.EntityType, x.EntityId });


        b.Entity<ModuleSetupState>(e =>
        {
            e.HasKey(x => x.ModuleId);
            e.Property(x => x.ModuleId).HasMaxLength(40);
            e.Property(x => x.SetUpBy).HasMaxLength(100);
            e.Property(x => x.SkippedChecks).HasMaxLength(1000);
        });

        b.Entity<SystemSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(60);
            e.Property(x => x.Value).HasMaxLength(500);
            e.Property(x => x.UpdatedBy).HasMaxLength(100);
        });

        b.Entity<Organisation.Organisation>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.HpiOrganisationId).HasMaxLength(20);
            e.HasMany(x => x.Sites).WithOne().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Site>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.HpiFacilityId).HasMaxLength(20);
            e.Property(x => x.AddressLine1).HasMaxLength(150);
            e.Property(x => x.AddressLine2).HasMaxLength(150);
            e.Property(x => x.Suburb).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Postcode).HasMaxLength(10);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
            e.HasMany(x => x.Facilities).WithOne().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Facility>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.HasMany(x => x.Locations).WithOne().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Location>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.FacilityId, x.Code }).IsUnique();
        });

        b.Entity<Clinician>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Specialty).HasMaxLength(100);
            e.Property(x => x.HpiCpn).HasMaxLength(10);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.PracticeName).HasMaxLength(150);
            e.Property(x => x.UserName).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<ProcedureItem>(e =>
        {
            e.ToTable("Procedures");
            e.Property(x => x.Code).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Service).HasMaxLength(20);
            e.Property(x => x.DefaultAnaesthetic).HasMaxLength(20);
            e.Property(x => x.FeeCode).HasMaxLength(20);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.HasKey(x => x.Sequence);
            e.Property(x => x.Sequence).ValueGeneratedNever();
            e.Property(x => x.UserId).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.PatientId).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Workstation).HasMaxLength(100);
            e.Property(x => x.PreviousHash).HasMaxLength(64);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.OccurredAt);
        });

        b.Entity<SetupState>(e =>
        {
            e.ToTable("SetupState");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.BootstrapCodeHash).HasMaxLength(200);
            e.Property(x => x.InstalledVersion).HasMaxLength(50);
            e.ToTable(t => t.HasCheckConstraint("CK_SetupState_SingleRow", "[Id] = 1"));
        });

        b.Entity<LicenceState>(e =>
        {
            e.ToTable("LicenceState", t => t.HasCheckConstraint("CK_LicenceState_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LicenceInstalledBy).HasMaxLength(100);
            e.Property(x => x.ActivationError).HasMaxLength(500);
            e.Property(x => x.LastKnownModules).HasMaxLength(500);
        });
    }
}
