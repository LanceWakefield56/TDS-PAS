using BrightPAS.Core;
using System.Globalization;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Setup;
using static BrightPAS.Modules.Endoscopy.Terminology;

namespace BrightPAS.Modules.Endoscopy;

public sealed record DetailsEdit(ProcedureKind Kind, Guid? EndoscopistId, string? EndoscopistName, string? Assistants, Guid? UpperScopeId, Guid? LowerScopeId, Guid? TowerId,
    bool NbspScreening, string? NbspNumber, IReadOnlyList<string>? Indications, string? OtherIndication, bool ConsentConfirmed, SedationKind Sedation, string? SedationNotes,
    bool Co2, bool WaterAssisted, bool ScopeGuide, bool PositionChange, bool AbdominalPressure);
public sealed record ProcedureEdit(DateTimeOffset? UpperStartAt, DateTimeOffset? UpperEndAt, UpperExtent? UpperExtent, bool? Retroflexion, bool? UpperPhotoDocumentation,
    DateTimeOffset? InsertedAt, DateTimeOffset? CaecumAt, DateTimeOffset? WithdrawalStartAt, DateTimeOffset? EndedAt, LowerExtent? LowerExtent,
    bool? AppendicealOrifice, bool? IleocaecalValve, bool? TerminalIleum, bool? CaecumPhotographed, int? BbpsRight, int? BbpsTransverse, int? BbpsLeft,
    bool? RectalRetroflexion, string? IncompleteReason, int? Comfort);
public sealed record ReportEdit(string? Complication, string? ComplicationDetails, string? Diagnosis, string? Recommendations, string? FollowUp, int? SurveillanceMonths, string? PatientAdvice);
public sealed record FindingEdit(string Site, string Kind, string? Grade, int? SizeMm, string? Description);
public sealed record PolypEdit(string Site, int? DistanceCm, int SizeMm, string Paris, string? Nice, string? Jnet, string Technique, bool? Lifted, string Resection,
    string Retrieved, string? Pot, string Complete, int Clips, bool Tattooed, string? Notes);
public sealed record HistologyEdit(string? Histology, string? Dysplasia, string? Notes);
public sealed record TattooEdit(string Site, int? DistanceCm, string Position, string Ink, decimal? VolumeMl, int? Injections, Guid? PolypId, string? Reason);
public sealed record InterventionEdit(string Kind, string Site, int? Count, string? Pot, string? Purpose, string? Details, string? Result);
public sealed record MediaEdit(string? Site, string? Caption, bool InReport, bool InPatientReport, int? Sort, Guid? FindingId, Guid? PolypId);
public sealed record DicomImageInfo(string? SopInstanceUid, string? PatientId, string? PatientName, string? Accession, string? StudyInstanceUid);
public sealed record KpiTargets(decimal CaecalIntubation, decimal Adr, decimal WithdrawalMinutes, decimal Retrieval, decimal AdequatePrep, decimal ComfortPoor, decimal UpperMinutes);
public sealed record KpiRow(string Endoscopist, int Colonoscopies, decimal? CaecalIntubation, decimal? MedianWithdrawal, decimal? WithdrawalOver6, decimal? Pdr, decimal? Adr,
    decimal? Ssldr, decimal? Retrieval, decimal? AdequatePrep, decimal? ComfortPoor, decimal? RectalRetroflexion, int LowerComplications,
    int Gastroscopies, decimal? D2, decimal? UpperRetroflexion, decimal? MedianUpperMinutes, decimal? UpperOver7, decimal? PhotoDocumentation, int UpperComplications);

/// <summary>Endoscopy procedures, structured findings, media and quality indicators. Every change is audited against the patient.</summary>
public sealed class EndoService(EndoDbContext db, MediaStore store, SettingsService settings, AuditWriter audit, TimeProvider clock)
{
    // ------------------------------------------------------------------ procedures
    public async Task<Procedure?> FindAsync(Guid id, CancellationToken ct = default) =>
        await db.Procedures.Include(p => p.Indications).SingleOrDefaultAsync(p => p.Id == id, ct);
    public async Task<Procedure?> ForBookingAsync(Guid bookingId, CancellationToken ct = default) =>
        await db.Procedures.Include(p => p.Indications).SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);

    /// <summary>Opens (or creates) the procedure for an endoscopy booking, giving it a DICOM accession number and study UID for the tower worklist.</summary>
    public async Task<Procedure> EnsureAsync(Guid bookingId, Guid patientId, string? procedureText, Guid? endoscopistId, string? endoscopistName, DateOnly date, string user, CancellationToken ct = default)
    {
        var p = await ForBookingAsync(bookingId, ct);
        if (p is not null) return p;
        var prefix = "E" + date.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var n = await db.Procedures.CountAsync(x => x.AccessionNumber.StartsWith(prefix), ct);
        p = new Procedure
        {
            BookingId = bookingId,
            PatientId = patientId,
            Kind = Guess(procedureText) ?? ProcedureKind.Gastroscopy,
            EndoscopistId = endoscopistId,
            EndoscopistName = endoscopistName,
            AccessionNumber = prefix + (n + 1).ToString("D4", CultureInfo.InvariantCulture),
            StudyInstanceUid = Uid(),
            CreatedBy = user,
            CreatedAt = clock.GetUtcNow(),
        };
        if (p.Kind == ProcedureKind.Gastroscopy) p.Sedation = SedationKind.ThroatSpray;
        db.Procedures.Add(p);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Create", p, null, ct);
        return p;
    }

    /// <summary>A DICOM UID derived from a random UUID (2.25 root, PS3.5 B.2).</summary>
    public static string Uid() => "2.25." + new BigInteger(Guid.NewGuid().ToByteArray().Append((byte)0).ToArray()).ToString(CultureInfo.InvariantCulture);

    private async Task<Procedure> OpenAsync(Guid id, CancellationToken ct, bool allowSigned = false)
    {
        var p = await FindAsync(id, ct) ?? throw new KeyNotFoundException("Procedure not found.");
        if (!allowSigned && p.Status == ProcedureStatus.Signed) throw new InvalidOperationException("The report is signed. Amend it first (with a reason) to change it.");
        return p;
    }

    private void Touch(Procedure p, string user)
    {
        p.UpdatedBy = user; p.UpdatedAt = clock.GetUtcNow();
        if (p.Status == ProcedureStatus.Planned) p.Status = ProcedureStatus.InProgress;
    }

    public async Task<IReadOnlyList<string>> UpdateDetailsAsync(Guid id, DetailsEdit r, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(id, ct);
        var errors = new List<string>();
        var codes = (r.Indications ?? []).Distinct().ToList();
        var valid = (HasUpper(r.Kind) ? UpperIndications : []).Concat(HasLower(r.Kind) ? LowerIndications : []).Select(i => i.Code).ToHashSet();
        foreach (var c in codes.Where(c => !valid.Contains(c))) errors.Add($"\"{IndicationLabel(c)}\" is not an indication for {KindLabel(r.Kind).ToLowerInvariant()}.");
        if (r.NbspScreening && !HasLower(r.Kind)) errors.Add("Bowel screening (NBSP) applies to colonoscopy only.");
        if (r.UpperScopeId is not null && r.UpperScopeId == r.LowerScopeId) errors.Add("Choose different scopes for the gastroscopy and the colonoscopy.");
        if (errors.Count > 0) return errors;
        var before = p.Kind;
        p.Kind = r.Kind; p.EndoscopistId = r.EndoscopistId; p.EndoscopistName = Clip(r.EndoscopistName, 100); p.Assistants = Clip(r.Assistants, 200);
        p.UpperScopeId = HasUpper(r.Kind) ? r.UpperScopeId : null; p.LowerScopeId = HasLower(r.Kind) ? r.LowerScopeId : null; p.TowerId = r.TowerId;
        p.NbspScreening = r.NbspScreening; p.NbspNumber = Clip(r.NbspNumber, 30); p.OtherIndication = Clip(r.OtherIndication, 300); p.ConsentConfirmed = r.ConsentConfirmed;
        p.Sedation = r.Sedation; p.SedationNotes = Clip(r.SedationNotes, 200);
        p.Co2 = r.Co2; p.WaterAssisted = r.WaterAssisted; p.ScopeGuide = r.ScopeGuide; p.PositionChange = r.PositionChange; p.AbdominalPressure = r.AbdominalPressure;
        db.Indications.RemoveRange(p.Indications.Where(i => !codes.Contains(i.Code)));
        foreach (var c in codes.Where(c => p.Indications.All(i => i.Code != c))) db.Indications.Add(new ProcedureIndication { ProcedureId = p.Id, Code = c });
        Touch(p, user);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Update", p, before != r.Kind ? $"procedure {before} → {r.Kind}" : "details", ct);
        return [];
    }

    public async Task<(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)> UpdateProcedureAsync(Guid id, ProcedureEdit r, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(id, ct);
        var errors = new List<string>();
        var warnings = new List<string>();
        foreach (var (v, n) in new[] { (r.BbpsRight, "right colon"), (r.BbpsTransverse, "transverse colon"), (r.BbpsLeft, "left colon") })
            if (v is < 0 or > 3) errors.Add($"Boston Bowel Preparation Scale for the {n} is 0–3.");
        if (r.Comfort is < 1 or > 5) errors.Add("Comfort score is 1 (comfortable) to 5 (severe discomfort).");
        if (r.UpperStartAt is { } us && r.UpperEndAt is { } ue && ue < us) errors.Add("The gastroscopy ends before it starts.");
        var times = new[] { (r.InsertedAt, "insertion"), (r.CaecumAt, "caecum reached"), (r.WithdrawalStartAt, "withdrawal start"), (r.EndedAt, "scope out") }.Where(t => t.Item1 is not null).ToList();
        for (var i = 1; i < times.Count; i++) if (times[i].Item1 < times[i - 1].Item1) errors.Add($"The {times[i].Item2} time is before the {times[i - 1].Item2} time.");
        if (r.CaecumAt is not null && r.LowerExtent is not (LowerExtent.Caecum or LowerExtent.TerminalIleum or null)) errors.Add("Caecum reached time is set but the extent is not the caecum or terminal ileum.");
        if (errors.Count > 0) return (errors, []);
        p.UpperStartAt = r.UpperStartAt; p.UpperEndAt = r.UpperEndAt; p.UpperExtent = r.UpperExtent; p.Retroflexion = r.Retroflexion; p.UpperPhotoDocumentation = r.UpperPhotoDocumentation;
        p.InsertedAt = r.InsertedAt; p.CaecumAt = r.CaecumAt; p.WithdrawalStartAt = r.WithdrawalStartAt; p.EndedAt = r.EndedAt; p.LowerExtent = r.LowerExtent;
        p.AppendicealOrifice = r.AppendicealOrifice; p.IleocaecalValve = r.IleocaecalValve; p.TerminalIleum = r.TerminalIleum; p.CaecumPhotographed = r.CaecumPhotographed;
        p.BbpsRight = r.BbpsRight; p.BbpsTransverse = r.BbpsTransverse; p.BbpsLeft = r.BbpsLeft; p.RectalRetroflexion = r.RectalRetroflexion;
        p.IncompleteReason = Clip(r.IncompleteReason, 300); p.Comfort = r.Comfort;
        Touch(p, user);
        if ((HasLower(p.Kind) ? p.EndedAt is not null : true) && (HasUpper(p.Kind) ? p.UpperEndAt is not null : true) && p.Status == ProcedureStatus.InProgress) p.Status = ProcedureStatus.Completed;
        await db.SaveChangesAsync(ct);
        await Audit(user, "Update", p, "procedure record", ct);
        if (IsColonoscopy(p.Kind) && WithdrawalMinutes(p) is { } w && w < 6) warnings.Add($"Withdrawal time {w:0.0} minutes – the standard is at least 6 minutes.");
        if (BbpsTotal(p) is { } b && !AdequatePrep(p)) warnings.Add($"Bowel preparation BBPS {b} – inadequate (needs 6 or more with every segment 2 or more); consider an early repeat.");
        if (HasUpper(p.Kind) && UpperMinutes(p) is { } u && u < 7) warnings.Add($"Gastroscopy time {u:0.0} minutes – the standard is at least 7 minutes.");
        return ([], warnings);
    }

    public async Task<IReadOnlyList<string>> UpdateReportAsync(Guid id, ReportEdit r, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(id, ct);
        var comp = string.IsNullOrWhiteSpace(r.Complication) ? "None" : r.Complication!;
        if (!Complications.Contains(comp)) return ["Choose a complication from the list (or None)."];
        if (comp != "None" && string.IsNullOrWhiteSpace(r.ComplicationDetails)) return ["Describe the complication and what was done."];
        if (r.SurveillanceMonths is < 1 or > 120) return ["Surveillance interval is 1–120 months (or blank)."];
        p.Complication = comp; p.ComplicationDetails = Clip(r.ComplicationDetails, 1000); p.Diagnosis = Clip(r.Diagnosis, 2000); p.Recommendations = Clip(r.Recommendations, 2000);
        p.FollowUp = Clip(r.FollowUp, 500); p.SurveillanceMonths = r.SurveillanceMonths; p.PatientAdvice = Clip(r.PatientAdvice, 2000);
        Touch(p, user);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Update", p, comp != "None" ? $"COMPLICATION: {comp}" : "report", ct);
        return [];
    }

    /// <summary>What must still be recorded before the endoscopist can sign (structured minimum dataset and quality measures).</summary>
    public async Task<IReadOnlyList<string>> MissingAsync(Procedure p, CancellationToken ct = default)
    {
        var m = new List<string>();
        if (p.EndoscopistId is null && string.IsNullOrWhiteSpace(p.EndoscopistName)) m.Add("Endoscopist");
        if (p.Indications.Count == 0 && string.IsNullOrWhiteSpace(p.OtherIndication)) m.Add("Indication");
        if (!p.ConsentConfirmed) m.Add("Consent confirmed");
        if (HasUpper(p.Kind))
        {
            if (p.UpperExtent is null) m.Add("Gastroscopy: extent reached");
            if (p.UpperStartAt is null || p.UpperEndAt is null) m.Add("Gastroscopy: start and end times");
            if (p.Retroflexion is null) m.Add("Gastroscopy: retroflexion (J-manoeuvre)");
        }
        if (HasLower(p.Kind))
        {
            if (p.LowerExtent is null) m.Add("Colonoscopy: extent reached");
            else if (IsColonoscopy(p.Kind) && p.LowerExtent is not (LowerExtent.Caecum or LowerExtent.TerminalIleum) && string.IsNullOrWhiteSpace(p.IncompleteReason)) m.Add("Colonoscopy: reason the caecum was not reached");
            if (p.InsertedAt is null || p.EndedAt is null) m.Add("Colonoscopy: insertion and scope-out times");
            if (IsColonoscopy(p.Kind) && p.LowerExtent is LowerExtent.Caecum or LowerExtent.TerminalIleum)
            {
                if (p.WithdrawalStartAt is null && p.CaecumAt is null) m.Add("Colonoscopy: caecum reached / withdrawal start time");
                if (p.AppendicealOrifice is null || p.IleocaecalValve is null) m.Add("Colonoscopy: caecal landmarks (appendix orifice, ileocaecal valve)");
            }
            if (IsColonoscopy(p.Kind) && (p.BbpsRight is null || p.BbpsTransverse is null || p.BbpsLeft is null)) m.Add("Bowel preparation (BBPS, each segment)");
            if (p.RectalRetroflexion is null) m.Add("Rectal retroflexion");
        }
        if (p.Comfort is null && p.Sedation is not (SedationKind.Propofol or SedationKind.GeneralAnaesthetic)) m.Add("Comfort score");
        var polyps = await db.Polyps.Where(x => x.ProcedureId == p.Id).ToListAsync(ct);
        foreach (var x in polyps.Where(x => x.Technique != "NotRemoved" && x.Retrieved == "Yes" && string.IsNullOrWhiteSpace(x.Pot))) m.Add($"Polyp {x.Number}: specimen pot");
        if (string.IsNullOrWhiteSpace(p.Diagnosis)) m.Add("Diagnosis / conclusion");
        if (string.IsNullOrWhiteSpace(p.Recommendations) && string.IsNullOrWhiteSpace(p.FollowUp)) m.Add("Recommendations or follow-up");
        return m;
    }

    public async Task<IReadOnlyList<string>> SignAsync(Guid id, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(id, ct);
        var missing = await MissingAsync(p, ct);
        if (missing.Count > 0) return ["Still to record: " + string.Join("; ", missing) + "."];
        if (await db.Media.AnyAsync(x => x.ProcedureId == p.Id && x.Status == MediaStatus.Recording, ct)) return ["A video is still recording. Stop it before signing."];
        p.Status = ProcedureStatus.Signed; p.SignedBy = user; p.SignedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await Audit(user, "Sign", p, null, ct);
        return [];
    }

    public async Task<string?> AmendAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(id, ct, allowSigned: true);
        if (p.Status != ProcedureStatus.Signed) return "The report is not signed.";
        if (string.IsNullOrWhiteSpace(reason)) return "Give the reason for amending a signed report.";
        p.Status = ProcedureStatus.Completed; p.UpdatedBy = user; p.UpdatedAt = clock.GetUtcNow(); p.GpLetterId = null; // the letter already sent stays in Letters; the corrected report can be sent again
        await db.SaveChangesAsync(ct);
        await Audit(user, "Amend", p, Clip(reason, 300), ct);
        return null;
    }

    /// <summary>Tower reported the procedure step in progress / completed (DICOM MPPS).</summary>
    public async Task MppsAsync(string accession, string mppsUid, bool completed, DateTimeOffset at, CancellationToken ct = default)
    {
        var p = await db.Procedures.SingleOrDefaultAsync(x => x.AccessionNumber == accession, ct);
        if (p is null || p.Status == ProcedureStatus.Signed) return;
        p.MppsUid = mppsUid;
        if (!completed)
        {
            if (p.Status == ProcedureStatus.Planned) p.Status = ProcedureStatus.InProgress;
            if (HasUpper(p.Kind) && p.UpperStartAt is null) p.UpperStartAt = at; else if (HasLower(p.Kind) && p.InsertedAt is null) p.InsertedAt = at;
        }
        else
        {
            if (HasLower(p.Kind) && p.EndedAt is null) p.EndedAt = at; else if (HasUpper(p.Kind) && p.UpperEndAt is null) p.UpperEndAt = at;
        }
        await db.SaveChangesAsync(ct);
        await Audit("tower", completed ? "MppsCompleted" : "MppsInProgress", p, mppsUid, ct);
    }

    // ------------------------------------------------------------------ findings, polyps, tattoos, therapy
    public async Task<(Finding? Row, IReadOnlyList<string> Errors)> SaveFindingAsync(Guid procedureId, Guid? id, FindingEdit r, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(procedureId, ct);
        var kind = FindingKinds.FirstOrDefault(k => k.Code == r.Kind);
        var errors = new List<string>();
        if (kind is null) errors.Add("Choose what was found.");
        if (!SiteFits(p.Kind, r.Site)) errors.Add("Choose a site examined in this procedure.");
        if (kind is not null && kind.Grades.Length > 0 && !string.IsNullOrWhiteSpace(r.Grade) && !kind.Grades.Contains(r.Grade)) errors.Add($"Choose the {kind.GradeLabel} from the list.");
        if (kind?.Code == "Other" && string.IsNullOrWhiteSpace(r.Description)) errors.Add("Describe the finding.");
        if (r.SizeMm is < 1 or > 300) errors.Add("Size is 1–300 mm.");
        if (errors.Count > 0) return (null, errors);
        var f = id is null ? null : await db.Findings.SingleOrDefaultAsync(x => x.Id == id && x.ProcedureId == p.Id, ct);
        if (id is not null && f is null) return (null, ["Finding not found."]);
        if (f is null) { f = new Finding { ProcedureId = p.Id, Number = await NextAsync(db.Findings.Where(x => x.ProcedureId == p.Id).Select(x => x.Number), ct) }; db.Findings.Add(f); }
        f.Site = r.Site; f.Kind = r.Kind; f.Grade = Clip(r.Grade, 60); f.SizeMm = r.SizeMm; f.Description = Clip(r.Description, 1000);
        Touch(p, user);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Finding", p, $"{f.Number}: {FindingLabel(f.Kind)} {f.Grade} at {SiteLabel(f.Site)}", ct);
        return (f, []);
    }

    public async Task<(Polyp? Row, IReadOnlyList<string> Errors)> SavePolypAsync(Guid procedureId, Guid? id, PolypEdit r, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(procedureId, ct);
        var errors = new List<string>();
        if (!SiteFits(p.Kind, r.Site)) errors.Add("Choose a site examined in this procedure.");
        if (r.SizeMm is < 1 or > 200) errors.Add("Polyp size is 1–200 mm (estimate against the open snare or forceps).");
        if (!Terminology.Paris.Contains(r.Paris)) errors.Add("Choose the Paris classification.");
        if (r.Nice is { Length: > 0 } && !Terminology.Nice.Contains(r.Nice)) errors.Add("NICE type is 1, 2 or 3.");
        if (r.Jnet is { Length: > 0 } && !Terminology.Jnet.Contains(r.Jnet)) errors.Add("JNET type is 1, 2A, 2B or 3.");
        if (Techniques.All(t => t.Code != r.Technique)) errors.Add("Choose how the polyp was removed (or Not removed).");
        if (!Resections.Contains(r.Resection) || !Retrievals.Contains(r.Retrieved) || !Completeness.Contains(r.Complete)) errors.Add("Choose resection, retrieval and completeness from the lists.");
        if (r.Technique == "NotRemoved" && (r.Resection != "NotRemoved" || r.Retrieved is not ("No" or "NotApplicable"))) errors.Add("A polyp that was not removed cannot be resected or retrieved.");
        if (r.Technique != "NotRemoved" && r.Resection == "NotRemoved") errors.Add("Say whether it came out en bloc or piecemeal.");
        if (r.Clips is < 0 or > 20) errors.Add("Clips 0–20.");
        if (r.DistanceCm is < 0 or > 200) errors.Add("Distance from the anus is 0–200 cm.");
        if (errors.Count > 0) return (null, errors);
        var x = id is null ? null : await db.Polyps.SingleOrDefaultAsync(q => q.Id == id && q.ProcedureId == p.Id, ct);
        if (id is not null && x is null) return (null, ["Polyp not found."]);
        if (x is null) { x = new Polyp { ProcedureId = p.Id, Number = await NextAsync(db.Polyps.Where(q => q.ProcedureId == p.Id).Select(q => q.Number), ct) }; db.Polyps.Add(x); }
        x.Site = r.Site; x.DistanceCm = r.DistanceCm; x.SizeMm = r.SizeMm; x.Paris = r.Paris; x.Nice = Clip(r.Nice, 10); x.Jnet = Clip(r.Jnet, 10); x.Technique = r.Technique;
        x.Lifted = r.Technique == "Emr" ? r.Lifted : null; x.Resection = r.Resection; x.Retrieved = r.Retrieved; x.Pot = Clip(r.Pot, 20); x.Complete = r.Complete;
        x.Clips = r.Clips; x.Tattooed = r.Tattooed; x.Notes = Clip(r.Notes, 500);
        Touch(p, user);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Polyp", p, $"{x.Number}: {x.SizeMm} mm {x.Paris} at {SiteLabel(x.Site)}, {x.Technique}, retrieved {x.Retrieved}", ct);
        return (x, []);
    }

    /// <summary>Histology can be added after the report is signed (results arrive later); it feeds the adenoma detection rate.</summary>
    public async Task<string?> HistologyAsync(Guid polypId, HistologyEdit r, string user, CancellationToken ct = default)
    {
        var x = await db.Polyps.SingleOrDefaultAsync(q => q.Id == polypId, ct);
        if (x is null) return "Polyp not found.";
        if (r.Histology is { Length: > 0 } && Histologies.All(h => h.Code != r.Histology)) return "Choose the histology from the list.";
        if (r.Dysplasia is { Length: > 0 } && !Dysplasias.Contains(r.Dysplasia)) return "Dysplasia is None, Low or High.";
        x.Histology = Clip(r.Histology, 40); x.Dysplasia = Clip(r.Dysplasia, 20); x.HistologyNotes = Clip(r.Notes, 500); x.HistologyBy = user; x.HistologyAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        var p = (await db.Procedures.FindAsync([x.ProcedureId], ct))!;
        await Audit(user, "Histology", p, $"polyp {x.Number}: {x.Histology} {x.Dysplasia}", ct);
        return null;
    }

    public async Task<(Tattoo? Row, IReadOnlyList<string> Errors)> SaveTattooAsync(Guid procedureId, Guid? id, TattooEdit r, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(procedureId, ct);
        var errors = new List<string>();
        if (!SiteFits(p.Kind, r.Site)) errors.Add("Choose a site examined in this procedure.");
        if (!TattooPositions.Contains(r.Position)) errors.Add("Say where the tattoo is in relation to the lesion (distal, proximal, both, opposite wall).");
        if (string.IsNullOrWhiteSpace(r.Ink)) errors.Add("Record the ink used (e.g. Spot).");
        if (r.VolumeMl is < 0 or > 20) errors.Add("Volume is 0–20 mL.");
        if (r.PolypId is { } pid && !await db.Polyps.AnyAsync(q => q.Id == pid && q.ProcedureId == p.Id, ct)) errors.Add("Choose a polyp from this procedure.");
        if (errors.Count > 0) return (null, errors);
        var t = id is null ? null : await db.Tattoos.SingleOrDefaultAsync(q => q.Id == id && q.ProcedureId == p.Id, ct);
        if (id is not null && t is null) return (null, ["Tattoo not found."]);
        if (t is null) { t = new Tattoo { ProcedureId = p.Id }; db.Tattoos.Add(t); }
        t.Site = r.Site; t.DistanceCm = r.DistanceCm; t.Position = r.Position; t.Ink = Clip(r.Ink, 60)!; t.VolumeMl = r.VolumeMl; t.Injections = r.Injections; t.PolypId = r.PolypId; t.Reason = Clip(r.Reason, 300);
        if (r.PolypId is { } linked && await db.Polyps.SingleOrDefaultAsync(q => q.Id == linked, ct) is { } lp) lp.Tattooed = true;
        Touch(p, user);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Tattoo", p, $"{SiteLabel(t.Site)} {t.DistanceCm} cm, {t.Position}", ct);
        return (t, []);
    }

    public async Task<(Intervention? Row, IReadOnlyList<string> Errors)> SaveInterventionAsync(Guid procedureId, Guid? id, InterventionEdit r, string user, CancellationToken ct = default)
    {
        var p = await OpenAsync(procedureId, ct);
        var errors = new List<string>();
        if (InterventionKinds.All(k => k.Code != r.Kind)) errors.Add("Choose what was done.");
        if (!SiteFits(p.Kind, r.Site)) errors.Add("Choose a site examined in this procedure.");
        if (r.Count is < 0 or > 50) errors.Add("Number is 0–50.");
        if (r.Kind == "Biopsy" && string.IsNullOrWhiteSpace(r.Pot)) errors.Add("Biopsies need the specimen pot label.");
        if (r.Kind == "Other" && string.IsNullOrWhiteSpace(r.Details)) errors.Add("Describe what was done.");
        if (errors.Count > 0) return (null, errors);
        var x = id is null ? null : await db.Interventions.SingleOrDefaultAsync(q => q.Id == id && q.ProcedureId == p.Id, ct);
        if (id is not null && x is null) return (null, ["Not found."]);
        if (x is null) { x = new Intervention { ProcedureId = p.Id }; db.Interventions.Add(x); }
        x.Kind = r.Kind; x.Site = r.Site; x.Count = r.Count; x.Pot = Clip(r.Pot, 20); x.Purpose = Clip(r.Purpose, 100); x.Details = Clip(r.Details, 500); x.Result = Clip(r.Result, 100);
        Touch(p, user);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Therapy", p, $"{x.Kind} {SiteLabel(x.Site)} x{x.Count}", ct);
        return (x, []);
    }

    /// <summary>Removes a finding, polyp, tattoo or therapy row (not allowed once signed). Pictures linked to it stay, unlinked.</summary>
    public async Task<string?> RemoveAsync(string what, Guid id, string user, CancellationToken ct = default)
    {
        Guid procedureId; string label;
        switch (what)
        {
            case "findings":
                var f = await db.Findings.SingleOrDefaultAsync(x => x.Id == id, ct); if (f is null) return "Not found.";
                procedureId = f.ProcedureId; label = $"finding {f.Number}";
                await OpenAsync(procedureId, ct);
                await db.Media.Where(m => m.FindingId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.FindingId, (Guid?)null), ct);
                db.Findings.Remove(f); break;
            case "polyps":
                var pl = await db.Polyps.SingleOrDefaultAsync(x => x.Id == id, ct); if (pl is null) return "Not found.";
                procedureId = pl.ProcedureId; label = $"polyp {pl.Number}";
                await OpenAsync(procedureId, ct);
                await db.Media.Where(m => m.PolypId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.PolypId, (Guid?)null), ct);
                await db.Tattoos.Where(t => t.PolypId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.PolypId, (Guid?)null), ct);
                db.Polyps.Remove(pl); break;
            case "tattoos":
                var t = await db.Tattoos.SingleOrDefaultAsync(x => x.Id == id, ct); if (t is null) return "Not found.";
                procedureId = t.ProcedureId; label = "tattoo"; await OpenAsync(procedureId, ct); db.Tattoos.Remove(t); break;
            case "therapy":
                var i = await db.Interventions.SingleOrDefaultAsync(x => x.Id == id, ct); if (i is null) return "Not found.";
                procedureId = i.ProcedureId; label = i.Kind; await OpenAsync(procedureId, ct); db.Interventions.Remove(i); break;
            default: return "Unknown item.";
        }
        await db.SaveChangesAsync(ct);
        await Audit(user, "Remove", (await db.Procedures.FindAsync([procedureId], ct))!, label, ct);
        return null;
    }

    // ------------------------------------------------------------------ pictures and video
    /// <summary>Stores a picture from the capture station, an upload or the tower (DICOM). Unmatched DICOM pictures (no procedure) wait in a queue.</summary>
    public async Task<(Media? Row, string? Error)> AddImageAsync(Guid? procedureId, byte[] data, MediaSource source, string user, DicomImageInfo? dicom = null, CancellationToken ct = default)
    {
        byte[]? original = null;
        if (Tiff.IsTiff(data))
        {
            var (png, terr) = Tiff.ToPng(data);
            if (png is null) return (null, terr);
            original = data; data = png;
        }
        var type = Imaging.Sniff(data);
        if (type is not ("image/jpeg" or "image/png")) return (null, "Only JPEG, PNG or TIFF pictures can be stored.");
        if (data.Length > 30 * 1024 * 1024) return (null, "The picture is larger than 30 MB.");
        Procedure? p = null;
        if (procedureId is { } pid) p = await OpenAsync(pid, ct, allowSigned: true);
        if (dicom?.SopInstanceUid is { Length: > 0 } sop && await db.Media.AnyAsync(m => m.SopInstanceUid == sop, ct)) return (await db.Media.SingleAsync(m => m.SopInstanceUid == sop, ct), null);
        var m = new Media
        {
            ProcedureId = p?.Id,
            PatientId = p?.PatientId,
            Kind = MediaKind.Image,
            Source = source,
            ContentType = type,
            CapturedAt = clock.GetUtcNow(),
            CapturedBy = user,
            SopInstanceUid = Clip(dicom?.SopInstanceUid, 64),
            DicomPatientId = Clip(dicom?.PatientId, 64),
            DicomPatientName = Clip(dicom?.PatientName, 100),
            DicomAccession = Clip(dicom?.Accession, 16),
            Sort = p is null ? 0 : await NextAsync(db.Media.Where(x => x.ProcedureId == p.Id).Select(x => x.Sort), ct),
        };
        await store.WriteAsync(m, data, ct);
        if (original is not null) await store.WriteOriginalAsync(m, original, "image/tiff", ct);
        db.Media.Add(m);
        if (p is not null) Touch(p, user);
        await db.SaveChangesAsync(ct);
        if (p is not null) await Audit(user, "Picture", p, $"{source} {m.Id}", ct);
        return (m, null);
    }

    public async Task<(Media? Row, string? Error)> AddVideoFileAsync(Guid procedureId, byte[] data, MediaSource source, string user, DicomImageInfo? dicom = null, CancellationToken ct = default)
    {
        var type = Imaging.Sniff(data);
        if (type is not ("video/mp4" or "video/webm")) return (null, "Only MP4 or WebM video can be stored.");
        var p = await OpenAsync(procedureId, ct, allowSigned: true);
        if (dicom?.SopInstanceUid is { Length: > 0 } sop && await db.Media.AnyAsync(x => x.SopInstanceUid == sop, ct)) return (await db.Media.SingleAsync(x => x.SopInstanceUid == sop, ct), null);
        var m = new Media
        {
            ProcedureId = p.Id,
            PatientId = p.PatientId,
            Kind = MediaKind.Video,
            Source = source,
            ContentType = type,
            CapturedAt = clock.GetUtcNow(),
            CapturedBy = user,
            SopInstanceUid = Clip(dicom?.SopInstanceUid, 64),
            Sort = await NextAsync(db.Media.Where(x => x.ProcedureId == p.Id).Select(x => x.Sort), ct)
        };
        await store.WriteAsync(m, data, ct);
        db.Media.Add(m);
        await db.SaveChangesAsync(ct);
        await Audit(user, "Video", p, $"{source} {m.Id}", ct);
        return (m, null);
    }

    /// <summary>Starts a recording from the capture station: chunks are appended as they arrive so nothing is lost if the browser closes.</summary>
    public async Task<(Media? Row, string? Error)> StartVideoAsync(Guid procedureId, string contentType, string user, CancellationToken ct = default)
    {
        var type = contentType.Split(';')[0].Trim().ToLowerInvariant();
        if (type is not ("video/webm" or "video/mp4")) return (null, "The browser must record WebM or MP4 video.");
        var p = await OpenAsync(procedureId, ct, allowSigned: true);
        var m = new Media
        {
            ProcedureId = p.Id,
            PatientId = p.PatientId,
            Kind = MediaKind.Video,
            Source = MediaSource.CaptureStation,
            Status = MediaStatus.Recording,
            ContentType = type,
            CapturedAt = clock.GetUtcNow(),
            CapturedBy = user,
            Sort = await NextAsync(db.Media.Where(x => x.ProcedureId == p.Id).Select(x => x.Sort), ct)
        };
        m.FileName = MediaStore.NewFileName(m);
        db.Media.Add(m);
        Touch(p, user);
        await db.SaveChangesAsync(ct);
        await Audit(user, "VideoStart", p, m.Id.ToString(), ct);
        return (m, null);
    }

    public async Task<string?> AppendVideoAsync(Guid mediaId, int seq, Stream chunk, CancellationToken ct = default)
    {
        var m = await db.Media.SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (m is null) return "Recording not found.";
        if (m.Status != MediaStatus.Recording) return "This recording has finished.";
        if (seq != m.Chunks) return seq < m.Chunks ? null : $"Chunk {seq} arrived out of order (expected {m.Chunks}).";
        await store.AppendAsync(m, chunk, ct);
        m.Chunks++;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> FinishVideoAsync(Guid mediaId, decimal? durationSeconds, string user, CancellationToken ct = default)
    {
        var m = await db.Media.SingleOrDefaultAsync(x => x.Id == mediaId, ct);
        if (m is null) return "Recording not found.";
        if (m.Status != MediaStatus.Recording) return null;
        await store.SealAsync(m, ct);
        m.Status = m.Bytes > 0 ? MediaStatus.Ready : MediaStatus.Deleted;
        if (m.Bytes == 0) { m.DeleteReason = "Empty recording"; m.DeletedAt = clock.GetUtcNow(); m.DeletedBy = user; }
        m.DurationSeconds = durationSeconds is > 0 and < 100000 ? Math.Round(durationSeconds.Value, 1) : null;
        await db.SaveChangesAsync(ct);
        var p = (await db.Procedures.FindAsync([m.ProcedureId!.Value], ct))!;
        await Audit(user, "VideoStop", p, $"{m.Id} {m.Bytes / 1024 / 1024} MB {m.DurationSeconds}s", ct);
        return null;
    }

    public async Task<string?> UpdateMediaAsync(Guid mediaId, MediaEdit r, string user, CancellationToken ct = default)
    {
        var m = await db.Media.SingleOrDefaultAsync(x => x.Id == mediaId && x.Status != MediaStatus.Deleted, ct);
        if (m?.ProcedureId is null) return "Picture not found.";
        if (r.Site is { Length: > 0 } && AllSites.All(s => s.Code != r.Site)) return "Choose a site from the list.";
        if (r.FindingId is { } f && !await db.Findings.AnyAsync(x => x.Id == f && x.ProcedureId == m.ProcedureId, ct)) return "Choose a finding from this procedure.";
        if (r.PolypId is { } pl && !await db.Polyps.AnyAsync(x => x.Id == pl && x.ProcedureId == m.ProcedureId, ct)) return "Choose a polyp from this procedure.";
        if (m.Kind == MediaKind.Video && (r.InReport || r.InPatientReport)) return "Videos cannot go into a printed report – choose a picture.";
        m.Site = Clip(r.Site, 40); m.Caption = Clip(r.Caption, 200); m.InReport = r.InReport; m.InPatientReport = r.InPatientReport; m.FindingId = r.FindingId; m.PolypId = r.PolypId;
        if (r.Sort is { } s) m.Sort = s;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Saves a drawn-on copy of a picture (arrows, circles, text). The original is never changed; the copy takes over the original's
    /// report choices so the annotated version is what is printed.
    /// </summary>
    public async Task<(Media? Row, string? Error)> AnnotateAsync(Guid mediaId, byte[] png, string user, CancellationToken ct = default)
    {
        var o = await db.Media.SingleOrDefaultAsync(x => x.Id == mediaId && x.Status == MediaStatus.Ready && x.Kind == MediaKind.Image, ct);
        if (o?.ProcedureId is null) return (null, "Picture not found.");
        if (Imaging.Sniff(png) is not ("image/png" or "image/jpeg")) return (null, "The drawing must be saved as a PNG or JPEG.");
        if (png.Length > 30 * 1024 * 1024) return (null, "The picture is larger than 30 MB.");
        var p = await OpenAsync(o.ProcedureId.Value, ct, allowSigned: true);
        var m = new Media
        {
            ProcedureId = o.ProcedureId,
            PatientId = o.PatientId,
            Kind = MediaKind.Image,
            Source = o.Source,
            ContentType = Imaging.Sniff(png)!,
            CapturedAt = clock.GetUtcNow(),
            CapturedBy = user,
            Site = o.Site,
            Caption = Clip(((o.Caption ?? "Picture") + " (annotated)"), 200),
            InReport = o.InReport,
            InPatientReport = o.InPatientReport,
            FindingId = o.FindingId,
            PolypId = o.PolypId,
            Sort = o.Sort,
            AnnotatedFromId = o.Id,
        };
        await store.WriteAsync(m, png, ct);
        o.InReport = false; o.InPatientReport = false;
        db.Media.Add(m);
        await db.SaveChangesAsync(ct);
        await Audit(user, "AnnotatePicture", p, $"{o.Id} → {m.Id}", ct);
        return (m, null);
    }

    /// <summary>Deleting keeps the row (who, when, why) and removes the file.</summary>
    public async Task<string?> DeleteMediaAsync(Guid mediaId, string reason, string user, CancellationToken ct = default)
    {
        var m = await db.Media.SingleOrDefaultAsync(x => x.Id == mediaId && x.Status != MediaStatus.Deleted, ct);
        if (m is null) return "Not found.";
        if (string.IsNullOrWhiteSpace(reason)) return "Give the reason for deleting.";
        await store.DeleteFileAsync(m, ct);
        m.Status = MediaStatus.Deleted; m.DeletedBy = user; m.DeletedAt = clock.GetUtcNow(); m.DeleteReason = Clip(reason, 300); m.InReport = false; m.InPatientReport = false;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "EndoscopyMedia", m.Id.ToString(), m.PatientId?.ToString(), reason: Clip(reason, 300), ct: ct);
        return null;
    }

    /// <summary>Pictures that arrived from a tower without a matching worklist entry are assigned to the right procedure by a person.</summary>
    public async Task<string?> AssignAsync(Guid mediaId, Guid procedureId, string user, CancellationToken ct = default)
    {
        var m = await db.Media.SingleOrDefaultAsync(x => x.Id == mediaId && x.ProcedureId == null && x.Status == MediaStatus.Ready, ct);
        if (m is null) return "Picture not found (or already assigned).";
        var p = await OpenAsync(procedureId, ct, allowSigned: true);
        m.ProcedureId = p.Id; m.PatientId = p.PatientId; m.Sort = await NextAsync(db.Media.Where(x => x.ProcedureId == p.Id).Select(x => x.Sort), ct);
        await db.SaveChangesAsync(ct);
        await Audit(user, "AssignPicture", p, $"{m.Id} (tower patient {m.DicomPatientId} {m.DicomPatientName})", ct);
        return null;
    }

    public async Task<IReadOnlyList<Media>> MediaAsync(Guid procedureId, CancellationToken ct = default) =>
        await db.Media.AsNoTracking().Where(m => m.ProcedureId == procedureId && m.Status != MediaStatus.Deleted).OrderBy(m => m.Sort).ThenBy(m => m.CapturedAt).ToListAsync(ct);
    public async Task<IReadOnlyList<Media>> UnmatchedAsync(CancellationToken ct = default) =>
        await db.Media.AsNoTracking().Where(m => m.ProcedureId == null && m.Status == MediaStatus.Ready).OrderByDescending(m => m.CapturedAt).Take(200).ToListAsync(ct);
    public async Task<Media?> MediaRowAsync(Guid id, CancellationToken ct = default) => await db.Media.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id && m.Status != MediaStatus.Deleted, ct);

    // ------------------------------------------------------------------ quality
    public static decimal? WithdrawalMinutes(Procedure p) =>
        (p.WithdrawalStartAt ?? p.CaecumAt) is { } s && p.EndedAt is { } e && e > s ? Math.Round((decimal)(e - s).TotalMinutes, 1) : null;
    public static decimal? UpperMinutes(Procedure p) => p.UpperStartAt is { } s && p.UpperEndAt is { } e && e > s ? Math.Round((decimal)(e - s).TotalMinutes, 1) : null;
    public static int? BbpsTotal(Procedure p) => p.BbpsRight is { } r && p.BbpsTransverse is { } t && p.BbpsLeft is { } l ? r + t + l : null;
    public static bool AdequatePrep(Procedure p) => BbpsTotal(p) is >= 6 && p.BbpsRight >= 2 && p.BbpsTransverse >= 2 && p.BbpsLeft >= 2;
    public static bool CaecumReached(Procedure p) => p.LowerExtent is LowerExtent.Caecum or LowerExtent.TerminalIleum;

    /// <summary>
    /// A surveillance suggestion for the endoscopist to confirm or change (based on number, size and histology of the polyps; follows the
    /// structure of the BSG/ACPGBI 2020 post-polypectomy guideline). Returns null until histology is known.
    /// </summary>
    public static string? SuggestSurveillance(Procedure p, IReadOnlyList<Polyp> polyps)
    {
        if (!IsColonoscopy(p.Kind) || !CaecumReached(p) || !AdequatePrep(p)) return IsColonoscopy(p.Kind) ? "Incomplete examination or inadequate preparation – consider a repeat procedure." : null;
        var removed = polyps.Where(x => x.Technique != "NotRemoved").ToList();
        if (removed.Count == 0) return "No polyps – no colonoscopy surveillance needed for polyps; back to the bowel screening programme or as clinically indicated.";
        if (removed.Any(x => x.Retrieved == "Yes" && x.Histology is null)) return null;
        if (removed.Any(x => x.Resection == "Piecemeal" && x.SizeMm >= 20)) return "Piecemeal removal of a polyp of 20 mm or more – site check in 2–6 months.";
        var premalignant = removed.Count(x => IsAdenoma(x.Histology) || x.Histology == "SessileSerrated");
        var highRisk = premalignant >= 5 || removed.Any(x => (IsAdenoma(x.Histology) || x.Histology == "SessileSerrated") && (x.SizeMm >= 10 || x.Dysplasia == "High"))
            || (premalignant >= 2 && removed.Any(x => (IsAdenoma(x.Histology) || x.Histology == "SessileSerrated") && x.SizeMm >= 10));
        return highRisk ? "High-risk findings – surveillance colonoscopy in 3 years." : "Low-risk findings – no surveillance colonoscopy needed; back to the bowel screening programme.";
    }

    public async Task<KpiTargets> TargetsAsync(CancellationToken ct = default)
    {
        async Task<decimal> T(string k, decimal d) => decimal.TryParse(await settings.GetAsync("endo.target." + k, ct), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : d;
        return new(await T("cir", 90), await T("adr", 25), await T("withdrawal", 6), await T("retrieval", 90), await T("prep", 90), await T("comfort", 10), await T("upper", 7));
    }

    public async Task SaveTargetsAsync(KpiTargets t, string user, CancellationToken ct = default)
    {
        foreach (var (k, v) in new[] { ("cir", t.CaecalIntubation), ("adr", t.Adr), ("withdrawal", t.WithdrawalMinutes), ("retrieval", t.Retrieval), ("prep", t.AdequatePrep), ("comfort", t.ComfortPoor), ("upper", t.UpperMinutes) })
            await settings.SetAsync("endo.target." + k, v.ToString(CultureInfo.InvariantCulture), user, ct);
    }

    /// <summary>Quality indicators per endoscopist (and "All") for completed/signed procedures whose date falls in the range.</summary>
    public async Task<IReadOnlyList<KpiRow>> KpisAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = NzTime.StartOfDay(from);
        var end = NzTime.StartOfDay(to.AddDays(1));
        var procs = await db.Procedures.AsNoTracking().Where(p => p.Status == ProcedureStatus.Completed || p.Status == ProcedureStatus.Signed).ToListAsync(ct);
        procs = procs.Where(p => (p.InsertedAt ?? p.UpperStartAt ?? p.CreatedAt) is var d && d >= start && d < end).ToList();
        var ids = procs.Select(p => p.Id).ToList();
        var polyps = (await db.Polyps.AsNoTracking().Where(x => ids.Contains(x.ProcedureId)).ToListAsync(ct)).ToLookup(x => x.ProcedureId);
        var rows = procs.GroupBy(p => p.EndoscopistName ?? "(not recorded)").OrderBy(g => g.Key).Select(g => Row(g.Key, g.ToList(), polyps)).ToList();
        if (rows.Count != 1) rows.Add(Row("All endoscopists", procs, polyps));
        return rows;
    }

    /// <summary>One row per completed colonoscopy in the period, for the quality indicator library (0.19.0): caecum reached and adenoma found.</summary>
    public sealed record ColonoscopyOutcome(Guid ProcedureId, Guid BookingId, Guid PatientId, DateOnly On, bool CaecumReached, bool Adenoma);

    public async Task<IReadOnlyList<ColonoscopyOutcome>> ColonoscopyOutcomesAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = NzTime.StartOfDay(from);
        var end = NzTime.StartOfDay(to.AddDays(1));
        var procs = await db.Procedures.AsNoTracking().Where(p => p.Status == ProcedureStatus.Completed || p.Status == ProcedureStatus.Signed).ToListAsync(ct);
        procs = procs.Where(p => IsColonoscopy(p.Kind) && (p.InsertedAt ?? p.UpperStartAt ?? p.CreatedAt) is var d && d >= start && d < end).ToList();
        var ids = procs.Select(p => p.Id).ToList();
        var polyps = (await db.Polyps.AsNoTracking().Where(x => ids.Contains(x.ProcedureId)).ToListAsync(ct)).ToLookup(x => x.ProcedureId);
        var nz = NzTime.Zone;
        return procs.Select(p => new ColonoscopyOutcome(p.Id, p.BookingId, p.PatientId, DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.InsertedAt ?? p.UpperStartAt ?? p.CreatedAt, nz).DateTime),
                CaecumReached(p), polyps[p.Id].Any(x => IsAdenoma(x.Histology))))
            .Where(o => o.On >= from && o.On <= to).ToList();
    }

    private static decimal? Pct(int n, int d) => d == 0 ? null : Math.Round(100m * n / d, 1);
    private static decimal? Median(IEnumerable<decimal> v) { var a = v.OrderBy(x => x).ToArray(); return a.Length == 0 ? null : a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2; }

    private static KpiRow Row(string name, List<Procedure> procs, ILookup<Guid, Polyp> polyps)
    {
        var colon = procs.Where(p => IsColonoscopy(p.Kind)).ToList();
        var upper = procs.Where(p => HasUpper(p.Kind)).ToList();
        var removed = colon.SelectMany(p => polyps[p.Id]).Where(x => x.Technique != "NotRemoved").ToList();
        var negative = colon.Where(p => !polyps[p.Id].Any(x => x.Technique != "NotRemoved")).Select(WithdrawalMinutes).OfType<decimal>().ToList();
        var allWithdrawal = colon.Select(WithdrawalMinutes).OfType<decimal>().ToList();
        var comfortScored = procs.Where(p => p.Comfort is not null).ToList();
        return new KpiRow(name, colon.Count,
            Pct(colon.Count(CaecumReached), colon.Count), Median(negative), Pct(allWithdrawal.Count(w => w >= 6), allWithdrawal.Count),
            Pct(colon.Count(p => polyps[p.Id].Any()), colon.Count),
            Pct(colon.Count(p => polyps[p.Id].Any(x => IsAdenoma(x.Histology))), colon.Count),
            Pct(colon.Count(p => polyps[p.Id].Any(x => IsSerrated(x.Histology))), colon.Count),
            Pct(removed.Count(x => x.Retrieved == "Yes"), removed.Count),
            Pct(colon.Count(AdequatePrep), colon.Count(p => BbpsTotal(p) is not null)),
            Pct(comfortScored.Count(p => p.Comfort >= 4), comfortScored.Count),
            Pct(colon.Count(p => p.RectalRetroflexion == true), colon.Count(p => p.RectalRetroflexion is not null)),
            colon.Count(p => p.Complication != "None"),
            upper.Count, Pct(upper.Count(p => p.UpperExtent == UpperExtent.D2), upper.Count), Pct(upper.Count(p => p.Retroflexion == true), upper.Count),
            Median(upper.Select(UpperMinutes).OfType<decimal>()), Pct(upper.Count(p => UpperMinutes(p) >= 7), upper.Count(p => UpperMinutes(p) is not null)),
            Pct(upper.Count(p => p.UpperPhotoDocumentation == true), upper.Count), upper.Count(p => p.Complication != "None" && p.Kind == ProcedureKind.Gastroscopy));
    }

    // ------------------------------------------------------------------ National Bowel Screening Programme extract
    public sealed record NbspRow(Guid ProcedureId, Guid PatientId, string Accession, string? NbspNumber, DateOnly Date, string Status, string? Endoscopist, Guid? EndoscopistId,
        string Kind, string Indications, string Sedation, string? Extent, bool CaecumReached, bool? TerminalIleum, int? Bbps, bool AdequatePrep, decimal? WithdrawalMinutes, int? Comfort,
        int PolypsSeen, int PolypsRemoved, int PolypsRetrieved, int? LargestPolypMm, int Adenomas, int AdvancedAdenomas, int Serrated, bool CancerSuspected, bool CancerConfirmed,
        int HistologyAwaited, int Tattoos, string Complication, int? SurveillanceMonths, DateTimeOffset? ExportedAt, IReadOnlyList<string> Problems);

    /// <summary>NBSP colonoscopies in the range with the screening dataset and a list of what is missing for each (NBSP number, signing, histology).</summary>
    public async Task<IReadOnlyList<NbspRow>> NbspAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var procs = await db.Procedures.AsNoTracking().Include(p => p.Indications).Where(p => p.NbspScreening).ToListAsync(ct);
        var start = NzTime.StartOfDay(from);
        var end = NzTime.StartOfDay(to.AddDays(1));
        procs = procs.Where(p => (p.InsertedAt ?? p.CreatedAt) is var d && d >= start && d < end).OrderBy(p => p.InsertedAt ?? p.CreatedAt).ToList();
        var ids = procs.Select(p => p.Id).ToList();
        var polyps = (await db.Polyps.AsNoTracking().Where(x => ids.Contains(x.ProcedureId)).ToListAsync(ct)).ToLookup(x => x.ProcedureId);
        var findings = (await db.Findings.AsNoTracking().Where(x => ids.Contains(x.ProcedureId)).ToListAsync(ct)).ToLookup(x => x.ProcedureId);
        var tattoos = (await db.Tattoos.AsNoTracking().Where(x => ids.Contains(x.ProcedureId)).ToListAsync(ct)).ToLookup(x => x.ProcedureId);
        return procs.Select(p =>
        {
            var pl = polyps[p.Id].ToList();
            var removed = pl.Where(x => x.Technique != "NotRemoved").ToList();
            var adenomas = pl.Where(x => IsAdenoma(x.Histology) && x.Histology != "Adenocarcinoma").ToList();
            var advanced = adenomas.Count(x => x.SizeMm >= 10 || x.Dysplasia == "High" || x.Histology is "TubulovillousAdenoma" or "VillousAdenoma");
            var problems = new List<string>();
            if (string.IsNullOrWhiteSpace(p.NbspNumber)) problems.Add("NBSP participant number missing");
            if (p.Status != ProcedureStatus.Signed) problems.Add("report not signed");
            var awaited = removed.Count(x => x.Retrieved == "Yes" && x.Histology is null);
            if (awaited > 0) problems.Add($"histology awaited for {awaited} polyp(s)");
            return new NbspRow(p.Id, p.PatientId, p.AccessionNumber, p.NbspNumber, DateOnly.FromDateTime((p.InsertedAt ?? p.CreatedAt).ToNz().DateTime), p.Status.ToString(),
                p.EndoscopistName, p.EndoscopistId, p.Kind.ToString(), string.Join("; ", p.Indications.Select(i => IndicationLabel(i.Code))), p.Sedation.ToString(), p.LowerExtent?.ToString(),
                CaecumReached(p), p.TerminalIleum, BbpsTotal(p), AdequatePrep(p), WithdrawalMinutes(p), p.Comfort,
                pl.Count, removed.Count, removed.Count(x => x.Retrieved == "Yes"), pl.Count == 0 ? null : pl.Max(x => x.SizeMm), adenomas.Count, advanced, pl.Count(x => IsSerrated(x.Histology)),
                findings[p.Id].Any(f => f.Kind == "Mass"), pl.Any(x => x.Histology == "Adenocarcinoma"), awaited, tattoos[p.Id].Count(), p.Complication, p.SurveillanceMonths, p.NbspExportedAt, problems);
        }).ToList();
    }

    public async Task MarkNbspExportedAsync(IEnumerable<Guid> ids, string user, CancellationToken ct = default)
    {
        var list = ids.ToList();
        var now = clock.GetUtcNow();
        await db.Procedures.Where(p => list.Contains(p.Id)).ExecuteUpdateAsync(s => s.SetProperty(p => p.NbspExportedAt, now), ct);
        await audit.WriteAsync(user, "NbspExtract", "EndoscopyProcedure", $"{list.Count} procedure(s)", reason: string.Join(",", list.Take(50)), ct: ct);
    }

    public async Task SetGpLetterAsync(Guid procedureId, Guid letterId, CancellationToken ct = default) =>
        await db.Procedures.Where(p => p.Id == procedureId).ExecuteUpdateAsync(s => s.SetProperty(p => p.GpLetterId, letterId), ct);

    // ------------------------------------------------------------------ patient report wording
    public async Task<IReadOnlyList<AdviceText>> AdviceAsync(CancellationToken ct = default)
    {
        if (!await db.Advice.AnyAsync(ct))
        {
            foreach (var (k, t, b) in DefaultAdvice) db.Advice.Add(new AdviceText { Key = k, Title = t, Body = b });
            await db.SaveChangesAsync(ct);
        }
        return await db.Advice.AsNoTracking().OrderBy(a => a.Key).ToListAsync(ct);
    }

    public async Task<string?> SaveAdviceAsync(string key, string title, string body, string user, CancellationToken ct = default)
    {
        await AdviceAsync(ct);
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) return "Title and text are both needed.";
        var a = await db.Advice.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (a is null) { a = new AdviceText { Key = Clip(key, 40)! }; db.Advice.Add(a); }
        a.Title = Clip(title, 100)!; a.Body = Clip(body, 4000)!; a.UpdatedBy = user; a.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "EndoscopyAdvice", key, reason: title, ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ helpers
    private static bool SiteFits(ProcedureKind k, string site) => (HasUpper(k) && IsUpperSite(site)) || (HasLower(k) && IsLowerSite(site));
    private static async Task<int> NextAsync(IQueryable<int> numbers, CancellationToken ct) => (await numbers.MaxAsync(n => (int?)n, ct) ?? 0) + 1;
    private Task Audit(string user, string action, Procedure p, string? reason, CancellationToken ct) =>
        audit.WriteAsync(user, action, "EndoscopyProcedure", p.Id.ToString(), p.PatientId.ToString(), reason: reason, ct: ct);
    internal static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : (s.Trim().Length <= max ? s.Trim() : s.Trim()[..max]);
}
