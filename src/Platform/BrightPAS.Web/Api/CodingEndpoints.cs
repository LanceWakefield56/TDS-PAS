using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Data;
using BrightPAS.Identity;
using BrightPAS.Modules.Coding;
using BrightPAS.Modules.Patients;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Api;

/// <summary>Coding &amp; statutory reporting (0.20.0, ADR 0033): coding queue and workbench, code tables, advice, NMDS files, NBRS entries and batches.</summary>
public static class CodingEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;

    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
    }

    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record ReportableRequest(bool Reportable, string? Reason);
    public sealed record MessageRequest(string Message);
    public sealed record TextRequest(string Text);
    public sealed record PeriodRequest(DateOnly PeriodTo);
    public sealed record MarkRequest(SubmissionStatus Status, string? Notes);
    public sealed record DownloadRequest(ExportOptions? Export);
    public sealed record RegistrationRequest(string ProfessionalGroup, string RegistrationNumber);

    private static object Shape(CodingCheck c) => new { level = c.Level.ToString(), c.Rule, c.Message, c.Item };

    public static void MapCodingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/coding").RequireModule(CodingModule.Id);

        // ------------------------------------------------------------ queue and workbench
        g.MapGet("/summary", async (CodingService svc) => Results.Ok(await svc.SummaryAsync())).RequireAuthorization(Permissions.CodingAny);
        g.MapGet("/queue", async (string? status, CodingService svc) => Results.Ok(await svc.QueueAsync(status ?? "open"))).RequireAuthorization(Permissions.CodingAny);
        g.MapGet("/events/{id:guid}", (Guid id, CodingService svc) => Guard(async () =>
        {
            var w = await svc.WorkbenchAsync(id);
            var p = w.Patient;
            return Results.Ok(new
            {
                episode = new
                {
                    w.Episode.Id,
                    w.Episode.EpisodeId,
                    w.Episode.BookingId,
                    w.Episode.PatientId,
                    w.Episode.AdmissionDate,
                    w.Episode.DischargeDate,
                    w.Episode.PurchaserCode,
                    w.Episode.AdmissionSource,
                    w.Episode.EventEndType,
                    status = w.Episode.Status.ToString(),
                    w.Episode.NotReportableReason,
                    w.Episode.CodedBy,
                    w.Episode.CodedAt,
                    w.Episode.CoderNote,
                    w.Episode.HealthNzError,
                    sent = w.Episode.SubmissionId is not null,
                    w.Episode.Version,
                    items = w.Episode.Items.Select(i => new { i.DiagnosisType, code = CodingChecks.Display(i.Code), i.Description, i.Date, tableDescription = w.Descriptions.GetValueOrDefault(i.Code) }),
                },
                patient = new { p.Id, name = $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p.Nhi, p.Mrn, p.DateOfBirth, sex = p.Sex.ToString(), problems = NmdsService.PatientProblems(p) },
                w.Procedure,
                w.Anaesthetic,
                w.Surgeon,
                w.DischargeDestination,
                checks = w.Checks.Select(Shape),
                suggestions = w.Suggestions.Select(m => new
                {
                    m.Label,
                    codes = m.ProcedureCodes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(c => new { code = c, description = w.Descriptions.GetValueOrDefault(CodingChecks.Normalise(c)) }),
                    diagnosis = m.DiagnosisCode,
                    diagnosisDescription = m.DiagnosisCode is null ? null : w.Descriptions.GetValueOrDefault(CodingChecks.Normalise(m.DiagnosisCode)),
                }),
            });
        })).RequireAuthorization(Permissions.CodingAny);
        g.MapPut("/events/{id:guid}", (Guid id, CodingSave r, ClaimsPrincipal u, CodingService svc) => Guard(async () =>
        {
            var (e, checks, errors) = await svc.SaveAsync(id, r, Me(u));
            if (e is null) return Results.BadRequest(new { errors });
            return Results.Ok(new { status = e.Status.ToString(), e.Version, checks = checks.Select(Shape), errors });
        })).RequireAuthorization(Permissions.CodingCode);
        g.MapPost("/events/{id:guid}/reportable", (Guid id, ReportableRequest r, ClaimsPrincipal u, CodingService svc) => Guard(async () =>
            Err(await svc.SetReportableAsync(id, r.Reportable, r.Reason, Me(u))))).RequireAuthorization(Permissions.CodingCode);
        g.MapPost("/events/{id:guid}/health-nz-error", (Guid id, MessageRequest r, ClaimsPrincipal u, CodingService svc) => Guard(async () =>
            Err(await svc.RecordHealthNzErrorAsync(id, r.Message, Me(u))))).RequireAuthorization(Permissions.CodingCode);

        // ------------------------------------------------------------ code tables and advice
        g.MapGet("/codes", async (string? q, string? types, CodingService svc) =>
            Results.Ok((await svc.SearchCodesAsync(q ?? "", types)).Select(c => new { code = CodingChecks.Display(c.Code), c.Type, c.Description, c.Sex, c.NeverAssign }))).RequireAuthorization(Permissions.CodingAny);
        g.MapPost("/codes/import", (TextRequest r, ClaimsPrincipal u, CodingService svc) => Guard(async () =>
        {
            if (string.IsNullOrWhiteSpace(r.Text)) return Results.BadRequest(new { errors = new[] { "Choose the code table file." } });
            if (r.Text.Length > 30_000_000) return Results.BadRequest(new { errors = new[] { "The file is too large (30 MB at most)." } });
            return Results.Ok(await svc.ImportCodesAsync(r.Text, Me(u)));
        })).RequireAuthorization(Permissions.CodingManage);
        g.MapGet("/advice", async (CodingService svc) => Results.Ok(await svc.AdviceAsync())).RequireAuthorization(Permissions.CodingAny);
        g.MapPost("/advice", (AdviceEdit e, ClaimsPrincipal u, CodingService svc) => Guard(async () =>
        {
            var (row, errors) = await svc.SaveAdviceAsync(e, Me(u));
            return row is null ? Results.BadRequest(new { errors }) : Results.Ok(row);
        })).RequireAuthorization(Permissions.CodingManage);
        g.MapGet("/picklist", async (CodingService svc) => Results.Ok(await svc.ProcedureMapsAsync())).RequireAuthorization(Permissions.CodingAny);
        g.MapPost("/picklist", (ProcedureMapEdit e, ClaimsPrincipal u, CodingService svc) => Guard(async () =>
        {
            var (row, errors) = await svc.SaveProcedureMapAsync(e, Me(u));
            return row is null ? Results.BadRequest(new { errors }) : Results.Ok(row);
        })).RequireAuthorization(Permissions.CodingManage);
        g.MapGet("/clinicians", async (PasDbContext core, CodingDbContext db) =>
        {
            var regs = await db.Registrations.AsNoTracking().ToDictionaryAsync(r => r.ClinicianId);
            var list = await core.Clinicians.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, type = c.Type.ToString() }).ToListAsync();
            return Results.Ok(list.Select(c => new { c.Id, c.Name, c.type, group = regs.GetValueOrDefault(c.Id)?.ProfessionalGroup, number = regs.GetValueOrDefault(c.Id)?.RegistrationNumber }));
        }).RequireAuthorization(Permissions.CodingAny);
        g.MapPut("/clinicians/{id:guid}", (Guid id, RegistrationRequest r, ClaimsPrincipal u, CodingService svc) => Guard(async () =>
            Errs(await svc.SaveRegistrationAsync(id, r.ProfessionalGroup, r.RegistrationNumber, Me(u))))).RequireAuthorization(Permissions.CodingManage);

        // ------------------------------------------------------------ settings
        g.MapGet("/settings", async (CodingService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new
            {
                settings = s,
                purchasers = CodingChecks.PurchaserCodes,
                endTypes = CodingChecks.EventEndTypes,
                diagnosisTypes = CodingChecks.DiagnosisTypes,
                nbrsKinds = NbrsService.Kinds.ToDictionary(k => k.Key, k => k.Value.Label),
                exitCategories = NbrsService.ExitCategories,
            });
        }).RequireAuthorization(Permissions.CodingAny);
        g.MapPut("/settings", (CodingSettingsEdit e, ClaimsPrincipal u, CodingService svc) => Guard(async () => Errs(await svc.SaveSettingsAsync(e, Me(u))))).RequireAuthorization(Permissions.CodingManage);

        // ------------------------------------------------------------ NMDS
        g.MapGet("/nmds/preview", (DateOnly? to, NmdsService svc, TimeProvider clock) => Guard(async () =>
        {
            var p = await svc.PreviewAsync(to ?? BrightPAS.Core.NzTime.Today(clock));
            return Results.Ok(new { ready = p.Ready.Count, p.Problems, p.Resubmissions, p.FileName });
        })).RequireAuthorization(Permissions.CodingSubmit);
        g.MapGet("/nmds/files", async (NmdsService svc) => Results.Ok(await svc.FilesAsync())).RequireAuthorization(Permissions.CodingAny);
        g.MapPost("/nmds/files", (PeriodRequest r, ClaimsPrincipal u, NmdsService svc) => Guard(async () =>
        {
            var (f, errors) = await svc.CreateAsync(r.PeriodTo, Me(u));
            return f is null ? Results.BadRequest(new { errors }) : Results.Ok(new { f.Id, f.FileName, f.Records, f.IsResubmission });
        })).RequireAuthorization(Permissions.CodingSubmit);
        g.MapPost("/nmds/files/{id:guid}/mark", (Guid id, MarkRequest r, ClaimsPrincipal u, NmdsService svc) => Guard(async () =>
            Err(await svc.MarkAsync(id, r.Status, r.Notes, Me(u))))).RequireAuthorization(Permissions.CodingSubmit);
        g.MapPost("/nmds/files/{id:guid}/download", (Guid id, DownloadRequest r, ClaimsPrincipal u, NmdsService svc, SecureExportService export) => Guard(async () =>
        {
            var f = await svc.FileAsync(id, Me(u));
            return await export.ExportAsync(r.Export, Path.GetFileNameWithoutExtension(f.FileName), [new(f.FileName, f.Content)], Me(u), "NmdsFile");
        })).RequireAuthorization(Permissions.CodingSubmit);

        // ------------------------------------------------------------ NBRS
        g.MapGet("/nbrs/entries", async (NbrsService svc, PatientsDbContext patients) =>
        {
            var rows = await svc.EntriesAsync();
            var ids = rows.Select(r => r.PatientId).Distinct().ToList();
            var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}{(p.Nhi is null ? "" : $" ({p.Nhi})")}");
            return Results.Ok(rows.Select(n => new
            {
                n.Id,
                n.LocalId,
                n.PatientId,
                patient = names.GetValueOrDefault(n.PatientId),
                n.WaitlistEntryId,
                n.BookingSource,
                n.ReferralDate,
                n.FirstAssessmentDate,
                n.CpacDate,
                n.CpacScore,
                n.CpacSystem,
                n.PurchaserCode,
                n.HealthSpecialty,
                clinicalCode = CodingChecks.Display(n.ClinicalCode),
                n.ClinicalCodeType,
                n.DomicileCode,
                n.ProfessionalGroup,
                n.ResponsibilityCode,
                n.AssessorCode,
                n.AssessorGroup,
                n.StagedFlag,
                events = n.Events.OrderBy(e => e.CreatedAt).Select(e => new { e.Id, e.Action, e.StatusCode, e.StatusDate, e.BookingMadeDate, e.BookedForDate, e.ExitDate, e.ExitCategory, e.ClientSystemId, result = e.Result.ToString(), e.Messages }),
            }));
        }).RequireAuthorization(Permissions.CodingAny);
        g.MapGet("/nbrs/waitlist", async (NbrsService svc, PatientsDbContext patients) =>
        {
            var rows = await svc.UnreportedWaitlistAsync();
            var ids = rows.Select(r => r.PatientId).Distinct().ToList();
            var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}");
            return Results.Ok(rows.Select(r => new { r.Id, patient = names.GetValueOrDefault(r.PatientId), r.Procedure, r.AddedOn, r.Status }));
        }).RequireAuthorization(Permissions.CodingAny);
        g.MapGet("/nbrs/draft/{waitlistId:guid}", (Guid waitlistId, NbrsService svc) => Guard(async () => Results.Ok(await svc.DraftFromWaitlistAsync(waitlistId)))).RequireAuthorization(Permissions.CodingCode);
        g.MapPost("/nbrs/entries", (NbrsEntryEdit e, ClaimsPrincipal u, NbrsService svc) => Guard(async () =>
        {
            var (row, errors) = await svc.SaveEntryAsync(e, Me(u));
            return row is null ? Results.BadRequest(new { errors }) : Results.Ok(new { row.Id, row.LocalId });
        })).RequireAuthorization(Permissions.CodingCode);
        g.MapGet("/nbrs/entries/{id:guid}/defaults", (Guid id, NbrsService svc) => Guard(async () =>
        {
            var (made, bookedFor) = await svc.BookingDatesAsync(id);
            return Results.Ok(new { bookingMadeDate = made, bookedForDate = bookedFor, clientSystemId = await svc.ClientSystemIdAsync(id) });
        })).RequireAuthorization(Permissions.CodingCode);
        g.MapPost("/nbrs/entries/{id:guid}/events", (Guid id, NbrsEventEdit e, ClaimsPrincipal u, NbrsService svc) => Guard(async () =>
        {
            var (row, errors) = await svc.AddEventAsync(id, e, Me(u));
            return row is null ? Results.BadRequest(new { errors }) : Results.Ok(new { row.Id });
        })).RequireAuthorization(Permissions.CodingCode);
        g.MapDelete("/nbrs/events/{id:guid}", (Guid id, ClaimsPrincipal u, NbrsService svc) => Guard(async () => Err(await svc.RemovePendingAsync(id, Me(u))))).RequireAuthorization(Permissions.CodingCode);
        g.MapGet("/nbrs/batches", async (NbrsService svc) => Results.Ok(await svc.BatchesAsync())).RequireAuthorization(Permissions.CodingAny);
        g.MapPost("/nbrs/batches", (ClaimsPrincipal u, NbrsService svc) => Guard(async () =>
        {
            var (b, errors) = await svc.CreateBatchAsync(Me(u));
            return b is null ? Results.BadRequest(new { errors }) : Results.Ok(new { b.Id, b.FileName, b.Records, b.Environment });
        })).RequireAuthorization(Permissions.CodingSubmit);
        g.MapPost("/nbrs/batches/{id:guid}/download", (Guid id, DownloadRequest r, ClaimsPrincipal u, NbrsService svc, SecureExportService export) => Guard(async () =>
        {
            var b = await svc.BatchAsync(id, Me(u));
            return await export.ExportAsync(r.Export, Path.GetFileNameWithoutExtension(b.FileName), [new(b.FileName, b.Content)], Me(u), "NbrsBatch");
        })).RequireAuthorization(Permissions.CodingSubmit);
        g.MapPost("/nbrs/acknowledgement", (TextRequest r, ClaimsPrincipal u, NbrsService svc) => Guard(async () =>
        {
            if (string.IsNullOrWhiteSpace(r.Text) || r.Text.Length > 5_000_000) return Results.BadRequest(new { errors = new[] { "Choose the acknowledgement (.NBK) file (5 MB at most)." } });
            var (ack, errors) = await svc.ImportAckAsync(r.Text, Me(u));
            return ack is null ? Results.BadRequest(new { errors }) : Results.Ok(new { ack.FileName, ack.Accepted, ack.Rejected, ack.Warnings });
        })).RequireAuthorization(Permissions.CodingSubmit);
    }
}
