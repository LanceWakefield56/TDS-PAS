using System.Globalization;
using System.Text;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Quality;
using BrightPAS.Modules.Theatre;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.FollowUp;

public sealed record SsiOpen(Guid BookingId, Guid? ContactId, DateOnly OnsetOn, string Signs, SsiSource Source, bool Antibiotics, bool ReturnToTheatre, bool Readmitted, string? Microbiology);

/// <summary>
/// SSI surveillance (spec 7.26.3, CDC/HQSC): suspected → confirmed superficial / deep / organ-space (or not an SSI) by the infection prevention lead,
/// with the operation's risk factors kept at the time; confirmed SSIs can raise a Quality incident; CSV export for HQSC/private hospital reporting.
/// </summary>
public sealed class SsiService(FollowUpDbContext db, TheatreDbContext theatre, QualityService quality, IEnumerable<IOperationFacts> facts, AuditWriter audit, TimeProvider clock)
{

    public async Task<(SsiCase? Case, IReadOnlyList<string> Errors)> OpenAsync(SsiOpen r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var b = await (from x in theatre.Bookings.AsNoTracking() join s in theatre.Sessions.AsNoTracking() on x.SessionId equals s.Id where x.Id == r.BookingId select new { x.PatientId, x.Procedure, s.Date }).SingleOrDefaultAsync(ct);
        if (b is null) return (null, ["Operation not found."]);
        if (r.OnsetOn < b.Date) errors.Add("The infection cannot start before the operation.");
        if (r.OnsetOn > NzTime.Today(clock)) errors.Add("The start date cannot be in the future.");
        if (string.IsNullOrWhiteSpace(r.Signs)) errors.Add("Describe the signs.");
        if (!Enum.IsDefined(r.Source)) errors.Add("Say where the information came from.");
        if (await db.SsiCases.AnyAsync(c => c.BookingId == r.BookingId && c.Class != SsiClass.NotSsi, ct)) errors.Add("There is already an SSI record for this operation.");
        if (errors.Count > 0) return (null, errors);
        OperationFacts f = new(false, false, null, null, null, new Dictionary<string, string>());
        foreach (var x in facts) { f = await x.ForBookingAsync(r.BookingId, ct); break; }
        var days = f.Implant ? 90 : 30;
        if (r.OnsetOn > b.Date.AddDays(days)) errors.Add($"Onset is more than {days} days after the operation – outside SSI surveillance.");
        if (errors.Count > 0) return (null, errors);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var s = await db.Settings.SingleAsync(ct);
            var c = new SsiCase
            {
                Number = $"SSI-{NzTime.Today(clock).Year}-{s.NextSsiNumber:D3}",
                PatientId = b.PatientId,
                BookingId = r.BookingId,
                ContactId = r.ContactId,
                Procedure = b.Procedure,
                Surgeon = f.Surgeon,
                OperationDate = b.Date,
                OnsetOn = r.OnsetOn,
                Signs = r.Signs.Trim().Length > 1000 ? r.Signs.Trim()[..1000] : r.Signs.Trim(),
                Source = r.Source,
                Antibiotics = r.Antibiotics,
                ReturnToTheatre = r.ReturnToTheatre,
                Readmitted = r.Readmitted,
                Microbiology = string.IsNullOrWhiteSpace(r.Microbiology) ? null : r.Microbiology.Trim(),
                SurveillanceDays = days,
                Implant = f.Implant,
                WoundClass = f.WoundClass,
                Asa = f.Asa,
                Risk = [.. f.Risk.Select(kv => new SsiRiskFactor { Name = kv.Key.Length > 60 ? kv.Key[..60] : kv.Key, Value = kv.Value.Length > 300 ? kv.Value[..300] : kv.Value })],
                CreatedAt = clock.GetUtcNow(),
                CreatedBy = user
            };
            s.NextSsiNumber++;
            db.SsiCases.Add(c);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await audit.WriteAsync(user, "Create", "SsiCase", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} suspected SSI after {c.Procedure} (onset {c.OnsetOn:yyyy-MM-dd}, {c.Source})", ct: ct);
            return ((SsiCase?)c, (IReadOnlyList<string>)[]);
        });
    }

    /// <summary>The infection prevention lead confirms the CDC/HQSC class (or that it is not an SSI). Deep and organ/space need the evidence noted.</summary>
    public async Task<string?> ConfirmAsync(Guid id, SsiClass cls, string? note, string user, CancellationToken ct = default)
    {
        var c = await db.SsiCases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "SSI record not found.";
        if (cls == SsiClass.Suspected || !Enum.IsDefined(cls)) return "Choose the classification.";
        if (cls is SsiClass.Deep or SsiClass.OrganSpace or SsiClass.NotSsi && string.IsNullOrWhiteSpace(note)) return "Note the evidence for this classification (CDC/HQSC criteria met).";
        var old = c.Class;
        c.Class = cls; c.ConfirmedBy = user; c.ConfirmedAt = clock.GetUtcNow(); c.ConfirmNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Confirm", "SsiCase", id.ToString(), c.PatientId.ToString(), reason: $"{c.Number}: {old} → {cls}. {note}", ct: ct);
        return null;
    }

    public async Task<(Guid? IncidentId, IReadOnlyList<string> Errors)> RaiseIncidentAsync(Guid id, string user, CancellationToken ct = default)
    {
        var c = await db.SsiCases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return (null, ["SSI record not found."]);
        if (c.IncidentId is not null) return (c.IncidentId, ["An incident has already been raised."]);
        if (c.Class is SsiClass.Suspected or SsiClass.NotSsi) return (null, ["Confirm the SSI first."]);
        var harm = c.Class == SsiClass.Superficial ? Harm.Minor : c.ReturnToTheatre || c.Readmitted ? Harm.Severe : Harm.Moderate;
        var (i, errors) = await quality.ReportAsync(new IncidentReport(NzTime.StartOfDay(c.OnsetOn),
            "Post-operative (surgical site infection)", IncidentCategory.Infection,
            $"{c.Number}: {FollowUpScript.ClassLabels[c.Class]} surgical site infection after {c.Procedure} on {c.OperationDate:d MMM yyyy}. Signs: {c.Signs}.", null, c.PatientId, c.BookingId, null, c.Surgeon, harm, false), user, ct);
        if (i is null) return (null, errors);
        c.IncidentId = i.Id;
        await db.SaveChangesAsync(ct);
        return (i.Id, []);
    }

    public async Task<IReadOnlyList<SsiCase>> ListAsync(bool openOnly, CancellationToken ct = default) =>
        await db.SsiCases.AsNoTracking().Where(c => !openOnly || c.Class == SsiClass.Suspected).OrderByDescending(c => c.CreatedAt).Take(500).ToListAsync(ct);

    public async Task<SsiCase?> GetAsync(Guid id, string user, string? workstation, CancellationToken ct = default)
    {
        var c = await db.SsiCases.AsNoTracking().Include(x => x.Risk).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is not null) await audit.WriteAsync(user, "View", "SsiCase", id.ToString(), c.PatientId.ToString(), workstation: workstation, ct: ct);
        return c;
    }

    /// <summary>
    /// CSV for HQSC SSI Improvement Programme / private hospital SSI reporting: one row per operation under surveillance with the outcome. No names;
    /// NHI is included because the programme matches records – the file goes through the secure (encrypted) export.
    /// </summary>
    public async Task<byte[]> ExportCsvAsync(DateOnly @from, DateOnly to, CancellationToken ct = default)
    {
        var surveyed = await db.Contacts.AsNoTracking().Where(c => c.Kind == ContactKind.SsiCheck && c.OperationDate >= @from && c.OperationDate <= to)
            .Select(c => new { c.BookingId, c.PatientId, c.Procedure, c.OperationDate, c.Status }).ToListAsync(ct);
        var cases = await db.SsiCases.AsNoTracking().Where(c => c.OperationDate >= @from && c.OperationDate <= to).ToListAsync(ct);
        var sb = new StringBuilder("operation_date,procedure,surveillance_days,followup_complete,ssi,ssi_class,onset_days,source,antibiotics,return_to_theatre,readmitted,implant,wound_class,asa,case_number\r\n");
        static string Q(string? s) => s is null ? "" : s.Contains(',', StringComparison.Ordinal) || s.Contains('"', StringComparison.Ordinal) ? $"\"{s.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : s;
        foreach (var o in surveyed.GroupBy(x => x.BookingId).Select(g => g.First()).OrderBy(x => x.OperationDate))
        {
            var c = cases.FirstOrDefault(x => x.BookingId == o.BookingId && x.Class != SsiClass.NotSsi);
            sb.Append(CultureInfo.InvariantCulture, $"{o.OperationDate:yyyy-MM-dd},{Q(o.Procedure)},{c?.SurveillanceDays ?? 30},{(o.Status == ContactStatus.Done ? "Y" : "N")},{(c is not null && c.Class != SsiClass.Suspected ? "Y" : c is not null ? "S" : "N")},")
              .Append(CultureInfo.InvariantCulture, $"{c?.Class.ToString() ?? ""},{(c is null ? "" : (c.OnsetOn.DayNumber - c.OperationDate.DayNumber).ToString(CultureInfo.InvariantCulture))},{c?.Source.ToString() ?? ""},")
              .Append(CultureInfo.InvariantCulture, $"{Yn(c?.Antibiotics)},{Yn(c?.ReturnToTheatre)},{Yn(c?.Readmitted)},{Yn(c?.Implant)},{Q(c?.WoundClass)},{c?.Asa?.ToString(CultureInfo.InvariantCulture) ?? ""},{c?.Number ?? ""}\r\n");
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string Yn(bool? b) => b is null ? "" : b.Value ? "Y" : "N";
}
