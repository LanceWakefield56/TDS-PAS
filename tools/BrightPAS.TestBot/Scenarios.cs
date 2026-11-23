using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Start-to-finish tests with the virtual devices and outside systems: SMS to virtual phones (and replies), email, the patient portal opened
/// from an SMS on a phone, Zebra wristbands and labels, DYMO labels, document scanning with text recognition, a barcode scanner, HealthLink
/// letters with acknowledgements, laboratory results over MLLP, and protected exports. BrightPAS settings changed for this are put back afterwards.
/// </summary>
public sealed partial class Scenarios(Bot bot, Sims sims) : IAsyncDisposable
{
    private Report R => bot.R;
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Func<Task>> _restore = [];
    private static readonly string[] Candidates = ["admin", "reception", "nurse", "pa", "finance", "privacy", "roster", "integration", "hreid"];
    private const string Area = "devices";
    private record TestPatient(string Id, string Family, string Given, string Dob, string Sex, string? Nhi, string Mrn, string? Mobile);
    private TestPatient? _patient;

    private async Task<Session> As(string account)
    {
        if (_sessions.TryGetValue(account, out var s)) return s;
        s = await bot.SignInAsync(account);
        _sessions[account] = s;
        return s;
    }

    /// <summary>The first demo account that may open <paramref name="path"/>.</summary>
    private async Task<Session?> WhoCanAsync(string path)
    {
        foreach (var a in Candidates)
        {
            var s = await As(a);
            if ((await s.GetAsync(path)).Status == 200) return s;
        }
        return null;
    }

    private async Task Step(string device, string check, Func<Task<(Outcome, string)>> run)
    {
        Console.WriteLine($"  · {device}: {check}");
        try
        {
            var (o, d) = await run();
            R.Add(Area, "", device, check, o, d);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            R.Add(Area, "", device, check, Outcome.Fail, $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
    }

    public async Task RunAsync()
    {
        await Step("Test patient", "Find a demonstration patient with a mobile number", FindPatientAsync);
        if (_patient is null) return;
        if (sims.CertificateTrusted || !bot.O.TrustCertificate)
        {
            await Step("SMS company (virtual Modica)", "Point BrightPAS at the virtual SMS company and email server", ConfigureMessagingAsync);
            await Step("Virtual phones", "Send a test SMS and receive it on a virtual phone", TestSmsAsync);
            await Step("Virtual phones", "Delivery receipt comes back and the message shows as delivered", ReceiptAsync);
            await Step("Virtual phones", "Patient texts back – reply arrives in BrightPAS", ReplyAsync);
            await Step("Virtual email", "Send a test email and receive it in a virtual inbox", TestEmailAsync);
            await Step("Virtual phones", "Send all reminders that are due now – check every message", RunRemindersAsync);
            await Step("Patient portal on a phone", "Portal link by SMS → patient opens it on a phone → sends a question → reception sees it", PortalAsync);
        }
        else R.Add(Area, "", "SMS company (virtual Modica)", "SMS and email tests", Outcome.Skip, "The server does not trust the virtual SMS company's certificate. Run the robot from an administrator command prompt on the BrightPAS server.");
        await Step("Virtual Zebra printer", "Print a wristband and labels from the patient record", ZebraAsync);
        await Step("Virtual washer (Belimed log)", "Import a washer log, release the load, pack the tray, print its label on the virtual Zebra and issue it to a case", CssdAsync);
        await EndoscopyStepsAsync();
        await ScribeStepsAsync();
        await Step("Virtual DYMO printer", "Print DYMO labels from the patient record", DymoAsync);
        await Step("Virtual scanner", "Scan a letter to the patient record; text recognition finds a word in it", ScanAsync);
        await Step("Virtual barcode scanner", "Scan a patient label barcode into Find a patient", BarcodeAsync);
        await Step("Virtual HealthLink", "Send a letter to a GP by HealthLink and get the acknowledgement", HealthLinkAsync);
        await EndoscopyGpStepsAsync();
        await Step("Virtual laboratory (MLLP)", "Laboratory sends a result over MLLP; it arrives in Lab results", LabAsync);
        await DevicesStepsAsync();
        await ImportStepsAsync();
        await QualityStepsAsync();
        await CodingStepsAsync();
        await EstimatesStepsAsync();
        await TriageStepsAsync();
        await JourneyStepsAsync();
        await Step("Exports", "Payroll export for Xero is encrypted, and the timesheet file is in the expected shape", PayrollAsync);
        await Step("Exports", "Letter as a password-protected PDF", PdfAsync);
    }

    // ------------------------------------------------------------------ test patient
    private async Task<(Outcome, string)> FindPatientAsync()
    {
        var s = await As("reception");
        foreach (var q in new[] { "Wilson", "Smith", "Brown", "Taylor", "a", "e" })
        {
            var r = await s.GetAsync("/api/patients?q=" + q);
            if (r.Json is not { ValueKind: JsonValueKind.Array } arr) continue;
            foreach (var p in arr.EnumerateArray().Take(15))
            {
                var d = await s.GetAsync("/api/patients/" + p.GetProperty("id").GetString());
                if (d.Json is not { } j) continue;
                var mobile = Str(j, "mobilePhone");
                if (mobile is null || !mobile.Replace(" ", "").StartsWith("02", StringComparison.Ordinal)) continue;
                _patient = new TestPatient(Str(j, "id")!, Str(j, "familyName")!, Str(j, "givenNames")!, Str(j, "dateOfBirth")!, Str(j, "sex") ?? "", Str(j, "nhi"), Str(j, "mrn")!, mobile);
                return (Outcome.Pass, $"{_patient.Family.ToUpperInvariant()}, {_patient.Given} (NHI {_patient.Nhi ?? "none"}, mobile {mobile})");
            }
        }
        return (Outcome.Fail, "No demonstration patient with a mobile number found – device tests cannot run.");
    }

    /// <summary>First array item matching, or null (JsonElement is a struct, so FirstOrDefault would return an unusable empty element).</summary>
    private static JsonElement? Find(JsonElement? arr, Func<JsonElement, bool> match)
    {
        if (arr is not { ValueKind: JsonValueKind.Array } a) return null;
        foreach (var x in a.EnumerateArray()) if (match(x)) return x;
        return null;
    }

    private static string? Str(JsonElement j, string name) => j.ValueKind != JsonValueKind.Object ? null : j.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind is JsonValueKind.Number ? v.GetRawText() : null;
    private static string E164(string nz) { var d = new string(nz.Where(char.IsDigit).ToArray()); return d.StartsWith('0') ? "+64" + d[1..] : "+" + d; }

    // ------------------------------------------------------------------ SMS and email
    private Session? _rem;
    private JsonElement _remOriginal;

    private async Task<(Outcome, string)> ConfigureMessagingAsync()
    {
        _rem = await WhoCanAsync("/api/reminders/settings");
        if (_rem is null) return (Outcome.Fail, "No demo account can open SMS & email settings.");
        var g = await _rem.GetAsync("/api/reminders/settings");
        _remOriginal = g.Json!.Value;
        var cb = _remOriginal.GetProperty("callbacks");
        sims.ReceiptUrl = cb.GetProperty("deliveryReceipts").GetString()!.Replace(new Uri(cb.GetProperty("deliveryReceipts").GetString()!).GetLeftPart(UriPartial.Authority), bot.O.Url);
        sims.ReplyUrl = cb.GetProperty("replies").GetString()!.Replace(new Uri(cb.GetProperty("replies").GetString()!).GetLeftPart(UriPartial.Authority), bot.O.Url);
        // No quiet hours while testing, so messages go straight away whatever the time of day (put back afterwards).
        var put = await _rem.PutAsync("/api/reminders/settings", Settings(_remOriginal, "Modica", "Smtp", sims.ModicaUrl, sims.ModicaApp, sims.ModicaPassword, "localhost", bot.O.SmtpPort, false, "reception@tds-demo.example.nz", "00:00", "00:00"));
        if (put.Status != 200) return (Outcome.Fail, $"Settings refused ({put.Status}): {put.Text}");
        var orig = _remOriginal;
        _restore.Add(async () =>
        {
            var r = await _rem.PutAsync("/api/reminders/settings", Settings(orig, Str(orig, "smsProvider")!, Str(orig, "emailProvider")!, Str(orig, "modicaBaseUrl"), Str(orig, "modicaApplication"), null,
                Str(orig, "smtpHost"), orig.GetProperty("smtpPort").GetInt32(), orig.GetProperty("smtpStartTls").GetBoolean(), Str(orig, "fromAddress")));
            R.Add(Area, _rem.Account, "SMS company (virtual Modica)", "Put SMS & email settings back", r.Status == 200 ? Outcome.Pass : Outcome.Warn, r.Status == 200 ? $"SMS {Str(orig, "smsProvider")}, email {Str(orig, "emailProvider")}" : r.Text);
        });
        var status = await _rem.GetAsync("/api/reminders/status");
        var problems = status.Json is { } sj && sj.TryGetProperty("problems", out var pr) ? string.Join(" ", pr.EnumerateArray().Select(x => x.ToString())) : "";
        return (Outcome.Pass, $"As {_rem.Account}: SMS via {sims.ModicaUrl}, email via localhost:{bot.O.SmtpPort}. Was: SMS {Str(orig, "smsProvider")}, email {Str(orig, "emailProvider")}. {(problems.Length > 0 ? "Problems listed: " + problems : "")}");
    }

    private static object Settings(JsonElement o, string sms, string email, string? modicaUrl, string? modicaApp, string? modicaPassword, string? smtpHost, int smtpPort, bool startTls, string? from,
        string? quietStart = null, string? quietEnd = null) => new
        {
            smsProvider = sms,
            emailProvider = email,
            sendBookingConfirmation = o.GetProperty("sendBookingConfirmation").GetBoolean(),
            sendDayBeforeReminder = o.GetProperty("sendDayBeforeReminder").GetBoolean(),
            sendEmailCopies = o.GetProperty("sendEmailCopies").GetBoolean(),
            dayBeforeHour = o.GetProperty("dayBeforeHour").GetInt32(),
            arrivalLeadMinutes = o.GetProperty("arrivalLeadMinutes").GetInt32(),
            quietStart = quietStart ?? Str(o, "quietStart"),
            quietEnd = quietEnd ?? Str(o, "quietEnd"),
            clinicName = Str(o, "clinicName") ?? "Tasman Day Surgery",
            clinicPhone = Str(o, "clinicPhone"),
            modicaBaseUrl = modicaUrl,
            modicaApplication = modicaApp,
            modicaPassword,
            modicaMask = Str(o, "modicaMask"),
            genericUrl = Str(o, "genericUrl"),
            genericAuthHeader = Str(o, "genericAuthHeader"),
            genericAuthValue = (string?)null,
            genericBodyTemplate = Str(o, "genericBodyTemplate"),
            smtpHost,
            smtpPort,
            smtpStartTls = startTls,
            smtpUser = (string?)null,
            smtpPassword = (string?)null,
            fromAddress = from ?? "reception@tds-demo.example.nz",
            fromName = Str(o, "fromName") ?? "Tasman Day Surgery",
        };

    private const string TestPhone = "021 555 0101";
    private string? _testSmsId;

    private async Task<(Outcome, string)> TestSmsAsync()
    {
        if (_rem is null) return (Outcome.Skip, "Settings not changed.");
        var r = await _rem.PostAsync("/api/reminders/test", new { channel = "Sms", to = TestPhone });
        var got = await Sims.WaitForAsync(sims.SmsOut, x => x.To == E164(TestPhone), TimeSpan.FromSeconds(15));
        if (got is null) return (Outcome.Fail, $"BrightPAS answered {r.Status} {r.Text} but the virtual phone {TestPhone} received nothing.");
        _testSmsId = got.Id;
        if (r.Status != 200) return (Outcome.Fail, $"The phone received the SMS, but BrightPAS answered {r.Status} to Send test (the screen shows an error): {Report.Trim(r.Text, 300)}");
        return (Outcome.Pass, $"Phone {got.To} received: \"{got.Text}\" (BrightPAS said {r.Text})");
    }

    private async Task<(Outcome, string)> ReceiptAsync()
    {
        if (_rem is null || _testSmsId is null) return (Outcome.Skip, "No test SMS.");
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(1000);
            var m = await _rem.GetAsync("/api/reminders/messages?patientId=" + Guid.Empty);
            var row = Find(m.Json, x => Str(x, "address")?.Replace(" ", "").EndsWith("5550101", StringComparison.Ordinal) == true || Str(x, "address") == E164(TestPhone));
            if (row is { } rr && Str(rr, "status") == "Delivered") return (Outcome.Pass, "Status Delivered after the SMS company's receipt.");
        }
        return (Outcome.Fail, "The SMS company sent a delivery receipt but the test message never showed as Delivered.");
    }

    private async Task<(Outcome, string)> ReplyAsync()
    {
        if (_rem is null) return (Outcome.Skip, "Settings not changed.");
        var from = E164(_patient!.Mobile!);
        var code = await sims.ReplyAsync(from, "Robot test reply: is parking available?", null);
        if (code != 200) return (Outcome.Fail, $"BrightPAS answered {code} to the SMS company's reply callback.");
        await Task.Delay(1500);
        var r = await _rem.GetAsync("/api/reminders/replies?view=new");
        var hit = Find(r.Json, x => Str(x, "body")?.Contains("Robot test reply", StringComparison.Ordinal) == true);
        return hit is { } h ? (Outcome.Pass, $"Reply listed for {Str(h, "patientName") ?? "unknown patient"} – meaning {Str(h, "meaning")}, action: {Str(h, "actionTaken")}")
            : (Outcome.Fail, "Reply accepted but not listed under SMS & email › Replies.");
    }

    private async Task<(Outcome, string)> TestEmailAsync()
    {
        if (_rem is null) return (Outcome.Skip, "Settings not changed.");
        var to = "robot.inbox@example.co.nz";
        var r = await _rem.PostAsync("/api/reminders/test", new { channel = "Email", to });
        var got = await Sims.WaitForAsync(sims.Mail, x => x.To == to, TimeSpan.FromSeconds(15));
        return got is null ? (Outcome.Fail, $"BrightPAS answered {r.Status} {r.Text} but the virtual inbox received nothing.")
            : r.Status != 200 ? (Outcome.Fail, $"The inbox received the email, but BrightPAS answered {r.Status} to Send test: {Report.Trim(r.Text, 300)}")
            : (Outcome.Pass, $"Inbox {to} received \"{got.Subject}\" from {got.From}.");
    }

    [GeneratedRegex(@"\{[A-Za-z]+\}")]
    private static partial Regex Placeholder();

    private async Task<(Outcome, string)> RunRemindersAsync()
    {
        if (_rem is null) return (Outcome.Skip, "Settings not changed.");
        var before = sims.SmsOut.Count; var mailBefore = sims.Mail.Count;
        var r = await _rem.PostAsync("/api/reminders/run-now", new { });
        await Task.Delay(4000);
        var sms = sims.SmsOut.Skip(before).ToList(); var mail = sims.Mail.Skip(mailBefore).ToList();
        var issues = new List<string>();
        foreach (var m in sms)
        {
            if (Placeholder().IsMatch(m.Text)) issues.Add($"SMS to {m.To} still has a {{placeholder}}: {m.Text}");
            if (m.Text.Length > 459) issues.Add($"SMS to {m.To} is {m.Text.Length} characters (more than 3 SMS parts – costs more): {Report.Trim(m.Text, 80)}");
            if (m.Text.Contains("  ", StringComparison.Ordinal) || m.Text.Contains(" ,", StringComparison.Ordinal) || m.Text.Contains("()", StringComparison.Ordinal)) issues.Add($"SMS to {m.To} looks like something is missing: {m.Text}");
        }
        foreach (var m in mail) if (Placeholder().IsMatch(m.Body + m.Subject)) issues.Add($"Email to {m.To} still has a {{placeholder}}: {m.Subject}");
        var failed = r.Json is { } j && j.TryGetProperty("failed", out var f) ? f.GetInt32() : 0;
        if (failed > 0) issues.Add($"{failed} message(s) failed to send – see SMS & email › Messages.");
        var detail = $"BrightPAS: {r.Text}. Virtual phones got {sms.Count} SMS, inboxes got {mail.Count} emails.";
        return issues.Count > 0 ? (Outcome.Fail, detail + " " + string.Join(" | ", issues)) : (Outcome.Pass, detail + (sms.Count == 0 ? " (nothing was due – fine)" : " Every message read correctly."));
    }

    private async Task<(Outcome, string)> PortalAsync()
    {
        var staff = await WhoCanAsync($"/api/portal/patients/{_patient!.Id}/link");
        if (staff is null) return (Outcome.Skip, "No demo account manages portal links.");
        var before = sims.SmsOut.Count;
        var r = await staff.PostAsync($"/api/portal/patients/{_patient.Id}/link", new { sendSms = true, sendEmail = false });
        if (r.Status != 200) return (Outcome.Fail, $"Could not create the link ({r.Status}): {r.Text}");
        var sms = await Sims.WaitForAsync(sims.SmsOut, x => x.To == E164(_patient.Mobile!) && sims.SmsOut.ToList().IndexOf(x) >= before, TimeSpan.FromSeconds(15));
        if (sms is null) return (Outcome.Fail, $"Link made ({r.Text}) but no SMS reached the patient's virtual phone.");
        var m = Regex.Match(sms.Text, @"https?://\S+/my/\S+");
        if (!m.Success) return (Outcome.Fail, $"The SMS has no portal link: {sms.Text}");
        var link = bot.O.Url + new Uri(m.Value.TrimEnd('.', ',', ')')).PathAndQuery;

        // The patient, on an iPhone-size screen, not signed in.
        await using var phone = await bot.NewContextAsync("patient", new ViewportSize { Width = 390, Height = 844 }, mobile: true);
        var p = phone.Page;
        await p.GotoAsync(link);
        await p.Locator("input[type=date]").FillAsync(_patient.Dob[..10]);
        await p.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();
        await p.Locator("h1:has-text('Kia ora')").WaitForAsync(new() { Timeout = 15000 });
        var shot = await phone.ShotAsync("portal-phone", fullPage: true);
        R.Gallery("/my/:token — patient phone", shot);
        foreach (var n in await Usability.CheckAsync(p, null, 0, desktop: false)) R.Add("screens", "patient", "/my (patient portal, phone)", n.Check, n.Outcome, n.Detail, n.Outcome == Outcome.Warn ? shot : null);
        foreach (var n in await Usability.LeaksAsync(p)) R.Add("screens", "patient", "/my (patient portal, phone)", n.Check, n.Outcome, n.Detail, shot);
        await p.Locator("textarea").FillAsync("Robot test: can I bring a support person on the day?");
        await p.GetByRole(AriaRole.Button, new() { Name = "Send", Exact = true }).ClickAsync();
        await p.Locator("text=Thank you").First.WaitForAsync(new() { Timeout = 10000 });
        var seen = false;
        for (var i = 0; i < 10 && !seen; i++) { if (i > 0) await Task.Delay(1000); seen = (await staff.GetAsync("/api/portal/requests")).Text.Contains("support person", StringComparison.Ordinal); }
        return seen ? (Outcome.Pass, $"SMS: \"{sms.Text}\". Patient opened it on a phone, entered date of birth, sent a question; it is in Portal requests.")
            : (Outcome.Fail, "Patient sent a question from the portal but it is not in Portal requests.");
    }

    // ------------------------------------------------------------------ printers
    private Session? _org;
    private List<JsonElement> _printersOriginal = [];
    private string? _dymoMedia;

    private async Task<bool> PrintersAsync()
    {
        if (_org is not null) return true;
        _org = await WhoCanAsync("/api/admin/printers");
        if (_org is null) return false;
        var g = await _org.GetAsync("/api/admin/printers");
        _printersOriginal = g.Json!.Value.GetProperty("printers").EnumerateArray().ToList();
        _dymoMedia = g.Json.Value.GetProperty("dymoMedia").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : Str(x, "code")).FirstOrDefault();
        _restore.Add(async () =>
        {
            foreach (var p in _printersOriginal.Where(p => p.GetProperty("isDefault").GetBoolean()))
                await _org.PostAsync("/api/admin/printers", PrinterBody(p));
            var now = await _org.GetAsync("/api/admin/printers");
            foreach (var p in now.Json!.Value.GetProperty("printers").EnumerateArray().Where(p => Str(p, "name")!.StartsWith("TestBot", StringComparison.Ordinal)))
            {
                var body = PrinterBody(p, isDefault: false, isActive: false);
                await _org.PostAsync("/api/admin/printers", body);
            }
            R.Add(Area, _org.Account, "Printers", "Put the default printers back (virtual ones switched off)", Outcome.Pass);
        });
        return true;
    }

    private static object PrinterBody(JsonElement p, bool? isDefault = null, bool? isActive = null) => new
    {
        id = Str(p, "id"),
        name = Str(p, "name"),
        kind = Str(p, "kind"),
        host = Str(p, "host") ?? "",
        port = p.TryGetProperty("port", out var po) && po.ValueKind == JsonValueKind.Number ? po.GetInt32() : 9100,
        dpi = p.TryGetProperty("dpi", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : 203,
        widthMm = p.GetProperty("widthMm").GetInt32(),
        lengthMm = p.GetProperty("lengthMm").GetInt32(),
        isDefault = isDefault ?? p.GetProperty("isDefault").GetBoolean(),
        isActive = isActive ?? p.GetProperty("isActive").GetBoolean(),
        language = Str(p, "language") ?? "Zpl",
        mediaCode = Str(p, "mediaCode"),
    };

    /// <summary>Adds a virtual printer, or updates it if an earlier robot run left it switched off.</summary>
    private async Task<string?> AddPrinterAsync(Dictionary<string, object?> body)
    {
        var existing = await _org!.GetAsync("/api/admin/printers");
        if (Find(existing.Json?.GetProperty("printers"), p => Str(p, "name") == (string?)body["name"]) is { } old) body["id"] = Str(old, "id");
        var r = await _org!.PostAsync("/api/admin/printers", body);
        return r.Status == 200 ? null : $"Printer refused ({r.Status}): {r.Text}";
    }

    private async Task<Session> PatientPageAsync()
    {
        var s = await As("reception");
        await s.Page.GotoAsync($"{bot.O.Url}/patients/{_patient!.Id}");
        try { await s.Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10000 }); } catch (TimeoutException) { }
        return s;
    }

    private async Task<(Outcome, string)> ZebraAsync()
    {
        if (!await PrintersAsync()) return (Outcome.Skip, "No demo account manages printers.");
        var e1 = await AddPrinterAsync(new() { ["id"] = null, ["name"] = "TestBot virtual Zebra wristband", ["kind"] = "Wristband", ["host"] = "127.0.0.1", ["port"] = bot.O.ZebraPort, ["dpi"] = 300, ["widthMm"] = 25, ["lengthMm"] = 254, ["isDefault"] = true, ["isActive"] = true, ["language"] = "Zpl", ["mediaCode"] = null });
        var e2 = await AddPrinterAsync(new() { ["id"] = null, ["name"] = "TestBot virtual Zebra labels", ["kind"] = "Label", ["host"] = "127.0.0.1", ["port"] = bot.O.ZebraPort, ["dpi"] = 203, ["widthMm"] = 57, ["lengthMm"] = 32, ["isDefault"] = true, ["isActive"] = true, ["language"] = "Zpl", ["mediaCode"] = null });
        if ((e1 ?? e2) is { } err) return (Outcome.Fail, err);
        var s = await PatientPageAsync();
        var before = sims.Labels.Count;
        var notes = new List<string>(); var fails = new List<string>();
        foreach (var (button, kind) in new[] { ("Print wristband", "wristband"), ("Print labels", "label") })
        {
            var b = s.Page.GetByRole(AriaRole.Button, new() { Name = button, Exact = true });
            if (await b.CountAsync() == 0) { fails.Add($"No \"{button}\" button on the patient record."); continue; }
            await b.First.ClickAsync();
            var got = await Sims.WaitForAsync(sims.Labels, x => sims.Labels.ToList().IndexOf(x) >= before, TimeSpan.FromSeconds(15));
            if (got is null) { fails.Add($"{button}: the virtual Zebra printer received nothing. Screen says: {await MsgAsync(s)}"); continue; }
            before = sims.Labels.Count;
            var check = CheckZpl(got.Zpl, _patient!);
            if (check.Count > 0) fails.Add($"{kind}: {string.Join(" ", check)}"); else notes.Add($"{kind} OK ({got.Zpl.Length} bytes, name, DOB and NHI/MRN present, fits the label)");
        }
        return fails.Count > 0 ? (Outcome.Fail, string.Join(" | ", fails.Concat(notes))) : (Outcome.Pass, string.Join("; ", notes) + ". Pictures of the labels are in the report.");
    }

    private static async Task<string> MsgAsync(Session s)
    {
        try { return string.Join(" ", await s.Page.Locator("main .error:visible, main [role=status]:visible, main .ok-text:visible").AllInnerTextsAsync()); } catch (PlaywrightException) { return ""; }
    }

    /// <summary>The label must start/end correctly, carry the patient's identifiers, and keep text inside the label.</summary>
    private static List<string> CheckZpl(string zpl, TestPatient p)
    {
        var e = new List<string>();
        if (!zpl.TrimStart().StartsWith("^XA", StringComparison.Ordinal) || !zpl.TrimEnd().EndsWith("^XZ", StringComparison.Ordinal)) e.Add("Not a complete ZPL label (^XA…^XZ).");
        if (!zpl.Contains(p.Family.ToUpperInvariant(), StringComparison.Ordinal)) e.Add("Patient family name missing.");
        if (p.Nhi is not null && !zpl.Contains(p.Nhi, StringComparison.Ordinal)) e.Add("NHI missing.");
        if (!zpl.Contains(p.Mrn, StringComparison.Ordinal)) e.Add("MRN missing.");
        var dob = DateOnly.Parse(p.Dob[..10], CultureInfo.InvariantCulture).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        if (!zpl.Contains(dob, StringComparison.Ordinal)) e.Add("Date of birth missing.");
        if (!zpl.Contains("^BC", StringComparison.Ordinal)) e.Add("No barcode.");
        var pw = Num(zpl, @"\^PW(\d+)"); var ll = Num(zpl, @"\^LL(\d+)");
        foreach (Match f in Regex.Matches(zpl, @"\^FO(\d+),(\d+)\^A0([NR]),(\d+),(\d+)(?:\^FB(\d+)[^\^]*)?(?:\^FR)?\^FD([^\^]*)\^FS"))
        {
            int x = int.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture), y = int.Parse(f.Groups[2].Value, CultureInfo.InvariantCulture), h = int.Parse(f.Groups[4].Value, CultureInfo.InvariantCulture);
            var len = (int)(f.Groups[7].Value.Length * h * 0.55);
            if (f.Groups[6].Success) continue; // field block wraps/clips inside its width
            if (f.Groups[3].Value == "N" && pw > 0 && x + len > pw) e.Add($"Text \"{f.Groups[7].Value}\" runs off the side of the label (needs {x + len} of {pw} dots).");
            if (f.Groups[3].Value == "R" && ll > 0 && y + len > ll) e.Add($"Text \"{f.Groups[7].Value}\" runs off the end of the band (needs {y + len} of {ll} dots).");
        }
        return e;
    }

    private static int Num(string s, string re) { var m = Regex.Match(s, re); return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0; }

    /// <summary>Draws a picture of a ZPL label so a person can see what would have printed.</summary>
    public async Task<string?> RenderZplAsync(string zpl)
    {
        var first = zpl.Split("^XZ")[0];
        int pw = Math.Max(Num(first, @"\^PW(\d+)"), 200), ll = Math.Max(Num(first, @"\^LL(\d+)"), 100);
        var scale = Math.Min(1.0, 1100.0 / Math.Max(pw, ll));
        var sb = new StringBuilder($"<html><body style='margin:0;background:#888'><div id='l' style='position:relative;background:#fff;width:{pw * scale}px;height:{ll * scale}px;overflow:hidden;font-family:Arial'>");
        foreach (Match f in Regex.Matches(first, @"\^FO(\d+),(\d+)((?:\^(?!FD)[A-Z0-9]{2}[^\^]*)*)\^FD([^\^]*)\^FS"))
        {
            double x = int.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture) * scale, y = int.Parse(f.Groups[2].Value, CultureInfo.InvariantCulture) * scale;
            var cmds = f.Groups[3].Value; var text = WebUtility.HtmlEncode(f.Groups[4].Value);
            var rot = cmds.Contains("^A0R", StringComparison.Ordinal) || cmds.Contains("^BCR", StringComparison.Ordinal);
            var t = rot ? "transform:rotate(90deg);transform-origin:0 0;" : "";
            var rev = cmds.Contains("^FR", StringComparison.Ordinal) ? "background:#000;color:#fff;" : "";
            var font = Regex.Match(cmds, @"\^A0[NR],(\d+)");
            if (cmds.Contains("^BC", StringComparison.Ordinal))
            {
                var h = Num(cmds, @"\^BC[NR],(\d+)") * scale;
                sb.Append(CultureInfo.InvariantCulture, $"<div style='position:absolute;left:{x + (rot ? h : 0)}px;top:{y}px;{t}height:{h}px;width:{text.Length * 22 * scale}px;background:repeating-linear-gradient(90deg,#000 0 2px,#fff 2px 4px,#000 4px 7px,#fff 7px 9px)' title='barcode {text}'></div>");
            }
            else
            {
                var h = (font.Success ? int.Parse(font.Groups[1].Value, CultureInfo.InvariantCulture) : 20) * scale;
                sb.Append(CultureInfo.InvariantCulture, $"<div style='position:absolute;left:{x + (rot ? h : 0)}px;top:{y}px;{t}{rev}font-size:{h * 0.9}px;line-height:{h}px;white-space:nowrap'>{text}</div>");
            }
        }
        sb.Append("</div></body></html>");
        var page = await (await As("reception")).Ctx.NewPageAsync();
        try
        {
            await page.SetContentAsync(sb.ToString());
            var name = $"label-{DateTime.Now:HHmmssfff}.png";
            await page.Locator("#l").ScreenshotAsync(new() { Path = Path.Combine(R.DeviceFolder, name) });
            return name;
        }
        catch (PlaywrightException) { return null; }
        finally { await page.CloseAsync(); }
    }

    private async Task<(Outcome, string)> DymoAsync()
    {
        if (!await PrintersAsync()) return (Outcome.Skip, "No demo account manages printers.");
        var err = await AddPrinterAsync(new() { ["id"] = null, ["name"] = "TestBot virtual DYMO", ["kind"] = "Label", ["host"] = "", ["port"] = 9100, ["dpi"] = 300, ["widthMm"] = 54, ["lengthMm"] = 25, ["isDefault"] = true, ["isActive"] = true, ["language"] = "Dymo", ["mediaCode"] = _dymoMedia });
        if (err is not null) return (Outcome.Fail, err);
        var s = await PatientPageAsync();
        var link = s.Page.GetByRole(AriaRole.Link, new() { Name = "Print labels (DYMO)" });
        if (await link.CountAsync() == 0) return (Outcome.Fail, "No \"Print labels (DYMO)\" on the patient record after setting a DYMO printer as the default.");
        await link.First.ClickAsync();
        await s.Page.Locator(".dymo-label").First.WaitForAsync(new() { Timeout = 15000 });
        await s.Page.WaitForTimeoutAsync(800);
        var printed = await s.Page.EvaluateAsync<int>("() => window.__bpPrints || 0");
        // What the printer gets: the page in print layout.
        await s.Page.EmulateMediaAsync(new() { Media = Media.Print });
        var name = $"dymo-{DateTime.Now:HHmmss}.png";
        await s.Page.Locator(".dymo-label").First.ScreenshotAsync(new() { Path = Path.Combine(R.DeviceFolder, name) });
        var box = await s.Page.Locator(".dymo-label").First.BoundingBoxAsync();
        var fits = await s.Page.Locator(".dymo-label").First.EvaluateAsync<bool>("e => [...e.children].every(c => { const a = c.getBoundingClientRect(), b = e.getBoundingClientRect(); return a.right <= b.right + 1 && a.bottom <= b.bottom + 1; })");
        var text = await s.Page.Locator(".dymo-label").First.InnerTextAsync();
        await s.Page.EmulateMediaAsync(new() { Media = Media.Screen });
        R.Device("Virtual DYMO printer", $"Label sheet ({_dymoMedia}) – print dialog opened {printed} time(s)", text, name);
        var fails = new List<string>();
        if (printed == 0) fails.Add("The print window did not open.");
        if (!text.Contains(_patient!.Family.ToUpperInvariant(), StringComparison.Ordinal)) fails.Add("Patient name missing on the label.");
        if (!text.Contains(_patient.Mrn, StringComparison.Ordinal)) fails.Add("MRN missing.");
        if (!fits) fails.Add("Something on the label runs past its edge (would be cut off).");
        return fails.Count > 0 ? (Outcome.Fail, string.Join(" ", fails)) : (Outcome.Pass, $"Print window opened; label {box?.Width:0}×{box?.Height:0}px shows name, DOB, NHI/MRN and barcode inside the edges.");
    }

    // ------------------------------------------------------------------ CSSD
    private async Task<(Outcome, string)> CssdAsync()
    {
        var s = await As("cssd");
        if ((await s.GetAsync("/api/assets/dashboard")).Status != 200) return (Outcome.Skip, "Assets & CSSD module not available to the CSSD account.");
        var machines = (await s.GetAsync("/api/assets/equipment?category=Washer")).Json;
        var washer = Find(machines, m => Str(m, "status") == "InService");
        if (washer is not { } w) return (Outcome.Skip, "No washer-disinfector in service.");
        var dirty = Find((await s.GetAsync("/api/assets/trays?status=Dirty")).Json, _ => true);
        if (dirty is not { } tray) return (Outcome.Skip, "No dirty tray to wash in the demonstration data.");
        var barcode = Str(tray, "barcode")!;
        var log = $"BELIMED WD 290 IQ\nBatch no.: 77{Random.Shared.Next(100, 999)}\nProgram: Instruments Standard\nStart: {DateTime.Now.AddMinutes(-60):dd.MM.yyyy HH:mm:ss}\nEnd: {DateTime.Now.AddMinutes(-5):dd.MM.yyyy HH:mm:ss}\nDisinfection temp: 93.2 °C\nHolding time: 05:00\nA0 value: 3010\nResult: Cycle OK\n";
        R.Device("Virtual washer (Belimed log)", "Cycle log produced by the virtual washer", log);
        var imp = await s.PostAsync("/api/assets/cycles/import", new
        {
            machineId = Str(w, "id"),
            kind = "Wash",
            fileName = "WD290-virtual.log",
            text = log,
            trayBarcodes = new[] { barcode },
            otherItems = Array.Empty<object>(),
            chemicalIndicator = "NotUsed",
            biologicalIndicator = "NotUsed",
            bowieDickTest = false,
            startedAt = (string?)null
        });
        if (imp.Status != 200) return (Outcome.Fail, $"Log import refused ({imp.Status}): {imp.Text}");
        var parsed = imp.Json!.Value.GetProperty("parsed");
        if (parsed.GetProperty("notes").GetArrayLength() > 0 || Str(parsed, "a0") != "3010") return (Outcome.Fail, $"The log was not read fully: {parsed}");
        var id = Str(imp.Json.Value, "id")!;
        var rel = await s.PostAsync($"/api/assets/cycles/{id}/release", new { release = true, note = (string?)null });
        if (rel.Status != 200) return (Outcome.Fail, $"Release refused: {rel.Text}");
        var pack = await s.PostAsync("/api/assets/trays/pack", new { barcode, contentsChecked = true });
        if (pack.Status != 200) return (Outcome.Fail, $"Packing refused: {pack.Text}");
        var notes = new List<string> { $"load {Str(imp.Json.Value, "loadNumber")} imported (A0 3010, cycle OK) and released; {barcode} packed" };
        // A sterile tray: print its label on the virtual Zebra (the default label printer during the device tests) and issue it to today's case.
        var sterile = Find((await s.GetAsync("/api/assets/trays?status=Sterile")).Json, _ => true);
        if (sterile is { } st)
        {
            var before = sims.Labels.Count;
            var pr = await s.PostAsync($"/api/assets/trays/{Str(st, "id")}/print", new { });
            var label = await Sims.WaitForAsync(sims.Labels, x => sims.Labels.ToList().IndexOf(x) >= before, TimeSpan.FromSeconds(10));
            if (pr.Status != 200 || label is null) return (Outcome.Fail, string.Join("; ", notes) + $". Tray label did not reach the virtual Zebra ({pr.Status} {pr.Text}).");
            if (!label.Zpl.Contains(Str(st, "barcode")!, StringComparison.Ordinal) || !label.Zpl.Contains("Use by", StringComparison.Ordinal)) return (Outcome.Fail, "Tray label is missing the barcode or use-by date.");
            notes.Add($"label for {Str(st, "barcode")} printed with barcode, load and use-by date");
            var cases = (await s.GetAsync("/api/assets/cases")).Json;
            if (Find(cases, _ => true) is { } c)
            {
                var iss = await s.PostAsync("/api/assets/trays/issue", new { barcode = Str(st, "barcode"), bookingId = Str(c, "bookingId") });
                if (iss.Status != 200) return (Outcome.Fail, string.Join("; ", notes) + $". Issue to case refused: {iss.Text}");
                var trace = await s.GetAsync($"/api/assets/trace?patientId={Str(c, "patientId")}");
                if (!trace.Text.Contains(Str(st, "barcode")!, StringComparison.Ordinal)) return (Outcome.Fail, "Tray issued but not found when tracing the patient.");
                notes.Add($"issued to {Str(c, "patientName")} and found by tracing the patient");
            }
            else notes.Add("no theatre case today to issue to (weekend?) – issue not tested");
        }
        return (Outcome.Pass, string.Join("; ", notes) + ".");
    }

    // ------------------------------------------------------------------ scanning
    private async Task<(Outcome, string)> ScanAsync()
    {
        var word = "ROBOTSCAN" + Random.Shared.Next(1000, 9999).ToString(CultureInfo.InvariantCulture);
        var s = await PatientPageAsync();
        // A "scanned" referral letter: a printed page photographed as an image.
        var page = await s.Ctx.NewPageAsync();
        await page.SetViewportSizeAsync(1240, 1754);
        await page.SetContentAsync($"""
            <html><body style="margin:80px;font-family:'Times New Roman',serif;font-size:30px;line-height:1.5;background:#fdfdf8;color:#111">
            <p style="text-align:right">Richmond Family Health<br>20 Queen Street, Richmond</p><p>{DateTime.Today:d MMMM yyyy}</p>
            <p>Dear Tasman Day Surgery,</p><p>Re: {_patient!.Given} {_patient.Family}, NHI {_patient.Nhi}</p>
            <p>Thank you for seeing this patient. Reference number {word}. Please review the hernia and arrange surgery.</p>
            <p>Yours sincerely,<br>Dr Robot Tester</p></body></html>
            """);
        var file = Path.Combine(R.DeviceFolder, $"scanned-letter-{word}.png");
        await page.ScreenshotAsync(new() { Path = file });
        await page.CloseAsync();
        var input = s.Page.Locator("input[type=file][capture]");
        if (await input.CountAsync() == 0) return (Outcome.Fail, "No \"Scan with camera / scanner\" on the patient record.");
        var mark = s.Mark();
        await input.First.SetInputFilesAsync(file);
        await s.Page.WaitForTimeoutAsync(2500);
        var issues = (await s.IssuesSinceAsync(mark)).Where(i => i.Status >= 400 || i.Kind == "script").ToList();
        if (issues.Count > 0) return (Outcome.Fail, "Upload failed: " + string.Join(" ", issues.Select(i => $"{i.Status} {i.Path} {i.Detail}")));
        R.Device("Virtual scanner", $"Scanned page with the word {word}", "A4 page image attached to the patient record as a scan.", Path.GetFileName(file));
        for (var i = 0; i < 45; i++)
        {
            var r = await s.GetAsync($"/api/patients/{_patient.Id}/documents/search?q={word}");
            if (r.Json is { ValueKind: JsonValueKind.Array } a && a.GetArrayLength() > 0) return (Outcome.Pass, $"Scan stored; text recognition read it and a search for {word} finds it after about {i * 2} s.");
            await Task.Delay(2000);
        }
        var docs = await s.GetAsync($"/api/patients/{_patient.Id}/documents");
        var status = Find(docs.Json, d => Str(d, "fileName")?.Contains(word, StringComparison.Ordinal) == true);
        return (Outcome.Fail, $"The scan was stored but searching for {word} found nothing after 90 s (text status: {(status is { } st ? Str(st, "textStatus") : "?")}). Check Security & printers › document text.");
    }

    private async Task<(Outcome, string)> BarcodeAsync()
    {
        var s = await As("reception");
        await s.Page.GotoAsync(bot.O.Url + "/patients");
        var box = s.Page.Locator("main input").First;
        await box.ClickAsync();
        // A USB barcode scanner "types" very fast and presses Enter.
        await s.Page.Keyboard.TypeAsync(_patient!.Nhi ?? _patient.Mrn, new() { Delay = 8 });
        await s.Page.Keyboard.PressAsync("Enter");
        await s.Page.WaitForTimeoutAsync(1500);
        var found = await s.Page.Locator($"main :text('{_patient.Family.ToUpperInvariant()}')").CountAsync() > 0;
        return found ? (Outcome.Pass, $"Typed {_patient.Nhi ?? _patient.Mrn} like a scanner; the patient was found.") : (Outcome.Fail, $"Scanning {_patient.Nhi ?? _patient.Mrn} into Find a patient did not find {_patient.Family}.");
    }

    // ------------------------------------------------------------------ HealthLink
    private async Task<(Outcome, string)> HealthLinkAsync()
    {
        var admin = await WhoCanAsync("/api/letters/healthlink");
        if (admin is null) return (Outcome.Skip, "No demo account manages HealthLink.");
        var orig = (await admin.GetAsync("/api/letters/healthlink")).Json!.Value;
        var put = await admin.PutAsync("/api/letters/healthlink", new { mode = "FolderDrop", senderEdi = Str(orig, "senderEdi") ?? "tdsdemo", outboxFolder = sims.HealthLinkOut, inboxFolder = sims.HealthLinkIn });
        if (put.Status != 200) return (Outcome.Fail, $"HealthLink settings refused ({put.Status}): {put.Text}");
        _restore.Add(async () =>
        {
            var r = await admin.PutAsync("/api/letters/healthlink", new { mode = Str(orig, "mode"), senderEdi = Str(orig, "senderEdi"), outboxFolder = Str(orig, "outboxFolder"), inboxFolder = Str(orig, "inboxFolder") });
            R.Add(Area, admin.Account, "Virtual HealthLink", "Put HealthLink settings back", r.Status == 200 ? Outcome.Pass : Outcome.Warn, r.Status == 200 ? $"Mode {Str(orig, "mode")}" : r.Text);
        });
        var writer = await WhoCanAsync("/api/letters?view=to-send");
        if (writer is null) return (Outcome.Skip, "No demo account can see letters.");
        var tried = new List<string>();
        // A new "GP booking notification" letter for the test patient (their GP practice is on HealthLink).
        var bookings = await writer.GetAsync($"/api/theatre/patients/{_patient!.Id}");
        var booking = Find(bookings.Json, _ => true) is { } bk ? Str(bk, "id") : null;
        var created = await writer.PostAsync("/api/letters", new { patientId = _patient!.Id, templateCode = "GPBOOK", bookingId = booking, referralId = (string?)null });
        var ids = new List<(string Id, string Number)>();
        if (created.Status == 200 && created.Json is { } cj) ids.Add((Str(cj, "id")!, Str(cj, "number") ?? "?"));
        else tried.Add("New GP letter: " + Report.Trim(created.Text, 160));
        foreach (var l in (await writer.GetAsync("/api/letters?view=to-send")).Json?.EnumerateArray().Take(6) ?? Enumerable.Empty<JsonElement>()) ids.Add((Str(l, "id")!, Str(l, "number") ?? "?"));
        foreach (var (id, number) in ids)
        {
            var d0 = await writer.GetAsync($"/api/letters/{id}");
            if (d0.Text.Contains("\"Draft\"", StringComparison.Ordinal))
            {
                var fin = await writer.PostAsync($"/api/letters/{id}/finalise", new { });
                if (fin.Status != 200) { tried.Add($"Finalise {number}: {Report.Trim(fin.Text, 120)}"); continue; }
            }
            var r = await writer.PostAsync($"/api/letters/{id}/send", new { });
            if (r.Status != 200) { tried.Add($"{number}: {Report.Trim(r.Text, 120)}"); continue; }
            var file = await Sims.WaitForAsync(sims.HealthLink, _ => true, TimeSpan.FromSeconds(20));
            if (file is null) return (Outcome.Fail, $"BrightPAS said {r.Text} but no file appeared in the HealthLink outbox folder {sims.HealthLinkOut}.");
            if (file.Problems.Length > 0) return (Outcome.Fail, $"Message written but not valid for HealthLink: {file.Problems}");
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(3000);
                var d = await writer.GetAsync($"/api/letters/{id}");
                if (d.Text.Contains("\"Acknowledged\"", StringComparison.Ordinal))
                    return (Outcome.Pass, $"Letter {number} written as a valid REF^I12 ({file.Name}); HealthLink's acknowledgement was read and the letter shows as Acknowledged.");
            }
            return (Outcome.Fail, $"Valid message sent ({file.Name}) and an acknowledgement was put in {sims.HealthLinkIn}, but the letter never showed as Acknowledged (the HealthLink worker reads that folder).");
        }
        return (Outcome.Fail, "No letter could be sent by HealthLink: " + string.Join(" | ", tried.Distinct().Take(4)));
    }

    // ------------------------------------------------------------------ laboratory
    private async Task<(Outcome, string)> LabAsync()
    {
        var p = _patient!;
        if (p.Nhi is null) return (Outcome.Skip, "Test patient has no NHI.");
        var control = "RBT" + DateTime.Now.ToString("HHmmss", CultureInfo.InvariantCulture);
        var now = DateTime.Now;
        var dob = DateOnly.Parse(p.Dob[..10], CultureInfo.InvariantCulture);
        var sex = p.Sex.StartsWith('F') ? 'F' : p.Sex.StartsWith('M') ? 'M' : 'U';
        var hl7 = $"MSH|^~\\&|DELTA|AWANUI-NELSON|BrightPAS|TDS|{now:yyyyMMddHHmm}||ORU^R01|{control}|P|2.4\r" +
                  $"PID|1||{p.Nhi}^^^NHI^NHI||{p.Family.ToUpperInvariant()}^{p.Given}||{dob:yyyyMMdd}|{sex}\r" +
                  $"OBR|1|TDS{control}|{control}A|HB^Haemoglobin|||{now.AddHours(-3):yyyyMMddHHmm}|||||||||11DEMA^REID^HAMISH^^^DR||||||{now:yyyyMMddHHmm}|||F\r" +
                  "OBX|1|NM|HB^Haemoglobin||138|g/L|130-175|N|||F\rNTE|1||Sent by the BrightPAS test robot (virtual laboratory).\r";
        string ack;
        try { ack = await Sims.SendMllpAsync("127.0.0.1", 12575, hl7); }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException or IOException) { return (Outcome.Fail, $"Could not connect to the lab interface on port 12575: {ex.Message}"); }
        R.Device("Virtual laboratory (MLLP)", $"ORU^R01 {control} sent; BrightPAS answered", ack.Replace("\r", "\n"));
        if (!ack.Contains("MSA|AA", StringComparison.Ordinal) && !ack.Contains("MSA|CA", StringComparison.Ordinal)) return (Outcome.Fail, $"BrightPAS did not accept the result: {ack.Replace('\r', ' ')}");
        var nurse = await WhoCanAsync("/api/labs/count");
        if (nurse is null) return (Outcome.Pass, "Result accepted (MSA|AA). No account to check Lab results.");
        var sentAt = DateTimeOffset.Now.AddMinutes(-2);
        for (var i = 0; i < 15; i++)
        {
            var l = await nurse.GetAsync($"/api/labs?patientId={p.Id}&view=all");
            var row = Find(l.Json, x => Str(x, "testName") == "Haemoglobin" && x.TryGetProperty("receivedAt", out var ra) && ra.GetDateTimeOffset() > sentAt);
            if (row is { } rr)
            {
                // Open it the way a nurse would.
                await nurse.Page.GotoAsync($"{bot.O.Url}/labs/{Str(rr, "id")}");
                try { await nurse.Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 8000 }); } catch (TimeoutException) { }
                var page = await nurse.SafeTextAsync();
                var shown = page.Contains("138", StringComparison.Ordinal) && page.Contains("test robot", StringComparison.OrdinalIgnoreCase);
                return shown ? (Outcome.Pass, $"Accepted (MSA|AA), matched to {p.Family.ToUpperInvariant()} by NHI, and the result page shows the value and the laboratory comment.")
                    : (Outcome.Fail, $"Result listed for the patient but its page does not show the value 138 g/L and the laboratory comment. Page text: {Report.Trim(page.Replace('\n', ' '), 300)}");
            }
            await Task.Delay(1000);
        }
        return (Outcome.Warn, "BrightPAS acknowledged the result (MSA|AA) but the robot could not find it in Lab results for the patient – check Messaging › Messages.");
    }

    // ------------------------------------------------------------------ exports
    private async Task<(Outcome, string)> PayrollAsync()
    {
        var s = await As("roster");
        var from = DateOnly.FromDateTime(DateTime.Today.AddDays(-13)); var to = DateOnly.FromDateTime(DateTime.Today);
        var res = await s.Ctx.APIRequest.PostAsync(bot.O.Url + "/api/roster/payroll-export", new() { Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1" }, DataObject = new { from, to, export = new { protect = true, password = "Robot-Export-Pass-2026!" } }, IgnoreHTTPSErrors = true });
        if (res.Status != 200) return (Outcome.Fail, $"Encrypted payroll export refused ({res.Status}): {await res.TextAsync()}");
        var bytes = await res.BodyAsync();
        var name = Path.Combine(R.DeviceFolder, "payroll-export-encrypted.zip");
        await File.WriteAllBytesAsync(name, bytes);
        var aes = bytes.Length > 30 && bytes[0] == 'P' && bytes[1] == 'K' && BitConverter.ToUInt16(bytes, 8) == 99 && (BitConverter.ToUInt16(bytes, 6) & 1) == 1;
        var notes = new List<string> { aes ? "encrypted ZIP uses AES (opens with 7-Zip and the password)" : "ZIP is NOT AES-encrypted" };
        // Unprotected: allowed or refused depending on the export policy.
        var plain = await s.Ctx.APIRequest.PostAsync(bot.O.Url + "/api/roster/payroll-export", new() { Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1" }, DataObject = new { from, to, export = new { protect = false, password = (string?)null } }, IgnoreHTTPSErrors = true });
        if (plain.Status == 400) { notes.Add("unprotected export refused – the policy requires a password (good)"); return (aes ? Outcome.Pass : Outcome.Fail, string.Join("; ", notes) + ". Timesheet contents not checked because the policy only allows encrypted files."); }
        var pb = await plain.BodyAsync();
        string csv;
        if (pb.Length > 2 && pb[0] == 'P' && pb[1] == 'K')
        {
            using var z = new System.IO.Compression.ZipArchive(new MemoryStream(pb));
            var e = z.Entries.FirstOrDefault(x => x.Name == "xero-timesheet.csv");
            if (e is null) return (Outcome.Fail, "Export has no xero-timesheet.csv.");
            using var rd = new StreamReader(e.Open()); csv = await rd.ReadToEndAsync();
        }
        else csv = Encoding.UTF8.GetString(pb);
        await File.WriteAllTextAsync(Path.Combine(R.DeviceFolder, "xero-timesheet.csv"), csv);
        var problems = CheckTimesheet(csv, from, to, out var rows);
        R.Device("Xero (payroll import file)", $"xero-timesheet.csv – {rows} staff rows", csv);
        notes.Add($"timesheet has {rows} rows");
        return problems.Count == 0 && aes ? (Outcome.Pass, string.Join("; ", notes) + ". Columns, payroll IDs, hours and totals check out. (Confirm the exact column layout with TDS's Xero payroll before go-live.)")
            : (Outcome.Fail, string.Join("; ", notes.Concat(problems)));
    }

    private static List<string> CheckTimesheet(string csv, DateOnly from, DateOnly to, out int rows)
    {
        var p = new List<string>();
        var lines = csv.TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        rows = Math.Max(0, lines.Count - 1);
        if (lines.Count == 0) { p.Add("Timesheet is empty."); return p; }
        var head = SplitCsv(lines[0]);
        var days = to.DayNumber - from.DayNumber + 1;
        if (head.Count < 4 || head[0] != "Employee" || head[1] != "Payroll ID" || head[2] != "Earnings Rate" || head[^1] != "Total") p.Add($"Header is {lines[0]}");
        if (head.Count != days + 4) p.Add($"Expected {days} day columns, found {head.Count - 4}.");
        foreach (var l in lines.Skip(1))
        {
            var c = SplitCsv(l);
            if (c.Count != head.Count) { p.Add($"Row has {c.Count} columns, header has {head.Count}: {l}"); continue; }
            if (string.IsNullOrWhiteSpace(c[1])) p.Add($"{c[0]} has no payroll ID (Xero cannot match them).");
            decimal sum = 0;
            for (var i = 3; i < c.Count - 1; i++)
            {
                if (c[i].Length == 0) continue;
                if (!decimal.TryParse(c[i], NumberStyles.Number, CultureInfo.InvariantCulture, out var h) || h < 0 || h > 24) p.Add($"{c[0]}: odd hours value \"{c[i]}\" on {head[i]}.");
                else sum += h;
            }
            if (decimal.TryParse(c[^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var total) && Math.Abs(total - sum) > 0.02m) p.Add($"{c[0]}: total {total} but the days add up to {sum}.");
        }
        return p;
    }

    private static List<string> SplitCsv(string line)
    {
        var r = new List<string>(); var sb = new StringBuilder(); var q = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (q) { if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else if (ch == '"') q = false; else sb.Append(ch); }
            else if (ch == '"') q = true; else if (ch == ',') { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(ch);
        }
        r.Add(sb.ToString());
        return r;
    }

    private async Task<(Outcome, string)> PdfAsync()
    {
        var s = await WhoCanAsync("/api/letters?view=recent");
        if (s is null) return (Outcome.Skip, "No account can see letters.");
        var list = await s.GetAsync("/api/letters?view=recent");
        var first = Find(list.Json, _ => true);
        if (first is not { } l) return (Outcome.Skip, "No letters in the demonstration data.");
        var res = await s.Ctx.APIRequest.PostAsync($"{bot.O.Url}/api/letters/{Str(l, "id")}/pdf", new() { Headers = new Dictionary<string, string> { ["X-BrightPAS-Request"] = "1" }, DataObject = new { export = new { protect = true, password = "Robot-Pdf-Pass-2026!", format = "pdf" } }, IgnoreHTTPSErrors = true });
        if (res.Status != 200) return (Outcome.Fail, $"PDF refused ({res.Status}): {await res.TextAsync()}");
        var b = await res.BodyAsync();
        await File.WriteAllBytesAsync(Path.Combine(R.DeviceFolder, "letter-protected.pdf"), b);
        var text = Encoding.Latin1.GetString(b);
        var ok = text.StartsWith("%PDF", StringComparison.Ordinal) && text.Contains("/Encrypt", StringComparison.Ordinal);
        return ok ? (Outcome.Pass, $"Letter {Str(l, "number")} downloaded as a PDF that needs a password to open ({b.Length / 1024} KB).") : (Outcome.Fail, "The PDF is not password-protected.");
    }

    public async Task RestoreAsync()
    {
        if (!bot.O.Restore) { R.Add(Area, "", "Settings", "Leave BrightPAS pointed at the virtual devices (--keep-settings)", Outcome.Info); return; }
        foreach (var r in _restore.AsEnumerable().Reverse())
        {
            try { await r(); } catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or HttpRequestException) { R.Add(Area, "", "Settings", "Put settings back", Outcome.Warn, ex.Message); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var s in _sessions.Values) await s.DisposeAsync();
    }
}
