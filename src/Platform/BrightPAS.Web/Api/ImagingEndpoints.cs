using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Pacs;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Api;

/// <summary>Imaging (PACS) (0.37.0, ADR 0050): studies, upload, filing, the viewer's pictures, imaging requests and settings.</summary>
public static class ImagingEndpoints
{
    public const long MaxUploadBytes = 1024L * 1024 * 1024;
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record FileRequest(Guid PatientId, Guid? BookingId, bool ConfirmMismatch);
    public sealed record EpisodeRequest(Guid? BookingId);
    public sealed record ReportRequest(string? Text);
    public sealed record ExportRequest(ExportOptions? Export);

    private static async Task<byte[]> Body(HttpRequest req, long max)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await req.Body.ReadAsync(buf)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new InvalidOperationException($"The file is larger than {max / 1024 / 1024} MB – split the CD into smaller ZIPs or choose the files.");
        }
        return ms.ToArray();
    }

    public static void MapImagingEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/imaging").RequireModule(ImagingModule.Id);

        g.MapGet("/meta", async (ImagingService svc) => Results.Ok(new { modalities = ImagingService.Modalities, unmatched = await svc.UnmatchedCountAsync() }))
            .RequireAuthorization(Permissions.ImagingAny);
        g.MapGet("/studies", async (string? view, Guid? patientId, Guid? bookingId, ImagingService svc) => Results.Ok(await svc.StudiesAsync(view, patientId, bookingId)))
            .RequireAuthorization(Permissions.ImagingSee);
        g.MapGet("/studies/{id:guid}", async (Guid id, ClaimsPrincipal u, ImagingService svc) => await svc.StudyAsync(id, Me(u)) is { } s ? Results.Ok(s) : Results.NotFound())
            .RequireAuthorization(Permissions.ImagingSee);
        g.MapGet("/images/{id:guid}/render", async (Guid id, int? frame, double? centre, double? width, bool? invert, int? size, ImagingService svc) =>
        {
            var r = await svc.RenderAsync(id, frame ?? 0, centre, width, invert == true, size ?? 1024);
            if (r is null) return Results.NotFound();
            return Results.File(r.Bytes, r.ContentType, lastModified: null, entityTag: null, enableRangeProcessing: false);
        }).RequireAuthorization(Permissions.ImagingSee);
        // Window values the viewer starts from (so the sliders show what the picture is drawn with).
        g.MapGet("/images/{id:guid}/window", async (Guid id, int? frame, ImagingService svc) =>
            await svc.RenderAsync(id, frame ?? 0, null, null, false, 256) is { } r ? Results.Ok(new { r.Adjustable, centre = r.Centre, width = r.Width2 }) : Results.NotFound())
            .RequireAuthorization(Permissions.ImagingSee);
        g.MapPost("/studies/{id:guid}/export", async (Guid id, ExportRequest r, ClaimsPrincipal u, ImagingService svc, SecureExportService export) =>
        {
            var files = await svc.ExportFilesAsync(id);
            if (files is not { } f || f.Files.Count == 0) return Results.NotFound();
            return await export.ExportAsync(r.Export ?? new(true, null), $"BrightPAS-imaging-{f.Label}",
                f.Files.Select(x => new SecureExportService.ExportFile(x.Name, x.Bytes)).ToList(), Me(u), "ImagingStudy", f.PatientId?.ToString());
        }).RequireAuthorization(Permissions.ImagingSee);

        // Upload: DICOM files, a ZIP of them, or each file of a CD folder in turn. Matched exactly as the watched folder does.
        g.MapPost("/import", async (HttpRequest req, ClaimsPrincipal u, ImagingService svc, BrightPAS.Core.Audit.AuditWriter audit) =>
        {
            byte[] body;
            try { body = await Body(req, MaxUploadBytes); }
            catch (InvalidOperationException ex) { return Err(ex.Message); }
            var name = FileImportService.Clean(req.Headers["X-File-Name"]);
            var results = await ImagingFolderWorker.ImportAsync(name, body, ImageSource.Upload, $"upload by {Me(u)}", svc);
            var summary = ImportSummary.From(results);
            await audit.WriteAsync(Me(u), "ImportFile", "Imaging", name, reason: $"{summary.Messages} file(s): {summary.Accepted} stored, {summary.Duplicates} already held, {summary.Rejected} not imported");
            return Results.Ok(summary);
        }).RequireAuthorization(Permissions.ImagingImport).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxUploadBytes + 1024));

        g.MapPost("/studies/{id:guid}/file", async (Guid id, FileRequest r, ClaimsPrincipal u, ImagingService svc) => Errs(await svc.FileAsync(id, r.PatientId, r.BookingId, r.ConfirmMismatch, Me(u))))
            .RequireAuthorization(Permissions.ImagingImport);
        g.MapPut("/studies/{id:guid}/episode", async (Guid id, EpisodeRequest r, ClaimsPrincipal u, ImagingService svc) => Err(await svc.LinkEpisodeAsync(id, r.BookingId, Me(u))))
            .RequireAuthorization(Permissions.ImagingImport);
        g.MapPut("/studies/{id:guid}/report", async (Guid id, ReportRequest r, ClaimsPrincipal u, ImagingService svc) => Err(await svc.SaveReportAsync(id, r.Text, Me(u))))
            .RequireAuthorization(Permissions.ImagingView);
        g.MapDelete("/studies/{id:guid}", async (Guid id, string? reason, ClaimsPrincipal u, ImagingService svc) => Err(await svc.DeleteAsync(id, reason, Me(u))))
            .RequireAuthorization(Permissions.ImagingManage);
        g.MapGet("/patients/{patientId:guid}/episodes", async (Guid patientId, ImagingService svc) => Results.Ok(await svc.PatientEpisodesAsync(patientId)))
            .RequireAuthorization(Permissions.ImagingSee);

        g.MapGet("/requests", async (string? view, Guid? patientId, ImagingService svc) => Results.Ok(await svc.RequestsAsync(view, patientId))).RequireAuthorization(Permissions.ImagingSee);
        g.MapPost("/requests", async (RequestInput r, ClaimsPrincipal u, ImagingService svc) =>
        {
            var (req, errors) = await svc.CreateRequestAsync(r, Me(u));
            return req is null ? Errs(errors) : Results.Ok(new { req.Id, req.AccessionNumber });
        }).RequireAuthorization(Permissions.ImagingImport);
        g.MapPost("/requests/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, ImagingService svc) => Err(await svc.CancelRequestAsync(id, Me(u)))).RequireAuthorization(Permissions.ImagingImport);
        g.MapGet("/worklist", async (DateOnly? date, string? modality, ImagingService svc) => Results.Ok(await svc.WorklistAsync(date, modality))).RequireAuthorization(Permissions.ImagingSee);

        g.MapGet("/settings", async (ImagingService svc, ImagingFolderWorker worker) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { s.StoragePath, s.ImportFolder, s.AutoFile, folder = worker.Status });
        }).RequireAuthorization(Permissions.ImagingManage);
        g.MapPut("/settings", async (ImagingSettingsInput r, ClaimsPrincipal u, ImagingService svc, ImagingFolderWorker worker) =>
        {
            var errors = await svc.SaveSettingsAsync(r, Me(u));
            if (errors.Count == 0) worker.Nudge();
            return Errs(errors);
        }).RequireAuthorization(Permissions.ImagingManage);
    }
}
