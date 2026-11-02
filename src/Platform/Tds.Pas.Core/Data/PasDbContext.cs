using Microsoft.EntityFrameworkCore;
using Tds.Pas.Core.Audit;
using Tds.Pas.Core.Organisation;
using Tds.Pas.Core.Setup;

namespace Tds.Pas.Core.Data;

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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite (used only for fast tests) cannot compare or sort DateTimeOffset; store as sortable binary there.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

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
    }
}
