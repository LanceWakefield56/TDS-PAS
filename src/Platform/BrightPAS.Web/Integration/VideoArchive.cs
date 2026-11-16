using ICSharpCode.SharpZipLib.Zip;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Setup;
using BrightPAS.Modules.Endoscopy;
using BrightPAS.Modules.Patients;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Integration;

/// <summary>Video retention settings as the screen shows them.</summary>
public sealed record VideoRetention(int? Days, bool ArchiveFirst, string? Folder, bool HasPassword, int Overdue, int Archived);

/// <summary>
/// Endoscopy video retention and archive (0.20.0): videos are kept 30 days by default (TDS decision, Nov 2026), then deleted from the
/// server. Before deleting, each video is written to the archive folder (a network share or a USB drive on the server) as an AES-256
/// encrypted, password-protected ZIP named by date, MRN and media id. If the archive folder or password is not set, or the copy fails,
/// the video is kept and the settings page says how many are waiting. Staff can also archive one video at any time.
/// </summary>
public sealed class VideoArchive(SettingsService settings, IDataProtectionProvider dp, EndoDbContext db, MediaStore store, PatientsDbContext patients, EndoService endo,
    AuditWriter audit, TimeProvider clock)
{
    public const string FolderKey = "endo.videoArchiveFolder", SecretKey = "endo.videoArchiveSecret", ArchiveFirstKey = "endo.videoArchiveFirst";
    public const int DefaultDays = 30;
    private readonly IDataProtector _p = dp.CreateProtector("BrightPAS.Endoscopy.VideoArchive.v1");

    /// <summary>Retention days: blank = the default 30, "never" = keep videos on the server.</summary>
    public async Task<int?> DaysAsync(CancellationToken ct = default)
    {
        var v = await settings.GetAsync(EndoscopyEndpointsRetention.Key, ct);
        if (string.Equals(v, "never", StringComparison.OrdinalIgnoreCase)) return null;
        return int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= 30 ? d : DefaultDays;
    }

    private async Task<string?> PasswordAsync(CancellationToken ct)
    {
        var p = await settings.GetAsync(SecretKey, ct);
        if (string.IsNullOrEmpty(p)) return null;
        try { return _p.Unprotect(p); }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    public async Task<VideoRetention> StatusAsync(CancellationToken ct = default)
    {
        var days = await DaysAsync(ct);
        var cutoff = clock.GetUtcNow().AddDays(-(days ?? 36500));
        var overdue = days is null ? 0 : await db.Media.CountAsync(m => m.Kind == MediaKind.Video && m.Status == MediaStatus.Ready && m.CapturedAt < cutoff, ct);
        return new(days, await settings.GetAsync(ArchiveFirstKey, ct) != "false", await settings.GetAsync(FolderKey, ct) is { Length: > 0 } f ? f : null,
            await PasswordAsync(ct) is not null, overdue, await db.Media.CountAsync(m => m.Kind == MediaKind.Video && m.ArchivedAt != null, ct));
    }

    public async Task<IReadOnlyList<string>> SaveAsync(string? days, bool archiveFirst, string? folder, string? newPassword, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var d = (days ?? "").Trim();
        if (d.Length > 0 && !d.Equals("never", StringComparison.OrdinalIgnoreCase) && (!int.TryParse(d, out var n) || n < 30 || n > 3650)) errors.Add("Keep videos 30–3650 days, or 'never' to keep them on the server.");
        if (folder is { Length: > 0 } f && !Path.IsPathFullyQualified(f.Trim())) errors.Add("The archive folder must be a full path – a network share (\\\\server\\endoscopy-archive) or a drive (E:\\Archive).");
        var pwProblem = newPassword is { Length: > 0 } ? SecureExportService.PasswordProblem(newPassword, (await settings.SecurityAsync(ct)).MinExportPasswordLength) : null;
        if (pwProblem is not null) errors.Add(pwProblem);
        if (errors.Count > 0) return errors;
        await settings.SetAsync(EndoscopyEndpointsRetention.Key, d.ToLowerInvariant(), user, ct);
        await settings.SetAsync(ArchiveFirstKey, archiveFirst ? "true" : "false", user, ct);
        await settings.SetAsync(FolderKey, folder?.Trim() ?? "", user, ct);
        if (newPassword is { Length: > 0 }) await settings.SetAsync(SecretKey, _p.Protect(newPassword), user, ct);
        await audit.WriteAsync(user, "Update", "VideoRetention", "endoscopy", reason: $"keep {(d.Length == 0 ? DefaultDays.ToString(System.Globalization.CultureInfo.InvariantCulture) : d)} days; archive first: {archiveFirst}; folder {folder}{(newPassword is { Length: > 0 } ? "; archive password changed" : "")}", ct: ct);
        return [];
    }

    /// <summary>Streams a file into an AES-256 encrypted ZIP (written as .partial, then renamed, so a half-written archive is never mistaken for a good one).</summary>
    public static async Task WriteEncryptedZipAsync(string source, string entryName, string target, string password, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var partial = target + ".partial";
        await using (var output = File.Create(partial))
        await using (var zip = new ZipOutputStream(output) { IsStreamOwner = false })
        {
            zip.SetLevel(0); // video is already compressed
            zip.Password = password;
            var entry = new ZipEntry(ZipEntry.CleanName(entryName)) { DateTime = DateTimeOffset.UtcNow.ToNz().DateTime, AESKeySize = 256, Size = new FileInfo(source).Length };
            zip.PutNextEntry(entry);
            await using (var input = File.OpenRead(source)) await input.CopyToAsync(zip, ct);
            zip.CloseEntry();
            zip.Finish();
        }
        File.Move(partial, target, overwrite: true);
    }

    /// <summary>Copies one video to the archive folder (or <paramref name="folderOverride"/>, e.g. a USB drive) as an encrypted ZIP.</summary>
    public async Task<(string? Path, string? Error)> ArchiveAsync(Guid mediaId, string? folderOverride, string user, CancellationToken ct = default)
    {
        var m = await db.Media.SingleOrDefaultAsync(x => x.Id == mediaId && x.Kind == MediaKind.Video && x.Status == MediaStatus.Ready, ct);
        if (m is null) return (null, "Video not found.");
        var folder = folderOverride is { Length: > 0 } fo ? fo.Trim() : await settings.GetAsync(FolderKey, ct);
        if (string.IsNullOrWhiteSpace(folder)) return (null, "Set the archive folder (Endoscopy › Settings › Video retention) first.");
        if (!Path.IsPathFullyQualified(folder)) return (null, "The archive folder must be a full path.");
        var password = await PasswordAsync(ct);
        if (password is null) return (null, "Set the archive password (Endoscopy › Settings › Video retention) first.");
        var source = await store.PathIfExistsAsync(m, ct);
        if (source is null) return (null, "The video file is missing from the media folder.");
        var mrn = m.PatientId is { } pid ? await patients.Patients.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Mrn).SingleOrDefaultAsync(ct) : null;
        var stamp = m.CapturedAt.ToNz().ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture);
        var name = $"{stamp}_{mrn ?? "unmatched"}_{m.Id.ToString("N")[..8]}";
        var target = Path.Combine(folder, m.CapturedAt.ToNz().ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture), name + ".zip");
        try { await WriteEncryptedZipAsync(source, name + Path.GetExtension(source), target, password, ct); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await audit.WriteAsync(user, "ArchiveFailed", "EndoscopyMedia", m.Id.ToString(), m.PatientId?.ToString(), reason: ex.Message, ct: ct);
            return (null, $"Could not write to the archive folder: {ex.Message}");
        }
        m.ArchivedAt = clock.GetUtcNow(); m.ArchivedTo = target.Length > 400 ? target[..400] : target;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Archive", "EndoscopyMedia", m.Id.ToString(), m.PatientId?.ToString(), reason: $"AES-256 encrypted copy written to {target}", ct: ct);
        return (target, null);
    }

    /// <summary>The 6-hourly clean-up: archive (when set to) then delete videos older than the retention period. Returns (deleted, kept because archiving failed).</summary>
    public async Task<(int Deleted, int Kept)> RunAsync(CancellationToken ct = default)
    {
        var days = await DaysAsync(ct);
        if (days is null) return (0, 0);
        var archiveFirst = await settings.GetAsync(ArchiveFirstKey, ct) != "false";
        var cutoff = clock.GetUtcNow().AddDays(-days.Value);
        var old = await db.Media.AsNoTracking().Where(m => m.Kind == MediaKind.Video && m.Status == MediaStatus.Ready && m.CapturedAt < cutoff)
            .Select(m => new { m.Id, m.ArchivedAt }).Take(200).ToListAsync(ct);
        int deleted = 0, kept = 0;
        foreach (var v in old)
        {
            if (archiveFirst && v.ArchivedAt is null && (await ArchiveAsync(v.Id, null, "system", ct)).Error is not null) { kept++; continue; }
            if (await endo.DeleteMediaAsync(v.Id, $"Video retention ({days} days){(archiveFirst ? " – encrypted copy in the archive" : "")}", "system", ct) is null) deleted++;
        }
        return (deleted, kept);
    }
}

/// <summary>The retention setting key (shared with the endoscopy settings screen).</summary>
public static class EndoscopyEndpointsRetention
{
    public const string Key = "endo.videoRetentionDays";
}
