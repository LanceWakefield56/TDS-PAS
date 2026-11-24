using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using BrightPAS.Identity;

namespace BrightPAS.Web.Security;

public static class PasAuthentication
{
    /// <summary>Full sign-in cookie (after password and MFA).</summary>
    public const string Scheme = "PasAuth";
    /// <summary>Short-lived cookie between the password step and the MFA step (or MFA enrolment).</summary>
    public const string PendingScheme = "PasPending";
    public const string StageClaim = "pas:stage";
    public const string CsrfHeader = "X-BrightPAS-Request";
    /// <summary>Per-sign-in session id, so signing out revokes that cookie even if a copy was kept (security test 0.26.0).</summary>
    public const string SessionClaim = "pas:sid";

    public static IServiceCollection AddPasAuthentication(this IServiceCollection services, IWebHostEnvironment env)
    {
        var secure = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        services.AddAuthentication(Scheme)
            .AddCookie(Scheme, o =>
            {
                o.Cookie.Name = env.IsDevelopment() ? "brightpas" : "__Host-brightpas"; // __Host- prefix requires HTTPS
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = secure;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(15); // idle timeout (sliding)
                o.SlidingExpiration = true;
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = ValidateAsync;
            })
            .AddCookie(PendingScheme, o =>
            {
                o.Cookie.Name = env.IsDevelopment() ? "brightpas-pending" : "__Host-brightpas-pending";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = secure;
                o.Cookie.Path = "/";
                o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                o.SlidingExpiration = false;
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            });

        services.AddAuthorization(o =>
        {
            // Secure by default: any endpoint that does not say otherwise needs a signed-in user. Public endpoints must opt out
            // explicitly with AllowAnonymous, and the endpoint security inventory test checks every one of them.
            o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(Scheme).RequireAuthenticatedUser().Build();
            foreach (var p in Permissions.All)
                o.AddPolicy(p, b => b.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim(Permissions.ClaimType, p));
            foreach (var p in Permissions.AnyOfPolicies)
                o.AddPolicy(p, b => b.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().RequireClaim(Permissions.ClaimType, p.Split('|')));
        });
        return services;
    }

    /// <summary>Re-checks the account every 2 minutes: deactivated users or changed security stamps are signed out.</summary>
    private static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var sid = ctx.Principal?.FindFirstValue(SessionClaim);
        if (sid is null || ctx.HttpContext.RequestServices.GetRequiredService<SessionRevocations>().IsRevoked(sid))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(Scheme);
            return;
        }
        var issued = ctx.Properties.IssuedUtc;
        var last = ctx.Properties.Items.TryGetValue("pas:checked", out var v) && DateTimeOffset.TryParse(v, out var t) ? t : issued;
        if (last is not null && DateTimeOffset.UtcNow - last < TimeSpan.FromMinutes(2)) return;

        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserManager<PasUser>>();
        var id = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id is null ? null : await users.FindByIdAsync(id);
        var stamp = user is null ? null : await users.GetSecurityStampAsync(user);
        if (user is null || !user.IsActive || stamp != ctx.Principal!.FindFirstValue(PasClaimsFactory.SecurityStampClaim))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(Scheme);
            return;
        }
        ctx.Properties.Items["pas:checked"] = DateTimeOffset.UtcNow.ToString("O");
        ctx.ShouldRenew = true;
    }

    /// <summary>
    /// CSRF protection for the JSON API: state-changing requests must carry a custom header, which browsers
    /// only allow same-origin scripts to add. Combined with SameSite=Strict cookies.
    /// </summary>
    public static IApplicationBuilder UsePasCsrfProtection(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var m = ctx.Request.Method;
        var unsafeMethod = !(HttpMethods.IsGet(m) || HttpMethods.IsHead(m) || HttpMethods.IsOptions(m));
        // Vendor callbacks (/api/hooks/...) come from servers, not browsers: they carry no cookie and are authenticated by a secret URL key instead.
        // Machine-to-machine endpoints (no cookies; authenticated by their own keys): SMS vendor callbacks and inbound REST interfaces.
        var vendorCallback = ctx.Request.Path.StartsWithSegments("/api/hooks") || ctx.Request.Path.StartsWithSegments("/api/integration/in") || ctx.Request.Path.StartsWithSegments("/api/mcp")
            || ctx.Request.Path.StartsWithSegments("/api/scribe/inbound"); // external scribe returning drafts (bearer token)
        if (unsafeMethod && !vendorCallback && ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Headers[CsrfHeader] != "1")
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { error = "MissingRequestHeader" });
            return;
        }
        await next();
    });
}


/// <summary>
/// Sessions ended by signing out (checked on every request). Kept in memory for the longest a cookie can live; after a server restart a
/// copied cookie could only be replayed within its 15-minute idle window – deactivation and password changes are enforced by the security stamp.
/// </summary>
public sealed class SessionRevocations(TimeProvider clock)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _revoked = new(StringComparer.Ordinal);
    private DateTimeOffset _nextPrune = DateTimeOffset.MinValue;

    public void Revoke(string sid)
    {
        var now = clock.GetUtcNow();
        _revoked[sid] = now.AddHours(13);
        if (now > _nextPrune)
        {
            foreach (var kv in _revoked.Where(kv => kv.Value < now)) _revoked.TryRemove(kv.Key, out _);
            _nextPrune = now.AddHours(1);
        }
    }

    public bool IsRevoked(string sid) => _revoked.TryGetValue(sid, out var until) && until > clock.GetUtcNow();
}
