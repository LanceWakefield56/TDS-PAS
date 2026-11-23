using System.Globalization;
using System.Text.Json;
using BrightPAS.Licensing;
using BrightPAS.Modules.Assets;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.FollowUp;
using BrightPAS.Modules.Letters;
using BrightPAS.Modules.Theatre;
using BrightPAS.Modules.Triage;
using BrightPAS.Web.Licensing;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Web.Integration;

/// <summary>
/// The operation's facts for follow-up and SSI surveillance: implant (stock), wound class, skin prep, duration, prophylactic antibiotics and timing,
/// recovery temperature (clinical record), ASA/BMI/diabetes and precautions (triage), and the surgeon. Each source is used only when licensed.
/// </summary>
public sealed class OperationFactsProvider(LicenceCache licence, TheatreDbContext theatre, TheatreService theatreService, EmrDbContext emr, AssetsDbContext assets, TriageDbContext triage) : IOperationFacts
{
    private static readonly string[] Antibiotics = ["cefazolin", "cephazolin", "cefuroxime", "gentamicin", "metronidazole", "clindamycin", "vancomycin", "amoxicillin", "augmentin", "flucloxacillin", "ceftriaxone", "teicoplanin"];
    private bool On(string m) => licence.Status.AccessTo(m) != ModuleAccess.None;

    public async Task<OperationFacts> ForBookingAsync(Guid bookingId, CancellationToken ct)
    {
        var risk = new Dictionary<string, string>(StringComparer.Ordinal);
        var b = await (from x in theatre.Bookings.AsNoTracking() join s in theatre.Sessions.AsNoTracking() on x.SessionId equals s.Id where x.Id == bookingId select new { x.Procedure, x.DurationMinutes, s.SurgeonId }).SingleOrDefaultAsync(ct);
        string? surgeon = null;
        if (b is not null) { surgeon = (await theatreService.ClinicianNamesAsync(ct)).GetValueOrDefault(b.SurgeonId); risk["planned_minutes"] = b.DurationMinutes.ToString(CultureInfo.InvariantCulture); }
        var implant = On("assets") && await assets.Implants.AsNoTracking().AnyAsync(i => i.BookingId == bookingId, ct);
        string? woundClass = null;
        if (On("emr") && await emr.Episodes.AsNoTracking().FirstOrDefaultAsync(e => e.BookingId == bookingId, ct) is { } ep)
        {
            var io = await emr.Intraop.AsNoTracking().FirstOrDefaultAsync(i => i.EpisodeId == ep.Id, ct);
            if (io is not null)
            {
                woundClass = io.WoundClass;
                if (io.SkinPrep is { } sp) risk["skin_prep"] = sp;
                if (io.KnifeToSkinAt is { } k && io.ClosureAt is { } c) risk["duration_minutes"] = ((int)(c - k).TotalMinutes).ToString(CultureInfo.InvariantCulture);
                var meds = await emr.Medications.AsNoTracking().Where(m => m.EpisodeId == ep.Id).ToListAsync(ct);
                var ab = meds.Where(m => Antibiotics.Any(a => m.Medicine.Contains(a, StringComparison.OrdinalIgnoreCase))).OrderBy(m => m.GivenAt).FirstOrDefault();
                risk["antibiotic_prophylaxis"] = ab is null ? "none recorded" : ab.Medicine;
                if (ab is not null && io.KnifeToSkinAt is { } knife) risk["antibiotic_minutes_before_incision"] = ((int)(knife - ab.GivenAt).TotalMinutes).ToString(CultureInfo.InvariantCulture);
            }
            var temps = await emr.Observations.AsNoTracking().Where(o => o.EpisodeId == ep.Id && o.TemperatureC != null).OrderBy(o => o.At).Select(o => o.TemperatureC!.Value).ToListAsync(ct);
            if (temps.Count > 0) risk["first_recovery_temp_c"] = temps[0].ToString("0.0", CultureInfo.InvariantCulture);
        }
        int? asa = null; var high = false;
        if (On("triage") && await triage.Assessments.AsNoTracking().Where(a => a.BookingId == bookingId && a.Status != AssessmentStatus.Superseded).OrderByDescending(a => a.CreatedAt).FirstOrDefaultAsync(ct) is { } t)
        {
            asa = t.ConfirmedAsa ?? t.SuggestedAsa;
            if (t.Bmi is { } bmi) risk["bmi"] = bmi.ToString("0.0", CultureInfo.InvariantCulture);
            var facts = JsonSerializer.Deserialize<Dictionary<string, string>>(t.FactsJson, JsonSerializerOptions.Web) ?? [];
            var diabetes = facts.TryGetValue("diabetes", out var d) && d.Equals("yes", StringComparison.OrdinalIgnoreCase);
            if (diabetes) risk["diabetes"] = "yes";
            if (t.Precautions is { } p) risk["precautions"] = p.Replace('\n', ';');
            high = (t.FinalOutcome ?? t.SuggestedOutcome) == TriageOutcome.SuitableWithPrecautions || asa >= 3 || t.Bmi >= 35 || diabetes;
        }
        if (implant) risk["implant"] = "yes";
        return new OperationFacts(implant, high || implant, surgeon, woundClass, asa, risk);
    }
}

/// <summary>Every 5 minutes: follow-up contacts for discharged patients, check-in links, unanswered links to phone calls, GP surveillance letters.</summary>
public sealed class FollowUpWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<FollowUpWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        do
        {
            if (licence.Status.AccessTo(FollowUpModule.Id) != ModuleAccess.Full) continue;
            try { await using var scope = scopes.CreateAsyncScope(); await RunAsync(scope.ServiceProvider, licence, log, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Follow-up scan failed; it will run again in 5 minutes."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static async Task RunAsync(IServiceProvider sp, LicenceCache licence, ILogger log, CancellationToken ct)
    {
        var svc = sp.GetRequiredService<FollowUpService>();
        var (created, linked, converted, newBookings) = await svc.ScanAsync(ct);
        if (created + linked + converted > 0) log.LogInformation("Follow-up: {Created} plan(s) started, {Linked} check-in link(s) sent, {Converted} changed to phone calls.", created, linked, converted);
        if (newBookings.Count == 0 || !(await svc.SettingsAsync(ct)).GpLetter || licence.Status.AccessTo("letters") != ModuleAccess.Full) return;
        var letters = sp.GetRequiredService<LetterService>();
        var surveyed = await sp.GetRequiredService<FollowUpDbContext>().Contacts.AsNoTracking().Where(c => newBookings.Contains(c.BookingId) && c.Kind == ContactKind.SsiCheck).Select(c => new { c.BookingId, c.PatientId }).Distinct().ToListAsync(ct);
        foreach (var x in surveyed)
        {
            var (letter, errors) = await letters.CreateDraftAsync(new NewLetter(x.PatientId, "GPSSI", x.BookingId, null), "system", null, ct);
            if (letter is null) log.LogWarning("GP surveillance letter not created for booking {Booking}: {Errors}", x.BookingId, string.Join("; ", errors));
        }
    }
}
