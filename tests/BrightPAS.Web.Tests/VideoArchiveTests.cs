using ICSharpCode.SharpZipLib.Zip;
using BrightPAS.Web.Integration;

namespace BrightPAS.Web.Tests;

/// <summary>0.20.0 endoscopy video archive: the copy written before a video is deleted is an AES-256 ZIP that needs the archive password.</summary>
public class VideoArchiveTests
{
    [Fact]
    public async Task Archive_is_a_streamed_aes256_zip_that_needs_the_password()
    {
        var dir = Directory.CreateTempSubdirectory("bp-archive-");
        try
        {
            var video = Path.Combine(dir.FullName, "video.mp4");
            var content = new byte[3 * 1024 * 1024 + 17];
            new Random(5).NextBytes(content);
            await File.WriteAllBytesAsync(video, content);
            var target = Path.Combine(dir.FullName, "2026", "20261109-1000_TDS0000001_abcd1234.zip");
            await VideoArchive.WriteEncryptedZipAsync(video, "20261109-1000_TDS0000001_abcd1234.mp4", target, "Archive-Pass-2026", CancellationToken.None);
            Assert.True(File.Exists(target));
            Assert.False(File.Exists(target + ".partial"));
            using (var zip = new ZipFile(target) { Password = "Archive-Pass-2026" })
            {
                var e = zip.GetEntry("20261109-1000_TDS0000001_abcd1234.mp4");
                Assert.True(e.IsCrypted); Assert.Equal(256, e.AESKeySize);
                using var ms = new MemoryStream();
                await zip.GetInputStream(e).CopyToAsync(ms);
                Assert.Equal(content, ms.ToArray());
            }
            using var wrong = new ZipFile(target) { Password = "wrong-password" };
            Assert.ThrowsAny<Exception>(() => { using var s = wrong.GetInputStream(wrong.GetEntry("20261109-1000_TDS0000001_abcd1234.mp4")); s.ReadByte(); });
        }
        finally { dir.Delete(true); }
    }
}
