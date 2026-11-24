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
    // Assets & CSSD module (0.13.0)
    public const string AssetsView = "assets.view";
    public const string AssetsManage = "assets.manage";
    public const string CssdProcess = "cssd.process";
    public const string CssdRelease = "cssd.release";
    public const string StockManage = "stock.manage";
    public const string PurchasingApprove = "purchasing.approve";
    // Clinical record (eMR) module (0.14.0)
    public const string EmrView = "emr.view";
    public const string EmrWrite = "emr.write";
    public const string EmrControlledDrugs = "emr.controlled-drugs";
    // Endoscopy module (0.15.0)
    public const string EndoView = "endoscopy.view";
    public const string EndoWrite = "endoscopy.write";
    public const string EndoSign = "endoscopy.sign";
    public const string EndoManage = "endoscopy.manage";
    // BrightPAS Scribe (0.16.0)
    public const string ScribeUse = "scribe.use";
    public const string ScribeApprove = "scribe.approve";
    public const string ScribeManage = "scribe.manage";
    // Devices & SaferSleep (0.17.0)
    public const string DevicesUse = "devices.use";
    public const string DevicesManage = "devices.manage";
    // Quality management (0.19.0)
    public const string QualityReport = "quality.report";
    public const string QualityView = "quality.view";
    public const string QualityManage = "quality.manage";
    // Coding & statutory reporting (0.20.0)
    public const string CodingCode = "coding.code";
    public const string CodingSubmit = "coding.submit";
    public const string CodingManage = "coding.manage";
    // Procedure estimates module (0.21.0)
    public const string EstimatesBuild = "estimates.build";
    public const string EstimatesManage = "estimates.manage";
    // Patient triage and suitability module (0.22.0)
    public const string TriageView = "triage.view";
    public const string TriageAssess = "triage.assess";
    public const string TriageReview = "triage.review";
    public const string TriageRules = "triage.rules";
    public const string TriageApprove = "triage.approve";
    public const string FormsDesign = "forms.design";
    public const string FormsApprove = "forms.approve";
    public const string FormsFill = "forms.fill";
    public const string ResearchBuild = "research.build";
    public const string ResearchApprove = "research.approve";
    public const string FollowUpView = "followup.view";
    public const string FollowUpRecord = "followup.record";
    public const string FollowUpPlans = "followup.plans";
    public const string FollowUpSsi = "followup.ssi";
    public const string FollowUpReport = "followup.report";
    // Claims & payments (0.26.0)
    public const string ClaimsView = "claims.view";
    public const string ClaimsManage = "claims.manage";
    public const string ClaimsAdmin = "claims.admin";
    public const string PaymentsTake = "payments.take";
    public const string IdentityLookup = "identity.lookup";

    /// <summary>Policy name allowing ANY of the given permissions (0.12.4), e.g. forms listed for staff who send OR triage them. Registered in <see cref="AnyOfPolicies"/>.</summary>
    public static string AnyOf(params string[] permissions) => string.Join('|', permissions);

    /// <summary>Every "any of" policy used by an endpoint; each part must be a real permission (checked by the endpoint security test).</summary>
    public static readonly IReadOnlyList<string> AnyOfPolicies = [AnyOf(FormsSend, FormsTriage), AnyOf(ScribeUse, ScribeApprove, ScribeManage), AnyOf(DevicesUse, DevicesManage), AnyOf(QualityView, QualityManage), AnyOf(CodingCode, CodingSubmit, CodingManage), AnyOf(EstimatesBuild, EstimatesManage), AnyOf(TriageView, TriageAssess, TriageReview, TriageRules), AnyOf(FollowUpView, FollowUpRecord, FollowUpPlans, FollowUpSsi, FollowUpReport), AnyOf(RemindersView, LettersView), AnyOf(ResearchBuild, ResearchApprove), AnyOf(FormsDesign, FormsFill, FormsApprove), AnyOf(FormsDesign, FormsApprove), AnyOf(ClaimsView, ClaimsManage, ClaimsAdmin), AnyOf(ClaimsManage, PaymentsTake), AnyOf(ClaimsManage, ClaimsAdmin)];
    /// <summary>Claims, approvals and remittances can be read by anyone with a claims permission.</summary>
    public static string ClaimsAny => AnyOf(ClaimsView, ClaimsManage, ClaimsAdmin);
    /// <summary>Scribe templates and dictionary can be read by clinicians, approvers and scribe administrators.</summary>
    public static string ScribeAny => AnyOf(ScribeUse, ScribeApprove, ScribeManage);
    /// <summary>Monitor status and the anaesthetic record can be read by clinicians using devices and by device administrators.</summary>
    public static string DevicesAny => AnyOf(DevicesUse, DevicesManage);
    public static string QualityAny => AnyOf(QualityView, QualityManage);
    /// <summary>The coding queue, code search and advice can be read by coders, by staff who submit files and by coding administrators.</summary>
    public static string CodingAny => AnyOf(CodingCode, CodingSubmit, CodingManage);
    /// <summary>Estimates and the price list can be read by staff who build estimates and by estimates administrators.</summary>
    public static string EstimatesAny => AnyOf(EstimatesBuild, EstimatesManage);
    /// <summary>Triage results can be read by anyone who views, assesses, reviews or manages triage rules.</summary>
    public static string TriageAny => AnyOf(TriageView, TriageAssess, TriageReview, TriageRules);
    /// <summary>Follow-up worklists can be read by anyone with a follow-up permission.</summary>
    public static string FollowUpAny => AnyOf(FollowUpView, FollowUpRecord, FollowUpPlans, FollowUpSsi, FollowUpReport);

    public static readonly IReadOnlyList<string> All =
    [
        PatientView, PatientRegister, PatientEdit, PatientMerge, PatientAlertsManage, BreakGlass, BreakGlassReview,
        AuditView, UsersManage, OrganisationManage, ModulesManage, IntegrationManage,
        LabsView, LabsAcknowledge, LabsMatch, TheatreView, TheatreBook, TheatreManage, FormsSend, FormsTriage,
        BillingView, BillingManage, BillingFees, RemindersView, RemindersManage, ReferralsView, ReferralsManage, ReferralsTriage, LettersView, LettersWrite, LettersTemplates, RosterView, RosterManage,
        CredentialingView, CredentialingManage, PortalSpecialist, PortalManage, MessagingView,
        AssetsView, AssetsManage, CssdProcess, CssdRelease, StockManage, PurchasingApprove, EmrView, EmrWrite, EmrControlledDrugs,
        EndoView, EndoWrite, EndoSign, EndoManage, ScribeUse, ScribeApprove, ScribeManage, DevicesUse, DevicesManage, QualityReport, QualityView, QualityManage, CodingCode, CodingSubmit, CodingManage, EstimatesBuild, EstimatesManage, TriageView, TriageAssess, TriageReview, TriageRules, TriageApprove, FollowUpView, FollowUpRecord, FollowUpPlans, FollowUpSsi, FollowUpReport, ResearchBuild, ResearchApprove, FormsDesign, FormsApprove, FormsFill, ClaimsView, ClaimsManage, ClaimsAdmin, PaymentsTake, IdentityLookup
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
    public const string QualityManager = "Quality Manager";
    public const string ClinicalCoder = "Clinical Coder";

    private static readonly string[] Clinical =
        [Permissions.QualityReport, Permissions.PatientView, Permissions.PatientEdit, Permissions.PatientAlertsManage, Permissions.BreakGlass,
         Permissions.LabsView, Permissions.LabsAcknowledge, Permissions.TheatreView];

    public static readonly IReadOnlyDictionary<string, (string Description, string[] Permissions)> Definitions =
        new Dictionary<string, (string, string[])>
        {
            [SystemAdministrator] = ("Manages users, organisation set-up and modules. No routine clinical access.",
                [Permissions.UsersManage, Permissions.OrganisationManage, Permissions.ModulesManage, Permissions.TheatreManage, Permissions.RemindersView, Permissions.RemindersManage,
                 Permissions.LettersTemplates, Permissions.CredentialingView, Permissions.CredentialingManage, Permissions.AssetsView, Permissions.AssetsManage, Permissions.EndoManage, Permissions.ScribeManage, Permissions.DevicesManage, Permissions.QualityReport, Permissions.CodingManage, Permissions.ClaimsAdmin]),
            [PrivacyOfficer] = ("Reviews break-glass use and audit reports; handles privacy requests; second-person check for identity corrections and merges.",
                [Permissions.AuditView, Permissions.BreakGlassReview, Permissions.PatientView, Permissions.PatientMerge, Permissions.QualityReport, Permissions.QualityView, Permissions.ResearchBuild, Permissions.ResearchApprove]),
            [Reception] = ("Registers patients and manages bookings.",
                [Permissions.PatientView, Permissions.PatientRegister, Permissions.PatientEdit, Permissions.TheatreView, Permissions.TheatreBook,
                 Permissions.TheatreManage, Permissions.FormsSend, Permissions.BillingView, Permissions.BillingManage, Permissions.LabsMatch, Permissions.RemindersView,
                 Permissions.ReferralsView, Permissions.ReferralsManage, Permissions.LettersView, Permissions.LettersWrite, Permissions.RosterView, Permissions.PortalManage, Permissions.QualityReport, Permissions.EstimatesBuild, Permissions.ClaimsView, Permissions.ClaimsManage, Permissions.PaymentsTake, Permissions.IdentityLookup]),
            [Nurse] = ("Clinical nursing access.", [.. Clinical, Permissions.IdentityLookup, Permissions.FormsFill, Permissions.TriageView, Permissions.TriageAssess, Permissions.FollowUpView, Permissions.FollowUpRecord, Permissions.PatientRegister, Permissions.TheatreBook, Permissions.FormsSend,
                 Permissions.FormsTriage, Permissions.LabsMatch, Permissions.RemindersView, Permissions.ReferralsView, Permissions.ReferralsManage,
                 Permissions.LettersView, Permissions.LettersWrite, Permissions.RosterView, Permissions.AssetsView, Permissions.CssdProcess, Permissions.StockManage, Permissions.EmrView, Permissions.EmrWrite, Permissions.EmrControlledDrugs, Permissions.EndoView, Permissions.EndoWrite, Permissions.ScribeUse, Permissions.DevicesUse]),
            [Anaesthetist] = ("Clinical access for anaesthetists.", [.. Clinical, Permissions.FormsFill, Permissions.FormsApprove, Permissions.FollowUpView, Permissions.TriageView, Permissions.TriageAssess, Permissions.TriageReview, Permissions.TriageRules, Permissions.TriageApprove, Permissions.EmrView, Permissions.EmrWrite, Permissions.EmrControlledDrugs, Permissions.EndoView, Permissions.ScribeUse, Permissions.DevicesUse, Permissions.FormsTriage, Permissions.ReferralsView, Permissions.LettersView, Permissions.RosterView, Permissions.PortalSpecialist]),
            [Surgeon] = ("Clinical access for surgeons and endoscopists.", [.. Clinical, Permissions.FormsFill, Permissions.FollowUpView, Permissions.FollowUpRecord, Permissions.TriageView, Permissions.EmrView, Permissions.EmrWrite, Permissions.EndoView, Permissions.EndoWrite, Permissions.EndoSign, Permissions.ScribeUse, Permissions.ScribeApprove, Permissions.TheatreBook, Permissions.ReferralsView, Permissions.ReferralsManage, Permissions.ReferralsTriage,
                 Permissions.LettersView, Permissions.LettersWrite, Permissions.PortalSpecialist]),
            [CssdTechnician] = ("Sterile services: trays, loads and traceability (patient view limited to traceability).",
                [Permissions.PatientView, Permissions.FormsFill, Permissions.RosterView, Permissions.AssetsView, Permissions.AssetsManage, Permissions.CssdProcess, Permissions.CssdRelease, Permissions.StockManage, Permissions.QualityReport]),
            [Finance] = ("Billing, invoices and payments; procedure estimates and the estimates price list.", [Permissions.PatientView, Permissions.BillingView, Permissions.BillingManage, Permissions.BillingFees, Permissions.AssetsView, Permissions.PurchasingApprove, Permissions.QualityReport, Permissions.EstimatesBuild, Permissions.EstimatesManage, Permissions.ClaimsView, Permissions.ClaimsManage, Permissions.ClaimsAdmin, Permissions.PaymentsTake, Permissions.IdentityLookup]),
            [PracticeAssistant] = ("Specialist's practice assistant: bookings for their specialist's patients.",
                [Permissions.PatientView, Permissions.PatientRegister, Permissions.TheatreView, Permissions.TheatreBook, Permissions.FormsSend, Permissions.LabsView,
                 Permissions.ReferralsView, Permissions.ReferralsManage, Permissions.LettersView, Permissions.LettersWrite, Permissions.PortalSpecialist]),
            [IntegrationAdministrator] = ("Manages interfaces, message flows, mappings and translation tables; sees message content (audited).",
                [Permissions.IntegrationManage, Permissions.MessagingView, Permissions.LettersView, Permissions.QualityReport]),
            [RosterManager] = ("Writes the staff roster and approves leave (no patient access).",
                [Permissions.RosterView, Permissions.RosterManage, Permissions.QualityReport]),
            [SpecialistPortal] = ("Visiting specialist or PA: sees only their own TDS patients' lists, appointments, waitlist and documents (read-only).",
                [Permissions.PortalSpecialist]),
            [QualityManager] = ("Runs the quality system: triages and investigates incidents, handles complaints, keeps the risk, hazard, action and policy registers and the quality indicators.",
                [Permissions.QualityReport, Permissions.QualityView, Permissions.QualityManage, Permissions.PatientView, Permissions.AssetsView, Permissions.FollowUpView, Permissions.FollowUpPlans, Permissions.FollowUpSsi, Permissions.FollowUpReport, Permissions.ResearchBuild, Permissions.FormsDesign, Permissions.FormsApprove, Permissions.FormsFill]),
            [ClinicalCoder] = ("Codes discharged events in ICD-10-AM/ACHI/ACS, prepares and sends the NMDS and NBRS files to Health NZ and corrects returned errors (reads the clinical record).",
                [Permissions.CodingCode, Permissions.CodingSubmit, Permissions.PatientView, Permissions.EmrView, Permissions.EndoView, Permissions.LabsView, Permissions.TheatreView, Permissions.ReferralsView, Permissions.QualityReport]),
        };
}
