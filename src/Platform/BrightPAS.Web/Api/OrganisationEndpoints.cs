using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Identity;

namespace BrightPAS.Web.Api;

/// <summary>Organisation reference data (read by every signed-in user) and its administration (clinic details, rooms, clinicians, procedures, roles).</summary>
public static class OrganisationEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record RolePermissions(string Name, string[] Permissions);
    public sealed record NewRole(string Name, string? Description);

    public static void MapOrganisationEndpoints(this IEndpointRouteBuilder app)
    {
        // Reference lists any signed-in user may read (they contain no patient information).
        app.MapGet("/api/organisation/locations", async (PasDbContext db) =>
            Results.Ok(await db.Locations.OrderBy(l => l.SortOrder)
                .Select(l => new { l.Id, l.Code, l.Name, type = l.Type.ToString(), l.IsActive }).ToListAsync()))
            .RequireAuthorization();
        app.MapGet("/api/organisation/clinicians", async (OrganisationService svc) =>
            Results.Ok((await svc.CliniciansAsync(activeOnly: true)).Select(c => new { c.Id, c.Name, type = c.Type.ToString(), c.Specialty })))
            .RequireAuthorization();
        app.MapGet("/api/organisation/procedures", async (OrganisationService svc) => Results.Ok(await svc.ProceduresAsync(activeOnly: true)))
            .RequireAuthorization();

        var g = app.MapGroup("/api/admin/organisation").RequireAuthorization(Permissions.OrganisationManage);
        g.MapGet("/", async (OrganisationService svc) =>
        {
            var o = await svc.GetAsync();
            if (o is null) return Results.NotFound();
            var site = o.Sites.Where(s => s.IsActive).OrderBy(s => s.Name).First();
            return Results.Ok(new
            {
                organisationName = o.Name, o.HpiOrganisationId, siteName = site.Name, site.HpiFacilityId, site.AddressLine1, site.AddressLine2, site.Suburb, site.City,
                site.Postcode, site.Phone, site.Email,
                facilities = site.Facilities.OrderBy(f => f.Name).Select(f => new
                {
                    f.Id, f.Name, type = f.Type.ToString(),
                    locations = f.Locations.OrderBy(l => l.SortOrder).Select(l => new { l.Id, l.FacilityId, l.Code, l.Name, type = l.Type.ToString(), l.IsActive, l.SortOrder })
                }),
                locationTypes = Enum.GetNames<LocationType>()
            });
        });
        g.MapPut("/", async (OrganisationDetails d, ClaimsPrincipal u, OrganisationService svc) => Errors(await svc.UpdateDetailsAsync(d, Me(u))));
        g.MapPost("/locations", async (LocationEdit l, ClaimsPrincipal u, OrganisationService svc) =>
        {
            var (loc, error) = await svc.SaveLocationAsync(l, Me(u));
            return loc is null ? Errors([error!]) : Results.Ok(new { loc.Id });
        });
        g.MapGet("/clinicians", async (OrganisationService svc) => Results.Ok(await svc.CliniciansAsync(activeOnly: false)));
        g.MapPost("/clinicians", async (ClinicianEdit c, ClaimsPrincipal u, OrganisationService svc) =>
        {
            var (row, errors) = await svc.SaveClinicianAsync(c, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        });
        g.MapGet("/procedures", async (OrganisationService svc) => Results.Ok(await svc.ProceduresAsync(activeOnly: false)));
        g.MapPost("/procedures", async (ProcedureEdit p, ClaimsPrincipal u, OrganisationService svc) =>
        {
            var (row, errors) = await svc.SaveProcedureAsync(p, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        });
        g.MapPost("/procedures/examples", async (ClaimsPrincipal u, OrganisationService svc) => Results.Ok(new { added = await svc.AddExampleProceduresAsync(Me(u)) }));

        // Roles and permissions.
        var r = app.MapGroup("/api/admin/roles").RequireAuthorization(Permissions.UsersManage);
        r.MapGet("/", async (RoleAdminService svc) => Results.Ok(new
        {
            roles = await svc.ListAsync(),
            permissions = RoleAdminService.Catalogue.Select(c => new { id = c.Key, group = c.Value.Group, text = c.Value.Text }),
            adminMustKeep = RoleAdminService.AdminMustKeep
        }));
        // Role name in the body: default role names contain "/", which cannot travel safely in a URL path.
        r.MapPut("/", async (RolePermissions req, ClaimsPrincipal u, RoleAdminService svc) =>
        {
            var res = await svc.SetPermissionsAsync(req.Name ?? "", req.Permissions ?? [], Me(u));
            return res.Succeeded ? Results.Ok() : Results.BadRequest(new { errors = res.Errors });
        });
        r.MapPost("/", async (NewRole req, ClaimsPrincipal u, RoleAdminService svc) =>
        {
            var res = await svc.CreateAsync(req.Name, req.Description, Me(u));
            return res.Succeeded ? Results.Ok() : Results.BadRequest(new { errors = res.Errors });
        });
    }
}
