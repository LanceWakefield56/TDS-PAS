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
        new("emr", "Clinical record (eMR)", "Perioperative, anaesthetic and nursing records.", false),
        new("rostering", "Rostering", "Staff rosters, leave and shift swaps (replaces TeamUp).", false),
        new("assets", "Assets & CSSD", "Asset register and sterile instrument traceability.", false),
        new("quality", "Quality management", "Incidents, surveys, SSI, falls and quality reporting.", false),
        new("endoscopy", "Endoscopy", "Endoscopy reporting and scope tracking (replaces Endobase).", false),
        new("messaging", "Messaging & workflow engine", "HL7/FHIR/DICOM interfaces and workflows.", false),
        new("reminders", "SMS & email reminders", "Booking confirmations, reminders, questionnaire links and two-way SMS replies.", true),
        new("portal", "Specialist & PA portal", "Portal for specialists and practice assistants.", false),
        new("letters", "Letters & documents", "Letter templates and urgent transfer print.", false),
        new("reporting", "Coding & statutory reporting", "Clinical coding, ACC, Health NZ and MoH reporting.", false),
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
