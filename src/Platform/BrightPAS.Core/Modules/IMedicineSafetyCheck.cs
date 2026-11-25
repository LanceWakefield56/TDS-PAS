namespace BrightPAS.Core.Modules;

/// <summary>A medicine about to be charted as given in the clinical record (0.27.0, ADR 0040).</summary>
public sealed record MedicineGiveCheck(Guid PatientId, Guid EpisodeId, string Medicine, string Dose, string Route, DateTimeOffset At, string? Barcode, Guid? MedicineId,
    decimal? WeightKg, string? OverrideReason, string User);

/// <summary>One thing the check found: Kind (Allergy, Dose, Interaction, Duplicate, NotChecked), Severity (Stop, Warn, Info) and plain-English text.</summary>
public sealed record MedicineCheckNote(string Kind, string Severity, string Text);

/// <summary>Allowed = false only when the checks are enforced, something must stop and no override reason was given.</summary>
public sealed record MedicineCheckOutcome(bool Allowed, string? Error, Guid? CheckId, IReadOnlyList<MedicineCheckNote> Findings);

/// <summary>
/// Medicine safety checks run before a medicine is charted as given (implemented by the Medicines module through the web app, only when it is
/// licensed). The clinical record calls every registered check; the first refusal stops the charting.
/// </summary>
public interface IMedicineSafetyCheck
{
    Task<MedicineCheckOutcome> CheckAsync(MedicineGiveCheck check, CancellationToken ct);

    /// <summary>Links the recorded check to the medicine row that was saved.</summary>
    Task LinkAsync(Guid checkId, Guid medicationId, CancellationToken ct);
}
