using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Licensing;
using BrightPAS.Messaging;
using BrightPAS.Modules.Devices;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Messaging;
using BrightPAS.Web.Licensing;
using MsgStatus = BrightPAS.Modules.Messaging.MessageStatus;

namespace BrightPAS.Web.Integration;

/// <summary>What an imported file is for: the module that would normally receive it over the network.</summary>
public enum ImportKind { Labs, Monitors, SaferSleep }

public sealed record ImportItemResult(string Name, string Outcome, string? Detail, Guid? PatientId);

public sealed record ImportSummary(int Messages, int Accepted, int Duplicates, int Rejected, IReadOnlyList<ImportItemResult> Items)
{
    public static ImportSummary From(IReadOnlyList<ImportItemResult> items) => new(items.Count, items.Count(i => i.Outcome == "Accepted"),
        items.Count(i => i.Outcome == "Duplicate"), items.Count(i => i.Outcome is "Rejected" or "Failed"), items);
}

/// <summary>
/// Offline import (0.18.0, ADR 0030): when a device or interface connection is down, staff import the messages from a file instead
/// (a USB stick, a network share or a file the sending system saved). Every message goes the same way a live one would:
/// through the messaging engine's inbound interface for that module when there is one (so it is stored, viewable, acknowledged and
/// runs the flows), otherwise straight to the module. Duplicates already received live are recognised and not filed twice.
/// </summary>
public sealed class FileImportService(IServiceProvider sp, MessagingService messaging, LicenceCache licence, AuditWriter audit)
{
    public const long MaxFileBytes = 200L * 1024 * 1024;

    private static InboundHandler HandlerFor(ImportKind k) => k switch
    {
        ImportKind.Labs => InboundHandler.LabResults,
        ImportKind.Monitors => InboundHandler.DeviceObservations,
        _ => InboundHandler.AnaesthesiaRecord,
    };

    /// <summary>Imports one uploaded file (HL7 text, possibly many messages, MLLP-framed or batched; or a ZIP of such files).</summary>
    public async Task<ImportSummary> ImportAsync(ImportKind kind, string fileName, byte[] bytes, string user, CancellationToken ct = default)
    {
        var name = Clean(fileName);
        var results = new List<ImportItemResult>();
        IReadOnlyList<FileItem> files;
        try { files = MessageFile.Expand(name, bytes); }
        catch (InvalidDataException ex) { return ImportSummary.From([new(name, "Rejected", ex.Message, null)]); }

        // Prefer the messaging engine's interface for this module, so the message is stored and visible like a live one.
        Interface? iface = null;
        if (licence.Status.AccessTo(MessagingModule.Id) == ModuleAccess.Full)
            iface = (await messaging.ActiveAsync(Transport.MllpListen, Transport.FolderWatch, Transport.HttpReceive)).OrderBy(i => i.Transport).FirstOrDefault(i => i.Handler == HandlerFor(kind));

        foreach (var f in files)
        {
            var hl7 = MessageFile.SplitHl7(MessageFile.Text(f.Bytes));
            if (hl7.Count == 0) { results.Add(new(f.Name, "Rejected", "No HL7 messages found in this file (expected text starting with MSH).", null)); continue; }
            var n = 0;
            foreach (var msg in hl7)
            {
                n++;
                var label = hl7.Count == 1 ? f.Name : $"{f.Name} #{n}";
                results.Add(iface is not null ? await ViaEngineAsync(iface, msg, label, user, ct) : await DirectAsync(kind, msg, label, user, ct));
            }
        }
        var summary = ImportSummary.From(results);
        await audit.WriteAsync(user, "ImportFile", kind.ToString(), name,
            reason: $"{summary.Messages} message(s): {summary.Accepted} accepted, {summary.Duplicates} duplicate, {summary.Rejected} rejected{(iface is null ? "" : $" (interface {iface.Code})")}", ct: ct);
        return summary;
    }

    private async Task<ImportItemResult> ViaEngineAsync(Interface i, string msg, string label, string user, CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var r = await scope.ServiceProvider.GetRequiredService<MessagingPipeline>().ReceiveAsync(i, msg, $"file:{Clip(label, 60)} by {user}", null, ct);
        var outcome = r.Status == MsgStatus.Processed ? (r.Error?.StartsWith("Duplicate", StringComparison.Ordinal) == true ? "Duplicate" : "Accepted") : r.Status.ToString();
        var patient = await scope.ServiceProvider.GetRequiredService<MsgDbContext>().Messages.AsNoTracking().Where(m => m.Id == r.MessageId).Select(m => m.PatientId).SingleOrDefaultAsync(ct);
        return new(label, outcome, r.Error, patient);
    }

    private async Task<ImportItemResult> DirectAsync(ImportKind kind, string msg, string label, string user, CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var s = scope.ServiceProvider;
        switch (kind)
        {
            case ImportKind.Labs:
                var l = await s.GetRequiredService<LabService>().IngestAsync(msg, "File", $"file:{Clip(label, 60)} by {user}", ct);
                return new(label, l.Outcome switch { LabMessageOutcome.Rejected => "Rejected", LabMessageOutcome.Duplicate => "Duplicate", _ => "Accepted" }, l.Error, null);
            case ImportKind.Monitors:
                var d = await s.GetRequiredService<DeviceService>().ImportAsync(msg, ct);
                return new(label, d.Accepted ? "Accepted" : "Rejected", d.Error ?? (d.Accepted ? $"{d.Stored} reading(s) kept from monitor {d.Monitor}." : null), d.PatientId);
            default:
                var a = await s.GetRequiredService<AnaesthesiaService>().IngestAsync(msg, null, $"file:{Clip(label, 60)} by {user}", ct);
                return new(label, !a.Accepted ? "Rejected" : a.Duplicate ? "Duplicate" : "Accepted",
                    a.Error ?? (a.Accepted && !a.Duplicate && !a.Filed ? "Received but not matched to an episode – file it under Devices › SaferSleep." : null), a.PatientId);
        }
    }

    /// <summary>Imports one file into a chosen inbound interface (messaging administrators – any format the interface takes).</summary>
    public async Task<ImportSummary> ImportToInterfaceAsync(Interface i, string fileName, byte[] bytes, string user, CancellationToken ct = default)
    {
        var name = Clean(fileName);
        if (i.Direction != InterfaceDirection.Inbound) return ImportSummary.From([new(name, "Rejected", "Files can only be imported into an inbound interface.", null)]);
        IReadOnlyList<FileItem> files;
        try { files = MessageFile.Expand(name, bytes); }
        catch (InvalidDataException ex) { return ImportSummary.From([new(name, "Rejected", ex.Message, null)]); }
        var results = new List<ImportItemResult>();
        foreach (var f in files)
        {
            IReadOnlyList<string> contents;
            try
            {
                contents = i.Format switch
                {
                    MessageFormat.Hl7v2 => MessageFile.SplitHl7(MessageFile.Text(f.Bytes)),
                    MessageFormat.Dicom => MessageFile.IsDicom(f.Bytes) ? [DicomConvert.ToJson(f.Bytes)] : [],
                    _ => [MessageFile.Text(f.Bytes)],
                };
            }
            catch (Exception ex) when (ex is MappingException or InvalidOperationException or FellowOakDicom.DicomDataException)
            {
                results.Add(new(f.Name, "Rejected", ex.Message, null)); continue;
            }
            if (contents.Count == 0) { results.Add(new(f.Name, "Rejected", $"Nothing in this file looks like {i.Format}.", null)); continue; }
            var n = 0;
            foreach (var c in contents)
            {
                n++;
                if (Encoding.UTF8.GetByteCount(c) > MessagingService.MaxContentBytes) { results.Add(new($"{f.Name} #{n}", "Rejected", "Message larger than 5 MB.", null)); continue; }
                results.Add(await ViaEngineAsync(i, c, contents.Count == 1 ? f.Name : $"{f.Name} #{n}", user, ct));
            }
        }
        var summary = ImportSummary.From(results);
        await audit.WriteAsync(user, "ImportFile", "Interface", i.Id.ToString(), reason: $"{name} into {i.Code}: {summary.Accepted} accepted, {summary.Duplicates} duplicate, {summary.Rejected} rejected", ct: ct);
        return summary;
    }

    /// <summary>The file name as shown and logged: no folders (a browser or share may send a path), no control characters, at most 120 characters.</summary>
    public static string Clean(string? fileName)
    {
        var raw = fileName ?? "";
        try { raw = Uri.UnescapeDataString(raw); } catch (UriFormatException) { } // the browser sends the name URL-encoded (HTTP headers are ASCII)
        var n = Path.GetFileName(raw.Replace('\\', '/'));
        n = new string(n.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return n.Length == 0 ? "file" : Clip(n, 120);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
