using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Licensing;
using BrightPAS.Modules.EBookings;
using BrightPAS.Modules.Reminders;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Emails the rooms contact when the booking office asks a question, accepts or declines (0.28.0, ADR 0041). The email carries only the request
/// number and asks them to open the portal – no patient details leave BrightPAS by email. Needs "Email the rooms" switched on and Reminders email set up.
/// </summary>
public sealed class EBookingEmailNotifier(EBookingsDbContext db, LicenceCache licence, IServiceProvider sp, ILogger<EBookingEmailNotifier> log) : IEBookingNotifier
{
    public async Task NotifyAsync(BookingRequest request, string what, CancellationToken ct)
    {
        if (request.ContactEmail is not { Length: > 0 } to) return;
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not { EmailRooms: true } || licence.Status.AccessTo(RemindersModule.Id) == ModuleAccess.None) return;
        var rs = await sp.GetRequiredService<ReminderService>().SettingsAsync(ct);
        var gw = sp.GetServices<IEmailGateway>().FirstOrDefault(g => g.Provider == rs.EmailProvider);
        if (gw is null || gw.ConfigurationProblem(rs) is { } problem) { log.LogInformation("Rooms email not sent for {Number}: email is not set up.", request.Number); return; }
        var (subject, line) = what switch
        {
            "question" => ($"Tasman Day Surgery has a question about booking request {request.Number}", "The booking office has asked a question about this booking request."),
            "accepted" => ($"Booking request {request.Number} accepted", "This booking request has been accepted onto the waitlist. We will confirm the date with you and the patient."),
            _ => ($"Booking request {request.Number} declined", "This booking request has been declined. The reason is in the portal."),
        };
        var body = $"{line}\n\nOpen the BrightPAS specialist portal and look for request {request.Number} to see the details.\n\n(For privacy, this email does not include patient details.)";
        var r = await gw.SendAsync(rs, to, subject, body, ct);
        if (!r.Ok) log.LogWarning("Could not email the rooms about {Number}: {Error}", request.Number, r.Error);
    }
}

/// <summary>
/// Imports booking files (BrightPAS JSON, HL7 REF^I12) that practice systems drop into the watched folder (share or USB), every two minutes.
/// A file is moved to "imported" when every booking in it was received, otherwise to "problems" with a .txt note beside it – never imported twice.
/// </summary>
public sealed class EBookingFolderWorker(IServiceScopeFactory scopes, LicenceCache licence, TimeProvider clock, ILogger<EBookingFolderWorker> log) : BackgroundService
{
    private static readonly string[] Extensions = [".json", ".hl7", ".txt", ".msg", ".ref"];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (licence.Status.AccessTo(EBookingsModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<EBookingService>();
                var s = await svc.SettingsAsync(stoppingToken);
                if (string.IsNullOrWhiteSpace(s.WatchFolder)) continue;
                await svc.RecordCheckAsync(await ScanAsync(scope.ServiceProvider.GetRequiredService<EBookingImporter>(), s.WatchFolder, stoppingToken), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Booking folder check failed; it will run again."); }
        }
    }

    public async Task<string?> ScanAsync(EBookingImporter importer, string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder)) return $"The booking folder {folder} can't be reached.";
        string? problem = null;
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
        {
            if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            var info = new FileInfo(path);
            if (clock.GetUtcNow() - info.LastWriteTimeUtc < TimeSpan.FromMinutes(1)) continue; // still being written
            if (info.Length > EBookingImporter.MaxBytes) { problem = $"{info.Name} is larger than 60 MB."; Move(path, "problems", problem); continue; }
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var r = await importer.ImportAsync(info.Name, bytes, "", ct);
            log.LogInformation("Booking file {File}: {Received} received ({Numbers}), {Problems} problem(s).", info.Name, r.Received, string.Join(", ", r.Numbers), r.Problems.Count);
            if (r.Problems.Count > 0) problem = $"{info.Name}: {string.Join(" ", r.Problems)}";
            Move(path, r.Problems.Count == 0 ? "imported" : "problems", r.Problems.Count == 0 ? null : $"Received: {string.Join(", ", r.Numbers)}\n" + string.Join("\n", r.Problems));
        }
        return problem;
    }

    private void Move(string path, string sub, string? note)
    {
        try
        {
            var dir = Path.Combine(Path.GetDirectoryName(path)!, sub);
            Directory.CreateDirectory(dir);
            var target = Path.Combine(dir, $"{clock.GetUtcNow().ToNz():yyyyMMdd-HHmmss}-{Path.GetFileName(path)}");
            File.Move(path, target);
            if (note is not null) File.WriteAllText(target + ".problem.txt", note);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(ex, "Could not move booking file {File} to {Folder}; renaming it so it is not imported twice.", path, sub);
            try { File.Move(path, path + ".done"); } catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException) { log.LogError(ex2, "Could not rename {File}.", path); }
        }
    }
}
