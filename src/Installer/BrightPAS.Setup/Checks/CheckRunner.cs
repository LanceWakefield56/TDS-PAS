using System.Text;

namespace BrightPAS.Setup.Checks;

public sealed record CheckOutcome(IPrerequisiteCheck Check, CheckResult Result);

public sealed record CheckReport(IReadOnlyList<CheckOutcome> Outcomes, DateTimeOffset RunAt, string Machine)
{
    public int Failures => Outcomes.Count(o => o.Result.Status == CheckStatus.Fail);
    public int Warnings => Outcomes.Count(o => o.Result.Status == CheckStatus.Warning);
    public bool ReadyToInstall => Failures == 0;
    /// <summary>0 = ready, 1 = ready with warnings, 2 = not ready.</summary>
    public int ExitCode => Failures > 0 ? 2 : Warnings > 0 ? 1 : 0;

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("BrightPAS – Setup prerequisite report");
        sb.AppendLine($"Machine: {Machine}    Run at: {RunAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(new string('-', 72));
        foreach (var o in Outcomes)
        {
            sb.AppendLine($"[{Label(o.Result.Status)}] {o.Check.Category} – {o.Check.Name}: {o.Result.Detail}");
            if (o.Result.Fix is not null) sb.AppendLine($"        What to do: {o.Result.Fix}");
        }
        sb.AppendLine(new string('-', 72));
        sb.AppendLine(ReadyToInstall
            ? (Warnings > 0 ? $"READY with {Warnings} warning(s)." : "READY – all checks passed.")
            : $"NOT READY – {Failures} problem(s) must be fixed first.");
        return sb.ToString();
    }

    public static string Label(CheckStatus s) => s switch
    {
        CheckStatus.Pass => " OK ",
        CheckStatus.Warning => "WARN",
        CheckStatus.Fail => "FAIL",
        _ => "SKIP"
    };
}

public static class CheckRunner
{
    public static CheckReport Run(IEnumerable<IPrerequisiteCheck> checks, SetupContext context, ISystemProbe probe, TimeProvider clock) =>
        new(checks.Select(c => new CheckOutcome(c, c.Run(context, probe))).ToList(), clock.GetLocalNow(), Environment.MachineName);
}
