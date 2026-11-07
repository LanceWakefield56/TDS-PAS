namespace BrightPAS.Core.Setup;

/// <summary>
/// Records that a licensed module has been through its guided set-up (see ADR 0014).
/// A licensed module without a row here shows as "Needs set-up" and is hidden from non-administrators' menus.
/// </summary>
public sealed class ModuleSetupState
{
    public string ModuleId { get; set; } = "";
    public DateTimeOffset SetUpAt { get; set; }
    public string SetUpBy { get; set; } = "";
    /// <summary>Checks that were not complete but accepted by the administrator (optional steps skipped).</summary>
    public string? SkippedChecks { get; set; }
}
