using ICSharpCode.SharpZipLib.Zip;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Tests;

public class SecureExportTests
{
    [Fact]
    public void Protected_export_is_an_aes256_zip_that_needs_the_password()
    {
        var bytes = SecureExportService.Zip([new("waitlist.csv", "NHI,Name\nZSC21TN,NGATAI"u8.ToArray())], "Tasman-Export-2026");
        using (var ok = new ZipFile(new MemoryStream(bytes)) { Password = "Tasman-Export-2026" })
        {
            var e = ok.GetEntry("waitlist.csv");
            Assert.True(e.IsCrypted);
            Assert.Equal(256, e.AESKeySize);
            using var r = new StreamReader(ok.GetInputStream(e));
            Assert.Contains("ZSC21TN", r.ReadToEnd());
        }
        Assert.DoesNotContain("ZSC21TN", System.Text.Encoding.UTF8.GetString(bytes));
        using var wrong = new ZipFile(new MemoryStream(bytes)) { Password = "wrong-password!" };
        Assert.ThrowsAny<Exception>(() => new StreamReader(wrong.GetInputStream(wrong.GetEntry("waitlist.csv"))).ReadToEnd());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("short", false)]
    [InlineData("aaaaaaaaaaaa", false)]
    [InlineData("Brightwater-Theatre-1", true)]
    public void Export_password_rules(string? pw, bool ok) => Assert.Equal(ok, SecureExportService.PasswordProblem(pw, 10) is null);
}
