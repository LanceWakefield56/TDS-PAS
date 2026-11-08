using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrightPAS.Identity;

/// <summary>Identity database context (schema "identity").</summary>
public sealed class PasIdentityDbContext(DbContextOptions<PasIdentityDbContext> options)
    : IdentityDbContext<PasUser, PasRole, Guid>(options)
{
    public const string Schema = "identity";

    public DbSet<BreakGlassGrant> BreakGlassGrants => Set<BreakGlassGrant>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite (used only for fast tests) cannot compare or sort DateTimeOffset; store as sortable binary there.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);
        // 0.10.1: indexes on lookup columns and referential integrity within the schema.
        b.Entity<BreakGlassGrant>().HasIndex(x => x.PatientId);

        b.Entity<PasUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.HpiCpn).HasMaxLength(20);
        });
        b.Entity<PasRole>(e => e.Property(x => x.Description).HasMaxLength(500));
        b.Entity<BreakGlassGrant>(e =>
        {
            e.Property(x => x.PatientId).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ReviewedBy).HasMaxLength(100);
            e.Property(x => x.ReviewNotes).HasMaxLength(1000);
            e.Property(x => x.ReviewStatus).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.PatientId, x.ExpiresAt });
            e.HasIndex(x => x.ReviewStatus);
        });
    }
}

/// <summary>Design-time factory for migrations only.</summary>
public sealed class PasIdentityDbContextFactory : IDesignTimeDbContextFactory<PasIdentityDbContext>
{
    public PasIdentityDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PasIdentityDbContext>()
            .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PasIdentityDbContext.Schema))
            .Options);
}
