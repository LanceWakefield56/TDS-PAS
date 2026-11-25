using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Core.Patients;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.EBookings;

public sealed record EBookingFileInput(DocumentCategory Category, string FileName, string ContentBase64);

/// <summary>A booking request as sent by the rooms (portal form, JSON file or HL7 message).</summary>
public sealed record EBookingInput(
    Guid SpecialistId, string? RoomsName, string? ContactName, string? ContactPhone, string? ContactEmail,
    string? Nhi, string? FamilyName, string? GivenNames, DateOnly? DateOfBirth, BirthSex Sex,
    string? MobilePhone, string? Email, string? AddressLine1, string? Suburb, string? City, string? Postcode, string? InterpreterLanguage,
    ServiceType Service, Guid? ProcedureItemId, string? Procedure, Laterality Laterality, AnaestheticType Anaesthetic, int? DurationMinutes,
    Priority Priority, Funder Funder, string? FunderReference, string? InsurerName,
    DateOnly? EarliestDate, DateOnly? LatestDate, string? Availability, string? ClinicalNotes,
    decimal? WeightKg, decimal? HeightCm, int? Asa, bool Diabetic, bool SleepApnoea, bool Anticoagulated, string? AnticoagulantDetails,
    string? Allergies, string? Comorbidities, bool ResponsibleAdultArranged, bool ConsentSigned, DateOnly? ConsentSignedOn,
    string? ExternalRef = null, IReadOnlyList<EBookingFileInput>? Files = null, bool ConfirmDuplicate = false);

public sealed record EBookingResult(BookingRequest? Request, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, bool NeedsConfirm = false);

/// <summary>What the booking office decides when accepting: these may differ from what the rooms asked for.</summary>
public sealed record EBookingAccept(string? Procedure, Laterality Laterality, AnaestheticType Anaesthetic, int DurationMinutes, Priority Priority, string? Notes);

public sealed record EBookingRow(Guid Id, string Number, string Status, string Source, DateTimeOffset SubmittedAt, Guid SpecialistId, string Specialist, string? RoomsName,
    string PatientName, string? Nhi, DateOnly DateOfBirth, bool PatientMatched, string Procedure, string Priority, string Funder, DateOnly? EarliestDate, DateOnly? LatestDate,
    int Files, bool AwaitingRooms, bool AwaitingTds, int Flags, string? WaitlistStatus, DateOnly? BookedFor, int DaysOpen);

public sealed record EBookingMatch(Guid Id, string Mrn, string? Nhi, string FamilyName, string GivenNames, DateOnly DateOfBirth, string Reason);

/// <summary>Told when the booking office asks, accepts or declines – the web host emails the rooms contact (no patient details).</summary>
public interface IEBookingNotifier
{
    Task NotifyAsync(BookingRequest request, string what, CancellationToken ct);
}

/// <summary>Booking requests from specialists' rooms: submit, review, ask, match the patient, accept onto the waitlist or decline. All audited.</summary>
public sealed class EBookingService(EBookingsDbContext db, PatientsDbContext patientsDb, PatientService patients, DocumentService documents, IDocumentCipher cipher,
    ReferralService referrals, ReferralsDbContext referralsDb, PasDbContext core, TheatreService theatre, AuditWriter audit, TimeProvider clock,
    IEnumerable<IEBookingNotifier> notifiers)
{
    public const int MaxFiles = 8;
    /// <summary>All files on one request together (raw bytes), so a request stays well inside the web server's upload limit.</summary>
    public const long MaxTotalBytes = 40L * 1024 * 1024;
    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    // ---------------------------------------------------------------- settings

    public async Task<EBookingSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is null) { s = new EBookingSettings(); db.Settings.Add(s); await db.SaveChangesAsync(ct); }
        return s;
    }

    public async Task<IReadOnlyList<string>> UpdateSettingsAsync(string? watchFolder, bool emailRooms, int maxFileMegabytes, string? instructions, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var folder = Blank(watchFolder);
        if (folder is not null && !(Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal))) errors.Add("The watched folder must be a full path, e.g. D:\\EBookings or \\\\server\\share\\EBookings.");
        if (maxFileMegabytes is < 1 or > 20) errors.Add("The largest file size must be between 1 and 20 MB.");
        if (instructions?.Length > 1000) errors.Add("Keep the instructions for the rooms under 1000 characters.");
        if (errors.Count > 0) return errors;
        var s = await SettingsAsync(ct);
        s.WatchFolder = folder; s.EmailRooms = emailRooms; s.MaxFileMegabytes = maxFileMegabytes; s.Instructions = Blank(instructions);
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "EBookingSettings", "1", reason: $"Folder {folder ?? "(none)"}, email rooms {emailRooms}, max {maxFileMegabytes} MB", ct: ct);
        return [];
    }

    /// <summary>Records the watched-folder check (shown on the settings tab).</summary>
    public async Task RecordCheckAsync(string? problem, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        s.LastCheckedAt = clock.GetUtcNow(); s.LastProblem = Trunc(problem, 500);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- submit

    /// <summary>
    /// Records a request. <paramref name="allowedSpecialists"/> limits a rooms user to the specialists they are (or work for); null means any (booking office, file import).
    /// A possible duplicate (an open request for the same patient and specialist) must be confirmed unless <see cref="EBookingInput.ConfirmDuplicate"/>.
    /// </summary>
    public async Task<EBookingResult> SubmitAsync(EBookingInput i, EBookingSource source, string user, IReadOnlyCollection<Guid>? allowedSpecialists, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var errors = new List<string>();
        if (allowedSpecialists is not null && !allowedSpecialists.Contains(i.SpecialistId)) errors.Add("Choose one of your specialists.");
        else if (await theatre.ClinicianProblemAsync(i.SpecialistId, true, "specialist the patient is booked with", ct) is { } se) errors.Add(se);
        if (string.IsNullOrWhiteSpace(i.ContactName)) errors.Add("Enter the name of the person at the rooms we should contact.");
        if (i.ContactEmail is { Length: > 0 } ce && !EmailOk(ce)) errors.Add("The contact email address does not look right.");

        string? nhi = null;
        if (!string.IsNullOrWhiteSpace(i.Nhi))
        {
            var v = NhiValidator.Validate(i.Nhi);
            if (!v.IsValid) errors.Add($"NHI: {v.Error}"); else nhi = i.Nhi.Trim().ToUpperInvariant();
        }
        if (string.IsNullOrWhiteSpace(i.FamilyName)) errors.Add("Enter the patient's family name.");
        if (string.IsNullOrWhiteSpace(i.GivenNames)) errors.Add("Enter the patient's given names.");
        if (i.FamilyName?.Trim().Length > 100 || i.GivenNames?.Trim().Length > 100) errors.Add("Names must be 100 characters or fewer.");
        if (i.DateOfBirth is not { } dob) errors.Add("Enter the patient's date of birth.");
        else if (dob > Today) errors.Add("The date of birth cannot be in the future.");
        else if (dob < Today.AddYears(-120)) errors.Add("The date of birth is more than 120 years ago – check the date.");
        if (!Enum.IsDefined(i.Sex)) errors.Add("Choose the patient's sex.");
        if (i.Email is { Length: > 0 } pe && !EmailOk(pe)) errors.Add("The patient's email address does not look right.");

        ProcedureItem? item = null;
        if (i.ProcedureItemId is { } pid)
        {
            item = await core.Procedures.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid, ct);
            if (item is null) errors.Add("The procedure chosen is not on TDS's procedure list.");
        }
        var procedure = Blank(i.Procedure) ?? item?.Name;
        if (procedure is null) errors.Add("Enter the procedure.");
        else if (procedure.Length > 300) errors.Add("Keep the procedure under 300 characters.");
        var duration = i.DurationMinutes ?? item?.DefaultMinutes ?? 30;
        if (duration is < 5 or > 720) errors.Add("The expected duration must be between 5 and 720 minutes.");
        if (!Enum.IsDefined(i.Laterality) || !Enum.IsDefined(i.Anaesthetic) || !Enum.IsDefined(i.Priority) || !Enum.IsDefined(i.Funder) || !Enum.IsDefined(i.Service))
            errors.Add("Choose the side, anaesthetic, urgency, funding and service from the lists.");
        if (i.EarliestDate is { } e1 && i.LatestDate is { } l1 && l1 < e1) errors.Add("The latest date must be on or after the earliest date.");
        if (i.LatestDate is { } l2 && l2 < Today) errors.Add("The latest date has already passed.");
        if (i.WeightKg is { } w && (w < 1 || w > 400)) errors.Add("Weight must be between 1 and 400 kg.");
        if (i.HeightCm is { } h && (h < 30 || h > 250)) errors.Add("Height must be between 30 and 250 cm.");
        if (i.Asa is { } asa && (asa < 1 || asa > 6)) errors.Add("ASA grade must be 1 to 6.");
        if (i.ConsentSignedOn is { } cs && cs > Today) errors.Add("The consent date cannot be in the future.");
        if (i.ClinicalNotes?.Length > 2000) errors.Add("Keep the clinical notes under 2000 characters.");
        if (i.Comorbidities?.Length > 1000 || i.Allergies?.Length > 500 || i.Availability?.Length > 500 || i.AnticoagulantDetails?.Length > 300)
            errors.Add("One of the text boxes is too long – shorten it.");

        var files = new List<BookingRequestFile>();
        if (i.Files is { Count: > MaxFiles }) errors.Add($"Attach no more than {MaxFiles} files.");
        foreach (var f in i.Files ?? [])
        {
            var (file, problem) = ReadFile(f, settings.MaxFileMegabytes, user);
            if (problem is not null) errors.Add($"{DocumentService.SafeFileName(f.FileName)}: {problem}"); else files.Add(file!);
        }
        if (files.Sum(f => f.SizeBytes) > MaxTotalBytes) errors.Add("The files together must be 40 MB or smaller – send the request, then add large scans one at a time.");
        if (errors.Count > 0) return new(null, errors, []);

        var external = Blank(i.ExternalRef);
        if (external is not null && await db.Requests.AnyAsync(r => r.ExternalRef == external && r.SpecialistId == i.SpecialistId, ct))
            return new(null, [$"Booking {external} for this specialist has already been received."], []);

        var dupes = await db.Requests.AsNoTracking()
            .Where(r => r.SpecialistId == i.SpecialistId && (r.Status == EBookingStatus.Submitted || r.Status == EBookingStatus.Query))
            .Where(r => (nhi != null && r.Nhi == nhi) || (r.DateOfBirth == i.DateOfBirth && r.FamilyName == i.FamilyName!.Trim()))
            .Select(r => r.Number).ToListAsync(ct);
        if (dupes.Count > 0 && !i.ConfirmDuplicate)
            return new(null, [], [$"There is already an open request for this patient with this specialist ({string.Join(", ", dupes)}). Send anyway only if this is a different procedure."], true);

        var now = clock.GetUtcNow();
        var r = new BookingRequest
        {
            Number = $"EB{settings.NextNumber:000000}",
            Source = source,
            ExternalRef = external,
            SpecialistId = i.SpecialistId,
            RoomsName = Trunc(Blank(i.RoomsName), 150),
            ContactName = Trunc(i.ContactName!.Trim(), 150)!,
            ContactPhone = Trunc(Blank(i.ContactPhone), 30),
            ContactEmail = Trunc(Blank(i.ContactEmail), 200),
            Nhi = nhi,
            FamilyName = i.FamilyName!.Trim(),
            GivenNames = i.GivenNames!.Trim(),
            DateOfBirth = i.DateOfBirth!.Value,
            Sex = i.Sex,
            MobilePhone = Trunc(Blank(i.MobilePhone), 30),
            Email = Trunc(Blank(i.Email), 200),
            AddressLine1 = Trunc(Blank(i.AddressLine1), 150),
            Suburb = Trunc(Blank(i.Suburb), 100),
            City = Trunc(Blank(i.City), 100),
            Postcode = Trunc(Blank(i.Postcode), 10),
            InterpreterLanguage = Trunc(Blank(i.InterpreterLanguage), 60),
            Service = i.Service,
            ProcedureItemId = item?.Id,
            Procedure = procedure!,
            Laterality = i.Laterality,
            Anaesthetic = i.Anaesthetic,
            DurationMinutes = duration,
            Priority = i.Priority,
            Funder = i.Funder,
            FunderReference = Trunc(Blank(i.FunderReference), 60),
            InsurerName = Trunc(Blank(i.InsurerName), 100),
            EarliestDate = i.EarliestDate,
            LatestDate = i.LatestDate,
            Availability = Blank(i.Availability),
            ClinicalNotes = Blank(i.ClinicalNotes),
            WeightKg = i.WeightKg,
            HeightCm = i.HeightCm,
            Asa = i.Asa,
            Diabetic = i.Diabetic,
            SleepApnoea = i.SleepApnoea,
            Anticoagulated = i.Anticoagulated,
            AnticoagulantDetails = Blank(i.AnticoagulantDetails),
            Allergies = Blank(i.Allergies),
            Comorbidities = Blank(i.Comorbidities),
            ResponsibleAdultArranged = i.ResponsibleAdultArranged,
            ConsentSigned = i.ConsentSigned,
            ConsentSignedOn = i.ConsentSigned ? i.ConsentSignedOn : null,
            SubmittedAt = now,
            SubmittedBy = user,
            StatusAt = now,
        };
        foreach (var f in files) { f.RequestId = r.Id; r.Files.Add(f); }
        settings.NextNumber++;
        db.Requests.Add(r);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Submit", "BookingRequest", r.Id.ToString(),
            reason: $"{r.Number} ({source}) to specialist {r.SpecialistId}: {r.Procedure}; {files.Count} file(s){(dupes.Count > 0 ? "; possible duplicate of " + string.Join(", ", dupes) + " confirmed" : "")}", ct: ct);
        return new(r, [], Flags(r));
    }

    private (BookingRequestFile? File, string? Problem) ReadFile(EBookingFileInput f, int maxMegabytes, string user)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(f.ContentBase64 ?? ""); } catch (FormatException) { return (null, "the file could not be read."); }
        if (bytes.Length == 0) return (null, "the file is empty.");
        if (bytes.Length > maxMegabytes * 1024L * 1024L) return (null, $"files must be {maxMegabytes} MB or smaller.");
        var type = DocumentService.DetectType(bytes);
        if (type is null) return (null, "only PDF, JPEG, PNG and TIFF files can be attached.");
        if (!Enum.IsDefined(f.Category)) return (null, "choose what the file is.");
        return (new BookingRequestFile
        {
            Category = f.Category,
            FileName = DocumentService.SafeFileName(f.FileName),
            ContentType = type,
            SizeBytes = bytes.Length,
            Content = cipher.Protect(bytes),
            AddedAt = clock.GetUtcNow(),
            AddedBy = user,
        }, null);
    }

    /// <summary>Adds a file to an open request (rooms or booking office).</summary>
    public async Task<string?> AddFileAsync(Guid id, EBookingFileInput f, string user, IReadOnlyCollection<Guid>? allowedSpecialists, CancellationToken ct = default)
    {
        var r = await db.Requests.Include(x => x.Files).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null || (allowedSpecialists is not null && !allowedSpecialists.Contains(r.SpecialistId))) return "Booking request not found.";
        if (r.Status is not (EBookingStatus.Submitted or EBookingStatus.Query)) return "Files can only be added while the request is open.";
        if (r.Files.Count >= MaxFiles) return $"A request can have no more than {MaxFiles} files.";
        var (file, problem) = ReadFile(f, (await SettingsAsync(ct)).MaxFileMegabytes, user);
        if (problem is not null) return char.ToUpperInvariant(problem[0]) + problem[1..];
        file!.RequestId = r.Id;
        db.Files.Add(file);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "AddFile", "BookingRequest", r.Id.ToString(), r.PatientId?.ToString(), reason: $"{r.Number}: {file.Category} {file.FileName} ({file.SizeBytes / 1024} KB)", ct: ct);
        return null;
    }

    public async Task<(BookingRequestFile Meta, byte[] Bytes)?> OpenFileAsync(Guid requestId, Guid fileId, string user, IReadOnlyCollection<Guid>? allowedSpecialists, CancellationToken ct = default)
    {
        var r = await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, ct);
        if (r is null || (allowedSpecialists is not null && !allowedSpecialists.Contains(r.SpecialistId))) return null;
        var f = await db.Files.AsNoTracking().SingleOrDefaultAsync(x => x.Id == fileId && x.RequestId == requestId, ct);
        if (f is null) return null;
        await audit.WriteAsync(user, "View", "BookingRequestFile", f.Id.ToString(), r.PatientId?.ToString(), reason: $"{r.Number}: {f.FileName}", ct: ct);
        return (f, cipher.Unprotect(f.Content));
    }

    // ---------------------------------------------------------------- points to check

    /// <summary>Things the booking office should look at before accepting (day-surgery suitability and missing paperwork). Advice only.</summary>
    public static IReadOnlyList<string> Flags(BookingRequest r)
    {
        var f = new List<string>();
        if (r.Nhi is null) f.Add("No NHI number was sent.");
        if (r.Asa is >= 4) f.Add($"ASA {r.Asa} – check the patient is suitable for day surgery.");
        if (Bmi(r) is { } bmi && bmi >= 40) f.Add($"BMI {bmi:0.#} – check the anaesthetist is happy for day surgery.");
        if (r.SleepApnoea) f.Add("Obstructive sleep apnoea.");
        if (r.Anticoagulated) f.Add("Takes blood thinners" + (r.AnticoagulantDetails is { } a ? $" ({a})" : "") + " – a stopping plan is needed.");
        if (r.Diabetic) f.Add("Diabetic – list position and fasting plan.");
        if (r.Allergies is not null) f.Add($"Allergies: {r.Allergies}");
        if (!r.ResponsibleAdultArranged && r.Anaesthetic is AnaestheticType.General or AnaestheticType.Sedation or AnaestheticType.Regional)
            f.Add("No responsible adult confirmed for the trip home and first night.");
        if (!r.ConsentSigned) f.Add("Consent not yet signed.");
        if (r.Funder == Funder.Insurer && r.FunderReference is null) f.Add("Insurer approval number not sent.");
        if (r.Funder == Funder.Acc && r.FunderReference is null) f.Add("ACC claim number not sent.");
        if (r.InterpreterLanguage is { } lang) f.Add($"Interpreter needed: {lang}.");
        return f;
    }

    public static decimal? Bmi(BookingRequest r) => r is { WeightKg: { } w, HeightCm: { } h } && h > 0 ? Math.Round(w / (h / 100m * (h / 100m)), 1) : null;

    // ---------------------------------------------------------------- lists

    public async Task<IReadOnlyList<EBookingRow>> ListAsync(string view, IReadOnlyCollection<Guid>? allowedSpecialists, CancellationToken ct = default)
    {
        var q = db.Requests.AsNoTracking().AsQueryable();
        if (allowedSpecialists is not null) q = q.Where(r => allowedSpecialists.Contains(r.SpecialistId));
        var since = clock.GetUtcNow().AddDays(-120);
        q = view switch
        {
            "open" => q.Where(r => r.Status == EBookingStatus.Submitted || r.Status == EBookingStatus.Query),
            "query" => q.Where(r => r.Status == EBookingStatus.Query),
            "accepted" => q.Where(r => r.Status == EBookingStatus.Accepted && r.StatusAt >= since),
            "closed" => q.Where(r => (r.Status == EBookingStatus.Declined || r.Status == EBookingStatus.Withdrawn) && r.StatusAt >= since),
            _ => q.Where(r => r.SubmittedAt >= since || r.Status == EBookingStatus.Submitted || r.Status == EBookingStatus.Query),
        };
        var list = await q.Include(r => r.Messages).OrderBy(r => r.Status == EBookingStatus.Submitted ? 0 : r.Status == EBookingStatus.Query ? 1 : 2)
            .ThenBy(r => r.SubmittedAt).Take(500).ToListAsync(ct);
        var ids = list.Select(r => r.Id).ToList();
        var fileCounts = await db.Files.AsNoTracking().Where(f => ids.Contains(f.RequestId)).GroupBy(f => f.RequestId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var waitIds = list.Where(r => r.WaitlistEntryId != null).Select(r => r.WaitlistEntryId!.Value).ToList();
        var waits = await referralsDb.Waitlist.AsNoTracking().Where(w => waitIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, ct);
        var names = await theatre.ClinicianNamesAsync(ct);
        return list.Select(r =>
        {
            var last = r.Messages.OrderBy(m => m.At).LastOrDefault();
            var w = r.WaitlistEntryId is { } wid ? waits.GetValueOrDefault(wid) : null;
            return new EBookingRow(r.Id, r.Number, r.Status.ToString(), r.Source.ToString(), r.SubmittedAt, r.SpecialistId, names.GetValueOrDefault(r.SpecialistId, "?"), r.RoomsName,
                $"{r.FamilyName.ToUpperInvariant()}, {r.GivenNames}", r.Nhi, r.DateOfBirth, r.PatientId != null, r.Procedure, r.Priority.ToString(), r.Funder.ToString(),
                r.EarliestDate, r.LatestDate, fileCounts.GetValueOrDefault(r.Id), r.Status == EBookingStatus.Query,
                r.Status == EBookingStatus.Submitted || (r.Status == EBookingStatus.Query && last is { FromTds: false }),
                Flags(r).Count, w?.Status.ToString(), w?.BookedFor, (int)(clock.GetUtcNow() - r.SubmittedAt).TotalDays);
        }).ToList();
    }

    /// <summary>Requests waiting for the booking office (for My list and the menu badge).</summary>
    public async Task<int> WaitingCountAsync(CancellationToken ct = default) =>
        await db.Requests.CountAsync(r => r.Status == EBookingStatus.Submitted, ct);

    public async Task<BookingRequest?> GetAsync(Guid id, IReadOnlyCollection<Guid>? allowedSpecialists, CancellationToken ct = default)
    {
        var r = await db.Requests.AsNoTracking().Include(x => x.Messages).Include(x => x.Files).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null || (allowedSpecialists is not null && !allowedSpecialists.Contains(r.SpecialistId))) return null;
        foreach (var f in r.Files) f.Content = [];
        r.Messages = [.. r.Messages.OrderBy(m => m.At)];
        return r;
    }

    public async Task<WaitlistEntry?> WaitlistEntryAsync(BookingRequest r, CancellationToken ct = default) =>
        r.WaitlistEntryId is { } id ? await referralsDb.Waitlist.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id, ct) : null;

    /// <summary>Registered patients who may be the person in the request: same NHI, or same date of birth and family or given name.</summary>
    public async Task<IReadOnlyList<EBookingMatch>> MatchesAsync(BookingRequest r, CancellationToken ct = default)
    {
        var family = PatientService.SearchKey(r.FamilyName);
        var given = PatientService.SearchKey(r.GivenNames);
        var list = await patientsDb.Patients.AsNoTracking().Where(p => p.MergedIntoId == null)
            .Where(p => (r.Nhi != null && p.Nhi == r.Nhi) || (p.DateOfBirth == r.DateOfBirth && (p.FamilyNameSearch == family || p.GivenNamesSearch == given)))
            .Take(10).ToListAsync(ct);
        return list.Select(p => new EBookingMatch(p.Id, p.Mrn, p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth,
            r.Nhi != null && p.Nhi == r.Nhi
                ? (p.DateOfBirth == r.DateOfBirth && p.FamilyNameSearch == family ? "Same NHI, name and date of birth" : "Same NHI – but the name or date of birth differs, check before using")
                : p.Nhi != null && r.Nhi != null ? "Same date of birth and similar name – but a different NHI" : "Same date of birth and similar name"))
            .OrderBy(m => m.Reason.StartsWith("Same NHI, name", StringComparison.Ordinal) ? 0 : 1).ToList();
    }

    // ---------------------------------------------------------------- conversation

    public async Task<string?> AddMessageAsync(Guid id, string text, bool fromTds, string user, IReadOnlyCollection<Guid>? allowedSpecialists, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null || (allowedSpecialists is not null && !allowedSpecialists.Contains(r.SpecialistId))) return "Booking request not found.";
        if (string.IsNullOrWhiteSpace(text)) return "Write the message first.";
        if (text.Length > 2000) return "Keep the message under 2000 characters.";
        if (r.Status is not (EBookingStatus.Submitted or EBookingStatus.Query)) return "This request is closed. Phone the booking office if something has changed.";
        db.Messages.Add(new BookingRequestMessage { RequestId = r.Id, FromTds = fromTds, Author = user, Text = text.Trim(), At = clock.GetUtcNow() });
        if (fromTds) r.Status = EBookingStatus.Query;
        else if (r.Status == EBookingStatus.Query) r.Status = EBookingStatus.Submitted;
        r.StatusAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, fromTds ? "Query" : "Reply", "BookingRequest", r.Id.ToString(), r.PatientId?.ToString(), reason: $"{r.Number}: message {(fromTds ? "to" : "from")} the rooms", ct: ct);
        if (fromTds) await NotifyAsync(r, "question", ct);
        return null;
    }

    public async Task<string?> WithdrawAsync(Guid id, string reason, string user, IReadOnlyCollection<Guid>? allowedSpecialists, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null || (allowedSpecialists is not null && !allowedSpecialists.Contains(r.SpecialistId))) return "Booking request not found.";
        if (r.Status is not (EBookingStatus.Submitted or EBookingStatus.Query)) return "Only open requests can be withdrawn. Phone the booking office to cancel an accepted booking.";
        if (string.IsNullOrWhiteSpace(reason)) return "Enter the reason for withdrawing.";
        r.Status = EBookingStatus.Withdrawn; r.DeclineReason = Trunc(reason.Trim(), 500); r.StatusAt = clock.GetUtcNow(); r.DecidedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Withdraw", "BookingRequest", r.Id.ToString(), r.PatientId?.ToString(), reason: $"{r.Number}: {r.DeclineReason}", ct: ct);
        return null;
    }

    public async Task<string?> DeclineAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Booking request not found.";
        if (r.Status is not (EBookingStatus.Submitted or EBookingStatus.Query)) return $"This request has already been {r.Status.ToString().ToLowerInvariant()}.";
        if (string.IsNullOrWhiteSpace(reason)) return "Enter the reason for declining, so the rooms can be told.";
        r.Status = EBookingStatus.Declined; r.DeclineReason = Trunc(reason.Trim(), 500); r.StatusAt = clock.GetUtcNow(); r.DecidedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Decline", "BookingRequest", r.Id.ToString(), r.PatientId?.ToString(), reason: $"{r.Number}: {r.DeclineReason}", ct: ct);
        await NotifyAsync(r, "declined", ct);
        return null;
    }

    // ---------------------------------------------------------------- patient

    public async Task<string?> MatchPatientAsync(Guid id, Guid patientId, string user, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Booking request not found.";
        if (r.Status is not (EBookingStatus.Submitted or EBookingStatus.Query)) return "The patient can only be changed while the request is open.";
        var p = await patientsDb.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return "Patient not found.";
        if (p.MergedIntoId is not null) return "That record was merged into another – choose the surviving record.";
        if (r.Nhi is not null && p.Nhi is not null && p.Nhi != r.Nhi) return $"The rooms sent NHI {r.Nhi} but this patient's NHI is {p.Nhi}. Ask the rooms to check before matching.";
        r.PatientId = p.Id;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "MatchPatient", "BookingRequest", r.Id.ToString(), p.Id.ToString(), reason: $"{r.Number} matched to {p.Mrn}", ct: ct);
        return null;
    }

    /// <summary>Registers the patient from the details the rooms sent (the usual duplicate checks apply).</summary>
    public async Task<RegistrationResult> RegisterPatientAsync(Guid id, bool confirmedNotDuplicate, string user, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return new(null, ["Booking request not found."], []);
        if (r.Status is not (EBookingStatus.Submitted or EBookingStatus.Query)) return new(null, ["The patient can only be registered while the request is open."], []);
        if (r.PatientId is not null) return new(null, ["The patient is already matched."], []);
        var result = await patients.RegisterAsync(new PatientRegistration(r.FamilyName, r.GivenNames, r.DateOfBirth, r.Sex, r.Nhi, MobilePhone: r.MobilePhone, Email: r.Email,
            AddressLine1: r.AddressLine1, Suburb: r.Suburb, City: r.City, Postcode: r.Postcode, ConfirmedNotDuplicate: confirmedNotDuplicate), user, ct);
        if (result.Patient is null) return result;
        r.PatientId = result.Patient.Id;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "MatchPatient", "BookingRequest", r.Id.ToString(), r.PatientId.ToString(), reason: $"{r.Number}: registered as {result.Patient.Mrn}", ct: ct);
        return result;
    }

    // ---------------------------------------------------------------- accept

    /// <summary>Puts the request on the waitlist: a referral (from the rooms) and an accepted waitlist entry are created, and the files are filed in the patient record.</summary>
    public async Task<(BookingRequest? Request, IReadOnlyList<string> Errors)> AcceptAsync(Guid id, EBookingAccept a, string user, CancellationToken ct = default)
    {
        var r = await db.Requests.Include(x => x.Files).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return (null, ["Booking request not found."]);
        if (r.Status is not (EBookingStatus.Submitted or EBookingStatus.Query)) return (null, [$"This request has already been {r.Status.ToString().ToLowerInvariant()}."]);
        var errors = new List<string>();
        if (r.PatientId is null) errors.Add("Match the request to a registered patient (or register them) first.");
        var procedure = Blank(a.Procedure) ?? r.Procedure;
        if (procedure.Length > 300) errors.Add("Keep the procedure under 300 characters.");
        if (a.DurationMinutes is < 5 or > 720) errors.Add("The expected duration must be between 5 and 720 minutes.");
        if (!Enum.IsDefined(a.Laterality) || !Enum.IsDefined(a.Anaesthetic) || !Enum.IsDefined(a.Priority)) errors.Add("Choose the side, anaesthetic and urgency from the lists.");
        if (errors.Count > 0) return (null, errors);

        var specialist = await core.Clinicians.AsNoTracking().SingleOrDefaultAsync(c => c.Id == r.SpecialistId, ct);
        var hpi = specialist?.HpiCpn is { } cpn && Regex.IsMatch(cpn.Trim().ToUpperInvariant(), "^[0-9]{2}[A-Z]{4}$") ? cpn.Trim().ToUpperInvariant() : null;
        var received = r.SubmittedAt.NzDate();
        if (received > Today) received = Today;
        var reason = Trunc(r.ClinicalNotes ?? $"Booking request {r.Number} from {r.RoomsName ?? "the specialist's rooms"}: {r.Procedure}", 2000)!;
        var (referral, rErrors) = await referrals.CreateReferralAsync(new NewReferral(r.PatientId!.Value, received, ReferralSource.Specialist, r.ContactName, r.RoomsName ?? specialist?.PracticeName,
            hpi, r.SpecialistId, r.Service, r.Funder, r.FunderReference, reason), user, ct);
        if (referral is null) return (null, rErrors);
        var (entry, tErrors) = await referrals.TriageAsync(referral.Id, new TriageDecision(true, Trunc(WaitlistNotes(r, a.Notes), 1000), null, procedure, a.Laterality, a.Anaesthetic,
            a.DurationMinutes, a.Priority, received), user, ct);
        if (entry is null)
        {
            await referrals.WithdrawReferralAsync(referral.Id, $"Booking request {r.Number} could not be accepted", user, ct);
            return (null, tErrors);
        }

        var filed = 0;
        foreach (var f in r.Files.Where(f => f.PatientDocumentId is null))
        {
            var (doc, _) = await documents.AddAsync(new NewPatientDocument(r.PatientId.Value, null, f.Category, $"{f.Category} from {r.RoomsName ?? "rooms"} ({r.Number})", f.FileName,
                Convert.ToBase64String(cipher.Unprotect(f.Content)), DocumentSource.Portal), user, 20, ct);
            if (doc is not null) { f.PatientDocumentId = doc.Id; filed++; }
        }
        r.Status = EBookingStatus.Accepted; r.StatusAt = clock.GetUtcNow(); r.DecidedBy = user; r.ReferralId = referral.Id; r.WaitlistEntryId = entry.Id;
        db.Messages.Add(new BookingRequestMessage
        {
            RequestId = r.Id,
            FromTds = true,
            Author = user,
            At = clock.GetUtcNow(),
            Text = $"Accepted onto the waitlist ({entry.Priority}, target {entry.TargetDate:d MMM yyyy}). We will confirm the date with you and the patient."
        });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Accept", "BookingRequest", r.Id.ToString(), r.PatientId.ToString(),
            reason: $"{r.Number} → referral {referral.Number}, waitlist {entry.Id}; {filed} file(s) filed", ct: ct);
        await NotifyAsync(r, "accepted", ct);
        return (r, []);
    }

    /// <summary>The rooms' dates and pre-operative details, carried onto the waitlist entry so the booking office sees them when booking.</summary>
    public static string WaitlistNotes(BookingRequest r, string? officeNotes)
    {
        var parts = new List<string> { $"Booking request {r.Number}" };
        if (r.EarliestDate is not null || r.LatestDate is not null)
            parts.Add($"dates {(r.EarliestDate is { } e ? e.ToString("d MMM", CultureInfo.InvariantCulture) : "any")}–{(r.LatestDate is { } l ? l.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "any")}");
        if (r.Availability is { } av) parts.Add(av);
        if (r.Asa is { } asa) parts.Add($"ASA {asa}");
        if (Bmi(r) is { } bmi) parts.Add($"BMI {bmi:0.#}");
        if (r.Anticoagulated) parts.Add("anticoagulated" + (r.AnticoagulantDetails is { } d ? $" ({d})" : ""));
        if (r.Diabetic) parts.Add("diabetic");
        if (r.SleepApnoea) parts.Add("OSA");
        if (r.Allergies is { } al) parts.Add($"allergies: {al}");
        if (r.InterpreterLanguage is { } lang) parts.Add($"interpreter: {lang}");
        if (Blank(officeNotes) is { } n) parts.Add(n);
        return string.Join("; ", parts);
    }

    private async Task NotifyAsync(BookingRequest r, string what, CancellationToken ct)
    {
        foreach (var n in notifiers)
        {
            try { await n.NotifyAsync(r, what, ct); }
#pragma warning disable CA1031 // A failed email must not undo the booking office's decision.
            catch (Exception) { /* reported by the notifier's own log */ }
#pragma warning restore CA1031
        }
    }

    // ---------------------------------------------------------------- merge

    public async Task<int> MergeAsync(Guid survivorId, Guid duplicateId, CancellationToken ct = default) =>
        await db.Requests.Where(r => r.PatientId == duplicateId).ExecuteUpdateAsync(s => s.SetProperty(r => r.PatientId, survivorId), ct);

    // ---------------------------------------------------------------- helpers

    public static bool EmailOk(string v) => Regex.IsMatch(v.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$") && v.Trim().Length <= 200;
    internal static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    internal static string? Trunc(string? v, int max) => v is null ? null : v.Length <= max ? v : v[..max];
}
