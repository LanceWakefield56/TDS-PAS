namespace Tds.Pas.Setup.Install;

public sealed record StepRecord(IInstallStep Step, StepResult Result);

/// <summary>Runs the platform installation steps in order and stops at the first failure.</summary>
public static class Installer
{
    public static IReadOnlyList<IInstallStep> PlatformSteps() =>
    [
        new CheckPrerequisitesStep(), new InstallIisStep(), new HardenTlsStep(), new HardenIisStep(),
        new CreateDatabaseStep(), new ApplySchemaStep(), new BootstrapCodeStep()
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
            StepResult result;
            try { result = await step.RunAsync(c, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = StepResult.Fail($"Unexpected error: {ex.Message}");
            }
            records.Add(new StepRecord(step, result));
            c.Log.Info($"  -> {Label(result.Outcome)}: {result.Message}");
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
