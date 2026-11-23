using System.Text.Json.Serialization;

namespace BrightPAS.Licensing;

/// <summary>
/// The signed content of a BrightPAS licence. Issued by the vendor's Licence Manager, verified by every installation.
/// Any change to this content breaks the signature.
/// </summary>
public sealed record LicenceDocument
{
    public int Format { get; init; } = 1;
    public required string LicenceId { get; init; }
    public required string Customer { get; init; }
    public string? Site { get; init; }
    /// <summary>Production, Evaluation or Sandbox.</summary>
    public string Edition { get; init; } = LicenceEditions.Production;
    public DateTimeOffset IssuedAt { get; init; }
    /// <summary>Last day the licence is valid (inclusive, New Zealand time).</summary>
    public DateOnly ExpiresOn { get; init; }
    /// <summary>The installation code this licence is locked to; null for an unbound sandbox/evaluation licence.</summary>
    public string? InstallationCode { get; init; }
    public int? MaxUsers { get; init; }
    /// <summary>Licensed module ids, or "*" for every module.</summary>
    public IReadOnlyList<string> Modules { get; init; } = [];
    /// <summary>Days after expiry during which modules keep working (with warnings) before becoming read-only.</summary>
    public int GraceDays { get; init; } = 14;
    public ActivationSettings? Activation { get; init; }

    public bool Covers(string moduleId) =>
        Modules.Any(m => m == "*" || m.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
}

public static class LicenceEditions
{
    public const string Production = "Production";
    public const string Evaluation = "Evaluation";
    public const string Sandbox = "Sandbox";
}

/// <summary>Optional online activation: the installation checks in with the vendor's licence server.</summary>
public sealed record ActivationSettings
{
    public required string Url { get; init; }
    /// <summary>How often the installation checks in.</summary>
    public int CheckInDays { get; init; } = 7;
    /// <summary>How long modules keep working when the licence server cannot be reached.</summary>
    public int OfflineGraceDays { get; init; } = 30;
}

/// <summary>Signed reply from the licence server confirming a licence is active on an installation.</summary>
public sealed record ActivationToken
{
    public int Format { get; init; } = 1;
    public required string LicenceId { get; init; }
    public required string InstallationCode { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ValidUntil { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ModuleAccess>))]
public enum ModuleAccess { None, ReadOnly, Full }

[JsonConverter(typeof(JsonStringEnumConverter<LicenceHealth>))]
public enum LicenceHealth { Missing, Valid, ExpiringSoon, InGrace, Expired, Invalid, WrongInstallation, ActivationOverdue, ClockProblem }

/// <summary>A product module that can be licensed. Modules not yet built are listed so licences can include them in advance.</summary>
public sealed record ProductModule(string Id, string Name, string Description, bool Available);

public static class ProductModules
{
    public static readonly IReadOnlyList<ProductModule> All =
    [
        new("labs", "Lab results", "HL7 lab and histology results, clinician inbox and acknowledgement.", true),
        new("theatre", "Theatre bookings", "Theatre lists, procedure bookings and the day's list.", true),
        new("referrals", "Referrals & waitlist", "Referral intake and triage, the surgical waitlist with target dates, and booking theatre from the waitlist.", true),
        new("forms", "Pre-admission forms", "Online patient health questionnaire and nurse triage.", true),
        new("billing", "Estimates & invoices", "Fee schedule, estimates, invoices, payments and fee splits.", true),
        new("triage", "Patient triage and suitability", "Risk scores (BMI, STOP-BANG, frailty, VTE, cardiac risk, suggested ASA), clinician-approved day-surgery suitability rules and medicine prompts, risk profile with recommendation and sign-off, re-triage after booking and a booking hold.", true),
        new("followup", "Post-op follow-up and SSI surveillance", "Follow-up plans and calendar, call scripts, patient check-in texts with alerts, escalations tracked to closure, SSI surveillance (CDC/HQSC) with GP letters, rates, SPC and HQSC export.", true),
        new("estimates", "Procedure estimates", "Patient cost estimates from the procedure price list (facility, surgeon, anaesthetist, consumables, add-ons, special packages, extended time), templates, warnings, the printed estimate, email with a hold period and right-patient checks, Incisive export and pricing spreadsheet imports.", true),
        new("emr", "Clinical record (eMR)", "Perioperative and nursing record: risk screens, Surgical Safety Checklist, intra-op record and counts, observations with early warning score, medicines and controlled drugs, blood products, notes and discharge.", true),
        new("rostering", "Rostering", "Staff rosters: shifts, regular shifts, leave and hours (replaces TeamUp).", true),
        new("assets", "Assets & CSSD", "Equipment register and maintenance, sterile instrument (AS/NZS 5369) and endoscope traceability, loan sets, stock, implants and purchasing.", true),
        new("quality", "Quality management", "Incidents (HQSC SAC, open disclosure), complaints to Right 10 timeframes, compliments, risk and hazard registers, corrective actions, policy register with read-and-acknowledge, quality indicators with SPC and an equity view, governance pack.", true),
        new("endoscopy", "Endoscopy", "Endoscopy reporting like EndoBASE: pictures and video from the Olympus tower (DICOM worklist or capture card), gastroscopy and colonoscopy terminology, polyps, tattoos, biopsies, histology, clinical and patient reports with pictures, quality indicators.", true),
        new("scribe", "BrightPAS Scribe", "AI dictation in every note field and an ambient scribe (with consent) that drafts notes from TDS templates; on-site or cloud engines; Medow Health option.", true),
        new("devices", "Devices & SaferSleep", "Bedside monitors (GE CARESCAPE) feeding vital signs for nurse validation into the chart, and the SaferSleep anaesthesia interface: patient and booking out, anaesthetic record (drugs, events, vital signs, PDF) back into the episode.", true),
        new("messaging", "Messaging & integration engine", "TDS's own integration engine: HL7, FHIR, DICOM, CDA/XML, X12 and REST interfaces; visual message flows, mappings and translation tables with a test harness; message store, retries, monitoring and alerts.", true),
        new("reminders", "SMS & email reminders", "Booking confirmations, reminders, questionnaire links and two-way SMS replies.", true),
        new("portal", "Specialist, PA & patient portals", "Read-only portal for specialists and their practice assistants; secure patient portal (appointments, requests, document upload).", true),
        new("credentialing", "Credentialing", "Clinician credentials and scope of practice, expiry alerts, and checks when booking theatre.", true),
        new("letters", "Letters & documents", "Letter templates, letters to patients, GPs and referrers, printing, and GP messaging via HealthLink.", true),
        new("reporting", "Coding & statutory reporting", "Clinical coding (ICD-10-AM/ACHI/ACS, NZ edition) with edit checks and advice, NMDS private hospital load file and NBRS batches to Health NZ with submission log and resubmission.", true),
        new("claims", "Claims & payments", "ACC and health insurer claims, Xero sync, EFTPOS and live NHI/HPI lookups (planned).", false),
    ];

    public static ProductModule? Find(string id) => All.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The result of checking a licence: overall health, per-module access, and plain-English messages.</summary>
public sealed record LicenceStatus(
    LicenceHealth Health,
    LicenceDocument? Licence,
    IReadOnlyDictionary<string, ModuleAccess> Modules,
    IReadOnlyList<string> Messages,
    bool SignedWithDevelopmentKey)
{
    public ModuleAccess AccessTo(string moduleId) =>
        Modules.TryGetValue(moduleId, out var a) ? a : ModuleAccess.None;

    public static LicenceStatus None(string message) =>
        new(LicenceHealth.Missing, null, new Dictionary<string, ModuleAccess>(), [message], false);
}
