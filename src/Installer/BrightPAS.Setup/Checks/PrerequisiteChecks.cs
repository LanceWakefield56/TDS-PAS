namespace BrightPAS.Setup.Checks;

/// <summary>The standard set of platform prerequisite checks, in display order.</summary>
public static class PrerequisiteChecks
{
    public const long GiB = 1024L * 1024 * 1024;
    public static readonly Version MinimumWindowsServer = new(10, 0, 17763); // Windows Server 2019
    public const int RequiredAspNetCoreMajor = 10;

    public static IReadOnlyList<IPrerequisiteCheck> All() =>
    [
        new Check("Server", "Windows Server version", (_, p) =>
        {
            if (!p.IsWindows) return CheckResult.Skip("Not running on Windows (development machine).");
            if (p.OsVersion < MinimumWindowsServer)
                return CheckResult.Fail($"Windows version {p.OsVersion} is too old.", "Use Windows Server 2019 or later (2022/2025 recommended).");
            var type = p.WindowsInstallationType ?? "Unknown";
            return type.StartsWith("Server", StringComparison.OrdinalIgnoreCase)
                ? CheckResult.Pass($"Windows {type} {p.OsVersion}.")
                : CheckResult.Warn($"This is a Windows {type} edition, not Windows Server.", "Install on Windows Server for production. A client edition is acceptable only for testing.");
        }),
        new Check("Server", "64-bit operating system", (_, p) =>
            p.Is64Bit ? CheckResult.Pass("64-bit.") : CheckResult.Fail("32-bit operating system.", "Use a 64-bit edition of Windows Server.")),
        new Check("Server", "Running as administrator", (_, p) =>
        {
            if (!p.IsWindows) return CheckResult.Skip("Not running on Windows.");
            return p.IsAdministrator ? CheckResult.Pass("Running with administrator rights.")
                : CheckResult.Fail("Setup is not running as administrator.", "Right-click Setup and choose 'Run as administrator'.");
        }),
        new Check("Server", "Memory (RAM)", (_, p) =>
        {
            var gb = p.TotalMemoryBytes / (double)GiB;
            if (gb >= 16) return CheckResult.Pass($"{gb:0.#} GB available.");
            if (gb >= 8) return CheckResult.Warn($"{gb:0.#} GB available.", "16 GB is recommended for the PAS application server (fine for testing).");
            return CheckResult.Fail($"{gb:0.#} GB available.", "Allocate at least 8 GB (16 GB recommended).");
        }),
        new Check("Server", "Free disk space", (c, p) =>
        {
            var free = p.FreeDiskBytes(c.InstallDrive);
            if (free is null) return CheckResult.Fail($"Drive {c.InstallDrive} was not found.", "Choose an existing drive with --drive.");
            var gb = free.Value / (double)GiB;
            if (gb >= 40) return CheckResult.Pass($"{gb:0.#} GB free on {c.InstallDrive}.");
            if (gb >= 20) return CheckResult.Warn($"{gb:0.#} GB free on {c.InstallDrive}.", "40 GB or more is recommended for logs, updates and documents.");
            return CheckResult.Fail($"{gb:0.#} GB free on {c.InstallDrive}.", "Free up space or add a larger virtual disk (at least 20 GB).");
        }),
        new Check("Server", "Time zone", (_, p) =>
            p.TimeZoneId is "New Zealand Standard Time" or "Pacific/Auckland"
                ? CheckResult.Pass("New Zealand time zone.")
                : CheckResult.Warn($"Time zone is '{p.TimeZoneId}'.", "Set the server time zone to (UTC+12:00) Auckland, Wellington.")),
        new Check("Web server", "IIS (Internet Information Services)", (_, p) =>
        {
            if (!p.IsWindows) return CheckResult.Skip("Not running on Windows.");
            return p.IisMajorVersion is >= 10 ? CheckResult.Pass($"IIS {p.IisMajorVersion} installed.")
                : CheckResult.Fail("IIS is not installed.", "Install the Web Server (IIS) role. A later Setup step can do this for you.");
        }),
        new Check("Web server", ".NET 10 ASP.NET Core runtime", (_, p) =>
        {
            if (!p.IsWindows) return CheckResult.Skip("Not running on Windows.");
            var v = p.AspNetCoreRuntimeVersions.Where(x => x.Major == RequiredAspNetCoreMajor).Max();
            return v is not null ? CheckResult.Pass($"ASP.NET Core {v} installed.")
                : CheckResult.Fail("ASP.NET Core 10 runtime not found.", "Install the .NET 10 Hosting Bundle (includes the IIS module).");
        }),
        new Check("Web server", "ASP.NET Core Module for IIS", (_, p) =>
        {
            if (!p.IsWindows) return CheckResult.Skip("Not running on Windows.");
            return p.AspNetCoreModuleV2Installed ? CheckResult.Pass("ASP.NET Core Module V2 installed.")
                : CheckResult.Fail("ASP.NET Core Module V2 is not registered in IIS.", "Install the .NET 10 Hosting Bundle after IIS is installed. If it is already installed, it was probably installed before IIS: put 'dotnet-hosting-10.x.x-win.exe' next to Setup.exe and Setup will repair it, or run it yourself and choose Repair, then run 'iisreset'.");
        }),
        new Check("Security", "Old TLS versions disabled", (_, p) =>
        {
            if (!p.IsWindows) return CheckResult.Skip("Not running on Windows.");
            var open = new[] { "SSL 3.0", "TLS 1.0", "TLS 1.1" }.Where(x => p.TlsProtocolServerEnabled(x) != false).ToList();
            return open.Count == 0 ? CheckResult.Pass("SSL 3.0, TLS 1.0 and TLS 1.1 are disabled.")
                : CheckResult.Warn($"Not explicitly disabled: {string.Join(", ", open)}.", "The security hardening step will disable these (only TLS 1.2 and 1.3 allowed).");
        }),
        new Check("Database", "SQL Server reachable", (c, p) =>
        {
            if (string.IsNullOrWhiteSpace(c.SqlServerHost)) return CheckResult.Skip("No SQL Server specified (use --sql-server).");
            return p.CanConnectTcp(c.SqlServerHost, c.SqlServerPort, TimeSpan.FromSeconds(5))
                ? CheckResult.Pass($"Connected to {c.SqlServerHost}:{c.SqlServerPort}.")
                : CheckResult.Fail($"Cannot reach {c.SqlServerHost}:{c.SqlServerPort}.", "Check: (1) the 'SQL Server (MSSQLSERVER)' service is Running (open services.msc); (2) TCP/IP is enabled – SQL Server Configuration Manager → SQL Server Network Configuration → Protocols for MSSQLSERVER → TCP/IP → Enable, then restart the SQL Server service; (3) on a separate SQL server, the firewall allows TCP 1433.");
        }),
    ];

    private sealed class Check(string category, string name, Func<SetupContext, ISystemProbe, CheckResult> run) : IPrerequisiteCheck
    {
        public string Name => name;
        public string Category => category;
        public CheckResult Run(SetupContext context, ISystemProbe probe)
        {
            try { return run(context, probe); }
            catch (Exception ex) { return CheckResult.Fail($"The check could not run: {ex.Message}", "Re-run Setup as administrator; if it persists, send the report file to support."); }
        }
    }
}
