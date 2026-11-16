using BrightPAS.Core;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Setup;
using BrightPAS.Licensing;
using BrightPAS.Messaging;
using BrightPAS.Modules.Assets;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Steriliser and washer cycle records from files (0.18.0): the machine's USB export or the network folder it writes to. Each record becomes a
/// load waiting for release (trays are added at the release screen as before). The machine is found from the record (TDS's steriliser writes its
/// name, e.g. "Sterilizer 1", which must match the equipment name, asset number or serial number) or the machine chosen on screen. A record already
/// imported (same machine and cycle number) is not imported again.
/// </summary>
public static class CssdLogImport
{
    public const string FolderKey = "cssd.logFolder";

    public static async Task<ImportSummary> ImportAsync(IServiceProvider sp, string fileName, byte[] bytes, string user, Guid? machineId, CancellationToken ct = default)
    {
        var name = FileImportService.Clean(fileName);
        IReadOnlyList<FileItem> files;
        try { files = MessageFile.Expand(name, bytes); }
        catch (InvalidDataException ex) { return ImportSummary.From([new(name, "Rejected", ex.Message, null)]); }
        var db = sp.GetRequiredService<AssetsDbContext>();
        var svc = sp.GetRequiredService<CssdService>();
        var machines = await db.Equipment.AsNoTracking().Where(e => e.Category == EquipmentCategory.Steriliser || e.Category == EquipmentCategory.Washer || e.Category == EquipmentCategory.Aer).ToListAsync(ct);
        var results = new List<ImportItemResult>();
        foreach (var f in files)
        {
            var text = MessageFile.Text(f.Bytes);
            if (text.Length > 200_000) { results.Add(new(f.Name, "Rejected", "Larger than a cycle record (200 KB of text).", null)); continue; }
            var record = SteriliserLog.Parse(text);
            var p = CssdService.ParseMachineLog(text);
            var machine = record?.Machine is { Length: > 0 } mn
                ? machines.FirstOrDefault(m => string.Equals(m.Name, mn, StringComparison.OrdinalIgnoreCase) || string.Equals(m.AssetNumber, mn, StringComparison.OrdinalIgnoreCase) || string.Equals(m.SerialNumber, mn, StringComparison.OrdinalIgnoreCase))
                : null;
            machine ??= machineId is { } mid ? machines.FirstOrDefault(m => m.Id == mid) : null;
            if (machine is null) { results.Add(new(f.Name, "Rejected", record?.Machine is { } n ? $"No machine called “{n}” in the equipment register – give the steriliser that name (or choose the machine) and import again." : "Choose the machine this log is from.", null)); continue; }
            if (p.StartedAt is null) { results.Add(new(f.Name, "Rejected", "The start time was not found – enter this load by hand.", null)); continue; }
            if (p.MachineCycleNumber is { } cn && await db.Cycles.AnyAsync(c => c.MachineId == machine.Id && c.MachineCycleNumber == cn, ct))
            { results.Add(new(f.Name, "Duplicate", $"Cycle {cn} on {machine.Name} is already a load.", null)); continue; }
            var kind = machine.Category switch { EquipmentCategory.Washer => CycleKind.Wash, EquipmentCategory.Aer => CycleKind.Aer, _ => CycleKind.Steam };
            var edit = new CycleEdit(machine.Id, kind, p.MachineCycleNumber, p.Program, p.StartedAt.Value, p.EndedAt, p.MaxTemperatureC, p.HoldSeconds, p.PressureKpa, p.A0, p.Passed == true,
                kind == CycleKind.Wash ? IndicatorResult.NotUsed : IndicatorResult.Pending, IndicatorResult.NotUsed, false, [], []);
            var (cycle, errors) = await svc.CreateCycleAsync(edit, Clip(f.Name, 100), text, user, ct, contentsLater: true);
            results.Add(cycle is null ? new(f.Name, "Rejected", string.Join(" ", errors), null)
                : new(f.Name, "Accepted", $"Load {cycle.LoadNumber} ({machine.Name}, cycle {p.MachineCycleNumber}) – {(p.Passed == true ? "parameters met" : "CHECK: " + string.Join(" ", p.Notes.Where(n => n.Contains("TOO", StringComparison.Ordinal) || n.Contains("NOT", StringComparison.Ordinal) || n.Contains("CHECK", StringComparison.Ordinal) || n.Contains("OUT", StringComparison.Ordinal))))}. Scan its trays onto it, then release.", null));
        }
        var summary = ImportSummary.From(results);
        await sp.GetRequiredService<AuditWriter>().WriteAsync(user, "ImportFile", "CssdLog", name, reason: $"{summary.Accepted} load(s), {summary.Duplicates} duplicate, {summary.Rejected} rejected", ct: ct);
        return summary;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
    private static readonly TimeZoneInfo Nz = Find();
    private static TimeZoneInfo Find() { try { return TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland"); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.Local; } }
}

/// <summary>Watches the folder the steriliser and washer write their cycle records to (CSSD settings) and imports them every minute.</summary>
public sealed class CssdLogWatcher(IServiceScopeFactory scopes, LicenceCache licence, ILogger<CssdLogWatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (licence.Status.AccessTo(AssetsModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var dir = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(CssdLogImport.FolderKey, stoppingToken);
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
                foreach (var file in Directory.EnumerateFiles(dir).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".csv" or ".txt" or ".log" or ".dat" or ".zip").Take(100))
                {
                    await using var s2 = scopes.CreateAsyncScope();
                    var r = await CssdLogImport.ImportAsync(s2.ServiceProvider, Path.GetFileName(file), await File.ReadAllBytesAsync(file, stoppingToken), "cssd log folder", null, stoppingToken);
                    var target = Path.Combine(dir, r.Rejected == 0 ? "processed" : "failed");
                    Directory.CreateDirectory(target);
                    File.Move(file, Path.Combine(target, $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Path.GetFileName(file)}"), overwrite: true);
                    if (r.Rejected > 0) log.LogWarning("CSSD log folder: {File} not imported: {Why}", file, string.Join(" ", r.Items.Select(i => i.Detail)));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException) { log.LogWarning(ex, "CSSD log folder could not be read; trying again in a minute."); }
        }
    }
}
