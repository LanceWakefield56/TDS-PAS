using BrightPAS.Core;
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
using Ast = BrightPAS.Modules.Assets;
using Cred = BrightPAS.Modules.Credentialing;
using Emr = BrightPAS.Modules.Emr;
using Endo = BrightPAS.Modules.Endoscopy;
using Scr = BrightPAS.Modules.Scribe;
using Dev = BrightPAS.Modules.Devices;
using Qm = BrightPAS.Modules.Quality;
using Cod = BrightPAS.Modules.Coding;
using Est = BrightPAS.Modules.Estimates;
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
        new("quality", "Morgan Hayes (Quality Manager)", [DefaultRoles.QualityManager], "Incidents, complaints, risks, actions, policies, indicators"),
        new("coder", "Ana Fale (Clinical Coder)", [DefaultRoles.ClinicalCoder], "Clinical coding, NMDS and NBRS files"),
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
        await AssetsAsync(ct);
        await EmrAsync(ct);
        await EndoscopyAsync(ct);
        await ScribeAsync(ct);
        await MessagingAsync(ct);
        await DevicesAsync(ct);
        await QualityAsync(ct);
        await CodingAsync(ct);
        await EstimatesAsync(ct);
        await TriageAsync(ct);
        await FollowUpAsync(ct);
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
        return DemoClock.At(NzTime.FromLocal(local));
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
        var fees = new Dictionary<string, string>
        {
            ["COLON"] = "COLON",
            ["GASTRO"] = "GASTRO",
            ["CARPAL"] = "CTR",
            ["INGHERNIA"] = "HERNIA",
            ["POLYPECT"] = "COLON",
            ["GASTROCOL"] = "GASTROCOL",
            ["LAPCHOLE"] = "LAPCHOLE",
            ["UMBHERNIA"] = "UMBHERNIA",
            ["SKINLES"] = "SKINLES",
            ["TRIGGER"] = "TRIGGER",
            ["BCC"] = "BCC",
            ["FLEXSIG"] = "FLEXSIG"
        };
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
            if (c == 0 && skipped.Count > 0 && skipped[0].StartsWith("Choose", StringComparison.Ordinal)) throw new InvalidOperationException("Demo data – roster: " + skipped[0]);
            created += c;
        }
        if (created == 0) throw new InvalidOperationException("Demo data – roster: no shifts were created.");
    }

    // ------------------------------------------------------------------ Clinical record (0.14.0)
    private async Task EmrAsync(CancellationToken ct)
    {
        var svc = S<Emr.EmrService>();
        const string Witness = "Demo Witness RN";
        var drugs = new Dictionary<string, Guid>();
        foreach (var (name, strength, form, qty) in new[] { ("Fentanyl", "100 microgram/2 mL", "ampoule", 20), ("Morphine", "10 mg/mL", "ampoule", 10), ("Midazolam", "5 mg/5 mL", "ampoule", 20), ("Oxycodone", "5 mg", "tablet", 40) })
        {
            var (d, err) = await svc.AddControlledDrugAsync(name, strength, form, Actor, ct);
            Ok(err, "controlled drug " + name);
            drugs[name] = d!.Id;
            Ok(await svc.RegisterAsync(new Emr.CdRequest(d.Id, Emr.CdMovement.Received, qty, Witness, "Pharmacy order " + _rng.Next(1000, 9999)), Actor, ct), "CD received");
        }
        var team = new Dictionary<string, string> { ["Nurse"] = "Demo Scrub RN", ["Anaesthetist"] = "Demo Anaesthetist", ["Surgeon"] = "Demo Surgeon" };
        // Completed records for recent finished cases.
        foreach (var (b, sess, _) in _done.OrderByDescending(x => x.Session.Date).Take(6))
        {
            var e = await svc.EpisodeAsync(b.Id, b.PatientId, Actor, ct);
            var sed = b.Anaesthetic is not (AnaestheticType.Local or AnaestheticType.None);
            Ok((await svc.AssessAsync(e.Id, new Emr.AssessmentRequest(Emr.AssessmentKind.NursingAdmission, new Dictionary<string, string> { ["id_band"] = "yes", ["allergies_checked"] = "None known", ["fasting_from"] = "02:00", ["escort_name"] = "Family member" }, null), Actor, ct)).Errors, "nursing admission");
            Ok(await svc.AdmitAsync(e.Id, sed, Actor, ct), "admit");
            Ok((await svc.AssessAsync(e.Id, new Emr.AssessmentRequest(Emr.AssessmentKind.Vte, new Dictionary<string, string> { ["age"] = "55", ["major_surgery"] = "yes" }, "Mechanical prophylaxis (stockings)"), Actor, ct)).Errors, "VTE");
            Ok((await svc.AssessAsync(e.Id, new Emr.AssessmentRequest(Emr.AssessmentKind.Falls, new Dictionary<string, string> { ["sedation"] = sed ? "yes" : "no", ["age"] = "55" }, null), Actor, ct)).Errors, "falls");
            foreach (var phase in new[] { Emr.CheckPhase.SignIn, Emr.CheckPhase.TimeOut })
                Ok((await svc.CompleteCheckAsync(e.Id, new Emr.CheckRequest(phase, Emr.EmrService.CheckItems[phase].Select(i => i.Key).ToList(), team, null), Actor, ct)).Errors, "checklist " + phase);
            var t0 = NzTime.FromLocal(sess.Date.ToDateTime(new TimeOnly(9, 0))).AddMinutes(_rng.Next(0, 120));
            Ok((await svc.SaveIntraopAsync(e.Id, new Emr.IntraopEdit(t0, t0.AddMinutes(5), t0.AddMinutes(15), t0.AddMinutes(55), t0.AddMinutes(65), "Supine", "Gel head ring, heel pads", "Chlorhexidine 2% in alcohol",
                "ELEC-01", "Left thigh", true, null, null, null, null, true, "Clean", null, "Island dressing", null), Actor, ct)).Errors, "intraop");
            foreach (var stage in new[] { Emr.CountStage.Initial, Emr.CountStage.Final })
                foreach (var (item, n) in new[] { ("Swabs", 10), ("Sharps", 4) })
                    Ok((await svc.CountAsync(e.Id, new Emr.CountRequest(stage, item, n, n, "Demo Circulating RN"), Actor, ct)).Error, "count");
            Ok((await svc.CompleteCheckAsync(e.Id, new Emr.CheckRequest(Emr.CheckPhase.SignOut, Emr.EmrService.CheckItems[Emr.CheckPhase.SignOut].Select(i => i.Key).ToList(), team, null), Actor, ct)).Errors, "sign out");
            Ok((await svc.GiveAsync(e.Id, new Emr.MedicationRequest("Cefazolin", "2 g", "IV", t0.AddMinutes(8), null, null, null, null), Actor, ct)).Error, "medication");
            if (sed) Ok((await svc.GiveAsync(e.Id, new Emr.MedicationRequest("Fentanyl", "50 microgram", "IV", t0.AddMinutes(6), null, drugs["Fentanyl"], Witness, null), Actor, ct)).Error, "CD given");
            Ok(await svc.FluidAsync(e.Id, new Emr.FluidRequest(t0.AddMinutes(60), true, "IV", 500), Actor, ct), "fluids");
            foreach (var m in new[] { 75, 105 })
                Ok((await svc.ObserveAsync(e.Id, new Emr.ObservationRequest(t0.AddMinutes(m), 14, 97, false, null, 124, 78, 72, 36.6m, "A", 2, 0), Actor, ct)).Errors, "observations");
            Ok((await svc.NoteAsync(e.Id, new Emr.NoteRequest(Emr.NoteKind.OperationNote, $"{b.Procedure}. Uncomplicated. Haemostasis secured. Counts correct.", true, null), Actor, ct)).Error, "op note");
            Ok(await svc.DischargeAsync(e.Id, new Emr.DischargeRequest("Family member", "021 555 0101", "Family member", true, null, true, "Home", sess.Date.AddDays(1)), Actor, ct), "discharge");
        }
        // Today's patients (if any list runs today): admitted with the nursing admission done.
        foreach (var (b, _, _) in _upcoming.Where(x => x.Session.Date == _today).Take(2))
        {
            var e = await svc.EpisodeAsync(b.Id, b.PatientId, Actor, ct);
            Ok((await svc.AssessAsync(e.Id, new Emr.AssessmentRequest(Emr.AssessmentKind.NursingAdmission, new Dictionary<string, string> { ["id_band"] = "yes", ["allergies_checked"] = "None known", ["fasting_from"] = "06:00", ["escort_name"] = "Partner" }, null), Actor, ct)).Errors, "nursing admission today");
            Ok(await svc.AdmitAsync(e.Id, b.Anaesthetic is not (AnaestheticType.Local or AnaestheticType.None), Actor, ct), "admit today");
        }
    }

    // ------------------------------------------------------------------ BrightPAS Scribe (0.16.0)
    private async Task ScribeAsync(CancellationToken ct)
    {
        var svc = S<Scr.ScribeService>();
        var templates = await svc.TemplatesAsync(ct);
        await svc.DictionaryAsync(ct);
        foreach (var c in await S<PasDbContext>().Clinicians.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct))
        {
            var surname = c.Name.Split(' ').Last();
            var (_, err) = await svc.SaveTermAsync(null, new Scr.TermEdit(c.Name, surname.Length > 3 ? $"doctor {surname.ToLowerInvariant()}" : null, "Clinician"), Actor, ct);
            if (err is not null && !err.Contains("already")) throw new InvalidOperationException("Demo data – scribe dictionary: " + err);
        }
        var consult = templates.Single(t => t.Code == "CONSULT");
        var cases = _done.OrderByDescending(d => d.Session.Date).Skip(2).Take(4).ToList();
        var n = 0;
        foreach (var (b, _, _) in cases)
        {
            var (sess, errs) = await svc.StartAsync(new Scr.NewSession(b.PatientId, b.Id, Scr.ScribeContext.Consultation, consult.Id), Actor, ct);
            Ok(errs, "scribe session");
            Ok((await svc.RecordConsentAsync(b.PatientId, sess!.Id, true, n == 0, Actor, ct)).Error, "scribe consent");
            Scr.Segment Seg(int i, string who, string text, decimal start) => new(i, who, start, start + 6, text, i == 4 ? 0.52m : 0.93m);
            var segs = new List<Scr.Segment>
            {
                Seg(1, "Clinician", $"Kia ora, thanks for coming in. Your GP referred you about {b.Procedure.ToLowerInvariant()}.", 0),
                Seg(2, "Patient", "Yes, it has been bothering me for about six months, mostly in the evenings.", 7),
                Seg(3, "Clinician", "Any allergies, and what medicines are you taking?", 14),
                Seg(4, "Patient", "No allergies. I take metformin 500 mg twice a day and aspirin.", 20),
                Seg(5, "Clinician", "On examination there is mild tenderness on the right side. We talked about the procedure, the risks of bleeding and infection, and you would like to go ahead.", 27),
                Seg(6, "Patient", "Yes please. My wife will drive me home.", 40),
                Seg(7, "Clinician", "I'll book you in, stop the aspirin seven days before, and we'll see you two weeks after.", 46),
            };
            await svc.SaveTranscriptAsync(sess.Id, segs, "pas-speech large-v3 (demo)", Actor, ct);
            var draft = new List<Scr.DraftedSentence>
            {
                new("Reason for referral", $"GP referral regarding {b.Procedure.ToLowerInvariant()}.", [1]),
                new("History", "Symptoms for about six months, mainly in the evenings.", [2]),
                new("Medicines and allergies", "No known allergies. Metformin 500 mg twice daily; aspirin.", [3, 4]),
                new("Examination", "Mild tenderness on the right side.", [5]),
                new("Discussion", "Procedure, risks of bleeding and infection discussed; patient wishes to proceed.", [5, 6]),
                new("Plan", "Book procedure. Stop aspirin 7 days before. Escort home arranged (wife). Review 2 weeks after.", [6, 7]),
            };
            await svc.SaveDraftAsync(sess.Id, draft, "pas-speech llama-3.1-70b (demo)", false, Actor, ct);
            if (n < 3)
            {
                var text = Scr.ScribeText.Render(draft) + (n == 1 ? "\nPatient given written information." : "");
                Ok((await svc.SignAsync(sess.Id, n == 2 ? text.Replace("Mild tenderness on the right side.", "Mild right iliac fossa tenderness, no guarding.") : text, Actor, ct)).Error, "scribe sign");
            }
            n++;
        }
    }

    // ------------------------------------------------------------------ Endoscopy (0.15.0)
    private async Task EndoscopyAsync(CancellationToken ct)
    {
        var svc = S<Endo.EndoService>();
        var names = await S<PasDbContext>().Clinicians.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var scopes = await S<Ast.AssetsDbContext>().Equipment.AsNoTracking().Where(e => e.Category == Ast.EquipmentCategory.Endoscope).OrderBy(e => e.AssetNumber).ToListAsync(ct);
        var tower = await S<Ast.AssetsDbContext>().Equipment.AsNoTracking().Where(e => e.Category == Ast.EquipmentCategory.EndoscopyTower).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        Guid? Scope(string prefix) => scopes.FirstOrDefault(x => (x.Model ?? "").StartsWith(prefix, StringComparison.Ordinal))?.Id;
        var cases = _done.Where(d => Endo.Terminology.Guess(d.Booking.Procedure) is not null).OrderByDescending(d => d.Session.Date).ThenBy(d => d.Booking.Order).Take(12).ToList();
        if (cases.Count == 0) return;
        var n = 0;
        foreach (var (b, sess, _) in cases)
        {
            var kind = Endo.Terminology.Guess(b.Procedure)!.Value;
            var p = await svc.EnsureAsync(b.Id, b.PatientId, b.Procedure, sess.SurgeonId, names.GetValueOrDefault(sess.SurgeonId), sess.Date, Actor, ct);
            var up = Endo.Terminology.HasUpper(kind); var low = Endo.Terminology.HasLower(kind); var col = Endo.Terminology.IsColonoscopy(kind);
            var ind = new List<string>();
            if (up) ind.Add(new[] { "Dyspepsia", "Reflux", "IdaUpper", "BarrettSurveillance" }[n % 4]);
            if (low) ind.Add(n % 3 == 0 ? "PositiveFit" : new[] { "RectalBleeding", "ChangeInBowelHabit", "PolypSurveillance" }[n % 3]);
            Ok(await svc.UpdateDetailsAsync(p.Id, new Endo.DetailsEdit(kind, sess.SurgeonId, names.GetValueOrDefault(sess.SurgeonId), "Demo Endoscopy RN, Demo Assistant",
                up ? Scope("GIF") : null, low ? Scope("CF") : null, tower, low && n % 3 == 0, low && n % 3 == 0 ? $"NBSP{_rng.Next(100000, 999999)}" : null, ind, null, true,
                Endo.SedationKind.Conscious, "Midazolam and fentanyl", true, false, false, n % 2 == 0, false), Actor, ct), "endoscopy details");
            var t0 = NzTime.FromLocal(sess.Date.ToDateTime(b.TheatreIn ?? sess.StartTime.AddMinutes(30 * b.Order)));
            var lowStart = up ? t0.AddMinutes(10) : t0;
            var reached = !col || n % 7 != 5;
            var withdrawal = 6 + _rng.Next(0, 9) + (n % 6 == 4 ? -2 : 0);
            Ok((await svc.UpdateProcedureAsync(p.Id, new Endo.ProcedureEdit(
                up ? t0 : null, up ? t0.AddMinutes(7 + _rng.Next(0, 4)) : null, up ? Endo.UpperExtent.D2 : null, up ? true : null, up ? true : null,
                low ? lowStart : null, col && reached ? lowStart.AddMinutes(8 + _rng.Next(0, 8)) : null, null, low ? lowStart.AddMinutes((col ? 16 : 10) + withdrawal) : null,
                low ? (col ? (reached ? (n % 4 == 0 ? Endo.LowerExtent.TerminalIleum : Endo.LowerExtent.Caecum) : Endo.LowerExtent.HepaticFlexure) : Endo.LowerExtent.Descending) : null,
                col && reached ? true : null, col && reached ? true : null, col && reached ? n % 4 == 0 : null, col && reached ? true : null,
                col ? 2 + n % 2 : null, col ? 2 + (n + 1) % 2 : null, col ? (n % 7 == 5 ? 1 : 3) : null, low ? true : null,
                col && !reached ? "Poor bowel preparation in the right colon" : null, 1 + n % 3), Actor, ct)).Errors, "endoscopy procedure");
            var findings = new List<string>();
            if (up)
            {
                var (kindF, grade, site) = (n % 4) switch { 0 => ("Gastritis", (string?)null, "Antrum"), 1 => ("Oesophagitis", (string?)"LA A", "LowerOesophagus"), 2 => ("HiatusHernia", (string?)"< 2 cm", "Goj"), _ => ("Barretts", (string?)"C1M2", "LowerOesophagus") };
                Ok((await svc.SaveFindingAsync(p.Id, null, new Endo.FindingEdit(site, kindF, grade, null, null), Actor, ct)).Errors, "finding");
                Ok((await svc.SaveInterventionAsync(p.Id, null, new Endo.InterventionEdit("Biopsy", kindF == "Barretts" ? "LowerOesophagus" : "Antrum", kindF == "Barretts" ? 8 : 4, "B1", kindF == "Barretts" ? "Barrett's (Seattle)" : "Sydney protocol", null, null), Actor, ct)).Errors, "biopsy");
                if (kindF == "Gastritis") Ok((await svc.SaveInterventionAsync(p.Id, null, new Endo.InterventionEdit("Clo", "Antrum", 1, null, null, null, "Negative"), Actor, ct)).Errors, "CLO");
                findings.Add(Endo.Terminology.FindingLabel(kindF) + (grade is null ? "" : $" ({grade})"));
            }
            var polyps = new List<Endo.Polyp>();
            if (low)
            {
                if (n % 3 != 1) Ok((await svc.SaveFindingAsync(p.Id, null, new Endo.FindingEdit("Sigmoid", "Diverticulosis", "Mild", null, null), Actor, ct)).Errors, "finding");
                var count = n % 5 == 0 ? 0 : 1 + n % 3;
                for (var i = 0; i < count; i++)
                {
                    var big = n % 4 == 3 && i == 0;
                    var site = new[] { "Ascending", "Transverse", "Sigmoid", "Rectum", "Descending" }[(n + i) % 5];
                    var (polyp, pe) = await svc.SavePolypAsync(p.Id, null, new Endo.PolypEdit(site, site == "Rectum" ? 8 : site == "Sigmoid" ? 25 : null, big ? 22 : 3 + _rng.Next(0, 6), big ? "LST-G" : i % 2 == 0 ? "Is" : "IIa",
                        big ? "2" : "2", null, big ? "Emr" : "ColdSnare", big ? true : null, big ? "Piecemeal" : "EnBloc", "Yes", $"P{i + 1}", "Yes", big ? 2 : 0, big, null), Actor, ct);
                    polyps.Add(Must(polyp, pe, "polyp"));
                    if (big) Ok((await svc.SaveTattooAsync(p.Id, null, new Endo.TattooEdit(site, null, "Distal", "Spot (carbon particle suspension)", 1.5m, 3, polyp!.Id, "EMR site for surveillance"), Actor, ct)).Errors, "tattoo");
                }
                if (n % 3 == 0) Ok((await svc.SaveFindingAsync(p.Id, null, new Endo.FindingEdit("Anus", "Haemorrhoids", "Grade 1", null, null), Actor, ct)).Errors, "finding");
            }
            // Pictures (invented): landmarks plus one per polyp; the first few in each report.
            var pics = new List<(Guid Id, string Site, string Caption, Guid? Polyp)>();
            async Task Pic(string site, string caption, bool polypLook, Guid? polypId)
            {
                var (m, err) = await svc.AddImageAsync(p.Id, Endo.Imaging.DemoPicture(_rng.Next(), polypLook), Endo.MediaSource.Dicom, "tower CV1500_PR1", null, ct);
                Ok(err, "demo picture");
                pics.Add((m!.Id, site, caption, polypId));
            }
            if (up) { await Pic("Goj", "Z-line", false, null); await Pic("Antrum", "Antrum", false, null); await Pic("D2", "D2", false, null); }
            if (col && reached) { await Pic("Caecum", "Appendiceal orifice", false, null); await Pic("Caecum", "Ileocaecal valve", false, null); }
            foreach (var x in polyps) await Pic(x.Site, $"Polyp {x.Number} – {x.SizeMm} mm {x.Paris}", true, x.Id);
            if (low) await Pic("Rectum", "Rectal retroflexion", false, null);
            var k = 0;
            foreach (var (id, site, caption, pid) in pics)
            {
                Ok(await svc.UpdateMediaAsync(id, new Endo.MediaEdit(site, caption, k < 6, pid is not null || k < 2, null, null, pid), Actor, ct), "picture details");
                k++;
            }
            // Histology for the older cases (results back); the newest stay "awaited".
            if (n >= 3)
                foreach (var x in polyps)
                    Ok(await svc.HistologyAsync(x.Id, new Endo.HistologyEdit(x.SizeMm >= 20 ? "TubulovillousAdenoma" : (n + x.Number) % 3 == 0 ? "Hyperplastic" : (n + x.Number) % 4 == 0 ? "SessileSerrated" : "TubularAdenoma",
                        x.SizeMm >= 20 ? "Low" : "None", null), "pathology", ct), "histology");
            var diagnosis = string.Join("; ", findings.Concat(polyps.Count > 0 ? [$"{polyps.Count} polyp(s) removed"] : []).DefaultIfEmpty("Normal examination")) + ".";
            Ok(await svc.UpdateReportAsync(p.Id, new Endo.ReportEdit("None", null, diagnosis, polyps.Count > 0 ? "Await histology. Surveillance as per the result." : "No further endoscopic follow-up needed for this indication.",
                up && findings.Any(f => f.StartsWith("Barrett", StringComparison.Ordinal)) ? "Repeat gastroscopy for Barrett's surveillance in 3 years." : null, polyps.Any(x => x.SizeMm >= 20) ? 6 : null,
                polyps.Count > 0 ? "We will write to you and your GP with the laboratory results." : null), Actor, ct), "endoscopy report");
            if (n != 0) Ok(await svc.SignAsync(p.Id, Actor, ct), "sign endoscopy");
            n++;
        }
    }

    // ------------------------------------------------------------------ Assets & CSSD (0.13.0)
    private async Task AssetsAsync(CancellationToken ct)
    {
        var eq = S<Ast.EquipmentService>();
        var cssd = S<Ast.CssdService>();
        var stock = S<Ast.StockService>();
        var now = _clock.GetUtcNow();
        Guid Sup(string name, string? xero, string email) => Must(eq.SaveSupplierAsync(new Ast.SupplierEdit(null, name, "Orders desk", email, "0800 555 0" + _rng.Next(100, 999), xero, true), Actor, ct).GetAwaiter().GetResult().Supplier, [], name).Id;
        var medSup = Sup("Medical Supplies NZ (demo)", "Medical Supplies NZ Ltd", "orders@medsupplies.example.nz");
        var belimed = Sup("Belimed NZ (demo)", "Belimed NZ", "service@belimed.example.nz");
        var olympus = Sup("Olympus NZ (demo)", "Olympus NZ", "service@olympus.example.nz");
        var loans = Sup("Ortho Loans NZ (demo)", null, "loans@ortholoans.example.nz");

        async Task<Guid> E(string no, string name, Ast.EquipmentCategory cat, string make, string model, string? room, Ast.RiskClass risk, Guid? sup, decimal? value, string? ip = null)
        {
            var (row, errors) = await eq.SaveAsync(new Ast.EquipmentEdit(null, no, name, cat, make, model, "SN" + _rng.Next(100000, 999999), room is null ? null : _loc[room], cat is Ast.EquipmentCategory.Washer or Ast.EquipmentCategory.Steriliser or Ast.EquipmentCategory.Aer or Ast.EquipmentCategory.DryingCabinet or Ast.EquipmentCategory.Endoscope ? "CSSD" : "Theatre",
                sup, _today.AddYears(-3).AddDays(-_rng.Next(0, 700)), value, _today.AddYears(1).AddDays(_rng.Next(-500, 300)), risk, ip, null), Actor, ct);
            return Must(row, errors, "equipment " + no).Id;
        }
        var washer = await E("WD-01", "Washer-disinfector", Ast.EquipmentCategory.Washer, "Belimed", "WD 290 IQ", null, Ast.RiskClass.High, belimed, 85000);
        var steriliser = await E("ST-01", "Sterilizer 1", Ast.EquipmentCategory.Steriliser, "Belimed", "MST-V 6-6-9", null, Ast.RiskClass.High, belimed, 120000);
        var aer = await E("AER-01", "Endoscope reprocessor", Ast.EquipmentCategory.Aer, "Olympus", "OER-Elite", null, Ast.RiskClass.High, olympus, 60000);
        await E("DRY-01", "Scope drying cabinet", Ast.EquipmentCategory.DryingCabinet, "Olympus", "ETD-Dry", null, Ast.RiskClass.Medium, olympus, 25000);
        var scopes = new List<Guid>();
        foreach (var (no, name, model) in new[] { ("GIF-H190-01", "Gastroscope 1", "GIF-H190"), ("GIF-H190-02", "Gastroscope 2", "GIF-H190"), ("GIF-H190-03", "Gastroscope 3", "GIF-H190"),
                     ("CF-HQ190L-1", "Colonoscope 1", "CF-HQ190L"), ("CF-HQ190L-2", "Colonoscope 2", "CF-HQ190L"), ("CF-HQ190L-3", "Colonoscope 3", "CF-HQ190L") })
            scopes.Add(await E(no, name, Ast.EquipmentCategory.Endoscope, "Olympus", model, null, Ast.RiskClass.High, olympus, 45000));
        await E("TOWER-01", "Endoscopy video tower", Ast.EquipmentCategory.EndoscopyTower, "Olympus", "EVIS X1 CV-1500", "PR1", Ast.RiskClass.High, olympus, 150000, "192.168.1.150");
        var mon1 = await E("MON-01", "Patient monitor – theatre", Ast.EquipmentCategory.Monitor, "GE HealthCare", "CARESCAPE B450", "TH1", Ast.RiskClass.High, medSup, 28000, "192.168.1.171");
        var mon2 = await E("MON-02", "Patient monitor – recovery 1", Ast.EquipmentCategory.Monitor, "GE HealthCare", "CARESCAPE B155", null, Ast.RiskClass.High, medSup, 12000, "192.168.1.172");
        var diathermy = await E("ELEC-01", "Diathermy generator", Ast.EquipmentCategory.Electromedical, "Medtronic", "Valleylab FT10", "TH1", Ast.RiskClass.High, medSup, 38000);
        var suction = await E("ELEC-02", "Theatre suction unit", Ast.EquipmentCategory.Electromedical, "Medela", "Basic 30", "TH1", Ast.RiskClass.Medium, medSup, 3500);
        var warmer = await E("ELEC-03", "Patient warming unit", Ast.EquipmentCategory.Electromedical, "3M", "Bair Hugger 675", "TH1", Ast.RiskClass.Medium, medSup, 4200);
        await E("FURN-01", "Operating table", Ast.EquipmentCategory.Furniture, "Maquet", "Alphamaxx", "TH1", Ast.RiskClass.Medium, medSup, 90000);

        foreach (var id in new[] { mon1, mon2, diathermy, suction, warmer })
        {
            Ok(await eq.SavePlanAsync(new Ast.PlanEdit(id, Ast.MaintenanceKind.TestAndTag, 12, _today.AddDays(_rng.Next(20, 300)), "Nelson Electrical Testing (demo)"), Actor, ct), "test and tag plan");
            Ok((await eq.RecordAsync(new Ast.MaintenanceEdit(id, Ast.MaintenanceKind.TestAndTag, _today.AddMonths(-_rng.Next(2, 10)), "Nelson Electrical Testing (demo)", true, "T-" + _rng.Next(1000, 9999), null), Actor, ct)).Errors, "test and tag");
        }
        foreach (var id in new[] { washer, steriliser, aer })
        {
            Ok(await eq.SavePlanAsync(new Ast.PlanEdit(id, Ast.MaintenanceKind.Ppm, 6, _today.AddDays(_rng.Next(15, 150)), id == aer ? "Olympus NZ (demo)" : "Belimed NZ (demo)"), Actor, ct), "PPM plan");
            Ok(await eq.SavePlanAsync(new Ast.PlanEdit(id, Ast.MaintenanceKind.Validation, 12, _today.AddDays(_rng.Next(60, 330)), "Sterile Validation Services (demo)"), Actor, ct), "validation plan");
            Ok((await eq.RecordAsync(new Ast.MaintenanceEdit(id, Ast.MaintenanceKind.Ppm, _today.AddMonths(-4), id == aer ? "Olympus NZ (demo)" : "Belimed NZ (demo)", true, null, "Six-monthly service"), Actor, ct)).Errors, "PPM record");
        }
        Ok(await eq.SavePlanAsync(new Ast.PlanEdit(suction, Ast.MaintenanceKind.Ppm, 12, _today.AddDays(-12), "Medela service (demo)"), Actor, ct), "overdue PPM");
        Ok(await eq.SavePlanAsync(new Ast.PlanEdit(mon1, Ast.MaintenanceKind.Calibration, 12, _today.AddDays(18), "GE HealthCare service (demo)"), Actor, ct), "calibration due soon");
        Ok((await eq.RecordAsync(new Ast.MaintenanceEdit(warmer, Ast.MaintenanceKind.TestAndTag, _today.AddDays(-2), "Nelson Electrical Testing (demo)", false, null, "Insulation resistance failed – cracked hose housing"), Actor, ct)).Errors, "failed test and tag");

        // Tray catalogue (names match the theatre records) and two barcoded trays each.
        var sets = new (string Code, string Name, string Spec, string[] Items)[]
        {
            ("GENERAL", "General set", "General surgery", ["2 Mayo scissors", "2 Metzenbaum scissors", "6 Artery forceps (curved)", "2 Toothed dissecting forceps", "2 Non-toothed dissecting forceps", "1 Needle holder", "2 Langenbeck retractors", "1 Scalpel handle no. 3", "1 Gallipot", "1 Kidney dish"]),
            ("LAPCHOLE", "Lap chole set", "General surgery", ["2 Maryland grasper", "2 Atraumatic grasper", "1 Hook diathermy", "1 Clip applier (medium-large)", "1 Suction-irrigation probe", "2 10 mm trocar", "2 5 mm trocar", "1 Veress needle"]),
            ("ANORECTAL", "Anorectal set", "Colorectal", ["1 Eisenhammer retractor", "1 Proctoscope", "2 Allis forceps", "1 Probe set", "1 Needle holder"]),
            ("HAND", "Hand set", "Orthopaedics (hand)", ["1 Tourniquet set", "2 Skin hooks", "1 Senn retractor pair", "1 Tenotomy scissors", "2 Mosquito forceps", "1 Adson forceps"]),
            ("PLASTICS", "Plastics fine set", "Plastics", ["2 Iris scissors", "2 Adson-Brown forceps", "1 Castroviejo needle holder", "4 Fine skin hooks", "1 Jeweller's forceps"]),
            ("MINOR", "Minor set", "General", ["1 Scissors", "2 Artery forceps", "1 Dissecting forceps", "1 Needle holder", "1 Scalpel handle"]),
        };
        foreach (var t in sets)
        {
            var set = Must((await cssd.SaveTraySetAsync(new Ast.TraySetEdit(null, t.Code, t.Name, t.Spec, 90, true,
                t.Items.Select(i => { var sp = i.IndexOf(' '); return new Ast.TrayItemEdit(i[(sp + 1)..], int.Parse(i[..sp], System.Globalization.CultureInfo.InvariantCulture)); }).ToList()), Actor, ct)).Set, [], "tray " + t.Code);
            Ok((await cssd.AddTraysAsync(set.Id, 2, Actor, ct)).Error, "trays " + t.Code);
        }
        var all = sets.SelectMany(t => new[] { $"{t.Code}-01", $"{t.Code}-02" }).ToList();
        // Wash, pack and sterilise every tray (yesterday), in loads of four.
        var start = now.AddDays(-1).AddHours(-6);
        foreach (var chunk in all.Chunk(6))
        {
            var (wash, we) = await cssd.CreateCycleAsync(new Ast.CycleEdit(washer, Ast.CycleKind.Wash, (4400 + _rng.Next(1, 99)).ToString(System.Globalization.CultureInfo.InvariantCulture), "Instruments Standard", start, start.AddMinutes(55), 93.2m, 300, null, 3050, true,
                Ast.IndicatorResult.NotUsed, Ast.IndicatorResult.NotUsed, false, chunk, null), "Manual", null, "cssd", ct);
            Ok((await cssd.ReleaseAsync(Must(wash, we, "wash load").Id, true, null, "cssd", ct)).Ok ? null : "release wash", "wash release");
            foreach (var b in chunk) Ok(await cssd.PackAsync(b, true, null, "cssd", ct), "pack " + b);
            start = start.AddMinutes(70);
        }
        var sterile = all.Where(b => !b.StartsWith("MINOR-02", StringComparison.Ordinal)).ToList();
        foreach (var chunk in sterile.Chunk(4))
        {
            var (steam, se) = await cssd.CreateCycleAsync(new Ast.CycleEdit(steriliser, Ast.CycleKind.Steam, (8800 + _rng.Next(1, 99)).ToString(System.Globalization.CultureInfo.InvariantCulture), "134 °C 4 min wrapped", start, start.AddMinutes(48), 134.3m, 240, 212, null, true,
                Ast.IndicatorResult.Pass, Ast.IndicatorResult.Pass, chunk[0] == sterile[0], chunk, [new Ast.CycleItemEdit("Steri packs (dressing, suture removal)", 6)]), "Manual", null, "cssd", ct);
            Ok((await cssd.ReleaseAsync(Must(steam, se, "steam load").Id, true, null, "cssd", ct)).Ok ? null : "release steam", "steam release");
            start = start.AddMinutes(60);
        }
        // Today: one load waiting for release (BI incubating).
        var (pending, pe) = await cssd.CreateCycleAsync(new Ast.CycleEdit(steriliser, Ast.CycleKind.Steam, "8899", "134 °C 4 min wrapped", now.AddMinutes(-50), now.AddMinutes(-5), 134.1m, 240, 211, null, true,
            Ast.IndicatorResult.Pass, Ast.IndicatorResult.Pending, false, ["MINOR-02"], [new Ast.CycleItemEdit("Steri packs", 4)]), "Manual", null, "cssd", ct);
        Must(pending, pe, "pending load");

        // Issue trays to recent cases (set-to-patient traceability) and bring most back dirty.
        var recent = _done.Where(d => d.Session.Type == SessionType.Surgical).OrderByDescending(d => d.Session.Date).Take(5).ToList();
        var issueable = new Queue<string>(sterile.Where(b => b.EndsWith("-01", StringComparison.Ordinal)));
        foreach (var (b, _, _) in recent)
        {
            if (issueable.Count == 0) break;
            var code = issueable.Dequeue();
            Ok((await cssd.IssueAsync(code, b.Id, b.PatientId, "nurse2", ct)).Error, "issue " + code);
            if (issueable.Count % 2 == 0) Ok(await cssd.ReturnDirtyAsync(code, "cssd", ct), "return " + code);
        }

        // Endoscopes: reprocessed and ready, one used today, one past storage time, one quarantined after a failed leak test.
        for (var n = 0; n < scopes.Count; n++)
        {
            var sc = scopes[n];
            var t0 = n == 4 ? now.AddDays(-5) : now.AddHours(-20 + n);
            foreach (var (kind, passed, mins) in new[] { (Ast.ScopeEventKind.LeakTest, (bool?)true, 0), (Ast.ScopeEventKind.ManualClean, null, 10), (Ast.ScopeEventKind.AerCycle, true, 40), (Ast.ScopeEventKind.DryingIn, null, 75) })
                Ok((await cssd.RecordScopeEventAsync(new Ast.ScopeEventEdit(sc, kind, passed, null, null, null, kind == Ast.ScopeEventKind.AerCycle ? $"AER cycle {5500 + n}" : null, t0.AddMinutes(mins)), "cssd", ct)).Error, "scope event");
        }
        var endo = _done.Where(d => d.Session.Type == SessionType.Endoscopy).OrderByDescending(d => d.Session.Date).FirstOrDefault();
        if (endo.Booking is not null)
            Ok((await cssd.RecordScopeEventAsync(new Ast.ScopeEventEdit(scopes[0], Ast.ScopeEventKind.Use, null, null, endo.Booking.Id, endo.Booking.PatientId, null, now.AddMinutes(-30)), "nurse", ct)).Error, "scope use");
        Ok((await cssd.RecordScopeEventAsync(new Ast.ScopeEventEdit(scopes[5], Ast.ScopeEventKind.LeakTest, false, null, null, null, "Leak at the bending section rubber", now.AddMinutes(-20)), "cssd", ct)).Error, "scope leak fail");

        // Loan sets.
        var (loan1, l1e) = await cssd.SaveLoanAsync(new Ast.LoanEdit(null, loans, "Mini C-arm hand fixation set (loan)", 42, recent.FirstOrDefault().Booking?.Id), "cssd", ct);
        Ok(await cssd.AdvanceLoanAsync(Must(loan1, l1e, "loan").Id, Ast.LoanStatus.Received, null, null, "cssd", ct), "loan received");
        var (loan2, l2e) = await cssd.SaveLoanAsync(new Ast.LoanEdit(null, loans, "Small fragment set (trial)", 65, null), "cssd", ct);
        Must(loan2, l2e, "loan 2");

        // Stock, an order received and invoiced, a draft reorder, and an implant used on a hernia repair.
        Ast.StockItem Item(string code, string name, string unit, bool implant, int reorder, int qty, decimal cost) =>
            Must(stock.SaveItemAsync(new Ast.StockItemEdit(null, code, name, unit, implant, medSup, code + "-MS", reorder, qty, cost, "310", true), Actor, ct).GetAwaiter().GetResult().Item, [], code);
        var mesh = Item("MESH-1515", "Polypropylene hernia mesh 15 × 15 cm", "each", true, 3, 5, 145m);
        var clips = Item("CLIP-ML", "Ligating clips medium-large (cartridge)", "box", false, 4, 10, 62m);
        var gauze = Item("GAUZE-10", "Gauze swabs 10 × 10 (pk 10)", "pack", false, 50, 200, 1.85m);
        var indicators = Item("BI-STEAM", "Steam biological indicators (box 25)", "box", false, 2, 4, 210m);
        Item("SUT-30V", "Absorbable suture 3/0 (box 12)", "box", false, 6, 12, 96m);
        var (po, poe) = await stock.CreateOrderAsync(medSup, [new Ast.OrderLineEdit(mesh.Id, 5, null), new Ast.OrderLineEdit(clips.Id, 10, null), new Ast.OrderLineEdit(gauze.Id, 200, null), new Ast.OrderLineEdit(indicators.Id, 4, null)], "Monthly order", "cssd", ct);
        var order = Must(po, poe, "order");
        Ok(await stock.SetOrderStatusAsync(order.Id, Ast.PoStatus.Approved, "finance", ct), "approve order");
        Ok(await stock.SetOrderStatusAsync(order.Id, Ast.PoStatus.Sent, "cssd", ct), "send order");
        var lines = (await stock.OrdersAsync(true, ct)).Single(o => o.Id == order.Id).Items;
        Ok(await stock.ReceiveAsync(order.Id, lines.Select(l => new Ast.ReceiptLine(l.Id, l.Quantity, "L" + _rng.Next(10000, 99999), l.Code == "GAUZE-10" ? null : _today.AddMonths(_rng.Next(8, 30)))).ToList(), "cssd", ct), "receive order");
        var total = lines.Sum(l => l.Quantity * l.UnitCost);
        Ok((await stock.AddInvoiceAsync(order.Id, new Ast.InvoiceRequest("MSNZ-" + _rng.Next(20000, 29999), _today.AddDays(-1), _today.AddDays(19), total), "finance", ct)).Error, "invoice");
        Ok(await stock.UseAsync(new Ast.UseRequest(gauze.Id, 180, null, null, null, "Theatre use this month"), "nurse2", ct), "gauze use");
        Ok(await stock.UseAsync(new Ast.UseRequest(clips.Id, 7, null, null, null, "Theatre use this month"), "nurse2", ct), "clip use");
        var hernia = _done.FirstOrDefault(d => d.Booking.Procedure.Contains("hernia", StringComparison.OrdinalIgnoreCase));
        if (hernia.Booking is not null)
        {
            var lot = (await stock.LotsAsync(mesh.Id, ct))[0];
            Ok((await stock.RecordImplantAsync(new Ast.ImplantRequest(mesh.Id, lot.Id, null, "SN-" + _rng.Next(100000, 999999), "Demo Medical", "Left inguinal", hernia.Booking.PatientId, hernia.Booking.Id,
                hernia.Session.Date), "nurse2", ct)).Error, "implant");
        }
        await stock.DraftReordersAsync("cssd", ct);
        Ok((await cssd.RaiseAsync(new Ast.NcEdit(null, "Instrument damage", "Hand set: one Senn retractor found bent at inspection.", null, null, null, "Removed from tray, replacement ordered.", null, null, null), "cssd", ct)).Errors, "nonconformance");
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
            // A short last window (e.g. a weekend) can legitimately create nothing – only fail if no lists are made at all (0.13.0 fix: the
            // demonstration loader failed on some days of the week).
            made += result.Created;
        }
        if (made == 0) throw new InvalidOperationException("Demo data – theatre: no lists were created.");
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
        "reid" => "Dr Hamish Reid",
        "natarajan" => "Dr Priya Natarajan",
        "whitaker" => "Dr Tom Whitaker",
        "tipene" => "Dr Mereana Tipene",
        "lindqvist" => "Dr Sarah Lindqvist",
        "oconnor" => "Dr Liam O'Connor",
        "kowalski" => "Dr Ben Kowalski",
        "grant" => "Dr Fiona Grant",
        _ => "Dr Raj Mehta"
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
        var trays = new Dictionary<string, string>
        {
            ["reid"] = "General set; Lap chole set",
            ["natarajan"] = "Anorectal set; General set",
            ["whitaker"] = "Hand set",
            ["tipene"] = "Plastics fine set",
            ["oconnor"] = "Minor set",
            ["lindqvist"] = ""
        };

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
        var p when p.StartsWith("Colonoscopy", StringComparison.Ordinal) => "COLON",
        "Gastroscopy" => "GASTRO",
        "Gastroscopy and colonoscopy" => "GASTROCOL",
        "Carpal tunnel release" => "CTR",
        "Inguinal hernia repair" => "HERNIA",
        "Laparoscopic cholecystectomy" => "LAPCHOLE",
        "Excision of basal cell carcinoma with flap repair" => "BCC",
        _ => "FAC-TH30"
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
            var scope = s.Type == SessionType.Endoscopy ? (b.Procedure.StartsWith("Gastro", StringComparison.Ordinal) ? "GIF-H190-0" : "CF-HQ190L-") + (1 + i % 3) : null;

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
            ["height_cm"] = (155 + i % 30).ToString(),
            ["weight_kg"] = (58 + i * 3 % 40).ToString(),
            ["support_person"] = "Partner – 021 555 0199",
            ["lives_alone"] = i % 6 == 0 ? "Yes" : "No",
            ["heart"] = "No",
            ["pacemaker"] = "No",
            ["high_bp"] = i % 3 == 0 ? "Yes" : "No",
            ["lung"] = "No",
            ["sleep_apnoea"] = "No",
            ["diabetes"] = i % 7 == 0 ? "Yes – tablets" : "No",
            ["kidney_liver"] = "No",
            ["stroke"] = "No",
            ["bleeding"] = "No",
            ["infection"] = "No",
            ["pregnant"] = "Not applicable",
            ["blood_thinners"] = i % 5 == 0 ? "Yes" : "No",
            ["herbal"] = "No",
            ["allergies"] = i % 4 == 0 ? "Yes" : "No",
            ["latex"] = "No",
            ["anaesthetic_problems"] = "No",
            ["family_mh"] = "No",
            ["teeth"] = "No",
            ["neck"] = "No",
            ["smoking"] = i % 8 == 0 ? "Smoke" : "Never",
            ["alcohol"] = "1–10",
            ["mobility"] = "Independently",
            ["interpreter"] = "No",
            ["confirm"] = "Yes",
            ["medications"] = i % 3 == 0 ? "Cilazapril 2.5 mg daily" : "None",
        };
        if (a["blood_thinners"] == "Yes") a["blood_thinners_details"] = "Aspirin 100 mg daily";
        if (a["allergies"] == "Yes") a["allergies_details"] = "Penicillin – rash";
        if (i == 9) { a["pacemaker"] = "Yes"; a["heart"] = "Yes"; a["heart_details"] = "Pacemaker 2023 for heart block"; }
        // PAQ-2 triage questions (0.22.0)
        a["snoring_loud"] = i % 4 == 1 ? "Yes" : "No";
        a["daytime_tired"] = i % 5 == 1 ? "Yes" : "No";
        a["observed_apnoea"] = i == 6 ? "Yes" : "No";
        a["stairs"] = i == 11 ? "No" : "Yes";
        a["recent_cardiac_stroke"] = "No";
        a["home_oxygen"] = "No";
        a["glp1"] = i == 8 ? "Yes" : "No";
        if (i == 8) { a["glp1_details"] = "Ozempic 1 mg on Sundays"; a["weight_kg"] = "128"; a["height_cm"] = "172"; }
        if (i == 6) { a["neck_cm"] = "44"; a["sleep_apnoea"] = "Yes"; a["cpap_use"] = "No"; }
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
            var rows = await svc.ListAsync("to-triage", patient.Id, ct);
            var row = rows.Count > 0 ? rows[0] : null;
            if (row is null) continue;
            using (At(s.Date.AddDays(-6) < _today ? s.Date.AddDays(-6) : _today, 11))
                Ok(await svc.TriageAsync(row.Id, Enum.Parse<TriageCategory>(row.Suggested ?? "Green"), row.Suggested == "Green" ? "Fit for day surgery" : "Discussed with anaesthetist – proceed",
                    row.Suggested == "Green" ? null : "Phoned patient to confirm details", addAllergyAlert: Answers(i)["allergies"] == "Yes", "nurse", ct), "triage questionnaire");
        }
    }

    /// <summary>Triage (0.22.0): the example rules approved for the demo only, triage of the upcoming bookings, and sign-offs by a nurse and an anaesthetist.</summary>
    private async Task TriageAsync(CancellationToken ct)
    {
        var rules = S<BrightPAS.Modules.Triage.TriageRulesService>();
        var rs = await rules.SeedExampleAsync("fgrant", ct);
        if (rs is null) return;
        Ok(await rules.ApproveAsync(rs.Id, "DEMONSTRATION ONLY – not a clinical approval. TDS's anaesthetists must review the rules before real use.", "fgrant", "Anaesthetist", ct), "approve demo rules");
        var svc = S<BrightPAS.Modules.Triage.TriageService>();
        await svc.ScanAsync(ct);
        var open = await svc.WorklistAsync("open", ct);
        var n = 0;
        foreach (var w in open.Where(w => w.Suggested <= BrightPAS.Modules.Triage.TriageOutcome.SuitableWithPrecautions && w.ProcedureDate <= _today.AddDays(5)).Take(6))
        {
            Ok(await svc.SignOffAsync(w.Id, new(w.Suggested, null, null, "Questionnaire reviewed; phoned patient to confirm escort.", null), false, "nurse", "Nurse", ct), "triage sign-off");
            n++;
        }
        var review = open.FirstOrDefault(w => w.Suggested == BrightPAS.Modules.Triage.TriageOutcome.AnaesthetistReview);
        if (review is not null)
            Ok(await svc.SignOffAsync(review.Id, new(BrightPAS.Modules.Triage.TriageOutcome.SuitableWithPrecautions, 3, "First on the list; device check on admission", "Reviewed in pre-assessment clinic.", "Reviewed by anaesthetist – suitable with precautions"), true, "fgrant", "Anaesthetist", ct), "anaesthetist sign-off");
    }

    private async Task FollowUpAsync(CancellationToken ct)
    {
        var fu = S<BrightPAS.Modules.FollowUp.FollowUpService>();
        await fu.SeedExamplesAsync("quality", ct);
        var st = await fu.SettingsAsync(ct);
        Ok(await fu.SaveSettingsAsync(new BrightPAS.Modules.FollowUp.FollowUpSettings { PublicUrl = "https://localhost:5001", DutyMobile = "021 555 0100", SendCheckInLinks = true, LinkDays = st.LinkDays, GpLetter = true }, "quality", ct), "follow-up settings");
        await fu.ScanAsync(ct);
        // Record a realistic spread of calls: most fine, one concern escalated to the GP, one unanswered, one suspected wound infection.
        var due = (await fu.WorklistAsync("overdue", ct: ct)).Concat(await fu.WorklistAsync("today", ct: ct)).Where(c => c.Method == "Phone").OrderBy(c => c.DueOn).ToList();
        var fine = new Dictionary<string, string> { ["pain"] = "2", ["pain_relief_working"] = "yes", ["nausea"] = "No", ["bleeding"] = "None", ["wound_red"] = "no", ["wound_swollen"] = "no", ["wound_discharge"] = "Nothing", ["wound_open"] = "no", ["fever"] = "no", ["eating"] = "yes", ["bowels_bladder"] = "yes", ["mobility"] = "yes", ["satisfaction"] = "9" };
        for (var k = 0; k < due.Count; k++)
        {
            var c = due[k];
            if (k == 1)
            {
                var r = await fu.RecordAsync(c.Id, new(BrightPAS.Modules.FollowUp.ContactStatus.Done, "Phone", new Dictionary<string, string>(fine) { ["pain"] = "8", ["pain_relief_working"] = "no" }, BrightPAS.Modules.FollowUp.Escalation.GpReview, "Advised to see GP today for stronger pain relief; GP practice phoned.", null), "nurse", ct);
                Ok(r.Errors, "follow-up escalation");
            }
            else if (k == 2) Ok((await fu.RecordAsync(c.Id, new(BrightPAS.Modules.FollowUp.ContactStatus.NoAnswer, "Phone", null, BrightPAS.Modules.FollowUp.Escalation.None, null, "Voicemail left."), "nurse", ct)).Errors, "follow-up no answer");
            else if (k == 3)
            {
                var r = await fu.RecordAsync(c.Id, new(BrightPAS.Modules.FollowUp.ContactStatus.Done, "Phone", new Dictionary<string, string>(fine) { ["wound_red"] = "yes", ["wound_swollen"] = "yes", ["wound_discharge"] = "Cloudy, yellow/green or smelly", ["fever"] = "yes" },
                    BrightPAS.Modules.FollowUp.Escalation.ReturnToTds, "Wound review at TDS tomorrow 9:00 with the surgeon; swab to be taken.", null), "nurse", ct);
                Ok(r.Errors, "follow-up wound");
                var (ssi, errors) = await S<BrightPAS.Modules.FollowUp.SsiService>().OpenAsync(new(c.BookingId, c.Id, _today.AddDays(-1) < c.OperationDate ? c.OperationDate : _today.AddDays(-1), r.Signs, BrightPAS.Modules.FollowUp.SsiSource.Patient, false, false, false, null), "nurse", ct);
                Ok(errors, "open SSI");
            }
            else if (k < 10) Ok((await fu.RecordAsync(c.Id, new(BrightPAS.Modules.FollowUp.ContactStatus.Done, "Phone", fine, BrightPAS.Modules.FollowUp.Escalation.None, null, null), "nurse", ct)).Errors, "follow-up call");
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
            var at = NzTime.FromLocal(_today.AddDays(-(n % 9) - 1).ToDateTime(new TimeOnly(10 + n % 6, 15)));
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
                NzTime.FromLocal(s.Date.ToDateTime(new TimeOnly(7, 30))), false, _clinName(KeyOf(s.SurgeonId))), null);
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

    // ------------------------------------------------------------------ Devices & SaferSleep (0.17.0)
    private async Task DevicesAsync(CancellationToken ct)
    {
        var msg = S<Msg.MessagingService>();
        Msg.Interface I((Msg.Interface? Interface, IReadOnlyList<string> Errors) r, string what) => Must(r.Interface, r.Errors, what);
        I(await msg.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "ge-monitors-in", "GE CARESCAPE Gateway – monitor results (MLLP)", Msg.InterfaceDirection.Inbound, Msg.Transport.MllpListen, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.DeviceObservations, null, 12580, null, null, null, null, [new Msg.ClientEdit("192.168.1.170", null, "GE CARESCAPE Gateway"), new Msg.ClientEdit("127.0.0.1", null, "This server (testing / test robot)")],
            true, 5, null, "Vital signs from the B450 and B1x5 monitors (HL7 ORU, IEEE 11073 codes)."), "integration", ct), "monitor interface");
        I(await msg.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "safersleep-in", "SaferSleep anaesthetic records (MLLP)", Msg.InterfaceDirection.Inbound, Msg.Transport.MllpListen, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.AnaesthesiaRecord, null, 12581, null, null, null, null, [new Msg.ClientEdit("192.168.1.175", null, "SaferSleep server"), new Msg.ClientEdit("127.0.0.1", null, "This server (testing / test robot)")],
            true, 3, null, "Completed anaesthetic record (drugs, events, vital signs, PDF)."), "integration", ct), "SaferSleep in");
        I(await msg.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "safersleep-out", "SaferSleep – patient and booking (ADT)", Msg.InterfaceDirection.Outbound, Msg.Transport.MllpSend, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.None, "127.0.0.1", 12582, null, null, null, null, null, true, 5, null, "Demo: points at this server, where the test robot's virtual SaferSleep listens. Change to the SaferSleep server."), "integration", ct), "SaferSleep out");

        var dev = S<Dev.DeviceService>();
        Ok(await dev.SaveSettingsAsync(new Dev.DeviceSettingsEdit(5, 5, 15, 30, 120, 24, 48, true, true, "safersleep-out", "SAFERSLEEP", "TDS", Dev.SendAdtOn.Admission, true), "integration", ct), "device settings");
        var mons = await S<Ast.AssetsDbContext>().Equipment.AsNoTracking().Where(e => e.Category == Ast.EquipmentCategory.Monitor).OrderBy(e => e.AssetNumber).ToListAsync(ct);
        var codes = new[] { "THEATRE1/OR1", "PACU/BAY1", "PACU/BAY2" };
        var monitorIds = new List<Guid>();
        for (var i = 0; i < mons.Count && i < codes.Length; i++)
        {
            var (m, err) = await dev.SaveMonitorAsync(null, mons[i].Id, codes[i], true, "integration", ct);
            Ok(err, "monitor " + codes[i]);
            monitorIds.Add(m!.Id);
        }

        // Anaesthetic records from SaferSleep for recent finished cases, and a recovery monitor trend for two of them.
        var emr = S<Emr.EmrDbContext>();
        var done = await emr.Episodes.AsNoTracking().Where(e => e.Stage == Emr.EpisodeStage.Discharged && e.SedationOrAnaesthesia).ToListAsync(ct);
        var intra = await emr.Intraop.AsNoTracking().Where(x => x.AnaestheticStartAt != null).ToDictionaryAsync(x => x.EpisodeId, x => x.AnaestheticStartAt!.Value, ct);
        var pdb = S<PatientsDbContext>();
        var ana = S<Dev.AnaesthesiaService>();
        var ddb = S<Dev.DevicesDbContext>();
        var n = 0;
        foreach (var e in done.Where(e => intra.ContainsKey(e.Id)).OrderBy(e => e.AdmittedAt))
        {
            var p = await pdb.Patients.AsNoTracking().SingleAsync(x => x.Id == e.PatientId, ct);
            if (p.Nhi is null) continue;
            var t0 = intra[e.Id];
            n++;
            var hl7 = Dev.SimplePdf.SaferSleepOru($"SSDEMO{n:D4}", $"SS-{t0:yyMMdd}-{n:D3}", e.Id.ToString("N"), p.Nhi, p.FamilyName, p.GivenNames, t0, false,
                [(1, "Midazolam", "2", "mg", "IV"), (2, "Fentanyl", "100", "microgram", "IV"), (3, "Propofol", "200", "mg", "IV"), (8, "Cefazolin", "2", "g", "IV"), (30, "Ondansetron", "4", "mg", "IV"), (45, "Paracetamol", "1", "g", "IV")],
                "6 mg/kg/h");
            var r = await ana.IngestAsync(hl7, null, "safersleep-in:demo", ct);
            if (!r.Accepted || !r.Filed) throw new InvalidOperationException($"Demo data – SaferSleep record: {r.Error ?? "not filed"}");
            if (n <= 2 && monitorIds.Count > 1)
            {
                var start = t0.AddMinutes(62);
                var a = new Dev.Association { MonitorId = monitorIds[1], EpisodeId = e.Id, PatientId = e.PatientId, StartedAt = start, StartedBy = "Ana Faleolo RN", EndedAt = start.AddMinutes(60), EndedBy = "Ana Faleolo RN" };
                ddb.Associations.Add(a);
                for (var m = 0; m <= 60; m++)
                {
                    var at = start.AddMinutes(m);
                    ddb.Readings.Add(new Dev.Reading { MonitorId = a.MonitorId, AssociationId = a.Id, At = at, Code = "HR", Value = 78 - m / 6 + _rng.Next(-3, 4), Unit = "/min" });
                    ddb.Readings.Add(new Dev.Reading { MonitorId = a.MonitorId, AssociationId = a.Id, At = at, Code = "SpO2", Value = Math.Min(100, 95 + m / 20 + _rng.Next(0, 2)), Unit = "%" });
                    ddb.Readings.Add(new Dev.Reading { MonitorId = a.MonitorId, AssociationId = a.Id, At = at, Code = "RR", Value = 14 + _rng.Next(-2, 3), Unit = "/min" });
                    if (m % 5 == 0)
                    {
                        ddb.Readings.Add(new Dev.Reading { MonitorId = a.MonitorId, AssociationId = a.Id, At = at, Code = "NIBP-S", Value = 128 - m / 4 + _rng.Next(-5, 6), Unit = "mmHg" });
                        ddb.Readings.Add(new Dev.Reading { MonitorId = a.MonitorId, AssociationId = a.Id, At = at, Code = "NIBP-D", Value = 78 + _rng.Next(-4, 5), Unit = "mmHg" });
                    }
                }
                ddb.Captures.Add(new Dev.Capture
                {
                    AssociationId = a.Id,
                    EpisodeId = e.Id,
                    MonitorId = a.MonitorId,
                    At = start.AddMinutes(20),
                    HeartRate = 44,
                    SpO2 = 72,
                    SystolicBp = 126,
                    DiastolicBp = 80,
                    RespiratoryRate = 14,
                    Status = Dev.CaptureStatus.Rejected,
                    DecidedBy = "Ana Faleolo RN",
                    DecidedAt = start.AddMinutes(21),
                    RejectReason = "Artefact – SpO2 probe slipped off finger"
                });
                await ddb.SaveChangesAsync(ct);
            }
            if (n >= 4) break;
        }
    }

    // ------------------------------------------------------------------ Quality management (0.19.0)
    private async Task QualityAsync(CancellationToken ct)
    {
        var q = S<Qm.QualityService>();
        var ind = S<Qm.IndicatorService>();
        Ok(await q.SaveSettingsAsync("General Manager; Clinical Governance chair; Director of Nursing", null, "quality", ct), "quality settings");
        await ind.SeedStarterAsync(ct);
        var pdb = S<PatientsDbContext>();
        var pts = await pdb.Patients.AsNoTracking().Where(p => p.MergedIntoId == null).OrderBy(p => p.Mrn).Take(6).Select(p => p.Id).ToListAsync(ct);
        var mon = await S<Ast.AssetsDbContext>().Equipment.AsNoTracking().Where(e => e.Category == Ast.EquipmentCategory.Monitor).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
        var now = _clock.GetUtcNow();
        async Task<Qm.Incident> Report(int daysAgo, string user, Qm.IncidentCategory c, Qm.Harm h, string where, string what, Guid? patient, bool anon = false, Guid? eq = null)
        {
            using (DemoClock.At(now.AddDays(-daysAgo)))
            {
                var (i, e) = await q.ReportAsync(new Qm.IncidentReport(now.AddDays(-daysAgo).AddHours(-2), where, c, what, "Patient checked and made safe; team informed.", patient, null, eq, null, h, anon), user, ct);
                return Must(i, e, "incident");
            }
        }
        // A closed fall with learning, an open SAC 2 medication event awaiting disclosure, a device near miss, an untriaged anonymous report, a staff sharps injury.
        var fall = await Report(40, "nurse3", Qm.IncidentCategory.Fall, Qm.Harm.Minor, "Recovery (stage 2)", "Patient stood up to go to the bathroom 30 minutes after sedation without calling for help and slid to the floor; small graze to the left elbow.", pts.ElementAtOrDefault(0));
        Ok(await q.TriageAsync(fall.Id, new Qm.IncidentTriage(4, Qm.Harm.Minor, false, "Morgan Hayes", []), "quality", ct), "triage fall");
        Ok(await q.FindingsAsync(fall.Id, new Qm.IncidentFindings("Falls risk not re-assessed after sedation; call bell out of reach.", "Busy list; bell clipped to the wall side of the chair.", "After sedation, re-check falls risk and put the call bell in the patient's hand before leaving the bedside."), "quality", ct), "findings fall");
        Ok(await q.DiscloseAsync(fall.Id, new Qm.DisclosureEdit(now.AddDays(-40), "Patient and husband", "In person", "Apologised; explained what happened and the graze care."), "nurse3", ct), "disclosure fall");
        var (fa, fe) = await q.NewActionAsync(new Qm.ActionEdit(Qm.ActionSource.Incident, fall.Id, null, "Add post-sedation falls re-check to the recovery discharge checklist", null, "Clinical nurse manager", _today.AddDays(1)), "quality", ct);
        var fallAction = Must(fa, fe, "fall action");
        Ok(await q.ActionDoneAsync(fallAction.Id, "Checklist v3 issued 12 Oct; staff briefed at handover (sign-in sheet in the quality folder).", "roster", ct), "action done");
        Ok(await q.ActionVerifyAsync(fallAction.Id, "quality", ct), "action verified");
        Ok(await q.CloseIncidentAsync(fall.Id, "quality", ct), "close fall");

        var med = await Report(6, "nurse2", Qm.IncidentCategory.Medication, Qm.Harm.Moderate, "Theatre 1", "Second dose of prophylactic antibiotic given in recovery – the theatre dose had not been charted. Patient developed a rash, treated with antihistamine.", pts.ElementAtOrDefault(1));
        Ok(await q.TriageAsync(med.Id, new Qm.IncidentTriage(2, Qm.Harm.Moderate, false, "Morgan Hayes", [Qm.Agency.Hqsc]), "quality", ct), "triage med");
        Ok((await q.NewActionAsync(new Qm.ActionEdit(Qm.ActionSource.Incident, med.Id, null, "Review antibiotic charting at Time Out and Sign Out with the anaesthetists", null, "Dr Ben Kowalski", _today.AddDays(21)), "quality", ct)).Errors, "med action");

        await Report(3, "cssd", Qm.IncidentCategory.EquipmentDevice, Qm.Harm.None, "CSSD", "Steriliser printout showed a temperature dip in the hold phase; load quarantined before release. Near miss.", null, eq: mon);
        await Report(1, "reception", Qm.IncidentCategory.InformationPrivacy, Qm.Harm.None, "Reception", "Appointment letter for one patient was put in the envelope of another; returned unopened by the recipient.", pts.ElementAtOrDefault(2), anon: true);
        var sharps = await Report(12, "nurse", Qm.IncidentCategory.StaffInjury, Qm.Harm.Minor, "Theatre 1", "Needlestick from a used suture needle during counting; blood-borne virus protocol followed.", null);
        Ok(await q.TriageAsync(sharps.Id, new Qm.IncidentTriage(3, Qm.Harm.Minor, false, "Morgan Hayes", [Qm.Agency.WorkSafe]), "quality", ct), "triage sharps");

        // Complaints: one acknowledged and decided on time and closed, one waiting for acknowledgement.
        var (c1, e1) = await q.NewComplaintAsync(new Qm.ComplaintEdit(_today.AddDays(-30), "Email", "Mr R. Thompson", "r.thompson@example.nz", pts.ElementAtOrDefault(3), "Unhappy that his procedure was cancelled on the morning with no explanation.", "Waiting time", "Morgan Hayes", null), "quality", ct);
        var comp = Must(c1, e1, "complaint");
        Ok(await q.ComplaintStepAsync(comp.Id, new Qm.ComplaintStep("acknowledge", _today.AddDays(-28), null, null, null, null), "quality", ct), "ack");
        Ok(await q.ComplaintStepAsync(comp.Id, new Qm.ComplaintStep("respond", _today.AddDays(-20), "Apologised; explained the emergency that delayed the list and rebooked at the patient's convenience. Advised of the right to contact the HDC and the Advocacy Service.", Qm.ComplaintOutcome.PartlyUpheld, false, false), "quality", ct), "respond");
        Ok((await q.NewActionAsync(new Qm.ActionEdit(Qm.ActionSource.Complaint, comp.Id, null, "Phone every patient whose list is cancelled on the day before 9 am", null, "Theatre manager", _today.AddDays(30)), "quality", ct)).Errors, "complaint action");
        Ok((await q.NewComplaintAsync(new Qm.ComplaintEdit(_today.AddDays(-1), "Letter", "Mrs S. Patel", null, pts.ElementAtOrDefault(4), "Invoice was higher than the estimate she was given.", "Billing / cost", "Morgan Hayes", null), "quality", ct)).Errors, "complaint 2");
        Ok((await q.ComplimentAsync(new Qm.ComplimentEdit(_today.AddDays(-5), "Patient (card)", "Recovery team", "Thank you all for making me feel so calm and cared for."), "quality", ct)).Errors, "compliment");

        // Risks and hazards.
        Qm.RiskEdit R(bool hz, string t, Qm.RiskCategory c, string owner, int l, int k, string controls, int? rl, int? rk, string? loc = null) =>
            new(hz, t, c, t, owner, l, k, controls, rl, rk, loc, null, _today.AddMonths(3), Qm.RiskStatus.Open);
        foreach (var r in new[]
        {
            R(false, "Cyber attack or ransomware takes the PAS offline", Qm.RiskCategory.InformationTechnology, "General Manager", 3, 5, "Immutable backups, MFA, EDR, offline downtime forms; import from file for devices.", 2, 4),
            R(false, "Single anaesthetist cover – list cancellations if unwell", Qm.RiskCategory.Operational, "Clinical director", 3, 3, "Locum agreement; roster review monthly.", 2, 3),
            R(false, "Emergency transfer to Nelson Hospital delayed", Qm.RiskCategory.Clinical, "Director of Nursing", 2, 5, "Transfer pack, St John agreement, annual simulation.", 1, 5),
            R(false, "Loss of key CSSD technician knowledge", Qm.RiskCategory.People, "General Manager", 3, 3, "Cross-training; written procedures in the policy register.", 2, 2),
            R(true, "Manual handling – patient transfers from theatre table", Qm.RiskCategory.HealthAndSafety, "Director of Nursing", 3, 3, "Slide sheets, minimum two staff, annual training.", 2, 2, "Theatre 1"),
            R(true, "Hot surfaces and steam – steriliser door", Qm.RiskCategory.HealthAndSafety, "CSSD lead", 2, 3, "Heat-resistant gloves; signage; door interlock checked at service.", 1, 2, "CSSD"),
        }) Ok((await q.SaveRiskAsync(null, r, "quality", ct)).Errors, "risk " + r.Title);

        // Policy register (documents live in SharePoint) with some acknowledgements.
        var docs = new List<Qm.PolicyDocument>();
        foreach (var (code, title, ver, owner, ack, months) in new[]
        {
            ("TDS-5.9", "Emergency blood collection procedure", "2.1", "Director of Nursing", true, 10),
            ("TDS-3.2", "Falls prevention and management", "4.0", "Clinical nurse manager", true, 4),
            ("TDS-4.1", "Incident management and open disclosure", "3.0", "Quality manager", true, 14),
            ("TDS-1.6", "Complaints management (Right 10)", "2.0", "Quality manager", false, -1),
            ("TDS-6.4", "Infection prevention – surgical site infection surveillance", "1.3", "IPC nurse", true, 8),
        })
        {
            var (d, de) = await q.SavePolicyAsync(null, new Qm.PolicyEdit(code, title, ver, owner, $"https://tasmandaysurgery.sharepoint.com/sites/policies/{code}.pdf", _today.AddMonths(months), ack, true), "quality", ct);
            docs.Add(Must(d, de, "policy " + code));
        }
        foreach (var u in new[] { "nurse", "nurse2", "nurse3", "roster" }) Ok(await q.AcknowledgeAsync(docs[0].Id, u, ct), "ack policy");
        Ok(await q.AcknowledgeAsync(docs[1].Id, "nurse", ct), "ack policy 2");

        // Patient satisfaction (entered monthly until surveys are built in).
        var psat = (await ind.ListAsync(false, ct)).Single(i => i.Code == "PSAT");
        var first = new DateOnly(_today.Year, _today.Month, 1);
        for (var m = 6; m >= 1; m--) Ok(await ind.EnterValueAsync(psat.Id, first.AddMonths(-m), 92 + _rng.Next(0, 8), 100, "quality", ct), "psat");
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
            Msg.InboundHandler.None, null, 11114, null, null, null, "BRIGHTPAS", [new Msg.ClientEdit("192.168.1.150", "ENDOTOWER1", "Procedure room 1 tower"), new Msg.ClientEdit("127.0.0.1", "TESTSCU", "Testing from this server")],
            true, 3, null, "Image headers from the endoscopy stack."), "integration", ct), "DICOM interface");
        var api = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "lab-api", "Partner results (REST)", Msg.InterfaceDirection.Inbound, Msg.Transport.HttpReceive, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.None, null, null, null, null, "demo-" + Guid.NewGuid().ToString("N"), null, null, true, 3, null, "Posted to /api/integration/in/lab-api with the interface key."), "integration", ct), "REST interface");
        var pacs = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "pacs-out", "Clinical record feed (HL7 folder)", Msg.InterfaceDirection.Outbound, Msg.Transport.FolderWrite, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.None, null, null, Dir("out-hl7"), null, null, null, null, true, 3, null, "Demo: files are written to the demo-interfaces folder."), "integration", ct), "HL7 folder interface");
        var archive = I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "results-json", "Results archive (JSON folder)", Msg.InterfaceDirection.Outbound, Msg.Transport.FolderWrite, Msg.MessageFormat.Json,
            Msg.InboundHandler.None, null, null, Dir("out-json"), null, null, null, null, true, 3, null, null), "integration", ct), "JSON folder interface");
        I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "scan-in", "Scanned results drop folder", Msg.InterfaceDirection.Inbound, Msg.Transport.FolderWatch, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.None, null, null, Dir("in"), null, null, null, null, true, 3, null, "Drop .hl7 files here to test."), "integration", ct), "folder watch interface");
        // Offline fallback (0.18.0): if the laboratory's MLLP link is down, it (or IT) saves the results to this share and BrightPAS files them from there.
        I(await svc.SaveInterfaceAsync(new Msg.InterfaceEdit(null, "lab-share", "Lab results from a network share (fallback)", Msg.InterfaceDirection.Inbound, Msg.Transport.FolderWatch, Msg.MessageFormat.Hl7v2,
            Msg.InboundHandler.LabResults, null, null, Dir("lab-share"), null, null, null, null, true, 3, null, "Used when the MLLP link is down: HL7 files saved here are filed as lab results (many messages per file allowed)."), "integration", ct), "lab share interface");
        await S<SettingsService>().SetAsync(BrightPAS.Web.Api.EndoscopyEndpoints.ImportFolderKey, Dir("endoscopy-share"), "integration", ct);
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
            "in" => "Receive",
            "isOru" => "Result message?",
            "map" => "To JSON",
            "json" => "Archive",
            "hl7" => "Clinical feed",
            "img" => "To HL7",
            _ => k
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
        foreach (var (b, s, _) in _done.OrderByDescending(d => d.Session.Date).Take(12))
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
                        : Oru($"PL{n:D4}", p.Nhi, p.FamilyName, p.GivenNames.Split(' ')[0], p.DateOfBirth, sex[0], NzTime.FromLocal(s.Date.ToDateTime(new TimeOnly(15, 0))), "Full blood count", "CBC",
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

    // ------------------------------------------------------------------ Coding & statutory reporting (0.20.0)
    /// <summary>Procedure estimates (0.21.0): TDS's own price list and stock list (the spreadsheets the module imports), surgeons linked to the demo clinicians, and a few estimates.</summary>
    private async Task EstimatesAsync(CancellationToken ct)
    {
        var imp = S<Est.EstimateImport>();
        var asm = typeof(DemoDataSeeder).Assembly;
        Stream Res(string name) => asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith("Demo.Estimates." + name, StringComparison.Ordinal)))!;
        await using (var f = Res("pricing.xlsx"))
        {
            var pv = await imp.PreviewPricingAsync(f, "Estimates - Facility & Anaesthetist.xlsx", ct);
            Ok(pv.Errors, "estimates pricing");
            await imp.ApplyPricingAsync(pv, true, true, false, "finance", ct);
        }
        await using (var f = Res("stock.xlsx"))
        {
            var pv = await imp.PreviewStockAsync(f, "TDS Suppliers and vendors stock prices.xlsx", ct);
            Ok(pv.Errors, "estimates stock");
            await imp.ApplyStockAsync(pv, "finance", ct);
        }
        var cat = S<Est.EstimateCatalogue>();
        var specs = (await cat.SpecialtiesAsync(ct)).ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);
        Guid[] Specs(params string[] names) => [.. names.Select(n => specs.TryGetValue(n, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)];
        foreach (var (name, notes) in new[] { ("Infusion", "Medication is charged in addition. Payment for the cost of medication must be made at least 10 days prior to your infusion so it can be ordered in time – delay in payment could postpone treatment. Please use ‘Infusion’ as the reference and your surname as the code."), ("Dental", "Teeth: ") })
            if (specs.TryGetValue(name, out var id)) Ok((await cat.SaveSpecialtyAsync(id, new Est.SpecialtyEdit(name, notes, true), "finance", ct)).Error, "specialty " + name);
        var surgeons = new Dictionary<ClinicianType, Guid>();
        var first = true;
        foreach (var c in await S<PasDbContext>().Clinicians.AsNoTracking().Where(c => c.IsActive && (c.Type == ClinicianType.Surgeon || c.Type == ClinicianType.Endoscopist)).OrderBy(c => c.Name).ToListAsync(ct))
        {
            var sp = c.Type == ClinicianType.Endoscopist ? Specs("Endoscopy") : first ? Specs("General Surgery", "Gynaecology", "Skin", "Veins") : Specs("General Surgery", "Skin");
            first = false;
            var (sid, err) = await cat.SaveSurgeonAsync(null, new Est.SurgeonEdit(c.Name, c.Id, true, sp), "finance", ct);
            Ok(err, "estimates surgeon");
            surgeons.TryAdd(c.Type, sid!.Value);
        }
        var svc = S<Est.EstimateService>();
        await svc.SeedTemplatesOnceAsync("finance", ct);
        var procs = (await cat.ProceduresAsync(ct)).ToDictionary(p => p.Code, StringComparer.Ordinal);
        var s = await svc.SettingsAsync(ct);
        s.IncisiveFolder = Path.Combine(Path.GetTempPath(), "BrightPAS-demo-incisive");
        Ok(await cat.SaveSettingsAsync(s, "finance", ct), "estimate settings");
        var people = _patients.Where(p => p.MergedIntoId is null).Take(4).ToList();
        if (people.Count < 3 || !procs.TryGetValue("GYN-001", out var gyn) || !procs.TryGetValue("GEN-011", out var hernia)) return;

        // 1. Issued self-funding colonoscopy from a template (biopsies required, polypectomy if required).
        var tpl = (await svc.TemplatesAsync(true, null, ct)).First(t => t.Name.StartsWith("Colonoscopy + biopsies", StringComparison.Ordinal));
        var f1 = await svc.ApplyTemplateAsync(tpl.Id, await svc.FromPatientAsync(people[0].Id, ct), ct);
        f1.SurgeonId = surgeons.GetValueOrDefault(ClinicianType.Endoscopist, surgeons.Values.First());
        (f1.ProcedureDate, f1.PreparedBy, f1.Email) = (_today.AddDays(24), "Grace Liu", f1.Email ?? "demo.patient@example.nz");
        Ok((await svc.SaveAsync(null, f1, true, "finance", ct)).Errors, "estimate 1");
        // 2. Insured hysteroscopy with sharp resection, issued.
        var f2 = await svc.FromPatientAsync(people[1].Id, ct);
        (f2.ProcedureId, f2.Anaesthetic, f2.Funding, f2.Insurer, f2.PolicyNumber, f2.ProcedureDate, f2.PreparedBy) =
            (gyn.Id, gyn.DefaultAnaesthetic, Est.Funding.Insurance, "Southern Cross", "SX-4471920", _today.AddDays(35), "Grace Liu");
        f2.SurgeonId = surgeons.GetValueOrDefault(ClinicianType.Surgeon);
        f2.Inputs.AddOns.Add(new Est.AddOnChoice { Code = "GYN-A01" });
        Ok((await svc.SaveAsync(null, f2, true, "finance", ct)).Errors, "estimate 2");
        // 3. Draft hernia repair (mesh if required) still being prepared.
        var f3 = await svc.FromPatientAsync(people[2].Id, ct);
        (f3.ProcedureId, f3.Anaesthetic, f3.PreparedBy) = (hernia.Id, hernia.DefaultAnaesthetic, "Grace Liu");
        f3.Inputs.AddOns.Add(new Est.AddOnChoice { Code = "GEN-A05", IfRequired = true });
        Ok((await svc.SaveAsync(null, f3, false, "finance", ct)).Errors, "estimate 3");
    }

    private async Task CodingAsync(CancellationToken ct)
    {
        var coding = S<Cod.CodingService>();
        // Demonstration identifiers (not Health NZ allocations) and the NZ purchaser codes.
        Ok(await coding.SaveSettingsAsync(new Cod.CodingSettingsEdit("D001", "D001", "DMO", "06", "06", "A0", "35", "06", false, 21, "D001", "CMPL", "16", "ICD-10-AM/ACHI/ACS Twelfth Edition"), "coder", ct), "coding settings");
        // A small DEMONSTRATION sample of codes with BrightPAS's own short descriptions – the real code table is licensed and imported from Health NZ.
        var sample = "clinical_code_system,clinical_code,clinical_code_type,clinical_code_description,sex_flag\r\n" + string.Join("\r\n", new[]
        {
            "16,K635,A,Polyp of colon (demo),", "16,Z121,V,Screening for neoplasm of intestinal tract (demo),", "16,D509,A,Iron deficiency anaemia unspecified (demo),",
            "16,R195,A,Other faecal abnormalities (demo),", "16,K802,A,Calculus of gallbladder without cholecystitis (demo),", "16,K4090,A,Unilateral inguinal hernia (demo),",
            "16,G560,A,Carpal tunnel syndrome (demo),", "16,C443,A,Malignant neoplasm of skin of face (demo),", "16,N800,A,Endometriosis of uterus (demo),F",
            "16,3209000,O,Colonoscopy to caecum (demo),", "16,3209300,O,Colonoscopy with polypectomy (demo),", "16,3047300,O,Panendoscopy to duodenum (demo),",
            "16,3044500,O,Laparoscopic cholecystectomy (demo),", "16,3933100,O,Release of carpal tunnel (demo),", "16,9251529,O,Sedation (demo),", "16,9251429,O,General anaesthesia (demo),",
            "16,W19,E,Unspecified fall (demo),", "16,Y9209,E,Home (demo),", "16,U739,E,Unspecified activity (demo),",
        }) + "\r\n";
        await coding.ImportCodesAsync(sample, "admin", ct);
        foreach (var (term, label, codes, dx) in new[]
        {
            ("COLON", "Colonoscopy", "32090-00 92515-29", "Z12.1"), ("POLYPECT", "Colonoscopy with polypectomy", "32093-00 92515-29", "K63.5"),
            ("GASTRO", "Gastroscopy", "30473-00 92515-29", "D50.9"), ("GASTROCOL", "Gastroscopy and colonoscopy", "30473-00 32090-00 92515-29", "D50.9"),
            ("LAPCHOLE", "Laparoscopic cholecystectomy", "30445-00 92514-29", "K80.2"), ("CARPAL", "Carpal tunnel release", "39331-00", "G56.0"),
        })
            Ok((await coding.SaveProcedureMapAsync(new Cod.ProcedureMapEdit(null, term, label, codes, dx), "admin", ct)).Errors, "picklist " + term);
        foreach (var a in Cod.CodingService.StarterAdvice()) Ok((await coding.SaveAdviceAsync(a, "admin", ct)).Errors, "advice " + a.Reference);
        var n = 10000;
        foreach (var c in await S<PasDbContext>().Clinicians.AsNoTracking().Where(c => c.Type == ClinicianType.Surgeon).ToListAsync(ct))
            Ok(await coding.SaveRegistrationAsync(c.Id, "MC", (n++).ToString(System.Globalization.CultureInfo.InvariantCulture), "admin", ct), "registration");
        // The discharged demo events: the first is coded, the next started.
        var queue = await coding.QueueAsync("open", ct);
        foreach (var (row, i) in queue.OrderByDescending(r => r.Procedure.Contains("olonoscopy", StringComparison.OrdinalIgnoreCase)).Take(2).Select((r, i) => (r, i)))
        {
            var w = await coding.WorkbenchAsync(row.Id, ct);
            var on = w.Episode.DischargeDate;
            var items = new List<Cod.ItemEdit> { new("A", "K63.5", "Sigmoid polyp 6 mm (demo)", null), new("O", "32093-00", "Colonoscopy to caecum, cold snare polypectomy", on), new("O", "92515-29", "IV sedation", on) };
            var (e, _, errors) = await coding.SaveAsync(row.Id, new Cod.CodingSave(w.Episode.Version, w.Episode.PurchaserCode, "R", "DR", "Demonstration coding", i == 0 ? items : items.Take(1).ToList(), i == 0), "coder", ct);
            if (e is null) throw new InvalidOperationException("Demo data – coding: " + string.Join(" ", errors));
        }
    }

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
