using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Galenica.Core.Audit;
using Galenica.Core.Data;
using Galenica.Core.Modules;

namespace Galenica.Modules.Patients.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public static string? Cs => Environment.GetEnvironmentVariable("PAS_TEST_SQLSERVER");
    public SqlServerFactAttribute() { if (string.IsNullOrWhiteSpace(Cs)) Skip = "Set PAS_TEST_SQLSERVER to run SQL Server integration tests."; }
}

public class PatientsSqlServerTests
{
    [SqlServerFact]
    public async Task Patients_register_on_sql_server_with_real_migrations_and_unique_nhi()
    {
        var name = "GalenicaTest_" + Guid.NewGuid().ToString("N")[..12];
        var cs = new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs) { InitialCatalog = name }.ConnectionString;
        void Sql(DbContextOptionsBuilder o, string schema) => o.UseSqlServer(cs, s => s.MigrationsHistoryTable("__EFMigrationsHistory", schema).EnableRetryOnFailure());

        var services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddSingleton(new PasDatabaseOptions(Sql))
            .AddDbContext<PasDbContext>(o => Sql(o, PasDbContext.Schema))
            .AddScoped<AuditWriter>()
            .AddPasModules([new PatientsModule()])
            .BuildServiceProvider();
        try
        {
            await using (var scope = services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<PasDbContext>().Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<PatientsDbContext>().Database.MigrateAsync();
            }

            await using (var scope = services.CreateAsyncScope())
            {
                var svc = scope.ServiceProvider.GetRequiredService<PatientService>();
                var a = await svc.RegisterAsync(new PatientRegistration("Ngātai", "Aroha", new DateOnly(1980, 5, 17), BirthSex.Female, "ZSC21TN"), "reception.a");
                var b = await svc.RegisterAsync(new PatientRegistration("Smith", "Jo", new DateOnly(1990, 1, 1), BirthSex.Unknown), "reception.a");
                var c = await svc.RegisterAsync(new PatientRegistration("Brown", "Sam", new DateOnly(1991, 2, 2), BirthSex.Unknown), "reception.a");
                Assert.True(a.Succeeded && b.Succeeded && c.Succeeded); // two patients without NHI are allowed (filtered unique index)
                Assert.Equal(["TDS0000001", "TDS0000002", "TDS0000003"], new[] { a.Patient!.Mrn, b.Patient!.Mrn, c.Patient!.Mrn });
                Assert.Single(await svc.SearchAsync("ngatai"));
                var audit = await scope.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.OrderBy(e => e.Sequence).ToListAsync();
                Assert.Null(AuditChain.FindFirstBreak(audit));
            }
        }
        finally
        {
            await services.DisposeAsync();
            SqlConnection.ClearAllPools();
            await using var conn = new SqlConnection(new SqlConnectionStringBuilder(SqlServerFactAttribute.Cs) { InitialCatalog = "master" }.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
