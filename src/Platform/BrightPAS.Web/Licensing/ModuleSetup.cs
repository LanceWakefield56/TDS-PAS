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
    public static readonly string[] WithWizard = ["theatre", "referrals", "labs", "forms", "billing", "reminders"];

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
