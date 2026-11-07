using BrightPAS.Setup.Checks;
using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Tests;

/// <summary>
/// 0.5.3: on the sandbox the .NET runtime was installed but the ASP.NET Core Module was not in IIS (Hosting Bundle
/// installed before IIS). Setup said "already done" because only a registry key was checked. It must now repair.
/// </summary>
public class HostingBundleTests
{
    private sealed class Probe : ISystemProbe
    {
        public bool ModuleAfterRepair { get; set; }
        public bool Module { get; set; }
        public bool IsWindows => true;
        public Version OsVersion => new(10, 0, 26100);
        public string? WindowsInstallationType => "Server";
        public bool Is64Bit => true;
        public bool IsAdministrator => true;
        public long TotalMemoryBytes => 32 * PrerequisiteChecks.GiB;
        public long? FreeDiskBytes(string drive) => 64 * PrerequisiteChecks.GiB;
        public int? IisMajorVersion => 10;
        public bool AspNetCoreModuleV2Installed => Module;
        public IReadOnlyList<Version> AspNetCoreRuntimeVersions => [new(10, 0, 14)];
        public bool? TlsProtocolServerEnabled(string protocol) => false;
        public string TimeZoneId => "New Zealand Standard Time";
        public bool CanConnectTcp(string host, int port, TimeSpan timeout) => true;
    }

    private sealed class Runner(Probe probe) : IProcessRunner
    {
        public List<string> Calls { get; } = [];
        public Task<ProcessResult> RunPowerShellAsync(string script, CancellationToken ct) { Calls.Add("ps"); return Task.FromResult(new ProcessResult(0, "IIS restarted.")); }
        public Task<ProcessResult> RunAsync(string fileName, string arguments, CancellationToken ct)
        {
            Calls.Add(Path.GetFileName(fileName) + " " + arguments);
            probe.Module = probe.ModuleAfterRepair;
            return Task.FromResult(new ProcessResult(0, ""));
        }
    }

    private static (InstallContext Ctx, Runner Runner) Make(Probe probe, bool withInstaller)
    {
        var folder = Directory.CreateTempSubdirectory("bpas-hb").FullName;
        if (withInstaller) File.WriteAllText(Path.Combine(folder, "dotnet-hosting-10.0.14-win.exe"), "x");
        var runner = new Runner(probe);
        var ctx = new InstallContext(new InstallOptions { SqlServer = "localhost" }, probe, runner, new InstallLog(null, false, console: false), TimeProvider.System, setupFolder: folder);
        return (ctx, runner);
    }

    [Fact]
    public async Task Runtime_without_module_is_repaired()
    {
        var probe = new Probe { Module = false, ModuleAfterRepair = true };
        var (ctx, runner) = Make(probe, withInstaller: true);
        var r = await new HostingBundleStep().RunAsync(ctx, CancellationToken.None);
        Assert.Equal(StepOutcome.Done, r.Outcome);
        Assert.Contains(runner.Calls, c => c.StartsWith("dotnet-hosting-10.0.14-win.exe /repair /quiet", StringComparison.Ordinal));
        Assert.Contains("repaired", r.Message);
    }

    [Fact]
    public async Task Repair_that_does_not_register_the_module_fails()
    {
        var probe = new Probe { Module = false, ModuleAfterRepair = false };
        var (ctx, _) = Make(probe, withInstaller: true);
        var r = await new HostingBundleStep().RunAsync(ctx, CancellationToken.None);
        Assert.Equal(StepOutcome.Failed, r.Outcome);
        Assert.Contains("still not registered", r.Message);
    }

    [Fact]
    public async Task Missing_module_without_installer_explains_how_to_repair()
    {
        var probe = new Probe { Module = false };
        var (ctx, runner) = Make(probe, withInstaller: false);
        var r = await new HostingBundleStep().RunAsync(ctx, CancellationToken.None);
        Assert.Equal(StepOutcome.Failed, r.Outcome);
        Assert.Contains("Repair", r.Message);
        Assert.Contains("iisreset", r.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Runtime_and_module_present_is_already_done()
    {
        var probe = new Probe { Module = true };
        var (ctx, runner) = Make(probe, withInstaller: true);
        var r = await new HostingBundleStep().RunAsync(ctx, CancellationToken.None);
        Assert.Equal(StepOutcome.AlreadyDone, r.Outcome);
        Assert.Empty(runner.Calls);
    }
}

/// <summary>0.5.4: ANCM V2 is found through its IIS registration, not a fixed folder (it lives under Program Files\IIS).</summary>
public class AncmDetectionTests
{
    private const string Config = """
        <configuration><system.webServer><globalModules>
          <add name="UriCacheModule" image="%windir%\System32\inetsrv\cachuri.dll" />
          <add name="AspNetCoreModuleV2" image="%ProgramFiles%\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll" />
        </globalModules></system.webServer></configuration>
        """;

    [Fact]
    public void Finds_the_registered_image_under_program_files()
    {
        var path = AncmImage.ImagePath(Config)!;
        Assert.EndsWith(@"IIS\Asp.Net Core Module\V2\aspnetcorev2.dll", path);
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain("%ProgramFiles%", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registered_and_present_means_installed()
    {
        Assert.True(AncmImage.IsRegistered(Config, _ => true));
        Assert.False(AncmImage.IsRegistered(Config, _ => false));
    }

    [Fact]
    public void Not_registered_means_not_installed()
    {
        Assert.False(AncmImage.IsRegistered("<configuration><system.webServer><globalModules /></system.webServer></configuration>", _ => true));
    }
}
