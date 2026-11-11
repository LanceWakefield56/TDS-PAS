using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Core.Patients;
using BrightPAS.Core.Setup;
using BrightPAS.Identity;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;
using Cred = BrightPAS.Modules.Credentialing;
using Let = BrightPAS.Modules.Letters;
using Msg = BrightPAS.Modules.Messaging;
using Por = BrightPAS.Modules.Portal;
using Rem = BrightPAS.Modules.Reminders;
using Ros = BrightPAS.Modules.Rostering;

namespace BrightPAS.Web.Demo;

/// <summary>Sign-in details for the demonstration accounts (shown by Setup and on the sign-in page of a demo system).</summary>
public static class DemoAccounts
{
    /// <summary>Every demonstration account uses this password.</summary>
    public const string Password = "Demo-Passphrase-2026";
    /// <summary>Shared authenticator-app secret (base32) for every demonstration account – add it once to an authenticator app.</summary>
    public const string TotpSecret = "BRIGHTPASDEMOKEY";
    public static string OtpAuthUri => $"otpauth://totp/BrightPAS%20demo?secret={TotpSecret}&issuer=BrightPAS%20demo&digits=6&period=30";

    public sealed record Account(string UserName, string DisplayName, string[] Roles, string Purpose);

    public static readonly IReadOnlyList<Account> All =
    [
        new("admin", "Sam Carter (System Administrator)", [DefaultRoles.SystemAdministrator], "Set-up, users, modules, organisation"),
        new("privacy", "Jo Ellis (Privacy Officer)", [DefaultRoles.PrivacyOfficer], "Audit, break-glass review, merges"),
        new("reception", "Kiri Walker", [DefaultRoles.Reception], "Front desk: patients, bookings, letters, portal links"),
        new("reception2", "Megan Doyle", [DefaultRoles.Reception], "Second receptionist"),
        new("nurse", "Hana Ruawai RN", [DefaultRoles.Nurse], "Pre-admission, day-of-surgery, results"),
        new("nurse2", "Claire Bishop RN", [DefaultRoles.Nurse], "Theatre nurse"),
        new("nurse3", "Ana Faleolo RN", [DefaultRoles.Nurse], "Recovery nurse"),
        new("hreid", "Dr Hamish Reid", [DefaultRoles.Surgeon], "General surgeon"),
        new("pnatarajan", "Dr Priya Natarajan", [DefaultRoles.Surgeon], "General / colorectal surgeon and endoscopist"),
        new("twhitaker", "Dr Tom Whitaker", [DefaultRoles.Surgeon], "Orthopaedic hand surgeon"),
        new("mtipene", "Dr Mereana Tipene", [DefaultRoles.Surgeon], "Plastic surgeon"),
        new("slindqvist", "Dr Sarah Lindqvist", [DefaultRoles.Surgeon], "Gastroenterologist (endoscopy)"),
        new("bkowalski", "Dr Ben Kowalski", [DefaultRoles.Anaesthetist], "Anaesthetist"),
        new("fgrant", "Dr Fiona Grant", [DefaultRoles.Anaesthetist], "Anaesthetist"),
        new("rmehta", "Dr Raj Mehta", [DefaultRoles.Anaesthetist], "Anaesthetist (endoscopy sedation)"),
        new("cssd", "Lee Turner", [DefaultRoles.CssdTechnician], "Sterile services technician"),
        new("finance", "Grace Liu", [DefaultRoles.Finance], "Estimates, invoices, payments, fees"),
        new("pa", "Rachel Moore", [DefaultRoles.PracticeAssistant], "PA for Dr Reid and Dr Whitaker"),
        new("integration", "Chris Patel", [DefaultRoles.IntegrationAdministrator], "Interfaces, mappings, flows"),
        new("integration2", "Dana Fox", [DefaultRoles.IntegrationAdministrator], "Second approver for mapping changes"),
        new("roster", "Julie Martin", [DefaultRoles.RosterManager, DefaultRoles.Nurse], "Nurse manager – writes the roster"),
        new("loconnor", "Dr Liam O'Connor", [DefaultRoles.SpecialistPortal], "Visiting urologist (portal only)"),
    ];
}

/// <summary>
/// Fills an EMPTY BrightPAS database with a realistic, fully set-up demonstration practice (all names, numbers and
/// people are invented; NHIs are test-range Z numbers). Everything is created through the same services the screens use,
/// so the data passes the same checks as data typed in by staff. Never runs on a database that already has users.
/// </summary>
public sealed class DemoDataSeeder(IServiceProvider sp, ILogger<DemoDataSeeder> log)
{
    public const string StateKey = "demo.state";          // requested | running | done | failed
    public const string DetailKey = "demo.detail";
    public const string Actor = "demo-loader";

    private readonly TimeProvider _clock = sp.GetRequiredService<TimeProvider>();
    private readonly Random _rng = new(20261111);
    private DateOnly _today;
    private readonly Dictionary<string, Guid> _clin = [];
    private readonly Dictionary<string, Guid> _loc = [];
    private readonly List<Patient> _patients = [];
    private readonly Dictionary<Guid, string> _practiceOf = [];

    private T S<T>() where T : notnull => sp.GetRequiredService<T>();

    public sealed record Summary(int Users, int Patients, int Sessions, int Bookings, int Referrals, int Invoices, int Letters, int LabReports, int Messages);

    public async Task<Summary> RunAsync(CancellationToken ct = default)
    {
        var users = S<UserManager<PasUser>>();
        if (await users.Users.AnyAsync(ct)) throw new InvalidOperationException("Demonstration data can only be loaded into a new, empty BrightPAS database (this one already has user accounts).");
        _today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var start = _today.AddDays(-70);

        using (At(start.AddDays(-14), 9))
        {
            await OrganisationAsync(ct);
            await UsersAsync(ct);
            await SettingsAsync(ct);
            await GpPracticesAsync(ct);
            await PatientsAsync(ct);
            await CredentialingAsync(ct);
            await RosterSetupAsync(start, ct);
            await TheatreTemplatesAsync(start, ct);
        }
        await ReferralsAsync(ct);
        await BookingsAsync(ct);
        await AppointmentsAsync(ct);
        await FormsAsync(ct);
        await BillingAsync(ct);
        await LabsAsync(ct);
        await LettersAsync(ct);
        await PatientRecordsAsync(ct);
        await PortalAsync(ct);
        await RosterActivityAsync(ct);
        await MessagingAsync(ct);
        await RemindersAsync(ct);
        await CompleteModuleSetupAsync(ct);

        var core = S<PasDbContext>();
        var summary = new Summary(await users.Users.CountAsync(ct), _patients.Count,
            await S<TheatreDbContext>().Sessions.CountAsync(ct), await S<TheatreDbContext>().Bookings.CountAsync(ct),
            await S<ReferralsDbContext>().Referrals.CountAsync(ct), await S<BillingDbContext>().Documents.CountAsync(d => d.Kind == DocumentKind.Invoice, ct),
            await S<Let.LettersDbContext>().Letters.CountAsync(ct), await S<LabsDbContext>().Reports.CountAsync(ct), await S<Msg.MsgDbContext>().Messages.CountAsync(ct));
        log.LogInformation("Demonstration data loaded: {Summary}", summary);
        return summary;
    }

    // ------------------------------------------------------------------ helpers

    private IDisposable At(DateOnly date, int hour, int minute = 0)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        return DemoClock.At(new DateTimeOffset(local, _clock.LocalTimeZone.GetUtcOffset(local)));
    }

    private static void Ok(IReadOnlyList<string> errors, string what)
    {
        if (errors.Count > 0) throw new InvalidOperationException($"Demo data – {what}: {string.Join(" ", errors)}");
    }
    private static void Ok(string? error, string what) { if (error is not null) throw new InvalidOperationException($"Demo data – {what}: {error}"); }
    private static T Must<T>(T? value, IReadOnlyList<string> errors, string what) where T : class
    {
        Ok(errors, what);
        return value ?? throw new InvalidOperationException($"Demo data – {what}: nothing was created.");
    }

    private T Pick<T>(IReadOnlyList<T> list) => list[_rng.Next(list.Count)];
    private bool Chance(double p) => _rng.NextDouble() < p;
    private static bool Weekday(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    // ------------------------------------------------------------------ organisation, users, settings

    private async Task OrganisationAsync(CancellationToken ct)
    {
        var org = S<OrganisationService>();
        Ok(await org.UpdateDetailsAsync(new OrganisationDetails("Tasman Day Surgery (DEMO)", "G00DEM-O", "Tasman Day Surgery – main site", "F00DEM-O",
            "1 Demonstration Way", null, "Richmond", "Nelson", "7020", "03 555 0100", "reception@demo.brightpas.nz"), Actor, ct), "organisation details");

        var core = S<PasDbContext>();
        var surgical = await core.Facilities.SingleAsync(f => f.Type == FacilityType.SurgicalSuite, ct);
        var site = await core.Sites.FirstAsync(ct);
        var endo = new Facility { SiteId = site.Id, Name = "Endoscopy suite", Type = FacilityType.Endoscopy };
        var clinic = new Facility { SiteId = site.Id, Name = "Outpatient clinic", Type = FacilityType.Clinic };
        core.Facilities.AddRange(endo, clinic);
        await core.SaveChangesAsync(ct);
        Ok((await org.SaveLocationAsync(new LocationEdit(null, endo.Id, "PR1", "Procedure room 1 (endoscopy)", LocationType.ProcedureRoom, true, 15), Actor, ct)).Error, "procedure room");
        Ok((await org.SaveLocationAsync(new LocationEdit(null, clinic.Id, "CR1", "Consult room 1", LocationType.ConsultRoom, true, 60), Actor, ct)).Error, "consult room 1");
        Ok((await org.SaveLocationAsync(new LocationEdit(null, clinic.Id, "CR2", "Consult room 2", LocationType.ConsultRoom, true, 61), Actor, ct)).Error, "consult room 2");
        foreach (var l in await core.Locations.AsNoTracking().ToListAsync(ct)) _loc[l.Code] = l.Id;

        (string Key, string Name, ClinicianType Type, string Specialty, string Cpn, string Practice, string? User)[] people =
        [
            ("reid", "Dr Hamish Reid", ClinicianType.Surgeon, "General surgery", "11DEMA", "Reid Surgical (demo)", "hreid"),
            ("natarajan", "Dr Priya Natarajan", ClinicianType.Surgeon, "Colorectal surgery and endoscopy", "12DEMB", "Nelson Colorectal (demo)", "pnatarajan"),
            ("whitaker", "Dr Tom Whitaker", ClinicianType.Surgeon, "Orthopaedic hand surgery", "13DEMC", "Top of the South Hand Clinic (demo)", "twhitaker"),
            ("tipene", "Dr Mereana Tipene", ClinicianType.Surgeon, "Plastic and reconstructive surgery", "14DEMD", "Tipene Plastics (demo)", "mtipene"),
            ("lindqvist", "Dr Sarah Lindqvist", ClinicianType.Endoscopist, "Gastroenterology", "15DEME", "Tasman Gastro (demo)", "slindqvist"),
            ("oconnor", "Dr Liam O'Connor", ClinicianType.Surgeon, "Urology (visiting)", "16DEMF", "Marlborough Urology (demo)", "loconnor"),
            ("kowalski", "Dr Ben Kowalski", ClinicianType.Anaesthetist, "Anaesthesia", "21DEMG", "Nelson Anaesthetic Group (demo)", "bkowalski"),
            ("grant", "Dr Fiona Grant", ClinicianType.Anaesthetist, "Anaesthesia", "22DEMH", "Nelson Anaesthetic Group (demo)", "fgrant"),
            ("mehta", "Dr Raj Mehta", ClinicianType.Anaesthetist, "Anaesthesia and sedation", "23DEMJ", "Nelson Anaesthetic Group (demo)", "rmehta"),
        ];
        var colour = 0;
        foreach (var p in people)
        {
            var (c, errors) = await org.SaveClinicianAsync(new ClinicianEdit(null, p.Name, p.Type, p.Specialty, p.Cpn, "03 555 01" + (20 + colour), $"{p.Key}@demo.brightpas.nz",
                p.Practice, p.User, true, colour++), Actor, ct);
            _clin[p.Key] = Must(c, errors, "clinician " + p.Name).Id;
        }

        await org.AddExampleProceduresAsync(Actor, ct);
        (string Code, string Name, string Service, int Min, string An, bool Side)[] more =
        [
            ("TRIGGER", "Trigger finger release", "Surgical", 20, "Local", true),
            ("DUPUY", "Dupuytren's fasciectomy", "Surgical", 60, "Regional", true),
            ("GANGLION", "Excision of ganglion", "Surgical", 30, "Regional", true),
            ("BCC", "Excision of basal cell carcinoma with flap repair", "Surgical", 45, "Local", false),
            ("BLEPH", "Upper eyelid blepharoplasty", "Surgical", 60, "Local", false),
            ("SCAR", "Scar revision", "Surgical", 45, "General", false),
            ("CYSTO", "Flexible cystoscopy", "Surgical", 20, "Local", false),
            ("VASECT", "Vasectomy", "Surgical", 30, "Local", false),
            ("PILONID", "Pilonidal sinus excision", "Surgical", 45, "General", false),
            ("POLYPECT", "Colonoscopy with polypectomy", "Endoscopy", 45, "Sedation", false),
        ];
        foreach (var m in more)
            Ok((await org.SaveProcedureAsync(new ProcedureEdit(null, m.Code, m.Name, m.Service, m.Min, m.An, m.Side, null, true), Actor, ct)).Errors, "procedure " + m.Code);
        var fees = new Dictionary<string, string> { ["COLON"] = "COLON", ["GASTRO"] = "GASTRO", ["CARPAL"] = "CTR", ["INGHERNIA"] = "HERNIA", ["POLYPECT"] = "COLON", ["GASTROCOL"] = "GASTROCOL",
            ["LAPCHOLE"] = "LAPCHOLE", ["UMBHERNIA"] = "UMBHERNIA", ["SKINLES"] = "SKINLES", ["TRIGGER"] = "TRIGGER", ["BCC"] = "BCC", ["FLEXSIG"] = "FLEXSIG" };
        foreach (var p in await core.Procedures.Where(p => fees.Keys.Contains(p.Code)).ToListAsync(ct)) p.FeeCode = fees[p.Code];
        await core.SaveChangesAsync(ct);
    }

    private async Task UsersAsync(CancellationToken ct)
    {
        var users = S<UserManager<PasUser>>();
        var now = _clock.GetUtcNow();
        foreach (var a in DemoAccounts.All)
        {
            var u = new PasUser { UserName = a.UserName, DisplayName = a.DisplayName, Email = $"{a.UserName}@demo.brightpas.nz", EmailConfirmed = true, CreatedAt = now };
            var r = await users.CreateAsync(u, DemoAccounts.Password);
            if (!r.Succeeded) throw new InvalidOperationException($"Demo data – user {a.UserName}: {string.Join(" ", r.Errors.Select(e => e.Description))}");
            foreach (var role in a.Roles) { var rr = await users.AddToRoleAsync(u, role); if (!rr.Succeeded) throw new InvalidOperationException($"Demo data – role {role}: {string.Join(" ", rr.Errors.Select(e => e.Description))}"); }
            await users.SetAuthenticationTokenAsync(u, "[AspNetUserStore]", "AuthenticatorKey", DemoAccounts.TotpSecret);
            await users.SetTwoFactorEnabledAsync(u, true);
        }
        // The installer's one-time first-administrator code is no longer needed.
        var core = S<PasDbContext>();
        var state = await core.SetupState.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (state is null) core.SetupState.Add(state = new SetupState());
        state.BootstrapCodeUsedAt = now;
        await core.SaveChangesAsync(ct);
        await S<BrightPAS.Core.Audit.AuditWriter>().WriteAsync(Actor, "Create", "User", "demo", reason: $"{DemoAccounts.All.Count} demonstration accounts", ct: ct);
    }

    private async Task SettingsAsync(CancellationToken ct)
    {
        var settings = S<SettingsService>();
        await settings.SetAsync("demo.banner", "true", Actor, ct);

        var billing = S<BillingService>();
        Ok(await billing.UpdateSettingsAsync(40, 20, 15, 30, 20, "123-456-789", "02-0800-0123456-000 (DEMO)", Actor, ct), "billing settings");
        await billing.SeedExampleFeesAsync(ct);
        foreach (var f in new[]
                 {
                     new FeeItem { Code = "LAPCHOLE", Description = "Laparoscopic cholecystectomy – procedure fee", Price = 4200m, Split = true },
                     new FeeItem { Code = "UMBHERNIA", Description = "Umbilical hernia repair – procedure fee", Price = 2600m, Split = true },
                     new FeeItem { Code = "SKINLES", Description = "Excision of skin lesion – procedure fee", Price = 690m, Split = true },
                     new FeeItem { Code = "TRIGGER", Description = "Trigger finger release – procedure fee", Price = 1250m, Split = true },
                     new FeeItem { Code = "BCC", Description = "BCC excision with flap – procedure fee", Price = 1650m, Split = true },
                     new FeeItem { Code = "FLEXSIG", Description = "Flexible sigmoidoscopy – procedure fee", Price = 780m, Split = true },
                     new FeeItem { Code = "GASTROCOL", Description = "Gastroscopy and colonoscopy – procedure fee", Price = 2150m, Split = true },
                     new FeeItem { Code = "CONSULT", Description = "Specialist consultation", Price = 260m, Split = true, SurgeonPercent = 100, AnaesthetistPercent = 0 },
                     new FeeItem { Code = "ANAES-REV", Description = "Pre-anaesthetic review", Price = 180m, Split = true, SurgeonPercent = 0, AnaesthetistPercent = 100 },
                 })
            Ok(await billing.SaveFeeAsync(f, Actor, ct), "fee " + f.Code);

        Ok(await S<ReferralService>().UpdateSettingsAsync(30, 90, 120, Actor, ct), "waiting-time targets");
        Ok(await S<ReferralService>().UpdateNbrsCodesAsync(new ReferralService.NbrsCodes("DEMO", "DEMO1", "S00", "M10"), Actor, ct), "NBRS codes");

        await S<Let.LetterService>().EnsureDefaultTemplatesAsync(ct);
        Ok(await S<Let.HealthLinkService>().UpdateSettingsAsync(new Let.HealthLinkSettingsEdit(Let.HealthLinkMode.TestOutbox, "tdsdemo", null, null), Actor, ct), "HealthLink settings");

        var labels = S<LabelService>();
        Ok(await labels.SavePrinterAsync(new PrinterEdit(null, "Admissions wristbands (Zebra)", PrinterKind.Wristband, "192.168.1.160", 9100, 300, 25, 254, true, true), Actor, ct), "wristband printer");
        Ok(await labels.SavePrinterAsync(new PrinterEdit(null, "Reception labels (Zebra)", PrinterKind.Label, "192.168.1.161", 9100, 203, 57, 32, true, true), Actor, ct), "label printer");
        Ok(await labels.SavePrinterAsync(new PrinterEdit(null, "Nurses' station labels (DYMO)", PrinterKind.Label, "", 0, 300, 57, 32, false, true, PrinterLanguage.Dymo, "11354"), Actor, ct), "DYMO printer");
    }

    private static readonly (string Name, string Hpi, string Edi, string Address, string City)[] Practices =
    [
        ("Richmond Family Health (demo)", "F0DEM1-A", "richfam", "20 Queen Street", "Richmond"),
        ("Stoke Medical Centre (demo)", "F0DEM2-B", "stokemc", "5 Main Road", "Stoke"),
        ("Nelson City Doctors (demo)", "F0DEM3-C", "nelcity", "100 Hardy Street", "Nelson"),
        ("Motueka Health (demo)", "F0DEM4-D", "motuhlth", "12 High Street", "Motueka"),
        ("Brightwater Medical (demo)", "F0DEM5-E", "brightmd", "3 Ellis Street", "Brightwater"),
        ("Wakefield Health Centre (demo)", "F0DEM6-F", "wakehc", "40 Edward Street", "Wakefield"),
    ];

    private async Task GpPracticesAsync(CancellationToken ct)
    {
        var letters = S<Let.LetterService>();
        foreach (var p in Practices)
            Ok(await letters.SavePracticeAsync(new Let.PracticeEdit(null, p.Name, p.Hpi, p.Edi, p.Address, null, p.City, "70" + _rng.Next(10, 99), "03 555 02" + _rng.Next(10, 99),
                $"{p.Edi}@demo.brightpas.nz", Let.DeliveryChannel.HealthLink, true), Actor, ct), "GP practice " + p.Name);
    }

    // ------------------------------------------------------------------ patients

    private static readonly (string Family, string Given, char Sex, string Eth)[] People =
    [
        ("Ngātai", "Aroha Mere", 'F', "21"), ("Thompson", "James Robert", 'M', "11"), ("Wilson", "Sophie Anne", 'F', "11"), ("Te Huia", "Rawiri", 'M', "21"),
        ("Smith", "Margaret Joy", 'F', "11"), ("Faleafa", "Sione", 'M', "31"), ("Patel", "Anjali", 'F', "43"), ("Brown", "David John", 'M', "11"),
        ("Chen", "Wei", 'M', "42"), ("Taylor", "Emma Louise", 'F', "11"), ("Parata", "Hēmi", 'M', "21"), ("Anderson", "Lucy", 'F', "11"),
        ("Williams", "Peter Graham", 'M', "11"), ("Tuilagi", "Mele", 'F', "31"), ("Kim", "Ji-woo", 'F', "44"), ("Jones", "Michael", 'M', "11"),
        ("Walker", "Ruth Elizabeth", 'F', "11"), ("Tapsell", "Wiremu", 'M', "21"), ("Singh", "Harpreet", 'M', "43"), ("Martin", "Olivia", 'F', "11"),
        ("Robinson", "Gary", 'M', "11"), ("Hohaia", "Moana", 'F', "21"), ("Clarke", "Barbara", 'F', "11"), ("Nguyen", "Minh", 'M', "41"),
        ("Hall", "Christopher", 'M', "11"), ("Pōtiki", "Tania", 'F', "21"), ("Young", "Kevin", 'M', "11"), ("Lauina", "Losa", 'F', "31"),
        ("Wright", "Helen Mary", 'F', "11"), ("King", "Andrew", 'M', "11"), ("Reihana", "Ngaio", 'F', "21"), ("Scott", "Daniel", 'M', "11"),
        ("Green", "Judith", 'F', "11"), ("Baker", "Stephen Paul", 'M', "11"), ("Wang", "Li", 'F', "42"), ("Adams", "Rebecca", 'F', "11"),
        ("Tamihana", "Pita", 'M', "21"), ("Mitchell", "Susan", 'F', "11"), ("Campbell", "Ian", 'M', "11"), ("Fonoti", "Tavita", 'M', "31"),
        ("Evans", "Natalie", 'F', "11"), ("Turner", "Graeme", 'M', "11"), ("Kereama", "Huia", 'F', "21"), ("Phillips", "Mark", 'M', "11"),
        ("Hughes", "Catherine", 'F', "11"), ("Sharma", "Rohan", 'M', "43"), ("Edwards", "Jennifer", 'F', "11"), ("Collins", "Brian", 'M', "11"),
        ("Rangi", "Mihi", 'F', "21"), ("Stewart", "Alan", 'M', "11"), ("Morris", "Diane", 'F', "11"), ("Lealaiauloto", "Ioane", 'M', "31"),
        ("Murphy", "Kathleen", 'F', "11"), ("Cook", "Trevor", 'M', "11"), ("Wairau", "Kahu", 'M', "21"), ("Rogers", "Pamela", 'F', "11"),
        ("Bell", "Nigel", 'M', "11"), ("Liu", "Mei", 'F', "42"), ("Ward", "Sharon", 'F', "11"), ("Cooper", "Grant", 'M', "11"),
        ("Rāwiri", "Anahera", 'F', "21"), ("Richardson", "Philip", 'M', "11"), ("Cox", "Joanne", 'F', "11"), ("Howard", "Raymond", 'M', "11"),
        ("Fifita", "Salote", 'F', "33"), ("Gray", "Malcolm", 'M', "11"), ("Hēnare", "Tui", 'F', "21"), ("Watson", "Lynette", 'F', "11"),
        ("Brooks", "Craig", 'M', "11"), ("Kaur", "Simran", 'F', "43"), ("Kelly", "Sean", 'M', "11"), ("Price", "Wendy", 'F', "11"),
        ("Ruatapu", "Manaia", 'M', "21"), ("Bennett", "Fiona", 'F', "11"), ("Wood", "Russell", 'M', "11"), ("Ross", "Heather", 'F', "11"),
        ("Takerei", "Ataahua", 'F', "21"), ("Jenkins", "Dennis", 'M', "11"), ("Perry", "Carol", 'F', "11"), ("Powell", "Neil", 'M', "11"),
        ("Tupou", "Viliami", 'M', "33"), ("Long", "Gillian", 'F', "11"), ("Hunter", "Jason", 'M', "11"), ("Paewai", "Kiri", 'F', "21"),
        ("Foster", "Elaine", 'F', "11"), ("Simmonds", "Bruce", 'M', "11"), ("Ellis", "Toni", 'F', "11"), ("Matthews", "Owen", 'M', "11"),
        ("Hira", "Rongo", 'M', "21"), ("Fraser", "Moira", 'F', "11"),
    ];

    private static readonly string[] Streets = ["Kōwhai Street", "Totara Avenue", "Rimu Place", "Queen Street", "Salisbury Road", "Champion Road", "Main Road",
        "Hill Street", "Oxford Street", "Wensley Road", "Bateup Road", "Lower Queen Street", "Ranzau Road", "Pomona Road", "Aniseed Valley Road"];
    private static readonly (string Suburb, string City, string Postcode, int Practice)[] Places =
    [
        ("Richmond", "Richmond", "7020", 0), ("Stoke", "Nelson", "7011", 1), ("Tāhunanui", "Nelson", "7011", 2), ("Nelson South", "Nelson", "7010", 2),
        ("Atawhai", "Nelson", "7010", 2), ("Motueka", "Motueka", "7120", 3), ("Māpua", "Māpua", "7005", 3), ("Brightwater", "Brightwater", "7022", 4),
        ("Wakefield", "Wakefield", "7025", 5), ("Hope", "Richmond", "7020", 0),
    ];

    /// <summary>Valid test-range NHIs (they start with Z and pass the check-digit rule), generated in a fixed order.</summary>
    private static IEnumerable<string> TestNhis()
    {
        const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        foreach (var a in letters)
            foreach (var b in letters)
                for (var n = 100; n < 10000; n += 37)
                {
                    var nhi = $"Z{a}{b}{n:D4}";
                    var v = NhiValidator.Validate(nhi);
                    if (v.IsValid && v.IsTestNumber) yield return nhi;
                }
    }

    private async Task PatientsAsync(CancellationToken ct)
    {
        var svc = S<PatientService>();
        using var nhis = TestNhis().Where((_, i) => i % 3 == 0).GetEnumerator();
        var i = 0;
        foreach (var p in People)
        {
            nhis.MoveNext();
            var place = Places[i % Places.Length];
            var age = 22 + _rng.Next(0, 62);
            var dob = _today.AddYears(-age).AddDays(-_rng.Next(0, 360));
            var given = p.Given.Split(' ')[0];
            var reg = new PatientRegistration(p.Family, p.Given, dob, p.Sex == 'F' ? BirthSex.Female : BirthSex.Male,
                Nhi: i % 23 == 22 ? null : nhis.Current, PreferredName: i % 9 == 4 ? given[..Math.Min(given.Length, 4)] : null,
                MobilePhone: $"021 555 {1000 + i * 7:D4}", HomePhone: i % 4 == 0 ? $"03 544 {2000 + i:D4}" : null,
                Email: i % 3 == 0 ? null : $"{given.ToLowerInvariant().Replace("ā", "a").Replace("ē", "e").Replace("ō", "o")}.{i}@example.nz",
                AddressLine1: $"{1 + _rng.Next(1, 220)} {Streets[_rng.Next(Streets.Length)]}", Suburb: place.Suburb, City: place.City, Postcode: place.Postcode,
                EthnicityCodes: [p.Eth switch { "11" => "11111", "21" => "21111", "31" => "31111", "33" => "33111", "41" => "41411", "42" => "42111", "43" => "43111", "44" => "44311", _ => "61199" }], GpPracticeHpiFacilityId: Practices[place.Practice].Hpi, ConfirmedNotDuplicate: true);
            var r = await svc.RegisterAsync(reg, "reception", ct);
            var patient = Must(r.Patient, r.Errors, $"patient {p.Family}");
            _patients.Add(patient);
            _practiceOf[patient.Id] = Practices[place.Practice].Name;
            i++;
        }
        // Alerts staff would have recorded.
        await svc.AddAlertAsync(_patients[2].Id, AlertType.Allergy, AlertSeverity.Critical, "Penicillin – anaphylaxis (2015)", "nurse", ct);
        await svc.AddAlertAsync(_patients[7].Id, AlertType.Allergy, AlertSeverity.Warning, "Latex – contact dermatitis", "nurse", ct);
        await svc.AddAlertAsync(_patients[12].Id, AlertType.Infection, AlertSeverity.Warning, "MRSA carrier – screened positive 2025; contact precautions", "nurse", ct);
        await svc.AddAlertAsync(_patients[19].Id, AlertType.Clinical, AlertSeverity.Critical, "Difficult airway – grade 4 view; video laryngoscope", "bkowalski", ct);
        await svc.AddAlertAsync(_patients[25].Id, AlertType.Allergy, AlertSeverity.Warning, "Codeine – severe vomiting", "nurse", ct);
        await svc.AddAlertAsync(_patients[31].Id, AlertType.Clinical, AlertSeverity.Warning, "Type 1 diabetes – insulin pump; first on list", "nurse", ct);
        await svc.AddAlertAsync(_patients[40].Id, AlertType.Safety, AlertSeverity.Information, "Hard of hearing – face patient when speaking", "reception", ct);
        await svc.AddAlertAsync(_patients[48].Id, AlertType.Administrative, AlertSeverity.Information, "Prefers contact by email, not phone", "reception", ct);
        await svc.AddAlertAsync(_patients[55].Id, AlertType.Clinical, AlertSeverity.Warning, "Malignant hyperthermia – family history; trigger-free anaesthetic", "fgrant", ct);
    }

    private Patient P(int i) => _patients[i % _patients.Count];

    // ------------------------------------------------------------------ credentialing

    private async Task CredentialingAsync(CancellationToken ct)
    {
        var svc = S<Cred.CredentialingService>();
        Ok(await svc.UpdateSettingsAsync(new Cred.CredSettingsEdit([Cred.CredentialType.Apc, Cred.CredentialType.Indemnity, Cred.CredentialType.CredentialingApproval], 60, true), Actor, ct), "credentialing settings");
        var n = 0;
        foreach (var (key, id) in _clin)
        {
            n++;
            var apcExpiry = key == "tipene" ? _today.AddDays(21) : new DateOnly(_today.Month >= 4 ? _today.Year + 1 : _today.Year, 3, 31);
            var indemnityExpiry = key == "oconnor" ? _today.AddDays(-5) : _today.AddMonths(4 + n);
            Ok(await svc.SaveCredentialAsync(new Cred.CredentialEdit(null, id, Cred.CredentialType.Apc, $"APC-{2026}-{1000 + n}", "Medical Council of New Zealand", apcExpiry.AddYears(-1).AddDays(1), apcExpiry, null, true), Actor, ct), "APC " + key);
            Ok(await svc.SaveCredentialAsync(new Cred.CredentialEdit(null, id, Cred.CredentialType.Indemnity, $"MPS-{50000 + n * 13}", "Medical Protection Society", indemnityExpiry.AddYears(-1), indemnityExpiry, null, true), Actor, ct), "indemnity " + key);
            Ok(await svc.SaveCredentialAsync(new Cred.CredentialEdit(null, id, Cred.CredentialType.CredentialingApproval, $"TDS-CR-{n:D3}", "TDS Credentialing Committee", _today.AddYears(-1).AddDays(-n * 9), _today.AddYears(2).AddDays(-n * 9), null, true), Actor, ct), "approval " + key);
            if (key is "kowalski" or "grant" or "mehta")
                Ok(await svc.SaveCredentialAsync(new Cred.CredentialEdit(null, id, Cred.CredentialType.Resuscitation, "ACLS", "NZ Resuscitation Council", _today.AddMonths(-8), _today.AddMonths(16), null, true), Actor, ct), "resus " + key);
        }
        foreach (var (key, service, review) in new[] { ("reid", "Surgical", 300), ("natarajan", "Surgical", 200), ("natarajan", "Endoscopy", 200), ("whitaker", "Surgical", 400), ("tipene", "Surgical", 150),
                     ("lindqvist", "Endoscopy", 500), ("oconnor", "Surgical", 30) })
            Ok(await svc.SavePrivilegeAsync(new Cred.PrivilegeEdit(null, _clin[key], service, null, _today.AddYears(-1), _today.AddDays(review), key == "oconnor" ? "Cystoscopy and vasectomy only" : null, true), Actor, ct), "scope " + key);
    }

    // ------------------------------------------------------------------ rostering

    private readonly Dictionary<string, Guid> _staff = [];

    private async Task RosterSetupAsync(DateOnly start, CancellationToken ct)
    {
        var svc = S<Ros.RosterService>();
        (string Key, string Name, Ros.StaffRole Role, string? User, decimal Hours)[] staff =
        [
            ("julie", "Julie Martin", Ros.StaffRole.Manager, "roster", 40), ("hana", "Hana Ruawai", Ros.StaffRole.Nurse, "nurse", 40),
            ("claire", "Claire Bishop", Ros.StaffRole.Nurse, "nurse2", 32), ("ana", "Ana Faleolo", Ros.StaffRole.Nurse, "nurse3", 32),
            ("mark", "Mark Dunn", Ros.StaffRole.AnaestheticTechnician, null, 40), ("tia", "Tia Rāwhiti", Ros.StaffRole.AnaestheticTechnician, null, 24),
            ("sue", "Sue Hart", Ros.StaffRole.HealthCareAssistant, null, 30), ("kiri", "Kiri Walker", Ros.StaffRole.Reception, "reception", 40),
            ("megan", "Megan Doyle", Ros.StaffRole.Reception, "reception2", 20), ("lee", "Lee Turner", Ros.StaffRole.Cssd, "cssd", 40),
        ];
        var colour = 0;
        foreach (var s in staff)
        {
            var (m, errors) = await svc.SaveStaffAsync(new Ros.StaffEdit(null, s.Name, s.Role, s.User, colour++, s.Hours, $"027 555 {3000 + colour:D4}", true, $"EMP{100 + colour}", "Ordinary Hours"), Actor, ct);
            _staff[s.Key] = Must(m, errors, "staff " + s.Name).Id;
        }
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        async Task Template(string key, DayOfWeek d, int sh, int sm, int eh, int em, string area) =>
            Ok(await svc.SaveTemplateAsync(new Ros.ShiftTemplateEdit(null, _staff[key], d, false, new TimeOnly(sh, sm), new TimeOnly(eh, em), 30, area, 1, start, null, true), Actor, ct), "regular shift " + key);
        foreach (var d in days)
        {
            await Template("julie", d, 8, 0, 16, 30, "Management");
            await Template("hana", d, 7, 0, 15, 30, "Pre-admission");
            await Template("mark", d, 7, 15, 15, 45, "Theatre 1");
            await Template("kiri", d, 7, 30, 16, 0, "Reception");
            await Template("lee", d, 7, 30, 16, 0, "CSSD");
            if (d != DayOfWeek.Friday) await Template("claire", d, 7, 30, 16, 0, "Theatre 1");
            if (d != DayOfWeek.Monday) await Template("ana", d, 8, 30, 17, 0, "Recovery");
            if (d is DayOfWeek.Tuesday or DayOfWeek.Wednesday or DayOfWeek.Friday) await Template("tia", d, 7, 30, 13, 0, "Procedure room 1");
            if (d is DayOfWeek.Monday or DayOfWeek.Wednesday or DayOfWeek.Thursday) await Template("sue", d, 8, 0, 14, 30, "Stage 2 recovery");
            if (d is DayOfWeek.Monday or DayOfWeek.Thursday) await Template("megan", d, 12, 0, 17, 0, "Reception");
        }
        var created = 0;
        for (var from = start; from <= _today.AddDays(42); from = from.AddDays(56))
        {
            var to = from.AddDays(55) > _today.AddDays(42) ? _today.AddDays(42) : from.AddDays(55);
            var (c, skipped) = await svc.GenerateAsync(from, to, Actor, ct);
            if (c == 0 && skipped.Count > 0 && skipped[0].StartsWith("Choose")) throw new InvalidOperationException("Demo data – roster: " + skipped[0]);
            created += c;
        }
        if (created == 0) throw new InvalidOperationException("Demo data – roster: no shifts were created.");
    }

    private async Task RosterActivityAsync(CancellationToken ct)
    {
        var svc = S<Ros.RosterService>();
        Ros.Leave Leave((Ros.Leave? Leave, IReadOnlyList<string> Errors) r, string what) => Must(r.Leave, r.Errors, what);
        Monday(out var nextMonday);
        using (At(_today.AddDays(-20), 10))
        {
            var annual = Leave(await svc.RequestLeaveAsync(new Ros.LeaveRequest(_staff["claire"], nextMonday, nextMonday.AddDays(4), Ros.LeaveType.Annual, "Family wedding in Auckland"), "nurse2", ct), "annual leave");
            Ok((await svc.DecideLeaveAsync(annual.Id, Ros.LeaveStatus.Approved, "roster", ct)).Error, "approve leave");
            var sick = Leave(await svc.RequestLeaveAsync(new Ros.LeaveRequest(_staff["ana"], _today.AddDays(-12), _today.AddDays(-12), Ros.LeaveType.Sick, null), "roster", ct), "sick leave");
            Ok((await svc.DecideLeaveAsync(sick.Id, Ros.LeaveStatus.Approved, "roster", ct)).Error, "approve sick leave");
        }
        using (At(_today.AddDays(-2), 14))
        {
            Leave(await svc.RequestLeaveAsync(new Ros.LeaveRequest(_staff["hana"], nextMonday.AddDays(21), nextMonday.AddDays(25), Ros.LeaveType.Training, "Perioperative nursing conference"), "nurse", ct), "training leave");
            Leave(await svc.RequestLeaveAsync(new Ros.LeaveRequest(_staff["kiri"], nextMonday.AddDays(14), nextMonday.AddDays(14), Ros.LeaveType.Annual, "Appointment"), "reception", ct), "annual leave request");
            var mine = (await svc.ShiftsAsync(_today.AddDays(3), _today.AddDays(12), _staff["ana"], ct)).FirstOrDefault(x => x.Date.DayOfWeek is DayOfWeek.Tuesday or DayOfWeek.Friday);
            if (mine is not null)
                Ok((await svc.RequestSwapAsync(new Ros.SwapCreate(mine.Id, _staff["sue"], null, "Could you take this one? School event."), _staff["ana"], "nurse3", ct)).Errors, "shift swap");
        }
    }

    private void Monday(out DateOnly nextMonday)
    {
        nextMonday = _today.AddDays(1);
        while (nextMonday.DayOfWeek != DayOfWeek.Monday) nextMonday = nextMonday.AddDays(1);
    }

    // ------------------------------------------------------------------ theatre

    private async Task TheatreTemplatesAsync(DateOnly start, CancellationToken ct)
    {
        var svc = S<TheatreService>();
        (string Room, DayOfWeek Day, int Sh, int Sm, int Eh, int Em, SessionType Type, string Surgeon, string Anaes, int Every)[] lists =
        [
            ("TH1", DayOfWeek.Monday, 8, 0, 12, 30, SessionType.Surgical, "reid", "kowalski", 1),
            ("TH1", DayOfWeek.Monday, 13, 0, 17, 0, SessionType.Surgical, "whitaker", "grant", 1),
            ("TH1", DayOfWeek.Tuesday, 8, 0, 13, 0, SessionType.Surgical, "natarajan", "kowalski", 1),
            ("PR1", DayOfWeek.Tuesday, 8, 0, 12, 0, SessionType.Endoscopy, "lindqvist", "mehta", 1),
            ("TH1", DayOfWeek.Wednesday, 8, 30, 12, 30, SessionType.Surgical, "tipene", "grant", 1),
            ("PR1", DayOfWeek.Wednesday, 13, 0, 17, 0, SessionType.Endoscopy, "natarajan", "mehta", 1),
            ("TH1", DayOfWeek.Thursday, 8, 0, 16, 30, SessionType.Surgical, "reid", "kowalski", 1),
            ("PR1", DayOfWeek.Friday, 8, 0, 12, 0, SessionType.Endoscopy, "lindqvist", "mehta", 1),
            ("TH1", DayOfWeek.Friday, 8, 0, 12, 0, SessionType.Surgical, "whitaker", "grant", 2),
            ("TH1", DayOfWeek.Friday, 13, 0, 16, 0, SessionType.Surgical, "oconnor", "grant", 4),
        ];
        var monday = start;
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(-1);
        foreach (var l in lists)
            Ok((await svc.SaveTemplateAsync(new TemplateEdit(null, _loc[l.Room], l.Day, new TimeOnly(l.Sh, l.Sm), new TimeOnly(l.Eh, l.Em), l.Type, _clin[l.Surgeon], _clin[l.Anaes], l.Every, monday, null, true), Actor, ct)).Error, "list template");
        var made = 0;
        for (var from = start; from <= _today.AddDays(42); from = from.AddDays(56))
        {
            var to = from.AddDays(55) > _today.AddDays(42) ? _today.AddDays(42) : from.AddDays(55);
            var result = await svc.GenerateFromTemplatesAsync(from, to, Actor, ct);
            if (result.Created == 0) throw new InvalidOperationException("Demo data – theatre: no lists were created. " + string.Join(" ", result.Skipped.Take(3)));
            made += result.Created;
        }
        // One list cancelled because the surgeon was away.
        var core = S<TheatreDbContext>();
        var away = await core.Sessions.Where(s => s.Date > _today.AddDays(14) && s.SurgeonId == _clin["tipene"]).OrderBy(s => s.Date).FirstOrDefaultAsync(ct);
        if (away is not null) Ok(await svc.CancelSessionAsync(away.Id, "reception", ct), "cancel list");
    }

    private static readonly Dictionary<string, (string Name, int Min, AnaestheticType An, bool Side, string? Fee)[]> Repertoire = new()
    {
        ["reid"] = [("Laparoscopic cholecystectomy", 75, AnaestheticType.General, false, "LAPCHOLE"), ("Inguinal hernia repair", 60, AnaestheticType.General, true, "HERNIA"),
                    ("Umbilical hernia repair", 45, AnaestheticType.General, false, "UMBHERNIA"), ("Excision of skin lesion", 20, AnaestheticType.Local, false, "SKINLES"),
                    ("Pilonidal sinus excision", 45, AnaestheticType.General, false, null)],
        ["natarajan"] = [("Haemorrhoidectomy", 30, AnaestheticType.General, false, null), ("Inguinal hernia repair", 60, AnaestheticType.General, true, "HERNIA"),
                         ("Laparoscopic cholecystectomy", 75, AnaestheticType.General, false, "LAPCHOLE"), ("Colonoscopy", 30, AnaestheticType.Sedation, false, "COLON"),
                         ("Colonoscopy with polypectomy", 45, AnaestheticType.Sedation, false, "COLON"), ("Flexible sigmoidoscopy", 20, AnaestheticType.None, false, "FLEXSIG")],
        ["whitaker"] = [("Carpal tunnel release", 20, AnaestheticType.Local, true, "CTR"), ("Trigger finger release", 20, AnaestheticType.Local, true, "TRIGGER"),
                        ("Dupuytren's fasciectomy", 60, AnaestheticType.Regional, true, null), ("Excision of ganglion", 30, AnaestheticType.Regional, true, null)],
        ["tipene"] = [("Excision of basal cell carcinoma with flap repair", 45, AnaestheticType.Local, false, "BCC"), ("Upper eyelid blepharoplasty", 60, AnaestheticType.Local, false, null),
                      ("Scar revision", 45, AnaestheticType.General, false, null), ("Excision of skin lesion", 20, AnaestheticType.Local, false, "SKINLES")],
        ["lindqvist"] = [("Colonoscopy", 30, AnaestheticType.Sedation, false, "COLON"), ("Gastroscopy", 15, AnaestheticType.Sedation, false, "GASTRO"),
                         ("Gastroscopy and colonoscopy", 45, AnaestheticType.Sedation, false, "GASTROCOL"), ("Colonoscopy with polypectomy", 45, AnaestheticType.Sedation, false, "COLON")],
        ["oconnor"] = [("Flexible cystoscopy", 20, AnaestheticType.Local, false, null), ("Vasectomy", 30, AnaestheticType.Local, false, null)],
    };

    private string KeyOf(Guid clinicianId) => _clin.First(c => c.Value == clinicianId).Key;

    private readonly List<(TheatreBooking Booking, TheatreSession Session, string Fee)> _done = [];
    private readonly List<(TheatreBooking Booking, TheatreSession Session, string Fee)> _upcoming = [];
    private readonly Dictionary<Guid, Guid> _referralOfPatient = [];

    private async Task ReferralsAsync(CancellationToken ct)
    {
        var svc = S<ReferralService>();
        var theatre = S<TheatreDbContext>();
        var sessions = await theatre.Sessions.AsNoTracking().Where(s => !s.IsCancelled && s.Date > _today.AddDays(2)).OrderBy(s => s.Date).ToListAsync(ct);
        (int Patient, string Specialist, ServiceType Service, Funder Funder, string Reason, string Proc, Laterality Side, AnaestheticType An, int Min, Priority Pri, string Outcome)[] items =
        [
            (60, "reid", ServiceType.Surgical, Funder.Insurer, "Symptomatic gallstones, two episodes of biliary colic", "Laparoscopic cholecystectomy", Laterality.NotApplicable, AnaestheticType.General, 75, Priority.SemiUrgent, "book"),
            (61, "reid", ServiceType.Surgical, Funder.SelfPay, "Painful right inguinal hernia, reducible", "Inguinal hernia repair", Laterality.Right, AnaestheticType.General, 60, Priority.Routine, "book"),
            (62, "whitaker", ServiceType.Surgical, Funder.Acc, "Left carpal tunnel syndrome, nerve conduction confirmed", "Carpal tunnel release", Laterality.Left, AnaestheticType.Local, 20, Priority.Routine, "book"),
            (63, "lindqvist", ServiceType.Endoscopy, Funder.HealthNz, "Positive bowel screening FIT test", "Colonoscopy", Laterality.NotApplicable, AnaestheticType.Sedation, 30, Priority.Urgent, "book"),
            (64, "lindqvist", ServiceType.Endoscopy, Funder.Insurer, "Dyspepsia not responding to PPI, age over 55", "Gastroscopy", Laterality.NotApplicable, AnaestheticType.Sedation, 15, Priority.SemiUrgent, "book"),
            (65, "tipene", ServiceType.Surgical, Funder.SelfPay, "Biopsy-proven BCC left cheek", "Excision of basal cell carcinoma with flap repair", Laterality.NotApplicable, AnaestheticType.Local, 45, Priority.SemiUrgent, "book"),
            (66, "natarajan", ServiceType.Surgical, Funder.Insurer, "Grade 3 haemorrhoids, bleeding", "Haemorrhoidectomy", Laterality.NotApplicable, AnaestheticType.General, 30, Priority.Routine, "wait"),
            (67, "whitaker", ServiceType.Surgical, Funder.SelfPay, "Dupuytren's contracture right ring finger, 40° MCPJ", "Dupuytren's fasciectomy", Laterality.Right, AnaestheticType.Regional, 60, Priority.Routine, "wait"),
            (68, "reid", ServiceType.Surgical, Funder.Insurer, "Umbilical hernia, enlarging", "Umbilical hernia repair", Laterality.NotApplicable, AnaestheticType.General, 45, Priority.Routine, "wait"),
            (69, "lindqvist", ServiceType.Endoscopy, Funder.HealthNz, "Iron deficiency anaemia", "Gastroscopy and colonoscopy", Laterality.NotApplicable, AnaestheticType.Sedation, 45, Priority.Urgent, "wait"),
            (70, "tipene", ServiceType.Surgical, Funder.SelfPay, "Dermatochalasis affecting vision", "Upper eyelid blepharoplasty", Laterality.NotApplicable, AnaestheticType.Local, 60, Priority.Routine, "hold"),
            (71, "natarajan", ServiceType.Endoscopy, Funder.Insurer, "Change in bowel habit, family history of bowel cancer", "Colonoscopy", Laterality.NotApplicable, AnaestheticType.Sedation, 30, Priority.SemiUrgent, "wait"),
            (72, "whitaker", ServiceType.Surgical, Funder.Acc, "Ganglion right wrist, painful", "Excision of ganglion", Laterality.Right, AnaestheticType.Regional, 30, Priority.Routine, "wait"),
            (73, "reid", ServiceType.Surgical, Funder.SelfPay, "Sebaceous cyst back, recurrent infection", "Excision of skin lesion", Laterality.NotApplicable, AnaestheticType.Local, 20, Priority.Routine, "decline"),
            (74, "lindqvist", ServiceType.Endoscopy, Funder.HealthNz, "Abdominal pain – already under Nelson Hospital gastroenterology", "Colonoscopy", Laterality.NotApplicable, AnaestheticType.Sedation, 30, Priority.Routine, "decline"),
            (75, "tipene", ServiceType.Surgical, Funder.SelfPay, "Scar revision abdomen", "Scar revision", Laterality.NotApplicable, AnaestheticType.General, 45, Priority.Routine, "withdraw"),
            (76, "reid", ServiceType.Surgical, Funder.Insurer, "Left inguinal hernia, intermittent pain", "", Laterality.NotApplicable, AnaestheticType.General, 0, Priority.Routine, "triage"),
            (77, "whitaker", ServiceType.Surgical, Funder.Acc, "Trigger finger right middle finger after injury", "", Laterality.NotApplicable, AnaestheticType.General, 0, Priority.Routine, "triage"),
            (78, "lindqvist", ServiceType.Endoscopy, Funder.HealthNz, "Positive FIT – bowel screening programme", "", Laterality.NotApplicable, AnaestheticType.General, 0, Priority.Routine, "triage"),
            (79, "natarajan", ServiceType.Surgical, Funder.SelfPay, "Perianal abscess history, ?fistula", "", Laterality.NotApplicable, AnaestheticType.General, 0, Priority.Routine, "triage"),
            (80, "oconnor", ServiceType.Surgical, Funder.SelfPay, "Requests vasectomy", "", Laterality.NotApplicable, AnaestheticType.General, 0, Priority.Routine, "triage"),
            (81, "tipene", ServiceType.Surgical, Funder.SelfPay, "Changing pigmented lesion left shoulder", "", Laterality.NotApplicable, AnaestheticType.General, 0, Priority.Routine, "triage"),
        ];
        var gps = new[] { ("Dr Anna Whyte", "Richmond Family Health (demo)", "31ABCD"), ("Dr Paul Ngata", "Stoke Medical Centre (demo)", "32ABCE"), ("Dr Lisa Moana", "Nelson City Doctors (demo)", "33ABCF"),
                          ("Dr Gareth Lloyd", "Motueka Health (demo)", "34ABCG"), ("Dr Kate Oliver", "Brightwater Medical (demo)", "35ABCH") };
        var n = 0;
        foreach (var it in items)
        {
            n++;
            var patient = P(it.Patient);
            var received = _today.AddDays(-(items.Length - n) * 2 - 3);
            var gp = gps[n % gps.Length];
            Referral referral;
            using (At(received, 10, 15))
            {
                var source = it.Funder switch { Funder.Acc => ReferralSource.Acc, Funder.HealthNz => ReferralSource.HealthNz, _ => ReferralSource.GeneralPractice };
                var (r, errors) = await svc.CreateReferralAsync(new NewReferral(patient.Id, received, source, gp.Item1, gp.Item2, gp.Item3, _clin[it.Specialist], it.Service, it.Funder,
                    it.Funder switch { Funder.Acc => $"ACC{4000000 + n * 731}", Funder.Insurer => $"SX-{88000 + n}", _ => null }, it.Reason), "reception", ct);
                referral = Must(r, errors, "referral");
            }
            _referralOfPatient[patient.Id] = referral.Id;
            if (it.Outcome == "triage") continue;
            using (At(received.AddDays(3), 16, 30))
            {
                var user = DemoAccounts.All.First(a => a.DisplayName == (_clinName(it.Specialist))).UserName;
                if (it.Outcome == "decline")
                {
                    Ok((await svc.TriageAsync(referral.Id, new TriageDecision(false, null, it.Patient == 74 ? "Already under the care of Health NZ gastroenterology; please refer back there." : "Can be managed in general practice (minor surgery).",
                        null, Laterality.NotApplicable, AnaestheticType.General, 0, Priority.Routine, null), user, ct)).Errors, "decline referral");
                    continue;
                }
                if (it.Outcome == "withdraw") { Ok(await svc.WithdrawReferralAsync(referral.Id, "Patient decided not to proceed", "reception", ct), "withdraw referral"); continue; }
                var (entry, errs) = await svc.TriageAsync(referral.Id, new TriageDecision(true, "Seen in clinic; consented.", null, it.Proc, it.Side, it.An, it.Min, it.Pri, received.AddDays(3)), user, ct);
                var e = Must(entry, errs, "accept referral");
                if (it.Outcome == "hold") Ok(await svc.HoldAsync(e.Id, true, "Patient travelling overseas", _today.AddDays(45), "reception", ct), "hold");
                if (it.Outcome == "book")
                {
                    var session = sessions.Where(s => s.SurgeonId == _clin[it.Specialist] && s.Date >= _today.AddDays(5)).Skip(n % 2).FirstOrDefault()
                        ?? throw new InvalidOperationException("Demo data – no list for waitlist booking.");
                    var res = await svc.BookAsync(e.Id, session.Id, acceptWarnings: true, "reception", ct);
                    Ok(res.Errors, "book from waitlist");
                }
            }
        }
        // Patients added straight to the waitlist (seen in clinic, no referral letter).
        using (At(_today.AddDays(-9), 11))
            foreach (var (pi, key, proc, min, an, svcType) in new[] { (82, "reid", "Inguinal hernia repair", 60, AnaestheticType.General, ServiceType.Surgical), (83, "lindqvist", "Colonoscopy", 30, AnaestheticType.Sedation, ServiceType.Endoscopy),
                         (84, "whitaker", "Carpal tunnel release", 20, AnaestheticType.Local, ServiceType.Surgical) })
                Ok((await svc.AddToWaitlistAsync(new NewWaitlistEntry(P(pi).Id, proc, proc.Contains("hernia") || proc.Contains("Carpal") ? Laterality.Left : Laterality.NotApplicable, an, min, svcType, _clin[key],
                    Funder.SelfPay, Priority.Routine, _today.AddDays(-9), "Added at clinic"), "reception", null, ct)).Errors, "waitlist entry");
    }

    private string _clinName(string key) => key switch
    {
        "reid" => "Dr Hamish Reid", "natarajan" => "Dr Priya Natarajan", "whitaker" => "Dr Tom Whitaker", "tipene" => "Dr Mereana Tipene",
        "lindqvist" => "Dr Sarah Lindqvist", "oconnor" => "Dr Liam O'Connor", "kowalski" => "Dr Ben Kowalski", "grant" => "Dr Fiona Grant", _ => "Dr Raj Mehta"
    };

    private async Task BookingsAsync(CancellationToken ct)
    {
        var svc = S<TheatreService>();
        var tdb = S<TheatreDbContext>();
        var sessions = await tdb.Sessions.AsNoTracking().Where(s => !s.IsCancelled).OrderBy(s => s.Date).ThenBy(s => s.StartTime).ToListAsync(ct);
        var busy = new HashSet<(Guid, DateOnly)>((await tdb.Bookings.AsNoTracking().Join(tdb.Sessions, b => b.SessionId, s => s.Id, (b, s) => new { b.PatientId, s.Date }).ToListAsync(ct))
            .Select(x => (x.PatientId, x.Date)));
        var pool = Enumerable.Range(0, 60).ToList(); // referral patients (60+) are booked through the waitlist
        var next = 0;
        var trays = new Dictionary<string, string> { ["reid"] = "General set; Lap chole set", ["natarajan"] = "Anorectal set; General set", ["whitaker"] = "Hand set", ["tipene"] = "Plastics fine set",
            ["oconnor"] = "Minor set", ["lindqvist"] = "" };

        foreach (var s in sessions)
        {
            var key = KeyOf(s.SurgeonId);
            var daysAhead = s.Date.DayNumber - _today.DayNumber;
            var fill = daysAhead <= 7 ? 0.9 : daysAhead <= 21 ? 0.7 : 0.4;
            var capacity = (int)(s.EndTime - s.StartTime).TotalMinutes;
            var existing = await tdb.Bookings.Where(b => b.SessionId == s.Id && b.Status != BookingStatus.Cancelled).SumAsync(b => b.DurationMinutes + TheatreService.TurnoverMinutes, ct);
            var used = existing;
            var order = 0;
            var created = new List<(TheatreBooking, string)>();
            while (true)
            {
                var proc = Pick(Repertoire[key]);
                if (s.Type == SessionType.Endoscopy && proc.An is not (AnaestheticType.Sedation or AnaestheticType.None)) continue;
                if (s.Type == SessionType.Surgical && key == "natarajan" && proc.An == AnaestheticType.Sedation) continue;
                if (used + proc.Min + TheatreService.TurnoverMinutes > capacity * fill) break;
                int pi = -1;
                for (var tries = 0; tries < pool.Count; tries++)
                {
                    var cand = pool[(next + tries) % pool.Count];
                    if (!busy.Contains((P(cand).Id, s.Date)) && !(daysAhead < 0 && _done.Count(d => d.Booking.PatientId == P(cand).Id) >= 2)) { pi = cand; next = (next + tries + 1) % pool.Count; break; }
                }
                if (pi < 0) break;
                var patient = P(pi);
                var payment = proc.Fee is null && key == "whitaker" ? PaymentType.Acc : (PaymentType)new[] { 1, 2, 2, 3, 1, 2, 4 }[_rng.Next(7)];
                var side = proc.Side ? (Chance(0.5) ? Laterality.Left : Laterality.Right) : Laterality.NotApplicable;
                var bookedOn = daysAhead < 0 ? s.Date.AddDays(-14 - _rng.Next(0, 20)) : _today.AddDays(-_rng.Next(1, 25));
                if (bookedOn > _today) bookedOn = _today;
                TheatreBooking booking;
                using (At(bookedOn, 9 + _rng.Next(0, 7), _rng.Next(0, 59)))
                {
                    var r = await svc.BookAsync(new NewBooking(s.Id, patient.Id, proc.Name, side, proc.Min, proc.An, null, AcceptWarnings: true, payment, IsTentative: daysAhead > 21 && Chance(0.2)), "reception", ct);
                    booking = Must(r.Booking, r.Errors, "booking");
                }
                busy.Add((patient.Id, s.Date));
                used += proc.Min + TheatreService.TurnoverMinutes;
                created.Add((booking, proc.Fee ?? "FAC-TH30"));
                order++;
            }
            // Bookings made through the waitlist on this list.
            foreach (var b in await tdb.Bookings.Where(b => b.SessionId == s.Id).ToListAsync(ct))
                if (created.All(c => c.Item1.Id != b.Id)) created.Add((b, FeeFor(b.Procedure)));

            await ProgressAsync(svc, s, created, key, trays.GetValueOrDefault(key, ""), daysAhead, ct);
        }
    }

    private static string FeeFor(string procedure) => procedure switch
    {
        var p when p.StartsWith("Colonoscopy") => "COLON", "Gastroscopy" => "GASTRO", "Gastroscopy and colonoscopy" => "GASTROCOL", "Carpal tunnel release" => "CTR",
        "Inguinal hernia repair" => "HERNIA", "Laparoscopic cholecystectomy" => "LAPCHOLE", "Excision of basal cell carcinoma with flap repair" => "BCC", _ => "FAC-TH30"
    };

    /// <summary>Moves each booking through the day like staff would: arrived, in theatre, recovery, discharged (with theatre times), or cancelled/DNA.</summary>
    private async Task ProgressAsync(TheatreService svc, TheatreSession s, List<(TheatreBooking Booking, string Fee)> list, string key, string trays, int daysAhead, CancellationToken ct)
    {
        var t = s.StartTime;
        var i = 0;
        foreach (var (b, fee) in list.OrderBy(x => x.Booking.Order))
        {
            i++;
            var checks = new List<string> { "consent", "form", "adult" };
            if (b.Payment is PaymentType.Insurer or PaymentType.Acc) checks.Add("approval");
            if (b.Payment == PaymentType.SelfFunded) checks.Add("estimate");
            if (b.Anaesthetic == AnaestheticType.General) checks.Add("anaesthetic");
            var scope = s.Type == SessionType.Endoscopy ? (b.Procedure.StartsWith("Gastro") ? "GIF-H190-0" : "CF-HQ190L-") + (1 + i % 3) : null;

            if (daysAhead > 0)
            {
                if (daysAhead <= 7) Ok(await svc.SetStatusAsync(b.Id, BookingStatus.Confirmed, null, "reception", ct), "confirm");
                var ticked = daysAhead <= 3 ? checks : daysAhead <= 14 ? checks.Take(2).ToList() : [];
                Ok(await svc.UpdateDetailsAsync(b.Id, new BookingDetails(b.IsTentative, b.Payment, b.Payment == PaymentType.SelfFunded && daysAhead <= 14 ? "Deposit $500 paid (EFTPOS)" : null, ticked, false,
                    null, null, trays.Length > 0 ? trays : null, null, null, null, null), "reception", ct), "booking details");
                _upcoming.Add((b, s, fee));
                continue;
            }
            if (daysAhead < 0 && Chance(0.05))
            {
                using (At(s.Date.AddDays(-2), 11)) Ok(await svc.SetStatusAsync(b.Id, BookingStatus.Cancelled, Pick(new[] { "Unwell – COVID positive", "Patient request – work commitments", "Surgeon request – further imaging needed" }), "reception", ct), "cancel");
                continue;
            }
            if (daysAhead < 0 && Chance(0.02))
            {
                using (At(s.Date, 12)) Ok(await svc.SetStatusAsync(b.Id, BookingStatus.DidNotAttend, null, "reception", ct), "DNA");
                continue;
            }
            var tin = t.AddMinutes(5);
            var tout = tin.AddMinutes(Math.Max(10, b.DurationMinutes + _rng.Next(-8, 15)));
            t = tout.AddMinutes(TheatreService.TurnoverMinutes);
            // Today: the list is in progress.
            var now = TimeOnly.FromDateTime(_clock.GetLocalNow().DateTime);
            var stage = daysAhead < 0 ? 4 : tout.AddMinutes(60) <= now ? 4 : tout <= now ? 3 : tin <= now ? 2 : tin.AddMinutes(-60) <= now ? 1 : 0;
            if (stage == 0) { Ok(await svc.SetStatusAsync(b.Id, BookingStatus.Confirmed, null, "reception", ct), "confirm today"); continue; }
            using (At(s.Date, Math.Max(6, tin.Hour - 1), tin.Minute)) Ok(await svc.SetStatusAsync(b.Id, BookingStatus.Arrived, null, "reception", ct), "arrived");
            if (stage >= 2) using (At(s.Date, tin.Hour, tin.Minute)) Ok(await svc.SetStatusAsync(b.Id, BookingStatus.InTheatre, null, "nurse2", ct), "in theatre");
            if (stage >= 3) using (At(s.Date, tout.Hour, tout.Minute)) Ok(await svc.SetStatusAsync(b.Id, BookingStatus.Recovery, null, "nurse2", ct), "recovery");
            var discharge = tout.AddMinutes(60 + _rng.Next(0, 60));
            if (stage >= 4) using (At(s.Date, discharge.Hour, discharge.Minute)) Ok(await svc.SetStatusAsync(b.Id, BookingStatus.Discharged, null, "nurse3", ct), "discharged");
            using (At(s.Date, discharge.Hour, discharge.Minute))
                Ok(await svc.UpdateDetailsAsync(b.Id, new BookingDetails(false, b.Payment, null, checks, false, stage >= 3 ? tin : null, stage >= 3 ? tout : null,
                    trays.Length > 0 ? trays : null, stage >= 3 && trays.Length > 0 ? trays : null, key == "whitaker" && i == 1 ? "Mini C-arm (loan)" : null, scope, null), "nurse2", ct), "theatre record");
            if (stage >= 4) _done.Add((b, s, fee));
        }
    }

    private async Task AppointmentsAsync(CancellationToken ct)
    {
        var svc = S<TheatreService>();
        var slots = new[] { (9, 0), (9, 30), (10, 0), (10, 30), (11, 0), (14, 0), (14, 30), (15, 0) };
        var specialists = new[] { "reid", "natarajan", "whitaker", "tipene", "lindqvist" };
        var n = 0;
        for (var d = _today.AddDays(-28); d <= _today.AddDays(28); d = d.AddDays(1))
        {
            if (!Weekday(d) || d.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Thursday) continue;
            var key = specialists[d.DayNumber % specialists.Length];
            foreach (var (h, m) in slots.Where((_, i) => (i + d.Day) % 3 != 0))
            {
                n++;
                var patient = P(n % 5 == 0 ? 60 + n % 25 : n * 7 % 85);
                var kind = n % 5 == 0 ? AppointmentKind.FollowUp : n % 11 == 0 ? AppointmentKind.PhoneCall : n % 7 == 0 ? AppointmentKind.PreAdmission : AppointmentKind.Consultation;
                (Appointment? Appointment, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings) r;
                using (At(d < _today ? d.AddDays(-10) : _today.AddDays(-3), 10))
                    r = await svc.BookAppointmentAsync(new NewAppointment(patient.Id, d, new TimeOnly(h, m), kind == AppointmentKind.PhoneCall ? 15 : 30, kind,
                        kind == AppointmentKind.PreAdmission ? _clin["kowalski"] : _clin[key], kind == AppointmentKind.PhoneCall ? null : _loc[n % 2 == 0 ? "CR1" : "CR2"],
                        kind == AppointmentKind.FollowUp, n % 13 == 0 && d > _today, null, AcceptWarnings: true), "reception", ct);
                if (r.Appointment is null) continue; // clash with another appointment – skip
                var status = d < _today ? (n % 17 == 0 ? AppointmentStatus.DidNotAttend : n % 19 == 0 ? AppointmentStatus.Cancelled : AppointmentStatus.Completed)
                    : d == _today && h < _clock.GetLocalNow().Hour ? AppointmentStatus.Arrived : d <= _today.AddDays(2) ? AppointmentStatus.Confirmed : AppointmentStatus.Booked;
                if (status != AppointmentStatus.Booked)
                    using (At(d, h, m))
                        Ok(await svc.UpdateAppointmentAsync(r.Appointment.Id, status, status == AppointmentStatus.Cancelled ? "Patient rescheduled" : null, false, null, "reception", ct), "appointment status");
            }
        }
    }

    // ------------------------------------------------------------------ forms, billing, labs, letters

    private static Dictionary<string, string> Answers(int i)
    {
        var a = new Dictionary<string, string>
        {
            ["height_cm"] = (155 + i % 30).ToString(), ["weight_kg"] = (58 + i * 3 % 40).ToString(), ["support_person"] = "Partner – 021 555 0199", ["lives_alone"] = i % 6 == 0 ? "Yes" : "No",
            ["heart"] = "No", ["pacemaker"] = "No", ["high_bp"] = i % 3 == 0 ? "Yes" : "No", ["lung"] = "No", ["sleep_apnoea"] = "No", ["diabetes"] = i % 7 == 0 ? "Yes – tablets" : "No",
            ["kidney_liver"] = "No", ["stroke"] = "No", ["bleeding"] = "No", ["infection"] = "No", ["pregnant"] = "Not applicable", ["blood_thinners"] = i % 5 == 0 ? "Yes" : "No",
            ["herbal"] = "No", ["allergies"] = i % 4 == 0 ? "Yes" : "No", ["latex"] = "No", ["anaesthetic_problems"] = "No", ["family_mh"] = "No", ["teeth"] = "No", ["neck"] = "No",
            ["smoking"] = i % 8 == 0 ? "Smoke" : "Never", ["alcohol"] = "1–10", ["mobility"] = "Independently", ["interpreter"] = "No", ["confirm"] = "Yes",
            ["medications"] = i % 3 == 0 ? "Cilazapril 2.5 mg daily" : "None",
        };
        if (a["blood_thinners"] == "Yes") a["blood_thinners_details"] = "Aspirin 100 mg daily";
        if (a["allergies"] == "Yes") a["allergies_details"] = "Penicillin – rash";
        if (i == 9) { a["pacemaker"] = "Yes"; a["heart"] = "Yes"; a["heart_details"] = "Pacemaker 2023 for heart block"; }
        return a;
    }

    private async Task FormsAsync(CancellationToken ct)
    {
        var svc = S<FormService>();
        var i = 0;
        foreach (var (b, s, _) in _upcoming.Where(u => u.Session.Date <= _today.AddDays(16)).OrderBy(u => u.Session.Date))
        {
            i++;
            var patient = _patients.FirstOrDefault(p => p.Id == b.PatientId) ?? await S<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(p => p.Id == b.PatientId, ct);
            FormRequest req; string token;
            using (At(s.Date.AddDays(-12) < _today ? s.Date.AddDays(-12) : _today, 10))
            {
                var (r, tk, err) = await svc.CreateAsync(patient.Id, b.Id, "nurse", ct);
                Ok(err, "questionnaire link");
                req = r!; token = tk!;
            }
            if (i % 5 == 4) continue; // not opened yet
            using (At(s.Date.AddDays(-10) < _today ? s.Date.AddDays(-10) : _today, 19))
            {
                Ok((await svc.VerifyAsync(token, patient.DateOfBirth, ct)).Error, "verify link");
                var (ok, errors) = await svc.SubmitAsync(token, patient.DateOfBirth, Answers(i), true, ct);
                if (!ok) Ok(errors, "submit questionnaire");
            }
            if (s.Date > _today.AddDays(6) || i == 9) continue; // still to triage
            var row = (await svc.ListAsync("to-triage", patient.Id, ct)).FirstOrDefault();
            if (row is null) continue;
            using (At(s.Date.AddDays(-6) < _today ? s.Date.AddDays(-6) : _today, 11))
                Ok(await svc.TriageAsync(row.Id, Enum.Parse<TriageCategory>(row.Suggested ?? "Green"), row.Suggested == "Green" ? "Fit for day surgery" : "Discussed with anaesthetist – proceed",
                    row.Suggested == "Green" ? null : "Phoned patient to confirm details", addAllergyAlert: Answers(i)["allergies"] == "Yes", "nurse", ct), "triage questionnaire");
        }
    }

    private async Task BillingAsync(CancellationToken ct)
    {
        var svc = S<BillingService>();
        var i = 0;
        foreach (var (b, s, fee) in _done.OrderBy(d => d.Session.Date))
        {
            i++;
            var payer = b.Payment switch { PaymentType.Insurer => Payer.Insurer, PaymentType.Acc => Payer.Acc, PaymentType.HealthNz => Payer.HealthNz, _ => Payer.SelfFunded };
            var lines = new List<LineInput> { new("FAC-DAY", null, 1, null), new(fee, null, fee == "FAC-TH30" ? Math.Max(1, b.DurationMinutes / 30) : 1, null), new("CONS", null, 1, null) };
            if (b.Procedure.Contains("polypectomy") || b.Procedure.Contains("BCC") || b.Procedure.Contains("lesion")) lines.Add(new("HISTO", null, 1, null));
            var surgeon = _clinName(KeyOf(s.SurgeonId));
            var anaes = s.AnaesthetistId is { } a ? _clinName(KeyOf(a)) : null;
            var insurer = payer == Payer.Insurer ? Pick(new[] { "Southern Cross", "nib", "AIA", "Partners Life" }) : payer == Payer.Acc ? "ACC" : payer == Payer.HealthNz ? "Health NZ Nelson Marlborough" : null;
            var ref_ = payer switch { Payer.Insurer => $"PA{700000 + i * 17}", Payer.Acc => $"ACC{4100000 + i * 29}", Payer.HealthNz => $"HNZ-{9000 + i}", _ => null };
            BillingDocument inv;
            if (payer == Payer.SelfFunded && i % 2 == 0)
            {
                BillingDocument est;
                using (At(s.Date.AddDays(-20), 11))
                {
                    var (e, errors) = await svc.CreateAsync(DocumentKind.Estimate, new NewDocument(b.PatientId, payer, insurer, ref_, surgeon, anaes, "Estimate valid 30 days", lines, b.Id), "finance", ct);
                    est = Must(e, errors, "estimate");
                    Ok(await svc.SetStatusAsync(est.Id, DocumentStatus.Issued, null, "finance", ct), "issue estimate");
                }
                using (At(s.Date.AddDays(-16), 15)) Ok(await svc.SetStatusAsync(est.Id, DocumentStatus.Accepted, null, "reception", ct), "accept estimate");
                using (At(s.Date.AddDays(1), 10)) { var (x, err) = await svc.InvoiceFromEstimateAsync(est.Id, "finance", ct); Ok(err, "invoice from estimate"); inv = x!; }
            }
            else
                using (At(s.Date.AddDays(1), 10))
                {
                    var (x, errors) = await svc.CreateAsync(DocumentKind.Invoice, new NewDocument(b.PatientId, payer, insurer, ref_, surgeon, anaes, null, lines, b.Id), "finance", ct);
                    inv = Must(x, errors, "invoice");
                }
            using (At(s.Date.AddDays(1), 10, 30)) Ok(await svc.SetStatusAsync(inv.Id, DocumentStatus.Issued, null, "finance", ct), "issue invoice");
            var total = (await svc.GetAsync(inv.Id, "finance", null, ct)) is { } d ? BillingService.Calculate(d).Total : 0m;
            var age = _today.DayNumber - s.Date.DayNumber;
            var method = payer switch { Payer.Insurer => PaymentMethod.Insurer, Payer.Acc => PaymentMethod.Acc, Payer.HealthNz => PaymentMethod.BankTransfer, _ => i % 3 == 0 ? PaymentMethod.CreditCard : PaymentMethod.Eftpos };
            if (age > 25 || (age > 6 && i % 3 != 0))
            {
                var paidOn = s.Date.AddDays(Math.Min(age, 5 + i % 20));
                if (i % 9 == 0)
                {
                    using (At(paidOn, 14)) Ok(await svc.RecordPaymentAsync(inv.Id, new NewPayment(paidOn, Math.Round(total * 0.6m, 2), method, ref_ ?? "EFTPOS"), "finance", ct), "part payment");
                }
                else if (!(age > 40 && i % 11 == 0)) // a couple left overdue
                    using (At(paidOn, 14)) Ok(await svc.RecordPaymentAsync(inv.Id, new NewPayment(paidOn, total, method, ref_ ?? (method == PaymentMethod.Eftpos ? "EFTPOS" : "Visa")), "finance", ct), "payment");
            }
        }
        // Estimates for upcoming self-funded surgery: issued, accepted and one declined.
        var j = 0;
        foreach (var (b, s, fee) in _upcoming.Where(u => u.Booking.Payment is PaymentType.SelfFunded or PaymentType.Insurer).Take(18))
        {
            j++;
            using (At(_today.AddDays(-10 + Math.Min(9, j / 2)), 11))
            {
                var payer = b.Payment == PaymentType.Insurer ? Payer.Insurer : Payer.SelfFunded;
                var (e, errors) = await svc.CreateAsync(DocumentKind.Estimate, new NewDocument(b.PatientId, payer, payer == Payer.Insurer ? "Southern Cross" : null, payer == Payer.Insurer ? $"PA{810000 + j}" : null,
                    _clinName(KeyOf(s.SurgeonId)), s.AnaesthetistId is { } a ? _clinName(KeyOf(a)) : null, null,
                    [new("FAC-DAY", null, 1, null), new(fee, null, fee == "FAC-TH30" ? Math.Max(1, b.DurationMinutes / 30) : 1, null), new("CONS", null, 1, null)], b.Id), "finance", ct);
                var est = Must(e, errors, "upcoming estimate");
                if (j % 6 == 5) continue; // draft
                Ok(await svc.SetStatusAsync(est.Id, DocumentStatus.Issued, null, "finance", ct), "issue upcoming estimate");
                if (j % 3 == 0) Ok(await svc.SetStatusAsync(est.Id, DocumentStatus.Accepted, null, "reception", ct), "accept upcoming estimate");
                if (j == 7) Ok(await svc.SetStatusAsync(est.Id, DocumentStatus.Declined, null, "reception", ct), "decline estimate");
            }
        }
        // Consultation invoices.
        using (At(_today.AddDays(-5), 16))
            for (var k = 0; k < 6; k++)
            {
                var (x, errors) = await svc.CreateAsync(DocumentKind.Invoice, new NewDocument(P(60 + k).Id, Payer.SelfFunded, null, null, _clinName(Pick(new[] { "reid", "whitaker", "tipene" })), null, null,
                    [new("CONSULT", null, 1, null)]), "finance", ct);
                var inv = Must(x, errors, "consult invoice");
                Ok(await svc.SetStatusAsync(inv.Id, DocumentStatus.Issued, null, "finance", ct), "issue consult invoice");
                if (k % 2 == 0) Ok(await svc.RecordPaymentAsync(inv.Id, new NewPayment(_today.AddDays(-5), 299m, PaymentMethod.Eftpos, "EFTPOS"), "reception", ct), "consult payment");
            }
    }

    private static string Oru(string control, string nhi, string family, string given, DateOnly dob, char sex, DateTimeOffset at, string test, string code, IEnumerable<(string Code, string Name, string Value, string Units, string Range, string Flag)> obs, string? note = null, string status = "F") =>
        $"MSH|^~\\&|DELTA|AWANUI-NELSON|BrightPAS|TDS|{at:yyyyMMddHHmm}||ORU^R01|{control}|P|2.4\r" +
        $"PID|1||{nhi}^^^NHI^NHI||{family.ToUpperInvariant()}^{given}||{dob:yyyyMMdd}|{sex}\r" +
        $"OBR|1|TDS{control}|{control}A|{code}^{test}|||{at.AddHours(-20):yyyyMMddHHmm}|||||||||11DEMA^REID^HAMISH^^^DR||||||{at:yyyyMMddHHmm}|||{status}\r" +
        string.Concat(obs.Select((o, i) => $"OBX|{i + 1}|{(o.Units.Length == 0 && !decimal.TryParse(o.Value, out _) ? "FT" : "NM")}|{o.Code}^{o.Name}||{o.Value}|{o.Units}|{o.Range}|{o.Flag}|||F\r")) +
        (note is null ? "" : $"NTE|1||{note}\r");

    private async Task LabsAsync(CancellationToken ct)
    {
        var svc = S<LabService>();
        var n = 0;
        var subjects = _upcoming.Where(u => u.Session.Date <= _today.AddDays(10)).Select(u => u.Booking.PatientId).Distinct().Take(10)
            .Concat(_done.Where(d => d.Booking.Procedure.Contains("polypectomy") || d.Booking.Procedure.Contains("lesion") || d.Booking.Procedure.Contains("carcinoma")).Select(d => d.Booking.PatientId).Distinct().Take(5)).ToList();
        foreach (var pid in subjects)
        {
            n++;
            var p = await S<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == pid, ct);
            if (p.Nhi is null) continue;
            var at = new DateTimeOffset(_today.AddDays(-(n % 9) - 1).ToDateTime(new TimeOnly(10 + n % 6, 15)), TimeSpan.FromHours(13));
            var sex = p.Sex == BirthSex.Female ? 'F' : 'M';
            string raw = n switch
            {
                <= 10 when n % 3 == 1 => Oru($"DL{26000 + n}", p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth, sex, at, "Full blood count", "CBC",
                    [("718-7", "Haemoglobin", n == 4 ? "98" : "138", "g/L", "115-155", n == 4 ? "L" : "N"), ("6690-2", "White cells", "6.8", "x10*9/L", "4.0-11.0", "N"), ("777-3", "Platelets", "254", "x10*9/L", "150-400", "N")],
                    n == 4 ? "Mild anaemia – suggest iron studies" : null),
                <= 10 when n % 3 == 2 => Oru($"DL{26000 + n}", p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth, sex, at, "Renal function and electrolytes", "U&E",
                    [("2951-2", "Sodium", "139", "mmol/L", "135-145", "N"), ("2823-3", "Potassium", n == 5 ? "5.9" : "4.2", "mmol/L", "3.5-5.2", n == 5 ? "H" : "N"), ("2160-0", "Creatinine", "84", "umol/L", "45-90", "N"),
                     ("33914-3", "eGFR", ">90", "mL/min/1.73m2", ">60", "N")]),
                <= 10 => Oru($"DL{26000 + n}", p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth, sex, at, "Coagulation screen", "COAG",
                    [("34714-6", "INR", n == 6 ? "2.6" : "1.0", "", "0.8-1.2", n == 6 ? "H" : "N"), ("14979-9", "APTT", "29", "s", "25-37", "N")], n == 6 ? "Patient on warfarin?" : null),
                _ => Oru($"DL{26000 + n}", p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth, sex, at, "Histology", "HISTO",
                    [("22637-3", "Pathology report", n % 2 == 0 ? "Tubular adenoma with low-grade dysplasia. Completely excised." : "Basal cell carcinoma, nodular type. Margins clear (closest 2 mm).", "", "", "N")]),
            };
            var r = await svc.IngestAsync(raw, "Delta lab (demo)", "lab-interface", ct);
            if (r.Outcome != LabMessageOutcome.Accepted) throw new InvalidOperationException("Demo data – lab result: " + r.Error);
            if (n % 9 > 3)
                foreach (var id in r.ReportIds)
                    using (At(DateOnly.FromDateTime(at.Date).AddDays(1) > _today ? _today : DateOnly.FromDateTime(at.Date).AddDays(1), 9)) Ok(await svc.AcknowledgeAsync(id, n > 10 ? "hreid" : "nurse", n == 4 ? "Iron studies requested" : "Reviewed", ct), "acknowledge result");
        }
        // One result the lab sent for someone who is not a TDS patient (needs matching by hand).
        var unknown = Oru("DL26999", "ZZZ0024", "UNKNOWN", "Pat", new DateOnly(1971, 3, 3), 'F', _clock.GetUtcNow().AddHours(-5), "Full blood count", "CBC",
            [("718-7", "Haemoglobin", "131", "g/L", "115-155", "N")]);
        await svc.IngestAsync(unknown, "Delta lab (demo)", "lab-interface", ct);
    }

    private async Task LettersAsync(CancellationToken ct)
    {
        var letters = S<Let.LetterService>();
        var hl = S<Let.HealthLinkService>();
        var refs = await S<ReferralsDbContext>().Referrals.AsNoTracking().ToListAsync(ct);
        foreach (var r in refs.Where(r => r.Status is ReferralStatus.Accepted or ReferralStatus.Declined))
            using (At(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.TriagedAt ?? _clock.GetUtcNow(), _clock.LocalTimeZone).DateTime), 17))
            {
                var (l, errors) = await letters.CreateDraftAsync(new Let.NewLetter(r.PatientId, r.Status == ReferralStatus.Accepted ? "REFACK" : "REFDEC", null, r.Id), "reception", null, ct);
                var letter = Must(l, errors, "referral letter");
                Ok(await letters.FinaliseAsync(letter.Id, "reception", ct), "finalise letter");
                Ok(await letters.RecordPrintAsync(letter.Id, "reception", ct), "print letter");
            }
        var i = 0;
        foreach (var (b, s, _) in _upcoming.OrderBy(u => u.Session.Date).Take(16))
        {
            i++;
            using (At(_today.AddDays(-Math.Min(8, i)), 15))
            {
                var (l, errors) = await letters.CreateDraftAsync(new Let.NewLetter(b.PatientId, i % 2 == 0 ? "APPT" : "ADMIT", b.Id, null), "reception", null, ct);
                var letter = Must(l, errors, "booking letter");
                if (i % 5 == 0) continue; // draft waiting to be checked
                Ok(await letters.FinaliseAsync(letter.Id, "reception", ct), "finalise booking letter");
                Ok(await letters.RecordPrintAsync(letter.Id, "reception", ct), "print booking letter");
                if (i % 3 != 0) continue;
                var (gl, gerrors) = await letters.CreateDraftAsync(new Let.NewLetter(b.PatientId, "GPBOOK", b.Id, null), "reception", null, ct);
                var gp = Must(gl, gerrors, "GP letter");
                Ok(await letters.FinaliseAsync(gp.Id, "reception", ct), "finalise GP letter");
                var (ok, msg) = await hl.SendAsync(gp.Id, "reception", ct);
                if (!ok) log.LogInformation("Demo GP letter not sent: {Message}", msg);
            }
        }
    }

    // ------------------------------------------------------------------ patient records, portal

    private async Task PatientRecordsAsync(CancellationToken ct)
    {
        var docs = S<DocumentService>();
        var org = (await S<OrganisationService>().MainSiteAsync(ct))!.Value;
        var lh = new Pdf.LetterheadData("Richmond Family Health (demo)", "20 Queen Street, Richmond 7020", "03 555 0211", "richfam@demo.brightpas.nz");
        var refs = await S<ReferralsDbContext>().Referrals.AsNoTracking().OrderBy(r => r.ReceivedOn).ToListAsync(ct);
        var k = 0;
        foreach (var r in refs.Take(12))
        {
            k++;
            var p = await S<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == r.PatientId, ct);
            var body = $"Dear {_clinName(KeyOf(r.SpecialistId))}\n\nRe: {p.GivenNames} {p.FamilyName}, DOB {p.DateOfBirth:d MMM yyyy}, NHI {p.Nhi ?? "not known"}\n\n" +
                       $"Thank you for seeing this patient. {r.Reason}.\n\nPast history: hypertension, well controlled. Medicines: cilazapril 2.5 mg daily. No known drug allergies.\n\n" +
                       "I would be grateful for your assessment and management.\n\nKind regards\n\n" + r.ReferrerName;
            var pdf = Pdf.PdfReports.Letter(lh, new Pdf.LetterData($"GP-{r.Number}", _clinName(KeyOf(r.SpecialistId)), org.Site.Name, "Referral", body, new DateTimeOffset(r.ReceivedOn.ToDateTime(new TimeOnly(9, 0))), false, r.ReferrerName), null);
            using (At(r.ReceivedOn, 10, 20))
                Ok((await docs.AddAsync(new NewPatientDocument(r.PatientId, null, DocumentCategory.Referral, $"GP referral {r.Number}", $"referral-{r.Number}.pdf", Convert.ToBase64String(pdf), DocumentSource.Scan), "reception", ct: ct)).Errors, "referral document");
        }
        foreach (var (b, s, _) in _done.Take(6))
        {
            var pdf = Pdf.PdfReports.Letter(new Pdf.LetterheadData("Tasman Day Surgery (DEMO)", "1 Demonstration Way, Richmond", "03 555 0100", null),
                new Pdf.LetterData($"CONSENT-{b.Id.ToString()[..6]}", "Patient", null, "Consent to treatment", $"I consent to: {b.Procedure}.\n\nRisks discussed: bleeding, infection, pain, need for further surgery.\n\nSigned by the patient and {_clinName(KeyOf(s.SurgeonId))}.",
                new DateTimeOffset(s.Date.ToDateTime(new TimeOnly(7, 30))), false, _clinName(KeyOf(s.SurgeonId))), null);
            using (At(s.Date, 7, 45))
                Ok((await docs.AddAsync(new NewPatientDocument(b.PatientId, b.Id, DocumentCategory.Consent, "Signed consent", "consent.pdf", Convert.ToBase64String(pdf), DocumentSource.Scan), "nurse", ct: ct)).Errors, "consent document");
        }

        // Identity work for the Privacy Officer: a spelling correction and a likely duplicate registration.
        var ids = S<IdentityService>();
        using (At(_today.AddDays(-1), 11))
        {
            Ok((await ids.RequestCorrectionAsync(new CorrectionRequest(P(14).Id, null, "Ji-woo Grace", null, null, null, false, "Patient advised her middle name was missed at registration", "Driver licence sighted"), "reception", ct)).Errors, "identity correction");
            var original = P(45); // registered without an NHI, like the duplicate
            var r = await S<PatientService>().RegisterAsync(new PatientRegistration(original.FamilyName, original.GivenNames.Split(' ')[0], original.DateOfBirth, original.Sex, MobilePhone: original.MobilePhone,
                City: original.City, ConfirmedNotDuplicate: true), "reception2", ct);
            var dup = Must(r.Patient, r.Errors, "duplicate registration");
            Ok((await ids.RequestMergeAsync(new MergeRequest(original.Id, dup.Id, "Registered twice at front desk – same DOB, mobile and address"), "reception", ct)).Errors, "merge request");
        }
    }

    private async Task PortalAsync(CancellationToken ct)
    {
        var portal = S<Por.PortalService>();
        Ok(await portal.AddDelegateAsync("pa", _clin["reid"], Actor, ct), "PA link");
        Ok(await portal.AddDelegateAsync("pa", _clin["whitaker"], Actor, ct), "PA link");
        var i = 0;
        foreach (var (b, s, _) in _upcoming.Where(u => u.Session.Date > _today.AddDays(3)).Take(5))
        {
            i++;
            var p = await S<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == b.PatientId, ct);
            using (At(_today.AddDays(-3), 12))
            {
                var (token, _, err) = await portal.CreateLinkAsync(p.Id, "reception", ct: ct);
                Ok(err, "portal link");
                if (i > 2) continue;
                var (access, _, openErr) = await portal.OpenAsync(token!, p.DateOfBirth, "203.0.113.10", ct);
                if (access is null) throw new InvalidOperationException("Demo data – portal open: " + openErr);
                var (req, rerr) = await portal.AddRequestAsync(access, p.Id, i == 1
                    ? new Por.PortalRequestCreate(Por.PortalRequestKind.ChangeAppointment, b.Id, "Could I please have a later time on the day? I need to drop the kids at school.")
                    : new Por.PortalRequestCreate(Por.PortalRequestKind.Question, b.Id, "Can I take my usual blood pressure tablet on the morning of surgery?"), ct);
                Ok(rerr, "portal request");
                if (i == 2) Ok(await portal.HandleRequestAsync(req!.Id, Por.PortalRequestStatus.Done, "Yes – take it with a sip of water at 6 am. Nurse Hana.", "nurse", ct), "answer portal request");
            }
        }
    }

    // ------------------------------------------------------------------ messaging engine

    private async Task MessagingAsync(CancellationToken ct)
    {
        var svc = S<Msg.MessagingService>();
        var maps = S<Msg.MappingService>();
        var flows = S<Msg.FlowService>();
        Ok(await svc.SaveSettingsAsync(new Msg.SettingsEdit(5, "it-alerts@demo.brightpas.nz", true), "integration", ct), "messaging settings");

        var keys = sp.GetRequiredService<IConfiguration>()["DataProtection:KeysPath"];
        var root = Path.Combine(string.IsNullOrWhiteSpace(keys) ? Path.GetTempPath() : Path.GetDirectoryName(Path.GetFullPath(keys.TrimEnd('\\', '/')))!, "demo-interfaces");
        string Dir(string name) { var d = Path.Combine(root, name); Directory.CreateDirectory(d); return d; }

        Msg.Interface I((Msg.Interface? Interface, IReadOnlyList<string> Errors) r, string what) => Must(r.Interface, r.Errors, what);
        var mllp = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "lab-mllp", "Delta lab results (MLLP)", Msg.InterfaceDirection.Inbound, Msg.Transport.MllpListen, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.LabResults, null, 12575, null, null, null, null, [new Msg.ClientEdit("192.168.1.140", null, "Delta lab interface engine"), new Msg.ClientEdit("127.0.0.1", null, "This server (testing)")],
            true, 3, 24, "Results from the community laboratory."), "integration", ct), "MLLP interface");
        var dicom = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "endo-dicom", "Endoscopy tower images (DICOM)", Msg.InterfaceDirection.Inbound, Msg.Transport.DicomListen, Msg.MessageFormat.Dicom,
            Msg.InboundHandler.None, null, 11112, null, null, null, "BRIGHTPAS", [new Msg.ClientEdit("192.168.1.150", "ENDOTOWER1", "Procedure room 1 tower"), new Msg.ClientEdit("127.0.0.1", "TESTSCU", "Testing from this server")],
            true, 3, null, "Image headers from the endoscopy stack."), "integration", ct), "DICOM interface");
        var api = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "lab-api", "Partner results (REST)", Msg.InterfaceDirection.Inbound, Msg.Transport.HttpReceive, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.None, null, null, null, null, "demo-" + Guid.NewGuid().ToString("N"), null, null, true, 3, null, "Posted to /api/integration/in/lab-api with the interface key."), "integration", ct), "REST interface");
        var pacs = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "pacs-out", "Clinical record feed (HL7 folder)", Msg.InterfaceDirection.Outbound, Msg.Transport.FolderWrite, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.None, null, null, Dir("out-hl7"), null, null, null, null, true, 3, null, "Demo: files are written to the demo-interfaces folder."), "integration", ct), "HL7 folder interface");
        var archive = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "results-json", "Results archive (JSON folder)", Msg.InterfaceDirection.Outbound, Msg.Transport.FolderWrite, Msg.MessageFormat.Json,
            Msg.InboundHandler.None, null, null, Dir("out-json"), null, null, null, null, true, 3, null, null), "integration", ct), "JSON folder interface");
        I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "scan-in", "Scanned results drop folder", Msg.InterfaceDirection.Inbound, Msg.Transport.FolderWatch, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.None, null, null, Dir("in"), null, null, null, null, true, 3, null, "Drop .hl7 files here to test."), "integration", ct), "folder watch interface");
        I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "fhir-partner", "Insurer FHIR API (not live yet)", Msg.InterfaceDirection.Outbound, Msg.Transport.HttpPost, Msg.MessageFormat.Json,
            Msg.InboundHandler.None, null, null, null, "https://partner.example.nz/fhir", null, null, null, false, 3, null, "Switched off until the insurer gives us test credentials."), "integration", ct), "HTTP interface");

        var (table, terr) = await maps.SaveTableAsync(new Msg.TableEdit(null, "HL7 sex to FHIR gender", "Administrative sex codes", Msg.MissingCode.UseDefault, "unknown",
            [new("F", "female", null), new("M", "male", null), new("O", "other", null), new("U", "unknown", null)]), "integration", ct);
        var sexTable = Must(table, terr, "translation table");

        const string sampleOru = "MSH|^~\\&|DELTA|AWANUI-NELSON|BrightPAS|TDS|202611101030||ORU^R01|SAMPLE1|P|2.4\rPID|1||ZZZ0016^^^NHI^NHI||TEST^Aroha^Mere||19800517|F\r" +
                                 "OBR|1|ORD1|ACC1|CBC^Full blood count\rOBX|1|NM|718-7^Haemoglobin||132|g/L|115-155|N|||F\rOBX|2|NM|777-3^Platelets||250|x10*9/L|150-400|N|||F\r";
        async Task<Guid> LiveMapping(string name, Msg.MessageFormat from, Msg.MessageFormat to, string template, string sample, IReadOnlyList<Msg.RuleEdit> rules)
        {
            var (m, errors) = await maps.CreateMappingAsync(new Msg.NewMappingEdit(name, from, to, null), "integration", ct);
            var mapping = Must(m, errors, "mapping " + name);
            var v = (await maps.MappingAsync(mapping.Id, null, ct))!.Selected;
            Ok(await maps.SaveDraftAsync(v.Id, new Msg.VersionEdit(Msg.StartFrom.Template, template, sample, "First version", rules, []), "integration", ct), "mapping rules " + name);
            Ok(await maps.SubmitAsync(v.Id, "integration", ct), "submit mapping");
            Ok(await maps.ApproveAsync(v.Id, "integration2", true, ct), "approve mapping");
            return mapping.Id;
        }
        Msg.RuleEdit R(string target, string? source, Msg.RuleFunction f = Msg.RuleFunction.Copy, string? arg = null, Guid? t = null) => new(target, source, f, arg, t, null, null, null);
        var oruToJson = await LiveMapping("Lab result to JSON archive", Msg.MessageFormat.Hl7v2, Msg.MessageFormat.Json, "{}", sampleOru,
        [
            R("resourceType", null, Msg.RuleFunction.Constant, "LabReport"), R("patient.nhi", "PID-3.1"), R("patient.name", "PID-5.2 + PID-5.1", Msg.RuleFunction.Join, " "),
            R("patient.gender", "PID-8", Msg.RuleFunction.Translate, null, sexTable.Id), R("patient.birthDate", "PID-7", Msg.RuleFunction.Date, "yyyyMMdd>yyyy-MM-dd"),
            R("test", "OBR-4.2"), R("accession", "OBR-3"), R("results[*].code", "OBX[*]-3.1"), R("results[*].name", "OBX[*]-3.2"), R("results[*].value", "OBX[*]-5"), R("results[*].units", "OBX[*]-6"),
        ]);
        const string sampleDicom = """{ "00080050": { "vr": "SH", "Value": ["ACC456"] }, "00080060": { "vr": "CS", "Value": ["ES"] }, "00081030": { "vr": "LO", "Value": ["Colonoscopy"] }, "00100010": { "vr": "PN", "Value": [{ "Alphabetic": "TEST^Aroha^Mere" }] }, "00100020": { "vr": "LO", "Value": ["ZZZ0016"] }, "00100030": { "vr": "DA", "Value": ["19800517"] }, "00100040": { "vr": "CS", "Value": ["F"] } }""";
        var dicomToHl7 = await LiveMapping("Endoscopy image to HL7 ORU", Msg.MessageFormat.Dicom, Msg.MessageFormat.Hl7v2, "MSH|^~\\&|BrightPAS|TDS|||||ORU^R01||P|2.4\rPID|1\rOBR|1\rOBX|1|TX\r", sampleDicom,
        [
            R("PID-3.1", "PatientID"), R("PID-3.5", null, Msg.RuleFunction.Constant, "NHI"), R("PID-5.1", "PatientName", Msg.RuleFunction.Split, "^|1"), R("PID-5.2", "PatientName", Msg.RuleFunction.Split, "^|2"),
            R("PID-7", "PatientBirthDate"), R("PID-8", "PatientSex"), R("OBR-3", "AccessionNumber"), R("OBR-4.2", "StudyDescription"), R("MSH-10", "AccessionNumber"),
            R("OBX-5", "Modality"),
        ]);
        // A change waiting for the second person to approve (shows the approval queue).
        var (draft, derr) = await maps.NewDraftAsync(oruToJson, (await maps.MappingAsync(oruToJson, null, ct))!.Selected.Id, "integration", ct);
        Ok(derr, "new mapping draft");
        Ok(await maps.SaveDraftAsync(draft!.Id, new Msg.VersionEdit(Msg.StartFrom.Template, "{}", sampleOru, "Add reference range",
            [R("resourceType", null, Msg.RuleFunction.Constant, "LabReport"), R("patient.nhi", "PID-3.1"), R("patient.name", "PID-5.2 + PID-5.1", Msg.RuleFunction.Join, " "),
             R("patient.gender", "PID-8", Msg.RuleFunction.Translate, null, sexTable.Id), R("test", "OBR-4.2"), R("accession", "OBR-3"), R("results[*].code", "OBX[*]-3.1"),
             R("results[*].value", "OBX[*]-5"), R("results[*].units", "OBX[*]-6"), R("results[*].range", "OBX[*]-7")], []), "integration", ct), "draft rules");
        Ok(await maps.SubmitAsync(draft.Id, "integration", ct), "submit draft");

        Msg.NodeEdit N(string k, Msg.NodeKind kind, int x, int y, Guid? iface = null, Guid? map = null, params Msg.FilterEdit[] c) => new(k, kind, k switch
        {
            "in" => "Receive", "isOru" => "Result message?", "map" => "To JSON", "json" => "Archive", "hl7" => "Clinical feed", "img" => "To HL7", _ => k
        }, x, y, iface, map, Msg.MatchMode.All, c);
        var (f1, e1) = await flows.SaveAsync(new Msg.FlowEdit(null, "Partner lab results", "REST results: archive as JSON, everything else to the clinical feed.", true,
            [N("in", Msg.NodeKind.Input, 40, 120, api.Id), N("isOru", Msg.NodeKind.Decision, 260, 120, c: new Msg.FilterEdit("MSH-9.1", Msg.ConditionOperator.Equals, "ORU")),
             N("map", Msg.NodeKind.Mapper, 480, 60, map: oruToJson), N("json", Msg.NodeKind.Output, 700, 60, archive.Id), N("hl7", Msg.NodeKind.Output, 480, 220, pacs.Id)],
            [new("in", "isOru", Msg.EdgeBranch.Next), new("isOru", "map", Msg.EdgeBranch.Yes), new("map", "json", Msg.EdgeBranch.Next), new("isOru", "hl7", Msg.EdgeBranch.No)]), "integration", ct);
        var flow1 = Must(f1, e1, "flow 1");
        var (f2, e2) = await flows.SaveAsync(new Msg.FlowEdit(null, "Endoscopy images to clinical record", "DICOM image headers become HL7 ORU messages.", true,
            [N("in", Msg.NodeKind.Input, 40, 120, dicom.Id), N("img", Msg.NodeKind.Mapper, 280, 120, map: dicomToHl7), N("hl7", Msg.NodeKind.Output, 520, 120, pacs.Id)],
            [new("in", "img", Msg.EdgeBranch.Next), new("img", "hl7", Msg.EdgeBranch.Next)]), "integration", ct);
        Must(f2, e2, "flow 2");

        // Traffic: results posted by the partner lab and images from the endoscopy tower over the last few days.
        var n = 0;
        foreach (var (b, s, _) in _done.Where(d => d.Session.Date >= _today.AddDays(-10)).Take(12))
        {
            n++;
            var p = await S<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == b.PatientId, ct);
            if (p.Nhi is null) continue;
            var sex = p.Sex == BirthSex.Female ? "F" : "M";
            using (At(s.Date, 15, n))
            {
                string content; Msg.Interface iface; Guid flowId;
                if (s.Type == SessionType.Endoscopy)
                {
                    content = $$"""{ "00080050": { "vr": "SH", "Value": ["ES{{b.Id.ToString()[..8].ToUpperInvariant()}}"] }, "00080060": { "vr": "CS", "Value": ["ES"] }, "00081030": { "vr": "LO", "Value": ["{{b.Procedure}}"] }, "00100010": { "vr": "PN", "Value": [{ "Alphabetic": "{{p.FamilyName.ToUpperInvariant()}}^{{p.GivenNames.Split(' ')[0]}}" }] }, "00100020": { "vr": "LO", "Value": ["{{p.Nhi}}"] }, "00100030": { "vr": "DA", "Value": ["{{p.DateOfBirth:yyyyMMdd}}"] }, "00100040": { "vr": "CS", "Value": ["{{sex}}"] } }""";
                    iface = dicom; flowId = f2!.Id;
                }
                else
                {
                    content = n % 4 == 0
                        ? $"MSH|^~\\&|PARTNER|LAB|BrightPAS|TDS|{s.Date:yyyyMMdd}1500||ADT^A08|ADT{n}|P|2.4\rPID|1||{p.Nhi}^^^NHI^NHI||{p.FamilyName.ToUpperInvariant()}^{p.GivenNames.Split(' ')[0]}||{p.DateOfBirth:yyyyMMdd}|{sex}\r"
                        : Oru($"PL{n:D4}", p.Nhi, p.FamilyName, p.GivenNames.Split(' ')[0], p.DateOfBirth, sex[0], new DateTimeOffset(s.Date.ToDateTime(new TimeOnly(15, 0)), TimeSpan.FromHours(13)), "Full blood count", "CBC",
                            [("718-7", "Haemoglobin", (120 + n).ToString(), "g/L", "115-155", "N"), ("777-3", "Platelets", "240", "x10*9/L", "150-400", "N")]);
                    iface = api; flowId = flow1.Id;
                }
                var msg = await svc.ReceiveAsync(iface, content, iface.Code == "endo-dicom" ? "ENDOTOWER1@192.168.1.150" : "203.0.113.25", null, ct);
                await svc.CompleteInboundAsync(msg.Id, Msg.MessageStatus.Processed, iface.Transport == Msg.Transport.HttpReceive ? "AA" : null, null, null, null, ct);
                await flows.ProcessAsync(msg, svc.ContentOf(msg), ct);
            }
        }
        // An MCP connection for AI tools (token not kept – create a new one in Messaging › MCP to use it).
        Ok((await S<Msg.McpService>().CreateAsync("Claude Desktop (demo)", 30, "integration", ct)).Error, "MCP client");
    }

    // ------------------------------------------------------------------ reminders

    private async Task RemindersAsync(CancellationToken ct)
    {
        var svc = S<Rem.ReminderService>();
        await svc.EnsureTemplatesAsync(ct);
        Ok(await svc.UpdateSettingsAsync(new Rem.ReminderService.SettingsUpdate(Rem.SmsProvider.TestOutbox, Rem.EmailProvider.TestOutbox, true, true, true, 10, 60,
            new TimeOnly(20, 0), new TimeOnly(8, 0), "Tasman Day Surgery (DEMO)", "03 555 0100", null, null, null, null, null, null, null, null, null, 587, true, null, null,
            "reception@demo.brightpas.nz", "Tasman Day Surgery (DEMO)"), "admin", ct), "reminder settings");
        await svc.QueueTestAsync(Rem.Channel.Sms, "+64215550000", "admin", ct);
        await svc.QueueTestAsync(Rem.Channel.Email, "admin@demo.brightpas.nz", "admin", ct);
        await svc.SendDueAsync(ct);
        await svc.SetPreferenceAsync(P(48).Id, false, true, "en", "reception", ct);
        // Booking confirmations and day-before reminders for upcoming bookings (test outbox: recorded, not sent).
        await svc.RunCycleAsync(ct);
        // Patients replying to their reminders.
        var sent = (await svc.MessagesAsync(null, null, ct)).Where(m => m.Channel == "Sms" && m.Kind != "Test").Take(4).ToList();
        var replies = new[] { "YES", "Yes see you then", "C", "Can I change to the afternoon?" };
        for (var i = 0; i < sent.Count; i++)
        {
            var p = await S<PatientsDbContext>().Patients.AsNoTracking().SingleAsync(x => x.Id == sent[i].PatientId, ct);
            if (p.MobilePhone is null) continue;
            await svc.HandleReplyAsync("+64" + new string(p.MobilePhone.Where(char.IsDigit).ToArray()).TrimStart('0'), replies[i], "TestOutbox", $"demo-reply-{i}", null, ct);
        }
    }

    // ------------------------------------------------------------------ module set-up

    private async Task CompleteModuleSetupAsync(CancellationToken ct)
    {
        var setup = S<Licensing.ModuleSetupService>();
        var problems = new List<string>();
        foreach (var id in Licensing.ModuleSetupService.WithWizard)
        {
            var (ok, error) = await setup.CompleteAsync(id, "admin", ct);
            if (!ok) problems.Add($"{id}: {error}");
        }
        if (problems.Count > 0) throw new InvalidOperationException("Demo data – module set-up: " + string.Join(" | ", problems));
    }
}
