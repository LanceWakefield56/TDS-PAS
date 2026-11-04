namespace Galenica.Setup.Checks;

public enum CheckStatus { Pass, Warning, Fail, Skipped }

public sealed record CheckResult(CheckStatus Status, string Detail, string? Fix = null)
{
    public static CheckResult Pass(string detail) => new(CheckStatus.Pass, detail);
    public static CheckResult Warn(string detail, string fix) => new(CheckStatus.Warning, detail, fix);
    public static CheckResult Fail(string detail, string fix) => new(CheckStatus.Fail, detail, fix);
    public static CheckResult Skip(string detail) => new(CheckStatus.Skipped, detail);
}

/// <summary>Settings the checks need, gathered from command-line options or the guided prompts.</summary>
public sealed record SetupContext(string InstallDrive, string? SqlServerHost, int SqlServerPort = 1433);

/// <summary>One prerequisite check shown to the installer user in plain English.</summary>
public interface IPrerequisiteCheck
{
    string Name { get; }
    string Category { get; }
    CheckResult Run(SetupContext context, ISystemProbe probe);
}
