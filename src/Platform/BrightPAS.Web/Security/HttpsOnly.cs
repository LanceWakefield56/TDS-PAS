using System.Text.RegularExpressions;

namespace BrightPAS.Web.Security;

/// <summary>
/// BrightPAS never serves anything over plain HTTP. A GET or HEAD to http:// is answered with a permanent redirect to the same address
/// on https:// (so typing "BRIGHTPAS-SRV" in the browser lands on the secure page); every other request is refused. HSTS (set on HTTPS
/// responses) then makes the browser use https:// directly from the next visit.
/// Replaces UseHttpsRedirection, which silently does nothing under IIS when it cannot work out the HTTPS port.
/// </summary>
public static partial class HttpsOnly
{
    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,253}$")] private static partial Regex HostRx();

    public static IApplicationBuilder UseHttpsOnly(this IApplicationBuilder app, int httpsPort) => app.Use(async (ctx, next) =>
    {
        if (ctx.Request.IsHttps) { await next(); return; }
        var host = ctx.Request.Host.Host;
        if (!HostRx().IsMatch(host)) { ctx.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        ctx.Response.Headers.CacheControl = "no-store";
        if (HttpMethods.IsGet(ctx.Request.Method) || HttpMethods.IsHead(ctx.Request.Method))
        {
            var port = httpsPort == 443 ? "" : ":" + httpsPort;
            ctx.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            ctx.Response.Headers.Location = $"https://{host}{port}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}";
            return;
        }
        // Never accept data over plain HTTP (sign-in details or patient information would travel unencrypted).
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsJsonAsync(new { error = "https_required", message = "BrightPAS only accepts secure (https://) connections." });
    });
}
