using Galenica.Setup.Install;
using Galenica.Setup.Support;

namespace Galenica.Setup.Tests;

public class DiagnosticsTests
{
    [Theory]
    [InlineData("Server=x;User ID=a;Password=S3cret!;Encrypt=True", "Server=x;User ID=a;Password=********;Encrypt=True")]
    [InlineData("\"Main\": \"Server=x;pwd=abc\"", "\"Main\": \"Server=x;pwd=********\"")]
    [InlineData("Integrated Security=True", "Integrated Security=True")]
    public void Masks_passwords(string input, string expected) => Assert.Equal(expected, Diagnostics.Mask(input));

    [Fact]
    public void Scripts_quote_site_names()
    {
        var scripts = Diagnostics.Scripts(new WebDeployOptions { AppPoolName = "O'Brien" }).ToList();
        Assert.Equal(2, scripts.Count);
        Assert.Contains("O''Brien", scripts[1].Script);
    }

    [Fact]
    public async Task Collects_a_zip_without_windows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "galdiag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "appsettings.Production.json"), "{\"ConnectionStrings\":{\"Main\":\"Server=x;Password=hunter2\"}}");
        var log = new InstallLog(null, false, console: false);
        var zip = await Diagnostics.CollectAsync(new ProcessRunner(), log, new WebDeployOptions { InstallDir = dir });
        Assert.NotNull(zip);
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip!);
        var entry = archive.Entries.Single(e => e.FullName == "web-app/appsettings.Production.json");
        using var reader = new StreamReader(entry.Open());
        var text = reader.ReadToEnd();
        Assert.DoesNotContain("hunter2", text);
        Assert.Contains("README.txt", archive.Entries.Select(e => e.FullName));
    }
}
