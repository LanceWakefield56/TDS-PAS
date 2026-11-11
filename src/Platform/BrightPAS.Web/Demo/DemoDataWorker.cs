using System.Text.Json;
using BrightPAS.Core.Setup;

namespace BrightPAS.Web.Demo;

/// <summary>
/// Loads the demonstration data once, when Setup has asked for it (setting demo.state = requested on a new database).
/// Runs in the background so the website starts normally; Setup waits for demo.state to become done or failed.
/// </summary>
public sealed class DemoDataWorker(IServiceScopeFactory scopes, ILogger<DemoDataWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await using var scope = scopes.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
            if (await settings.GetAsync(DemoDataSeeder.StateKey, stoppingToken) != "requested") return;
            await settings.SetAsync(DemoDataSeeder.StateKey, "running", DemoDataSeeder.Actor, stoppingToken);
            log.LogWarning("Loading DEMONSTRATION data into this database (requested by Setup). This takes a minute or two.");
            try
            {
                DemoDataSeeder.Summary summary;
                await using (var work = scopes.CreateAsyncScope())
                    summary = await work.ServiceProvider.GetRequiredService<DemoDataSeeder>().RunAsync(stoppingToken);
                await settings.SetAsync(DemoDataSeeder.DetailKey, JsonSerializer.Serialize(summary), DemoDataSeeder.Actor, stoppingToken);
                await settings.SetAsync(DemoDataSeeder.StateKey, "done", DemoDataSeeder.Actor, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Demonstration data could not be loaded");
                await using var s2 = scopes.CreateAsyncScope();
                var st = s2.ServiceProvider.GetRequiredService<SettingsService>();
                var text = ex.Message.Length > 900 ? ex.Message[..900] : ex.Message;
                await st.SetAsync(DemoDataSeeder.DetailKey, text, DemoDataSeeder.Actor, CancellationToken.None);
                await st.SetAsync(DemoDataSeeder.StateKey, "failed", DemoDataSeeder.Actor, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogError(ex, "Demonstration data check failed"); }
    }
}
