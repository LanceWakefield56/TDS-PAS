using System.IO.Compression;
using BrightPAS.Setup.Install;

namespace BrightPAS.Setup.Tests;

/// <summary>0.5.5: re-installing over a running site failed with "Access to the path ... is denied".</summary>
public class WebFilesTests
{
    private static string MakeZip(params (string Name, string Text)[] files)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("bpas-zip").FullName, "web.zip");
        using var z = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (n, t) in files) { using var w = new StreamWriter(z.CreateEntry(n).Open()); w.Write(t); }
        return path;
    }

    [Fact]
    public async Task Overwrites_existing_read_only_files()
    {
        var target = Directory.CreateTempSubdirectory("bpas-web").FullName;
        var existing = Path.Combine(target, "a.dll");
        File.WriteAllText(existing, "old");
        File.SetAttributes(existing, FileAttributes.ReadOnly);
        var n = await WebFiles.ExtractWithRetryAsync(MakeZip(("a.dll", "new"), ("wwwroot/index.html", "<p>")), target, new InstallLog(null, false, console: false), CancellationToken.None);
        Assert.Equal(2, n);
        Assert.Equal("new", File.ReadAllText(existing));
        Assert.True(File.Exists(Path.Combine(target, "wwwroot", "index.html")));
    }

    [Fact]
    public async Task Refuses_paths_outside_the_install_folder()
    {
        var target = Directory.CreateTempSubdirectory("bpas-web").FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WebFiles.ExtractWithRetryAsync(MakeZip(("../evil.txt", "x")), target, new InstallLog(null, false, console: false), CancellationToken.None));
    }

    [Fact]
    public void Stop_script_takes_site_offline_and_waits_for_the_worker_process()
    {
        var s = PowerShellScripts.StopSiteForUpdate("BrightPAS", @"C:\Program Files\BrightPAS\Web");
        Assert.Contains("app_offline.htm", s);
        Assert.Contains("Stop-WebAppPool", s);
        Assert.Contains("w3wp.exe", s);
        Assert.Contains("Stop-Process", s);
    }
}
