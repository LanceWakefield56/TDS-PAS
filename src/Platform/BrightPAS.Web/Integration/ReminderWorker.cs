using Microsoft.AspNetCore.DataProtection;
using BrightPAS.Licensing;
using BrightPAS.Modules.Reminders;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>Encrypts vendor passwords with ASP.NET Core Data Protection (keys outside the web folder, DPAPI-protected on Windows).</summary>
public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _p = provider.CreateProtector("BrightPAS.Reminders.Secrets.v1");
    public string Protect(string plain) => _p.Protect(plain);
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue)) return null;
        try { return _p.Unprotect(protectedValue); }
        catch (System.Security.Cryptography.CryptographicException) { return null; } // keys lost: the secret must be re-entered
    }
}

/// <summary>Runs the reminders cycle every minute (queue confirmations and reminders, send due messages) while the module is licensed.</summary>
public sealed class ReminderWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<ReminderWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            if (licence.Status.AccessTo(RemindersModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var (queued, sent, failed) = await scope.ServiceProvider.GetRequiredService<ReminderService>().RunCycleAsync(stoppingToken);
                if (queued + sent + failed > 0) log.LogInformation("Reminders: {Queued} queued, {Sent} sent, {Failed} failed.", queued, sent, failed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Reminders cycle failed; it will run again in a minute."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
