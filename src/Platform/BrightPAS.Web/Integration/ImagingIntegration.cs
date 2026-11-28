using BrightPAS.Licensing;
using BrightPAS.Modules.Pacs;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

public sealed record ImagingFolderStatus(string? Folder, string? Problem, DateTimeOffset? LastRun, DateTimeOffset? LastImport, int Stored, int Failed);

/// <summary>
/// Imaging (PACS) watched folder (0.37.0, ADR 0050): every minute, DICOM files the X-ray/scanning machine or a radiology provider saved to the
/// share (loose files, files in sub-folders such as a copied CD, or ZIPs) are stored exactly as an upload would be, then moved to processed\ or failed\.
/// Files changed in the last 30 seconds are left for the next run (they may still be copying). This is the fallback that stays when a
/// vendor link (DICOM C-STORE) is added later.
/// </summary>
public sealed class ImagingFolderWorker(IServiceScopeFactory scopes, ILogger<ImagingFolderWorker> log, TimeProvider clock) : BackgroundService
{
    private readonly object _gate = new();
    private ImagingFolderStatus _status = new(null, null, null, null, 0, 0);
    private readonly SemaphoreSlim _nudge = new(0);
    public ImagingFolderStatus Status { get { lock (_gate) return _status; } }
    private void Update(Func<ImagingFolderStatus, ImagingFolderStatus> f) { lock (_gate) _status = f(_status); }
    public void Nudge() => _nudge.Release();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { }, CancellationToken.None);
        while (!ct.IsCancellationRequested)
        {
            try { await RunOnceAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Imaging watched folder could not be read."); }
            await _nudge.WaitAsync(TimeSpan.FromMinutes(1), ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        if (sp.GetRequiredService<LicenceCache>().Status.AccessTo(ImagingModule.Id) != ModuleAccess.Full) return;
        var svc = sp.GetRequiredService<ImagingService>();
        var dir = (await svc.SettingsAsync(ct)).ImportFolder;
        Update(x => x with { Folder = dir, LastRun = clock.GetUtcNow(), Problem = null });
        if (dir is null) return;
        if (!Directory.Exists(dir)) { Update(x => x with { Problem = $"{dir} not found or not readable by the BrightPAS service account." }); return; }
        var cutoff = clock.GetUtcNow().UtcDateTime.AddSeconds(-30);
        var processed = Path.Combine(dir, "processed");
        var failed = Path.Combine(dir, "failed");
        var files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 })
            .Where(f => !f.StartsWith(processed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !f.StartsWith(failed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.GetLastWriteTimeUtc(f) < cutoff && !Path.GetFileName(f).StartsWith('.'))
            .Take(500).ToList();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(dir, file);
            var ok = true;
            try
            {
                var bytes = await File.ReadAllBytesAsync(file, ct);
                await using var s2 = scopes.CreateAsyncScope();
                var results = await ImportAsync(rel, bytes, ImageSource.Folder, $"watched folder ({rel})", s2.ServiceProvider.GetRequiredService<ImagingService>(), ct);
                var stored = results.Count(r => r.Outcome == "Accepted");
                // Not-DICOM files (e.g. the CD's viewer program, readme) and the DICOMDIR are not failures; a broken DICOM file is.
                ok = results.All(r => r.Outcome != "Failed");
                Update(x => x with { Stored = x.Stored + stored, Failed = x.Failed + (ok ? 0 : 1), LastImport = stored > 0 ? clock.GetUtcNow() : x.LastImport });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                log.LogWarning("Imaging watched folder: {File} not imported: {Reason}", rel, ex.Message);
                ok = false;
            }
            try
            {
                var target = Path.Combine(ok ? processed : failed, Path.GetDirectoryName(rel) ?? "");
                Directory.CreateDirectory(target);
                File.Move(file, Path.Combine(target, $"{clock.GetUtcNow():yyyyMMddHHmmss}-{Path.GetFileName(file)}"), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning("Imaging watched folder: could not move {File}: {Reason}", rel, ex.Message); }
        }
        // Tidy empty sub-folders left by a copied CD.
        foreach (var sub in Directory.EnumerateDirectories(dir).Where(d => !string.Equals(d, processed, StringComparison.OrdinalIgnoreCase) && !string.Equals(d, failed, StringComparison.OrdinalIgnoreCase)))
            try { if (!Directory.EnumerateFileSystemEntries(sub, "*", SearchOption.AllDirectories).Any(e => File.Exists(e))) Directory.Delete(sub, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogDebug("Imaging watched folder: could not remove {Dir}: {Reason}", sub, ex.Message); }
    }

    /// <summary>
    /// Stores every DICOM object in one file (or ZIP). Outcomes: Accepted, Duplicate (already held), Rejected (not a DICOM picture – e.g. a DICOMDIR,
    /// a viewer program or a text file on a CD), Failed (a DICOM file that could not be stored).
    /// </summary>
    public static async Task<List<ImportItemResult>> ImportAsync(string name, byte[] bytes, ImageSource source, string who, ImagingService svc, CancellationToken ct = default)
    {
        var results = new List<ImportItemResult>();
        IReadOnlyList<BrightPAS.Messaging.FileItem> items;
        try { items = BrightPAS.Messaging.MessageFile.Expand(name, bytes); }
        catch (InvalidDataException ex) { return [new(name, "Failed", $"Could not open the file: {ex.Message}", null)]; }
        foreach (var f in items)
        {
            if (!BrightPAS.Messaging.MessageFile.IsDicom(f.Bytes)) { results.Add(new(f.Name, "Rejected", "Not a DICOM file – skipped.", null)); continue; }
            var r = await svc.StoreAsync(f.Bytes, source, who, ct);
            results.Add(r.Duplicate ? new(f.Name, "Duplicate", "Already held.", null)
                : r.Stored ? new(f.Name, "Accepted", r.Filed ? "Filed to the patient." : "Stored – waiting in To match.", null)
                : new(f.Name, r.NotUnderstood && r.Error?.StartsWith("DICOMDIR", StringComparison.Ordinal) == true ? "Rejected" : "Failed", r.Error, null));
        }
        return results;
    }
}
