using System.Xml.Linq;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

namespace BrightPAS.Setup.Checks;

/// <summary>Cross-platform facts shared by both probes.</summary>
public abstract class SystemProbeBase : ISystemProbe
{
    public abstract bool IsWindows { get; }
    public Version OsVersion => Environment.OSVersion.Version;
    public virtual string? WindowsInstallationType => null;
    public bool Is64Bit => Environment.Is64BitOperatingSystem;
    public abstract bool IsAdministrator { get; }
    public long TotalMemoryBytes => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    public virtual int? IisMajorVersion => null;
    public virtual bool AspNetCoreModuleV2Installed => false;
    public virtual IReadOnlyList<Version> AspNetCoreRuntimeVersions => [];
    public virtual bool? TlsProtocolServerEnabled(string protocol) => null;
    public string TimeZoneId => TimeZoneInfo.Local.Id;

    public long? FreeDiskBytes(string drive)
    {
        try { return new DriveInfo(drive).AvailableFreeSpace; }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return null; }
    }

    public bool CanConnectTcp(string host, int port, TimeSpan timeout)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync(host, port).Wait(timeout) && client.Connected;
        }
        catch (Exception ex) when (ex is SocketException or AggregateException or ArgumentException) { return false; }
    }

    public static ISystemProbe Create() => OperatingSystem.IsWindows() ? new WindowsSystemProbe() : new NonWindowsSystemProbe();
}

public sealed class NonWindowsSystemProbe : SystemProbeBase
{
    public override bool IsWindows => false;
    public override bool IsAdministrator => false;
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemProbe : SystemProbeBase
{
    public override bool IsWindows => true;

    public override bool IsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public override string? WindowsInstallationType =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType", null) as string;

    public override int? IisMajorVersion =>
        Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\InetStp", "MajorVersion", null) as int?;

    public override bool AspNetCoreModuleV2Installed
    {
        get
        {
            // 0.5.4: the module is installed when IIS's applicationHost.config registers AspNetCoreModuleV2 as a
            // global module AND the DLL it points to exists (normally %ProgramFiles%\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll).
            var appHost = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "inetsrv", "config", "applicationHost.config");
            try { return File.Exists(appHost) && AncmImage.IsRegistered(File.ReadAllText(appHost)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
    }

    public override IReadOnlyList<Version> AspNetCoreRuntimeVersions
    {
        get
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.AspNetCore.App");
            if (!Directory.Exists(dir)) return [];
            return Directory.GetDirectories(dir)
                .Select(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : null)
                .OfType<Version>().OrderBy(v => v).ToList();
        }
    }

    public override bool? TlsProtocolServerEnabled(string protocol)
    {
        var value = Registry.GetValue(
            $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols\{protocol}\Server",
            "Enabled", null);
        return value is int i ? i != 0 : null;
    }
}

/// <summary>Reads the ASP.NET Core Module V2 global-module registration from applicationHost.config.</summary>
public static class AncmImage
{
    public static string? ImagePath(string applicationHostConfig)
    {
        var doc = XDocument.Parse(applicationHostConfig);
        var add = doc.Descendants("globalModules").Elements("add")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), "AspNetCoreModuleV2", StringComparison.OrdinalIgnoreCase));
        var image = (string?)add?.Attribute("image");
        return image is null ? null : Environment.ExpandEnvironmentVariables(image);
    }

    public static bool IsRegistered(string applicationHostConfig, Func<string, bool>? fileExists = null)
    {
        var path = ImagePath(applicationHostConfig);
        return path is not null && (fileExists ?? File.Exists)(path);
    }
}
