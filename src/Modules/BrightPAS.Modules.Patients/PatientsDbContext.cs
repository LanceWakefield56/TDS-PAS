using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrightPAS.Modules.Patients;

/// <summary>Patients module database context (schema "patients").</summary>
public sealed class PatientsDbContext(DbContextOptions<PatientsDbContext> options) : DbContext(options)
{
    public const string Schema = "patients";

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientAlert> Alerts => Set<PatientAlert>();
    public DbSet<MrnCounter> MrnCounter => Set<MrnCounter>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<Patient>(e =>
        {
            e.Property(x => x.Mrn).HasMaxLength(20);
            e.HasIndex(x => x.Mrn).IsUnique();
            e.Property(x => x.Nhi).HasMaxLength(7).IsFixedLength();
            e.HasIndex(x => x.Nhi).IsUnique().HasFilter("[Nhi] IS NOT NULL");
            e.Property(x => x.FamilyName).HasMaxLength(100);
            e.Property(x => x.GivenNames).HasMaxLength(150);
            e.Property(x => x.PreferredName).HasMaxLength(100);
            e.Property(x => x.FamilyNameSearch).HasMaxLength(100);
            e.Property(x => x.GivenNamesSearch).HasMaxLength(150);
            e.HasIndex(x => new { x.FamilyNameSearch, x.GivenNamesSearch, x.DateOfBirth });
            e.HasIndex(x => x.DateOfBirth);
            e.Property(x => x.Sex).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Gender).HasMaxLength(50);
            e.Property(x => x.MobilePhone).HasMaxLength(30);
            e.Property(x => x.HomePhone).HasMaxLength(30);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.AddressLine1).HasMaxLength(200);
            e.Property(x => x.AddressLine2).HasMaxLength(200);
            e.Property(x => x.Suburb).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.Postcode).HasMaxLength(10);
            e.Property(x => x.GpPracticeHpiFacilityId).HasMaxLength(20);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasMany(x => x.Ethnicities).WithOne().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Alerts).WithOne().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PatientEthnicity>(e =>
        {
            e.ToTable("PatientEthnicities");
            e.HasKey(x => new { x.PatientId, x.Order });
            e.Property(x => x.Code).HasMaxLength(5);
        });
        b.Entity<PatientAlert>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.RecordedBy).HasMaxLength(100);
        });
        b.Entity<MrnCounter>(e =>
        {
            e.ToTable("MrnCounter", t => t.HasCheckConstraint("CK_MrnCounter_SingleRow", "[Id] = 1"));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.NextValue).IsConcurrencyToken();
            e.HasData(new MrnCounter { Id = 1, NextValue = 1 });
        });
    }
}

public sealed class PatientsDbContextFactory : IDesignTimeDbContextFactory<PatientsDbContext>
{
    public PatientsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PatientsDbContext>()
            .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PatientsDbContext.Schema))
            .Options);
}
