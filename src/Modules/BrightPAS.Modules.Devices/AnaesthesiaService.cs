using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Messaging.Hl7;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Devices;

public sealed record RecordResult(bool Accepted, bool Duplicate, string? Error, Guid? RecordId, Guid? PatientId, bool Filed);

/// <summary>
/// SaferSleep interface: receives the anaesthetic record (HL7 ORU), matches it to the episode (visit number BrightPAS sent, else NHI and date),
/// keeps drugs / events / vital signs, files the PDF to the patient's documents, a signed anaesthetic note and (optionally) the medicines into the
/// eMR medicines list. Corrections replace the earlier filing. Records that cannot be matched wait under Devices › SaferSleep to be filed by hand.
/// </summary>
public sealed class AnaesthesiaService(DevicesDbContext db, EmrDbContext emr, PatientsDbContext patients, EmrService emrService, DocumentService documents, AuditWriter audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo Nz = TryZone("Pacific/Auckland");
    private static TimeZoneInfo TryZone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; } }

    public async Task<RecordResult> IngestAsync(string raw, Guid? messageId, string receivedBy, CancellationToken ct = default)
    {
        SaferSleepRecord r;
        try { r = DeviceHl7.ParseSaferSleep(raw, Nz); }
        catch (Hl7Exception ex) { return new(false, false, ex.Message, null, null, false); }
        if (await db.Records.AsNoTracking().SingleOrDefaultAsync(x => x.ControlId == r.ControlId, ct) is { } dup) return new(true, true, null, dup.Id, dup.PatientId, dup.EpisodeId is not null);

        Episode? ep = null;
        if (Guid.TryParseExact(r.VisitNumber ?? "", "N", out var eid) || Guid.TryParse(r.VisitNumber ?? "", out eid))
            ep = await emr.Episodes.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eid, ct);
        Guid? patientId = ep?.PatientId;
        if (patientId is null && r.Nhi is { } nhi)
            patientId = await patients.Patients.AsNoTracking().Where(p => p.Nhi == nhi && p.MergedIntoId == null).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (ep is not null && r.Nhi is { } n2 && await patients.Patients.AsNoTracking().Where(p => p.Id == ep.PatientId).Select(p => p.Nhi).SingleOrDefaultAsync(ct) is { } epNhi && !string.Equals(epNhi, n2, StringComparison.OrdinalIgnoreCase))
            return new(false, false, $"The visit number belongs to a different patient than NHI {n2} – not filed. Check SaferSleep.", null, null, false);
        if (ep is null && patientId is { } pid)
        {
            var day = r.Start is { } st ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(st, Nz).DateTime) : DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var cands = await emr.Episodes.AsNoTracking().Where(e => e.PatientId == pid && e.AdmittedAt != null).ToListAsync(ct);
            var sameDay = cands.Where(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.AdmittedAt!.Value, Nz).DateTime) == day).ToList();
            if (sameDay.Count == 1) ep = sameDay[0];
        }

        var rec = new AnaestheticRecord
        {
            EpisodeId = ep?.Id,
            PatientId = patientId,
            CaseNumber = r.CaseNumber,
            ControlId = r.ControlId,
            IsCorrection = r.IsCorrection,
            Nhi = r.Nhi,
            Anaesthetist = r.Anaesthetist,
            StartAt = r.Start,
            EndAt = r.End,
            Asa = r.Asa,
            Technique = r.Technique,
            Airway = r.Airway,
            Comments = r.Comments,
            MessageId = messageId,
            ReceivedAt = clock.GetUtcNow(),
        };
        foreach (var d in r.Drugs) rec.Drugs.Add(new AnaestheticDrug { At = d.At, Name = d.Name, Dose = d.Dose, Unit = d.Unit, Route = d.Route, Rate = d.Rate, Code = d.Code });
        foreach (var (at, text) in r.Events) rec.Events.Add(new AnaestheticEvent { At = at, Text = text });
        foreach (var v in r.Vitals) rec.Vitals.Add(new AnaestheticVital { At = v.At, Code = v.Code, Value = v.Value, Unit = v.Unit });
        db.Records.Add(rec);
        var previous = await db.Records.Where(x => x.CaseNumber == r.CaseNumber && x.SupersededById == null && x.Id != rec.Id).ToListAsync(ct);
        foreach (var p in previous) p.SupersededById = rec.Id;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(receivedBy, "Receive", "AnaestheticRecord", rec.Id.ToString(), patientId?.ToString(), reason: $"SaferSleep case {r.CaseNumber}{(r.IsCorrection ? " (correction)" : "")}{(ep is null ? " – unmatched" : "")}", ct: ct);
        var filed = false;
        if (ep is not null)
        {
            await FileAsync(rec, r.Pdf, previous.OrderByDescending(p => p.ReceivedAt).FirstOrDefault(), receivedBy, ct);
            filed = true;
        }
        return new(true, false, null, rec.Id, patientId, filed);
    }

    /// <summary>Files a matched record: PDF to documents, signed anaesthetic note (amending the previous one on correction), medicines to the eMR.</summary>
    private async Task FileAsync(AnaestheticRecord rec, byte[]? pdf, AnaestheticRecord? previous, string user, CancellationToken ct)
    {
        var ep = await emr.Episodes.AsNoTracking().SingleAsync(e => e.Id == rec.EpisodeId, ct);
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(ct) ?? new DeviceSettings();
        var by = $"SaferSleep{(rec.Anaesthetist is null ? "" : " – " + rec.Anaesthetist)}";
        if (pdf is not null)
        {
            var (doc, _) = await documents.AddAsync(new NewPatientDocument(ep.PatientId, ep.BookingId, DocumentCategory.Clinical,
                $"Anaesthetic record (SaferSleep){(rec.IsCorrection ? " – corrected" : "")} – {Local(rec.StartAt ?? rec.ReceivedAt):d MMM yyyy}", $"anaesthetic-record-{rec.CaseNumber}.pdf",
                Convert.ToBase64String(pdf), DocumentSource.Upload), "SaferSleep", 20, ct);
            rec.DocumentId = doc?.Id;
        }
        var amend = previous?.NoteId;
        var (note, _) = await emrService.NoteAsync(ep.Id, new NoteRequest(NoteKind.Anaesthetic, Summary(rec), true, amend), by.Length > 100 ? by[..100] : by, ct);
        if (note is null && amend is not null) (note, _) = await emrService.NoteAsync(ep.Id, new NoteRequest(NoteKind.Anaesthetic, Summary(rec), true, null), by.Length > 100 ? by[..100] : by, ct);
        rec.NoteId = note?.Id;
        if (s.FileDrugsToEmr)
        {
            var items = rec.Drugs.OrderBy(d => d.At).Select(d => (d.Name, d.Rate ?? (d.Dose is { } q ? $"{q.ToString("0.###", CultureInfo.InvariantCulture)} {d.Unit}".Trim() : ""), d.Route ?? "", d.At,
                rec.Anaesthetist ?? "Anaesthetist", (string?)$"From the SaferSleep anaesthetic record (case {rec.CaseNumber}){(d.Rate is null ? "" : " – infusion")}")).ToList();
            await emrService.FileExternalMedicationsAsync(ep.Id, "SaferSleep", rec.CaseNumber, items, user, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static DateTimeOffset Local(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, Nz);

    public static string Summary(AnaestheticRecord r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Anaesthetic record from SaferSleep (case {r.CaseNumber}){(r.IsCorrection ? " – CORRECTED" : "")}.");
        if (r.Anaesthetist is not null) sb.AppendLine($"Anaesthetist: {r.Anaesthetist}");
        if (r.StartAt is { } s) sb.AppendLine($"Anaesthesia: {Local(s):HH:mm}{(r.EndAt is { } e ? $" – {Local(e):HH:mm}" : "")}");
        if (r.Asa is not null) sb.AppendLine($"ASA: {r.Asa}");
        if (r.Technique is not null) sb.AppendLine($"Technique: {r.Technique}");
        if (r.Airway is not null) sb.AppendLine($"Airway: {r.Airway}");
        if (r.Drugs.Count > 0)
        {
            sb.AppendLine("Drugs:");
            foreach (var d in r.Drugs.OrderBy(d => d.At))
                sb.AppendLine($"  {Local(d.At):HH:mm} {d.Name} {(d.Rate ?? (d.Dose is { } q ? $"{q.ToString("0.###", CultureInfo.InvariantCulture)} {d.Unit}" : "")).Trim()}{(d.Route is null ? "" : " " + d.Route)}");
        }
        if (r.Events.Count > 0)
        {
            sb.AppendLine("Events:");
            foreach (var e in r.Events.OrderBy(e => e.At)) sb.AppendLine($"  {Local(e.At):HH:mm} {e.Text}");
        }
        if (r.Comments is not null) sb.AppendLine($"Comments: {r.Comments}");
        sb.Append("The full record (with the chart) is the PDF in the patient's documents.");
        return sb.ToString();
    }

    /// <summary>Files an unmatched record to an episode chosen by a person (the stored message is read again for the PDF).</summary>
    public async Task<string?> FileToEpisodeAsync(Guid recordId, Guid episodeId, byte[]? pdf, string user, CancellationToken ct = default)
    {
        var rec = await db.Records.Include(r => r.Drugs).Include(r => r.Events).SingleOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new KeyNotFoundException("Record not found.");
        if (rec.EpisodeId is not null) return "This record is already filed.";
        if (rec.SupersededById is not null) return "A newer version of this record has arrived – file that one.";
        var ep = await emr.Episodes.AsNoTracking().SingleOrDefaultAsync(e => e.Id == episodeId, ct) ?? throw new KeyNotFoundException("Episode not found.");
        if (rec.Nhi is { } nhi && await patients.Patients.AsNoTracking().Where(p => p.Id == ep.PatientId).Select(p => p.Nhi).SingleOrDefaultAsync(ct) is { } pn && !string.Equals(pn, nhi, StringComparison.OrdinalIgnoreCase))
            return $"The record is for NHI {nhi}, but this episode's patient has NHI {pn}.";
        rec.EpisodeId = ep.Id; rec.PatientId = ep.PatientId;
        await db.SaveChangesAsync(ct);
        var previous = await db.Records.AsNoTracking().Where(x => x.CaseNumber == rec.CaseNumber && x.SupersededById == rec.Id && x.NoteId != null).OrderByDescending(x => x.ReceivedAt).FirstOrDefaultAsync(ct);
        await FileAsync(rec, pdf, previous, user, ct);
        await audit.WriteAsync(user, "File", "AnaestheticRecord", rec.Id.ToString(), ep.PatientId.ToString(), reason: $"Filed by hand to the episode (case {rec.CaseNumber})", ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ ADT bookkeeping (the web worker builds and queues the message)
    public async Task<bool> SentAsync(Guid episodeId, string evt, CancellationToken ct = default) => await db.AdtSends.AnyAsync(a => a.EpisodeId == episodeId && a.Event == evt && a.Error == null, ct);

    public async Task RecordAdtAsync(Guid episodeId, string evt, Guid? messageId, string? error, string user, CancellationToken ct = default)
    {
        db.AdtSends.Add(new AdtSend { EpisodeId = episodeId, Event = evt, At = clock.GetUtcNow(), By = user, MessageId = messageId, Error = Clip(error, 300) });
        await db.SaveChangesAsync(ct);
    }

    private static string? Clip(string? s, int max) => DeviceService.Clip(s, max);
}
