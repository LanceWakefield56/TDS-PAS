using BrightPAS.Licensing;
using BrightPAS.Modules.Letters;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>Reads HealthLink acknowledgements from the inbox folder every minute (FolderDrop mode) while the Letters module is licensed.</summary>
public sealed class HealthLinkWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<HealthLinkWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (licence.Status.AccessTo(LettersModule.Id) == ModuleAccess.None) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<HealthLinkService>().ProcessAcknowledgementsAsync(stoppingToken);
                if (n > 0) log.LogInformation("HealthLink: {Count} acknowledgement(s) processed.", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "HealthLink acknowledgement check failed; it will run again in a minute."); }
        }
    }
}
