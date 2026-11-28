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
            new BrightPAS.Modules.Credentialing.CredentialingModule(), new BrightPAS.Modules.Portal.PortalModule(), new BrightPAS.Modules.Messaging.MessagingModule(), new BrightPAS.Modules.Assets.AssetsModule(), new BrightPAS.Modules.Emr.EmrModule(), new BrightPAS.Modules.Endoscopy.EndoscopyModule(), new BrightPAS.Modules.Scribe.ScribeModule(), new BrightPAS.Modules.Devices.DevicesModule(), new BrightPAS.Modules.Quality.QualityModule(), new BrightPAS.Modules.Coding.CodingModule(), new BrightPAS.Modules.Estimates.EstimatesModule(), new BrightPAS.Modules.Triage.TriageModule(), new BrightPAS.Modules.Medicines.MedicinesModule(), new BrightPAS.Modules.EBookings.EBookingsModule(), new BrightPAS.Modules.FollowUp.FollowUpModule(), new BrightPAS.Modules.Research.ResearchModule(), new BrightPAS.Modules.Claims.ClaimsModule(), new BrightPAS.Modules.Consent.ConsentModule(), new BrightPAS.Modules.Payments.PaymentsModule(), new BrightPAS.Modules.Prescribing.PrescribingModule()]);
        s.AddLogging();
        s.AddSingleton<BrightPAS.Modules.Estimates.IEstimatePdf, TestEstimatePdf>();
        s.AddSingleton<TestEstimateMailer>();
        s.AddSingleton<BrightPAS.Modules.Estimates.IEstimateMailer>(sp => sp.GetRequiredService<TestEstimateMailer>());
        s.AddSingleton<IDocumentCipher, TestDocumentCipher>();
        s.AddSingleton<BrightPAS.Modules.Consent.IConsentPdf, TestConsentPdf>();
        s.AddSingleton<TestWindcave>();
        s.AddSingleton<BrightPAS.Modules.Payments.IWindcaveApi>(sp => sp.GetRequiredService<TestWindcave>());
        s.AddSingleton<BrightPAS.Modules.Payments.IStatementPdf, TestStatementPdf>();
        s.AddSingleton<TestNzeps>();
        s.AddSingleton<BrightPAS.Modules.Prescribing.INzepsApi>(sp => sp.GetRequiredService<TestNzeps>());
        s.AddSingleton<TestPrescriptionPdf>();
        s.AddSingleton<BrightPAS.Modules.Prescribing.IPrescriptionPdf>(sp => sp.GetRequiredService<TestPrescriptionPdf>());
        s.AddScoped<BrightPAS.Core.Organisation.OrganisationService>();
        Services = s.BuildServiceProvider();
        using var scope = Services.CreateScope();
        var sp = scope.ServiceProvider;
        foreach (DbContext db in new DbContext[] { sp.GetRequiredService<PasDbContext>(), sp.GetRequiredService<PatientsDbContext>(), sp.GetRequiredService<LabsDbContext>(),
                     sp.GetRequiredService<TheatreDbContext>(), sp.GetRequiredService<FormsDbContext>(), sp.GetRequiredService<BillingDbContext>(),
                     sp.GetRequiredService<RemindersDbContext>(), sp.GetRequiredService<ReferralsDbContext>(), sp.GetRequiredService<LettersDbContext>(), sp.GetRequiredService<RosterDbContext>(),
                     sp.GetRequiredService<BrightPAS.Modules.Credentialing.CredDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Portal.PortalDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Messaging.MsgDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Assets.AssetsDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Emr.EmrDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Endoscopy.EndoDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Scribe.ScribeDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Devices.DevicesDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Quality.QualityDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Coding.CodingDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Estimates.EstDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Triage.TriageDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Medicines.MedicinesDbContext>(), sp.GetRequiredService<BrightPAS.Modules.EBookings.EBookingsDbContext>(), sp.GetRequiredService<BrightPAS.Modules.FollowUp.FollowUpDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Research.ResearchDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Claims.ClaimsDbContext>() , sp.GetRequiredService<BrightPAS.Modules.Consent.ConsentDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Payments.PaymentsDbContext>(), sp.GetRequiredService<BrightPAS.Modules.Prescribing.RxDbContext>()})
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

/// <summary>Stand-in for the printed estimate: the text that matters, "encrypted" with the password so tests can check the rule.</summary>
public sealed class TestEstimatePdf : BrightPAS.Modules.Estimates.IEstimatePdf
{
    public byte[] Render(BrightPAS.Modules.Estimates.Estimate e, BrightPAS.Modules.Estimates.EstSettings s, string? password) =>
        System.Text.Encoding.UTF8.GetBytes((password is null ? "" : $"/Encrypt pw={password}\n") + $"{e.Ref}|{e.Title}|{e.Subtotal:0.00}|" +
            string.Join(";", e.Lines.OrderBy(l => l.Sequence).Select(l => $"{l.Kind}:{l.Label}={l.DisplayText ?? l.Amount?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}")));
}

/// <summary>Records what would be emailed; can be told to fail.</summary>
public sealed class TestEstimateMailer : BrightPAS.Modules.Estimates.IEstimateMailer
{
    public List<BrightPAS.Modules.Estimates.OutgoingEmail> Sent { get; } = [];
    public string? Problem { get; set; }
    public bool Fail { get; set; }
    public Task<string?> ConfigurationProblemAsync(CancellationToken ct) => Task.FromResult(Problem);
    public Task<BrightPAS.Modules.Estimates.MailSendResult> SendAsync(BrightPAS.Modules.Estimates.OutgoingEmail m, CancellationToken ct)
    {
        if (Fail) return Task.FromResult(new BrightPAS.Modules.Estimates.MailSendResult(false, "server down"));
        Sent.Add(m);
        return Task.FromResult(new BrightPAS.Modules.Estimates.MailSendResult(true, null));
    }
    public byte[] Draft(BrightPAS.Modules.Estimates.OutgoingEmail m) => System.Text.Encoding.UTF8.GetBytes($"X-Unsent: 1\nTo: {m.To}\nSubject: {m.Subject}\n\n{m.Body}");
}

/// <summary>A tiny valid PDF that names the consent (the real layout is MigraDoc in the web host).</summary>
public sealed class TestConsentPdf : BrightPAS.Modules.Consent.IConsentPdf
{
    public List<BrightPAS.Modules.Consent.ConsentPdfModel> Rendered { get; } = [];
    public byte[] Render(BrightPAS.Modules.Consent.ConsentPdfModel model)
    {
        Rendered.Add(model);
        return System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {model.Number} {(model.ConfirmedAt is null ? "signed" : "confirmed")}\n%%EOF\n");
    }
}

/// <summary>Fake Windcave: records sessions; tests set what a session query returns.</summary>
public sealed class TestWindcave : BrightPAS.Modules.Payments.IWindcaveApi
{
    public List<(string Reference, decimal Amount, string ReturnUrl, string NotifyUrl)> Created { get; } = [];
    public Dictionary<string, BrightPAS.Modules.Payments.GatewayResult> Results { get; } = [];
    public int Queries { get; private set; }
    public Task<BrightPAS.Modules.Payments.GatewayStart> CreateSessionAsync(string baseUrl, string user, string key, decimal amount, string merchantReference, string returnUrl, string notificationUrl, CancellationToken ct)
    {
        Created.Add((merchantReference, amount, returnUrl, notificationUrl));
        return Task.FromResult(new BrightPAS.Modules.Payments.GatewayStart($"https://uat.windcave.com/hpp/?id=S{Created.Count}", $"S{Created.Count}", null));
    }
    public Task<BrightPAS.Modules.Payments.GatewayResult> QuerySessionAsync(string baseUrl, string user, string key, string sessionId, CancellationToken ct)
    {
        Queries++;
        return Task.FromResult(Results.TryGetValue(sessionId, out var r) ? r : new BrightPAS.Modules.Payments.GatewayResult(false, false, false, null, null, null, null, null));
    }
    public List<(string TransactionId, decimal Amount, string Reference)> Refunds { get; } = [];
    public bool RefundOk { get; set; } = true;
    public Task<BrightPAS.Modules.Payments.GatewayRefund> RefundAsync(string baseUrl, string user, string key, string transactionId, decimal amount, string merchantReference, CancellationToken ct)
    {
        Refunds.Add((transactionId, amount, merchantReference));
        return Task.FromResult(RefundOk ? new BrightPAS.Modules.Payments.GatewayRefund(true, $"R{Refunds.Count}", null) : new BrightPAS.Modules.Payments.GatewayRefund(false, null, "DECLINED"));
    }
}

public sealed class TestStatementPdf : BrightPAS.Modules.Payments.IStatementPdf
{
    public List<BrightPAS.Modules.Payments.StatementPdfModel> Rendered { get; } = [];
    public byte[] Render(IReadOnlyList<BrightPAS.Modules.Payments.StatementPdfModel> statements)
    {
        Rendered.AddRange(statements);
        return System.Text.Encoding.ASCII.GetBytes($"%PDF-1.4\n% {string.Join(",", statements.Select(x => x.Number))}\n%%EOF\n");
    }
}

/// <summary>Fake NZePS: records what was sent and cancelled; tests can make it refuse.</summary>
public sealed class TestNzeps : BrightPAS.Modules.Prescribing.INzepsApi
{
    public List<string> Sent { get; } = [];
    public List<string> Cancelled { get; } = [];
    public string? Refuse { get; set; }
    public Task<(bool Ok, string? Reference, string? Error)> SendAsync(string url, string? clientId, string? key, string fhirJson, CancellationToken ct)
    {
        if (Refuse is not null) return Task.FromResult<(bool, string?, string?)>((false, null, Refuse));
        Sent.Add(fhirJson);
        return Task.FromResult<(bool, string?, string?)>((true, $"NZEPS-{Sent.Count}", null));
    }
    public Task<(bool Ok, string? Error)> CancelAsync(string url, string? clientId, string? key, string nzepsId, string reason, CancellationToken ct)
    {
        Cancelled.Add(nzepsId);
        return Task.FromResult<(bool, string?)>((true, null));
    }
}

public sealed class TestPrescriptionPdf : BrightPAS.Modules.Prescribing.IPrescriptionPdf
{
    public List<BrightPAS.Modules.Prescribing.PrescriptionPdfModel> Rendered { get; } = [];
    public byte[] Render(BrightPAS.Modules.Prescribing.PrescriptionPdfModel m) { Rendered.Add(m); return "%PDF-test"u8.ToArray(); }
}
