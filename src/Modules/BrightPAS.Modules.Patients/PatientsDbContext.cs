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
    public DbSet<PatientDocument> Documents => Set<PatientDocument>();
    public DbSet<IdentityCorrection> Corrections => Set<IdentityCorrection>();
    public DbSet<PatientMerge> Merges => Set<PatientMerge>();
    public DbSet<LabelPrinter> Printers => Set<LabelPrinter>();
    public DbSet<TransferRecord> Transfers => Set<TransferRecord>();
    public DbSet<DocumentText> DocumentTexts => Set<DocumentText>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        // 0.10.1: indexes on lookup columns and referential integrity within the schema.
        b.Entity<Patient>().HasIndex(x => x.GpPracticeHpiFacilityId);
        b.Entity<Patient>().HasOne<Patient>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PatientDocument>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<IdentityCorrection>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PatientMerge>().HasOne<Patient>().WithMany().HasForeignKey(x => x.SurvivorId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PatientMerge>().HasOne<Patient>().WithMany().HasForeignKey(x => x.DuplicateId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TransferRecord>().HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<TransferRecord>().HasIndex(x => x.BookingId);

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
        b.Entity<PatientDocument>(e =>
        {
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.FileName).HasMaxLength(130);
            e.Property(x => x.ContentType).HasMaxLength(40);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.UploadedBy).HasMaxLength(100);
            e.Property(x => x.DeletedReason).HasMaxLength(300);
            e.Property(x => x.DeletedBy).HasMaxLength(100);
            e.HasIndex(x => new { x.PatientId, x.IsDeleted });
            e.HasIndex(x => x.BookingId);
        });
        b.Entity<DocumentText>(e =>
        {
            e.ToTable("DocumentTexts");
            e.HasKey(x => x.DocumentId);
            e.HasOne<PatientDocument>().WithOne().HasForeignKey<DocumentText>(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Method).HasMaxLength(10);
            e.Property(x => x.Message).HasMaxLength(300);
            e.HasIndex(x => x.Status);
        });
        b.Entity<IdentityCorrection>(e =>
        {
            e.Property(x => x.NewFamilyName).HasMaxLength(100);
            e.Property(x => x.NewGivenNames).HasMaxLength(150);
            e.Property(x => x.NewSex).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.NewNhi).HasMaxLength(7);
            e.Property(x => x.Before).HasMaxLength(400);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Evidence).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.Property(x => x.DecidedBy).HasMaxLength(100);
            e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.HasIndex(x => new { x.Status, x.PatientId });
        });
        b.Entity<PatientMerge>(e =>
        {
            e.Property(x => x.DuplicateSummary).HasMaxLength(400);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.RequestedBy).HasMaxLength(100);
            e.Property(x => x.DecidedBy).HasMaxLength(100);
            e.Property(x => x.DecisionNote).HasMaxLength(300);
            e.Property(x => x.Result).HasMaxLength(1000);
            e.HasIndex(x => x.Status);
        });
        b.Entity<LabelPrinter>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(15);
            e.Property(x => x.Host).HasMaxLength(100);
            e.Property(x => x.Language).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.MediaCode).HasMaxLength(20);
        });
        b.Entity<TransferRecord>(e =>
        {
            e.Property(x => x.ReceivingFacility).HasMaxLength(150);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Situation).HasMaxLength(1000);
            e.Property(x => x.Background).HasMaxLength(2000);
            e.Property(x => x.Assessment).HasMaxLength(2000);
            e.Property(x => x.Recommendation).HasMaxLength(1000);
            e.Property(x => x.Escort).HasMaxLength(150);
            e.Property(x => x.TransportMode).HasMaxLength(50);
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
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
