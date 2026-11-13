namespace BrightPAS.Identity;

/// <summary>
/// Fine-grained permissions, stored as role claims of type <see cref="ClaimType"/>.
/// Modules add their own permissions as they are built.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "pas:perm";
    /// <summary>Marks a default permission as already given to a role once (see RoleSeeder).</summary>
    public const string SeededClaimType = "pas:seeded";

    public const string PatientView = "patient.view";
    public const string PatientRegister = "patient.register";
    public const string PatientEdit = "patient.edit";
    public const string PatientMerge = "patient.merge";
    public const string PatientAlertsManage = "patient.alerts.manage";
    public const string BreakGlass = "patient.breakglass";
    public const string BreakGlassReview = "privacy.breakglass.review";
    public const string AuditView = "privacy.audit.view";
    public const string UsersManage = "admin.users.manage";
    public const string OrganisationManage = "admin.organisation.manage";
    public const string ModulesManage = "admin.modules.manage";
    public const string IntegrationManage = "admin.integration.manage";

    // Lab results module
    public const string LabsView = "labs.view";
    public const string LabsAcknowledge = "labs.acknowledge";
    public const string LabsMatch = "labs.match";
    // Theatre bookings module
    public const string TheatreView = "theatre.view";
    public const string TheatreBook = "theatre.book";
    public const string TheatreManage = "theatre.manage";
    // Pre-admission forms module
    public const string FormsSend = "forms.send";
    public const string FormsTriage = "forms.triage";
    // Estimates & invoices module
    public const string BillingView = "billing.view";
    public const string BillingManage = "billing.manage";
    public const string BillingFees = "billing.fees";
    // SMS & email reminders module
    public const string RemindersView = "reminders.view";
    public const string RemindersManage = "reminders.manage";
    // Referrals & waitlist module
    public const string ReferralsView = "referrals.view";
    public const string ReferralsManage = "referrals.manage";
    public const string ReferralsTriage = "referrals.triage";
    // Letters & documents module
    public const string LettersView = "letters.view";
    public const string LettersWrite = "letters.write";
    public const string LettersTemplates = "letters.templates";
    // Rostering module
    public const string RosterView = "roster.view";
    public const string RosterManage = "roster.manage";
    // Credentialing module
    public const string CredentialingView = "credentialing.view";
    public const string CredentialingManage = "credentialing.manage";
    // Portals module
    public const string PortalSpecialist = "portal.specialist";
    public const string PortalManage = "portal.manage";
    // Messaging engine (changes use IntegrationManage)
    public const string MessagingView = "messaging.view";

    /// <summary>Policy name allowing ANY of the given permissions (0.12.4), e.g. forms listed for staff who send OR triage them. Registered in <see cref="AnyOfPolicies"/>.</summary>
    public static string AnyOf(params string[] permissions) => string.Join('|', permissions);

    /// <summary>Every "any of" policy used by an endpoint; each part must be a real permission (checked by the endpoint security test).</summary>
    public static readonly IReadOnlyList<string> AnyOfPolicies = [AnyOf(FormsSend, FormsTriage)];

    public static readonly IReadOnlyList<string> All =
    [
        PatientView, PatientRegister, PatientEdit, PatientMerge, PatientAlertsManage, BreakGlass, BreakGlassReview,
        AuditView, UsersManage, OrganisationManage, ModulesManage, IntegrationManage,
        LabsView, LabsAcknowledge, LabsMatch, TheatreView, TheatreBook, TheatreManage, FormsSend, FormsTriage,
        BillingView, BillingManage, BillingFees, RemindersView, RemindersManage, ReferralsView, ReferralsManage, ReferralsTriage, LettersView, LettersWrite, LettersTemplates, RosterView, RosterManage,
        CredentialingView, CredentialingManage, PortalSpecialist, PortalManage, MessagingView
    ];
}

/// <summary>Default roles created at installation. TDS can adjust role permissions afterwards.</summary>
public static class DefaultRoles
{
    public const string SystemAdministrator = "System Administrator";
    public const string PrivacyOfficer = "Privacy Officer";
    public const string Reception = "Reception / Booking";
    public const string Nurse = "Nurse";
    public const string Anaesthetist = "Anaesthetist";
    public const string Surgeon = "Surgeon / Endoscopist";
    public const string CssdTechnician = "CSSD Technician";
    public const string Finance = "Finance";
    public const string PracticeAssistant = "Practice Assistant (PA)";
    public const string IntegrationAdministrator = "Integration Administrator";
    public const string RosterManager = "Roster Manager";
    public const string SpecialistPortal = "Specialist (portal only)";

    private static readonly string[] Clinical =
        [Permissions.PatientView, Permissions.PatientEdit, Permissions.PatientAlertsManage, Permissions.BreakGlass,
         Permissions.LabsView, Permissions.LabsAcknowledge, Permissions.TheatreView];

    public static readonly IReadOnlyDictionary<string, (string Description, string[] Permissions)> Definitions =
        new Dictionary<string, (string, string[])>
        {
            [SystemAdministrator] = ("Manages users, organisation set-up and modules. No routine clinical access.",
                [Permissions.UsersManage, Permissions.OrganisationManage, Permissions.ModulesManage, Permissions.TheatreManage, Permissions.RemindersView, Permissions.RemindersManage,
                 Permissions.LettersTemplates, Permissions.CredentialingView, Permissions.CredentialingManage]),
            [PrivacyOfficer] = ("Reviews break-glass use and audit reports; handles privacy requests; second-person check for identity corrections and merges.",
                [Permissions.AuditView, Permissions.BreakGlassReview, Permissions.PatientView, Permissions.PatientMerge]),
            [Reception] = ("Registers patients and manages bookings.",
                [Permissions.PatientView, Permissions.PatientRegister, Permissions.PatientEdit, Permissions.TheatreView, Permissions.TheatreBook,
                 Permissions.TheatreManage, Permissions.FormsSend, Permissions.BillingView, Permissions.BillingManage, Permissions.LabsMatch, Permissions.RemindersView,
                 Permissions.ReferralsView, Permissions.ReferralsManage, Permissions.LettersView, Permissions.LettersWrite, Permissions.RosterView, Permissions.PortalManage]),
            [Nurse] = ("Clinical nursing access.", [.. Clinical, Permissions.PatientRegister, Permissions.TheatreBook, Permissions.FormsSend,
                 Permissions.FormsTriage, Permissions.LabsMatch, Permissions.RemindersView, Permissions.ReferralsView, Permissions.ReferralsManage,
                 Permissions.LettersView, Permissions.LettersWrite, Permissions.RosterView]),
            [Anaesthetist] = ("Clinical access for anaesthetists.", [.. Clinical, Permissions.FormsTriage, Permissions.ReferralsView, Permissions.LettersView, Permissions.RosterView, Permissions.PortalSpecialist]),
            [Surgeon] = ("Clinical access for surgeons and endoscopists.", [.. Clinical, Permissions.TheatreBook, Permissions.ReferralsView, Permissions.ReferralsManage, Permissions.ReferralsTriage,
                 Permissions.LettersView, Permissions.LettersWrite, Permissions.PortalSpecialist]),
            [CssdTechnician] = ("Sterile services: trays, loads and traceability (patient view limited to traceability).",
                [Permissions.PatientView, Permissions.RosterView]),
            [Finance] = ("Billing, invoices and payments.", [Permissions.PatientView, Permissions.BillingView, Permissions.BillingManage, Permissions.BillingFees]),
            [PracticeAssistant] = ("Specialist's practice assistant: bookings for their specialist's patients.",
                [Permissions.PatientView, Permissions.PatientRegister, Permissions.TheatreView, Permissions.TheatreBook, Permissions.FormsSend, Permissions.LabsView,
                 Permissions.ReferralsView, Permissions.ReferralsManage, Permissions.LettersView, Permissions.LettersWrite, Permissions.PortalSpecialist]),
            [IntegrationAdministrator] = ("Manages interfaces, message flows, mappings and translation tables; sees message content (audited).",
                [Permissions.IntegrationManage, Permissions.MessagingView, Permissions.LettersView]),
            [RosterManager] = ("Writes the staff roster and approves leave (no patient access).",
                [Permissions.RosterView, Permissions.RosterManage]),
            [SpecialistPortal] = ("Visiting specialist or PA: sees only their own TDS patients' lists, appointments, waitlist and documents (read-only).",
                [Permissions.PortalSpecialist]),
        };
}
