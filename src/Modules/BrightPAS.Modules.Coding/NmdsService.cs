using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Patients;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Coding;

/// <summary>One record for the private hospital load file.</summary>
public sealed record NmdsRecord(string RecordId, string Purchaser, string Agency, string Facility, Patient Patient, CodedEpisode Episode, IReadOnlyList<CodedItem> Items, string CodingSystem);

public sealed record NmdsPreview(IReadOnlyList<Guid> Ready, IReadOnlyList<string> Problems, int Resubmissions, string? FileName);

/// <summary>
/// National Minimum Dataset – private hospitals load file (Health NZ "Private Hospitals Load File Format"): a CSV named AAA0YYMM.CSV
/// (hospital acronym, then 0 and the year and month of the latest discharge in the file), character fields in double quotes, one row per
/// event with repeating diagnosis/procedure/accident groups (type, description, code, coding system, date). A submission log keeps each
/// file; events Health NZ reports back with errors are corrected and go in the next file (same record id, so the event is replaced).
/// </summary>
public sealed class NmdsService(CodingDbContext db, CodingService coding, PatientsDbContext patients, AuditWriter audit, TimeProvider clock)
{
    public static readonly IReadOnlyDictionary<string, string> EthnicDescriptions = new Dictionary<string, string>
    {
        ["10"] = "European nfd",
        ["11"] = "NZ European",
        ["12"] = "Other European",
        ["21"] = "NZ Maori",
        ["30"] = "Pacific Island nfd",
        ["31"] = "Samoan",
        ["32"] = "Cook Island Maori",
        ["33"] = "Tongan",
        ["34"] = "Niuean",
        ["35"] = "Tokelauan",
        ["36"] = "Fijian",
        ["37"] = "Other Pacific Island",
        ["40"] = "Asian nfd",
        ["41"] = "Southeast Asian",
        ["42"] = "Chinese",
        ["43"] = "Indian",
        ["44"] = "Other Asian",
        ["51"] = "Middle Eastern",
        ["52"] = "Latin American",
        ["53"] = "African",
        ["61"] = "Other ethnicity",
        ["99"] = "Not stated",
    };

    /// <summary>Stable 14-character record id for an event (resubmissions reuse it).</summary>
    public static string RecordId(CodedEpisode e) => "TDS" + e.EpisodeId.ToString("N")[..11].ToUpperInvariant();

    /// <summary>AAA0YYMM.CSV for the latest discharge month in the file.</summary>
    public static string FileName(string acronym, DateOnly latestDischarge) => $"{acronym.ToUpperInvariant()}0{latestDischarge:yyMM}.CSV";

    private static string Q(string? v, int max)
    {
        var s = (v ?? "").Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (s.Length > max) s = s[..max];
        return $"\"{s}\"";
    }

    /// <summary>The problems that stop a patient's event going in a file (load file mandatory fields).</summary>
    public static IReadOnlyList<string> PatientProblems(Patient p)
    {
        var r = new List<string>();
        if (string.IsNullOrWhiteSpace(p.FamilyName)) r.Add("family name missing");
        if (string.IsNullOrWhiteSpace(p.GivenNames)) r.Add("first name missing");
        if (string.IsNullOrWhiteSpace(p.AddressLine1)) r.Add("address line 1 missing (Health NZ derives the domicile from it)");
        if (p.Nhi is { } nhi && !NhiValidator.Validate(nhi).IsValid) r.Add("NHI fails its check");
        return r;
    }

    public static string BuildCsv(IReadOnlyList<NmdsRecord> records)
    {
        var groups = Math.Max(1, records.Max(r => r.Items.Count));
        var sb = new StringBuilder();
        var head = new List<string> { "ID", "PURCHASER CODE", "HEALTH AGENCY", "HAF", "NHI", "SURNAME", "FIRSTNAM", "SECNAME", "THIRDNAM", "ADD1", "ADD2", "ADD3", "ADD4", "ADD5", "DOB",
            "DOMCODE", "NZRES", "ETH1", "ETHDESC1", "ETH2", "ETHDESC2", "ETH3", "ETHDESC3", "GENDER", "ADMSRC", "ADMDESC", "ADMDATE", "DISDATE", "DISTYPE", "DISDESC", "ADMWGT", "GEST", "MOTHAGE" };
        for (var g = 1; g <= groups; g++) head.AddRange([$"DIAGTYPE{g:D2}", $"DESC{g:D2}", $"DIAGCODE{g:D2}", $"CODESYS{g:D2}", $"OPDATE{g:D2}"]);
        sb.Append(string.Join(',', head)).Append("\r\n");
        foreach (var r in records)
        {
            var p = r.Patient; var e = r.Episode;
            var names = (p.GivenNames ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var eth = p.Ethnicities.OrderBy(x => x.Order).Select(x => x.Code).Take(3).ToList();
            var f = new List<string>
            {
                Q(r.RecordId, 14), Q(r.Purchaser, 2), Q(r.Agency, 4), Q(r.Facility, 4), Q(p.Nhi, 7), Q(p.FamilyName, 25),
                Q(names.ElementAtOrDefault(0), 20), Q(names.ElementAtOrDefault(1), 20), Q(names.Length > 2 ? string.Join(' ', names.Skip(2)) : null, 20),
                Q(p.AddressLine1, 35), Q(p.AddressLine2, 30), Q(p.Suburb, 30), Q(p.City, 30), Q(p.Postcode, 30),
                Q(CodingChecks.Ymd(p.DateOfBirth), 8), Q(null, 4), Q(null, 1),
            };
            for (var i = 0; i < 3; i++)
            {
                var two = i < eth.Count ? CodingChecks.EthnicCode(eth[i]) : null;
                var desc = i < eth.Count && eth[i].Length >= 2 ? EthnicDescriptions.GetValueOrDefault(two ?? eth[i][..2]) : null;
                f.Add(Q(two, 2)); f.Add(Q(desc, 20));
            }
            f.Add(Q(p.Sex switch { BirthSex.Female => "F", BirthSex.Male => "M", BirthSex.Indeterminate => "I", _ => "U" }, 1));
            f.Add(Q(e.AdmissionSource, 1)); f.Add(Q(e.AdmissionSource == "T" ? "Transferred from another facility" : "Routine admission", 30));
            f.Add(Q(CodingChecks.Ymd(e.AdmissionDate), 8)); f.Add(Q(CodingChecks.Ymd(e.DischargeDate), 8));
            f.Add(Q(e.EventEndType, 2)); f.Add(Q(CodingChecks.EventEndTypes.GetValueOrDefault(e.EventEndType), 25));
            f.Add(""); f.Add(""); f.Add(""); // infant fields: not used at a day-surgery unit
            for (var g = 0; g < groups; g++)
            {
                if (g < r.Items.Count)
                {
                    var i = r.Items[g];
                    f.AddRange([Q(i.DiagnosisType, 1), Q(i.Description, 50), Q(i.Code, 8), Q(r.CodingSystem, 2), Q(i.Date is { } d && i.DiagnosisType is "O" or "E" ? CodingChecks.Ymd(d) : "", 8)]);
                }
                else f.AddRange(["\"\"", "\"\"", "\"\"", "\"\"", "\"\""]);
            }
            sb.Append(string.Join(',', f)).Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Coded events ready to send (discharged up to the period end), with the problems that would stop them.</summary>
    public async Task<NmdsPreview> PreviewAsync(DateOnly periodTo, CancellationToken ct = default)
    {
        var s = await coding.SettingsAsync(ct);
        var problems = new List<string>();
        if (s.HealthAgencyCode is null || s.FacilityCode is null || s.FileAcronym is null) problems.Add("Set the health agency, HAF code and file acronym (Coding › Settings) – Health NZ allocates them.");
        var rows = await db.Episodes.AsNoTracking().Include(e => e.Items).Where(e => e.Status == CodingStatus.Coded && e.DischargeDate <= periodTo).OrderBy(e => e.DischargeDate).ToListAsync(ct);
        var pids = rows.Select(r => r.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Include(p => p.Ethnicities).Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var ready = new List<Guid>();
        foreach (var e in rows)
        {
            var p = people[e.PatientId];
            var pp = PatientProblems(p);
            var checks = await coding.ChecksAsync(e, p, ct);
            var errs = checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Message).ToList();
            if (pp.Count + errs.Count > 0) { problems.Add($"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames} (discharged {e.DischargeDate:d MMM yyyy}): {string.Join("; ", pp.Concat(errs))}"); continue; }
            ready.Add(e.Id);
        }
        var latest = rows.Where(r => ready.Contains(r.Id)).Select(r => r.DischargeDate).DefaultIfEmpty().Max();
        return new(ready, problems, rows.Count(r => ready.Contains(r.Id) && r.SubmissionId is not null),
            ready.Count > 0 && s.FileAcronym is not null ? FileName(s.FileAcronym, latest) : null);
    }

    public async Task<(NmdsSubmission? File, IReadOnlyList<string> Errors)> CreateAsync(DateOnly periodTo, string user, CancellationToken ct = default)
    {
        var s = await coding.SettingsAsync(ct);
        var pre = await PreviewAsync(periodTo, ct);
        if (s.HealthAgencyCode is null || s.FacilityCode is null || s.FileAcronym is null) return (null, [pre.Problems[0]]);
        if (pre.Ready.Count == 0) return (null, ["No coded events are ready for this period."]);
        var rows = await db.Episodes.Include(e => e.Items).Where(e => pre.Ready.Contains(e.Id)).OrderBy(e => e.DischargeDate).ToListAsync(ct);
        var pids = rows.Select(r => r.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Include(p => p.Ethnicities).Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var records = rows.Select(e => new NmdsRecord(RecordId(e), e.PurchaserCode, s.HealthAgencyCode, s.FacilityCode, people[e.PatientId], e,
            e.Items.OrderBy(i => i.Sequence).Select(i => new CodedItem { DiagnosisType = i.DiagnosisType, Code = i.Code, Description = i.Description, Date = i.Date, Sequence = i.Sequence }).ToList(),
            s.CodingSystem)).ToList();
        var csv = BuildCsv(records);
        var file = new NmdsSubmission
        {
            FileName = pre.FileName!,
            PeriodTo = periodTo,
            Records = rows.Count,
            Content = Encoding.ASCII.GetBytes(ToAscii(csv)),
            CreatedBy = user,
            CreatedAt = clock.GetUtcNow(),
            IsResubmission = rows.Any(r => r.SubmissionId is not null),
        };
        db.Submissions.Add(file);
        foreach (var e in rows)
        {
            db.SubmissionEvents.Add(new NmdsSubmissionEvent { SubmissionId = file.Id, CodedEpisodeId = e.Id, RecordId = RecordId(e) });
            e.SubmissionId = file.Id; e.Status = CodingStatus.Submitted; e.HealthNzError = null; e.Version++;
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "NmdsFile", file.Id.ToString(), reason: $"{file.FileName}: {file.Records} event(s){(file.IsResubmission ? ", includes resubmissions" : "")}", ct: ct);
        return (file, []);
    }

    /// <summary>The load file is ASCII: macrons and other accents are written without them (Māori → Maori).</summary>
    public static string ToAscii(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch < 128 ? ch : '?');
        }
        return sb.ToString();
    }

    public async Task<string?> MarkAsync(Guid id, SubmissionStatus status, string? notes, string user, CancellationToken ct = default)
    {
        var f = await db.Submissions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("File not found.");
        if (status is not (SubmissionStatus.Sent or SubmissionStatus.Accepted or SubmissionStatus.Errors)) return "Mark the file sent, accepted or returned with errors.";
        f.Status = status;
        if (status == SubmissionStatus.Sent) { f.SentBy = user; f.SentAt = clock.GetUtcNow(); }
        if (!string.IsNullOrWhiteSpace(notes)) f.Notes = notes.Trim().Length > 1000 ? notes.Trim()[..1000] : notes.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, status.ToString(), "NmdsFile", f.Id.ToString(), reason: f.Notes, ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<NmdsSubmission>> FilesAsync(CancellationToken ct = default) =>
        await db.Submissions.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new NmdsSubmission
        {
            Id = x.Id,
            FileName = x.FileName,
            PeriodTo = x.PeriodTo,
            Records = x.Records,
            Status = x.Status,
            IsResubmission = x.IsResubmission,
            CreatedBy = x.CreatedBy,
            CreatedAt = x.CreatedAt,
            SentBy = x.SentBy,
            SentAt = x.SentAt,
            Notes = x.Notes,
        }).ToListAsync(ct);

    public async Task<NmdsSubmission> FileAsync(Guid id, string user, CancellationToken ct = default)
    {
        var f = await db.Submissions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("File not found.");
        await audit.WriteAsync(user, "Export", "NmdsFile", f.Id.ToString(), reason: $"{f.FileName} downloaded ({f.Records} events – health information)", ct: ct);
        return f;
    }
}
