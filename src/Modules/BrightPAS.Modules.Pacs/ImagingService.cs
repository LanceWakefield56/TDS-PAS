using System.Globalization;
using FellowOakDicom;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Setup;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Pacs;

public sealed record ImagingSettings(string StoragePath, string? ImportFolder, bool AutoFile);
public sealed record ImagingSettingsInput(string? StoragePath, string? ImportFolder, bool AutoFile);
public sealed record StoreResult(bool Stored, bool Duplicate, Guid? StudyId, bool Filed, string? Error, bool NotUnderstood = false);
public sealed record RequestInput(Guid PatientId, Guid? BookingId, string? Modality, string? Examination, string? BodyPart, string? ClinicalNotes, DateOnly? WantedOn);
public sealed record StudyRow(Guid Id, string Status, Guid? PatientId, string? Patient, string? Mrn, Guid? BookingId, string? Episode, string? AccessionNumber, DateOnly? StudyDate,
    string? Description, string Modalities, string? BodyPart, int ImageCount, string? FilePatientId, string? FilePatientName, DateOnly? FileBirthDate,
    string Source, DateTimeOffset ReceivedAt, string ReceivedBy, string? MatchedBy, bool HasReport, Guid? FirstImageId);
public sealed record EpisodeOption(Guid BookingId, DateOnly Date, string Procedure, string Status);
public sealed record WorklistItem(string AccessionNumber, string StudyInstanceUid, string Modality, string Examination, string? BodyPart, DateOnly? WantedOn,
    string PatientId, string PatientName, DateOnly BirthDate, string Sex);

/// <summary>Imaging (PACS) (0.37.0, ADR 0050): storing, matching and filing DICOM studies; imaging requests; the viewer's data.</summary>
public sealed class ImagingService(ImagingDbContext db, PatientsDbContext patients, TheatreDbContext theatre, ImagingStore store, SettingsService settings, AuditWriter audit, TimeProvider clock)
{
    public const string ImportFolderKey = "imaging.importFolder";
    public const string AutoFileKey = "imaging.autoFile";
    public static readonly string[] Modalities = ["CR", "DX", "CT", "MR", "US", "XA", "RF", "MG", "NM", "PT", "OT"];
    private const string DicomDirClass = "1.2.840.10008.1.3.10";

    // ---------------------------------------------------------------- settings
    public async Task<ImagingSettings> SettingsAsync(CancellationToken ct = default) =>
        new(await store.RootAsync(ct), await settings.GetAsync(ImportFolderKey, ct) is { Length: > 0 } f ? f : null, await settings.GetAsync(AutoFileKey, ct) != "false");

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(ImagingSettingsInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var path = r.StoragePath?.Trim();
        var folder = r.ImportFolder?.Trim();
        if (path is { Length: > 0 } && !Path.IsPathFullyQualified(path)) errors.Add("The imaging folder must be a full path, e.g. D:\\BrightPAS\\Imaging or \\\\server\\share\\imaging.");
        if (folder is { Length: > 0 } && !Path.IsPathFullyQualified(folder)) errors.Add("The watched folder must be a full path, e.g. \\\\xray-pc\\export or D:\\Imaging-in.");
        if (path is { Length: > 0 } && folder is { Length: > 0 } && string.Equals(Path.GetFullPath(path).TrimEnd('\\', '/'), Path.GetFullPath(folder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            errors.Add("The watched folder cannot be the imaging folder itself.");
        if (errors.Count > 0) return errors;
        await settings.SetAsync(ImagingStore.PathKey, path ?? "", user, ct);
        await settings.SetAsync(ImportFolderKey, folder ?? "", user, ct);
        await settings.SetAsync(AutoFileKey, r.AutoFile ? "true" : "false", user, ct);
        await audit.WriteAsync(user, "Settings", "ImagingSettings", "1", reason: $"Folder {(path is { Length: > 0 } ? path : "default")}; watched {(folder ?? "none")}; auto-file {r.AutoFile}", ct: ct);
        return [];
    }

    // ---------------------------------------------------------------- storing
    /// <summary>
    /// Stores one DICOM object – the single entry point for upload, the watched folder and (later) the vendor link. The same object
    /// (SOP instance UID) is stored once. Images join their study; a new study is matched to a patient if it can be, otherwise it waits in "To match".
    /// </summary>
    public async Task<StoreResult> StoreAsync(byte[] bytes, ImageSource source, string who, CancellationToken ct = default)
    {
        DicomFile file;
        try { file = ImagingDicom.Open(bytes); }
        catch (Exception ex) when (ex is DicomException or EndOfStreamException or InvalidOperationException or ArgumentException)
        { return new(false, false, null, false, $"Not a DICOM file BrightPAS can read: {ex.Message}", true); }
        var f = ImagingDicom.Facts(file);
        if (f.SopClassUid == DicomDirClass) return new(false, false, null, false, "DICOMDIR (the CD's index) – skipped; the pictures are in the other files.", true);
        if (f.SopInstanceUid.Length == 0 || f.StudyInstanceUid.Length == 0 || f.SeriesInstanceUid.Length == 0)
            return new(false, false, null, false, "The file has no study, series or image identifier (UID).", true);
        if (f.SopInstanceUid.Length > 64 || f.StudyInstanceUid.Length > 64 || f.SeriesInstanceUid.Length > 64) return new(false, false, null, false, "A UID in the file is longer than DICOM allows.", true);
        var existing = await db.Images.AsNoTracking().Where(i => i.SopInstanceUid == f.SopInstanceUid).Select(i => (Guid?)i.StudyId).FirstOrDefaultAsync(ct);
        if (existing is not null) return new(false, true, existing, true, null);

        var viewable = ImagingDicom.CanRender(file);
        for (var attempt = 0; ; attempt++)
        {
            try { return await StoreOnceAsync(bytes, f, viewable, source, who, ct); }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Another import created the same study or series at the same moment – try once more with what is now saved.
                db.ChangeTracker.Clear();
                if (await db.Images.AsNoTracking().AnyAsync(i => i.SopInstanceUid == f.SopInstanceUid, ct)) return new(false, true, null, true, null);
            }
        }
    }

    private async Task<StoreResult> StoreOnceAsync(byte[] bytes, DicomFacts f, bool viewable, ImageSource source, string who, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var study = await db.Studies.SingleOrDefaultAsync(s => s.StudyInstanceUid == f.StudyInstanceUid, ct);
        var isNew = study is null;
        if (study is null)
        {
            study = new Study
            {
                StudyInstanceUid = f.StudyInstanceUid, AccessionNumber = Cut(f.AccessionNumber, 16), StudyDate = f.StudyDate, StudyTime = Cut(f.StudyTime, 16),
                Description = Cut(f.StudyDescription ?? f.SeriesDescription, 200), BodyPart = Cut(f.BodyPart, 64), ReferringPhysician = Cut(f.ReferringPhysician, 200),
                Institution = Cut(f.Institution, 200), FilePatientId = Cut(f.PatientId, 64), FilePatientName = Cut(f.PatientName, 200), FileBirthDate = f.BirthDate,
                FileSex = Cut(f.Sex, 16), Source = source, ReceivedAt = now, ReceivedBy = Cut(who, 200) ?? "",
            };
            db.Studies.Add(study);
        }
        else if (study.Status == StudyStatus.Deleted) return new(false, false, study.Id, false, "This study was deleted in BrightPAS; its images are not taken in again.");
        var series = await db.Series.SingleOrDefaultAsync(s => s.SeriesInstanceUid == f.SeriesInstanceUid, ct);
        if (series is not null && series.StudyId != study.Id) return new(false, false, null, false, "The series in this file belongs to another study (UIDs do not agree).", true);
        if (series is null)
        {
            series = new Series { StudyId = study.Id, SeriesInstanceUid = f.SeriesInstanceUid, Modality = Cut(f.Modality, 16) ?? "OT", SeriesNumber = f.SeriesNumber, Description = Cut(f.SeriesDescription, 200), BodyPart = Cut(f.BodyPart, 64) };
            db.Series.Add(series);
        }
        var img = new Image
        {
            StudyId = study.Id, SeriesId = series.Id, SopInstanceUid = f.SopInstanceUid, SopClassUid = Cut(f.SopClassUid, 64) ?? "", InstanceNumber = f.InstanceNumber,
            Rows = f.Rows, Columns = f.Columns, Frames = f.Frames, Photometric = Cut(f.Photometric, 20), TransferSyntax = Cut(f.TransferSyntax, 64) ?? "", Viewable = viewable,
            WindowCentre = f.WindowCentre, WindowWidth = f.WindowWidth, PixelSpacing = f.PixelSpacing, ReceivedAt = now,
        };
        await store.WriteAsync(img, bytes, ct);
        db.Images.Add(img);
        series.ImageCount++;
        study.ImageCount++;
        study.Bytes += img.Bytes;
        var mods = study.Modalities.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!mods.Contains(series.Modality, StringComparer.Ordinal)) { mods.Add(series.Modality); study.Modalities = Cut(string.Join(',', mods), 60) ?? ""; }
        if (isNew) await MatchAsync(study, ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { await store.DeleteAsync(img, CancellationToken.None); throw; }
        if (isNew)
            await audit.WriteAsync(who, "Receive", "ImagingStudy", study.Id.ToString(), study.PatientId?.ToString(),
                reason: $"{source}; {(study.Status == StudyStatus.Filed ? $"filed by {study.MatchedBy}" : "to match")}", ct: ct);
        return new(true, false, study.Id, study.Status == StudyStatus.Filed, null);
    }

    /// <summary>Accession number or study UID of a BrightPAS request; otherwise (when allowed) NHI or MRN together with the date of birth.</summary>
    private async Task MatchAsync(Study s, CancellationToken ct)
    {
        ImagingRequest? req = null;
        if (s.AccessionNumber is { Length: > 0 } acc) req = await db.Requests.SingleOrDefaultAsync(r => r.AccessionNumber == acc && r.Status != RequestStatus.Cancelled, ct);
        req ??= await db.Requests.FirstOrDefaultAsync(r => r.StudyInstanceUid == s.StudyInstanceUid && r.Status != RequestStatus.Cancelled, ct);
        if (req is not null && await LivePatientAsync(req.PatientId, ct) is { } rp)
        {
            FileTo(s, rp.Id, req.BookingId, MatchMethod.Accession, "BrightPAS (request " + req.AccessionNumber + ")");
            s.RequestId = req.Id;
            req.Status = RequestStatus.Done;
            req.StudyId = s.Id;
            s.BookingId ??= await EpisodeOnAsync(rp.Id, s.StudyDate, ct);
            return;
        }
        if (await settings.GetAsync(AutoFileKey, ct) == "false" || s.FilePatientId is not { Length: > 0 } pid || s.FileBirthDate is not { } dob) return;
        var key = pid.Trim().ToUpperInvariant();
        var found = await patients.Patients.AsNoTracking().Where(p => p.MergedIntoId == null && (p.Nhi == key || p.Mrn == key)).ToListAsync(ct);
        if (found.Count != 1 || found[0].DateOfBirth != dob) return;
        FileTo(s, found[0].Id, null, MatchMethod.PatientIdAndBirthDate, "BrightPAS (NHI/MRN and date of birth)");
        s.BookingId = await EpisodeOnAsync(found[0].Id, s.StudyDate, ct);
    }

    private void FileTo(Study s, Guid patientId, Guid? bookingId, MatchMethod how, string by)
    {
        s.PatientId = patientId;
        s.BookingId = bookingId;
        s.MatchedBy = how;
        s.Status = StudyStatus.Filed;
        s.FiledAt = clock.GetUtcNow();
        s.FiledBy = Cut(by, 100);
    }

    private async Task<Patient?> LivePatientAsync(Guid id, CancellationToken ct)
    {
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        while (p?.MergedIntoId is { } m) p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == m, ct);
        return p;
    }

    /// <summary>The patient's single (not cancelled) booking on the study date, if exactly one.</summary>
    private async Task<Guid?> EpisodeOnAsync(Guid patientId, DateOnly? date, CancellationToken ct)
    {
        if (date is null) return null;
        var ids = await (from b in theatre.Bookings.AsNoTracking()
                         join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                         where b.PatientId == patientId && s.Date == date && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.DidNotAttend
                         select b.Id).Take(2).ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : null;
    }

    // ---------------------------------------------------------------- lists and detail
    public async Task<IReadOnlyList<StudyRow>> StudiesAsync(string? view, Guid? patientId, Guid? bookingId, CancellationToken ct = default)
    {
        var q = db.Studies.AsNoTracking().Where(s => s.Status != StudyStatus.Deleted);
        if (patientId is { } p) q = q.Where(s => s.PatientId == p);
        if (bookingId is { } b) q = q.Where(s => s.BookingId == b);
        if (view == "unmatched") q = q.Where(s => s.Status == StudyStatus.Unmatched);
        var rows = await q.OrderByDescending(s => s.ReceivedAt).Take(view == "unmatched" || patientId is not null || bookingId is not null ? 500 : 200).ToListAsync(ct);
        return await RowsAsync(rows, ct);
    }

    private async Task<IReadOnlyList<StudyRow>> RowsAsync(List<Study> rows, CancellationToken ct)
    {
        var pids = rows.Where(r => r.PatientId is not null).Select(r => r.PatientId!.Value).Distinct().ToList();
        var pts = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var bids = rows.Where(r => r.BookingId is not null).Select(r => r.BookingId!.Value).Distinct().ToList();
        var eps = await EpisodesAsync(bids, ct);
        var sids = rows.Select(r => r.Id).ToList();
        var firsts = (await db.Images.AsNoTracking().Where(i => sids.Contains(i.StudyId) && i.Viewable).Select(i => new { i.StudyId, i.Id, i.InstanceNumber, i.ReceivedAt }).ToListAsync(ct))
            .GroupBy(i => i.StudyId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.InstanceNumber ?? int.MaxValue).ThenBy(i => i.ReceivedAt).First().Id);
        return rows.Select(s =>
        {
            var pt = s.PatientId is { } id && pts.TryGetValue(id, out var x) ? x : null;
            var ep = s.BookingId is { } bid && eps.TryGetValue(bid, out var e) ? e : null;
            return new StudyRow(s.Id, s.Status.ToString(), s.PatientId, pt is null ? null : $"{pt.FamilyName.ToUpperInvariant()}, {pt.GivenNames}", pt?.Mrn, s.BookingId,
                ep is null ? null : $"{ep.Date:d MMM yyyy} {ep.Procedure}", s.AccessionNumber, s.StudyDate, s.Description, s.Modalities, s.BodyPart, s.ImageCount,
                s.FilePatientId, s.FilePatientName, s.FileBirthDate, s.Source.ToString(), s.ReceivedAt, s.ReceivedBy, s.MatchedBy?.ToString(), s.Report is not null,
                firsts.TryGetValue(s.Id, out var fi) ? fi : null);
        }).ToList();
    }

    private async Task<Dictionary<Guid, EpisodeOption>> EpisodesAsync(List<Guid> bookingIds, CancellationToken ct) =>
        bookingIds.Count == 0 ? [] : (await (from b in theatre.Bookings.AsNoTracking()
                                             join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                                             where bookingIds.Contains(b.Id)
                                             select new { b.Id, s.Date, b.Procedure, b.Status }).ToListAsync(ct))
            .ToDictionary(x => x.Id, x => new EpisodeOption(x.Id, x.Date, x.Procedure, x.Status.ToString()));

    public async Task<IReadOnlyList<EpisodeOption>> PatientEpisodesAsync(Guid patientId, CancellationToken ct = default) =>
        (await (from b in theatre.Bookings.AsNoTracking()
                join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                where b.PatientId == patientId && b.Status != BookingStatus.Cancelled
                orderby s.Date descending
                select new { b.Id, s.Date, b.Procedure, b.Status }).Take(50).ToListAsync(ct))
        .Select(x => new EpisodeOption(x.Id, x.Date, x.Procedure, x.Status.ToString())).ToList();

    public async Task<int> UnmatchedCountAsync(CancellationToken ct = default) => await db.Studies.CountAsync(s => s.Status == StudyStatus.Unmatched, ct);

    public async Task<object?> StudyAsync(Guid id, string user, CancellationToken ct = default)
    {
        var s = await db.Studies.AsNoTracking().Include(x => x.Series).ThenInclude(x => x.Images).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (s is null || s.Status == StudyStatus.Deleted) return null;
        var row = (await RowsAsync([s], ct))[0];
        var pt = s.PatientId is { } pid ? await patients.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid, ct) : null;
        var req = s.RequestId is { } rid ? await db.Requests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rid, ct) : null;
        await audit.WriteAsync(user, "View", "ImagingStudy", s.Id.ToString(), s.PatientId?.ToString(), ct: ct);
        return new
        {
            study = row,
            s.StudyInstanceUid, s.StudyTime, s.ReferringPhysician, s.Institution, s.FileSex, s.FiledAt, s.FiledBy, s.Bytes,
            s.Report, s.ReportAt, s.ReportBy,
            patientBirthDate = pt?.DateOfBirth, patientNhi = pt?.Nhi,
            birthDateMismatch = pt is not null && s.FileBirthDate is { } fb && fb != pt.DateOfBirth,
            request = req is null ? null : new { req.Id, req.AccessionNumber, req.Examination, req.ClinicalNotes, req.RequestedBy, req.RequestedAt },
            series = s.Series.OrderBy(x => x.SeriesNumber ?? int.MaxValue).ThenBy(x => x.Description).Select(x => new
            {
                x.Id, x.Modality, x.SeriesNumber, x.Description, x.BodyPart, x.ImageCount,
                images = x.Images.OrderBy(i => i.InstanceNumber ?? int.MaxValue).ThenBy(i => i.ReceivedAt).Select(i => new { i.Id, i.InstanceNumber, i.Frames, i.Rows, i.Columns, i.Viewable, i.PixelSpacing, i.WindowCentre, i.WindowWidth }),
            }),
        };
    }

    public async Task<Image?> ImageAsync(Guid imageId, CancellationToken ct = default)
    {
        var img = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId, ct);
        if (img is null) return null;
        var deleted = await db.Studies.AsNoTracking().AnyAsync(s => s.Id == img.StudyId && s.Status == StudyStatus.Deleted, ct);
        return deleted ? null : img;
    }

    /// <summary>Draws one frame of an image for the viewer (window centre/width in modality units; size = longest side in pixels).</summary>
    public async Task<RenderedImage?> RenderAsync(Guid imageId, int frame, double? centre, double? width, bool invert, int size, CancellationToken ct = default)
    {
        var img = await ImageAsync(imageId, ct);
        if (img is null || !img.Viewable || await store.ReadAsync(img, ct) is not { } bytes) return null;
        try { return ImagingDicom.Render(ImagingDicom.Open(bytes).Dataset, frame, centre, width, invert, Math.Clamp(size, 16, 4096)); }
        catch (Exception ex) when (ex is DicomException or InvalidOperationException or NotSupportedException or ArgumentException or IndexOutOfRangeException) { return null; }
    }

    /// <summary>The original DICOM files of a study, for an (encrypted) export.</summary>
    public async Task<(IReadOnlyList<(string Name, byte[] Bytes)> Files, Guid? PatientId, string Label)?> ExportFilesAsync(Guid studyId, CancellationToken ct = default)
    {
        var s = await db.Studies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == studyId && x.Status != StudyStatus.Deleted, ct);
        if (s is null) return null;
        var images = await db.Images.AsNoTracking().Where(i => i.StudyId == studyId).OrderBy(i => i.SeriesId).ThenBy(i => i.InstanceNumber).ToListAsync(ct);
        var files = new List<(string, byte[])>();
        var n = 0;
        foreach (var i in images)
            if (await store.ReadAsync(i, ct) is { } b) files.Add(($"IMG{++n:D4}.dcm", b));
        var label = s.AccessionNumber ?? s.StudyDate?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? s.Id.ToString("N")[..8];
        return (files, s.PatientId, label);
    }

    // ---------------------------------------------------------------- filing by a person
    /// <summary>Files (or re-files) a study to a patient and optionally an episode. A different date of birth in the images needs <paramref name="confirmMismatch"/>.</summary>
    public async Task<IReadOnlyList<string>> FileAsync(Guid studyId, Guid patientId, Guid? bookingId, bool confirmMismatch, string user, CancellationToken ct = default)
    {
        var s = await db.Studies.SingleOrDefaultAsync(x => x.Id == studyId, ct);
        if (s is null || s.Status == StudyStatus.Deleted) return ["Study not found."];
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return ["Patient not found."];
        if (p.MergedIntoId is not null) return ["This patient record was merged into another – choose the surviving record."];
        if (s.FileBirthDate is { } dob && dob != p.DateOfBirth && !confirmMismatch)
            return [$"The date of birth in the images ({dob:d MMM yyyy}) is not this patient's ({p.DateOfBirth:d MMM yyyy}). Check you have the right patient, then tick \"I have checked\" to file anyway."];
        if (bookingId is { } b && !await theatre.Bookings.AnyAsync(x => x.Id == b && x.PatientId == patientId, ct)) return ["That episode is not this patient's."];
        var before = s.PatientId;
        FileTo(s, patientId, bookingId, MatchMethod.Person, user);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, before is null ? "File" : "Refile", "ImagingStudy", s.Id.ToString(), patientId.ToString(),
            reason: string.Join("; ", new[] { before is not null && before != patientId ? $"moved from patient {before}" : null, confirmMismatch ? "date of birth differs – checked" : null }.OfType<string>()) is { Length: > 0 } why ? why : null, ct: ct);
        return [];
    }

    public async Task<string?> LinkEpisodeAsync(Guid studyId, Guid? bookingId, string user, CancellationToken ct = default)
    {
        var s = await db.Studies.SingleOrDefaultAsync(x => x.Id == studyId, ct);
        if (s is null || s.Status != StudyStatus.Filed || s.PatientId is null) return "File the study to a patient first.";
        if (bookingId is { } b && !await theatre.Bookings.AnyAsync(x => x.Id == b && x.PatientId == s.PatientId, ct)) return "That episode is not this patient's.";
        s.BookingId = bookingId;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "LinkEpisode", "ImagingStudy", s.Id.ToString(), s.PatientId.ToString(), reason: bookingId?.ToString() ?? "unlinked", ct: ct);
        return null;
    }

    public async Task<string?> SaveReportAsync(Guid studyId, string? text, string user, CancellationToken ct = default)
    {
        var s = await db.Studies.SingleOrDefaultAsync(x => x.Id == studyId, ct);
        if (s is null || s.Status == StudyStatus.Deleted) return "Study not found.";
        var t = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (t is { Length: > 8000 }) return "The report is too long (8,000 characters at most).";
        s.Report = t;
        s.ReportAt = clock.GetUtcNow();
        s.ReportBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Report", "ImagingStudy", s.Id.ToString(), s.PatientId?.ToString(), ct: ct);
        return null;
    }

    /// <summary>Deletes the image files; the study row stays with who, when and why (and the same study is not taken in again).</summary>
    public async Task<string?> DeleteAsync(Guid studyId, string? reason, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Say why the study is being deleted.";
        var s = await db.Studies.SingleOrDefaultAsync(x => x.Id == studyId, ct);
        if (s is null || s.Status == StudyStatus.Deleted) return "Study not found.";
        foreach (var img in await db.Images.Where(i => i.StudyId == studyId).ToListAsync(ct)) await store.DeleteAsync(img, ct);
        s.Status = StudyStatus.Deleted;
        s.DeletedAt = clock.GetUtcNow();
        s.DeletedBy = user;
        s.DeletedReason = Cut(reason.Trim(), 500);
        if (s.RequestId is { } rid && await db.Requests.SingleOrDefaultAsync(r => r.Id == rid, ct) is { } req) { req.Status = RequestStatus.Requested; req.StudyId = null; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "ImagingStudy", s.Id.ToString(), s.PatientId?.ToString(), reason: s.DeletedReason, ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- requests (orders)
    public async Task<(ImagingRequest? Request, IReadOnlyList<string> Errors)> CreateRequestAsync(RequestInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var modality = (r.Modality ?? "").Trim().ToUpperInvariant();
        if (!Modalities.Contains(modality, StringComparer.Ordinal)) errors.Add("Choose the type of imaging (e.g. DX X-ray, CT, US).");
        if (string.IsNullOrWhiteSpace(r.Examination)) errors.Add("Say what imaging is wanted (e.g. Chest PA, Left knee AP and lateral).");
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.PatientId, ct);
        if (p is null || p.MergedIntoId is not null) errors.Add("Choose the patient.");
        if (r.BookingId is { } b && !await theatre.Bookings.AnyAsync(x => x.Id == b && x.PatientId == r.PatientId, ct)) errors.Add("That episode is not this patient's.");
        if (r.WantedOn is { } w && w < NzTime.Today(clock).AddDays(-1)) errors.Add("The date wanted cannot be in the past.");
        if (errors.Count > 0) return (null, errors);
        var prefix = "X" + NzTime.Today(clock).ToString("yyMMdd", CultureInfo.InvariantCulture);
        var strategy = db.Database.CreateExecutionStrategy();
        var req = await strategy.ExecuteAsync(async () =>
        {
            for (var attempt = 0; ; attempt++)
            {
                var n = await db.Requests.CountAsync(x => x.AccessionNumber.StartsWith(prefix), ct);
                var item = new ImagingRequest
                {
                    PatientId = r.PatientId, BookingId = r.BookingId, AccessionNumber = prefix + (n + 1 + attempt).ToString("D4", CultureInfo.InvariantCulture), StudyInstanceUid = ImagingDicom.NewUid(),
                    Modality = modality, Examination = Cut(r.Examination!.Trim(), 200)!, BodyPart = Cut(r.BodyPart?.Trim() is { Length: > 0 } bp ? bp.ToUpperInvariant() : null, 64),
                    ClinicalNotes = Cut(r.ClinicalNotes?.Trim() is { Length: > 0 } cn ? cn : null, 2000), WantedOn = r.WantedOn, RequestedAt = clock.GetUtcNow(), RequestedBy = user,
                };
                db.Requests.Add(item);
                try { await db.SaveChangesAsync(ct); return item; }
                catch (DbUpdateException) when (attempt < 5) { db.Requests.Remove(item); db.ChangeTracker.Clear(); }
            }
        });
        await audit.WriteAsync(user, "Create", "ImagingRequest", req.Id.ToString(), req.PatientId.ToString(), reason: $"{req.AccessionNumber} {req.Modality} {req.Examination}", ct: ct);
        return (req, []);
    }

    public async Task<IReadOnlyList<object>> RequestsAsync(string? view, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Requests.AsNoTracking();
        if (patientId is { } p) q = q.Where(r => r.PatientId == p);
        if (view != "all") q = q.Where(r => r.Status == RequestStatus.Requested);
        var rows = await q.OrderByDescending(r => r.RequestedAt).Take(300).ToListAsync(ct);
        var pids = rows.Select(r => r.PatientId).Distinct().ToList();
        var pts = await patients.Patients.AsNoTracking().Where(x => pids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var eps = await EpisodesAsync(rows.Where(r => r.BookingId is not null).Select(r => r.BookingId!.Value).Distinct().ToList(), ct);
        return rows.Select(r => (object)new
        {
            r.Id, r.AccessionNumber, r.Modality, r.Examination, r.BodyPart, r.ClinicalNotes, r.WantedOn, status = r.Status.ToString(), r.StudyId, r.RequestedAt, r.RequestedBy,
            r.PatientId, patient = pts.TryGetValue(r.PatientId, out var pt) ? $"{pt.FamilyName.ToUpperInvariant()}, {pt.GivenNames}" : null, mrn = pt?.Mrn,
            r.BookingId, episode = r.BookingId is { } b && eps.TryGetValue(b, out var e) ? $"{e.Date:d MMM yyyy} {e.Procedure}" : null,
        }).ToList();
    }

    public async Task<string?> CancelRequestAsync(Guid id, string user, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Request not found.";
        if (r.Status != RequestStatus.Requested) return "Only a request still waiting for images can be cancelled.";
        r.Status = RequestStatus.Cancelled;
        r.CancelledAt = clock.GetUtcNow();
        r.CancelledBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "ImagingRequest", r.Id.ToString(), r.PatientId.ToString(), reason: r.AccessionNumber, ct: ct);
        return null;
    }

    /// <summary>
    /// Requests still waiting for images, as a Modality Worklist would serve them (for the later vendor link, and the request form).
    /// Patient ID is the NHI when known, otherwise the MRN – the same key automatic filing uses.
    /// </summary>
    public async Task<IReadOnlyList<WorklistItem>> WorklistAsync(DateOnly? on, string? modality, CancellationToken ct = default)
    {
        var q = db.Requests.AsNoTracking().Where(r => r.Status == RequestStatus.Requested);
        if (on is { } d) q = q.Where(r => r.WantedOn == null || r.WantedOn <= d);
        if (modality is { Length: > 0 } m) q = q.Where(r => r.Modality == m);
        var rows = await q.OrderBy(r => r.RequestedAt).Take(500).ToListAsync(ct);
        var pids = rows.Select(r => r.PatientId).Distinct().ToList();
        var pts = await patients.Patients.AsNoTracking().Where(x => pids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        return rows.Where(r => pts.ContainsKey(r.PatientId)).Select(r =>
        {
            var p = pts[r.PatientId];
            return new WorklistItem(r.AccessionNumber, r.StudyInstanceUid, r.Modality, r.Examination, r.BodyPart, r.WantedOn, p.Nhi ?? p.Mrn,
                $"{p.FamilyName.ToUpperInvariant()}^{p.GivenNames}", p.DateOfBirth, p.Sex.ToString() switch { "Male" => "M", "Female" => "F", _ => "O" });
        }).ToList();
    }

    // ---------------------------------------------------------------- merge
    public async Task<int> MergeAsync(Guid survivorId, Guid duplicateId, CancellationToken ct = default) =>
        await db.Studies.Where(s => s.PatientId == duplicateId).ExecuteUpdateAsync(x => x.SetProperty(s => s.PatientId, survivorId), ct)
        + await db.Requests.Where(r => r.PatientId == duplicateId).ExecuteUpdateAsync(x => x.SetProperty(r => r.PatientId, survivorId), ct);

    private static string? Cut(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..max];
}
