namespace Tds.Pas.Identity;

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

    public static readonly IReadOnlyList<string> All =
    [
        PatientView, PatientRegister, PatientEdit, PatientMerge, PatientAlertsManage, BreakGlass, BreakGlassReview,
        AuditView, UsersManage, OrganisationManage, ModulesManage, IntegrationManage
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
        [Permissions.PatientView, Permissions.PatientEdit, Permissions.PatientAlertsManage, Permissions.BreakGlass];

    public static readonly IReadOnlyDictionary<string, (string Description, string[] Permissions)> Definitions =
        new Dictionary<string, (string, string[])>
        {
            [SystemAdministrator] = ("Manages users, organisation set-up and modules. No routine clinical access.",
                [Permissions.UsersManage, Permissions.OrganisationManage, Permissions.ModulesManage]),
            [PrivacyOfficer] = ("Reviews break-glass use and audit reports; handles privacy requests.",
                [Permissions.AuditView, Permissions.BreakGlassReview, Permissions.PatientView]),
            [Reception] = ("Registers patients and manages bookings.",
                [Permissions.PatientView, Permissions.PatientRegister, Permissions.PatientEdit]),
            [Nurse] = ("Clinical nursing access.", [.. Clinical, Permissions.PatientRegister]),
            [Anaesthetist] = ("Clinical access for anaesthetists.", Clinical),
            [Surgeon] = ("Clinical access for surgeons and endoscopists.", Clinical),
            [CssdTechnician] = ("Sterile services: trays, loads and traceability (patient view limited to traceability).",
                [Permissions.PatientView]),
            [Finance] = ("Billing, invoices and payments.", [Permissions.PatientView]),
            [PracticeAssistant] = ("Specialist's practice assistant: bookings for their specialist's patients.",
                [Permissions.PatientView, Permissions.PatientRegister]),
            [IntegrationAdministrator] = ("Manages interfaces, message profiles and translation tables.",
                [Permissions.IntegrationManage]),
        };
}
