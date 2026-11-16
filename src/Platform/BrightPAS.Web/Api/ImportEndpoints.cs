using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Devices;
using BrightPAS.Modules.Labs;
using BrightPAS.Modules.Messaging;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>
/// Offline import (0.18.0, ADR 0030): when a device or interface connection is down, its messages can be imported from a file –
/// USB stick, network share or a file the sending system saved. One file per request (the browser sends each chosen file in turn),
/// as the raw body with its name in X-File-Name. HL7 files may hold many messages; ZIPs are opened (with limits).
/// </summary>
public static class ImportEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;

    private static async Task<byte[]> Body(HttpRequest req, long max)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await req.Body.ReadAsync(buf)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new InvalidOperationException($"The file is larger than {max / 1024 / 1024} MB – split it or import it in parts.");
        }
        return ms.ToArray();
    }

    private static async Task<IResult> Run(HttpRequest req, Func<string, byte[], Task<ImportSummary>> f)
    {
        try
        {
            var data = await Body(req, FileImportService.MaxFileBytes);
            if (data.Length == 0) return Results.BadRequest(new { errors = new[] { "The file is empty." } });
            return Results.Ok(await f(req.Headers["X-File-Name"].ToString(), data));
        }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
    }

    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/imports/labs", (HttpRequest req, ClaimsPrincipal u, FileImportService svc) =>
                Run(req, (name, data) => svc.ImportAsync(ImportKind.Labs, name, data, Me(u))))
            .RequireModule(LabsModule.Id).RequireAuthorization(Permissions.LabsMatch);
        app.MapPost("/api/imports/monitors", (HttpRequest req, ClaimsPrincipal u, FileImportService svc) =>
                Run(req, (name, data) => svc.ImportAsync(ImportKind.Monitors, name, data, Me(u))))
            .RequireModule(DevicesModule.Id).RequireAuthorization(Permissions.DevicesUse);
        app.MapPost("/api/imports/safersleep", (HttpRequest req, ClaimsPrincipal u, FileImportService svc) =>
                Run(req, (name, data) => svc.ImportAsync(ImportKind.SaferSleep, name, data, Me(u))))
            .RequireModule(DevicesModule.Id).RequireAuthorization(Permissions.DevicesUse);
        app.MapPost("/api/messaging/interfaces/{id:guid}/import", async (Guid id, HttpRequest req, ClaimsPrincipal u, FileImportService svc, MessagingService messaging) =>
            {
                var i = await messaging.InterfaceAsync(id);
                if (i is null) return Results.NotFound(new { errors = new[] { "Interface not found." } });
                return await Run(req, (name, data) => svc.ImportToInterfaceAsync(i, name, data, Me(u)));
            })
            .RequireModule(MessagingModule.Id).RequireAuthorization(Permissions.IntegrationManage);
    }
}
