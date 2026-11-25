using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Medicines and medicine safety (0.27.0) through the screens and the API: the live check while charting finds an allergy group;
/// enforced checks refuse a "Stop" without a reason and accept it with one; the patient page shows the allergy linked to a group;
/// a pack barcode finds the medicine; a nurse cannot approve medicine rules or change how strict the checks are.
/// </summary>
public sealed partial class Scenarios
{
    private const string MedsArea = "Medicines & medicine safety";
    private string? _medsPatient;
    private string? _medsBooking;

    private async Task MedicinesStepsAsync()
    {
        await Step(MedsArea, "Nurse charting amoxicillin for a patient with a penicillin allergy sees the medicine check stop it", MedsLiveCheckAsync);
        await Step(MedsArea, "Enforced checks: a stop needs a reason; with a reason it is recorded and shown as an override", MedsEnforceAsync);
        await Step(MedsArea, "Patient page shows the allergy linked to the penicillin group", MedsPatientPanelAsync);
        await Step(MedsArea, "A medicine pack barcode finds the medicine", MedsBarcodeAsync);
        await Step(MedsArea, "A nurse cannot approve medicine rules or change how strict the checks are", MedsGuardAsync);
    }

    private async Task<(Outcome, string)> MedsLiveCheckAsync()
    {
        var an = await As("fgrant");
        var rep = await an.GetAsync("/api/medicines/report");
        if (rep.Status != 200) return (Outcome.Skip, "Medicines is not available (module off or no permission).");
        var o = Find(rep.Json!.Value.GetProperty("notable"), _ => true);
        if (o is null) return (Outcome.Skip, "No demo patient with a penicillin allergy override in the report.");
        _medsPatient = Str(o.Value, "patientId");
        var nurse = await As("nurse");
        var bookings = await nurse.GetAsync($"/api/theatre/patients/{_medsPatient}");
        var b = Find(bookings.Json, x => Str(x, "status") is "Booked" or "Confirmed");
        if (b is null) return (Outcome.Skip, "The patient has no upcoming booking to chart against.");
        _medsBooking = Str(b.Value, "id");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/emr/{_medsBooking}?tab=meds");
        await page.GetByLabel("Medicine", new() { Exact = true }).FillAsync("Amoxicillin 500 mg capsule");
        await page.GetByLabel("Dose").FillAsync("1 capsule");
        await page.GetByLabel("Route").SelectOptionAsync("Oral");
        var stop = page.Locator(".med-finding.sev-stop").First;
        await Assertions.Expect(stop).ToBeVisibleAsync(new() { Timeout = 15000 });
        var text = await stop.InnerTextAsync();
        await Assertions.Expect(page.GetByLabel(new System.Text.RegularExpressions.Regex("^Reason to give it anyway"))).ToBeVisibleAsync();
        return text.Contains("penicillin", StringComparison.OrdinalIgnoreCase)
            ? (Outcome.Pass, $"Live check: {text.Trim()}")
            : (Outcome.Fail, $"The stop does not mention penicillins: {text.Trim()}");
    }

    private async Task<(Outcome, string)> MedsEnforceAsync()
    {
        if (_medsBooking is null) return (Outcome.Skip, "No booking from the previous step.");
        var an = await As("fgrant");
        var settings = await an.GetAsync("/api/medicines/settings");
        var mode = Str(settings.Json!.Value, "mode") ?? "Advise";
        var set = await an.PutAsync("/api/medicines/settings", new { mode = "Enforce", checkRegularMedicines = true, duplicateWithinMinutes = 60 });
        if (set.Status != 200) return (Outcome.Fail, $"The anaesthetist could not enforce the checks (HTTP {set.Status}): {set.Text}");
        try
        {
            var nurse = await As("nurse");
            var ep = await nurse.GetAsync($"/api/emr/bookings/{_medsBooking}");
            var epId = ep.Json!.Value.GetProperty("episode").ValueKind == System.Text.Json.JsonValueKind.Object ? Str(ep.Json!.Value.GetProperty("episode"), "id") : null;
            if (epId is null) return (Outcome.Skip, "No clinical record episode for the booking.");
            var body = new { medicine = "Amoxicillin 500 mg capsule", dose = "1 capsule", route = "Oral", givenAt = (string?)null, barcode = (string?)null, controlledDrugId = (string?)null, witnessedBy = (string?)null, notes = (string?)null, overrideReason = (string?)null };
            var refused = await nurse.PostAsync($"/api/emr/episodes/{epId}/medications", body);
            if (refused.Status != 400 || !refused.Text.Contains("\"stopped\":true", StringComparison.Ordinal)) return (Outcome.Fail, $"Without a reason it was not stopped (HTTP {refused.Status}).");
            var ok = await nurse.PostAsync($"/api/emr/episodes/{epId}/medications", body with { overrideReason = "Robot: allergy service letter on file" });
            if (ok.Status != 200) return (Outcome.Fail, $"With a reason it was refused (HTTP {ok.Status}): {ok.Text}");
            var page = nurse.Page;
            await page.GotoAsync($"{bot.O.Url}/emr/{_medsBooking}?tab=meds");
            await Assertions.Expect(page.GetByText("given despite a stop").First).ToBeVisibleAsync(new() { Timeout = 10000 });
            return (Outcome.Pass, "Refused without a reason (HTTP 400, stopped); recorded with the reason and shown as \"given despite a stop\".");
        }
        finally { await an.PutAsync("/api/medicines/settings", new { mode, checkRegularMedicines = true, duplicateWithinMinutes = 60 }); }
    }

    private async Task<(Outcome, string)> MedsPatientPanelAsync()
    {
        if (_medsPatient is null) return (Outcome.Skip, "No patient from the first step.");
        var nurse = await As("nurse");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/patients/{_medsPatient}");
        var panel = page.Locator("section.card", new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Medicine safety", Exact = true }) });
        await Assertions.Expect(panel).ToBeVisibleAsync(new() { Timeout = 10000 });
        await Assertions.Expect(panel.GetByText("PEN group")).ToBeVisibleAsync();
        return (Outcome.Pass, "Medicine safety panel: allergy checked as the PEN group, with recent stops and overrides.");
    }

    private async Task<(Outcome, string)> MedsBarcodeAsync()
    {
        var nurse = await As("nurse");
        var r = await nurse.GetAsync("/api/medicines/barcode/9400000000016");
        if (r.Status == 404) return (Outcome.Fail, "The demo cefazolin pack barcode was not found.");
        if (r.Status != 200) return (Outcome.Skip, $"Barcode lookup not available (HTTP {r.Status}).");
        var name = Str(r.Json!.Value, "name") ?? "";
        return name.Contains("Cefazolin", StringComparison.Ordinal) ? (Outcome.Pass, $"GTIN 9400000000016 → {name}") : (Outcome.Fail, $"Wrong medicine: {name}");
    }

    private async Task<(Outcome, string)> MedsGuardAsync()
    {
        var nurse = await As("nurse");
        var rules = await nurse.GetAsync("/api/medicines/rules");
        var any = Find(rules.Json, _ => true);
        if (any is null) return (Outcome.Skip, "No medicine rules in the demo data.");
        var approve = await nurse.PostAsync($"/api/medicines/rules/{Str(any.Value, "id")}/approve", new { note = "robot" });
        var settings = await nurse.PutAsync("/api/medicines/settings", new { mode = "Off", checkRegularMedicines = true, duplicateWithinMinutes = 60 });
        return approve.Status == 403 && settings.Status == 403
            ? (Outcome.Pass, "Approve and settings refused for the nurse (HTTP 403).")
            : (Outcome.Fail, $"Approve HTTP {approve.Status}, settings HTTP {settings.Status} – expected 403.");
    }
}
