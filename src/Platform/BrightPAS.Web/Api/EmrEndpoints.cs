using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Identity;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Clinical record (eMR, 0.14.0): episode per theatre booking, checklist, intra-op, counts, observations, medicines, controlled drugs, notes, discharge.</summary>
public static class EmrEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record AdmitRequest(bool SedationOrAnaesthesia);
    public sealed record StageRequest(EpisodeStage Stage);
    public sealed record ResolveRequest(string Note);
    public sealed record SpecimenStep(SpecimenStatus To, string? Courier);
    public sealed record DraftNote(string Text, bool Sign);
    public sealed record NewDrug(string Name, string Strength, string Form);

    /// <summary>Wraps service calls that throw for a closed or missing episode into a plain-English 400/404.</summary>
    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
    }

    public static void MapEmrEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/emr").RequireModule(EmrModule.Id);

        // Today's patients (theatre and procedure lists) with where each episode is up to.
        g.MapGet("/today", async (DateOnly? date, TheatreService theatre, EmrDbContext db, TimeProvider clock) =>
        {
            var d = date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var sessions = (await theatre.SessionsAsync(d, d)).Where(s => !s.IsCancelled).ToList();
            var ids = sessions.SelectMany(s => s.Bookings).Select(b => b.Id).ToList();
            var eps = await db.Episodes.AsNoTracking().Where(e => ids.Contains(e.BookingId)).ToDictionaryAsync(e => e.BookingId);
            var epIds = eps.Values.Select(e => e.Id).ToList();
            var lastObs = await db.Observations.AsNoTracking().Where(o => epIds.Contains(o.EpisodeId) && o.Ews != null).GroupBy(o => o.EpisodeId)
                .Select(g2 => new { EpisodeId = g2.Key, Ews = g2.OrderByDescending(o => o.At).First().Ews }).ToDictionaryAsync(x => x.EpisodeId, x => x.Ews);
            var checks = await db.SafetyChecks.AsNoTracking().Where(c => epIds.Contains(c.EpisodeId)).Select(c => new { c.EpisodeId, c.Phase }).ToListAsync();
            return Results.Ok(sessions.SelectMany(s => s.Bookings.Where(b => b.Status != "Cancelled" && b.Status != "DidNotAttend").Select(b =>
            {
                var e = eps.GetValueOrDefault(b.Id);
                return new
                {
                    bookingId = b.Id,
                    b.PatientId,
                    b.PatientName,
                    b.Nhi,
                    b.Mrn,
                    b.Procedure,
                    s.LocationName,
                    start = b.PlannedStart,
                    s.Surgeon,
                    s.Anaesthetist,
                    bookingStatus = b.Status,
                    episodeId = e?.Id,
                    stage = e?.Stage.ToString() ?? "NotStarted",
                    ews = e is null ? null : lastObs.GetValueOrDefault(e.Id),
                    checks = e is null ? [] : checks.Where(c => c.EpisodeId == e.Id).Select(c => c.Phase.ToString()).ToList(),
                };
            })).OrderBy(x => x.start));
        }).RequireAuthorization(Permissions.EmrView);

        // The whole record for one booking (episode created when a clinician first opens it to write).
        g.MapGet("/bookings/{bookingId:guid}", async (Guid bookingId, ClaimsPrincipal u, IServiceProvider sp) =>
        {
            var theatre = sp.GetRequiredService<TheatreDbContext>();
            var booking = await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId);
            if (booking is null) return Results.NotFound();
            var db = sp.GetRequiredService<EmrDbContext>();
            var e = await db.Episodes.AsNoTracking().SingleOrDefaultAsync(x => x.BookingId == bookingId);
            if (e is null && u.HasClaim(Permissions.ClaimType, Permissions.EmrWrite)) e = await sp.GetRequiredService<EmrService>().EpisodeAsync(bookingId, booking.PatientId, Me(u));
            await sp.GetRequiredService<BrightPAS.Core.Audit.AuditWriter>().WriteAsync(Me(u), "View", "Episode", e?.Id.ToString() ?? bookingId.ToString(), booking.PatientId.ToString());
            return Results.Ok(await RecordAsync(sp, booking, e));
        }).RequireAuthorization(Permissions.EmrView);

        g.MapPost("/episodes/{id:guid}/admit", (Guid id, AdmitRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () => Done(await svc.AdmitAsync(id, r.SedationOrAnaesthesia, Me(u)))))
            .RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/stage", (Guid id, StageRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () => Done(await svc.SetStageAsync(id, r.Stage, Me(u)))))
            .RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/assessments", (Guid id, AssessmentRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (row, errors) = await svc.AssessAsync(id, r, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Score, row.Risk, row.Actions });
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/assessments/score", (AssessmentRequest r) => Results.Ok(EmrService.Score(r.Kind, r.Answers ?? new Dictionary<string, string>()))).RequireAuthorization(Permissions.EmrView);
        g.MapPost("/episodes/{id:guid}/checks", (Guid id, CheckRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (row, errors) = await svc.CompleteCheckAsync(id, r, Me(u));
            return row is null ? Errors(errors) : Results.Ok();
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPut("/episodes/{id:guid}/intraop", (Guid id, IntraopEdit r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (errors, warnings) = await svc.SaveIntraopAsync(id, r, Me(u));
            return errors.Count > 0 ? Errors(errors) : Results.Ok(new { warnings });
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/counts", (Guid id, CountRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (row, error, warning) = await svc.CountAsync(id, r, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { row!.Id, warning });
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/counts/{id:guid}/resolve", (Guid id, ResolveRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () => Done(await svc.ResolveCountAsync(id, r.Note, Me(u)))))
            .RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/specimens", (Guid id, SpecimenRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (row, error) = await svc.AddSpecimenAsync(id, r, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { row!.Id, row.Number });
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/specimens/{id:guid}/step", async (Guid id, SpecimenStep r, ClaimsPrincipal u, EmrService svc) => Done(await svc.SpecimenStepAsync(id, r.To, r.Courier, Me(u))))
            .RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/observations", (Guid id, ObservationRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (row, errors) = await svc.ObserveAsync(id, r with { Source = "Manual", DeviceId = null }, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Ews, row.Escalation });
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/medications", (Guid id, MedicationRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (row, error) = await svc.GiveAsync(id, r, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { row!.Id });
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/fluids", (Guid id, FluidRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () => Done(await svc.FluidAsync(id, r, Me(u)))))
            .RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/blood", (Guid id, BloodRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () =>
        {
            var (row, error) = await svc.AddBloodAsync(id, r, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { row!.Id });
        })).RequireAuthorization(Permissions.EmrWrite);
        g.MapPut("/blood/{id:guid}", async (Guid id, BloodUpdate r, ClaimsPrincipal u, EmrService svc) => Done(await svc.UpdateBloodAsync(id, r, Me(u)))).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/notes", async (Guid id, NoteRequest r, ClaimsPrincipal u, EmrService svc) =>
        {
            var (row, error) = await svc.NoteAsync(id, r, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { row!.Id });
        }).RequireAuthorization(Permissions.EmrWrite);
        g.MapPut("/notes/{id:guid}", async (Guid id, DraftNote r, ClaimsPrincipal u, EmrService svc) => Done(await svc.UpdateDraftNoteAsync(id, r.Text, r.Sign, Me(u)))).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/episodes/{id:guid}/discharge", (Guid id, DischargeRequest r, ClaimsPrincipal u, EmrService svc) => Guard(async () => Errors(await svc.DischargeAsync(id, r, Me(u)))))
            .RequireAuthorization(Permissions.EmrWrite);

        // Controlled drug register.
        g.MapGet("/controlled-drugs", async (EmrService svc) => Results.Ok(await svc.ControlledDrugsAsync())).RequireAuthorization(Permissions.EmrWrite);
        g.MapPost("/controlled-drugs", async (NewDrug r, ClaimsPrincipal u, EmrService svc) =>
        {
            var (d, error) = await svc.AddControlledDrugAsync(r.Name, r.Strength, r.Form, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { d!.Id });
        }).RequireAuthorization(Permissions.EmrControlledDrugs);
        g.MapPost("/controlled-drugs/register", async (CdRequest r, ClaimsPrincipal u, EmrService svc) => Done(await svc.RegisterAsync(r, Me(u)))).RequireAuthorization(Permissions.EmrControlledDrugs);
        g.MapGet("/controlled-drugs/{id:guid}/register", async (Guid id, EmrService svc, PatientsDbContext patients) =>
        {
            var rows = await svc.RegisterEntriesAsync(id);
            var ids = rows.Where(r => r.PatientId != null).Select(r => r.PatientId!.Value).Distinct().ToList();
            var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames} ({p.Nhi ?? p.Mrn})");
            return Results.Ok(rows.Select(r => new { r.Id, movement = r.Movement.ToString(), r.Quantity, r.BalanceAfter, patient = r.PatientId is { } p ? names.GetValueOrDefault(p) : null, r.Reference, r.By, r.WitnessedBy, r.At }));
        }).RequireAuthorization(Permissions.EmrControlledDrugs);
    }

    private static async Task<object> RecordAsync(IServiceProvider sp, TheatreBooking booking, Episode? e)
    {
        var theatre = sp.GetRequiredService<TheatreDbContext>();
        var session = await theatre.Sessions.AsNoTracking().SingleAsync(s => s.Id == booking.SessionId);
        var core = sp.GetRequiredService<BrightPAS.Core.Data.PasDbContext>();
        var clinicians = await core.Clinicians.AsNoTracking().Where(c => c.Id == session.SurgeonId || c.Id == session.AnaesthetistId).ToDictionaryAsync(c => c.Id, c => c.Name);
        var patients = sp.GetRequiredService<PatientsDbContext>();
        var p = await patients.Patients.AsNoTracking().Include(x => x.Alerts).SingleAsync(x => x.Id == booking.PatientId);
        var svc = sp.GetRequiredService<EmrService>();
        var header = new
        {
            bookingId = booking.Id,
            patientId = p.Id,
            name = $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}",
            p.Nhi,
            p.Mrn,
            p.DateOfBirth,
            sex = p.Sex.ToString(),
            alerts = p.Alerts.Where(a => a.IsActive).Select(a => new { type = a.Type.ToString(), severity = a.Severity.ToString(), a.Description }),
            booking.Procedure,
            laterality = booking.Laterality.ToString(),
            anaesthetic = booking.Anaesthetic.ToString(),
            date = session.Date,
            surgeon = clinicians.GetValueOrDefault(session.SurgeonId),
            anaesthetist = session.AnaesthetistId is { } aid ? clinicians.GetValueOrDefault(aid) : null,
        };
        var checklist = EmrService.CheckItems.ToDictionary(k => k.Key.ToString(), k => new { items = k.Value.Select(i => new { key = i.Key, text = i.Text }), roles = EmrService.CheckRoles[k.Key] });
        if (e is null) return new { header, episode = (object?)null, checklist };
        var db = sp.GetRequiredService<EmrDbContext>();
        var id = e.Id;
        var assessments = await db.Assessments.AsNoTracking().Include(a => a.Answers).Where(a => a.EpisodeId == id).OrderByDescending(a => a.RecordedAt).ToListAsync();
        var checks = await db.SafetyChecks.AsNoTracking().Include(c => c.Team).Where(c => c.EpisodeId == id).ToListAsync();
        var fluids = await db.Fluids.AsNoTracking().Where(f => f.EpisodeId == id).OrderBy(f => f.At).ToListAsync();
        var drugs = await db.ControlledDrugs.AsNoTracking().Where(d => d.IsActive).ToDictionaryAsync(d => d.Id);
        return new
        {
            header,
            checklist,
            episode = new { e.Id, stage = e.Stage.ToString(), e.AdmittedAt, e.AdmittedBy, e.SedationOrAnaesthesia, e.EscortName, e.EscortPhone, e.OvernightCarer, e.EscortConfirmed, e.EscortOverrideReason, e.DischargedAt, e.DischargedBy, e.DischargeDestination, e.FollowUpCallOn },
            assessments = assessments.Select(a => new { a.Id, kind = a.Kind.ToString(), a.Score, a.Risk, a.Actions, a.RecordedBy, a.RecordedAt, answers = a.Answers.ToDictionary(x => x.Key, x => x.Value) }),
            checks = checks.Select(c => new { phase = c.Phase.ToString(), c.CompletedAt, c.LedBy, team = c.Team.Select(t => new { t.Role, t.Name }), c.Notes }),
            intraop = await db.Intraop.AsNoTracking().SingleOrDefaultAsync(x => x.EpisodeId == id),
            counts = (await db.Counts.AsNoTracking().Where(c => c.EpisodeId == id).OrderBy(c => c.At).ToListAsync()).Select(c => new { c.Id, stage = c.Stage.ToString(), c.Item, c.Expected, c.Counted, c.CountedBy, c.CheckedBy, c.At, c.Resolved, c.ResolutionNote }),
            countProblems = await svc.CountProblemsAsync(id),
            specimens = (await db.Specimens.AsNoTracking().Where(s => s.EpisodeId == id).OrderBy(s => s.Number).ToListAsync()).Select(s => new { s.Id, s.Number, s.Site, s.Test, s.Container, s.CollectedAt, s.CollectedBy, status = s.Status.ToString(), s.SentAt, s.SentBy, s.Courier, s.ReceivedAt }),
            observations = await db.Observations.AsNoTracking().Where(o => o.EpisodeId == id).OrderByDescending(o => o.At).Take(200).ToListAsync(),
            medications = (await db.Medications.AsNoTracking().Where(m => m.EpisodeId == id).OrderByDescending(m => m.GivenAt).ToListAsync())
                .Select(m => new { m.Id, m.Medicine, m.Dose, m.Route, m.GivenAt, m.GivenBy, m.WitnessedBy, m.Barcode, m.Notes, m.Source, controlled = m.ControlledDrugId is { } c && drugs.ContainsKey(c) }),
            fluids = new { entries = fluids, totalIn = fluids.Where(f => f.IsInput).Sum(f => f.Millilitres), totalOut = fluids.Where(f => !f.IsInput).Sum(f => f.Millilitres) },
            blood = await db.BloodUnits.AsNoTracking().Where(b => b.EpisodeId == id).OrderBy(b => b.ReceivedAt).ToListAsync(),
            notes = (await db.Notes.AsNoTracking().Where(n => n.EpisodeId == id).OrderByDescending(n => n.At).ToListAsync()).Select(n => new { n.Id, kind = n.Kind.ToString(), n.Text, n.Author, n.At, n.Signed, n.AmendsNoteId }),
            templates = (await svc.TemplatesAsync()).Select(t => new { t.Id, t.Name, kind = t.Kind.ToString(), t.Body }),
            controlledDrugs = drugs.Values.OrderBy(d => d.Name).Select(d => new { d.Id, label = $"{d.Name} {d.Strength} {d.Form}", d.Balance }),
        };
    }
}
