using BrightPAS.Core;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Licensing;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Endoscopy;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Quality;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Built-in quality indicator calculations (0.19.0, spec 7.14.1 / 7.14.4): every number comes from live PAS data, case by case, so a rate can be
/// drilled down to the patients behind it and split by ethnicity (prioritised: Māori, Pacific, Other). A calculation whose source module is not
/// licensed returns nothing.
/// </summary>
public sealed class QualityIndicatorCalculators(IServiceProvider sp, LicenceCache licence) : IIndicatorCalculators
{
    private static readonly TimeZoneInfo Nz = TryZone("Pacific/Auckland");
    private static TimeZoneInfo TryZone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; } }
    private static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t, Nz).DateTime);
    private static (DateTimeOffset From, DateTimeOffset To) Range(DateOnly from, DateOnly to) =>
        (NzTime.StartOfDay(from), NzTime.StartOfDay(to.AddDays(1)));

    public IReadOnlyList<(string Key, string Name)> Available { get; } =
    [
        ("dosc", "Day-of-surgery cancellations (theatre bookings)"), ("transfer", "Transfers to hospital (clinical record)"), ("ssc", "Surgical Safety Checklist complete (clinical record)"),
        ("count", "Count discrepancies (clinical record)"), ("ews", "Early warning score ≥ 5 (clinical record)"), ("inc1000", "Incidents per 1,000 episodes"),
        ("fall1000", "Falls per 1,000 episodes"), ("sac12", "SAC 1–2 incidents"), ("cmpack", "Complaints acknowledged within 5 working days"),
        ("cir", "Caecal intubation (endoscopy)"), ("adr", "Adenoma detection (endoscopy)"),
        ("ssi", "Surgical site infections (confirmed, per operation under surveillance)"), ("fucomplete", "Follow-up contacts completed"),
    ];

    private bool Has(string module) => licence.Status.AccessTo(module) != ModuleAccess.None;

    public async Task<IReadOnlyList<IndicatorCase>?> CasesAsync(string key, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var scope = sp.CreateAsyncScope();
        var s = scope.ServiceProvider;
        var (a, b) = Range(from, to);
        bool In(DateTimeOffset? t) => t is { } x && Day(x) >= from && Day(x) <= to;
        switch (key)
        {
            case "ssi":
                {
                    if (!Has(BrightPAS.Modules.FollowUp.FollowUpModule.Id)) return null;
                    var fdb = s.GetRequiredService<BrightPAS.Modules.FollowUp.FollowUpDbContext>();
                    var ops = await fdb.Contacts.AsNoTracking().Where(c => c.Kind == BrightPAS.Modules.FollowUp.ContactKind.SsiCheck && c.OperationDate >= @from && c.OperationDate <= to)
                        .Select(c => new { c.BookingId, c.PatientId, c.Procedure, c.OperationDate }).Distinct().ToListAsync(ct);
                    var cases = await fdb.SsiCases.AsNoTracking().Where(c => c.OperationDate >= @from && c.OperationDate <= to
                        && (c.Class == BrightPAS.Modules.FollowUp.SsiClass.Superficial || c.Class == BrightPAS.Modules.FollowUp.SsiClass.Deep || c.Class == BrightPAS.Modules.FollowUp.SsiClass.OrganSpace)).Select(c => c.BookingId).ToListAsync(ct);
                    var groups = await GroupsAsync(s, ops.Select(o => o.PatientId), ct);
                    return ops.GroupBy(o => o.BookingId).Select(g => g.First()).Select(o => new IndicatorCase(o.OperationDate, true, cases.Contains(o.BookingId) ? 1 : 0, $"{o.OperationDate:d MMM} {o.Procedure}", "/followup/ssi", groups.GetValueOrDefault(o.PatientId, "Not stated"))).ToList();
                }
            case "fucomplete":
                {
                    if (!Has(BrightPAS.Modules.FollowUp.FollowUpModule.Id)) return null;
                    var today = NzTime.Today(s.GetRequiredService<TimeProvider>());
                    var rows = await s.GetRequiredService<BrightPAS.Modules.FollowUp.FollowUpDbContext>().Contacts.AsNoTracking()
                        .Where(c => c.DueOn >= @from && c.DueOn <= to && c.DueOn <= today && c.Status != BrightPAS.Modules.FollowUp.ContactStatus.Cancelled).ToListAsync(ct);
                    var groups = await GroupsAsync(s, rows.Select(r => r.PatientId), ct);
                    return rows.Select(r => new IndicatorCase(r.DueOn, true, r.Status == BrightPAS.Modules.FollowUp.ContactStatus.Done ? 1 : 0, $"{r.DueOn:d MMM} {r.Label}", $"/followup/{r.Id}", groups.GetValueOrDefault(r.PatientId, "Not stated"))).ToList();
                }
            case "dosc":
                {
                    if (!Has(TheatreModule.Id)) return null;
                    var tdb = s.GetRequiredService<TheatreDbContext>();
                    var rows = await (from bk in tdb.Bookings.AsNoTracking()
                                      join se in tdb.Sessions.AsNoTracking() on bk.SessionId equals se.Id
                                      where se.Date >= @from && se.Date <= to && !se.IsCancelled
                                      select new { bk.Id, bk.PatientId, bk.Status, bk.StatusAt, bk.Procedure, se.Date }).ToListAsync(ct);
                    // Bookings cancelled before the day of surgery are not counted at all; cancelled on the day count in both.
                    rows = rows.Where(r => r.Status != BookingStatus.Cancelled || Day(r.StatusAt) >= r.Date).ToList();
                    var groups = await GroupsAsync(s, rows.Select(r => r.PatientId), ct);
                    return rows.Select(r => new IndicatorCase(r.Date, true, r.Status == BookingStatus.Cancelled ? 1 : 0, $"{r.Date:d MMM} {r.Procedure}", $"/theatre?date={r.Date:yyyy-MM-dd}", groups.GetValueOrDefault(r.PatientId, "Not stated"))).ToList();
                }
            case "transfer":
                {
                    if (!Has(EmrModule.Id)) return null;
                    var eps = await s.GetRequiredService<EmrDbContext>().Episodes.AsNoTracking()
                        .Where(e => (e.Stage == EpisodeStage.Discharged || e.Stage == EpisodeStage.Transferred) && e.DischargedAt >= a && e.DischargedAt < b).ToListAsync(ct);
                    eps = eps.Where(e => In(e.DischargedAt)).ToList();
                    var groups = await GroupsAsync(s, eps.Select(e => e.PatientId), ct);
                    return eps.Select(e => new IndicatorCase(Day(e.DischargedAt!.Value), true, e.Stage == EpisodeStage.Transferred || (e.DischargeDestination ?? "").Contains("hospital", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                        e.Stage == EpisodeStage.Transferred ? "Transferred" : "Discharged", $"/emr/{e.BookingId}", groups.GetValueOrDefault(e.PatientId, "Not stated"))).ToList();
                }
            case "ssc":
            case "count":
                {
                    if (!Has(EmrModule.Id)) return null;
                    var emr = s.GetRequiredService<EmrDbContext>();
                    var intra = await emr.Intraop.AsNoTracking().Where(i => i.InTheatreAt >= a && i.InTheatreAt < b).Select(i => new { i.EpisodeId, i.InTheatreAt }).ToListAsync(ct);
                    intra = intra.Where(i => In(i.InTheatreAt)).ToList();
                    var ids = intra.Select(i => i.EpisodeId).ToList();
                    var eps = await emr.Episodes.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
                    var groups = await GroupsAsync(s, eps.Values.Select(e => e.PatientId), ct);
                    if (key == "ssc")
                    {
                        var phases = (await emr.SafetyChecks.AsNoTracking().Where(c => ids.Contains(c.EpisodeId)).Select(c => new { c.EpisodeId, c.Phase }).ToListAsync(ct)).ToLookup(c => c.EpisodeId);
                        return intra.Where(i => eps.ContainsKey(i.EpisodeId)).Select(i => new IndicatorCase(Day(i.InTheatreAt!.Value), true, phases[i.EpisodeId].Select(p => p.Phase).Distinct().Count() == 3 ? 1 : 0,
                            "Theatre episode", $"/emr/{eps[i.EpisodeId].BookingId}", groups.GetValueOrDefault(eps[i.EpisodeId].PatientId, "Not stated"))).ToList();
                    }
                    var counts = (await emr.Counts.AsNoTracking().Where(c => ids.Contains(c.EpisodeId)).Select(c => new { c.EpisodeId, c.Expected, c.Counted }).ToListAsync(ct)).ToLookup(c => c.EpisodeId);
                    return intra.Where(i => eps.ContainsKey(i.EpisodeId) && counts[i.EpisodeId].Any()).Select(i => new IndicatorCase(Day(i.InTheatreAt!.Value), true, counts[i.EpisodeId].Any(c => c.Expected != c.Counted) ? 1 : 0,
                        "Theatre episode with counts", $"/emr/{eps[i.EpisodeId].BookingId}", groups.GetValueOrDefault(eps[i.EpisodeId].PatientId, "Not stated"))).ToList();
                }
            case "ews":
                {
                    if (!Has(EmrModule.Id)) return null;
                    var emr = s.GetRequiredService<EmrDbContext>();
                    var obs = await emr.Observations.AsNoTracking().Where(o => o.At >= a && o.At < b).Select(o => new { o.EpisodeId, o.At, o.Ews }).ToListAsync(ct);
                    obs = obs.Where(o => In(o.At)).ToList();
                    var byEp = obs.GroupBy(o => o.EpisodeId).ToList();
                    var ids = byEp.Select(g => g.Key).ToList();
                    var eps = await emr.Episodes.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
                    var groups = await GroupsAsync(s, eps.Values.Select(e => e.PatientId), ct);
                    return byEp.Where(g => eps.ContainsKey(g.Key)).Select(g => new IndicatorCase(Day(g.Min(o => o.At)), true, g.Any(o => o.Ews >= 5) ? 1 : 0,
                        $"Highest EWS {g.Max(o => o.Ews) ?? 0}", $"/emr/{eps[g.Key].BookingId}", groups.GetValueOrDefault(eps[g.Key].PatientId, "Not stated"))).ToList();
                }
            case "inc1000":
            case "fall1000":
                {
                    var q = s.GetRequiredService<QualityDbContext>();
                    var inc = await q.Incidents.AsNoTracking().Where(i => i.OccurredAt >= a && i.OccurredAt < b && (key == "inc1000" || i.Category == IncidentCategory.Fall)).ToListAsync(ct);
                    inc = inc.Where(i => In(i.OccurredAt)).ToList();
                    var cases = new List<IndicatorCase>();
                    var pids = inc.Where(i => i.PatientId is not null).Select(i => i.PatientId!.Value).ToList();
                    if (Has(EmrModule.Id))
                    {
                        var eps = await s.GetRequiredService<EmrDbContext>().Episodes.AsNoTracking().Where(e => e.AdmittedAt >= a && e.AdmittedAt < b).Select(e => new { e.PatientId, e.AdmittedAt, e.BookingId }).ToListAsync(ct);
                        eps = eps.Where(e => In(e.AdmittedAt)).ToList();
                        var g1 = await GroupsAsync(s, eps.Select(e => e.PatientId).Concat(pids), ct);
                        cases.AddRange(eps.Select(e => new IndicatorCase(Day(e.AdmittedAt!.Value), true, 0, "Episode", $"/emr/{e.BookingId}", g1.GetValueOrDefault(e.PatientId, "Not stated"))));
                        cases.AddRange(inc.Select(i => new IndicatorCase(Day(i.OccurredAt), false, 1, $"{i.Number} {i.Category}", $"/quality/incidents/{i.Id}", i.PatientId is { } p ? g1.GetValueOrDefault(p, "Not stated") : "Not stated")));
                        return cases;
                    }
                    return null;
                }
            case "sac12":
                {
                    var q = s.GetRequiredService<QualityDbContext>();
                    var inc = await q.Incidents.AsNoTracking().Where(i => i.OccurredAt >= a && i.OccurredAt < b && i.Sac != null && i.Sac <= 2).ToListAsync(ct);
                    var groups = await GroupsAsync(s, inc.Where(i => i.PatientId is not null).Select(i => i.PatientId!.Value), ct);
                    return inc.Where(i => In(i.OccurredAt)).Select(i => new IndicatorCase(Day(i.OccurredAt), false, 1, $"{i.Number} SAC {i.Sac}", $"/quality/incidents/{i.Id}",
                        i.PatientId is { } p ? groups.GetValueOrDefault(p, "Not stated") : "Not stated")).ToList();
                }
            case "cmpack":
                {
                    var q = s.GetRequiredService<QualityDbContext>();
                    var list = await q.Complaints.AsNoTracking().Where(c => c.ReceivedOn >= from && c.ReceivedOn <= to).ToListAsync(ct);
                    var groups = await GroupsAsync(s, list.Where(c => c.PatientId is not null).Select(c => c.PatientId!.Value), ct);
                    return list.Select(c => new IndicatorCase(c.ReceivedOn, true, c.AcknowledgedOn is { } ack && ack <= c.AcknowledgeBy ? 1 : 0, $"{c.Number} (due {c.AcknowledgeBy:d MMM})", $"/quality/complaints/{c.Id}",
                        c.PatientId is { } p ? groups.GetValueOrDefault(p, "Not stated") : "Not stated")).ToList();
                }
            case "cir":
            case "adr":
                {
                    if (!Has(EndoscopyModule.Id)) return null;
                    var rows = await s.GetRequiredService<EndoService>().ColonoscopyOutcomesAsync(from, to, ct);
                    var groups = await GroupsAsync(s, rows.Select(r => r.PatientId), ct);
                    return rows.Select(r => new IndicatorCase(r.On, true, (key == "cir" ? r.CaecumReached : r.Adenoma) ? 1 : 0, "Colonoscopy", $"/endoscopy/{r.BookingId}", groups.GetValueOrDefault(r.PatientId, "Not stated"))).ToList();
                }
            default: return null;
        }
    }

    /// <summary>Prioritised ethnicity group per patient (Statistics NZ / HISO 10001 codes): Māori, then Pacific, then Other; residual codes (9x) are Not stated.</summary>
    private static async Task<Dictionary<Guid, string>> GroupsAsync(IServiceProvider s, IEnumerable<Guid> patientIds, CancellationToken ct)
    {
        var ids = patientIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var codes = await s.GetRequiredService<PatientsDbContext>().Set<PatientEthnicity>().AsNoTracking().Where(e => ids.Contains(e.PatientId)).Select(e => new { e.PatientId, e.Code }).ToListAsync(ct);
        return codes.GroupBy(c => c.PatientId).ToDictionary(g => g.Key, g => IndicatorService.EquityGroup(g.Select(x => x.Code)));
    }
}
