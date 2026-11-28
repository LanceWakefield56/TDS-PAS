using System.Globalization;
using System.Security.Cryptography;
using BrightPAS.Core.Setup;

namespace BrightPAS.Modules.Pacs;

/// <summary>
/// Image files live in the imaging folder (setting imaging.storagePath; Setup's Imaging:StoragePath or C:\ProgramData\BrightPAS\Imaging on Windows).
/// Each DICOM object is kept exactly as received under yyyy\MM\{image id}.dcm – names are made here, never taken from the file – with a SHA-256 in the row.
/// </summary>
public sealed class ImagingStore(SettingsService settings, Microsoft.Extensions.Configuration.IConfiguration? config = null)
{
    public const string PathKey = "imaging.storagePath";

    public string DefaultRootPath => config?["Imaging:StoragePath"] is { Length: > 0 } c ? c : DefaultRoot;

    public static string DefaultRoot => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrightPAS", "Imaging")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".brightpas", "imaging");

    public async Task<string> RootAsync(CancellationToken ct = default) => await settings.GetAsync(PathKey, ct) is { Length: > 0 } p ? p : DefaultRootPath;

    private async Task<string> FullAsync(string fileName, CancellationToken ct)
    {
        var root = Path.GetFullPath(await RootAsync(ct));
        var full = Path.GetFullPath(Path.Combine(root, fileName));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Image path is outside the imaging folder.");
        return full;
    }

    public async Task WriteAsync(Image img, byte[] data, CancellationToken ct = default)
    {
        img.FileName = Path.Combine(img.ReceivedAt.UtcDateTime.ToString("yyyy", CultureInfo.InvariantCulture), img.ReceivedAt.UtcDateTime.ToString("MM", CultureInfo.InvariantCulture), img.Id.ToString("N") + ".dcm");
        var full = await FullAsync(img.FileName, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, data, ct);
        img.Bytes = data.LongLength;
        img.Sha256 = Convert.ToHexString(SHA256.HashData(data));
    }

    public async Task<string?> PathIfExistsAsync(Image img, CancellationToken ct = default)
    {
        if (img.FileName.Length == 0) return null;
        var full = await FullAsync(img.FileName, ct);
        return File.Exists(full) ? full : null;
    }

    public async Task<byte[]?> ReadAsync(Image img, CancellationToken ct = default) => await PathIfExistsAsync(img, ct) is { } p ? await File.ReadAllBytesAsync(p, ct) : null;

    public async Task DeleteAsync(Image img, CancellationToken ct = default)
    {
        if (await PathIfExistsAsync(img, ct) is { } p) File.Delete(p);
    }

    /// <summary>Set-up check: the folder exists (or can be made) and the website can write to it.</summary>
    public async Task<(bool Ok, string Detail)> CheckAsync(CancellationToken ct = default)
    {
        var root = await RootAsync(ct);
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".write-test-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;
            return (true, $"{root} – {free / 1024 / 1024 / 1024} GB free");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return (false, $"{root}: {ex.Message}"); }
    }
}
