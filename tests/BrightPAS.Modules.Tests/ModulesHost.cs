using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Tests;

/// <summary>All modules on in-memory SQLite (one database per schema), with a fixed NZ clock.</summary>
public sealed class ModulesHost : IAsyncDisposable
{
    private readonly Dictionary<string, SqliteConnection> _dbs = [];
    public ServiceProvider Services { get; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(13)));

    public ModulesHost()
    {
        SqliteConnection Db(string schema)
        {
            if (!_dbs.TryGetValue(schema, out var c)) { c = new SqliteConnection("DataSource=:memory:"); c.Open(); _dbs[schema] = c; }
            return c;
        }
        Clock.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland"));
        var s = new ServiceCollection();
        s.AddSingleton<TimeProvider>(Clock);
        s.AddSingleton(new PasDatabaseOptions((o, schema) => o.UseSqlite(Db(schema))));
        s.AddDbContext<PasDbContext>(o => o.UseSqlite(Db("core")));
        s.AddScoped<AuditWriter>();
        s.AddPasModules([new PatientsModule(), new LabsModule(), new TheatreModule(), new FormsModule(), new BillingModule()]);
        Services = s.BuildServiceProvider();
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;
        foreach (DbContext db in new DbContext[] { sp.GetRequiredService<PasDbContext>(), sp.GetRequiredService<PatientsDbContext>(), sp.GetRequiredService<LabsDbContext>(),
                     sp.GetRequiredService<TheatreDbContext>(), sp.GetRequiredService<FormsDbContext>(), sp.GetRequiredService<BillingDbContext>() })
            db.Database.EnsureCreated();
        var core = sp.GetRequiredService<PasDbContext>();
        core.Organisations.Add(BrightPAS.Core.Organisation.TdsDefaultSetup.Create());
        core.SaveChanges();
    }

    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    public async Task<Patient> PatientAsync(string family = "Ngātai", string given = "Aroha Mere", string? nhi = "ZSC21TN", DateOnly? dob = null)
    {
        await using var scope = Scope();
        var r = await scope.ServiceProvider.GetRequiredService<PatientService>()
            .RegisterAsync(new PatientRegistration(family, given, dob ?? new DateOnly(1980, 5, 17), BirthSex.Female, nhi, ConfirmedNotDuplicate: true), "reception");
        Assert.True(r.Succeeded, string.Join("; ", r.Errors));
        return r.Patient!;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        foreach (var c in _dbs.Values) await c.DisposeAsync();
    }
}
