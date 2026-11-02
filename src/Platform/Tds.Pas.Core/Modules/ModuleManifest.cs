namespace Tds.Pas.Core.Modules;

/// <summary>
/// Describes a PAS module: identity, version, database schema and the modules it depends on.
/// Every module (Scheduling, Billing, Workforce, ...) publishes one manifest.
/// </summary>
public sealed record ModuleManifest(
    string Id,
    string Name,
    Version Version,
    string Schema,
    IReadOnlyList<string> DependsOn,
    string Description = "")
{
    /// <summary>Id of the platform core module, which every other module depends on implicitly.</summary>
    public const string CoreId = "core";
}
