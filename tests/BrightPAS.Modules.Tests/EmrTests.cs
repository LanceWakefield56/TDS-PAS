using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Emr;

namespace BrightPAS.Modules.Tests;

/// <summary>0.14.0 clinical record: risk scores, checklist hard stops, counts, early warning score, controlled drugs, discharge hard stop.</summary>
public class EmrTests
{
    private static Dictionary<string, string> D(params (string, string)[] x) => x.ToDictionary(p => p.Item1, p => p.Item2);
    private static readonly Dictionary<string, string> Team3 = new() { ["Surgeon"] = "Dr Hall", ["Anaesthetist"] = "Dr Ford", ["Nurse"] = "RN Kim" };

    [Fact]
    public void Risk_screens_score_as_documented()
    {
        Assert.Equal("High", EmrService.Score(AssessmentKind.Vte, D(("age", "70"), ("major_surgery", "yes"), ("previous_vte", "yes"))).Risk); // 2+2+3
        Assert.Equal("Low", EmrService.Score(AssessmentKind.Vte, D(("age", "30"))).Risk);
        var braden = EmrService.Score(AssessmentKind.PressureInjury, D(("sensory", "2"), ("moisture", "2"), ("activity", "2"), ("mobility", "2"), ("nutrition", "2"), ("friction", "1")));
        Assert.Equal(11, braden.Score); Assert.Equal("High", braden.Risk);
        Assert.Equal("Moderate", EmrService.Score(AssessmentKind.Frailty, D(("cfs", "5"))).Risk);
        Assert.Equal("High", EmrService.Score(AssessmentKind.Delirium, D(("alertness", "4"))).Risk);
        Assert.Equal("High", EmrService.Score(AssessmentKind.Falls, D(("fallen_last_year", "yes"), ("age", "80"))).Risk);
    }

    [Fact]
    public void Early_warning_score_and_escalation()
    {
        var normal = new ObservationRequest(null, 16, 98, false, null, 125, 80, 72, 36.8m, "A", 2, 0);
        Assert.Equal((0, false), EmrService.Ews(normal));
        var sick = normal with { RespiratoryRate = 26, SpO2 = 91, OnOxygen = true, SystolicBp = 88, HeartRate = 125 };
        var (score, three) = EmrService.Ews(sick);
        Assert.Equal(3 + 3 + 2 + 3 + 2, score);
        Assert.True(three);
        Assert.StartsWith("EMERGENCY", EmrService.Escalation(score, three));
        Assert.Contains("single parameter", EmrService.Escalation(3, true));
        Assert.Null(EmrService.Ews(normal with { HeartRate = null }).Score); // incomplete set: no score
    }

    [Fact]
    public async Task Checklist_counts_and_discharge_hard_stops()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var emr = s.ServiceProvider.GetRequiredService<EmrService>();
        var e = await emr.EpisodeAsync(Guid.NewGuid(), p.Id, "rn");
        Assert.Contains("nursing admission", await emr.AdmitAsync(e.Id, true, "rn"));
        Assert.NotEmpty((await emr.AssessAsync(e.Id, new(AssessmentKind.NursingAdmission, D(("id_band", "yes")), null), "rn")).Errors);
        Assert.Empty((await emr.AssessAsync(e.Id, new(AssessmentKind.NursingAdmission, D(("id_band", "yes"), ("allergies_checked", "Penicillin"), ("fasting_from", "02:00"), ("escort_name", "Hemi (husband)")), null), "rn")).Errors);
        Assert.Null(await emr.AdmitAsync(e.Id, true, "rn"));

        // Time Out before Sign In, or with items/team missing, is refused.
        Assert.Contains((await emr.CompleteCheckAsync(e.Id, new(CheckPhase.TimeOut, EmrService.CheckItems[CheckPhase.TimeOut].Select(i => i.Key).ToList(), Team3, null), "rn")).Errors, x => x.Contains("Sign In must"));
        var partial = await emr.CompleteCheckAsync(e.Id, new(CheckPhase.SignIn, ["identity"], new Dictionary<string, string> { ["Nurse"] = "RN Kim" }, null), "rn");
        Assert.Contains(partial.Errors, x => x.StartsWith("Not yet confirmed", StringComparison.Ordinal));
        Assert.Contains(partial.Errors, x => x.Contains("anaesthetist"));
        Assert.Empty((await emr.CompleteCheckAsync(e.Id, new(CheckPhase.SignIn, EmrService.CheckItems[CheckPhase.SignIn].Select(i => i.Key).ToList(), Team3, null), "rn")).Errors);
        Assert.Contains("Time Out", (await emr.SaveIntraopAsync(e.Id, new(DateTimeOffset.Now.AddMinutes(-30), null, DateTimeOffset.Now, null, null, "Supine", null, null, null, null, null, null, null, null, null, false, null, null, null, null), "rn")).Errors.Single());
        Assert.Empty((await emr.CompleteCheckAsync(e.Id, new(CheckPhase.TimeOut, EmrService.CheckItems[CheckPhase.TimeOut].Select(i => i.Key).ToList(), Team3, null), "rn")).Errors);

        // Counts need two people; a wrong closing count blocks Sign Out until resolved.
        Assert.Contains("two people", (await emr.CountAsync(e.Id, new(CountStage.Initial, "Swabs", 10, 10, "rn"), "rn")).Error);
        await emr.CountAsync(e.Id, new(CountStage.Initial, "Swabs", 10, 10, "RN Lee"), "rn");
        var (closing, _, warning) = await emr.CountAsync(e.Id, new(CountStage.Closing, "Swabs", 10, 9, "RN Lee"), "rn");
        Assert.Contains("X-ray", warning);
        var signOut = new CheckRequest(CheckPhase.SignOut, EmrService.CheckItems[CheckPhase.SignOut].Select(i => i.Key).ToList(), Team3, null);
        Assert.Contains((await emr.CompleteCheckAsync(e.Id, signOut, "rn")).Errors, x => x.Contains("not resolved"));
        Assert.Null(await emr.ResolveCountAsync(closing!.Id, "Swab found in bin, recounted 10 – surgeon informed", "rn"));
        Assert.Empty((await emr.CompleteCheckAsync(e.Id, signOut, "rn")).Errors);

        // Controlled drug: witness required, balance goes down, register entry written.
        var (cd, _) = await emr.AddControlledDrugAsync("Fentanyl", "100 mcg/2 ml", "ampoule", "rn");
        Assert.Contains("witness", await emr.RegisterAsync(new(cd!.Id, CdMovement.Received, 10, "rn", "Pharmacy order 55"), "rn"));
        Assert.Null(await emr.RegisterAsync(new(cd.Id, CdMovement.Received, 10, "RN Lee", "Pharmacy order 55"), "rn"));
        Assert.Contains("witness", (await emr.GiveAsync(e.Id, new("Fentanyl", "50 mcg", "IV", null, null, cd.Id, null, null), "rn")).Error);
        Assert.Null((await emr.GiveAsync(e.Id, new("Fentanyl", "50 mcg", "IV", null, null, cd.Id, "RN Lee", null), "rn")).Error);
        Assert.Equal(9, (await emr.ControlledDrugsAsync()).Single().Balance);
        Assert.Equal(2, (await emr.RegisterEntriesAsync(cd.Id)).Count);

        // Discharge hard stop: escort/carer or override; high EWS needs a medical note.
        await emr.SetStageAsync(e.Id, EpisodeStage.Recovery, "rn");
        await emr.ObserveAsync(e.Id, new(null, 22, 94, false, null, 105, 60, 105, 37.0m, "A", 3, 0), "rn"); // 2+1+1+1 = 5
        var refused = await emr.DischargeAsync(e.Id, new("Hemi", "021 555 0101", null, false, null, true, null, null), "rn");
        Assert.Contains(refused, x => x.Contains("responsible adult"));
        Assert.Contains(refused, x => x.Contains("early warning score is 5"));
        await emr.NoteAsync(e.Id, new(NoteKind.Medical, "Reviewed – mild tachycardia from pain, analgesia given, fit for discharge.", true, null), "drford");
        Assert.Empty(await emr.DischargeAsync(e.Id, new("Hemi", "021 555 0101", "Hemi", true, null, true, null, s.ServiceProvider.GetRequiredService<TimeProvider>().GetLocalNow().Date is var d ? DateOnly.FromDateTime(d).AddDays(1) : null), "rn"));
        Assert.Contains("closed", (await Assert.ThrowsAsync<InvalidOperationException>(() => emr.FluidAsync(e.Id, new(null, true, "IV", 500), "rn"))).Message);
    }
}
