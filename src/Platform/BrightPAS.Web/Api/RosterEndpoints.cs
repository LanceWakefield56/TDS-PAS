using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Rostering;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Rostering: staff, shifts, regular shifts, leave and hours.</summary>
public static class RosterEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });
    private static bool Manager(ClaimsPrincipal u) => u.HasClaim(Permissions.ClaimType, Permissions.RosterManage);

    public sealed record RangeRequest(DateOnly From, DateOnly To);
    public sealed record DecisionRequest(LeaveStatus Decision);

    public static void MapRosterEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/roster").RequireModule(RosteringModule.Id);
        g.MapGet("/me", async (ClaimsPrincipal u, RosterService svc) => Results.Ok(new { staff = await svc.StaffForUserAsync(Me(u)), manager = Manager(u) }))
            .RequireAuthorization(Permissions.RosterView);
        g.MapGet("/staff", async (bool? all, ClaimsPrincipal u, RosterService svc) =>
            Results.Ok((await svc.StaffAsync(activeOnly: all != true || !Manager(u))).Select(s => new { s.Id, s.Name, role = s.Role.ToString(), s.Colour, s.IsActive,
                userName = Manager(u) ? s.UserName : null, contractedHours = Manager(u) ? s.ContractedHours : null, phone = Manager(u) ? s.Phone : null })))
            .RequireAuthorization(Permissions.RosterView);
        g.MapPost("/staff", async (StaffEdit e, ClaimsPrincipal u, RosterService svc) =>
        {
            var (row, errors) = await svc.SaveStaffAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.RosterManage);
        g.MapGet("/shifts", async (DateOnly from, DateOnly to, Guid? staffId, RosterService svc) =>
            to < from || to.DayNumber - from.DayNumber > 62 ? Results.BadRequest(new { errors = new[] { "Choose up to 9 weeks." } }) : Results.Ok(await svc.ShiftsAsync(from, to, staffId)))
            .RequireAuthorization(Permissions.RosterView);
        g.MapPost("/shifts", async (ShiftEdit e, ClaimsPrincipal u, RosterService svc) =>
        {
            var r = await svc.SaveShiftAsync(e, Me(u));
            return r.Shift is not null ? Results.Ok(new { r.Shift.Id, r.Warnings }) : Results.BadRequest(new { errors = r.Errors, warnings = r.Warnings });
        }).RequireAuthorization(Permissions.RosterManage);
        g.MapPost("/shifts/{id:guid}/delete", async (Guid id, ClaimsPrincipal u, RosterService svc) =>
            await svc.DeleteShiftAsync(id, Me(u)) is { } error ? Results.BadRequest(new { errors = new[] { error } }) : Results.Ok()).RequireAuthorization(Permissions.RosterManage);
        g.MapGet("/hours", async (DateOnly from, DateOnly to, RosterService svc) => Results.Ok(await svc.HoursAsync(from, to))).RequireAuthorization(Permissions.RosterManage);
        g.MapGet("/templates", async (RosterService svc) => Results.Ok(await svc.TemplatesAsync())).RequireAuthorization(Permissions.RosterManage);
        g.MapPost("/templates", async (ShiftTemplateEdit t, ClaimsPrincipal u, RosterService svc) => Errors(await svc.SaveTemplateAsync(t, Me(u))))
            .RequireAuthorization(Permissions.RosterManage);
        g.MapPost("/templates/generate", async (RangeRequest r, ClaimsPrincipal u, RosterService svc) =>
        {
            var (created, skipped) = await svc.GenerateAsync(r.From, r.To, Me(u));
            return Results.Ok(new { created, skipped });
        }).RequireAuthorization(Permissions.RosterManage);
        // Leave: managers see and decide everything; staff see and request their own (via the user link on their staff record).
        g.MapGet("/leave", async (string? view, ClaimsPrincipal u, RosterService svc) =>
        {
            if (Manager(u)) return Results.Ok(await svc.LeaveAsync(view ?? "requested", null));
            var me = await svc.StaffForUserAsync(Me(u));
            return Results.Ok(me is null ? [] : await svc.LeaveAsync("all", me.Id));
        }).RequireAuthorization(Permissions.RosterView);
        g.MapPost("/leave", async (LeaveRequest r, ClaimsPrincipal u, RosterService svc) =>
        {
            if (!Manager(u) && (await svc.StaffForUserAsync(Me(u)))?.Id != r.StaffId)
                return Results.Json(new { errors = new[] { "You can only request leave for yourself." } }, statusCode: 403);
            var (row, errors) = await svc.RequestLeaveAsync(r, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.RosterView);
        g.MapPost("/leave/{id:guid}/decide", async (Guid id, DecisionRequest r, ClaimsPrincipal u, RosterService svc) =>
        {
            var (error, affected) = await svc.DecideLeaveAsync(id, r.Decision, Me(u));
            return error is null ? Results.Ok(new { shiftsNeedingCover = affected }) : Results.BadRequest(new { errors = new[] { error } });
        }).RequireAuthorization(Permissions.RosterManage);
    }
}
