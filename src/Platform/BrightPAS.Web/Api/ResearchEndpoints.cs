using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Research;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Api;

/// <summary>De-identified and anonymised reporting (spec 7.21, ADR 0038).</summary>
public static class ResearchEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    public sealed record DecideRequest(bool Approve, string? Note);

    private static object View(Extract x) => new
    {
        x.Id,
        x.Number,
        x.Title,
        x.Dataset,
        level = x.Level.ToString(),
        x.FromDate,
        x.ToDate,
        purpose = x.Purpose.ToString(),
        x.Recipient,
        x.EthicsReference,
        x.MaoriDataGovernance,
        x.SmallCell,
        x.RemoveSmallGroups,
        status = x.Status.ToString(),
        x.Rows,
        x.MinGroupSize,
        x.AtRiskRows,
        x.SuppressedCells,
        x.RequestedBy,
        x.RequestedAt,
        x.DecidedBy,
        x.DecidedAt,
        x.DecisionNote,
        x.ReleasedBy,
        x.ReleasedAt,
        x.Downloads,
        columns = x.Columns.OrderBy(c => c.Position).Select(c => c.Column)
    };

    public static void MapResearchEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/research").RequireModule(ResearchModule.LicenceId);
        var any = Permissions.AnyOf(Permissions.ResearchBuild, Permissions.ResearchApprove);

        g.MapGet("/datasets", (ExtractService svc) => Results.Ok(svc.Datasets.Select(d => new { d.Key, d.Label, columns = d.Columns.Select(c => new { c.Key, c.Label, kind = c.Kind.ToString() }) })))
            .RequireAuthorization(any);
        g.MapGet("/extracts", async (ExtractService svc) => Results.Ok((await svc.ListAsync()).Select(View))).RequireAuthorization(any);
        g.MapGet("/extracts/{id:guid}", async (Guid id, ExtractService svc) => await svc.GetAsync(id) is { } x ? Results.Ok(View(x)) : Results.NotFound()).RequireAuthorization(any);
        g.MapPost("/extracts", async (ExtractEdit e, ClaimsPrincipal u, ExtractService svc) =>
        {
            var (x, errors) = await svc.CreateAsync(e, Me(u));
            return x is null ? Results.BadRequest(new { errors }) : Results.Ok(new { x.Id, x.Number, status = x.Status.ToString() });
        }).RequireAuthorization(Permissions.ResearchBuild);
        g.MapPost("/extracts/{id:guid}/preview", async (Guid id, ClaimsPrincipal u, ExtractService svc) =>
            await svc.PreviewAsync(id, Me(u)) is { } r ? Results.Ok(r) : Results.NotFound()).RequireAuthorization(any);
        g.MapPost("/extracts/{id:guid}/decide", async (Guid id, DecideRequest r, ClaimsPrincipal u, ExtractService svc) =>
            await svc.DecideAsync(id, r.Approve, r.Note, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.ResearchApprove);
        g.MapPost("/extracts/{id:guid}/download", async (Guid id, CodingEndpoints.DownloadRequest r, ClaimsPrincipal u, ExtractService svc, SecureExportService export) =>
        {
            var (csv, name, error) = await svc.ReleaseAsync(id, Me(u));
            if (csv is null) return Results.BadRequest(new { errors = new[] { error } });
            return await export.ExportAsync(r.Export, name!, [new(name + ".csv", csv)], Me(u), "DeidentifiedExtract");
        }).RequireAuthorization(Permissions.ResearchBuild);
        g.MapPost("/extracts/{id:guid}/key", async (Guid id, CodingEndpoints.DownloadRequest r, ClaimsPrincipal u, ExtractService svc, SecureExportService export) =>
        {
            var key = await svc.StudyKeyAsync(id, Me(u));
            if (key is null) return Results.BadRequest(new { errors = new[] { "Only pseudonymised extracts have a linking key." } });
            return await export.ExportAsync(r.Export, "linking-key", [new("linking-key.csv", key)], Me(u), "StudyKey");
        }).RequireAuthorization(Permissions.ResearchApprove);
    }
}
