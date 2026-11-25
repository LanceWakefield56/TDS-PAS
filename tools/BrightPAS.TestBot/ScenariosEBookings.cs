using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Bookings from specialists' rooms (0.28.0) through the screens: the PA answers a TDS question and sends a new request from the portal form;
/// the booking office registers the patient and accepts the request onto the waitlist, and the rooms then see it accepted; an HL7 REF^I12
/// file is imported on the screen; the PA cannot see the booking office's list or another specialist's request.
/// </summary>
public sealed partial class Scenarios
{
    private const string RoomsArea = "Bookings from rooms";
    private string? _roomsNumber;

    private async Task EBookingsStepsAsync()
    {
        await Step(RoomsArea, "PA answers the booking office's question in the portal", RoomsAnswerAsync);
        await Step(RoomsArea, "PA sends a new booking request from the portal form", RoomsSendAsync);
        await Step(RoomsArea, "Booking office registers the patient and accepts the request onto the waitlist; the rooms see it accepted", RoomsAcceptAsync);
        await Step(RoomsArea, "Booking office imports an HL7 REF^I12 referral file", RoomsHl7Async);
        await Step(RoomsArea, "A PA cannot see the booking office's list or another specialist's request", RoomsGuardAsync);
    }

    private async Task<(Outcome, string)> RoomsAnswerAsync()
    {
        var pa = await As("pa");
        var list = await pa.GetAsync("/api/ebookings/rooms");
        if (list.Status != 200) return (Outcome.Skip, $"Bookings from rooms is not available (HTTP {list.Status}).");
        var q = Find(list.Json, x => Str(x, "status") == "Query");
        if (q is null) return (Outcome.Skip, "No demo request is waiting for the rooms.");
        var number = Str(q.Value, "number")!;
        var page = pa.Page;
        await page.GotoAsync($"{bot.O.Url}/portal");
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Booking requests") }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = number, Exact = true }).ClickAsync();
        await page.GetByLabel("Message to the TDS booking office").FillAsync("Her GP is managing the apixaban – stop 48 hours before. No cardiologist involved. (robot test)");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send message", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByText("Message sent.")).ToBeVisibleAsync(new() { Timeout = 10000 });
        var after = Find((await pa.GetAsync("/api/ebookings/rooms")).Json, x => Str(x, "number") == number);
        return Str(after!.Value, "status") == "Submitted" ? (Outcome.Pass, $"{number} answered; back with TDS.") : (Outcome.Fail, $"{number} is {Str(after.Value, "status")} after the answer.");
    }

    private async Task<(Outcome, string)> RoomsSendAsync()
    {
        var pa = await As("pa");
        var page = pa.Page;
        await page.GotoAsync($"{bot.O.Url}/portal");
        var newTab = page.GetByRole(AriaRole.Button, new() { Name = "New booking request", Exact = true });
        try { await newTab.WaitForAsync(new() { Timeout = 15000 }); }
        catch (TimeoutException) { return (Outcome.Skip, "The portal has no booking request tab (module off or no permission)."); }
        await newTab.ClickAsync();
        var spec = page.GetByLabel("Specialist").First;
        await Assertions.Expect(spec).ToBeVisibleAsync(new() { Timeout = 10000 });
        await spec.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await page.GetByLabel("Contact person").FillAsync("Rachel Moore");
        await page.GetByLabel("Family name").FillAsync("Robotson");
        await page.GetByLabel("Given names").FillAsync("Test Rooms");
        await page.GetByLabel("Date of birth").FillAsync("1975-03-14");
        await page.GetByLabel("Procedure", new() { Exact = true }).FillAsync("Excision of sebaceous cyst, back");
        await page.GetByLabel("Expected minutes").FillAsync("20");
        await page.GetByLabel("Anaesthetic").SelectOptionAsync("Local");
        await page.GetByLabel("Funding").SelectOptionAsync("SelfPay");
        await page.GetByLabel("Consent signed", new() { Exact = true }).CheckAsync();
        await page.Locator("input[type=file]").First.SetInputFilesAsync(new FilePayload { Name = "robot-letter.pdf", MimeType = "application/pdf", Buffer = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n") });
        await Assertions.Expect(page.GetByText("robot-letter.pdf").First).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Send booking request", Exact = true }).ClickAsync();
        var sent = page.GetByText(new System.Text.RegularExpressions.Regex(@"Booking request EB\d+ sent to TDS"));
        await Assertions.Expect(sent).ToBeVisibleAsync(new() { Timeout = 15000 });
        _roomsNumber = System.Text.RegularExpressions.Regex.Match(await sent.InnerTextAsync(), @"EB\d+").Value;
        return (Outcome.Pass, $"Sent {_roomsNumber} with a letter attached.");
    }

    private async Task<(Outcome, string)> RoomsAcceptAsync()
    {
        if (_roomsNumber is null) return (Outcome.Skip, "No request from the previous step.");
        var rec = await As("reception");
        var list = await rec.GetAsync("/api/ebookings?view=open");
        var row = Find(list.Json, x => Str(x, "number") == _roomsNumber);
        if (row is null) return (Outcome.Fail, $"{_roomsNumber} is not in the booking office's list.");
        var page = rec.Page;
        var stage = "My list card";
        try
        {
            await page.GotoAsync($"{bot.O.Url}/");
            await Assertions.Expect(page.GetByText(new System.Text.RegularExpressions.Regex(@"booking requests? to check")).First).ToBeVisibleAsync(new() { Timeout = 15000 });
            stage = "open the request";
            await page.GotoAsync($"{bot.O.Url}/rooms-bookings?tab=requests&open={Str(row.Value, "id")}");
            var register = page.GetByRole(AriaRole.Button, new() { Name = "Register as a new patient", Exact = true });
            await register.WaitForAsync(new() { Timeout = 15000 });
            stage = "register the patient";
            await register.ClickAsync();
            var registered = page.GetByText("Patient registered.");
            var confirm = page.GetByRole(AriaRole.Button, new() { Name = "They are a different person – register", Exact = true });
            await registered.Or(confirm).First.WaitForAsync(new() { Timeout = 15000 });
            if (await confirm.IsVisibleAsync()) { await confirm.ClickAsync(); await registered.WaitForAsync(new() { Timeout = 15000 }); }
            stage = "accept";
            await page.GetByRole(AriaRole.Button, new() { Name = "Accept onto the waitlist", Exact = true }).ClickAsync();
            await page.GetByText("Accepted onto the waitlist. The rooms can see this in the portal.").WaitForAsync(new() { Timeout = 15000 });
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var shot = await rec.ShotAsync("rooms-accept-" + stage.Replace(' ', '-'));
            var shown = string.Join(" | ", await page.Locator("main .error, main .notice, main .warn-box").AllInnerTextsAsync());
            return (Outcome.Fail, $"At \"{stage}\": {ex.Message.Split('\n')[0]} Screen: {shown} ({shot})");
        }
        var detail = await rec.GetAsync($"/api/ebookings/{Str(row.Value, "id")}");
        var w = detail.Json!.Value.GetProperty("waitlist");
        var filed = detail.Json!.Value.GetProperty("files").EnumerateArray().All(f => f.GetProperty("filed").GetBoolean());
        var pa = await As("pa");
        var mine = Find((await pa.GetAsync("/api/ebookings/rooms")).Json, x => Str(x, "number") == _roomsNumber);
        if (w.ValueKind != JsonValueKind.Object) return (Outcome.Fail, "No waitlist entry after accepting.");
        if (!filed) return (Outcome.Fail, "The letter was not filed in the patient record.");
        return Str(mine!.Value, "status") == "Accepted"
            ? (Outcome.Pass, $"{_roomsNumber}: on the waitlist ({Str(w, "priority")}, target {Str(w, "targetDate")}); letter filed; the PA sees it accepted.")
            : (Outcome.Fail, $"The PA sees {Str(mine.Value, "status")}.");
    }

    private async Task<(Outcome, string)> RoomsHl7Async()
    {
        var rec = await As("reception");
        var page = rec.Page;
        await page.GotoAsync($"{bot.O.Url}/rooms-bookings?tab=import");
        var picker = page.Locator("input[type=file]");
        try { await picker.First.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 15000 }); }
        catch (TimeoutException) { return (Outcome.Skip, "No import screen (module off or no permission)."); }
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var hl7 = string.Join("\r",
            $@"MSH|^~\&|ROBOTPMS|ROBOT ROOMS|BRIGHTPAS|TDS|{stamp}||REF^I12^REF_I12|RB{stamp}|P|2.4",
            $"RF1|A|R|SURG|||RB-{stamp}",
            "PRD|RT|Lindqvist^Sarah^^^Dr|||||15DEME",
            "PID|1||^^^NHI||Hl7test^Robot||19820704|F|||1 Test Lane^^Nelson^^7010",
            "PR1|1||^Gastroscopy",
            "NTE|1||Robot HL7 import test.");
        await picker.First.SetInputFilesAsync(new FilePayload { Name = $"robot-{stamp}.hl7", MimeType = "text/plain", Buffer = Encoding.UTF8.GetBytes(hl7) });
        var ok = page.GetByText(new System.Text.RegularExpressions.Regex(@"1 request received: EB\d+"));
        await Assertions.Expect(ok).ToBeVisibleAsync(new() { Timeout = 15000 });
        return (Outcome.Pass, (await ok.InnerTextAsync()).Trim());
    }

    private async Task<(Outcome, string)> RoomsGuardAsync()
    {
        var pa = await As("pa");
        var office = await pa.GetAsync("/api/ebookings?view=all");
        var rec = await As("reception");
        var other = Find((await rec.GetAsync("/api/ebookings?view=all")).Json, x => Str(x, "specialist")?.Contains("Lindqvist", StringComparison.Ordinal) == true);
        var peek = other is null ? (Status: 0, Json: (JsonElement?)null, Text: "") : await pa.GetAsync($"/api/ebookings/rooms/{Str(other.Value, "id")}");
        if (office.Status != 403) return (Outcome.Fail, $"The PA could open the booking office's list (HTTP {office.Status}).");
        if (other is not null && peek.Status != 404) return (Outcome.Fail, $"The PA could open Dr Lindqvist's request (HTTP {peek.Status}).");
        return (Outcome.Pass, $"Booking office list: HTTP {office.Status}; another specialist's request: {(other is null ? "none to try" : $"HTTP {peek.Status}")}.");
    }
}
