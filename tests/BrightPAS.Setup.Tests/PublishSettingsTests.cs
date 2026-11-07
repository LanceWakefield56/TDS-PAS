using System.Xml.Linq;

namespace BrightPAS.Setup.Tests;

/// <summary>
/// Regression guard for 0.5.1: a trimmed Setup.exe crashed with TypeLoadException inside SqlClient's Windows
/// native SNI interop when opening the first encrypted connection. Linux test runs use the managed SNI and
/// cannot catch that, so the publish settings themselves are asserted here.
/// </summary>
public class PublishSettingsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BrightPAS.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [Fact]
    public void Setup_is_not_trimmed()
    {
        var csproj = XDocument.Load(Path.Combine(RepoRoot(), "src", "Installer", "BrightPAS.Setup", "BrightPAS.Setup.csproj"));
        var trimmed = csproj.Descendants("PublishTrimmed").Select(e => e.Value.Trim()).ToList();
        Assert.All(trimmed, v => Assert.Equal("false", v, ignoreCase: true));
        Assert.Empty(csproj.Descendants("TrimMode"));
    }

    [Fact]
    public void Release_script_publishes_setup_untrimmed()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "build-release.sh"));
        var line = script.Split('\n').Single(l => l.Contains("dotnet publish src/Installer/BrightPAS.Setup"));
        Assert.Contains("-p:PublishTrimmed=false", line);
    }
}
