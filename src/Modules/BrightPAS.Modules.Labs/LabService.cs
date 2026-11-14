using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Patients;
using BrightPAS.Messaging.Hl7;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Labs;

public sealed record IngestResult(LabMessageOutcome Outcome, IReadOnlyList<Guid> ReportIds, string Ack, string? Error);

public sealed record LabReportRow(Guid Id, Guid? PatientId, string PatientName, string? Nhi, string? Mrn, string Laboratory, string TestName,
    string? RequestedBy, DateTimeOffset? CollectedAt, DateTimeOffset? ReportedAt, DateTimeOffset ReceivedAt, string ResultStatus,
    bool IsAbnormal, string MatchStatus, string? MatchNote, DateTimeOffset? AcknowledgedAt, string? AcknowledgedBy);

public sealed class LabService(LabsDbContext db, PatientsDbContext patients, AuditWriter audit, TimeProvider clock)
{
    public const int MaxMessageLength = 1_000_000;
    private static readonly TimeZoneInfo Nz = FindNz();

    /// <summary>Receives one HL7 ORU^R01 message: stores it, matches the patient, files each report. Returns the HL7 ACK to send back.</summary>
    public async Task<IngestResult> IngestAsync(string raw, string source, string receivedBy, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        Hl7Message msg;
        try { msg = Hl7Message.Parse(raw); }
        catch (Hl7Exception ex)
        {
            await Store(raw, source, receivedBy, null, LabMessageOutcome.Rejected, ex.Message, ct);
            return new(LabMessageOutcome.Rejected, [], "", ex.Message);
        }

        if (raw.Length > MaxMessageLength) return await Reject(msg, raw, source, receivedBy, "Message too large.", ct);
        if (msg.Msh.Get(9, 1) != "ORU" || msg.Msh.Get(9, 2) != "R01")
            return await Reject(msg, raw, source, receivedBy, $"Only ORU^R01 result messages are accepted here (received {msg.MessageType}).", ct);
        var pid = msg.First("PID");
        if (pid is null) return await Reject(msg, raw, source, receivedBy, "The message has no PID (patient) segment.", ct);
        if (!msg.All("OBR").Any()) return await Reject(msg, raw, source, receivedBy, "The message has no OBR (report) segment.", ct);

        var facility = Clip(msg.SendingFacility.Length > 0 ? msg.SendingFacility : msg.SendingApplication, 100);
        if (!string.IsNullOrEmpty(msg.ControlId) && await db.Messages.AnyAsync(m => m.SendingFacility == facility && m.ControlId == msg.ControlId && m.Outcome == LabMessageOutcome.Accepted, ct))
        {
            await Store(raw, source, receivedBy, msg, LabMessageOutcome.Duplicate, "Already received.", ct);
            return new(LabMessageOutcome.Duplicate, [], msg.Ack("AA", "Duplicate – already received", now), null);
        }

        var stored = await Store(raw, source, receivedBy, msg, LabMessageOutcome.Accepted, null, ct);
        var (nhi, mrn, family, given, dob) = ReadPatient(msg, pid);
        var (patientId, note) = await MatchAsync(nhi, mrn, family, dob, ct);

        var reports = new List<LabReport>();
        LabReport? current = null;
        LabObservation? lastObs = null;
        foreach (var seg in msg.Segments)
        {
            switch (seg.Name)
            {
                case "OBR":
                    current = new LabReport
                    {
                        MessageId = stored.Id,
                        PatientId = patientId,
                        MatchStatus = patientId is null ? LabMatchStatus.Unmatched : LabMatchStatus.Matched,
                        MatchNote = note,
                        PatientNhi = nhi,
                        PatientMrn = mrn,
                        PatientFamilyName = Clip(family, 100),
                        PatientGivenNames = Clip(given, 150),
                        PatientDateOfBirth = dob,
                        Laboratory = facility,
                        PlacerOrderNumber = Clip(seg.Get(2, 1), 50),
                        AccessionNumber = Clip(seg.Get(3, 1), 50),
                        TestCode = Clip(seg.Get(4, 1), 50),
                        TestName = Clip(Coalesce(seg.Get(4, 2), seg.Get(4, 1), "Result"), 200),
                        RequestedBy = Clip(Name(seg.Get(16, 6), seg.Get(16, 3), seg.Get(16, 2)), 150),
                        RequestedByCode = Clip(seg.Get(16, 1), 30),
                        CollectedAt = Hl7Message.ParseTimestamp(seg.Get(7, 1), Nz),
                        ReportedAt = Hl7Message.ParseTimestamp(seg.Get(22, 1), Nz),
                        ResultStatus = Status(seg.Get(25)),
                        ReceivedAt = now
                    };
                    reports.Add(current);
                    lastObs = null;
                    break;
                case "OBX" when current is not null:
                    var reps = Math.Max(1, seg.Repetitions(5));
                    var value = string.Join("\n", Enumerable.Range(0, reps).Select(r => seg.Get(5, 0, 0, r)));
                    if (seg.Get(2) is "CE" or "CWE") value = Coalesce(seg.Get(5, 2), seg.Get(5, 1));
                    lastObs = new LabObservation
                    {
                        Sequence = current.Observations.Count + 1,
                        ValueType = Clip(seg.Get(2), 5),
                        Code = Clip(seg.Get(3, 1), 50),
                        Name = Clip(Coalesce(seg.Get(3, 2), seg.Get(3, 1)), 200),
                        Value = Clip(value, 4000),
                        Units = Clip(Coalesce(seg.Get(6, 1)), 50),
                        ReferenceRange = Clip(seg.Get(7), 60),
                        Flag = Clip(seg.Get(8, 1), 5)
                    };
                    current.Observations.Add(lastObs);
                    break;
                case "NTE" when current is not null:
                    var text = string.Join("\n", Enumerable.Range(0, Math.Max(1, seg.Repetitions(3))).Select(r => seg.Get(3, 0, 0, r)));
                    if (lastObs is not null) lastObs.Comment = Clip(Join(lastObs.Comment, text), 2000);
                    else current.Comment = Clip(Join(current.Comment, text), 4000);
                    break;
            }
        }

        foreach (var r in reports)
        {
            r.IsAbnormal = r.Observations.Any(o => o.IsAbnormal);
            // A final or corrected report replaces the earlier version of the same test for the same patient identifiers.
            if (r.ResultStatus is LabResultStatus.Corrected or LabResultStatus.Final && !string.IsNullOrEmpty(r.AccessionNumber))
            {
                var earlier = await db.Reports.Where(x => x.Laboratory == r.Laboratory && x.AccessionNumber == r.AccessionNumber && x.TestCode == r.TestCode
                    && x.SupersededById == null && x.PatientNhi == r.PatientNhi && x.PatientMrn == r.PatientMrn && x.PatientDateOfBirth == r.PatientDateOfBirth
                    && x.PatientId == r.PatientId).ToListAsync(ct);
                foreach (var e in earlier) e.SupersededById = r.Id;
            }
            db.Reports.Add(r);
        }
        await db.SaveChangesAsync(ct);
        foreach (var r in reports)
            await audit.WriteAsync($"interface:{source}", "LabResultReceived", "LabReport", r.Id.ToString(), r.PatientId?.ToString(),
                reason: $"{r.Laboratory} {r.TestName} ({r.ResultStatus}){(r.PatientId is null ? " – UNMATCHED" : "")}", ct: ct);
        return new(LabMessageOutcome.Accepted, reports.Select(r => r.Id).ToList(),
            msg.Ack("AA", patientId is null ? "Received – patient not matched, held for review" : null, now), null);
    }

    private async Task<IngestResult> Reject(Hl7Message msg, string raw, string source, string by, string error, CancellationToken ct)
    {
        await Store(raw, source, by, msg, LabMessageOutcome.Rejected, error, ct);
        return new(LabMessageOutcome.Rejected, [], msg.Ack("AR", error, clock.GetUtcNow()), error);
    }

    private async Task<LabMessage> Store(string raw, string source, string by, Hl7Message? msg, LabMessageOutcome outcome, string? error, CancellationToken ct)
    {
        var m = new LabMessage
        {
            ReceivedAt = clock.GetUtcNow(),
            Source = Clip(source, 20),
            Raw = raw.Length > MaxMessageLength ? raw[..MaxMessageLength] : raw,
            Outcome = outcome,
            Error = error is null ? null : Clip(error, 1000),
            ReceivedBy = Clip(by, 100),
            ControlId = msg is null ? null : Clip(msg.ControlId, 50),
            MessageType = msg is null ? null : Clip(msg.MessageType, 20),
            SendingFacility = msg is null ? null : Clip(msg.SendingFacility.Length > 0 ? msg.SendingFacility : msg.SendingApplication, 100)
        };
        db.Messages.Add(m);
        await db.SaveChangesAsync(ct);
        return m;
    }

    private static (string? Nhi, string? Mrn, string Family, string Given, DateOnly? Dob) ReadPatient(Hl7Message msg, Hl7Segment pid)
    {
        string? nhi = null, mrn = null;
        for (var i = 0; i < Math.Max(1, pid.Repetitions(3)); i++)
        {
            var id = pid.Get(3, 1, 0, i).ToUpperInvariant();
            var type = pid.Get(3, 5, 0, i).ToUpperInvariant();
            var authority = pid.Get(3, 4, 0, i).ToUpperInvariant();
            if (id.Length == 0) continue;
            if ((type == "NHI" || authority.Contains("NHI") || NhiValidator.Validate(id).IsValid) && nhi is null && NhiValidator.Validate(id).IsValid) nhi = id;
            else if (type is "MR" or "PI" or "PT" && mrn is null) mrn = id;
        }
        if (nhi is null && NhiValidator.Validate(pid.Get(2, 1).ToUpperInvariant()).IsValid) nhi = pid.Get(2, 1).ToUpperInvariant();
        DateOnly? dob = DateOnly.TryParseExact(pid.Get(7, 1).Length >= 8 ? pid.Get(7, 1)[..8] : "", "yyyyMMdd", out var d) ? d : null;
        return (nhi, mrn, pid.Get(5, 1), string.Join(' ', new[] { pid.Get(5, 2), pid.Get(5, 3) }.Where(x => x.Length > 0)), dob);
    }

    /// <summary>
    /// Matches on NHI (then MRN), and only accepts the match if date of birth and family name agree.
    /// Anything doubtful is held as unmatched for a person to check – a result on the wrong patient is a serious clinical risk.
    /// </summary>
    private async Task<(Guid? Id, string? Note)> MatchAsync(string? nhi, string? mrn, string family, DateOnly? dob, CancellationToken ct)
    {
        Patient? p = null;
        if (nhi is not null) p = await patients.Patients.SingleOrDefaultAsync(x => x.Nhi == nhi, ct);
        if (p is null && mrn is not null) p = await patients.Patients.SingleOrDefaultAsync(x => x.Mrn == mrn, ct);
        if (p is null) return (null, nhi is null && mrn is null ? "No NHI or record number in the message." : $"No patient found with NHI {nhi ?? "-"} / record {mrn ?? "-"}.");
        if (dob is not null && dob != p.DateOfBirth) return (null, $"Identifier matches {p.Mrn}, but the date of birth differs ({dob:dd/MM/yyyy} vs {p.DateOfBirth:dd/MM/yyyy}).");
        if (family.Length > 0 && PatientService.SearchKey(family) != p.FamilyNameSearch) return (null, $"Identifier matches {p.Mrn}, but the family name differs ({family} vs {p.FamilyName}).");
        return (p.Id, null);
    }

    public async Task<IReadOnlyList<LabReportRow>> InboxAsync(string view, string? requestedBy, Guid? patientId, int take = 200, CancellationToken ct = default)
    {
        var q = db.Reports.AsNoTracking().Where(r => r.SupersededById == null);
        q = view switch
        {
            "unacknowledged" => q.Where(r => r.AcknowledgedAt == null && r.PatientId != null),
            "abnormal" => q.Where(r => r.IsAbnormal && r.PatientId != null),
            "unmatched" => q.Where(r => r.PatientId == null),
            _ => q
        };
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        if (!string.IsNullOrWhiteSpace(requestedBy)) q = q.Where(r => r.RequestedBy != null && r.RequestedBy.Contains(requestedBy));
        var list = await q.OrderByDescending(r => r.ReceivedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
        return await Rows(list, ct);
    }

    private async Task<IReadOnlyList<LabReportRow>> Rows(List<LabReport> list, CancellationToken ct)
    {
        var ids = list.Where(r => r.PatientId != null).Select(r => r.PatientId!.Value).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return list.Select(r =>
        {
            var p = r.PatientId is { } id && people.TryGetValue(id, out var x) ? x : null;
            var name = p is null ? $"{r.PatientFamilyName.ToUpperInvariant()}, {r.PatientGivenNames} (as sent by lab)" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}";
            return new LabReportRow(r.Id, r.PatientId, name, p?.Nhi ?? r.PatientNhi, p?.Mrn ?? r.PatientMrn, r.Laboratory, r.TestName, r.RequestedBy,
                r.CollectedAt, r.ReportedAt, r.ReceivedAt, r.ResultStatus.ToString(), r.IsAbnormal, r.MatchStatus.ToString(), r.MatchNote, r.AcknowledgedAt, r.AcknowledgedBy);
        }).ToList();
    }

    public async Task<(LabReportRow Row, LabReport Report)?> GetAsync(Guid id, string user, string? workstation, CancellationToken ct = default)
    {
        var r = await db.Reports.AsNoTracking().Include(x => x.Observations).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        r.Observations = [.. r.Observations.OrderBy(o => o.Sequence)];
        await audit.WriteAsync(user, "View", "LabReport", r.Id.ToString(), r.PatientId?.ToString(), workstation: workstation, ct: ct);
        return ((await Rows([r], ct))[0], r);
    }

    public async Task<string?> AcknowledgeAsync(Guid id, string user, string? comment, CancellationToken ct = default)
    {
        var r = await db.Reports.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Result not found.";
        if (r.PatientId is null) return "Match this result to a patient before acknowledging it.";
        if (r.AcknowledgedAt is not null) return $"Already acknowledged by {r.AcknowledgedBy}.";
        r.AcknowledgedAt = clock.GetUtcNow();
        r.AcknowledgedBy = user;
        r.AcknowledgementComment = string.IsNullOrWhiteSpace(comment) ? null : Clip(comment.Trim(), 1000);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "LabResultAcknowledged", "LabReport", r.Id.ToString(), r.PatientId.ToString(), reason: r.AcknowledgementComment, ct: ct);
        return null;
    }

    public async Task<string?> MatchToPatientAsync(Guid id, Guid patientId, string user, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Record why you are sure this is the right patient.";
        var r = await db.Reports.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Result not found.";
        if (r.PatientId is not null) return "This result is already matched.";
        if (!await patients.Patients.AnyAsync(p => p.Id == patientId, ct)) return "Patient not found.";
        r.PatientId = patientId;
        r.MatchStatus = LabMatchStatus.ManuallyMatched;
        r.MatchedBy = user;
        r.MatchNote = Clip($"Matched by {user}: {reason.Trim()}", 300);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "LabResultMatched", "LabReport", r.Id.ToString(), patientId.ToString(), reason: reason, ct: ct);
        return null;
    }

    public Task<int> UnacknowledgedCountAsync(CancellationToken ct = default) =>
        db.Reports.CountAsync(r => r.SupersededById == null && r.AcknowledgedAt == null && r.PatientId != null, ct);

    /// <summary>A synthetic ORU^R01 for testing (test-range NHI; fictional lab).</summary>
    public static string SampleMessage(string nhi, string family, string given, DateOnly dob, DateTimeOffset now)
    {
        var ts = now.ToString("yyyyMMddHHmm");
        var id = now.ToString("yyyyMMddHHmmss");
        return string.Join('\r',
            $"MSH|^~\\&|LABSYS|TEST LAB NELSON|BRIGHTPAS|TDS|{ts}||ORU^R01|MSG{id}|P|2.4",
            $"PID|1||{nhi}^^^NHI^NHI||{family.ToUpperInvariant()}^{given}||{dob:yyyyMMdd}|F",
            $"OBR|1|ORD{id}|ACC{id}|CBC^Complete blood count|||{ts}|||||||||12345^SMITH^JOHN^^^DR||||||{ts}|||F",
            "OBX|1|NM|718-7^Haemoglobin||98|g/L|115-155|L|||F",
            "OBX|2|NM|6690-2^White cell count||7.2|x10*9/L|4.0-11.0|N|||F",
            "OBX|3|NM|777-3^Platelets||265|x10*9/L|150-400|N|||F",
            "NTE|1||Low haemoglobin – consider iron studies.",
            $"OBR|2|ORD{id}B|ACC{id}B|ELEC^Electrolytes|||{ts}|||||||||12345^SMITH^JOHN^^^DR||||||{ts}|||F",
            "OBX|1|NM|2951-2^Sodium||139|mmol/L|135-145|N|||F",
            "OBX|2|NM|2823-3^Potassium||5.9|mmol/L|3.5-5.2|H|||F",
            "OBX|3|NM|2160-0^Creatinine||88|umol/L|45-90|N|||F") + "\r";
    }

    private static LabResultStatus Status(string s) => s switch
    {
        "F" => LabResultStatus.Final,
        "C" => LabResultStatus.Corrected,
        "X" => LabResultStatus.Cancelled,
        "A" => LabResultStatus.Partial,
        _ => LabResultStatus.Preliminary
    };
    private static string Name(string prefix, string given, string family) => string.Join(' ', new[] { prefix, given, family }.Where(x => x.Length > 0));
    private static string Coalesce(params string[] v) => v.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    private static string Join(string? a, string b) => string.IsNullOrEmpty(a) ? b : a + "\n" + b;
    private static string Clip(string s, int n) => s.Length > n ? s[..n] : s;
    private static TimeZoneInfo FindNz()
    {
        foreach (var id in new[] { "Pacific/Auckland", "New Zealand Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.Utc;
    }
}
