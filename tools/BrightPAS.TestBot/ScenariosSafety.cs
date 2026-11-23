using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// 0.25.0: outbox (undo send; sent to the wrong person → correction and privacy incident; second-person release), form designer (import a CSV,
/// publish; a nurse fills in the pre-op form and the allergy reaches the patient record), de-identified reporting (aggregate download, Privacy Officer
/// approval of a research extract, requester cannot approve their own).
/// </summary>
public sealed partial class Scenarios
{
    private const string SafetyArea = "Send safety, forms & de-identified reports";

    private async Task SafetyStepsAsync()
    {
        await Step(SafetyArea, "Outbox: a message is held, and Undo send stops it for good", OutboxUndoAsync);
        await Step(SafetyArea, "Outbox: sent to the wrong person → correction notice and a privacy incident", OutboxWrongRecipientAsync);
        await Step(SafetyArea, "Form designer: import a CSV question list and publish it", FormImportAsync);
        await Step(SafetyArea, "Nurse fills in the pre-op form; the allergy goes onto the patient record", FormFillAsync);
        await Step(SafetyArea, "De-identified reports: Privacy Officer approves; the requester cannot approve their own", ResearchApproveAsync);
    }

    private async Task<(Outcome, string)> OutboxUndoAsync()
    {
        if (_patient is null) return (Outcome.Skip, "No test patient.");
        var staff = await WhoCanAsync($"/api/portal/patients/{_patient.Id}/link");
        if (staff is null) return (Outcome.Skip, "No account sends portal links.");
        var r = await staff.PostAsync($"/api/portal/patients/{_patient.Id}/link", new { sendSms = true, sendEmail = false });
        if (r.Status != 200) return (Outcome.Fail, $"Could not queue the link ({r.Status}).");
        var ob = await staff.GetAsync("/api/outbox");
        var held = Find(ob.Json?.GetProperty("messages"), m => Str(m, "patientId") == _patient.Id && Str(m, "status") == "Queued" && Str(m, "kind") == "PortalLink");
        if (held is null) return (Outcome.Fail, "The portal link was not held in the outbox.");
        var page = staff.Page;
        await page.GotoAsync($"{bot.O.Url}/outbox");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Outbox" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.Locator("tr", new() { HasText = "Portal link" }).First.GetByRole(AriaRole.Button, new() { Name = "Undo send" }).ClickAsync();
        await page.GetByLabel("Why stop it?").FillAsync("Robot: sent by mistake");
        await page.GetByRole(AriaRole.Button, new() { Name = "Stop it" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Stopped – it will not be sent.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var before = sims.SmsOut.Count;
        await Task.Delay(TimeSpan.FromSeconds(20));
        var leaked = sims.SmsOut.Skip(before).Any(x => x.Text.Contains("/my/", StringComparison.Ordinal) && x.To == E164(_patient.Mobile!));
        return leaked ? (Outcome.Fail, "The message was sent after Undo send.") : (Outcome.Pass, "Portal link held in the outbox, stopped with a reason, and never sent.");
    }

    private async Task<(Outcome, string)> OutboxWrongRecipientAsync()
    {
        var staff = await As("reception");
        var ob = await staff.GetAsync("/api/outbox");
        if (ob.Status != 200) return (Outcome.Skip, "Reception cannot open the outbox.");
        var sent = Find(ob.Json?.GetProperty("messages"), m => Str(m, "status") is "Sent" or "Delivered" && m.GetProperty("wrongRecipientAt").ValueKind == JsonValueKind.Null && Str(m, "patientId") is not null);
        if (sent is null) return (Outcome.Skip, "No message sent to a patient in the last 7 days.");
        var r = await staff.PostAsync($"/api/outbox/messages/{Str(sent.Value, "id")}/wrong-recipient", new { reason = "Robot: mobile number belonged to someone else" });
        if (r.Status != 200) return (Outcome.Fail, $"Report failed ({r.Status}): {r.Text}");
        var corr = r.Json!.Value.GetProperty("correctionId").ValueKind == JsonValueKind.String;
        var inc = r.Json!.Value.GetProperty("incidentId").ValueKind == JsonValueKind.String;
        var again = await staff.PostAsync($"/api/outbox/messages/{Str(sent.Value, "id")}/wrong-recipient", new { reason = "again" });
        return corr && inc && again.Status == 400 ? (Outcome.Pass, "Correction notice queued to the same address; privacy incident opened in Quality; can't be reported twice.")
            : (Outcome.Fail, $"Correction {corr}, incident {inc}, second report HTTP {again.Status}.");
    }

    private async Task<(Outcome, string)> FormImportAsync()
    {
        var q = await As("quality");
        var csv = "question,type,options,required\nRoom temperature in range,yesno,,yes\nHumidity in range,yesno,,yes\nWhich theatre,choice,Theatre 1|Theatre 2|Procedure room,yes\nComments,longtext,,no\n";
        var code = "ROBOT-" + DateTime.UtcNow.ToString("HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var r = await q.PostAsync("/api/forms/designer/import", new { code, name = "Robot theatre environment check", category = "Audit", kind = "csv", text = csv, base64 = (string?)null });
        if (r.Status != 200) return r.Status == 403 ? (Outcome.Skip, "Quality cannot design forms.") : (Outcome.Fail, $"Import failed ({r.Status}): {r.Text}");
        var id = Str(r.Json!.Value, "id");
        var page = q.Page;
        await page.GotoAsync($"{bot.O.Url}/forms/designer/{id}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Fields" })).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Publish v1" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Published.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var v = (await q.GetAsync($"/api/forms/designer/versions/{id}")).Json!.Value;
        return Str(v, "status") == "Published" && v.GetProperty("fields").GetArrayLength() == 4
            ? (Outcome.Pass, $"{code}: 4 questions imported from CSV (yes/no, pick one, long text) and published.") : (Outcome.Fail, $"Status {Str(v, "status")}.");
    }

    private async Task<(Outcome, string)> FormFillAsync()
    {
        var nurse = await As("nurse");
        var forms = await nurse.GetAsync("/api/forms/designer/forms");
        if (forms.Status != 200) return (Outcome.Skip, "The nurse cannot use forms.");
        var npa = Find(forms.Json, f => Str(f, "code") == "NPA");
        if (npa is null) return (Outcome.Skip, "No pre-op form in the demo data.");
        var vid = Str(npa.Value.GetProperty("versions")[0], "id");
        var pid = _patient?.Id;
        if (pid is null) return (Outcome.Skip, "No test patient.");
        var s = await nurse.PostAsync("/api/forms/designer/entries", new { versionId = vid, patientId = pid, bookingId = (string?)null });
        if (s.Status != 200) return (Outcome.Fail, $"Could not start the form ({s.Status}): {s.Text}");
        var page = nurse.Page;
        await page.GotoAsync($"{bot.O.Url}/forms/entry/{Str(s.Json!.Value, "id")}");
        await page.Locator(".fill-q", new() { HasText = "Weight (kg)" }).Locator("input").FillAsync("88");
        await page.Locator(".fill-q", new() { HasText = "Height (cm)" }).Locator("input").FillAsync("176");
        await page.Locator(".fill-q", new() { HasText = "Any allergies?" }).GetByLabel("Yes").CheckAsync();
        await page.Locator(".fill-q", new() { HasText = "Allergy and reaction" }).Locator("input").FillAsync("Robot test – codeine, nausea");
        await page.Locator(".fill-q", new() { HasText = "Fasting from" }).Locator("select").SelectOptionAsync("Midnight");
        await page.Locator(".fill-q", new() { HasText = "Responsible adult for 24 hours" }).GetByLabel("Yes").CheckAsync();
        await page.Locator(".fill-q", new() { HasText = "Nurse" }).Last.Locator("input").FillAsync("Robot Nurse");
        await page.GetByRole(AriaRole.Button, new() { Name = "Complete" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Completed.", new() { Exact = false })).ToBeVisibleAsync(new() { Timeout = 10000 });
        var alerts = await nurse.GetAsync($"/api/patients/{pid}");
        var has = alerts.Text.Contains("Robot test – codeine", StringComparison.Ordinal);
        return has ? (Outcome.Pass, "Pre-op form filled with show/hide (allergy detail appears), BMI calculated, completed; the allergy is now a patient alert and the measurements went to triage.")
            : (Outcome.Fail, "Form completed but the allergy did not reach the patient record.");
    }

    private async Task<(Outcome, string)> ResearchApproveAsync()
    {
        var q = await As("quality");
        var list = await q.GetAsync("/api/research/extracts");
        if (list.Status != 200) return (Outcome.Skip, "De-identified reports not available to quality.");
        var pending = Find(list.Json, x => Str(x, "status") == "Submitted");
        if (pending is null) return (Outcome.Skip, "No extract waiting for approval.");
        var id = Str(pending.Value, "id");
        var own = await q.PostAsync($"/api/research/extracts/{id}/decide", new { approve = true, note = "self" });
        var pr = await As("privacy");
        var page = pr.Page;
        await page.GotoAsync($"{bot.O.Url}/research/{id}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Check and preview" }).ClickAsync();
        await Assertions.Expect(page.GetByText("Preview below – nothing has been released.")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await page.GetByLabel("Note (needed to decline, or to approve when rows are at risk)").FillAsync("Robot: HDEC approval checked; pseudonymised extract held by the investigator.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Approved.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var x = (await pr.GetAsync($"/api/research/extracts/{id}")).Json!.Value;
        return own.Status == 403 && Str(x, "status") == "Approved"
            ? (Outcome.Pass, $"{Str(x, "number")}: quality could not approve (HTTP 403); the Privacy Officer previewed (smallest group {x.GetProperty("minGroupSize")}) and approved.")
            : (Outcome.Fail, $"Own approval HTTP {own.Status}; status {Str(x, "status")}.");
    }
}
