namespace BrightPAS.Identity;

/// <summary>
/// Fine-grained permissions, stored as role claims of type <see cref="ClaimType"/>.
/// Modules add their own permissions as they are built.
/// </summary>
public static class Permissions
{
    public const string ClaimType = "pas:perm";

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

    public static readonly IReadOnlyList<string> All =
    [
        PatientView, PatientRegister, PatientEdit, PatientMerge, PatientAlertsManage, BreakGlass, BreakGlassReview,
        AuditView, UsersManage, OrganisationManage, ModulesManage, IntegrationManage,
        LabsView, LabsAcknowledge, LabsMatch, TheatreView, TheatreBook, TheatreManage, FormsSend, FormsTriage,
        BillingView, BillingManage, BillingFees, RemindersView, RemindersManage, ReferralsView, ReferralsManage, ReferralsTriage
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

    private static readonly string[] Clinical =
        [Permissions.PatientView, Permissions.PatientEdit, Permissions.PatientAlertsManage, Permissions.BreakGlass,
         Permissions.LabsView, Permissions.LabsAcknowledge, Permissions.TheatreView];

    public static readonly IReadOnlyDictionary<string, (string Description, string[] Permissions)> Definitions =
        new Dictionary<string, (string, string[])>
        {
            [SystemAdministrator] = ("Manages users, organisation set-up and modules. No routine clinical access.",
                [Permissions.UsersManage, Permissions.OrganisationManage, Permissions.ModulesManage, Permissions.TheatreManage, Permissions.RemindersView, Permissions.RemindersManage]),
            [PrivacyOfficer] = ("Reviews break-glass use and audit reports; handles privacy requests.",
                [Permissions.AuditView, Permissions.BreakGlassReview, Permissions.PatientView]),
            [Reception] = ("Registers patients and manages bookings.",
                [Permissions.PatientView, Permissions.PatientRegister, Permissions.PatientEdit, Permissions.TheatreView, Permissions.TheatreBook,
                 Permissions.TheatreManage, Permissions.FormsSend, Permissions.BillingView, Permissions.BillingManage, Permissions.LabsMatch, Permissions.RemindersView,
                 Permissions.ReferralsView, Permissions.ReferralsManage]),
            [Nurse] = ("Clinical nursing access.", [.. Clinical, Permissions.PatientRegister, Permissions.TheatreBook, Permissions.FormsSend,
                 Permissions.FormsTriage, Permissions.LabsMatch, Permissions.RemindersView, Permissions.ReferralsView, Permissions.ReferralsManage]),
            [Anaesthetist] = ("Clinical access for anaesthetists.", [.. Clinical, Permissions.FormsTriage, Permissions.ReferralsView]),
            [Surgeon] = ("Clinical access for surgeons and endoscopists.", [.. Clinical, Permissions.TheatreBook, Permissions.ReferralsView, Permissions.ReferralsManage, Permissions.ReferralsTriage]),
            [CssdTechnician] = ("Sterile services: trays, loads and traceability (patient view limited to traceability).",
                [Permissions.PatientView]),
            [Finance] = ("Billing, invoices and payments.", [Permissions.PatientView, Permissions.BillingView, Permissions.BillingManage, Permissions.BillingFees]),
            [PracticeAssistant] = ("Specialist's practice assistant: bookings for their specialist's patients.",
                [Permissions.PatientView, Permissions.PatientRegister, Permissions.TheatreView, Permissions.TheatreBook, Permissions.FormsSend, Permissions.LabsView,
                 Permissions.ReferralsView, Permissions.ReferralsManage]),
            [IntegrationAdministrator] = ("Manages interfaces, message profiles and translation tables.",
                [Permissions.IntegrationManage]),
        };
}
