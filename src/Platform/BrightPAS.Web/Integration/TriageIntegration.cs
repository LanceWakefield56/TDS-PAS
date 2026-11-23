using BrightPAS.Core.Modules;
using BrightPAS.Licensing;
using BrightPAS.Modules.Triage;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>Holds a theatre booking at "Booked" until triage is signed off as suitable (when the triage module is licensed and the setting is on).</summary>
public sealed class TriageBookingGate(LicenceCache licence, TriageService triage) : IBookingConfirmGate
{
    public Task<string?> CheckAsync(Guid bookingId, Guid patientId, string procedure, CancellationToken ct) =>
        licence.Status.AccessTo(TriageModule.Id) == ModuleAccess.Full ? triage.CanConfirmAsync(bookingId, patientId, procedure, ct) : Task.FromResult<string?>(null);
}

/// <summary>Starts triage for new bookings and re-triage when new answers or results arrive or a booking changes (every 5 minutes).</summary>
public sealed class TriageWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<TriageWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            if (licence.Status.AccessTo(TriageModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var n = await scope.ServiceProvider.GetRequiredService<TriageService>().ScanAsync(stoppingToken);
                if (n > 0) log.LogInformation("Triage: {Count} assessment(s) started or re-run.", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Triage scan failed; it will run again in 5 minutes."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
