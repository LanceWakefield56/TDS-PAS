namespace Galenica.Setup.Checks;

/// <summary>
/// Reads facts about the server. Separated from the checks so the checks can be tested on any
/// machine with a fake probe; the real Windows implementation reads the registry and file system.
/// </summary>
public interface ISystemProbe
{
    bool IsWindows { get; }
    Version OsVersion { get; }
    /// <summary>"Server", "Server Core" or "Client" on Windows; null elsewhere.</summary>
    string? WindowsInstallationType { get; }
    bool Is64Bit { get; }
    bool IsAdministrator { get; }
    long TotalMemoryBytes { get; }
    long? FreeDiskBytes(string drive);
    int? IisMajorVersion { get; }
    bool AspNetCoreModuleV2Installed { get; }
    IReadOnlyList<Version> AspNetCoreRuntimeVersions { get; }
    /// <summary>Server-side enabled state of a TLS protocol: true/false if configured, null if not set (OS default).</summary>
    bool? TlsProtocolServerEnabled(string protocol);
    string TimeZoneId { get; }
    bool CanConnectTcp(string host, int port, TimeSpan timeout);
}
