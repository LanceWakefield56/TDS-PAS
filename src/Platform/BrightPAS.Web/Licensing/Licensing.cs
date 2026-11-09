using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Setup;
using BrightPAS.Licensing;

namespace BrightPAS.Web.Licensing;

/// <summary>The current licence status, shared by every request (refreshed at start-up, every 15 minutes and on change).</summary>
public sealed class LicenceCache
{
    private volatile LicenceStatus _status = LicenceStatus.None("Licence not checked yet.");
    public LicenceStatus Status => _status;
    public string InstallationCode { get; private set; } = "";
    public DateTimeOffset CheckedAt { get; private set; }
    internal void Set(LicenceStatus s, string code, DateTimeOffset at) { _status = s; InstallationCode = code; CheckedAt = at; }
}

public sealed class LicensingService(PasDbContext db, LicenceCache cache, TimeProvider clock, IConfiguration config,
    IHttpClientFactory httpFactory, AuditWriter audit, ILogger<LicensingService> log)
{
    private IReadOnlyList<TrustedKey> Keys => VendorKeys.BuiltIn(config.GetValue("Licensing:AllowDevelopmentKey", true));

    public async Task<LicenceState> StateAsync(CancellationToken ct = default)
    {
        var s = await db.LicenceState.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is null)
        {
            s = new LicenceState();
            db.LicenceState.Add(s);
            await db.SaveChangesAsync(ct);
        }
        return s;
    }

    public string InstallationCode(LicenceState s) =>
        BrightPAS.Licensing.InstallationIdentity.Code(s.InstallationId, config["Licensing:MachineId"] ?? BrightPAS.Licensing.InstallationIdentity.MachineId());

    private LicenceStatus Evaluate(LicenceState s, string code, DateTimeOffset now, string? text = null) =>
        LicenceEvaluator.Evaluate(new LicenceEvaluator.Input(text ?? s.LicenceText, code, now, s.LastSeenAt, s.ActivationText, s.LicenceInstalledAt,
            s.LastKnownModules?.Split(',', StringSplitOptions.RemoveEmptyEntries)), Keys);

    /// <summary>Re-checks the licence and updates the shared status. With <paramref name="heartbeat"/>, also records "last seen" for clock checks.</summary>
    /// <summary>
    /// Test builds: licence keys are not enforced (Licensing:Enforce = false, the default until BrightPAS is near production) –
    /// every built module is available with no licence file or activation, and a banner says so. Set Licensing:Enforce = true to check licences again.
    /// </summary>
    public bool Enforced => config.GetValue("Licensing:Enforce", false);

    public static LicenceStatus TestBuildStatus() => new(LicenceHealth.Valid, null,
        ProductModules.All.Where(m => m.Available).ToDictionary(m => m.Id, _ => ModuleAccess.Full, StringComparer.OrdinalIgnoreCase),
        ["Test build: licence keys are switched off and every module is available. Not for real patient data."], false);

    public async Task<LicenceStatus> RefreshAsync(bool heartbeat = false, CancellationToken ct = default)
    {
        var s = await StateAsync(ct);
        var now = clock.GetUtcNow();
        var code = InstallationCode(s);
        if (!Enforced)
        {
            var open = TestBuildStatus();
            cache.Set(open, code, now);
            return open;
        }
        var status = Evaluate(s, code, now);
        if (status.Licence is not null && status.Health is not (LicenceHealth.Invalid or LicenceHealth.WrongInstallation or LicenceHealth.ClockProblem))
            s.LastKnownModules = string.Join(',', status.Licence.Modules);
        if (heartbeat && status.Health != LicenceHealth.ClockProblem && (s.LastSeenAt is null || now > s.LastSeenAt)) s.LastSeenAt = now;
        await db.SaveChangesAsync(ct);
        cache.Set(status, code, now);
        return status;
    }

    public async Task<(bool Ok, string? Error, LicenceStatus Status)> InstallAsync(string text, string user, string? workstation, CancellationToken ct = default)
    {
        var s = await StateAsync(ct);
        var code = InstallationCode(s);
        var check = LicenceCodec.VerifyLicence(text, Keys);
        if (!check.Ok) return (false, check.Error, cache.Status);
        var trial = Evaluate(s, code, clock.GetUtcNow(), text);
        if (trial.Health is LicenceHealth.WrongInstallation or LicenceHealth.Expired)
            return (false, trial.Messages.Last(), cache.Status);

        s.LicenceText = text;
        s.LicenceInstalledAt = clock.GetUtcNow();
        s.LicenceInstalledBy = user;
        s.ActivationText = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "LicenceInstalled", "Licence", check.Value!.LicenceId,
            reason: $"{check.Value.Edition}; modules {string.Join(",", check.Value.Modules)}; expires {check.Value.ExpiresOn:yyyy-MM-dd}", workstation: workstation, ct: ct);
        var status = await RefreshAsync(ct: ct);
        if (status.Licence?.Activation is not null) await ActivateAsync(user, ct);
        return (true, null, cache.Status);
    }

    /// <summary>Confirms the licence with the vendor's licence server and stores the signed reply.</summary>
    public async Task<(bool Ok, string Message)> ActivateAsync(string user, CancellationToken ct = default)
    {
        var s = await StateAsync(ct);
        var status = cache.Status.Licence is null ? await RefreshAsync(ct: ct) : cache.Status;
        var act = status.Licence?.Activation;
        if (act is null) return (false, "This licence does not use online activation.");
        var code = InstallationCode(s);
        string message;
        var ok = false;
        try
        {
            var client = httpFactory.CreateClient("licensing");
            var res = await client.PostAsJsonAsync(new Uri(new Uri(act.Url.TrimEnd('/') + "/"), "api/activate"),
                new { licenceId = status.Licence!.LicenceId, installationCode = code, product = "BrightPAS", version = typeof(LicensingService).Assembly.GetName().Version?.ToString() }, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            if (res.IsSuccessStatusCode && LicenceCodec.VerifyActivation(body, Keys) is { Ok: true } v
                && v.Value!.LicenceId == status.Licence.LicenceId && LicenceEvaluator.Normalise(v.Value.InstallationCode) == LicenceEvaluator.Normalise(code))
            {
                s.ActivationText = body; s.ActivationError = null; ok = true;
                message = $"Licence confirmed online until {v.Value.ValidUntil:d MMMM yyyy}.";
            }
            else message = $"The licence server refused the activation ({(int)res.StatusCode}): {Trim(body)}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            message = $"Could not reach the licence server {act.Url}: {ex.Message}";
        }
        s.ActivationCheckedAt = clock.GetUtcNow();
        if (!ok) { s.ActivationError = message; log.LogWarning("Licence activation failed: {Message}", message); }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, ok ? "LicenceActivated" : "LicenceActivationFailed", "Licence", status.Licence!.LicenceId, reason: Trim(message), ct: ct);
        await RefreshAsync(ct: ct);
        return (ok, message);
    }

    public async Task<bool> ActivationDueAsync(CancellationToken ct)
    {
        var act = cache.Status.Licence?.Activation;
        if (act is null) return false;
        var s = await StateAsync(ct);
        return s.ActivationCheckedAt is null || s.ActivationCheckedAt < clock.GetUtcNow().AddDays(-act.CheckInDays)
            || (s.ActivationError is not null && s.ActivationCheckedAt < clock.GetUtcNow().AddHours(-6));
    }

    private static string Trim(string s) => s.Length > 400 ? s[..400] : s;
}

/// <summary>Re-checks the licence every 15 minutes, records "last seen", and checks in online when due.</summary>
public sealed class LicenceHeartbeat(IServiceScopeFactory scopes, ILogger<LicenceHeartbeat> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<LicensingService>();
                await svc.RefreshAsync(heartbeat: true, stoppingToken);
                if (await svc.ActivationDueAsync(stoppingToken)) await svc.ActivateAsync("system", stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Licence check failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public static class LicensingEndpointExtensions
{
    /// <summary>
    /// Blocks an endpoint (or group) unless the module is licensed. Read-only access (expired/problem licence) still allows GET,
    /// so clinical data stays viewable.
    /// </summary>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string moduleId) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequiresModuleAttribute(moduleId)).AddEndpointFilter(async (ctx, next) =>
        {
            var status = ctx.HttpContext.RequestServices.GetRequiredService<LicenceCache>().Status;
            var name = ProductModules.Find(moduleId)?.Name ?? moduleId;
            return status.AccessTo(moduleId) switch
            {
                ModuleAccess.None => Results.Json(new { error = "module_not_licensed", module = moduleId, message = $"The {name} module is not licensed on this system." }, statusCode: 403),
                ModuleAccess.ReadOnly when !HttpMethods.IsGet(ctx.HttpContext.Request.Method) && !HttpMethods.IsHead(ctx.HttpContext.Request.Method) =>
                    Results.Json(new { error = "licence_read_only", module = moduleId, message = $"The {name} module is read-only: {status.Messages.LastOrDefault()}" }, statusCode: 403),
                _ => await next(ctx)
            };
        });
}

/// <summary>Marks an endpoint as belonging to a licensed module (checked by the endpoint security inventory test).</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresModuleAttribute(string moduleId) : Attribute
{
    public string ModuleId { get; } = moduleId;
}
