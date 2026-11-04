namespace Galenica.Setup.Install;

public sealed record StepRecord(IInstallStep Step, StepResult Result);

/// <summary>Runs the platform installation steps in order and stops at the first failure.</summary>
public static class Installer
{
    public static IReadOnlyList<IInstallStep> PlatformSteps() =>
    [
        new CheckPrerequisitesStep(), new InstallIisStep(), new HardenTlsStep(), new HardenIisStep(),
        new CreateDatabaseStep(), new ApplySchemaStep(), new BootstrapCodeStep(),
        new HostingBundleStep(), new AppDatabaseLoginStep(), new DeployWebFilesStep(), new ConfigureIisSiteStep(), new WebHealthCheckStep()
    ];

    public static async Task<IReadOnlyList<StepRecord>> RunAsync(IEnumerable<IInstallStep> steps, InstallContext c, CancellationToken ct = default)
    {
        var records = new List<StepRecord>();
        var list = steps.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var step = list[i];
            c.Log.Info($"\nStep {i + 1} of {list.Count}: {step.Name}");
            c.Log.Detail(step.Description);
            c.Log.Debug($"Step '{step.GetType().Name}' starting.");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            StepResult result;
            try { result = await step.RunAsync(c, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                c.Log.Error($"Unexpected error in step '{step.Name}': {ex.Message}", ex);
                result = StepResult.Fail($"Unexpected error: {ex.GetType().Name}: {ex.Message}");
            }
            watch.Stop();
            records.Add(new StepRecord(step, result));
            var line = $"  -> {Label(result.Outcome)}: {result.Message}";
            if (result.Outcome == StepOutcome.Failed) c.Log.Error(line); else c.Log.Info(line);
            c.Log.Debug($"Step '{step.GetType().Name}' finished: {result.Outcome} in {watch.Elapsed.TotalSeconds:0.0}s.");
            if (result.Outcome == StepOutcome.Failed) break;
        }
        return records;
    }

    public static string Label(StepOutcome o) => o switch
    {
        StepOutcome.Done => "DONE",
        StepOutcome.AlreadyDone => "ALREADY DONE",
        StepOutcome.Skipped => "SKIPPED",
        StepOutcome.WhatIf => "WOULD DO",
        _ => "FAILED"
    };
}
