using System.Security.Claims;
using Galenica.Core.Audit;
using Galenica.Identity;

namespace Galenica.Web.Api;

public static class AdminEndpoints
{
    public sealed record CreateUserRequest(string UserName, string DisplayName, string[] Roles, string Password);
    public sealed record RolesRequest(string[] Roles);
    public sealed record PasswordRequest(string Password);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/users").RequireAuthorization(Permissions.UsersManage);
        static IResult R(AdminResult r) => r.Succeeded ? Results.Ok() : Results.BadRequest(new { errors = r.Errors });

        g.MapGet("/", async (UserAdminService svc) => Results.Ok(await svc.ListAsync()));
        g.MapGet("/roles", async (UserAdminService svc) => Results.Ok((await svc.RolesAsync()).Select(r => new { name = r.Name, description = r.Description })));
        g.MapPost("/", async (CreateUserRequest req, ClaimsPrincipal me, UserAdminService svc) =>
        {
            var (r, u) = await svc.CreateAsync(req.UserName, req.DisplayName, req.Roles, req.Password, me.Identity!.Name!);
            return r.Succeeded ? Results.Ok(u) : Results.BadRequest(new { errors = r.Errors });
        });
        g.MapPut("/{id:guid}/roles", async (Guid id, RolesRequest req, ClaimsPrincipal me, UserAdminService svc) => R(await svc.SetRolesAsync(id, req.Roles, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/deactivate", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.SetActiveAsync(id, false, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/reactivate", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.SetActiveAsync(id, true, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/unlock", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.UnlockAsync(id, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/reset-mfa", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.ResetMfaAsync(id, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/reset-password", async (Guid id, PasswordRequest req, ClaimsPrincipal me, UserAdminService svc) => R(await svc.ResetPasswordAsync(id, req.Password, me.Identity!.Name!)));

        var a = app.MapGroup("/api/audit").RequireAuthorization(Permissions.AuditView);
        a.MapGet("/", async (string? user, string? patient, string? action, int? take, AuditQueryService svc) =>
            Results.Ok((await svc.SearchAsync(user, patient, action, take ?? 200))
                .Select(e => new { e.Sequence, e.OccurredAt, e.UserId, e.Action, e.EntityType, e.EntityId, e.PatientId, e.Reason, e.Workstation })));
        a.MapGet("/verify", async (ClaimsPrincipal me, AuditQueryService svc, AuditWriter audit) =>
        {
            var r = await svc.VerifyAsync();
            await audit.WriteAsync(me.Identity!.Name!, "AuditVerified", "AuditLog", "core.AuditEvents", reason: r.Intact ? $"Intact ({r.Records} records)" : $"BROKEN at {r.FirstBrokenSequence}");
            return Results.Ok(r);
        });
    }
}
