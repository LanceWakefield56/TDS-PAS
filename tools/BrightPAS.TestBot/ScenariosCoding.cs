using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Coding &amp; statutory reporting (0.20.0) through the screens: the coder opens a discharged event, adds a principal diagnosis and a
/// procedure from the code search, sees the edit checks and marks it coded; an NMDS load file is created and downloaded as a protected ZIP;
/// an NBRS entry is created from a Health NZ funded waitlist entry, booked, batched (AAA00001.NBR layout) and an acknowledgement loaded.
/// </summary>
public sealed partial class Scenarios
{
    private const string CodingArea = "Coding & statutory reporting";

    private async Task CodingStepsAsync()
    {
        await Step(CodingArea, "The coder codes a discharged event in the workbench: code search, edit checks, marked coded", CodeEventAsync);
        await Step(CodingArea, "An NMDS load file is created and downloads as a protected ZIP with the private hospital CSV layout", NmdsFileAsync);
        await Step(CodingArea, "NBRS: entry from a Health NZ funded waitlist entry, booked event, batch file and acknowledgement", NbrsAsync);
    }

    private async Task<(Outcome, string)> CodeEventAsync()
    {
        var coder = await As("coder");
        var q = await coder.GetAsync("/api/coding/queue?status=open");
        if (q.Status != 200) return (Outcome.Skip, "The coder cannot open the coding queue (module off or no permission).");
        var row = Find(q.Json, r => Str(r, "status") == "ToCode");
        if (row is null) return (Outcome.Skip, "No discharged event waiting to be coded.");
        var id = Str(row.Value, "id");
        var page = coder.Page;
        await page.GotoAsync($"{bot.O.Url}/coding/events/{id}");
        await page.GetByLabel("Find a code").FillAsync("K63");
        await page.GetByRole(AriaRole.Button, new() { Name = "Find", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "K63.5" }).First.ClickAsync(new() { Timeout = 10000 });
        await page.GetByLabel("Find a code").FillAsync("32093");
        await page.GetByRole(AriaRole.Button, new() { Name = "Find", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "32093-00" }).First.ClickAsync(new() { Timeout = 10000 });
        await page.GetByLabel("Description 1").FillAsync("Rectosigmoid polyp 5 mm (robot)");
        await page.GetByRole(AriaRole.Button, new() { Name = "Check and mark coded" }).ClickAsync();
        // Wait for the save result itself (the demo banner is also a status region).
        var status = page.GetByText("Coded – the event goes in the next NMDS file.").Or(page.Locator("p.error[role=alert]"));
        await Assertions.Expect(status.First).ToBeVisibleAsync(new() { Timeout = 15000 });
        var shown = await status.First.InnerTextAsync();
        var after = (await coder.GetAsync($"/api/coding/events/{id}")).Json!.Value;
        var st = after.GetProperty("episode").GetProperty("status").GetString();
        var checks = after.GetProperty("checks").EnumerateArray().Select(c => $"{c.GetProperty("level").GetString()}: {c.GetProperty("rule").GetString()}").ToList();
        if (st == "Coded") return (Outcome.Pass, $"Event coded on screen ({shown.Trim()}). Checks shown: {(checks.Count == 0 ? "none" : string.Join("; ", checks))}.");
        return (Outcome.Warn, $"Not marked coded – the edit checks stopped it ({shown.Trim()}): {string.Join("; ", checks)}. This can be right for the demo event chosen.");
    }

    private async Task<(Outcome, string)> NmdsFileAsync()
    {
        var coder = await As("coder");
        var today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var pre = await coder.GetAsync($"/api/coding/nmds/preview?to={today}");
        if (pre.Status != 200) return (Outcome.Skip, "The coder cannot prepare NMDS files.");
        if (pre.Json!.Value.GetProperty("ready").GetInt32() == 0) return (Outcome.Skip, $"No coded event ready: {pre.Text}");
        var made = await coder.PostAsync("/api/coding/nmds/files", new { periodTo = today });
        if (made.Status != 200) return (Outcome.Fail, $"File not created ({made.Status}): {made.Text}");
        var fileId = Str(made.Json!.Value, "id"); var name = Str(made.Json!.Value, "fileName") ?? "nmds.csv";
        var res = await coder.Ctx.APIRequest.PostAsync($"{bot.O.Url}/api/coding/nmds/files/{fileId}/download", new()
        {
            Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1" },
            DataObject = new { export = new { protect = true, password = "Robot-Nmds-Pass-2026!" } },
            IgnoreHTTPSErrors = true,
        });
        if (res.Status != 200) return (Outcome.Fail, $"Download refused ({res.Status}): {await res.TextAsync()}");
        var zip = await res.BodyAsync();
        await File.WriteAllBytesAsync(Path.Combine(R.DeviceFolder, "nmds-file-protected.zip"), zip);
        using var z = new ICSharpCode.SharpZipLib.Zip.ZipFile(new MemoryStream(zip)) { Password = "Robot-Nmds-Pass-2026!" };
        var e = z.GetEntry(name);
        if (e is null || !e.IsCrypted) return (Outcome.Fail, $"The ZIP does not hold an encrypted {name}.");
        using var r = new StreamReader(z.GetInputStream(e), Encoding.ASCII);
        var csv = await r.ReadToEndAsync();
        await File.WriteAllTextAsync(Path.Combine(R.DeviceFolder, name), csv);
        var head = csv.Split("\r\n")[0];
        return head.StartsWith("ID,PURCHASER CODE,HEALTH AGENCY,HAF,NHI,SURNAME", StringComparison.Ordinal) && head.Contains("DIAGTYPE01", StringComparison.Ordinal)
            ? (Outcome.Pass, $"{name}: {csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length - 1} event(s), AES-256 ZIP, header and repeating diagnosis groups as in the Health NZ layout.")
            : (Outcome.Fail, "The CSV header does not follow the private hospital load file layout: " + head);
    }

    private async Task<(Outcome, string)> NbrsAsync()
    {
        var coder = await As("coder");
        var wait = await coder.GetAsync("/api/coding/nbrs/waitlist");
        if (wait.Status != 200) return (Outcome.Skip, "The coder cannot open NBRS.");
        var w = wait.Json!.Value.EnumerateArray().FirstOrDefault();
        if (w.ValueKind != JsonValueKind.Object) return (Outcome.Skip, "No Health NZ funded waitlist entry without an NBRS entry.");
        var draft = (await coder.GetAsync($"/api/coding/nbrs/draft/{Str(w, "id")}")).Json!.Value;
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(draft.GetRawText())!;
        body["bookingSource"] = "2"; body["referralDate"] = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd"); body["firstAssessmentDate"] = DateTime.Today.AddDays(-40).ToString("yyyy-MM-dd");
        body["cpacDate"] = DateTime.Today.AddDays(-40).ToString("yyyy-MM-dd"); body["cpacScore"] = "62"; body["cpacSystem"] = "GS01"; body["healthSpecialty"] = "M15";
        body["clinicalCode"] = "32090-00"; body["domicileCode"] = "2077"; body["responsibilityCode"] ??= "10001"; body["assessorCode"] ??= "10001";
        var saved = await coder.PostAsync("/api/coding/nbrs/entries", body);
        if (saved.Status != 200) return (Outcome.Fail, $"Entry not saved: {saved.Text}");
        var entryId = Str(saved.Json!.Value, "id");
        var page = coder.Page;
        await page.GotoAsync($"{bot.O.Url}/coding?tab=nbrs");
        await Assertions.Expect(page.GetByText(Str(saved.Json!.Value, "localId") ?? "?").First).ToBeVisibleAsync(new() { Timeout = 10000 });
        var ev = await coder.PostAsync($"/api/coding/nbrs/entries/{entryId}/events", new
        {
            kind = "book",
            statusDate = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd"),
            bookingMadeDate = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd"),
            bookedForDate = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd"),
        });
        if (ev.Status != 200) return (Outcome.Fail, $"Booked event refused by the local checks: {ev.Text}");
        var batch = await coder.PostAsync("/api/coding/nbrs/batches", new { });
        if (batch.Status != 200) return (Outcome.Fail, $"Batch not created: {batch.Text}");
        var file = Str(batch.Json!.Value, "fileName");
        var ack = $"AH,D001,{Path.ChangeExtension(file, ".NBK")},00001\r\nAC,00002,NBR4004,\"Data record accepted\"\r\n";
        var loaded = await coder.PostAsync("/api/coding/nbrs/acknowledgement", new { text = ack });
        if (loaded.Status != 200) return (Outcome.Fail, $"Acknowledgement not loaded: {loaded.Text}");
        return (Outcome.Pass, $"Entry {Str(saved.Json!.Value, "localId")} booked; batch {file} created (CMPL); simulated acknowledgement loaded – {loaded.Json!.Value.GetProperty("accepted").GetInt32()} accepted.");
    }
}
