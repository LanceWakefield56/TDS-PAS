using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace BrightPAS.Web.Security;

/// <summary>
/// Website logging and error trapping (0.12.2):
/// - daily log files in &lt;data&gt;\logs\BrightPAS-Web-YYYYMMDD.log (30 days, 20 MB per file), plus Windows Event Log for warnings and errors;
/// - every unexpected error gets a short reference shown to the user and written to the log with the full detail;
/// - start-up crashes, unobserved task errors and background-worker failures are always logged;
/// - no patient data: SQL parameter values are never logged, secret link tokens in URLs are masked and request bodies are not logged.
/// </summary>
public static partial class PasLogging
{
    public static string LogFolder(IConfiguration config)
    {
        if (config["Logging:File:Path"] is { Length: > 0 } p) return p;
        if (config["DataProtection:KeysPath"] is { Length: > 0 } keys && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(keys)) is { } data) return Path.Combine(data, "logs");
        return OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrightPAS", "logs")
            : Path.Combine(AppContext.BaseDirectory, "logs");
    }

    private const string Template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{Ref}: {Message:lj}{NewLine}{Exception}";

    public static void Configure(WebApplicationBuilder builder)
    {
        var folder = LogFolder(builder.Configuration);
        string? problem = null;
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem = ex.Message; folder = Path.Combine(Path.GetTempPath(), "BrightPAS-logs"); Directory.CreateDirectory(folder); }

        var level = builder.Configuration.GetValue("Logging:File:Level", LogEventLevel.Information);
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(folder, "BrightPAS-Web-.log"), outputTemplate: Template, rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: builder.Configuration.GetValue("Logging:File:RetainDays", 30), fileSizeLimitBytes: 20_000_000, rollOnFileSizeLimit: true, shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2))
            .CreateLogger();
        Log.Logger = logger;
        builder.Logging.AddSerilog(logger, dispose: true); // alongside the default providers (Windows Event Log on the server)
        builder.Services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "BrightPAS stopped because of an unexpected error.");
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "A background task failed without being handled.");
            e.SetObserved();
        };
        Log.Information("BrightPAS {Version} starting ({Environment}); logs in {Folder}{Problem}", typeof(PasLogging).Assembly.GetName().Version, builder.Environment.EnvironmentName, folder,
            problem is null ? "" : $" (could not use the configured folder: {problem})");
    }

    /// <summary>Last resort when start-up itself fails before logging is ready.</summary>
    public static void StartupFailed(Exception ex, IConfiguration? config)
    {
        try
        {
            var folder = config is null ? Path.Combine(Path.GetTempPath(), "BrightPAS-logs") : LogFolder(config);
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, $"BrightPAS-Web-startup-error-{DateTime.Now:yyyyMMdd}.log"), $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} BrightPAS could not start:{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception) { /* nothing more we can do; IIS also records it in the Windows Event Log */ }
        Log.Fatal(ex, "BrightPAS could not start.");
        Log.CloseAndFlush();
    }

    [GeneratedRegex(@"^(/api/(?:q|my|hooks/sms)/)[^/]+", RegexOptions.IgnoreCase)]
    private static partial Regex SecretSegment();

    /// <summary>The request path safe for the log: secret link tokens and callback keys are masked; query strings are never logged.</summary>
    public static string SafePath(PathString path) => SecretSegment().Replace(path.Value ?? "", "$1***");

    public static string Reference(HttpContext ctx) =>
        ctx.Items["pas-ref"] as string ?? (string)(ctx.Items["pas-ref"] = Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8]);

    /// <summary>Gives every request a reference, logs slow and failed requests, and turns unexpected errors into a friendly message with that reference.</summary>
    public static void UsePasErrorHandling(this WebApplication app)
    {
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BrightPAS.Requests");
        app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
        {
            var feature = ctx.Features.Get<IExceptionHandlerPathFeature>();
            var reference = Reference(ctx);
            log.LogError(feature?.Error, "Unexpected error {Reference} on {Method} {Path} for {User}", reference, ctx.Request.Method,
                SafePath(feature?.Path is { } p ? new PathString(p) : ctx.Request.Path), ctx.User.Identity?.Name ?? "anonymous");
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var message = $"Something went wrong. Please try again; if it keeps happening give IT support this reference: {reference}.";
            if (ctx.Request.Path.StartsWithSegments("/api"))
                await ctx.Response.WriteAsJsonAsync(new { error = "ServerError", message, reference, errors = new[] { message } });
            else
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.WriteAsync($"<!DOCTYPE html><html><head><title>BrightPAS</title></head><body><h1>Something went wrong</h1><p>{message}</p></body></html>");
            }
        }));
        app.Use(async (ctx, next) =>
        {
            var reference = Reference(ctx);
            using var _ = LogContext.PushProperty("Ref", " #" + reference);
            ctx.Response.Headers["X-BrightPAS-Ref"] = reference;
            var sw = Stopwatch.StartNew();
            await next();
            sw.Stop();
            var status = ctx.Response.StatusCode;
            if (status >= 500 || sw.ElapsedMilliseconds > 5000)
                log.LogWarning("{Method} {Path} answered {Status} in {Ms} ms for {User}", ctx.Request.Method, SafePath(ctx.Request.Path), status, sw.ElapsedMilliseconds, ctx.User.Identity?.Name ?? "anonymous");
            else if (log.IsEnabled(LogLevel.Debug))
                log.LogDebug("{Method} {Path} answered {Status} in {Ms} ms", ctx.Request.Method, SafePath(ctx.Request.Path), status, sw.ElapsedMilliseconds);
        });
    }

    public sealed record LogLine(string Time, string Level, string Source, string Text);

    [GeneratedRegex(@"^(?<t>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<l>[A-Z]{3})\] (?<s>[^:]*): (?<m>.*)$")]
    private static partial Regex LineRx();

    /// <summary>Recent log entries at or above a level (newest first), for the administrator's "Website log" screen.</summary>
    public static IReadOnlyList<LogLine> Recent(IConfiguration config, string minLevel, int take)
    {
        var order = new[] { "DBG", "INF", "WRN", "ERR", "FTL" };
        var min = Math.Max(0, Array.IndexOf(order, minLevel));
        var folder = LogFolder(config);
        if (!Directory.Exists(folder)) return [];
        var lines = new List<LogLine>();
        foreach (var file in Directory.GetFiles(folder, "BrightPAS-Web-*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
        {
            string[] all;
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                all = sr.ReadToEnd().Split('\n');
            }
            catch (IOException) { continue; }
            LogLine? current = null;
            var block = new List<LogLine>();
            foreach (var raw in all)
            {
                var line = raw.TrimEnd('\r');
                var m = LineRx().Match(line);
                if (m.Success)
                {
                    if (current is not null) block.Add(current);
                    current = new(m.Groups["t"].Value, m.Groups["l"].Value, m.Groups["s"].Value, m.Groups["m"].Value);
                }
                else if (current is not null && line.Length > 0 && current.Text.Length < 6000) current = current with { Text = current.Text + "\n" + line };
            }
            if (current is not null) block.Add(current);
            lines.AddRange(block.Where(l => Array.IndexOf(order, l.Level) >= min));
            if (lines.Count >= take) break;
        }
        return lines.OrderByDescending(l => l.Time, StringComparer.Ordinal).Take(take).ToList();
    }
}
