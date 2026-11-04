using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Galenica.Core.Audit;
using Galenica.Core.Data;
using Galenica.Core.Organisation;

namespace Galenica.Core.Tests;

/// <summary>Runs only when PAS_TEST_SQLSERVER holds a SQL Server connection string (no database name needed).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("PAS_TEST_SQLSERVER");
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            Skip = "Set PAS_TEST_SQLSERVER to run SQL Server integration tests.";
    }
}

/// <summary>Creates a throw-away database per test class, applies the real migrations, then drops it.</summary>
public sealed class SqlServerDatabase : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;
    private string _name = string.Empty;

    public DbContextOptions<PasDbContext> Options => new DbContextOptionsBuilder<PasDbContext>()
        .UseSqlServer(ConnectionString, s => s.MigrationsHistoryTable("__EFMigrationsHistory", PasDbContext.Schema)).Options;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(SqlServerFactAttribute.ConnectionString)) return;
        _name = "GalenicaTest_" + Guid.NewGuid().ToString("N")[..12];
        ConnectionString = new SqlConnectionStringBuilder(SqlServerFactAttribute.ConnectionString) { InitialCatalog = _name }.ConnectionString;
        await using var db = new PasDbContext(Options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(_name)) return;
        await using var conn = new SqlConnection(new SqlConnectionStringBuilder(SqlServerFactAttribute.ConnectionString) { InitialCatalog = "master" }.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}];", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

public class SqlServerIntegrationTests(SqlServerDatabase database) : IClassFixture<SqlServerDatabase>
{
    [SqlServerFact]
    public async Task Migrations_create_the_audit_table_as_an_append_only_ledger_table()
    {
        await using var conn = new SqlConnection(database.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT t.ledger_type_desc FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'core' AND t.name = 'AuditEvents'", conn);
        Assert.Equal("APPEND_ONLY_LEDGER_TABLE", (string?)await cmd.ExecuteScalarAsync());
    }

    [SqlServerFact]
    public async Task Audit_records_are_written_chained_and_cannot_be_altered_or_deleted()
    {
        await using (var db = new PasDbContext(database.Options))
        {
            var writer = new AuditWriter(db, TimeProvider.System);
            await writer.WriteAsync("admin", "Login", "User", "admin", workstation: "PAS-APP");
            await writer.WriteAsync("nurse.a", "View", "Patient", "P1", patientId: "P1");
            var stored = await db.AuditEvents.OrderBy(x => x.Sequence).ToListAsync();
            Assert.Null(AuditChain.FindFirstBreak(stored));
        }

        await using var conn = new SqlConnection(database.ConnectionString);
        await conn.OpenAsync();
        await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await using var update = new SqlCommand("UPDATE core.AuditEvents SET UserId = 'someone.else' WHERE Sequence = 1", conn);
            await update.ExecuteNonQueryAsync();
        });
        await Assert.ThrowsAsync<SqlException>(async () =>
        {
            await using var delete = new SqlCommand("DELETE FROM core.AuditEvents WHERE Sequence = 1", conn);
            await delete.ExecuteNonQueryAsync();
        });
    }

    [SqlServerFact]
    public async Task Default_organisation_setup_round_trips_on_sql_server()
    {
        await using (var db = new PasDbContext(database.Options))
        {
            db.Organisations.Add(TdsDefaultSetup.Create());
            await db.SaveChangesAsync();
        }
        await using (var db = new PasDbContext(database.Options))
        {
            var theatres = await db.Locations.Where(l => l.Type == LocationType.Theatre).OrderBy(l => l.SortOrder).ToListAsync();
            Assert.Equal(["TH1", "TH2"], theatres.Select(t => t.Code));
            Assert.False(theatres[1].IsActive);
        }
    }
}
