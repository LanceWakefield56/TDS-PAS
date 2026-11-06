using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;

namespace BrightPAS.Modules.Patients.Tests;

public sealed class PatientsHost : IAsyncDisposable
{
    private readonly SqliteConnection _patients = new("DataSource=:memory:");
    private readonly SqliteConnection _core = new("DataSource=:memory:");
    public ServiceProvider Services { get; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(13)));

    public PatientsHost()
    {
        _patients.Open(); _core.Open();
        var s = new ServiceCollection();
        s.AddSingleton<TimeProvider>(Clock);
        s.AddSingleton(new PasDatabaseOptions((o, _) => o.UseSqlite(_patients)));
        s.AddDbContext<PasDbContext>(o => o.UseSqlite(_core));
        s.AddScoped<AuditWriter>();
        s.AddPasModules([new PatientsModule()]);
        Services = s.BuildServiceProvider();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PatientsDbContext>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<PasDbContext>().Database.EnsureCreated();
    }

    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _patients.DisposeAsync();
        await _core.DisposeAsync();
    }
}

public class PatientServiceTests
{
    private static PatientRegistration Aroha(string? nhi = "ZSC21TN") =>
        new("Ngātai", "Aroha Mere", new DateOnly(1980, 5, 17), BirthSex.Female, nhi,
            MobilePhone: "021 555 0101", EthnicityCodes: ["21111"]);

    [Fact]
    public async Task Module_is_registered_through_the_catalogue()
    {
        await using var host = new PatientsHost();
        var catalogue = host.Services.GetRequiredService<ModuleCatalogue>();
        Assert.Contains(catalogue.All, m => m.Id == "patients");
        Assert.Contains(catalogue.All, m => m.Id == "core");
    }

    [Fact]
    public async Task Registers_patient_with_mrn_test_flag_ethnicity_and_audit()
    {
        await using var host = new PatientsHost();
        await using var scope = host.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<PatientService>();
        var r = await svc.RegisterAsync(Aroha(), "reception.a");

        Assert.True(r.Succeeded, string.Join("; ", r.Errors));
        Assert.Equal("TDS0000001", r.Patient!.Mrn);
        Assert.True(r.Patient.IsTestPatient);
        Assert.Equal("NGATAI", r.Patient.FamilyNameSearch);
        Assert.Single(r.Patient.Ethnicities);

        var second = await svc.RegisterAsync(new PatientRegistration("Smith", "John", new DateOnly(1970, 1, 1), BirthSex.Male), "reception.a");
        Assert.Equal("TDS0000002", second.Patient!.Mrn);

        var audit = await scope.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.ToListAsync();
        Assert.Equal(2, audit.Count(e => e.Action == "Create" && e.EntityType == "Patient"));
    }

    [Fact]
    public async Task Invalid_details_are_rejected_with_plain_messages()
    {
        await using var host = new PatientsHost();
        await using var scope = host.Scope();
        var r = await scope.ServiceProvider.GetRequiredService<PatientService>().RegisterAsync(
            new PatientRegistration("", "Kim", new DateOnly(2030, 1, 1), BirthSex.Unknown, "ABC1234",
                EthnicityCodes: ["1", "2", "3", "4", "5", "6", "7"], Email: "nope"), "reception.a");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Errors, e => e.Contains("Family name"));
        Assert.Contains(r.Errors, e => e.Contains("future"));
        Assert.Contains(r.Errors, e => e.StartsWith("NHI"));
        Assert.Contains(r.Errors, e => e.Contains("6 ethnicities"));
        Assert.Contains(r.Errors, e => e.Contains("Email"));
    }

    [Fact]
    public async Task Same_nhi_cannot_be_registered_twice()
    {
        await using var host = new PatientsHost();
        await using var scope = host.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<PatientService>();
        await svc.RegisterAsync(Aroha(), "reception.a");
        var again = await svc.RegisterAsync(Aroha() with { FamilyName = "Different" }, "reception.a");
        Assert.False(again.Succeeded);
        Assert.Single(again.PossibleDuplicates);
    }

    [Fact]
    public async Task Possible_duplicate_by_name_and_birth_date_needs_confirmation()
    {
        await using var host = new PatientsHost();
        await using var scope = host.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<PatientService>();
        await svc.RegisterAsync(Aroha(nhi: null), "reception.a");

        // Same person typed without the macron and without NHI.
        var dup = await svc.RegisterAsync(Aroha(nhi: null) with { FamilyName = "Ngatai" }, "reception.a");
        Assert.False(dup.Succeeded);
        Assert.Single(dup.PossibleDuplicates);

        var confirmed = await svc.RegisterAsync(Aroha(nhi: null) with { FamilyName = "Ngatai", ConfirmedNotDuplicate = true }, "reception.a");
        Assert.True(confirmed.Succeeded);
    }

    [Fact]
    public async Task Search_by_nhi_mrn_and_macron_insensitive_name()
    {
        await using var host = new PatientsHost();
        await using var scope = host.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<PatientService>();
        var p = (await svc.RegisterAsync(Aroha(), "reception.a")).Patient!;
        await svc.RegisterAsync(new PatientRegistration("Ngata", "Wiremu", new DateOnly(1965, 3, 3), BirthSex.Male), "reception.a");

        Assert.Single(await svc.SearchAsync("zsc21tn"));
        Assert.Single(await svc.SearchAsync(p.Mrn.ToLowerInvariant()));
        Assert.Equal(2, (await svc.SearchAsync("ngata")).Count);
        Assert.Single(await svc.SearchAsync("Ngatai, aroha"));
        Assert.Single(await svc.SearchAsync("ngāta", new DateOnly(1965, 3, 3)));
        Assert.Empty(await svc.SearchAsync("   "));
    }

    [Fact]
    public async Task Opening_a_record_and_adding_an_alert_are_audited()
    {
        await using var host = new PatientsHost();
        await using var scope = host.Scope();
        var svc = scope.ServiceProvider.GetRequiredService<PatientService>();
        var p = (await svc.RegisterAsync(Aroha(), "reception.a")).Patient!;
        await svc.AddAlertAsync(p.Id, AlertType.Allergy, AlertSeverity.Critical, "Penicillin – anaphylaxis", "nurse.a");
        var opened = await svc.GetAsync(p.Id, "nurse.a", "PREOP-02");
        Assert.Single(opened!.Alerts);

        var audit = await scope.ServiceProvider.GetRequiredService<PasDbContext>().AuditEvents.OrderBy(e => e.Sequence).ToListAsync();
        Assert.Contains(audit, e => e.Action == "View" && e.UserId == "nurse.a" && e.Workstation == "PREOP-02" && e.PatientId == p.Id.ToString());
        Assert.Contains(audit, e => e.EntityType == "PatientAlert");
        Assert.Null(AuditChain.FindFirstBreak(audit));
    }

    [Theory]
    [InlineData("Māori", "MAORI")]
    [InlineData("  te  Whānau ", "TE WHANAU")]
    [InlineData("O'Brien", "O'BRIEN")]
    public void Search_keys_ignore_case_macrons_and_extra_spaces(string input, string expected) =>
        Assert.Equal(expected, PatientService.SearchKey(input));
}
