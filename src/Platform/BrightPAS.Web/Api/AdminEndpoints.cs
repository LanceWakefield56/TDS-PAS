using System.Security.Claims;
using BrightPAS.Core.Audit;
using BrightPAS.Identity;

namespace BrightPAS.Web.Api;

/// <summary>System administration API: users, roles and permissions, organisation settings and the audit log (administrator permissions only).</summary>
public static class AdminEndpoints
{
    public sealed record CreateUserRequest(string UserName, string DisplayName, string[] Roles, string Password);
    public sealed record RolesRequest(string[] Roles);
    public sealed record PasswordRequest(string Password);
    public sealed record LicenceUpload(string? Text);

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/admin/users").RequireAuthorization(Permissions.UsersManage);
        static IResult R(AdminResult r) => r.Succeeded ? Results.Ok() : Results.BadRequest(new { errors = r.Errors });

        g.MapGet("/", async (UserAdminService svc) => Results.Ok(await svc.ListAsync()));
        g.MapGet("/roles", async (UserAdminService svc) => Results.Ok((await svc.RolesAsync()).Select(r => new { name = r.Name, description = r.Description })));
        g.MapPost("/", async (CreateUserRequest req, ClaimsPrincipal me, UserAdminService svc, BrightPAS.Web.Licensing.LicenceCache licence) =>
        {
            if (licence.Status.Licence?.MaxUsers is int max && (await svc.ListAsync()).Count(u => u.IsActive) >= max)
                return Results.BadRequest(new { errors = new[] { $"The licence allows {max} active users. Deactivate an account or ask for a larger licence." } });
            var (r, u) = await svc.CreateAsync(req.UserName, req.DisplayName, req.Roles, req.Password, me.Identity!.Name!);
            return r.Succeeded ? Results.Ok(u) : Results.BadRequest(new { errors = r.Errors });
        });
        g.MapPut("/{id:guid}/roles", async (Guid id, RolesRequest req, ClaimsPrincipal me, UserAdminService svc) => R(await svc.SetRolesAsync(id, req.Roles, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/deactivate", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.SetActiveAsync(id, false, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/reactivate", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.SetActiveAsync(id, true, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/unlock", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.UnlockAsync(id, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/reset-mfa", async (Guid id, ClaimsPrincipal me, UserAdminService svc) => R(await svc.ResetMfaAsync(id, me.Identity!.Name!)));
        g.MapPost("/{id:guid}/reset-password", async (Guid id, PasswordRequest req, ClaimsPrincipal me, UserAdminService svc) => R(await svc.ResetPasswordAsync(id, req.Password, me.Identity!.Name!)));

        var l = app.MapGroup("/api/admin/licence").RequireAuthorization(Permissions.ModulesManage);
        l.MapGet("/", async (BrightPAS.Web.Licensing.LicensingService svc, BrightPAS.Web.Licensing.LicenceCache cache) =>
        {
            var state = await svc.StateAsync();
            var st = await svc.RefreshAsync();
            var d = st.Licence;
            return Results.Ok(new
            {
                installationCode = cache.InstallationCode,
                health = st.Health.ToString(),
                messages = st.Messages,
                developmentKey = st.SignedWithDevelopmentKey,
                productionKeyBuiltIn = BrightPAS.Licensing.VendorKeys.HasProductionKey,
                testBuild = !svc.Enforced,
                licence = d is null ? null : new
                {
                    d.LicenceId,
                    d.Customer,
                    d.Site,
                    d.Edition,
                    d.IssuedAt,
                    d.ExpiresOn,
                    d.InstallationCode,
                    d.MaxUsers,
                    d.GraceDays,
                    activation = d.Activation is null ? null : new { d.Activation.Url, d.Activation.CheckInDays, d.Activation.OfflineGraceDays }
                },
                installedAt = state.LicenceInstalledAt,
                installedBy = state.LicenceInstalledBy,
                activationCheckedAt = state.ActivationCheckedAt,
                activationError = state.ActivationError,
                modules = BrightPAS.Licensing.ProductModules.All.Select(m => new { m.Id, m.Name, m.Description, m.Available, access = st.AccessTo(m.Id).ToString() })
            });
        });
        l.MapPost("/", async (LicenceUpload req, ClaimsPrincipal me, HttpContext http, BrightPAS.Web.Licensing.LicensingService svc) =>
        {
            var (ok, error, _) = await svc.InstallAsync(req.Text ?? "", me.Identity!.Name!, AuthEndpoints.Workstation(http));
            return ok ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
        });
        l.MapPost("/activate", async (ClaimsPrincipal me, BrightPAS.Web.Licensing.LicensingService svc) =>
        {
            var (ok, message) = await svc.ActivateAsync(me.Identity!.Name!);
            return ok ? Results.Ok(new { message }) : Results.BadRequest(new { errors = new[] { message } });
        });

        // Module set-up wizards (ADR 0014).
        var m = app.MapGroup("/api/admin/modules").RequireAuthorization(Permissions.ModulesManage);
        m.MapGet("/", async (BrightPAS.Web.Licensing.ModuleSetupService svc) => Results.Ok(await svc.ListAsync()));
        m.MapGet("/{id}/setup", async (string id, BrightPAS.Web.Licensing.ModuleSetupService svc) =>
        {
            var mod = BrightPAS.Licensing.ProductModules.Find(id);
            if (mod is null || !mod.Available || !BrightPAS.Web.Licensing.ModuleSetupService.WithWizard.Contains(mod.Id)) return Results.NotFound();
            var list = await svc.ListAsync();
            return Results.Ok(new { module = list.First(x => x.Id == mod.Id), checks = await svc.ChecksAsync(mod.Id) });
        });
        m.MapPost("/{id}/setup/complete", async (string id, ClaimsPrincipal me, BrightPAS.Web.Licensing.ModuleSetupService svc) =>
        {
            var (ok, error) = await svc.CompleteAsync(id, me.Identity!.Name!);
            return ok ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
        });
        m.MapPost("/{id}/setup/reopen", async (string id, ClaimsPrincipal me, BrightPAS.Web.Licensing.ModuleSetupService svc) =>
        {
            var error = await svc.ReopenAsync(id, me.Identity!.Name!);
            return error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
        });

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
