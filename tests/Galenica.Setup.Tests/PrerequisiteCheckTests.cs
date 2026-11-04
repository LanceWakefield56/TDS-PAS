using Galenica.Setup.Checks;

namespace Galenica.Setup.Tests;

public class PrerequisiteCheckTests
{
    private sealed class FakeProbe : ISystemProbe
    {
        public bool IsWindows { get; init; } = true;
        public Version OsVersion { get; init; } = new(10, 0, 20348); // Windows Server 2022
        public string? WindowsInstallationType { get; init; } = "Server";
        public bool Is64Bit { get; init; } = true;
        public bool IsAdministrator { get; init; } = true;
        public long TotalMemoryBytes { get; init; } = 16 * PrerequisiteChecks.GiB;
        public long? FreeDisk { get; init; } = 100 * PrerequisiteChecks.GiB;
        public long? FreeDiskBytes(string drive) => FreeDisk;
        public int? IisMajorVersion { get; init; } = 10;
        public bool AspNetCoreModuleV2Installed { get; init; } = true;
        public IReadOnlyList<Version> AspNetCoreRuntimeVersions { get; init; } = [new(10, 0, 1)];
        public Dictionary<string, bool?> Tls { get; init; } = new() { ["SSL 3.0"] = false, ["TLS 1.0"] = false, ["TLS 1.1"] = false };
        public bool? TlsProtocolServerEnabled(string protocol) => Tls.GetValueOrDefault(protocol);
        public string TimeZoneId { get; init; } = "New Zealand Standard Time";
        public bool SqlReachable { get; init; } = true;
        public bool CanConnectTcp(string host, int port, TimeSpan timeout) => SqlReachable;
    }

    private static readonly SetupContext Ctx = new(@"C:\", "PAS-SQL");

    private static CheckReport Run(FakeProbe probe, SetupContext? ctx = null) =>
        CheckRunner.Run(PrerequisiteChecks.All(), ctx ?? Ctx, probe, TimeProvider.System);

    private static CheckResult ResultOf(CheckReport r, string name) => r.Outcomes.Single(o => o.Check.Name == name).Result;

    [Fact]
    public void Well_prepared_server_is_ready_with_no_warnings()
    {
        var r = Run(new FakeProbe());
        Assert.True(r.ReadyToInstall);
        Assert.Equal(0, r.Warnings);
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void Old_windows_fails()
    {
        var r = Run(new FakeProbe { OsVersion = new Version(10, 0, 14393) }); // Server 2016
        Assert.Equal(CheckStatus.Fail, ResultOf(r, "Windows Server version").Status);
        Assert.Equal(2, r.ExitCode);
    }

    [Fact]
    public void Client_windows_only_warns()
    {
        var r = Run(new FakeProbe { WindowsInstallationType = "Client" });
        Assert.Equal(CheckStatus.Warning, ResultOf(r, "Windows Server version").Status);
    }

    [Theory]
    [InlineData(4, CheckStatus.Fail)]
    [InlineData(8, CheckStatus.Warning)]
    [InlineData(32, CheckStatus.Pass)]
    public void Memory_thresholds(int gb, CheckStatus expected)
    {
        var r = Run(new FakeProbe { TotalMemoryBytes = gb * PrerequisiteChecks.GiB });
        Assert.Equal(expected, ResultOf(r, "Memory (RAM)").Status);
    }

    [Fact]
    public void Missing_iis_and_hosting_bundle_fail_with_instructions()
    {
        var r = Run(new FakeProbe { IisMajorVersion = null, AspNetCoreModuleV2Installed = false, AspNetCoreRuntimeVersions = [new Version(8, 0, 5)] });
        Assert.Equal(CheckStatus.Fail, ResultOf(r, "IIS (Internet Information Services)").Status);
        Assert.Equal(CheckStatus.Fail, ResultOf(r, ".NET 10 ASP.NET Core runtime").Status);
        Assert.False(string.IsNullOrEmpty(ResultOf(r, "ASP.NET Core Module for IIS").Fix));
    }

    [Fact]
    public void Tls_not_explicitly_disabled_warns()
    {
        var r = Run(new FakeProbe { Tls = new() { ["SSL 3.0"] = false } });
        var res = ResultOf(r, "Old TLS versions disabled");
        Assert.Equal(CheckStatus.Warning, res.Status);
        Assert.Contains("TLS 1.0", res.Detail);
    }

    [Fact]
    public void Unreachable_sql_server_fails_and_missing_name_is_skipped()
    {
        Assert.Equal(CheckStatus.Fail, ResultOf(Run(new FakeProbe { SqlReachable = false }), "SQL Server reachable").Status);
        Assert.Equal(CheckStatus.Skipped, ResultOf(Run(new FakeProbe(), new SetupContext(@"C:\", null)), "SQL Server reachable").Status);
    }

    [Fact]
    public void Windows_only_checks_skip_on_other_platforms()
    {
        var r = Run(new FakeProbe { IsWindows = false });
        Assert.Equal(CheckStatus.Skipped, ResultOf(r, "IIS (Internet Information Services)").Status);
        Assert.True(r.ReadyToInstall);
    }

    [Fact]
    public void Report_text_lists_every_check_and_the_verdict()
    {
        var r = Run(new FakeProbe { FreeDisk = 10 * PrerequisiteChecks.GiB });
        var text = r.ToText();
        Assert.Contains("Free disk space", text);
        Assert.Contains("NOT READY", text);
        Assert.Contains("What to do", text);
    }
}
