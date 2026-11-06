namespace BrightPAS.Core.Setup;

/// <summary>
/// Single-row licence state: the installation's random id (part of its installation code), the installed signed licence,
/// the latest online activation token, and the last time the system ran (to detect the clock being wound back).
/// </summary>
public sealed class LicenceState
{
    public int Id { get; set; } = 1;
    public Guid InstallationId { get; set; } = Guid.NewGuid();
    public string? LicenceText { get; set; }
    public DateTimeOffset? LicenceInstalledAt { get; set; }
    public string? LicenceInstalledBy { get; set; }
    public string? ActivationText { get; set; }
    public DateTimeOffset? ActivationCheckedAt { get; set; }
    public string? ActivationError { get; set; }
    /// <summary>Comma-separated module ids from the last valid licence, so a later licence problem keeps their data viewable.</summary>
    public string? LastKnownModules { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}
