using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Modules;
using BrightPAS.Licensing;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Forms;
using BrightPAS.Modules.Medicines;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Triage;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Gathers what BrightPAS knows about a patient for the medicine checks (0.27.0, ADR 0040): age from the date of birth, active allergy alerts,
/// medicines given in the last 24 hours (all episodes, including those filed from SaferSleep), weight (latest triage, else the questionnaire)
/// and the medicines the patient listed in the questionnaire.
/// </summary>
public sealed class MedicineFactsProvider(PatientsDbContext patients, EmrDbContext emr, FormsDbContext forms, TriageDbContext triage, TimeProvider clock)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<PatientFacts> ForAsync(Guid patientId, DateTimeOffset at, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().Include(x => x.Alerts).SingleOrDefaultAsync(x => x.Id == patientId, ct) ?? throw new KeyNotFoundException("Patient not found.");
        var age = NzTime.Age(p.DateOfBirth, at.NzDate());
        var allergies = p.Alerts.Where(a => a.IsActive && a.Type == AlertType.Allergy).Select(a => (a.Id, a.Description)).ToList();
        var since = at.AddHours(-24);
        var given = await (from m in emr.Medications.AsNoTracking()
                           join e in emr.Episodes.AsNoTracking() on m.EpisodeId equals e.Id
                           where e.PatientId == patientId && m.GivenAt > since && m.GivenAt <= at
                           orderby m.GivenAt
                           select new { m.Medicine, m.Dose, m.Route, m.GivenAt }).ToListAsync(ct);

        decimal? weight = null; string? weightSource = null;
        var facts = await triage.Assessments.AsNoTracking().Where(a => a.PatientId == patientId && a.Status != AssessmentStatus.Cancelled)
            .OrderByDescending(a => a.CreatedAt).Select(a => new { a.FactsJson, a.CreatedAt }).FirstOrDefaultAsync(ct);
        if (facts is not null && Weight(facts.FactsJson) is { } tw) { weight = tw; weightSource = $"triage {facts.CreatedAt.NzDate().ToString("d MMM yyyy", Inv)}"; }
        var form = await forms.Requests.AsNoTracking().Include(r => r.Answers)
            .Where(r => r.PatientId == patientId && (r.Status == FormStatus.Submitted || r.Status == FormStatus.Triaged))
            .OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(ct);
        if (weight is null && form?.Answers.FirstOrDefault(a => a.QuestionKey == "weight_kg") is { } wa && decimal.TryParse(wa.Value, NumberStyles.Number, Inv, out var fw) && fw is > 0 and < 400)
        { weight = fw; weightSource = $"questionnaire {form.SubmittedAt?.NzDate().ToString("d MMM yyyy", Inv)}"; }
        var regular = form?.Answers.FirstOrDefault(a => a.QuestionKey == "medications")?.Value;
        return new(age, weight, weightSource, allergies, [.. given.Select(g => (g.Medicine, g.Dose, g.Route, g.GivenAt))], regular);
    }

    private static decimal? Weight(string json)
    {
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web);
            return d is not null && d.TryGetValue("weight_kg", out var v) && decimal.TryParse(v, NumberStyles.Number, Inv, out var w) && w is > 0 and < 400 ? w : null;
        }
        catch (JsonException) { return null; }
    }

    public DateTimeOffset Now => clock.GetUtcNow();
}

/// <summary>The clinical record's medicine check (only when the Medicines module is licensed).</summary>
public sealed class MedicineSafetyGate(LicenceCache licence, MedicineSafetyService safety, MedicineFactsProvider facts) : IMedicineSafetyCheck
{
    public async Task<MedicineCheckOutcome> CheckAsync(MedicineGiveCheck c, CancellationToken ct)
    {
        if (licence.Status.AccessTo(MedicinesModule.Id) != ModuleAccess.Full) return new(true, null, null, []);
        var f = await facts.ForAsync(c.PatientId, c.At, ct);
        var r = await safety.CheckAsync(new SafetyRequest(c.PatientId, c.EpisodeId, c.Medicine, c.Dose, c.Route, c.At, c.Barcode, c.MedicineId, c.WeightKg, c.OverrideReason, c.User, Record: true), f, ct);
        return new(r.Allowed, r.Error, r.CheckId, [.. r.Findings.Select(x => new MedicineCheckNote(x.Kind.ToString(), x.Severity.ToString(), x.Text))]);
    }

    public Task LinkAsync(Guid checkId, Guid medicationId, CancellationToken ct) => safety.LinkCheckAsync(checkId, medicationId, ct);
}

/// <summary>
/// Downloads the NZ Formulary PDFs on the first working day of each month (when set up with the PDF addresses and switched on) and imports
/// them; tries again every 6 hours until this month's release is in. Problems are shown on Medicines › NZ Formulary.
/// </summary>
public sealed class NzfWorker(IServiceScopeFactory scopes, LicenceCache licence, IHttpClientFactory http, TimeProvider clock, ILogger<NzfWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (licence.Status.AccessTo(MedicinesModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var nzf = scope.ServiceProvider.GetRequiredService<NzfService>();
                var s = await nzf.SettingsAsync(stoppingToken);
                if (!string.IsNullOrWhiteSpace(s.WatchFolder)) await WatchFolderAsync(nzf, s.WatchFolder, stoppingToken);
                if (!s.AutoDownload || (s.LastCheckedAt is { } last && clock.GetUtcNow() - last < TimeSpan.FromHours(6))) continue;
                var today = clock.GetUtcNow().NzDate();
                string? problem = null;
                foreach (var (edition, url) in new[] { ("NZF", s.NzfUrl), ("NZFC", s.NzfcUrl) })
                {
                    if (string.IsNullOrWhiteSpace(url) || !await nzf.DueAsync(edition, today, stoppingToken)) continue;
                    try
                    {
                        var client = http.CreateClient("nzf");
                        using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
                        resp.EnsureSuccessStatusCode();
                        if (resp.Content.Headers.ContentLength > NzfPdf.MaxBytes) throw new InvalidOperationException("The PDF is larger than 120 MB.");
                        var bytes = await resp.Content.ReadAsByteArrayAsync(stoppingToken);
                        var r = await nzf.ImportAsync(Path.GetFileName(new Uri(url).AbsolutePath) is { Length: > 0 } f ? f : edition + ".pdf", bytes, "nzf-download", stoppingToken);
                        log.LogInformation("NZF download: {Edition} release {Release} – {Monographs} monographs{Same}.", r.Edition, r.Release, r.Monographs, r.AlreadyImported ? " (already imported – not yet updated)" : "");
                        if (r.AlreadyImported) problem = $"{edition}: the file at the address has not changed yet – BrightPAS will try again.";
                    }
                    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
                    {
                        problem = $"{edition}: {ex.Message}";
                        log.LogWarning(ex, "NZF download of {Edition} failed; trying again in 6 hours.", edition);
                    }
                }
                await nzf.RecordCheckAsync(problem, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "NZF check failed; it will run again."); }
        }
    }

    /// <summary>Imports any new PDF in the watched folder (share or USB), then moves it to "imported" (left in place if it can't be moved – it is not imported twice).</summary>
    private async Task WatchFolderAsync(NzfService nzf, string folder, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(folder)) { await nzf.RecordCheckAsync($"The NZF folder {folder} can't be reached.", ct); return; }
            foreach (var path in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(path);
                if (info.Length > NzfPdf.MaxBytes || clock.GetUtcNow() - info.LastWriteTimeUtc < TimeSpan.FromMinutes(2)) continue; // still copying
                var bytes = await File.ReadAllBytesAsync(path, ct);
                string? problem = null;
                if (!await nzf.HaveAsync(bytes, ct))
                {
                    try
                    {
                        var r = await nzf.ImportAsync(info.Name, bytes, "nzf-folder", ct);
                        log.LogInformation("NZF folder: imported {Edition} release {Release} from {File}.", r.Edition, r.Release, info.Name);
                    }
                    catch (InvalidOperationException ex) { problem = $"{info.Name}: {ex.Message}"; }
                }
                if (problem is not null) { await nzf.RecordCheckAsync(problem, ct); continue; }
                try
                {
                    var done = Path.Combine(folder, "imported");
                    Directory.CreateDirectory(done);
                    File.Move(path, Path.Combine(done, $"{Path.GetFileNameWithoutExtension(info.Name)}-{clock.GetUtcNow():yyyyMMddHHmm}.pdf"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning(ex, "NZF folder: {File} imported but could not be moved.", info.Name); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await nzf.RecordCheckAsync($"The NZF folder {folder} can't be read: {ex.Message}", ct); }
    }
}
