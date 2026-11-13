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
using BrightPAS.Modules.Letters;
using BrightPAS.Modules.Rostering;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Reminders;
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
        s.AddScoped<BrightPAS.Core.Setup.SettingsService>();
        s.AddSingleton<ISecretProtector, ReversibleTestProtector>();
        s.AddSingleton(new ReminderDefaults(SmsProvider.TestOutbox, EmailProvider.TestOutbox));
        s.AddPasModules([new PatientsModule(), new LabsModule(), new TheatreModule(), new FormsModule(), new BillingModule(), new RemindersModule(), new ReferralsModule(), new LettersModule(), new RosteringModule(),
            new BrightPAS.Modules.Credentialing.CredentialingModule(), new BrightPAS.Modules.Portal.PortalModule(), new BrightPAS.Modules.Messaging.MessagingModule(), new BrightPAS.Modules.Assets.AssetsModule(), new BrightPAS.Modules.Emr.EmrModule(), new BrightPAS.Modules.Endoscopy.EndoscopyModule()]);
        s.AddLogging();
        s.AddSingleton<IDocumentCipher, TestDocumentCipher>();
        s.AddScoped<BrightPAS.Core.Organisation.OrganisationService>();
        Services = s.BuildServiceProvider();
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;
        foreach (DbContext db in new DbContext[] { sp.GetRequiredService<PasDbContext>(), sp.GetRequiredService<PatientsDbContext>(), sp.GetRequiredService<LabsDbContext>(),
                     sp.GetRequiredService<TheatreDbContext>(), sp.GetRequiredService<FormsDbContext>(), sp.GetRequiredService<BillingDbContext>(),
                     sp.GetRequiredService<RemindersDbContext>(), sp.GetRequiredService<ReferralsDbContext>(), sp.GetRequiredService<LettersDbContext>(), sp.GetRequiredService<RosterDbContext>(),
                     sp.GetRequiredService<BrightPAS.Modules.Credentialing.CredDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Portal.PortalDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Messaging.MsgDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Assets.AssetsDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Emr.EmrDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Endoscopy.EndoDbContext>() })
            db.Database.EnsureCreated();
        var core = sp.GetRequiredService<PasDbContext>();
        core.Organisations.Add(BrightPAS.Core.Organisation.TdsDefaultSetup.Create());
        // Clinician register used by the tests (lists, appointments and the waitlist reference clinicians by id).
        var colour = 0;
        foreach (var (name, type) in new[] { ("Dr Ben Hall", BrightPAS.Core.Organisation.ClinicianType.Surgeon), ("Dr Kate Ford", BrightPAS.Core.Organisation.ClinicianType.Anaesthetist),
                     ("Dr Ana Lee", BrightPAS.Core.Organisation.ClinicianType.Endoscopist), ("Dr X", BrightPAS.Core.Organisation.ClinicianType.Surgeon), ("Dr A", BrightPAS.Core.Organisation.ClinicianType.Surgeon) })
            core.Clinicians.Add(new BrightPAS.Core.Organisation.Clinician { Id = Doc.Id(name), Name = name, Type = type, Colour = colour++, IsActive = true });
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

/// <summary>Test stand-in for Data Protection: visibly "encrypted" so tests can check secrets are never stored in plain text.</summary>
public sealed class ReversibleTestProtector : ISecretProtector
{
    public string Protect(string plain) => "ENC:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plain));
    public string? Unprotect(string? p) => p is null ? null : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(p[4..]));
}

/// <summary>Test cipher: reversible XOR so tests can check contents are not stored as-is.</summary>
public sealed class TestDocumentCipher : IDocumentCipher
{
    public byte[] Protect(byte[] plain) => [0xEE, .. plain.Select(b => (byte)(b ^ 0x5A))];
    public byte[] Unprotect(byte[] c) => c.Length > 0 && c[0] == 0xEE ? c[1..].Select(b => (byte)(b ^ 0x5A)).ToArray() : throw new System.Security.Cryptography.CryptographicException();
}

/// <summary>Stable ids for the test clinician register.</summary>
public static class Doc
{
    public static Guid Id(string name) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name)));
}
