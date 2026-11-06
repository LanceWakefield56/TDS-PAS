namespace BrightPAS.Core.Setup;

/// <summary>Single-row table tracking installation state, including the one-time first-admin code.</summary>
public sealed class SetupState
{
    public int Id { get; set; } = 1;
    public string? BootstrapCodeHash { get; set; }
    public DateTimeOffset? BootstrapCodeCreatedAt { get; set; }
    public DateTimeOffset? BootstrapCodeUsedAt { get; set; }
    public string? InstalledVersion { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }

    /// <summary>Bootstrap codes expire after 7 days if unused.</summary>
    public static readonly TimeSpan BootstrapLifetime = TimeSpan.FromDays(7);
}
