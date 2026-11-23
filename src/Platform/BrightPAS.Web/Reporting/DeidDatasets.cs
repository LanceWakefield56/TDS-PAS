using System.Globalization;
using BrightPAS.Modules.FollowUp;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Research;
using BrightPAS.Modules.Theatre;
using BrightPAS.Modules.Triage;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Web.Reporting;

/// <summary>Patient columns shared by every dataset (classified for de-identification, spec 7.21.2).</summary>
internal static class PatientColumns
{
    public static readonly ColumnDef[] Defs =
    [
        new("patient_name", "Patient name", ColumnKind.DirectId), new("nhi", "NHI", ColumnKind.DirectId), new("mrn", "MRN", ColumnKind.DirectId),
        new("date_of_birth", "Age", ColumnKind.BirthDate), new("sex", "Sex", ColumnKind.Category, true), new("ethnicity", "Ethnicity (prioritised)", ColumnKind.Ethnicity, true),
        new("town", "Region", ColumnKind.Place),
    ];

    /// <summary>Prioritised level-1 ethnic group (Stats NZ): Māori, Pacific, Asian, MELAA, Other, European.</summary>
    public static string Ethnicity(IEnumerable<string> codes)
    {
        var first = codes.Select(c => c.Length > 0 ? c[0] : '9').ToHashSet();
        foreach (var (d, name) in new[] { ('2', "Māori"), ('3', "Pacific peoples"), ('4', "Asian"), ('5', "Middle Eastern/Latin American/African"), ('6', "Other"), ('1', "European") })
            if (first.Contains(d)) return name;
        return "Not stated";
    }

    public static async Task<Dictionary<Guid, Dictionary<string, string?>>> LoadAsync(PatientsDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        var pts = await db.Patients.AsNoTracking().Include(p => p.Ethnicities).Where(p => list.Contains(p.Id) && !p.IsTestPatient).ToListAsync(ct);
        return pts.ToDictionary(p => p.Id, p => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["patient_name"] = $"{p.GivenNames} {p.FamilyName}",
            ["nhi"] = p.Nhi,
            ["mrn"] = p.Mrn,
            ["date_of_birth"] = p.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["sex"] = p.Sex.ToString(),
            ["ethnicity"] = Ethnicity(p.Ethnicities.OrderBy(e => e.Order).Select(e => e.Code)),
            ["town"] = p.City
        });
    }

    public static DataRow Row(Guid pid, Dictionary<string, string?> patient, Dictionary<string, string?> own)
    {
        foreach (var kv in patient) own[kv.Key] = kv.Value;
        return new DataRow(pid, own);
    }
}

public sealed class EpisodesDataset(TheatreDbContext theatre, TheatreService theatreService, TriageDbContext triage, PatientsDbContext patients) : IDatasetSource
{
    public string Key => "episodes";
    public string Label => "Operations and procedures";
    public IReadOnlyList<ColumnDef> Columns { get; } =
    [
        .. PatientColumns.Defs, new("procedure", "Procedure", ColumnKind.Category, true), new("operation_date", "Operation date", ColumnKind.Date),
        new("anaesthetic", "Anaesthetic", ColumnKind.Category), new("payment", "Funding", ColumnKind.Category), new("outcome", "Outcome", ColumnKind.Category),
        new("surgeon", "Surgeon", ColumnKind.Clinician), new("triage_outcome", "Triage outcome", ColumnKind.Category), new("asa", "ASA", ColumnKind.Number), new("bmi", "BMI", ColumnKind.Number),
    ];

    public async Task<IReadOnlyList<DataRow>> RowsAsync(DateOnly @from, DateOnly to, CancellationToken ct)
    {
        var rows = await (from b in theatre.Bookings.AsNoTracking()
                          join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                          where s.Date >= @from && s.Date <= to && b.Status != BookingStatus.Cancelled
                          select new { b.Id, b.PatientId, b.Procedure, s.Date, b.Anaesthetic, b.Payment, b.Status, s.SurgeonId }).ToListAsync(ct);
        var names = await theatreService.ClinicianNamesAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var tri = await triage.Assessments.AsNoTracking().Where(a => a.BookingId != null && ids.Contains(a.BookingId.Value) && a.Status == AssessmentStatus.SignedOff).ToListAsync(ct);
        var pts = await PatientColumns.LoadAsync(patients, rows.Select(r => r.PatientId), ct);
        return [.. rows.Where(r => pts.ContainsKey(r.PatientId)).Select(r =>
        {
            var t = tri.Where(a => a.BookingId == r.Id).OrderByDescending(a => a.SignedOffAt).FirstOrDefault();
            return PatientColumns.Row(r.PatientId, pts[r.PatientId], new(StringComparer.Ordinal)
            {
                ["procedure"] = r.Procedure, ["operation_date"] = r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["anaesthetic"] = r.Anaesthetic.ToString(), ["payment"] = r.Payment.ToString(),
                ["outcome"] = r.Status.ToString(), ["surgeon"] = names.GetValueOrDefault(r.SurgeonId), ["triage_outcome"] = t is null ? null : Outcomes.Labels[t.FinalOutcome ?? t.SuggestedOutcome],
                ["asa"] = (t?.ConfirmedAsa ?? t?.SuggestedAsa)?.ToString(CultureInfo.InvariantCulture), ["bmi"] = t?.Bmi?.ToString("0.0", CultureInfo.InvariantCulture)
            });
        })];
    }
}

public sealed class SsiDataset(FollowUpDbContext followup, PatientsDbContext patients) : IDatasetSource
{
    public string Key => "ssi";
    public string Label => "Surgical site infections";
    public IReadOnlyList<ColumnDef> Columns { get; } =
    [
        .. PatientColumns.Defs, new("procedure", "Procedure", ColumnKind.Category, true), new("operation_date", "Operation date", ColumnKind.Date), new("onset_day", "Onset (days after)", ColumnKind.Number),
        new("class", "Classification", ColumnKind.Category), new("source", "Found by", ColumnKind.Category), new("antibiotics", "Antibiotics", ColumnKind.Category), new("return_to_theatre", "Return to theatre", ColumnKind.Category),
        new("implant", "Implant", ColumnKind.Category), new("wound_class", "Wound class", ColumnKind.Category), new("asa", "ASA", ColumnKind.Number), new("surgeon", "Surgeon", ColumnKind.Clinician), new("signs", "Signs (free text)", ColumnKind.FreeText),
    ];

    public async Task<IReadOnlyList<DataRow>> RowsAsync(DateOnly @from, DateOnly to, CancellationToken ct)
    {
        var cases = await followup.SsiCases.AsNoTracking().Where(c => c.OperationDate >= @from && c.OperationDate <= to && c.Class != SsiClass.NotSsi).ToListAsync(ct);
        var pts = await PatientColumns.LoadAsync(patients, cases.Select(c => c.PatientId), ct);
        static string Yn(bool b) => b ? "Yes" : "No";
        return [.. cases.Where(c => pts.ContainsKey(c.PatientId)).Select(c => PatientColumns.Row(c.PatientId, pts[c.PatientId], new(StringComparer.Ordinal)
        {
            ["procedure"] = c.Procedure, ["operation_date"] = c.OperationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["onset_day"] = (c.OnsetOn.DayNumber - c.OperationDate.DayNumber).ToString(CultureInfo.InvariantCulture),
            ["class"] = FollowUpScript.ClassLabels[c.Class], ["source"] = c.Source.ToString(), ["antibiotics"] = Yn(c.Antibiotics), ["return_to_theatre"] = Yn(c.ReturnToTheatre), ["implant"] = Yn(c.Implant),
            ["wound_class"] = c.WoundClass, ["asa"] = c.Asa?.ToString(CultureInfo.InvariantCulture), ["surgeon"] = c.Surgeon, ["signs"] = c.Signs
        }))];
    }
}

public sealed class FollowUpDataset(FollowUpDbContext followup, PatientsDbContext patients) : IDatasetSource
{
    public string Key => "followup";
    public string Label => "Post-operative follow-up";
    public IReadOnlyList<ColumnDef> Columns { get; } =
    [
        .. PatientColumns.Defs, new("procedure", "Procedure", ColumnKind.Category, true), new("operation_date", "Operation date", ColumnKind.Date), new("contact", "Contact", ColumnKind.Category),
        new("status", "Result", ColumnKind.Category), new("concern", "Concern", ColumnKind.Category), new("escalation", "Escalation", ColumnKind.Category), new("pain", "Pain (0–10)", ColumnKind.Number),
        new("satisfaction", "Satisfaction (0–10)", ColumnKind.Number), new("comments", "Patient comments (free text)", ColumnKind.FreeText),
    ];

    public async Task<IReadOnlyList<DataRow>> RowsAsync(DateOnly @from, DateOnly to, CancellationToken ct)
    {
        var rows = await followup.Contacts.AsNoTracking().Include(c => c.Answers).Where(c => c.OperationDate >= @from && c.OperationDate <= to && c.Status != ContactStatus.Cancelled).ToListAsync(ct);
        var pts = await PatientColumns.LoadAsync(patients, rows.Select(c => c.PatientId), ct);
        string? A(Contact c, string k) => c.Answers.FirstOrDefault(a => a.QuestionKey == k)?.Value;
        return [.. rows.Where(c => pts.ContainsKey(c.PatientId)).Select(c => PatientColumns.Row(c.PatientId, pts[c.PatientId], new(StringComparer.Ordinal)
        {
            ["procedure"] = c.Procedure, ["operation_date"] = c.OperationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["contact"] = c.Kind.ToString(), ["status"] = c.Status.ToString(),
            ["concern"] = c.Concern ? "Yes" : "No", ["escalation"] = FollowUpScript.EscalationLabels[c.Escalation], ["pain"] = A(c, "pain"), ["satisfaction"] = A(c, "satisfaction"), ["comments"] = A(c, "concerns")
        }))];
    }
}
