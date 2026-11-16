using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Coding;

public sealed record CodingSettingsEdit(string? HealthAgencyCode, string? FacilityCode, string? FileAcronym, string? PurchaserSelfFunded, string? PurchaserInsurer,
    string? PurchaserAcc, string? PurchaserHealthNz, string? PurchaserNoCharge, bool TdsReportsContractedEvents, int ReportWithinDays, string? NbrsAgencyCode, string NbrsEnvironment,
    string CodingSystem, string EditionName);

public sealed record ItemEdit(string DiagnosisType, string Code, string Description, DateOnly? Date);

public sealed record CodingSave(int Version, string PurchaserCode, string AdmissionSource, string EventEndType, string? CoderNote, IReadOnlyList<ItemEdit> Items, bool MarkCoded);

public sealed record AdviceEdit(Guid? Id, string Reference, string Title, AdviceSource Source, string Edition, bool AppliesToNz, DateOnly? ReviewedOn, string CodePrefixes, string Summary, string? Link, bool IsActive);

public sealed record ProcedureMapEdit(Guid? Id, string Term, string Label, string ProcedureCodes, string? DiagnosisCode);

/// <summary>Everything the workbench shows for one event.</summary>
public sealed record Workbench(CodedEpisode Episode, Patient Patient, string Procedure, string? Anaesthetic, string? Surgeon, string? DischargeDestination,
    IReadOnlyList<CodingCheck> Checks, IReadOnlyList<ProcedureMap> Suggestions, IReadOnlyDictionary<string, string> Descriptions);

public sealed record CodeImportResult(int Added, int Updated, IReadOnlyList<string> Problems);

/// <summary>Coder workbench, code tables, advice library and the queue of events to code (spec 7.11).</summary>
public sealed class CodingService(CodingDbContext db, EmrDbContext emr, TheatreDbContext theatre, PatientsDbContext patients, PasDbContext core, AuditWriter audit, TimeProvider clock)
{
    private DateOnly Today => NzTime.Today(clock);

    // ------------------------------------------------------------------ settings
    public async Task<CodingSettings> SettingsAsync(CancellationToken ct = default) =>
        await db.Settings.SingleOrDefaultAsync(ct) ?? new CodingSettings
        {
            PurchaserSelfFunded = "06",
            PurchaserInsurer = "06",
            PurchaserAcc = "A0",
            PurchaserHealthNz = "35",
            PurchaserNoCharge = "06",
        };

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(CodingSettingsEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        static string? Up(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
        var agency = Up(e.HealthAgencyCode); var facility = Up(e.FacilityCode); var acronym = Up(e.FileAcronym); var nbrsAgency = Up(e.NbrsAgencyCode);
        if (agency is not null && agency.Length != 4) errors.Add("Health agency code is 4 characters (allocated by Health NZ).");
        if (facility is not null && facility.Length != 4) errors.Add("Health agency facility (HAF) code is 4 characters.");
        if (acronym is not null && (acronym.Length != 3 || !acronym.All(char.IsLetter))) errors.Add("File acronym is 3 letters (allocated by Health NZ).");
        if (nbrsAgency is not null && nbrsAgency.Length != 4) errors.Add("NBRS agency code is 4 characters.");
        var purchasers = new[] { e.PurchaserSelfFunded, e.PurchaserInsurer, e.PurchaserAcc, e.PurchaserHealthNz, e.PurchaserNoCharge }.Select(Up).ToArray();
        if (purchasers.Any(p => p is null || !CodingChecks.PurchaserCodes.ContainsKey(p))) errors.Add("Choose a current purchaser code for every payment type.");
        if (e.ReportWithinDays is < 1 or > 90) errors.Add("Reporting time: 1–90 days after discharge.");
        if (e.NbrsEnvironment is not ("CMPL" or "PROD")) errors.Add("NBRS environment is CMPL (compliance testing) or PROD.");
        if (string.IsNullOrWhiteSpace(e.CodingSystem) || e.CodingSystem.Trim().Length != 2 || !e.CodingSystem.Trim().All(char.IsDigit)) errors.Add("Clinical coding system id is 2 digits (16 = Twelfth Edition).");
        if (string.IsNullOrWhiteSpace(e.EditionName)) errors.Add("Name the classification edition in use.");
        if (errors.Count > 0) return errors;
        var s = await db.Settings.SingleOrDefaultAsync(ct);
        if (s is null) { s = new CodingSettings(); db.Settings.Add(s); }
        s.HealthAgencyCode = agency; s.FacilityCode = facility; s.FileAcronym = acronym; s.NbrsAgencyCode = nbrsAgency;
        (s.PurchaserSelfFunded, s.PurchaserInsurer, s.PurchaserAcc, s.PurchaserHealthNz, s.PurchaserNoCharge) = (purchasers[0], purchasers[1], purchasers[2], purchasers[3], purchasers[4]);
        s.TdsReportsContractedEvents = e.TdsReportsContractedEvents; s.ReportWithinDays = e.ReportWithinDays; s.NbrsEnvironment = e.NbrsEnvironment;
        s.CodingSystem = e.CodingSystem.Trim(); s.EditionName = e.EditionName.Trim();
        s.UpdatedBy = user; s.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "CodingSettings", "1", reason: $"agency {agency}, HAF {facility}, acronym {acronym}, edition {s.CodingSystem}, contracted events by TDS: {e.TdsReportsContractedEvents}", ct: ct);
        return [];
    }

    public static string PurchaserFor(PaymentType p, CodingSettings s) => p switch
    {
        PaymentType.Insurer => s.PurchaserInsurer ?? "06",
        PaymentType.Acc => s.PurchaserAcc ?? "A0",
        PaymentType.HealthNz => s.PurchaserHealthNz ?? "35",
        PaymentType.NoCharge => s.PurchaserNoCharge ?? "06",
        _ => s.PurchaserSelfFunded ?? "06",
    };

    // ------------------------------------------------------------------ code table
    /// <summary>
    /// Imports the clinical code table (CSV with a header row). Columns are found by name: clinical_code_system, clinical_code,
    /// clinical_code_type, clinical_code_description (or description), sex_flag, age_from, age_to, never_assigned_diagnosis_flag,
    /// principal_flag (N = not acceptable as principal) and end_date / active. Codes already held are updated.
    /// </summary>
    public async Task<CodeImportResult> ImportCodesAsync(string csv, string user, CancellationToken ct = default)
    {
        var problems = new List<string>();
        var rows = Csv.Parse(csv);
        if (rows.Count < 2) return new(0, 0, ["The file has no rows under its header."]);
        var head = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => names.Select(n => head.IndexOf(n)).FirstOrDefault(i => i >= 0, -1);
        int cSys = Col("clinical_code_system", "system", "coding_system"), cCode = Col("clinical_code", "code"), cType = Col("clinical_code_type", "type", "code_type"),
            cDesc = Col("clinical_code_description", "code_description", "description", "short_description"), cSex = Col("sex_flag", "sex"), cFrom = Col("age_from", "age_min"),
            cTo = Col("age_to", "age_max"), cNever = Col("never_assigned_diagnosis_flag", "never_assign"), cPrin = Col("principal_flag", "principal"), cEnd = Col("end_date"), cActive = Col("active", "is_active");
        if (cCode < 0 || cType < 0 || cDesc < 0) return new(0, 0, ["The header must name at least clinical_code, clinical_code_type and a description column."]);
        var s = await SettingsAsync(ct);
        var held = await db.Codes.ToDictionaryAsync(c => (c.System, c.Code, c.Type), ct);
        int added = 0, updated = 0;
        var today = Today;
        for (var n = 1; n < rows.Count; n++)
        {
            var r = rows[n];
            string V(int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
            var sys = cSys >= 0 ? V(cSys) : s.CodingSystem;
            var code = CodingChecks.Normalise(V(cCode)); var type = V(cType).ToUpperInvariant(); var desc = V(cDesc);
            if (code.Length == 0 && desc.Length == 0) continue;
            if (code.Length is 0 or > 8 || type.Length != 1 || "ABEMOV".IndexOf(type[0], StringComparison.Ordinal) < 0 || desc.Length == 0 || sys.Length is 0 or > 2)
            { if (problems.Count < 20) problems.Add($"Row {n + 1}: skipped (code, type or description missing or invalid)."); continue; }
            var active = true;
            if (cEnd >= 0 && V(cEnd).Length > 0 && TryDate(V(cEnd), out var end) && end < today) active = false;
            if (cActive >= 0 && V(cActive).Length > 0) active = V(cActive).ToUpperInvariant() is "Y" or "YES" or "1" or "TRUE";
            var sex = V(cSex).ToUpperInvariant() is "M" or "F" ? V(cSex).ToUpperInvariant() : null;
            int? from = int.TryParse(V(cFrom), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) ? f : null;
            int? to = int.TryParse(V(cTo), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : null;
            var never = V(cNever).ToUpperInvariant() is "Y" or "1" or "TRUE";
            var notPrincipal = cPrin >= 0 && V(cPrin).ToUpperInvariant() is "N" or "0" or "FALSE";
            if (!held.TryGetValue((sys, code, type), out var c))
            {
                c = new ClinicalCode { System = sys, Code = code, Type = type };
                db.Codes.Add(c); held[(sys, code, type)] = c; added++;
            }
            else updated++;
            c.Description = desc.Length > 250 ? desc[..250] : desc; c.Sex = sex; c.AgeFromYears = from; c.AgeToYears = to; c.NeverAssign = never; c.NotPrincipal = notPrincipal; c.IsActive = active;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Import", "ClinicalCodeTable", s.CodingSystem, reason: $"{added} added, {updated} updated, {problems.Count} row problem(s)", ct: ct);
        return new(added, updated, problems);
    }

    private static bool TryDate(string v, out DateOnly d) =>
        DateOnly.TryParseExact(v, ["yyyyMMdd", "yyyy-MM-dd", "d/MM/yyyy", "dd/MM/yyyy", "d-MMM-yy", "dd-MMM-yy", "d-MMM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    public async Task<IReadOnlyList<ClinicalCode>> SearchCodesAsync(string term, string? types, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        term = (term ?? "").Trim();
        if (term.Length < 2) return [];
        var code = CodingChecks.Normalise(term);
        var q = db.Codes.AsNoTracking().Where(c => c.System == s.CodingSystem && c.IsActive);
        if (!string.IsNullOrEmpty(types)) { var ts = types.ToUpperInvariant().ToCharArray().Select(x => x.ToString()).ToList(); q = q.Where(c => ts.Contains(c.Type)); }
        var byCode = code.Length >= 2 ? await q.Where(c => c.Code.StartsWith(code)).OrderBy(c => c.Code).Take(30).ToListAsync(ct) : [];
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var byText = q;
        foreach (var w in words)
        {
            var pattern = "%" + w.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal) + "%";
            byText = byText.Where(c => EF.Functions.Like(c.Description, pattern));
        }
        var text = await byText.OrderBy(c => c.Code).Take(30).ToListAsync(ct);
        return byCode.Concat(text).DistinctBy(c => c.Id).Take(40).ToList();
    }

    public async Task<int> CodeCountAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        return await db.Codes.CountAsync(c => c.System == s.CodingSystem && c.IsActive, ct);
    }

    private async Task<Dictionary<string, ClinicalCode>> TableForAsync(IEnumerable<string> codes, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var list = codes.Select(CodingChecks.Normalise).Where(c => c.Length > 0).Distinct().ToList();
        var rows = await db.Codes.AsNoTracking().Where(c => c.System == s.CodingSystem && list.Contains(c.Code)).ToListAsync(ct);
        // A code can sit in two type sections (rare); prefer the active one.
        return rows.GroupBy(c => c.Code).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.IsActive).First());
    }

    // ------------------------------------------------------------------ queue
    /// <summary>Adds every discharged eMR episode not yet in the coding queue. Contracted Health NZ events are marked not reportable unless TDS reports them.</summary>
    public async Task<int> RefreshQueueAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var held = await db.Episodes.AsNoTracking().Select(e => e.EpisodeId).ToListAsync(ct);
        var heldSet = held.ToHashSet();
        var done = await emr.Episodes.AsNoTracking().Where(e => (e.Stage == EpisodeStage.Discharged || e.Stage == EpisodeStage.Transferred) && e.DischargedAt != null)
            .OrderBy(e => e.DischargedAt).Take(2000).ToListAsync(ct);
        var fresh = done.Where(e => !heldSet.Contains(e.Id)).ToList();
        if (fresh.Count == 0) return 0;
        var bookingIds = fresh.Select(e => e.BookingId).ToList();
        var bookings = await theatre.Bookings.AsNoTracking().Where(b => bookingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var now = clock.GetUtcNow();
        foreach (var e in fresh)
        {
            bookings.TryGetValue(e.BookingId, out var b);
            var purchaser = PurchaserFor(b?.Payment ?? PaymentType.SelfFunded, s);
            var admitted = (e.AdmittedAt ?? e.DischargedAt!.Value).NzDate();
            var ce = new CodedEpisode
            {
                EpisodeId = e.Id,
                PatientId = e.PatientId,
                BookingId = e.BookingId,
                AdmissionDate = admitted,
                DischargeDate = e.DischargedAt!.Value.NzDate(),
                PurchaserCode = purchaser,
                EventEndType = e.Stage == EpisodeStage.Transferred ? "DA" : "DR",
                AdmissionSource = "R",
                CreatedAt = now,
            };
            if (b?.Payment == PaymentType.HealthNz && !s.TdsReportsContractedEvents)
            {
                ce.Status = CodingStatus.NotReportable;
                ce.NotReportableReason = "Contracted Health NZ event – reported to the NMDS by the District (NZ convention on ACS 0029). Still coded for TDS's own records if wanted.";
            }
            db.Episodes.Add(ce);
        }
        await db.SaveChangesAsync(ct);
        return fresh.Count;
    }

    public sealed record QueueRow(Guid Id, Guid PatientId, string Patient, string? Nhi, DateOnly Discharged, string Procedure, string Purchaser, string Status, int Items, bool Overdue, string? Error);

    public async Task<IReadOnlyList<QueueRow>> QueueAsync(string? status, CancellationToken ct = default)
    {
        await RefreshQueueAsync(ct);
        var s = await SettingsAsync(ct);
        var q = db.Episodes.AsNoTracking().Include(e => e.Items).AsQueryable();
        if (Enum.TryParse<CodingStatus>(status, out var st)) q = q.Where(e => e.Status == st);
        else if (status == "open") q = q.Where(e => e.Status == CodingStatus.ToCode || e.Status == CodingStatus.InProgress || e.Status == CodingStatus.Rejected);
        var rows = await q.OrderBy(e => e.DischargeDate).Take(500).ToListAsync(ct);
        var pids = rows.Select(r => r.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var bids = rows.Select(r => r.BookingId).Distinct().ToList();
        var procs = await theatre.Bookings.AsNoTracking().Where(b => bids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Procedure, ct);
        var today = Today;
        return rows.Select(r =>
        {
            people.TryGetValue(r.PatientId, out var p);
            var overdue = r.Status is not (CodingStatus.Submitted or CodingStatus.NotReportable) && today.DayNumber - r.DischargeDate.DayNumber > s.ReportWithinDays;
            return new QueueRow(r.Id, r.PatientId, p is null ? "?" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, r.DischargeDate,
                procs.TryGetValue(r.BookingId, out var pr) ? pr : "", r.PurchaserCode, r.Status.ToString(), r.Items.Count, overdue, r.HealthNzError);
        }).ToList();
    }

    public sealed record Summary(int ToCode, int InProgress, int Coded, int Rejected, int Overdue, int NotReportable, int Codes, bool SettingsDone);

    public async Task<Summary> SummaryAsync(CancellationToken ct = default)
    {
        var rows = await QueueAsync(null, ct);
        var s = await SettingsAsync(ct);
        int Count(CodingStatus x) => rows.Count(r => r.Status == x.ToString());
        return new(Count(CodingStatus.ToCode), Count(CodingStatus.InProgress), Count(CodingStatus.Coded), Count(CodingStatus.Rejected), rows.Count(r => r.Overdue),
            Count(CodingStatus.NotReportable), await CodeCountAsync(ct), s.HealthAgencyCode is not null && s.FacilityCode is not null && s.FileAcronym is not null);
    }

    // ------------------------------------------------------------------ workbench
    public async Task<EventFacts> FactsAsync(CodedEpisode e, Patient p, CancellationToken ct)
    {
        var b = await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.BookingId, ct);
        return new(p.Sex switch { BirthSex.Female => "F", BirthSex.Male => "M", BirthSex.Indeterminate => "I", _ => "U" }, p.DateOfBirth, e.AdmissionDate, e.DischargeDate,
            b?.Anaesthetic.ToString(), e.PurchaserCode, e.EventEndType, e.AdmissionSource);
    }

    public async Task<IReadOnlyList<CodingCheck>> ChecksAsync(CodedEpisode e, Patient p, CancellationToken ct = default)
    {
        var table = await TableForAsync(e.Items.Select(i => i.Code), ct);
        var advice = await db.Advice.AsNoTracking().Where(a => a.IsActive && a.AppliesToNz).ToListAsync(ct);
        return CodingChecks.Check(await FactsAsync(e, p, ct), e.Items.OrderBy(i => i.Sequence).ToList(), table, advice, Today);
    }

    public async Task<Workbench> WorkbenchAsync(Guid id, CancellationToken ct = default)
    {
        var e = await db.Episodes.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Coding record not found.");
        e.Items = e.Items.OrderBy(i => i.Sequence).ToList();
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == e.PatientId, ct);
        var b = await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.BookingId, ct);
        string? surgeon = null;
        if (b is not null)
        {
            var session = await theatre.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == b.SessionId, ct);
            if (session is not null) surgeon = await core.Clinicians.AsNoTracking().Where(c => c.Id == session.SurgeonId).Select(c => c.Name).SingleOrDefaultAsync(ct);
        }
        var ep = await emr.Episodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == e.EpisodeId, ct);
        var suggestions = new List<ProcedureMap>();
        if (b is not null)
        {
            var itemCode = await core.Procedures.AsNoTracking().Where(x => x.Name == b.Procedure).Select(x => x.Code).FirstOrDefaultAsync(ct);
            var terms = new[] { itemCode ?? "", b.Procedure }.Where(t => t.Length > 0).ToList();
            suggestions = await db.ProcedureMaps.AsNoTracking().Where(m => terms.Contains(m.Term) || m.Label == b.Procedure).ToListAsync(ct);
        }
        var table = await TableForAsync(e.Items.Select(i => i.Code).Concat(suggestions.SelectMany(m => m.ProcedureCodes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(m.DiagnosisCode ?? ""))), ct);
        var advice = await db.Advice.AsNoTracking().Where(a => a.IsActive && a.AppliesToNz).ToListAsync(ct);
        var checks = CodingChecks.Check(await FactsAsync(e, p, ct), e.Items, table, advice, Today);
        return new(e, p, b?.Procedure ?? "", b?.Anaesthetic.ToString(), surgeon, ep?.DischargeDestination, checks, suggestions, table.ToDictionary(k => k.Key, v => v.Value.Description));
    }

    /// <summary>Saves the coding. With MarkCoded, error-level edit checks keep it "in progress" and are returned; warnings do not block.</summary>
    public async Task<(CodedEpisode? Episode, IReadOnlyList<CodingCheck> Checks, IReadOnlyList<string> Errors)> SaveAsync(Guid id, CodingSave r, string user, CancellationToken ct = default)
    {
        var e = await db.Episodes.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Coding record not found.");
        if (e.Version != r.Version) return (null, [], ["Someone else saved this event since you opened it – reopen it to see their changes."]);
        if (r.Items.Count > 99) return (null, [], ["At most 99 codes per event."]);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == e.PatientId, ct);
        var wasSubmitted = e.Status == CodingStatus.Submitted;
        e.PurchaserCode = (r.PurchaserCode ?? "").Trim().ToUpperInvariant(); e.AdmissionSource = (r.AdmissionSource ?? "").Trim().ToUpperInvariant();
        e.EventEndType = (r.EventEndType ?? "").Trim().ToUpperInvariant(); e.CoderNote = string.IsNullOrWhiteSpace(r.CoderNote) ? null : r.CoderNote.Trim();
        db.Items.RemoveRange(e.Items);
        e.Items = r.Items.Select((i, n) => new CodedItem
        {
            CodedEpisodeId = e.Id,
            Sequence = n + 1,
            DiagnosisType = (i.DiagnosisType ?? "").Trim().ToUpperInvariant(),
            Code = CodingChecks.Normalise(i.Code),
            Description = (i.Description ?? "").Trim(),
            Date = i.Date,
        }).ToList();
        // Principal diagnosis first, then other diagnoses, procedures, external causes (NMDS order).
        var order = new Dictionary<string, int> { ["A"] = 0, ["B"] = 1, ["O"] = 2, ["E"] = 3, ["M"] = 4 };
        var seq = 1;
        foreach (var i in e.Items.OrderBy(i => order.GetValueOrDefault(i.DiagnosisType, 9)).ThenBy(i => i.Sequence).ToList()) i.Sequence = seq++;
        db.Items.AddRange(e.Items);
        var checks = await ChecksAsync(e, p, ct);
        var blocking = checks.Any(c => c.Level == CheckLevel.Error);
        if (e.Status != CodingStatus.NotReportable)
            e.Status = r.MarkCoded && !blocking ? CodingStatus.Coded : e.Items.Count == 0 ? CodingStatus.ToCode : CodingStatus.InProgress;
        if (r.MarkCoded && !blocking) { e.CodedBy = user; e.CodedAt = clock.GetUtcNow(); e.HealthNzError = null; }
        e.Version++;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, r.MarkCoded && !blocking ? "Code" : "Update", "CodedEpisode", e.Id.ToString(), e.PatientId.ToString(),
            reason: $"{e.Items.Count} code(s): {string.Join(' ', e.Items.Select(i => i.DiagnosisType + ":" + CodingChecks.Display(i.Code)))}{(wasSubmitted ? " – changed after submission, to be resent" : "")}", ct: ct);
        return (e, checks, r.MarkCoded && blocking ? ["Fix the errors before marking the event coded."] : []);
    }

    public async Task<string?> SetReportableAsync(Guid id, bool reportable, string? reason, string user, CancellationToken ct = default)
    {
        var e = await db.Episodes.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Coding record not found.");
        if (!reportable && string.IsNullOrWhiteSpace(reason)) return "Give the reason the event is not reported by TDS.";
        if (e.Status == CodingStatus.Submitted) return "The event has been sent to Health NZ – record a correction instead.";
        e.Status = reportable ? (e.Items.Count == 0 ? CodingStatus.ToCode : CodingStatus.InProgress) : CodingStatus.NotReportable;
        e.NotReportableReason = reportable ? null : reason!.Trim();
        e.Version++;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, reportable ? "Reportable" : "NotReportable", "CodedEpisode", e.Id.ToString(), e.PatientId.ToString(), reason: reason, ct: ct);
        return null;
    }

    /// <summary>Records an error Health NZ reported for the event (from their error report); it goes back to the coder and in the next file once fixed.</summary>
    public async Task<string?> RecordHealthNzErrorAsync(Guid id, string message, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Enter the error Health NZ reported.";
        var e = await db.Episodes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Coding record not found.");
        if (e.SubmissionId is null) return "This event has not been sent yet.";
        e.HealthNzError = message.Trim().Length > 1000 ? message.Trim()[..1000] : message.Trim();
        e.Status = CodingStatus.Rejected; e.Version++;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "HealthNzError", "CodedEpisode", e.Id.ToString(), e.PatientId.ToString(), reason: e.HealthNzError, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ advice, picklists, registrations
    public async Task<IReadOnlyList<CodingAdvice>> AdviceAsync(CancellationToken ct = default) =>
        await db.Advice.AsNoTracking().OrderBy(a => a.Source).ThenBy(a => a.Reference).ToListAsync(ct);

    public async Task<(CodingAdvice? Row, IReadOnlyList<string> Errors)> SaveAdviceAsync(AdviceEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Reference)) errors.Add("Reference (e.g. Q3725 or ACS 0031) is required.");
        if (string.IsNullOrWhiteSpace(e.Title)) errors.Add("Title is required.");
        if (string.IsNullOrWhiteSpace(e.Summary)) errors.Add("Write a short summary in your own words.");
        if (e.Summary?.Length > 2000) errors.Add("Summary: at most 2000 characters.");
        if (e.Link is { Length: > 0 } l && !l.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) errors.Add("Link must start with https://.");
        if (errors.Count > 0) return (null, errors);
        var a = e.Id is { } id ? await db.Advice.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Advice not found.") : null;
        if (a is null) { a = new CodingAdvice(); db.Advice.Add(a); }
        a.Reference = e.Reference.Trim(); a.Title = e.Title.Trim(); a.Source = e.Source; a.Edition = (e.Edition ?? "").Trim(); a.AppliesToNz = e.AppliesToNz; a.ReviewedOn = e.ReviewedOn;
        a.CodePrefixes = (e.CodePrefixes ?? "").Trim().ToUpperInvariant(); a.Summary = e.Summary!.Trim(); a.Link = string.IsNullOrWhiteSpace(e.Link) ? null : e.Link.Trim(); a.IsActive = e.IsActive;
        a.UpdatedBy = user; a.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "CodingAdvice", a.Id.ToString(), reason: $"{a.Reference} applies to NZ: {a.AppliesToNz}", ct: ct);
        return (a, []);
    }

    public async Task<IReadOnlyList<ProcedureMap>> ProcedureMapsAsync(CancellationToken ct = default) => await db.ProcedureMaps.AsNoTracking().OrderBy(m => m.Label).ToListAsync(ct);

    public async Task<(ProcedureMap? Row, IReadOnlyList<string> Errors)> SaveProcedureMapAsync(ProcedureMapEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(e.Term)) errors.Add("Term (the procedure list code or name) is required.");
        if (string.IsNullOrWhiteSpace(e.Label)) errors.Add("Label is required.");
        var codes = (e.ProcedureCodes ?? "").Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(CodingChecks.Normalise).ToList();
        if (codes.Count == 0) errors.Add("Give at least one ACHI procedure code.");
        var table = await TableForAsync(codes.Append(e.DiagnosisCode ?? ""), ct);
        foreach (var c in codes.Where(c => !table.TryGetValue(c, out var t) || t.Type != "O")) errors.Add($"{CodingChecks.Display(c)} is not an ACHI procedure code in the code table.");
        if (!string.IsNullOrWhiteSpace(e.DiagnosisCode) && !table.ContainsKey(CodingChecks.Normalise(e.DiagnosisCode))) errors.Add($"{e.DiagnosisCode} is not in the code table.");
        if (await db.ProcedureMaps.AnyAsync(m => m.Term == e.Term.Trim() && m.Id != e.Id, ct)) errors.Add("That term already has a picklist entry.");
        if (errors.Count > 0) return (null, errors);
        var m = e.Id is { } id ? await db.ProcedureMaps.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Picklist entry not found.") : null;
        if (m is null) { m = new ProcedureMap(); db.ProcedureMaps.Add(m); }
        m.Term = e.Term.Trim(); m.Label = e.Label.Trim(); m.ProcedureCodes = string.Join(' ', codes.Select(CodingChecks.Display));
        m.DiagnosisCode = string.IsNullOrWhiteSpace(e.DiagnosisCode) ? null : CodingChecks.Display(e.DiagnosisCode); m.UpdatedBy = user; m.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "ProcedureMap", m.Id.ToString(), reason: $"{m.Term} → {m.ProcedureCodes}", ct: ct);
        return (m, []);
    }

    public async Task<IReadOnlyList<string>> SaveRegistrationAsync(Guid clinicianId, string group, string number, string user, CancellationToken ct = default)
    {
        group = (group ?? "").Trim().ToUpperInvariant(); number = (number ?? "").Trim();
        var errors = new List<string>();
        if (group.Length != 2) errors.Add("Professional group is 2 letters (MC = Medical Council of NZ).");
        if (number.Length is 0 or > 10) errors.Add("Registration number: 1–10 characters.");
        if (group == "MC" && !number.All(char.IsDigit)) errors.Add("Medical Council numbers are digits only.");
        if (!await core.Clinicians.AnyAsync(c => c.Id == clinicianId, ct)) errors.Add("Clinician not found.");
        if (errors.Count > 0) return errors;
        var r = await db.Registrations.SingleOrDefaultAsync(x => x.ClinicianId == clinicianId, ct);
        if (r is null) { r = new ClinicianRegistration { ClinicianId = clinicianId }; db.Registrations.Add(r); }
        r.ProfessionalGroup = group; r.RegistrationNumber = number;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Save", "ClinicianRegistration", clinicianId.ToString(), reason: $"{group} {number}", ct: ct);
        return [];
    }

    /// <summary>Health NZ funded advice and NZ conventions as starting content for the advice library (summaries in TDS's words).</summary>
    public static IReadOnlyList<AdviceEdit> StarterAdvice() =>
    [
        new(null, "ACS 0029 (NZ)", "Contracted procedures – one hospital reports", AdviceSource.NzConvention, "Twelfth Edition", true, new DateOnly(2023, 7, 1), "",
            "In NZ only one hospital codes and reports a contracted event to the NMDS, preferably the public hospital; the contract says which. Report with the right agency, facility and funding codes within 21 days.",
            null, true),
        new(null, "ACS 0233 (NZ)", "Morphology optional", AdviceSource.NzConvention, "Twelfth Edition", true, new DateOnly(2023, 7, 1), "M8 M9",
            "Morphology codes are optional for the NMDS in NZ – the Cancer Registry collects morphology directly.", null, true),
        new(null, "ACS 2001 (NZ)", "Z03.6 needs substance, place and activity", AdviceSource.NzConvention, "Twelfth Edition", true, new DateOnly(2023, 7, 1), "Z036",
            "Observation for suspected toxic effect from an ingested substance needs external cause codes for the substance, the place of occurrence and the activity (NMDS Data Dictionary requirement).", null, true),
        new(null, "NMDS free text", "Use free text from the record for descriptions", AdviceSource.NmdsRequirement, "Twelfth Edition", true, new DateOnly(2023, 7, 1), "S T U5 U6 U7 V W X Y",
            "Describe each code with the clinical statement from the record rather than the classification title – required for complications, injuries and external causes; also mesh brand/size/type, vaping, synthetic drugs.", null, true),
        new(null, "ACS 0031", "Anaesthesia", AdviceSource.IhacpaRule, "Twelfth Edition", true, null, "925",
            "Code the anaesthesia given for a procedure from blocks [1909]/[1910]. Do not assign 92513 Infiltration of local anaesthetic (ACS 0031, second exception).", null, true),
        new(null, "Q3725", "Ophthalmic blocks for cataract surgery", AdviceSource.IhacpaRule, "Thirteenth Edition", false, new DateOnly(2026, 9, 23), "92509",
            "Sub-Tenon's, retrobulbar and peribulbar blocks are regional blocks (92509); subconjunctival anaesthesia is infiltration (not coded). Published for Thirteenth Edition – check the Health NZ review before applying in NZ Twelfth Edition coding.",
            null, true),
    ];
}

/// <summary>Minimal RFC 4180 CSV reader (quoted fields, doubled quotes, CR/LF).</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); field.Clear(); if (row.Count > 1 || row[0].Length > 0) rows.Add(row); row = []; break;
                default: field.Append(ch); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); if (row.Count > 1 || row[0].Length > 0) rows.Add(row); }
        return rows;
    }
}
