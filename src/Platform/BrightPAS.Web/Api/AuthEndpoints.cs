using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using BrightPAS.Identity;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Api;

public static class AuthEndpoints
{
    public sealed record LoginRequest(string UserName, string Password);
    public sealed record CodeRequest(string Code);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Public by design (sign-in steps check the pending sign-in cookie themselves); rate-limited against guessing.
        var g = app.MapGroup("/api/auth").RequireRateLimiting("auth").AllowAnonymous();

        // Step 1: password. Returns what happens next; never says whether the username exists.
        g.MapPost("/login", async (LoginRequest req, HttpContext http, SignInService signIn) =>
        {
            await http.SignOutAsync(PasAuthentication.PendingScheme);
            var step = await signIn.CheckPasswordAsync(req.UserName.Trim(), req.Password, Workstation(http));
            if (step is SignInStep.NeedsMfaCode or SignInStep.NeedsMfaEnrolment)
            {
                var stage = step == SignInStep.NeedsMfaCode ? "mfa" : "enrol";
                var id = new ClaimsIdentity([new Claim(ClaimTypes.Name, req.UserName.Trim()), new Claim(PasAuthentication.StageClaim, stage)], PasAuthentication.PendingScheme);
                await http.SignInAsync(PasAuthentication.PendingScheme, new ClaimsPrincipal(id));
                return Results.Ok(new { next = stage });
            }
            return Results.Ok(new { next = step switch { SignInStep.LockedOut => "locked", SignInStep.Inactive => "failed", _ => "failed" } });
        });

        // Step 2: authenticator or recovery code.
        g.MapPost("/mfa", async (CodeRequest req, HttpContext http, SignInService signIn, UserManager<PasUser> users, PasClaimsFactory claims) =>
        {
            var pending = await Pending(http, "mfa");
            if (pending is null) return Results.Unauthorized();
            var step = await signIn.CheckMfaAsync(pending, req.Code, Workstation(http));
            if (step != SignInStep.Success) return Results.Ok(new { next = step == SignInStep.LockedOut ? "locked" : "retry" });
            await CompleteSignIn(http, (await users.FindByNameAsync(pending))!, claims);
            return Results.Ok(new { next = "done" });
        });

        // New accounts: set up the authenticator app before first use.
        g.MapPost("/enrol/begin", async (HttpContext http, UserManager<PasUser> users, MfaService mfa) =>
        {
            var pending = await Pending(http, "enrol");
            var user = pending is null ? null : await users.FindByNameAsync(pending);
            if (user is null || user.TwoFactorEnabled) return Results.Unauthorized();
            var e = await mfa.BeginTotpEnrolmentAsync(user);
            return Results.Ok(new { sharedKey = e.SharedKey, otpAuthUri = e.OtpAuthUri });
        });

        g.MapPost("/enrol/complete", async (CodeRequest req, HttpContext http, UserManager<PasUser> users, MfaService mfa, PasClaimsFactory claims, BrightPAS.Core.Audit.AuditWriter audit) =>
        {
            var pending = await Pending(http, "enrol");
            var user = pending is null ? null : await users.FindByNameAsync(pending);
            if (user is null || user.TwoFactorEnabled) return Results.Unauthorized();
            var codes = await mfa.CompleteTotpEnrolmentAsync(user, req.Code);
            if (codes is null) return Results.Ok(new { next = "retry" });
            await audit.WriteAsync(user.UserName!, "MfaEnrolled", "User", user.Id.ToString(), workstation: Workstation(http));
            await CompleteSignIn(http, user, claims);
            return Results.Ok(new { next = "done", recoveryCodes = codes });
        });

        g.MapPost("/logout", async (HttpContext http, BrightPAS.Core.Audit.AuditWriter audit) =>
        {
            var name = http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null;
            await http.SignOutAsync(PasAuthentication.Scheme);
            await http.SignOutAsync(PasAuthentication.PendingScheme);
            if (name is not null) await audit.WriteAsync(name, "Logout", "User", name, workstation: Workstation(http));
            return Results.Ok();
        });

        app.MapGet("/api/me", async (ClaimsPrincipal user, BrightPAS.Web.Licensing.LicenceCache licence, BrightPAS.Web.Licensing.ModuleSetupService setup) => Results.Ok(new
        {
            moduleSetup = await setup.SetUpModulesAsync(),
            userName = user.Identity!.Name,
            displayName = user.FindFirstValue(PasClaimsFactory.DisplayNameClaim),
            roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value),
            permissions = user.FindAll(Permissions.ClaimType).Select(c => c.Value),
            modules = licence.Status.Modules.ToDictionary(m => m.Key, m => m.Value.ToString()),
            licence = new
            {
                health = licence.Status.Health.ToString(),
                edition = licence.Status.Licence?.Edition,
                developmentKey = licence.Status.SignedWithDevelopmentKey,
                message = licence.Status.Health is BrightPAS.Licensing.LicenceHealth.Valid ? null : licence.Status.Messages.LastOrDefault()
            }
        })).RequireAuthorization();
    }

    private static async Task<string?> Pending(HttpContext http, string stage)
    {
        var r = await http.AuthenticateAsync(PasAuthentication.PendingScheme);
        return r.Succeeded && r.Principal.FindFirstValue(PasAuthentication.StageClaim) == stage ? r.Principal.Identity?.Name : null;
    }

    private static async Task CompleteSignIn(HttpContext http, PasUser user, PasClaimsFactory claims)
    {
        await http.SignOutAsync(PasAuthentication.PendingScheme);
        await http.SignInAsync(PasAuthentication.Scheme, await claims.CreateAsync(user, PasAuthentication.Scheme),
            new AuthenticationProperties { IsPersistent = false, IssuedUtc = DateTimeOffset.UtcNow });
    }

    /// <summary>Workstation label for the audit log: client address (the UI may later send a registered device name).</summary>
    public static string? Workstation(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
