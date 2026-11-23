using System.Security.Claims;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Identity;
using BrightPAS.Web.Journey;

namespace BrightPAS.Web.Api;

/// <summary>Patient journey, Today list and day board (ADR 0036). Read only; every action opens the screen that does the job.</summary>
public static class JourneyEndpoints
{
    public static void MapJourneyEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/journey").RequireAuthorization();

        g.MapGet("/patients/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, JourneyService svc, AuditWriter audit) =>
        {
            var v = await svc.ForPatientAsync(id);
            if (v is null) return Results.NotFound();
            await audit.WriteAsync(u.Identity!.Name!, "View", "PatientJourney", id.ToString(), patientId: id.ToString(), workstation: AuthEndpoints.Workstation(http));
            return Results.Ok(v);
        }).RequireAuthorization(Permissions.PatientView);

        g.MapGet("/today", async (ClaimsPrincipal u, JourneyService svc) =>
            Results.Ok(await svc.TodayAsync(p => u.HasClaim(Permissions.ClaimType, p)))).RequireAuthorization(Permissions.PatientView);

        g.MapGet("/day", async (string? date, JourneyService svc, TimeProvider clock) =>
        {
            var d = DateOnly.TryParse(date, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : NzTime.Today(clock);
            return Results.Ok(new { date = d, columns = JourneyService.Columns, cards = await svc.DayAsync(d) });
        }).RequireAuthorization(Permissions.TheatreView);
    }
}
