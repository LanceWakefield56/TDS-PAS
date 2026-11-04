using System.Text;
using System.Text.RegularExpressions;

namespace Galenica.Setup.Install;

/// <summary>
/// Verbose installation log. Everything is written to the log file with a timestamp and level:
/// each step, every PowerShell script and its full output, every SQL batch, timings and full error
/// details (including stack traces). The console shows a readable summary (and everything with --verbose).
/// Secrets (passwords, one-time codes, connection-string passwords) are always masked.
/// </summary>
public sealed partial class InstallLog : IInstallLog, IDisposable
{
    private readonly StreamWriter? _file;
    private readonly bool _verbose;
    private readonly bool _console;
    private readonly List<string> _secrets = [];
    private readonly object _gate = new();
    public string? FilePath { get; }
    public List<string> Lines { get; } = [];
    public int Warnings { get; private set; }
    public int Errors { get; private set; }

    public InstallLog(string? filePath, bool verbose, bool console = true)
    {
        _verbose = verbose;
        _console = console;
        if (filePath is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _file = new StreamWriter(filePath, append: true, new UTF8Encoding(false)) { AutoFlush = true };
            FilePath = filePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_console) Console.WriteLine($"(Could not open log file {filePath}: {ex.Message})");
        }
    }

    /// <summary>Registers a value that must never appear in the log or on screen.</summary>
    public void AddSecret(string? secret)
    {
        if (!string.IsNullOrEmpty(secret) && secret.Length >= 4) lock (_gate) _secrets.Add(secret);
    }

    public string Mask(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string[] secrets;
        lock (_gate) secrets = [.. _secrets];
        foreach (var s in secrets.OrderByDescending(s => s.Length)) text = text.Replace(s, "********", StringComparison.Ordinal);
        return PasswordInConnectionString().Replace(text, "$1********");
    }

    public void Info(string message) => Write("INFO ", message, ConsoleColor.Gray, showOnConsole: true);
    public void Detail(string message) => Write("INFO ", "    " + message, ConsoleColor.DarkGray, showOnConsole: true);
    public void Debug(string message) => Write("DEBUG", message, ConsoleColor.DarkGray, showOnConsole: _verbose);
    public void Warn(string message) { Warnings++; Write("WARN ", message, ConsoleColor.Yellow, showOnConsole: true); }

    public void Error(string message, Exception? ex = null)
    {
        Errors++;
        Write("ERROR", message, ConsoleColor.Red, showOnConsole: true);
        if (ex is not null) Write("ERROR", "Exception details:" + Environment.NewLine + ex, ConsoleColor.Red, showOnConsole: _verbose);
    }

    /// <summary>Logs a block of text (script, output, SQL) line by line at DEBUG level.</summary>
    public void Block(string title, string text)
    {
        Debug($"--- {title} ---");
        foreach (var line in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n')) Debug("| " + line);
        Debug($"--- end {title} ---");
    }

    private void Write(string level, string message, ConsoleColor colour, bool showOnConsole)
    {
        message = Mask(message);
        lock (_gate)
        {
            foreach (var line in message.Replace("\r", string.Empty).Split('\n'))
            {
                var stamped = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {line}";
                Lines.Add(stamped);
                _file?.WriteLine(stamped);
            }
            if (_console && showOnConsole)
            {
                Console.ForegroundColor = colour;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }
    }

    public void Dispose() => _file?.Dispose();

    [GeneratedRegex(@"((?:Password|Pwd)\s*=\s*)[^;]*", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordInConnectionString();

    /// <summary>Default log folder: C:\ProgramData\Galenica\Logs on Windows, next to Setup elsewhere.</summary>
    public static string DefaultPath(string kind) =>
        Path.Combine(OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Galenica", "Logs")
            : AppContext.BaseDirectory, $"Galenica-{kind}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
}
