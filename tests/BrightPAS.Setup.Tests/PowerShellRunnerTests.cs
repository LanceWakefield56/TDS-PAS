using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Tests;

/// <summary>
/// 0.5.2: scripts piped to "powershell -Command -" silently skipped multi-line blocks and still exited 0, so no
/// install script ever ran. Scripts now run from a .ps1 file and must print a completion marker.
/// </summary>
public class PowerShellRunnerTests
{
    private static bool PwshAvailable()
    {
        if (OperatingSystem.IsWindows()) return true;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator).Any(d => File.Exists(Path.Combine(d, "pwsh")));
    }

    [Fact]
    public void Exit_zero_without_marker_is_a_failure()
    {
        var r = ProcessRunner.Interpret(0, "");
        Assert.NotEqual(0, r.ExitCode);
        Assert.Contains("completion marker missing", r.Output);
    }

    [Fact]
    public void Marker_is_required_and_stripped_from_output()
    {
        var r = ProcessRunner.Interpret(0, "hello\n" + ProcessRunner.CompletionMarker + "\n");
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("hello", r.Output);
    }

    [Fact]
    public void Wrapper_prints_marker_only_on_success()
    {
        var w = ProcessRunner.Wrap("'x'");
        Assert.True(w.IndexOf(ProcessRunner.CompletionMarker, StringComparison.Ordinal) > w.IndexOf("} catch {", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Multi_line_script_really_runs()
    {
        if (!PwshAvailable()) return; // PowerShell not installed on this test machine
        var r = await new ProcessRunner().RunPowerShellAsync("$x = 2\nif ($x -eq 2) {\n  'block ran'\n}\nforeach ($i in 1..2) { \"item $i\" }", CancellationToken.None);
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("block ran", r.Output);
        Assert.Contains("item 2", r.Output);
    }

    [Fact]
    public async Task Script_error_gives_exit_code_1_with_details()
    {
        if (!PwshAvailable()) return;
        var r = await new ProcessRunner().RunPowerShellAsync("$ErrorActionPreference = 'Stop'\nthrow 'boom'", CancellationToken.None);
        Assert.Equal(1, r.ExitCode);
        Assert.Contains("ERROR: boom", r.Output);
        Assert.DoesNotContain(ProcessRunner.CompletionMarker, r.Output);
    }

    [Fact]
    public void App_pool_script_waits_for_the_virtual_account()
    {
        var s = AppDatabaseLoginStep.AppPoolScript("BrightPAS");
        Assert.Contains("New-WebAppPool", s);
        Assert.Contains("Start-WebAppPool", s);
        Assert.Contains("IIS APPPOOL\\$pool", s);
        Assert.Contains("SecurityIdentifier", s);
        Assert.DoesNotContain("\\\\", s); // no doubled backslashes in PowerShell paths
    }
}
