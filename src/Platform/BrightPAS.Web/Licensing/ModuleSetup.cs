using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Core.Setup;
using BrightPAS.Identity;
using BrightPAS.Licensing;

namespace BrightPAS.Web.Licensing;

/// <summary>
/// One step of a module's guided set-up. <see cref="Ok"/> is worked out live from the system's settings and data,
/// so the wizard always shows the truth. Optional steps can be skipped; required ones must pass before the module is marked set up.
/// </summary>
public sealed record SetupCheck(string Key, string Title, string Explanation, bool Ok, string Status, bool Optional,
    string? ActionLabel = null, string? ActionLink = null, string? ActionPermission = null, string? WhoCanDoIt = null);

public enum ModuleState { NotLicensed, NeedsSetUp, Active, ReadOnly, ComingLater }

public sealed record ModuleRow(string Id, string Name, string Description, bool Available, string Access, string State, DateTimeOffset? SetUpAt, string? SetUpBy,
    int? RequiredStepsOpen, bool HasWizard, string? SkippedChecks);

/// <summary>Module set-up wizards (ADR 0014): one installer, licence unlocks, each module guided through its own set-up inside BrightPAS.</summary>
public sealed class ModuleSetupService(PasDbContext core, LicenceCache licence, AuditWriter audit, TimeProvider clock, IServiceProvider sp, IConfiguration config)
{
    /// <summary>Modules that are built and have a set-up wizard.</summary>
    public static readonly string[] WithWizard = ["theatre", "referrals", "labs", "forms", "billing", "reminders", "letters", "rostering", "credentialing", "portal", "messaging"];

    public async Task<IReadOnlyDictionary<string, bool>> SetUpModulesAsync(CancellationToken ct = default) =>
        (await core.ModuleSetup.AsNoTracking().Select(m => m.ModuleId).ToListAsync(ct)).ToDictionary(m => m, _ => true, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ModuleRow>> ListAsync(CancellationToken ct = default)
    {
        var done = await core.ModuleSetup.AsNoTracking().ToDictionaryAsync(m => m.ModuleId, StringComparer.OrdinalIgnoreCase, ct);
        var status = licence.Status;
        var list = new List<ModuleRow>();
        foreach (var m in ProductModules.All)
        {
            var access = status.AccessTo(m.Id);
            var setUp = done.GetValueOrDefault(m.Id);
            var state = !m.Available ? ModuleState.ComingLater
                : access == ModuleAccess.None ? ModuleState.NotLicensed
                : access == ModuleAccess.ReadOnly ? ModuleState.ReadOnly
                : setUp is null ? ModuleState.NeedsSetUp : ModuleState.Active;
            int? open = null;
            if (m.Available && access != ModuleAccess.None && WithWizard.Contains(m.Id))
                open = (await ChecksAsync(m.Id, ct)).Count(c => !c.Ok && !c.Optional);
            list.Add(new ModuleRow(m.Id, m.Name, m.Description, m.Available, access.ToString(), state.ToString(), setUp?.SetUpAt, setUp?.SetUpBy,
                open, WithWizard.Contains(m.Id), setUp?.SkippedChecks));
        }
        return list;
    }

    public async Task<IReadOnlyList<SetupCheck>> ChecksAsync(string moduleId, CancellationToken ct = default) => moduleId switch
    {
        "theatre" => await TheatreChecksAsync(ct),
        "referrals" => await ReferralsChecksAsync(ct),
        "labs" => await LabsChecksAsync(ct),
        "forms" => FormsChecks(),
        "billing" => await BillingChecksAsync(ct),
        "reminders" => await RemindersChecksAsync(ct),
        "letters" => await LettersChecksAsync(ct),
        "rostering" => await RosterChecksAsync(ct),
        "credentialing" => await CredentialingChecksAsync(ct),
        "portal" => await PortalChecksAsync(ct),
        "messaging" => await MessagingChecksAsync(ct),
        _ => []
    };

    public async Task<(bool Ok, string? Error)> CompleteAsync(string moduleId, string user, CancellationToken ct = default)
    {
        var m = ProductModules.Find(moduleId);
        if (m is null || !m.Available || !WithWizard.Contains(m.Id)) return (false, "This module has no set-up.");
        if (licence.Status.AccessTo(m.Id) != ModuleAccess.Full) return (false, $"The {m.Name} module is not licensed. Load a licence that includes it first.");
        var checks = await ChecksAsync(m.Id, ct);
        var missing = checks.Where(c => !c.Ok && !c.Optional).ToList();
        if (missing.Count > 0) return (false, "Finish these steps first: " + string.Join("; ", missing.Select(c => c.Title)) + ".");
        var skipped = string.Join("; ", checks.Where(c => !c.Ok && c.Optional).Select(c => c.Title));
        var row = await core.ModuleSetup.SingleOrDefaultAsync(x => x.ModuleId == m.Id, ct);
        if (row is null) core.ModuleSetup.Add(row = new ModuleSetupState { ModuleId = m.Id });
        row.SetUpAt = clock.GetUtcNow(); row.SetUpBy = user; row.SkippedChecks = skipped.Length == 0 ? null : skipped[..Math.Min(skipped.Length, 1000)];
        await core.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ModuleSetUp", "Module", m.Id, reason: skipped.Length == 0 ? "All steps complete" : $"Optional steps skipped: {skipped}", ct: ct);
        return (true, null);
    }

    public async Task<string?> ReopenAsync(string moduleId, string user, CancellationToken ct = default)
    {
        var row = await core.ModuleSetup.SingleOrDefaultAsync(x => x.ModuleId == moduleId, ct);
        if (row is null) return "This module has not been set up yet.";
        core.ModuleSetup.Remove(row);
        await core.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ModuleSetUpReopened", "Module", moduleId, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ per-module checks

    private T Svc<T>() where T : notnull => sp.GetRequiredService<T>();

    private async Task<IReadOnlyList<SetupCheck>> TheatreChecksAsync(CancellationToken ct)
    {
        var rooms = await core.Locations.AsNoTracking().CountAsync(l => l.IsActive && (l.Type == LocationType.Theatre || l.Type == LocationType.ProcedureRoom), ct);
        var lists = await Svc<BrightPAS.Modules.Theatre.TheatreDbContext>().Sessions.AsNoTracking().CountAsync(ct);
        var bookers = await UsersWithAsync(Permissions.TheatreBook, ct);
        return
        [
            new("rooms", "Theatres and procedure rooms", "At least one active theatre or procedure room is needed to create theatre lists. TDS's rooms were created at installation.",
                rooms > 0, rooms > 0 ? $"{rooms} active room(s)." : "No active theatre or procedure room.", false),
            new("bookers", "Staff who can book", "Reception, nurses, surgeons or PAs need user accounts with booking rights.",
                bookers > 0, bookers > 0 ? $"{bookers} user(s) can book theatre." : "No user can book theatre yet.", false, "Add user accounts", "/admin/users", Permissions.UsersManage),
            new("clinicians", "Clinician register", "Add TDS's surgeons, endoscopists and anaesthetists so they can be picked on lists instead of typed.",
                await core.Clinicians.AnyAsync(c => c.IsActive, ct), $"{await core.Clinicians.CountAsync(c => c.IsActive, ct)} active clinician(s).", true,
                "Open clinicians", "/admin/organisation?tab=clinicians", Permissions.OrganisationManage),
            new("procedures", "Procedure catalogue", "Add the procedures TDS does, with usual times, so bookings fill in the duration and anaesthetic.",
                await core.Procedures.AnyAsync(p => p.IsActive, ct), $"{await core.Procedures.CountAsync(p => p.IsActive, ct)} procedure(s).", true,
                "Open procedures", "/admin/organisation?tab=procedures", Permissions.OrganisationManage),
            new("templates", "Regular list templates", "Set up each surgeon's regular lists (e.g. every Tuesday morning) so lists can be created for weeks ahead in one go.",
                await Svc<BrightPAS.Modules.Theatre.TheatreDbContext>().Templates.AnyAsync(t => t.IsActive, ct), "Optional.", true,
                "Open list templates", "/theatre/templates", Permissions.TheatreManage),
            new("first-list", "Create the first theatre list", "Create a list (theatre, date, times, surgeon) so bookings can be made. Optional during set-up.",
                lists > 0, lists > 0 ? $"{lists} list(s) created." : "No theatre lists yet.", true, "Open Theatre lists", "/theatre", Permissions.TheatreManage, "Reception or System Administrator"),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> ReferralsChecksAsync(CancellationToken ct)
    {
        var svc = Svc<BrightPAS.Modules.Referrals.ReferralService>();
        var s = await svc.SettingsAsync(ct);
        var theatre = licence.Status.AccessTo("theatre") == ModuleAccess.Full;
        var triagers = await UsersWithAsync(Permissions.ReferralsTriage, ct);
        var managers = await UsersWithAsync(Permissions.ReferralsManage, ct);
        return
        [
            new("targets", "Waiting-time targets", $"Each priority has a target number of days from the decision to treat. Current: urgent {s.UrgentDays}, semi-urgent {s.SemiUrgentDays}, routine {s.RoutineDays} days. Confirm or change them.",
                s.UpdatedAt is not null, s.UpdatedAt is not null ? $"Confirmed by {s.UpdatedBy} on {s.UpdatedAt:d MMM yyyy}." : "Not yet confirmed.", false, "Review targets", "/admin/referral-targets", Permissions.ModulesManage),
            new("theatre", "Theatre bookings module", "Booking from the waitlist uses the Theatre bookings module.",
                theatre, theatre ? "Licensed." : "Not licensed: the waitlist works, but patients must be booked by hand.", true),
            new("triagers", "Clinicians who triage", "Surgeons or endoscopists accept or decline referrals. They need user accounts in the Surgeon / Endoscopist role.",
                triagers > 0, triagers > 0 ? $"{triagers} user(s) can triage referrals." : "No user can triage referrals yet.", false, "Add user accounts", "/admin/users", Permissions.UsersManage),
            new("managers", "Staff who manage the waitlist", "Reception, nurses or PAs record referrals and manage the waitlist.",
                managers > 0, managers > 0 ? $"{managers} user(s) can manage the waitlist." : "No user can manage the waitlist yet.", false, "Add user accounts", "/admin/users", Permissions.UsersManage),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> LettersChecksAsync(CancellationToken ct)
    {
        var site = await Svc<BrightPAS.Core.Organisation.OrganisationService>().MainSiteAsync(ct);
        var addressOk = site is { } x && !string.IsNullOrWhiteSpace(x.Site.AddressLine1) && !string.IsNullOrWhiteSpace(x.Site.City) && !string.IsNullOrWhiteSpace(x.Site.Phone);
        var letters = Svc<BrightPAS.Modules.Letters.LetterService>();
        var templates = (await letters.TemplatesAsync(activeOnly: true, ct)).Count;
        var db = Svc<BrightPAS.Modules.Letters.LettersDbContext>();
        var practices = await db.Practices.CountAsync(p => p.IsActive, ct);
        var hl = await letters.SettingsAsync(ct);
        var writers = await UsersWithAsync(Permissions.LettersWrite, ct);
        return
        [
            new("clinic", "Clinic address and phone (letterhead)", "Printed at the top of every letter and used in the {ClinicAddress} and {ClinicPhone} placeholders.",
                addressOk, addressOk ? $"{site!.Value.Site.AddressLine1}, {site.Value.Site.City} · {site.Value.Site.Phone}" : "Address or phone missing.", false,
                "Open clinic details", "/admin/organisation", Permissions.OrganisationManage),
            new("templates", "Letter templates", "Standard templates (appointment, admission, GP notification, referral received/declined) are built in. Review the wording.",
                templates > 0, $"{templates} active template(s).", false, "Open templates", "/admin/letters", Permissions.LettersTemplates),
            new("writers", "Staff who write letters", "Reception, nurses, surgeons or PAs write and send letters.",
                writers > 0, writers > 0 ? $"{writers} user(s) can write letters." : "No user can write letters yet.", false, "Add user accounts", "/admin/users", Permissions.UsersManage),
            new("practices", "GP practices", "Add the GP practices TDS writes to (with HPI facility number and HealthLink EDI). Patients are linked to their practice by its HPI facility number.",
                practices > 0, $"{practices} practice(s).", true, "Open GP practices", "/admin/letters?tab=practices", Permissions.LettersTemplates),
            new("healthlink", "HealthLink connection", "Letters to GPs go through HealthLink. Test mode records messages without sending; 'Folder' mode hands them to the HealthLink messaging client on this server.",
                hl.Mode == BrightPAS.Modules.Letters.HealthLinkMode.FolderDrop, hl.Mode switch
                {
                    BrightPAS.Modules.Letters.HealthLinkMode.FolderDrop => $"Connected via folder as {hl.SenderEdi}.",
                    BrightPAS.Modules.Letters.HealthLinkMode.TestOutbox => "TEST MODE – letters to GPs are recorded, not sent. Print them meanwhile.",
                    _ => "Off – print letters to GPs."
                }, true, "Open HealthLink settings", "/admin/letters?tab=healthlink", Permissions.LettersTemplates, "IT / Integration Administrator"),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> RosterChecksAsync(CancellationToken ct)
    {
        var db = Svc<BrightPAS.Modules.Rostering.RosterDbContext>();
        var staff = await db.Staff.CountAsync(s => s.IsActive, ct);
        var linked = await db.Staff.CountAsync(s => s.IsActive && s.UserName != null, ct);
        var templates = await db.Templates.CountAsync(t => t.IsActive, ct);
        var managers = await UsersWithAsync(Permissions.RosterManage, ct);
        return
        [
            new("managers", "Who writes the roster", "Give the person who does the roster the 'Roster Manager' role (User accounts). They add staff, shifts and approve leave.",
                managers > 0, managers > 0 ? $"{managers} user(s) can manage the roster." : "Nobody can manage the roster yet.", false, "Open user accounts", "/admin/users", Permissions.UsersManage),
            new("staff", "Staff list", "Add the people who are rostered, with their role, colour and contracted hours.",
                staff > 0, $"{staff} staff member(s).", false, null, null, null, "Roster Manager (Roster › Staff)"),
            new("linked", "Link staff to their BrightPAS accounts", "Linked staff see their own shifts and can request leave in BrightPAS.",
                linked > 0, $"{linked} of {staff} linked.", true, null, null, null, "Roster Manager (Roster › Staff)"),
            new("templates", "Regular shifts", "Set up each person's usual weekly shifts, then create the roster weeks ahead in one step.",
                templates > 0, $"{templates} regular shift(s).", true, null, null, null, "Roster Manager (Roster › Regular shifts)"),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> CredentialingChecksAsync(CancellationToken ct)
    {
        var db = Svc<BrightPAS.Modules.Credentialing.CredDbContext>();
        var clinicians = await core.Clinicians.CountAsync(c => c.IsActive, ct);
        var withCreds = await db.Credentials.Where(c => c.IsActive).Select(c => c.ClinicianId).Distinct().CountAsync(ct);
        var privs = await db.Privileges.Where(p => p.IsActive).Select(p => p.ClinicianId).Distinct().CountAsync(ct);
        var managers = await UsersWithAsync(Permissions.CredentialingManage, ct);
        return
        [
            new("clinicians", "Clinician register", "Credentials are recorded against the clinicians in Organisation set-up › Clinicians.",
                clinicians > 0, $"{clinicians} active clinician(s).", false, "Open organisation set-up", "/admin/organisation", Permissions.OrganisationManage),
            new("managers", "Who keeps credentials up to date", "Give the credentialing administrator a role with 'Record credentials' (System Administrator has it).",
                managers > 0, $"{managers} user(s).", false, "Open roles", "/admin/roles", Permissions.UsersManage),
            new("credentials", "Record current credentials", "Enter each clinician's APC, indemnity and TDS approval with expiry dates. Bookings warn when one has expired.",
                withCreds > 0, $"{withCreds} of {clinicians} clinician(s) have credentials recorded.", false, "Open credentialing", "/credentialing", Permissions.CredentialingManage),
            new("privileges", "Scope of practice", "Record what each surgeon/endoscopist is approved to do at TDS (whole service or individual procedures).",
                privs > 0, $"{privs} clinician(s) with scope recorded.", true, "Open credentialing", "/credentialing", Permissions.CredentialingManage),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> PortalChecksAsync(CancellationToken ct)
    {
        var linked = await core.Clinicians.CountAsync(c => c.IsActive && c.UserName != null, ct);
        var delegates = await Svc<BrightPAS.Modules.Portal.PortalDbContext>().Delegates.CountAsync(ct);
        var staff = await UsersWithAsync(Permissions.PortalManage, ct);
        return
        [
            new("staff", "Who sends patient links and answers requests", "Reception has 'Send patient portal links' by default.",
                staff > 0, $"{staff} user(s).", false, "Open roles", "/admin/roles", Permissions.UsersManage),
            new("specialists", "Specialists linked to their accounts", "In Organisation set-up › Clinicians, enter each specialist's BrightPAS user name. Give visiting specialists the 'Specialist (portal only)' role.",
                linked > 0, $"{linked} clinician(s) linked to a user account.", true, "Open organisation set-up", "/admin/organisation", Permissions.OrganisationManage),
            new("pas", "Practice assistants", "Link each PA to the specialist(s) they work for (User accounts › PA links).",
                delegates > 0, $"{delegates} PA link(s).", true, "Open user accounts", "/admin/users", Permissions.UsersManage),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> MessagingChecksAsync(CancellationToken ct)
    {
        var db = Svc<BrightPAS.Modules.Messaging.MsgDbContext>();
        var admins = await UsersWithAsync(Permissions.IntegrationManage, ct);
        var viewers = await UsersWithAsync(Permissions.MessagingView, ct);
        var ifaces = await db.Interfaces.AsNoTracking().CountAsync(i => i.IsActive && i.Transport != BrightPAS.Modules.Messaging.Transport.Monitored, ct);
        var flows = await db.Flows.AsNoTracking().CountAsync(f => f.IsActive, ct);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct);
        var listeners = await db.Interfaces.AsNoTracking().Where(i => i.IsActive && (i.Transport == BrightPAS.Modules.Messaging.Transport.MllpListen)).Select(i => i.Id).ToListAsync(ct);
        var open = listeners.Count == 0 ? 0 : await db.Interfaces.AsNoTracking().CountAsync(i => listeners.Contains(i.Id) && !db.AllowedClients.Any(c => c.InterfaceId == i.Id), ct);
        return
        [
            new("admins", "Integration administrators", "Give the people who set up interfaces the 'Integration Administrator' role. A second person approves mapping changes before they go live.",
                admins >= 2, $"{admins} user(s) can change interfaces{(admins == 1 ? " – add a second so changes can be approved" : "")}.", false, "Open user accounts", "/admin/users", Permissions.UsersManage),
            new("viewers", "Who watches the dashboard", "Interface problems show on the messaging dashboard. Viewing a message's content is audited.",
                viewers > 0, $"{viewers} user(s) can see the dashboard.", true, "Open user accounts", "/admin/users", Permissions.UsersManage),
            new("interfaces", "Connections", "Add each connection: lab results (MLLP), devices (DICOM), partner APIs (REST), folders, or outbound systems.",
                ifaces > 0, $"{ifaces} active interface(s).", false, "Open interfaces", "/messaging/interfaces", Permissions.IntegrationManage),
            new("allowed", "Only known systems can connect", "Every MLLP listener should list the IP addresses allowed to connect; DICOM devices must be registered by IP and AE title.",
                open == 0, open == 0 ? "All listeners are restricted." : $"{open} MLLP listener(s) accept any computer.", true, "Open interfaces", "/messaging/interfaces", Permissions.IntegrationManage),
            new("flows", "Message flows", "Draw a flow for each message path (input → filters → mapper → outputs) and test it with a sample message before switching it on.",
                flows > 0, $"{flows} active flow(s).", true, "Open flows", "/messaging/flows", Permissions.IntegrationManage),
            new("alerts", "Alert email", "Who is emailed when an interface stops or messages fail (needs the reminders email sender).",
                !string.IsNullOrWhiteSpace(settings?.AlertEmail), settings?.AlertEmail is { } a ? $"Alerts go to {a}." : "No alert email set.", true, "Open messaging settings", "/messaging/settings", Permissions.IntegrationManage),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> LabsChecksAsync(CancellationToken ct)
    {
        var port = config.GetValue("Labs:Mllp:Port", 0);
        var allowed = config["Labs:Mllp:AllowedClients"];
        var received = await Svc<BrightPAS.Modules.Labs.LabsDbContext>().Messages.AsNoTracking().CountAsync(ct);
        var viewers = await UsersWithAsync(Permissions.LabsView, ct);
        return
        [
            new("mllp", "Laboratory connection (HL7 over MLLP)", "Results arrive automatically when the lab's interface engine sends to this server. The port is set by Setup (--mllp-port). Optional: results can also be imported by hand.",
                port > 0, port > 0 ? $"Listening on port {port}{(string.IsNullOrWhiteSpace(allowed) ? " – WARNING: any computer may connect; restrict with --mllp-allowed" : $" (allowed: {allowed})")}." : "Not switched on.", true, null, null, null, "IT – re-run Setup with --mllp-port"),
            new("test", "Import a test result", "Import a synthetic test message to check results file to the right patient.",
                received > 0, received > 0 ? $"{received} message(s) received." : "No messages yet.", true, "Open Interfaces", "/admin/interfaces", Permissions.IntegrationManage, "Integration Administrator"),
            new("viewers", "Clinicians who see results", "Nurses, anaesthetists and surgeons see the results inbox.",
                viewers > 0, viewers > 0 ? $"{viewers} user(s) can view results." : "No user can view results yet.", false, "Add user accounts", "/admin/users", Permissions.UsersManage),
        ];
    }

    private static IReadOnlyList<SetupCheck> FormsChecks() =>
    [
        new("questionnaire", "Health questionnaire", $"The standard pre-admission questionnaire (version {BrightPAS.Modules.Forms.Questionnaire.Version}) is built in.", true, "Ready.", false),
        new("links", "Sending links by SMS or email", "Questionnaire links can be sent automatically when the SMS & email reminders module is licensed and set up; otherwise staff copy the link.",
            true, "Links can always be copied; sending uses the reminders module.", true),
    ];

    private async Task<IReadOnlyList<SetupCheck>> BillingChecksAsync(CancellationToken ct)
    {
        var db = Svc<BrightPAS.Modules.Billing.BillingDbContext>();
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct);
        var fees = await db.Fees.AsNoTracking().CountAsync(f => f.IsActive, ct);
        const string finance = "Finance";
        return
        [
            new("gst", "GST number", "Printed on estimates and invoices (tax invoices need it).", !string.IsNullOrWhiteSpace(s?.GstNumber),
                string.IsNullOrWhiteSpace(s?.GstNumber) ? "Not entered." : $"GST number {s!.GstNumber}.", false, "Open billing settings", "/billing?tab=settings", Permissions.BillingFees, finance),
            new("bank", "Bank account for payments", "Printed on invoices so patients can pay by bank transfer.", !string.IsNullOrWhiteSpace(s?.BankAccount),
                string.IsNullOrWhiteSpace(s?.BankAccount) ? "Not entered." : "Entered.", false, "Open billing settings", "/billing?tab=settings", Permissions.BillingFees, finance),
            new("split", "Clinician fee split", $"Default split of procedure fees: surgeon {s?.SurgeonPercent ?? 40}%, anaesthetist {s?.AnaesthetistPercent ?? 20}%, remainder to TDS. Change it in billing settings if needed.",
                true, "Using the shown percentages.", true, "Open billing settings", "/billing?tab=settings", Permissions.BillingFees, finance),
            new("fees", "Fee schedule", "Add TDS's fees (procedure, facility, consumables), or load the example schedule and edit it.", fees > 0,
                fees > 0 ? $"{fees} active fee(s)." : "No fees yet.", false, "Open fee schedule", "/billing?tab=fees", Permissions.BillingFees, finance),
        ];
    }

    private async Task<IReadOnlyList<SetupCheck>> RemindersChecksAsync(CancellationToken ct)
    {
        var svc = Svc<BrightPAS.Modules.Reminders.ReminderService>();
        var db = Svc<BrightPAS.Modules.Reminders.RemindersDbContext>();
        var s = await svc.SettingsAsync(ct);
        var problems = await svc.ProblemsAsync(ct);
        var smsProblem = problems.FirstOrDefault(p => p.Contains("SMS", StringComparison.OrdinalIgnoreCase) || p.Contains("Modica", StringComparison.OrdinalIgnoreCase));
        var emailProblem = problems.FirstOrDefault(p => (p.Contains("mail", StringComparison.OrdinalIgnoreCase) || p.Contains("SMTP", StringComparison.OrdinalIgnoreCase)) && !p.StartsWith("TEST MODE"));
        var smsOk = s.SmsProvider != BrightPAS.Modules.Reminders.SmsProvider.Off && (smsProblem is null || smsProblem.StartsWith("TEST MODE"));
        var emailOk = s.EmailProvider != BrightPAS.Modules.Reminders.EmailProvider.Off && emailProblem is null;
        var testMode = s.SmsProvider == BrightPAS.Modules.Reminders.SmsProvider.TestOutbox || s.EmailProvider == BrightPAS.Modules.Reminders.EmailProvider.TestOutbox;
        var tested = await db.Messages.AsNoTracking().AnyAsync(m => m.Kind == BrightPAS.Modules.Reminders.MessageKind.Test
            && (m.Status == BrightPAS.Modules.Reminders.MessageStatus.Sent || m.Status == BrightPAS.Modules.Reminders.MessageStatus.Delivered), ct);
        const string link = "/reminders?tab=settings";
        return
        [
            new("clinic", "Clinic name and phone", "Shown in every message so patients know who it is from and who to call.",
                !string.IsNullOrWhiteSpace(s.ClinicName) && !string.IsNullOrWhiteSpace(s.ClinicPhone),
                string.IsNullOrWhiteSpace(s.ClinicPhone) ? "Clinic phone number not entered." : $"{s.ClinicName}, {s.ClinicPhone}.", false, "Open settings", link, Permissions.RemindersManage),
            new("sms", "SMS vendor", "Choose the SMS vendor (Modica by default) and enter the account details from the vendor. 'Test outbox' records messages without sending them.",
                smsOk, s.SmsProvider == BrightPAS.Modules.Reminders.SmsProvider.Off ? "SMS is switched off." : smsProblem is { } sp2 && !sp2.StartsWith("TEST MODE") ? sp2 : $"{s.SmsProvider} configured.",
                s.SmsProvider == BrightPAS.Modules.Reminders.SmsProvider.Off, "Open settings", link, Permissions.RemindersManage),
            new("email", "Email (SMTP)", "Email copies of reminders and questionnaire links go through TDS's mail server (Microsoft 365 / Exchange). Optional if TDS only uses SMS.",
                emailOk, s.EmailProvider == BrightPAS.Modules.Reminders.EmailProvider.Off ? "Email is switched off." : emailProblem ?? $"{s.EmailProvider} configured.", true, "Open settings", link, Permissions.RemindersManage),
            new("test", "Send a test message", "Send a test SMS or email to your own phone or mailbox from the settings page, and check it arrives.",
                tested, tested ? "A test message was sent successfully." : "No successful test message yet.", false, "Open settings", link, Permissions.RemindersManage),
            new("mode", "Live sending", "While in test mode, messages are recorded in the outbox but not sent to patients. Switch to the real vendor before go-live.",
                !testMode && smsOk, testMode ? "TEST MODE – nothing is sent to patients." : !smsOk ? "Nothing is sent until the SMS vendor is set up." : "Live: messages are sent to patients.", true, "Open settings", link, Permissions.RemindersManage),
        ];
    }

    private async Task<int> UsersWithAsync(string permission, CancellationToken ct)
    {
        var id = Svc<PasIdentityDbContext>();
        var roleIds = await id.RoleClaims.AsNoTracking().Where(c => c.ClaimType == Permissions.ClaimType && c.ClaimValue == permission).Select(c => c.RoleId).ToListAsync(ct);
        return await id.UserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId))
            .Join(id.Users.AsNoTracking().Where(u => u.IsActive), ur => ur.UserId, u => u.Id, (ur, u) => u.Id).Distinct().CountAsync(ct);
    }
}
