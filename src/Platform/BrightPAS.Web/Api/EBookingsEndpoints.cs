using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Data;
using BrightPAS.Identity;
using BrightPAS.Modules.EBookings;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Portal;
using BrightPAS.Modules.Referrals;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Bookings from specialists' rooms (0.28.0, ADR 0041): the rooms send requests through the portal; the booking office reviews them onto the waitlist.</summary>
public static class EBookingsEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record TextRequest(string? Text);
    public sealed record ReasonRequest(string? Reason);
    public sealed record MatchRequest(Guid PatientId);
    public sealed record RegisterRequest(bool ConfirmNotDuplicate);
    public sealed record SettingsEdit(string? WatchFolder, bool EmailRooms, int MaxFileMegabytes, string? Instructions);

    /// <summary>Base64 attachments make a request up to about 110 MB (8 files of 10 MB).</summary>
    private const long MaxRequestBytes = 120L * 1024 * 1024;

    private static object SubmitResult(EBookingResult r) => r.Request is null
        ? new { errors = r.Errors, warnings = r.Warnings, needsConfirm = r.NeedsConfirm }
        : new { id = r.Request.Id, number = r.Request.Number, warnings = r.Warnings };

    private static object View(BookingRequest r, string specialist, WaitlistEntry? w, IReadOnlyList<EBookingMatch>? matches, Patient? patient, bool forTds) => new
    {
        r.Id,
        r.Number,
        status = r.Status.ToString(),
        source = r.Source.ToString(),
        r.ExternalRef,
        r.SpecialistId,
        specialist,
        r.RoomsName,
        r.ContactName,
        r.ContactPhone,
        r.ContactEmail,
        r.Nhi,
        r.FamilyName,
        r.GivenNames,
        r.DateOfBirth,
        sex = r.Sex.ToString(),
        r.MobilePhone,
        r.Email,
        r.AddressLine1,
        r.Suburb,
        r.City,
        r.Postcode,
        r.InterpreterLanguage,
        service = r.Service.ToString(),
        r.ProcedureItemId,
        r.Procedure,
        laterality = r.Laterality.ToString(),
        anaesthetic = r.Anaesthetic.ToString(),
        r.DurationMinutes,
        priority = r.Priority.ToString(),
        funder = r.Funder.ToString(),
        r.FunderReference,
        r.InsurerName,
        r.EarliestDate,
        r.LatestDate,
        r.Availability,
        r.ClinicalNotes,
        r.WeightKg,
        r.HeightCm,
        bmi = EBookingService.Bmi(r),
        r.Asa,
        r.Diabetic,
        r.SleepApnoea,
        r.Anticoagulated,
        r.AnticoagulantDetails,
        r.Allergies,
        r.Comorbidities,
        r.ResponsibleAdultArranged,
        r.ConsentSigned,
        r.ConsentSignedOn,
        r.SubmittedAt,
        r.SubmittedBy,
        r.StatusAt,
        r.DecidedBy,
        r.DeclineReason,
        flags = EBookingService.Flags(r),
        files = r.Files.OrderBy(f => f.AddedAt).Select(f => new { f.Id, category = f.Category.ToString(), f.FileName, f.ContentType, f.SizeBytes, f.AddedAt, f.AddedBy, filed = f.PatientDocumentId != null }),
        messages = r.Messages.Select(m => new { m.Id, m.FromTds, author = forTds || !m.FromTds ? m.Author : "TDS booking office", m.Text, m.At }),
        patient = forTds && patient is not null ? new { patient.Id, patient.Mrn, patient.Nhi, patient.FamilyName, patient.GivenNames, patient.DateOfBirth } : null,
        patientMatched = r.PatientId != null,
        matches = forTds ? matches : null,
        waitlist = w is null ? null : new { w.Id, status = w.Status.ToString(), priority = w.Priority.ToString(), w.TargetDate, w.BookedFor, w.BookingId },
        r.ReferralId,
    };

    private static async Task<IResult> DetailAsync(Guid id, IReadOnlyCollection<Guid>? mine, bool forTds, EBookingService svc, PasDbContext core, PatientsDbContext patients)
    {
        var r = await svc.GetAsync(id, mine);
        if (r is null) return Results.NotFound(new { errors = new[] { "Booking request not found." } });
        var name = await core.Clinicians.AsNoTracking().Where(c => c.Id == r.SpecialistId).Select(c => c.Name).SingleOrDefaultAsync() ?? "?";
        var w = await svc.WaitlistEntryAsync(r);
        var patient = r.PatientId is { } pid ? await patients.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid) : null;
        var matches = forTds && r.PatientId is null ? await svc.MatchesAsync(r) : null;
        return Results.Ok(View(r, name, w, matches, patient, forTds));
    }

    private static async Task<(byte[]? Bytes, string? Error)> ReadBodyAsync(HttpRequest req, int max)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await req.Body.ReadAsync(buf)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) return (null, $"The file is larger than {max / 1024 / 1024} MB.");
        }
        return ms.Length == 0 ? (null, "The file is empty.") : (ms.ToArray(), null);
    }

    public static void MapEBookingsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ebookings").RequireModule(EBookingsModule.Id);
        var size = new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxRequestBytes);

        // ================================================================ rooms (specialist / PA, portal)
        g.MapGet("/rooms", async (string? view, ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc) =>
        {
            var mine = await PortalEndpoints.MyClinicianIdsAsync(u, core, portal);
            return Results.Ok(await svc.ListAsync(view ?? "all", mine));
        }).RequireAuthorization(Permissions.EBookingsSend);
        g.MapGet("/rooms/options", async (ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc) =>
        {
            var mine = await PortalEndpoints.MyClinicianIdsAsync(u, core, portal);
            var specialists = await core.Clinicians.AsNoTracking().Where(c => mine.Contains(c.Id) && c.IsActive).OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, c.PracticeName, c.Phone, c.Email }).ToListAsync();
            var procedures = await core.Procedures.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Code, p.Name, p.Service, p.DefaultMinutes, p.DefaultAnaesthetic }).ToListAsync();
            var s = await svc.SettingsAsync();
            return Results.Ok(new { specialists, procedures, instructions = s.Instructions, maxFileMegabytes = s.MaxFileMegabytes, maxFiles = EBookingService.MaxFiles });
        }).RequireAuthorization(Permissions.EBookingsSend);
        g.MapPost("/rooms", async (EBookingInput i, ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc) =>
        {
            var mine = await PortalEndpoints.MyClinicianIdsAsync(u, core, portal);
            var r = await svc.SubmitAsync(i, EBookingSource.Portal, Me(u), mine);
            return r.Request is null ? Results.BadRequest(SubmitResult(r)) : Results.Ok(SubmitResult(r));
        }).RequireAuthorization(Permissions.EBookingsSend).WithMetadata(size);
        g.MapGet("/rooms/{id:guid}", async (Guid id, ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc, PatientsDbContext patients) =>
            await DetailAsync(id, await PortalEndpoints.MyClinicianIdsAsync(u, core, portal), false, svc, core, patients)).RequireAuthorization(Permissions.EBookingsSend);
        g.MapPost("/rooms/{id:guid}/messages", async (Guid id, TextRequest t, ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc) =>
            Err(await svc.AddMessageAsync(id, t.Text ?? "", false, Me(u), await PortalEndpoints.MyClinicianIdsAsync(u, core, portal)))).RequireAuthorization(Permissions.EBookingsSend);
        g.MapPost("/rooms/{id:guid}/withdraw", async (Guid id, ReasonRequest r, ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc) =>
            Err(await svc.WithdrawAsync(id, r.Reason ?? "", Me(u), await PortalEndpoints.MyClinicianIdsAsync(u, core, portal)))).RequireAuthorization(Permissions.EBookingsSend);
        g.MapPost("/rooms/{id:guid}/files", async (Guid id, EBookingFileInput f, ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc) =>
            Err(await svc.AddFileAsync(id, f, Me(u), await PortalEndpoints.MyClinicianIdsAsync(u, core, portal)))).RequireAuthorization(Permissions.EBookingsSend).WithMetadata(size);
        g.MapGet("/rooms/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, ClaimsPrincipal u, PasDbContext core, PortalService portal, EBookingService svc) =>
            await svc.OpenFileAsync(id, fileId, Me(u), await PortalEndpoints.MyClinicianIdsAsync(u, core, portal)) is { } f ? Results.File(f.Bytes, f.Meta.ContentType, f.Meta.FileName) : Results.NotFound())
            .RequireAuthorization(Permissions.EBookingsSend);

        // ================================================================ booking office
        g.MapGet("", async (string? view, EBookingService svc) => Results.Ok(await svc.ListAsync(view ?? "open", null))).RequireAuthorization(Permissions.EBookingsManage);
        g.MapGet("/count", async (EBookingService svc) => Results.Ok(new { waiting = await svc.WaitingCountAsync() })).RequireAuthorization(Permissions.EBookingsManage);
        g.MapGet("/options", async (PasDbContext core, EBookingService svc) =>
        {
            var specialists = await core.Clinicians.AsNoTracking().Where(c => c.IsActive && c.Type != BrightPAS.Core.Organisation.ClinicianType.Anaesthetist).OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, c.PracticeName, c.Phone, c.Email }).ToListAsync();
            var procedures = await core.Procedures.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Code, p.Name, p.Service, p.DefaultMinutes, p.DefaultAnaesthetic }).ToListAsync();
            var s = await svc.SettingsAsync();
            return Results.Ok(new { specialists, procedures, instructions = s.Instructions, maxFileMegabytes = s.MaxFileMegabytes, maxFiles = EBookingService.MaxFiles });
        }).RequireAuthorization(Permissions.EBookingsManage);
        g.MapPost("", async (EBookingInput i, ClaimsPrincipal u, EBookingService svc) =>
        {
            var r = await svc.SubmitAsync(i, EBookingSource.Portal, Me(u), null);
            return r.Request is null ? Results.BadRequest(SubmitResult(r)) : Results.Ok(SubmitResult(r));
        }).RequireAuthorization(Permissions.EBookingsManage).WithMetadata(size);
        g.MapGet("/{id:guid}", async (Guid id, EBookingService svc, PasDbContext core, PatientsDbContext patients) => await DetailAsync(id, null, true, svc, core, patients))
            .RequireAuthorization(Permissions.EBookingsManage);
        g.MapPost("/{id:guid}/messages", async (Guid id, TextRequest t, ClaimsPrincipal u, EBookingService svc) => Err(await svc.AddMessageAsync(id, t.Text ?? "", true, Me(u), null)))
            .RequireAuthorization(Permissions.EBookingsManage);
        g.MapPost("/{id:guid}/decline", async (Guid id, ReasonRequest r, ClaimsPrincipal u, EBookingService svc) => Err(await svc.DeclineAsync(id, r.Reason ?? "", Me(u))))
            .RequireAuthorization(Permissions.EBookingsManage);
        g.MapPost("/{id:guid}/match", async (Guid id, MatchRequest m, ClaimsPrincipal u, EBookingService svc) => Err(await svc.MatchPatientAsync(id, m.PatientId, Me(u))))
            .RequireAuthorization(Permissions.EBookingsManage);
        g.MapPost("/{id:guid}/register", async (Guid id, RegisterRequest m, ClaimsPrincipal u, EBookingService svc) =>
        {
            var r = await svc.RegisterPatientAsync(id, m.ConfirmNotDuplicate, Me(u));
            return r.Patient is not null ? Results.Ok(new { r.Patient.Id, r.Patient.Mrn })
                : Results.BadRequest(new { errors = r.Errors, duplicates = r.PossibleDuplicates });
        }).RequireAuthorization(Permissions.EBookingsManage).RequireAuthorization(Permissions.PatientRegister);
        g.MapPost("/{id:guid}/accept", async (Guid id, EBookingAccept a, ClaimsPrincipal u, EBookingService svc) =>
        {
            var (r, errors) = await svc.AcceptAsync(id, a, Me(u));
            return r is null ? Errs(errors) : Results.Ok(new { r.WaitlistEntryId, r.ReferralId });
        }).RequireAuthorization(Permissions.EBookingsManage);
        g.MapPost("/{id:guid}/files", async (Guid id, EBookingFileInput f, ClaimsPrincipal u, EBookingService svc) => Err(await svc.AddFileAsync(id, f, Me(u), null)))
            .RequireAuthorization(Permissions.EBookingsManage).WithMetadata(size);
        g.MapGet("/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, ClaimsPrincipal u, EBookingService svc) =>
            await svc.OpenFileAsync(id, fileId, Me(u), null) is { } f ? Results.File(f.Bytes, f.Meta.ContentType, f.Meta.FileName) : Results.NotFound())
            .RequireAuthorization(Permissions.EBookingsManage);

        // Files from practice systems (BrightPAS JSON or HL7 REF^I12): upload here, or drop them into the watched folder.
        g.MapPost("/import", async (HttpRequest req, ClaimsPrincipal u, EBookingImporter importer) =>
        {
            var (bytes, error) = await ReadBodyAsync(req, EBookingImporter.MaxBytes);
            if (bytes is null) return Err(error);
            var r = await importer.ImportAsync(Uri.UnescapeDataString(req.Headers["X-File-Name"].ToString()), bytes, Me(u));
            return r.Received == 0 && r.Problems.Count > 0 ? Results.BadRequest(new { errors = r.Problems, r.Received, r.Numbers }) : Results.Ok(r);
        }).RequireAuthorization(Permissions.EBookingsManage).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(EBookingImporter.MaxBytes + 1024));
        g.MapGet("/example.json", (TimeProvider clock) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(EBookingImporter.ExampleJson(BrightPAS.Core.NzTime.Today(clock))), "application/json", "brightpas-booking-example.json"))
            .RequireAuthorization(Permissions.EBookingsManage);
        g.MapGet("/settings", async (EBookingService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { s.WatchFolder, s.EmailRooms, s.MaxFileMegabytes, s.Instructions, s.LastCheckedAt, s.LastProblem, s.UpdatedAt, s.UpdatedBy });
        }).RequireAuthorization(Permissions.EBookingsManage);
        g.MapPut("/settings", async (SettingsEdit e, ClaimsPrincipal u, EBookingService svc) => Errs(await svc.UpdateSettingsAsync(e.WatchFolder, e.EmailRooms, e.MaxFileMegabytes, e.Instructions, Me(u))))
            .RequireAuthorization(Permissions.EBookingsManage);
    }
}
