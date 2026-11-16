using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Patients;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Coding;

public sealed record NbrsEntryEdit(Guid? Id, Guid PatientId, Guid? WaitlistEntryId, string LocalId, string BookingSource, DateOnly? ReferralDate, DateOnly? FirstAssessmentDate,
    DateOnly? CpacDate, string? CpacScore, string? CpacSystem, string PurchaserCode, string HealthSpecialty, string ClinicalCode, string ClinicalCodeType, string? DomicileCode,
    string? ProfessionalGroup, string? ResponsibilityCode, string? AssessorCode, string? AssessorGroup, string? StagedFlag);

public sealed record NbrsEventEdit(string Kind, DateOnly? StatusDate, DateOnly? CertaintyDate, DateOnly? BookingMadeDate, DateOnly? BookedForDate, string? DeferredBy,
    DateOnly? ExitDate, string? ExitCategory, string? ContractAgency, string? TreatmentFacility, string? ClientSystemId);

/// <summary>Result of loading a Health NZ acknowledgement file.</summary>
public sealed record NbrsAck(string FileName, int Accepted, int Rejected, int Warnings, IReadOnlyList<(int Line, bool Ok, IReadOnlyList<string> Messages)> Lines);

/// <summary>
/// National Booking Reporting System (file specification document 4.5, file version V04.0). Builds the comma-delimited ASCII batch
/// (CR LF; a BH header then one BE record per event) named AAA00001.NBR with gap-free sequence numbers per environment (CMPL compliance
/// testing, PROD production), checks each event locally against the NBRS error conditions it can (E1, E6–E9, E18, E26, E37, E38–E48 …),
/// and reads the acknowledgement file (.NBK: AH, AC/RJ with ER/WN) back onto each event.
/// </summary>
public sealed class NbrsService(CodingDbContext db, CodingService coding, PatientsDbContext patients, ReferralsDbContext referrals, PasDbContext core, TheatreDbContext theatre,
    AuditWriter audit, TimeProvider clock)
{
    /// <summary>Status changes BrightPAS sends; the kinds map to status/action codes.</summary>
    public static readonly IReadOnlyDictionary<string, (string Action, string? Status, string Label)> Kinds = new Dictionary<string, (string, string?, string)>
    {
        ["book"] = ("A", "01", "Booked (01)"),
        ["certainty"] = ("A", "02", "Given certainty (02)"),
        ["defer"] = ("A", "05", "Deferred (05)"),
        ["rebook"] = ("A", "06", "Rebooked (06)"),
        ["treated"] = ("A", "20", "Exit – treated (20, category 11)"),
        ["exit"] = ("A", "20", "Exit – other category (20)"),
        ["change"] = ("C", null, "Change data (C)"),
        ["delete"] = ("D", null, "Delete last status (D)"),
        ["erase"] = ("E", "99", "Erase entry (E)"),
    };

    public static readonly IReadOnlyDictionary<string, string> ExitCategories = new Dictionary<string, string>
    {
        ["11"] = "Treated – publicly funded elective",
        ["12"] = "Treated – acute",
        ["13"] = "Returned to primary care",
        ["14"] = "Changed patient circumstance",
        ["15"] = "Medically unfit",
    };

    private static string Ymd(DateOnly? d) => d is { } x ? x.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : "";

    /// <summary>Field value with no commas or line breaks (NBRS char fields are unquoted).</summary>
    private static string F(string? v, int max)
    {
        var s = NmdsService.ToAscii((v ?? "").Replace(",", " ", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim());
        return s.Length > max ? s[..max] : s;
    }

    public static string BatchFileName(string acronym, int number) => $"{acronym.ToUpperInvariant()}{number:D5}.NBR";

    /// <summary>One BE record in the specification's field order (35 fields).</summary>
    public static string BeRecord(NbrsEntry n, NbrsEvent e, string nhi, string codingSystem)
    {
        var book = e.Action == "A" && e.StatusCode is "01" or "02" or "05";
        // Status date and event local id go with the dated status changes 01, 02, 05, 06 (specification section 9.2).
        var dated = e.Action == "A" && e.StatusCode is "01" or "02" or "05" or "06";
        var f = new[]
        {
            "BE", e.Action, F(n.FacilityCode, 4), F(n.LocalId, 14), F(e.Action == "C" || e.Action == "D" ? null : e.StatusCode, 2),
            dated ? Ymd(e.StatusDate) : "", F(nhi, 7),
            book || e.Action == "C" ? Ymd(n.ReferralDate) : "", book || e.Action == "C" ? F(n.BookingSource, 1) : "", book ? Ymd(n.FirstAssessmentDate) : "",
            book || e.StatusCode is "06" or "07" ? Ymd(n.CpacDate) : "", book || e.StatusCode is "06" or "07" ? F(n.CpacScore, 5) : "", book || e.StatusCode is "06" or "07" ? F(n.CpacSystem, 4) : "",
            Ymd(e.CertaintyDate), Ymd(e.BookingMadeDate), Ymd(e.BookedForDate),
            book || e.StatusCode == "06" || e.Action == "C" ? F(n.PurchaserCode, 2) : "", F(e.ContractAgency, 4), F(e.TreatmentFacility, 4), F(e.ClientSystemId, 14),
            book || e.Action == "C" ? F(n.HealthSpecialty, 3) : "", "",
            book || e.Action == "C" ? F(n.ClinicalCode, 8) : "", book || e.Action == "C" ? F(n.ClinicalCodeType, 1) : "", book || e.Action == "C" ? F(codingSystem, 2) : "",
            F(e.DeferredBy, 1), Ymd(e.ExitDate), F(e.ExitCategory, 2), book || e.Action == "C" ? F(n.StagedFlag, 1) : "",
            dated ? e.EventLocalId.ToString("D2", CultureInfo.InvariantCulture) : "",
            F(n.ProfessionalGroup, 2), F(n.ResponsibilityCode, 10), F(n.DomicileCode, 4), F(n.AssessorCode, 10), F(n.AssessorGroup, 2),
        };
        return string.Join(',', f);
    }

    public static string BuildBatch(string agency, string acronym, int number, string environment, DateTimeOffset nowNz, IReadOnlyList<string> beRecords)
    {
        var sb = new StringBuilder();
        var date = nowNz.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        sb.Append(string.Join(',', "BH", F(agency, 4), BatchFileName(acronym, number), beRecords.Count.ToString("D5", CultureInfo.InvariantCulture), date, date,
            nowNz.ToString("HHmm", CultureInfo.InvariantCulture), environment, "V04.0")).Append("\r\n");
        foreach (var r in beRecords) sb.Append(r).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Local checks against the NBRS error conditions that can be tested before sending. Errors stop the event going in a batch.</summary>
    public static IReadOnlyList<string> Validate(NbrsEntry n, NbrsEvent e, string? nhi, IReadOnlyList<NbrsEvent> earlier, string agencyCode)
    {
        var r = new List<string>();
        if (nhi is null || !NhiValidator.Validate(nhi).IsValid) r.Add("E4: the patient needs a valid NHI number.");
        if (n.FacilityCode.Length != 4) r.Add("E17: facility code must be 4 characters.");
        if (string.IsNullOrWhiteSpace(n.LocalId) || n.LocalId.Length > 14) r.Add("Local booking system entry identifier: 1–14 characters.");
        if (agencyCode.Length != 4) r.Add("Set the NBRS agency code (Coding › Settings).");
        var lastStatus = earlier.Where(x => x.Result != NbrsResult.Rejected && x.StatusDate is not null && x.Action == "A").Select(x => x.StatusDate).Max();
        var exists = earlier.Any(x => x.Result != NbrsResult.Rejected && x.Action == "A");
        var exited = earlier.Any(x => x.Result != NbrsResult.Rejected && x.Action == "A" && x.StatusCode == "20");
        if (exited && e.Action != "D" && e.Action != "E") r.Add("E24: the entry has exited – only a delete or erase can follow.");
        var book = e.Action == "A" && e.StatusCode is "01" or "02" or "05";
        if (e.Action is "C" or "D" or "E" && !exists) r.Add("The entry must exist in NBRS (send its booking first).");
        if (e.Action == "A" && e.StatusCode is "06" or "20" && !exists) r.Add("E24: the entry does not exist yet – book it first.");
        if (e.Action == "A" && e.StatusCode != "20")
        {
            if (e.StatusDate is null) r.Add("Booking status date is required.");
            else if (lastStatus is { } ls && e.StatusDate <= ls) r.Add("E8: status date must be after the previous status date.");
        }
        if (book)
        {
            if (n.BookingSource.Length != 1 || !char.IsDigit(n.BookingSource[0])) r.Add("E16: booking source code is required.");
            if (n.BookingSource is "2" or "4" && n.ReferralDate is null) r.Add("E46: date of referral is required when the booking source is public specialist (2) or primary care (4).");
            if (n.FirstAssessmentDate is null) r.Add("E45: first assessment date is required.");
            if (n.FirstAssessmentDate is { } fsa && n.ReferralDate is { } rd && fsa < rd) r.Add("E6: first assessment date is before the referral date.");
            var cpac = new object?[] { n.CpacDate, n.CpacScore, n.CpacSystem };
            if (cpac.Any(x => x is null || x is string { Length: 0 })) r.Add("E26: CPAC assessment date, score and scoring system are all required.");
            if (n.CpacDate is { } cd && n.FirstAssessmentDate is { } f2 && cd < f2) r.Add("E1/E43: CPAC assessment date is before the first assessment date.");
            if (n.PurchaserCode.Length != 2) r.Add("E12: principal health service purchaser is required.");
            if (n.HealthSpecialty.Length != 3) r.Add("E13: health specialty code is 3 characters (e.g. S05).");
            if (n.ClinicalCode.Length == 0 || n.ClinicalCodeType.Length != 1) r.Add("E3/E15: clinical code and clinical code type are required.");
            if (string.IsNullOrWhiteSpace(n.DomicileCode) || n.DomicileCode.Length != 4) r.Add("E44: domicile code (4 characters) is required.");
            if (string.IsNullOrWhiteSpace(n.AssessorCode)) r.Add("E40: assessor code is required.");
            if (!string.IsNullOrWhiteSpace(n.AssessorCode) && n.AssessorGroup?.Length != 2) r.Add("E41: assessor group is required with the assessor code.");
            if (e.StatusCode == "02" && e.CertaintyDate is null) r.Add("Date certainty given is required for status 02.");
            if (e.StatusCode == "02" && e.CertaintyDate is { } cg && n.CpacDate is { } c2 && cg < c2) r.Add("E21: certainty date is before the CPAC assessment date.");
            if (e.StatusCode == "05" && string.IsNullOrWhiteSpace(e.DeferredBy)) r.Add("E25: deferred-by code is required for status 05.");
        }
        if (e.Action == "A" && e.StatusCode is "01" or "06")
        {
            if (e.BookingMadeDate is null || e.BookedForDate is null) r.Add("Date booking made and date booked for treatment are required.");
            if (e.BookingMadeDate is { } bm && n.CpacDate is { } c3 && bm < c3) r.Add("E7: booking made date is before the CPAC assessment date.");
            if (e.BookingMadeDate is { } bm2 && e.BookedForDate is { } bf && bf < bm2) r.Add("E7/E23: booked-for date is before the booking made date.");
        }
        if (e.Action == "A" && e.StatusCode is "01" or "02" or "05" or "06" or "07" || e.StatusCode == "20" && e.ExitCategory == "11")
        {
            if (string.IsNullOrWhiteSpace(n.ProfessionalGroup) || n.ProfessionalGroup.Length != 2) r.Add("E38: professional group code (MC for the Medical Council) is required.");
            if (string.IsNullOrWhiteSpace(n.ResponsibilityCode)) r.Add("E39: clinical responsibility code (the surgeon's registration number) is required.");
            else if (n.ProfessionalGroup == "MC" && !n.ResponsibilityCode.All(char.IsDigit)) r.Add("Medical Council codes are digits only.");
        }
        if (e.StatusCode == "20")
        {
            if (e.ExitDate is null) r.Add("Date of exit category is required.");
            else if (lastStatus is { } l2 && e.ExitDate <= l2) r.Add("E18: exit date must be after the latest booking status date.");
            if (e.ExitCategory is null || !ExitCategories.ContainsKey(e.ExitCategory)) r.Add("E27: choose an exit category.");
            if (e.ExitCategory is "11" or "12" && string.IsNullOrWhiteSpace(e.ClientSystemId)) r.Add("E48: client system identifier (the event's NMDS record id) is required for treated exits.");
        }
        if (e.EventLocalId is < 0 or > 99) r.Add("E37: event local id 00–99.");
        return r;
    }

    // ------------------------------------------------------------------ entries and events
    public async Task<IReadOnlyList<NbrsEntry>> EntriesAsync(CancellationToken ct = default) =>
        await db.NbrsEntries.AsNoTracking().Include(x => x.Events).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);

    /// <summary>A new entry pre-filled from a Health NZ funded waitlist entry (the coder completes the CPAC and assessment fields from the District's referral).</summary>
    public async Task<NbrsEntryEdit> DraftFromWaitlistAsync(Guid waitlistId, CancellationToken ct = default)
    {
        var w = await referrals.Waitlist.AsNoTracking().SingleOrDefaultAsync(x => x.Id == waitlistId, ct) ?? throw new KeyNotFoundException("Waitlist entry not found.");
        if (w.Funder != Funder.HealthNz) throw new InvalidOperationException("Only Health NZ funded (outsourced) waitlist entries are reported to NBRS.");
        var rs = await referrals.Settings.AsNoTracking().SingleOrDefaultAsync(ct);
        var r = w.ReferralId is { } rid ? await referrals.Referrals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rid, ct) : null;
        var reg = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(x => x.ClinicianId == w.SpecialistId, ct);
        var s = await coding.SettingsAsync(ct);
        var proc = await core.Procedures.AsNoTracking().Where(p => p.Name == w.Procedure).Select(p => p.Code).FirstOrDefaultAsync(ct);
        var map = await db.ProcedureMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Term == proc || m.Label == w.Procedure, ct);
        var source = r?.Source switch { ReferralSource.GeneralPractice => "4", ReferralSource.Specialist or ReferralSource.HealthNz => "2", _ => "" };
        return new(null, w.PatientId, w.Id, "W" + w.Id.ToString("N")[..13].ToUpperInvariant(), source, r?.ReceivedOn, r?.TriagedAt?.NzDate() ?? r?.ReceivedOn, r?.TriagedAt?.NzDate(),
            null, null, s.PurchaserHealthNz ?? "35", (w.Service == ServiceType.Endoscopy ? rs?.NbrsSpecialtyEndoscopy : rs?.NbrsSpecialtySurgical) ?? "",
            map is null ? "" : CodingChecks.Normalise(map.ProcedureCodes.Split(' ')[0]), "O", null, reg?.ProfessionalGroup ?? "MC", reg?.RegistrationNumber, reg?.RegistrationNumber, reg?.ProfessionalGroup ?? "MC", "1");
    }

    public async Task<(NbrsEntry? Row, IReadOnlyList<string> Errors)> SaveEntryAsync(NbrsEntryEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var s = await coding.SettingsAsync(ct);
        var facility = s.FacilityCode;
        if (facility is null) errors.Add("Set the facility code (Coding › Settings) first.");
        var local = (e.LocalId ?? "").Trim().ToUpperInvariant();
        if (local.Length is 0 or > 14) errors.Add("Local booking id: 1–14 characters.");
        if (!await patients.Patients.AnyAsync(p => p.Id == e.PatientId, ct)) errors.Add("Patient not found.");
        if (e.CpacScore is { Length: > 5 }) errors.Add("CPAC score: at most 5 characters.");
        if (e.CpacSystem is { Length: > 4 }) errors.Add("CPAC scoring system: at most 4 characters.");
        if (e.StagedFlag is { Length: > 0 } sf && sf is not ("1" or "2" or "3" or "4")) errors.Add("Staged/planned flag: 1 normal, 2 staged, 3 planned, 4 surveillance.");
        if (await db.NbrsEntries.AnyAsync(x => x.LocalId == local && x.FacilityCode == facility && x.Id != e.Id, ct)) errors.Add("That local booking id is already used.");
        if (errors.Count > 0) return (null, errors);
        var n = e.Id is { } id ? await db.NbrsEntries.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("NBRS entry not found.") : null;
        if (n is null) { n = new NbrsEntry { CreatedBy = user, CreatedAt = clock.GetUtcNow() }; db.NbrsEntries.Add(n); }
        else if (n.LocalId != local && await db.NbrsEvents.AnyAsync(x => x.EntryId == n.Id && x.Result != NbrsResult.Pending, ct)) return (null, ["The local id cannot change once the entry has been sent."]);
        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToUpperInvariant();
        n.LocalId = local; n.FacilityCode = facility!; n.PatientId = e.PatientId; n.WaitlistEntryId = e.WaitlistEntryId; n.BookingSource = (e.BookingSource ?? "").Trim();
        n.ReferralDate = e.ReferralDate; n.FirstAssessmentDate = e.FirstAssessmentDate; n.CpacDate = e.CpacDate; n.CpacScore = T(e.CpacScore); n.CpacSystem = T(e.CpacSystem);
        n.PurchaserCode = T(e.PurchaserCode) ?? ""; n.HealthSpecialty = T(e.HealthSpecialty) ?? ""; n.ClinicalCode = CodingChecks.Normalise(e.ClinicalCode); n.ClinicalCodeType = T(e.ClinicalCodeType) ?? "O";
        n.DomicileCode = T(e.DomicileCode); n.ProfessionalGroup = T(e.ProfessionalGroup); n.ResponsibilityCode = T(e.ResponsibilityCode); n.AssessorCode = T(e.AssessorCode);
        n.AssessorGroup = T(e.AssessorGroup); n.StagedFlag = T(e.StagedFlag);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "NbrsEntry", n.Id.ToString(), n.PatientId.ToString(), reason: n.LocalId, ct: ct);
        return (n, []);
    }

    public async Task<(NbrsEvent? Row, IReadOnlyList<string> Errors)> AddEventAsync(Guid entryId, NbrsEventEdit e, string user, CancellationToken ct = default)
    {
        if (!Kinds.TryGetValue(e.Kind ?? "", out var kind)) return (null, ["Choose what happened (book, certainty, defer, rebook, exit, change, delete or erase)."]);
        var n = await db.NbrsEntries.Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == entryId, ct) ?? throw new KeyNotFoundException("NBRS entry not found.");
        if (n.Events.Any(x => x.Result == NbrsResult.Pending)) return (null, ["An earlier event for this entry has not been sent yet – send or remove it first."]);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == n.PatientId, ct);
        var s = await coding.SettingsAsync(ct);
        var ev = new NbrsEvent
        {
            EntryId = n.Id,
            Action = kind.Action,
            StatusCode = e.Kind == "exit" || e.Kind == "treated" ? "20" : kind.Status,
            StatusDate = e.StatusDate,
            CertaintyDate = e.CertaintyDate,
            BookingMadeDate = e.BookingMadeDate,
            BookedForDate = e.BookedForDate,
            DeferredBy = e.DeferredBy?.Trim(),
            ExitDate = e.ExitDate,
            ExitCategory = e.Kind == "treated" ? "11" : e.ExitCategory?.Trim(),
            ContractAgency = e.ContractAgency?.Trim(),
            TreatmentFacility = e.TreatmentFacility?.Trim() ?? (e.Kind == "treated" ? s.FacilityCode : null),
            ClientSystemId = e.ClientSystemId?.Trim(),
            CreatedBy = user,
            CreatedAt = clock.GetUtcNow(),
        };
        // Event local id distinguishes several events for the patient on the same day.
        var sameDay = n.Events.Count(x => (x.StatusDate ?? x.ExitDate) == (ev.StatusDate ?? ev.ExitDate));
        ev.EventLocalId = sameDay;
        var errors = Validate(n, ev, p.Nhi, n.Events.OrderBy(x => x.CreatedAt).ToList(), s.NbrsAgencyCode ?? "");
        if (errors.Count > 0) return (null, errors);
        db.NbrsEvents.Add(ev);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Add", "NbrsEvent", ev.Id.ToString(), n.PatientId.ToString(), reason: $"{n.LocalId} {kind.Label}", ct: ct);
        return (ev, []);
    }

    public async Task<string?> RemovePendingAsync(Guid eventId, string user, CancellationToken ct = default)
    {
        var ev = await db.NbrsEvents.SingleOrDefaultAsync(x => x.Id == eventId, ct) ?? throw new KeyNotFoundException("Event not found.");
        if (ev.Result != NbrsResult.Pending) return "Only an event not yet sent can be removed – send a delete (D) instead.";
        db.NbrsEvents.Remove(ev);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Remove", "NbrsEvent", ev.Id.ToString(), reason: "pending event removed", ct: ct);
        return null;
    }

    /// <summary>The client system identifier for a treated exit: the NMDS record id of the coded event for the entry's booking.</summary>
    public async Task<string?> ClientSystemIdAsync(Guid entryId, CancellationToken ct = default)
    {
        var n = await db.NbrsEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entryId, ct);
        if (n?.WaitlistEntryId is not { } wid) return null;
        var bookingId = await referrals.Waitlist.AsNoTracking().Where(w => w.Id == wid).Select(w => w.BookingId).SingleOrDefaultAsync(ct);
        if (bookingId is null) return null;
        var ce = await db.Episodes.AsNoTracking().FirstOrDefaultAsync(e => e.BookingId == bookingId, ct);
        return ce is null ? null : NmdsService.RecordId(ce);
    }

    // ------------------------------------------------------------------ batches
    public async Task<(NbrsBatch? Batch, IReadOnlyList<string> Errors)> CreateBatchAsync(string user, CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(ct);
        if (s?.FileAcronym is null || s.NbrsAgencyCode is null || s.FacilityCode is null) return (null, ["Set the file acronym, facility code and NBRS agency code (Coding › Settings)."]);
        var pending = await db.NbrsEvents.Where(x => x.Result == NbrsResult.Pending).OrderBy(x => x.CreatedAt).ToListAsync(ct);
        if (pending.Count == 0) return (null, ["No events are waiting to be sent."]);
        var entryIds = pending.Select(x => x.EntryId).Distinct().ToList();
        var entries = await db.NbrsEntries.AsNoTracking().Include(x => x.Events).Where(x => entryIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var pids = entries.Values.Select(x => x.PatientId).Distinct().ToList();
        var nhis = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nhi, ct);
        var errors = new List<string>();
        foreach (var ev in pending)
        {
            var n = entries[ev.EntryId];
            var problems = Validate(n, ev, nhis.GetValueOrDefault(n.PatientId), n.Events.Where(x => x.Id != ev.Id && x.CreatedAt <= ev.CreatedAt).ToList(), s.NbrsAgencyCode);
            if (problems.Count > 0) errors.Add($"{n.LocalId}: {string.Join(" ", problems)}");
        }
        if (errors.Count > 0) return (null, errors);
        var env = s.NbrsEnvironment;
        var number = env == "PROD" ? s.NextNbrsBatchProd : s.NextNbrsBatchCmpl;
        if (number > 99999) return (null, ["The batch sequence has reached 99999 – ask Health NZ how to continue."]);
        var records = pending.Select(ev => BeRecord(entries[ev.EntryId], ev, nhis.GetValueOrDefault(entries[ev.EntryId].PatientId) ?? "", s.CodingSystem)).ToList();
        var content = BuildBatch(s.NbrsAgencyCode, s.FileAcronym, number, env, clock.GetUtcNow().ToNz(), records);
        var batch = new NbrsBatch { Environment = env, Number = number, FileName = BatchFileName(s.FileAcronym, number), Records = records.Count, Content = Encoding.ASCII.GetBytes(content), CreatedBy = user, CreatedAt = clock.GetUtcNow() };
        db.NbrsBatches.Add(batch);
        for (var i = 0; i < pending.Count; i++) { pending[i].BatchId = batch.Id; pending[i].LineNumber = i + 2; pending[i].Result = NbrsResult.Sent; }
        if (env == "PROD") s.NextNbrsBatchProd++; else s.NextNbrsBatchCmpl++;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "NbrsBatch", batch.Id.ToString(), reason: $"{batch.FileName} ({env}): {batch.Records} event(s)", ct: ct);
        return (batch, []);
    }

    /// <summary>Parses an acknowledgement file: AH header, then AC (accepted) or RJ (rejected) per input line, each followed by its ER/WN messages.</summary>
    public static NbrsAck ParseAck(string text)
    {
        var lines = new List<(int Line, bool Ok, IReadOnlyList<string> Messages)>();
        string file = ""; int accepted = 0, rejected = 0, warnings = 0;
        (int Line, bool Ok, List<string> Msgs)? current = null;
        foreach (var row in Csv.Parse(text))
        {
            var t = row.Count > 0 ? row[0].Trim().ToUpperInvariant() : "";
            switch (t)
            {
                case "AH": file = row.Count > 2 ? row[2].Trim() : ""; break;
                case "AC":
                case "RJ":
                    if (current is { } c) lines.Add((c.Line, c.Ok, c.Msgs));
                    current = (row.Count > 1 && int.TryParse(row[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ln) ? ln : 0, t == "AC", new List<string>());
                    if (t == "AC") accepted++; else rejected++;
                    break;
                case "ER":
                case "WN":
                    if (t == "WN") warnings++;
                    current?.Msgs.Add($"{(t == "ER" ? "Error" : "Warning")} {(row.Count > 1 ? row[1].Trim() : "")}: {(row.Count > 2 ? row[2].Trim() : "")}");
                    break;
            }
        }
        if (current is { } last) lines.Add((last.Line, last.Ok, last.Msgs));
        return new(file, accepted, rejected, warnings, lines);
    }

    public async Task<(NbrsAck? Ack, IReadOnlyList<string> Errors)> ImportAckAsync(string text, string user, CancellationToken ct = default)
    {
        var ack = ParseAck(text);
        if (ack.Lines.Count == 0) return (null, ["No AC or RJ records found – is this an NBRS acknowledgement (.NBK) file?"]);
        var name = Path.GetFileNameWithoutExtension(ack.FileName);
        var batch = await db.NbrsBatches.SingleOrDefaultAsync(b => b.FileName.StartsWith(name + "."), ct);
        if (batch is null || name.Length == 0) return (null, [$"No batch named {ack.FileName} was created by BrightPAS."]);
        var events = await db.NbrsEvents.Where(x => x.BatchId == batch.Id).ToDictionaryAsync(x => x.LineNumber ?? 0, ct);
        foreach (var l in ack.Lines)
        {
            if (!events.TryGetValue(l.Line, out var ev)) continue;
            ev.Result = l.Ok ? NbrsResult.Accepted : NbrsResult.Rejected;
            ev.Messages = l.Messages.Count == 0 ? null : string.Join("\n", l.Messages) is { Length: > 2000 } m ? m[..2000] : string.Join("\n", l.Messages);
        }
        batch.Accepted = ack.Accepted; batch.Rejected = ack.Rejected; batch.Warnings = ack.Warnings; batch.AcknowledgedAt = clock.GetUtcNow();
        batch.Status = ack.Rejected > 0 ? SubmissionStatus.Errors : SubmissionStatus.Accepted;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Acknowledge", "NbrsBatch", batch.Id.ToString(), reason: $"{batch.FileName}: {ack.Accepted} accepted, {ack.Rejected} rejected, {ack.Warnings} warning(s)", ct: ct);
        return (ack, []);
    }

    public async Task<IReadOnlyList<NbrsBatch>> BatchesAsync(CancellationToken ct = default) =>
        await db.NbrsBatches.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(100).Select(b => new NbrsBatch
        {
            Id = b.Id,
            Environment = b.Environment,
            Number = b.Number,
            FileName = b.FileName,
            Records = b.Records,
            Status = b.Status,
            Accepted = b.Accepted,
            Rejected = b.Rejected,
            Warnings = b.Warnings,
            CreatedBy = b.CreatedBy,
            CreatedAt = b.CreatedAt,
            AcknowledgedAt = b.AcknowledgedAt,
        }).ToListAsync(ct);

    public async Task<NbrsBatch> BatchAsync(Guid id, string user, CancellationToken ct = default)
    {
        var b = await db.NbrsBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Batch not found.");
        await audit.WriteAsync(user, "Export", "NbrsBatch", b.Id.ToString(), reason: $"{b.FileName} downloaded (health information)", ct: ct);
        return b;
    }

    /// <summary>Health NZ funded waitlist entries that have no NBRS entry yet.</summary>
    public async Task<IReadOnlyList<(Guid Id, Guid PatientId, string Procedure, DateOnly AddedOn, string Status)>> UnreportedWaitlistAsync(CancellationToken ct = default)
    {
        var have = await db.NbrsEntries.AsNoTracking().Where(x => x.WaitlistEntryId != null).Select(x => x.WaitlistEntryId!.Value).ToListAsync(ct);
        var rows = await referrals.Waitlist.AsNoTracking().Where(w => w.Funder == Funder.HealthNz && !have.Contains(w.Id)).OrderBy(w => w.AddedOn).Take(200).ToListAsync(ct);
        return rows.Select(w => (w.Id, w.PatientId, w.Procedure, w.AddedOn, w.Status.ToString())).ToList();
    }

    /// <summary>The booking for a waitlist entry, to pre-fill the booked event.</summary>
    public async Task<(DateOnly? MadeOn, DateOnly? BookedFor)> BookingDatesAsync(Guid entryId, CancellationToken ct = default)
    {
        var n = await db.NbrsEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entryId, ct);
        if (n?.WaitlistEntryId is not { } wid) return (null, null);
        var w = await referrals.Waitlist.AsNoTracking().SingleOrDefaultAsync(x => x.Id == wid, ct);
        if (w?.BookingId is not { } bid) return (null, w?.BookedFor);
        var b = await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bid, ct);
        return (b?.CreatedAt.NzDate(), w.BookedFor);
    }
}
