using Microsoft.EntityFrameworkCore;
using Galenica.Core.Data;

namespace Galenica.Web.Api;

public static class OrganisationEndpoints
{
    public static void MapOrganisationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/organisation/locations", async (PasDbContext db) =>
            Results.Ok(await db.Locations.OrderBy(l => l.SortOrder)
                .Select(l => new { l.Id, l.Code, l.Name, type = l.Type.ToString(), l.IsActive }).ToListAsync()))
            .RequireAuthorization();
    }
}
