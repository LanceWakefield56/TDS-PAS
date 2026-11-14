using System.Net.Http.Json;
using System.Text.Json;
using BrightPAS.TestBot;

// Pin the process culture to New Zealand English so the server's regional settings never change number/date formats (HL7, CSV, JSON).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-NZ");
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-NZ");

Console.OutputEncoding = System.Text.Encoding.UTF8;
Options o;
try { o = Options.Parse(args); }
catch (HelpRequested) { Console.WriteLine(Options.Help); return 0; }
catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }

Console.WriteLine($"BrightPAS Test Robot {typeof(Options).Assembly.GetName().Version?.ToString(3)} – testing {o.Url}");

// 1. Safety: only ever a demonstration system.
DemoInfo demo;
string version;
using (var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { Timeout = TimeSpan.FromSeconds(30) })
{
    try
    {
        var health = await http.GetFromJsonAsync<JsonElement>(o.Url + "/health");
        version = health.TryGetProperty("version", out var v) ? v.GetString() ?? "?" : "?";
        var st = await http.GetFromJsonAsync<JsonElement>(o.Url + "/api/setup/status");
        if (!st.TryGetProperty("demo", out var d) || d.ValueKind != JsonValueKind.Object || d.GetProperty("password").ValueKind != JsonValueKind.String)
        {
            Console.Error.WriteLine("STOPPED: this BrightPAS is not a demonstration system (or its demonstration data is still loading).");
            Console.Error.WriteLine("The robot only runs against a demonstration database (Setup install --demo-data), never a real one.");
            return 3;
        }
        demo = new DemoInfo(d.GetProperty("password").GetString()!, d.GetProperty("authenticatorSecret").GetString()!,
            d.GetProperty("accounts").EnumerateArray().Select(a => new DemoAccount(a.GetProperty("userName").GetString()!, a.GetProperty("displayName").GetString()!, a.GetProperty("purpose").GetString() ?? "")).ToList());
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
    {
        Console.Error.WriteLine($"Could not reach BrightPAS at {o.Url}: {ex.Message}");
        return 4;
    }
}

var report = new Report(o.Out);
report.Facts["site"] = o.Url;
report.Facts["version"] = version;
report.Facts["robot"] = typeof(Options).Assembly.GetName().Version?.ToString(3) ?? "?";
report.Facts["depth"] = o.Depth;
report.Facts["computer"] = Environment.MachineName;
report.Facts["accounts"] = string.Join(", ", demo.Accounts.Select(a => a.UserName));
Console.WriteLine($"Demonstration system {version}, {demo.Accounts.Count} demo accounts. Report: {o.Out}");

Sims? sims = null;
Scenarios? scenarios = null;
var exit = 0;
try
{
    if (o.Simulators && o.Wants("devices"))
    {
        sims = new Sims(o, report);
        try { await sims.StartAsync(); report.Facts["virtual devices"] = $"SMS company https://localhost:{o.ModicaPort}, email localhost:{o.SmtpPort}, Zebra localhost:{o.ZebraPort}, HealthLink {sims.HealthLinkOut}, SaferSleep localhost:{o.SaferSleepPort} (the GE monitor gateway and SaferSleep records are sent to ports 12580 and 12581)"; }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException or UnauthorizedAccessException)
        {
            report.Add("devices", "robot", "virtual devices", "Start the virtual devices", Outcome.Fail, ex.Message);
            await sims.DisposeAsync(); sims = null;
        }
    }

    await using var bot = await Bot.StartAsync(o, report, demo);
    var crawler = new Crawler(bot);

    if (sims is not null)
    {
        scenarios = new Scenarios(bot, sims);
        sims.RenderLabel = scenarios.RenderZplAsync;
        Console.WriteLine("Virtual devices and outside systems:");
        try { await scenarios.RunAsync(); }
        finally { await scenarios.RestoreAsync(); await scenarios.DisposeAsync(); scenarios = null; }
    }

    if (o.Wants("signin") || o.Wants("pages") || o.Wants("buttons") || o.Wants("forms") || o.Wants("screens") || o.Wants("permissions"))
    {
        Console.WriteLine("Every account, every page:");
        foreach (var a in demo.Accounts) await crawler.RunAccountAsync(a, o.Full);
        // Wrong password and wrong code must be refused politely.
        await SignInRefusalsAsync(bot, demo);
    }
    if (o.Full && o.Wants("sizes"))
    {
        Console.WriteLine("Tablet and phone sizes:");
        await crawler.SizesAsync("reception", ["/", "/patients", "/calendar", "/theatre", "/letters"]);
        await crawler.SizesAsync("nurse", ["/", "/labs", "/forms"]);
    }
}
catch (Exception ex) when (ex is not OutOfMemoryException)
{
    report.Add("robot", "robot", "robot", "The robot itself stopped unexpectedly", Outcome.Fail, ex.ToString());
    exit = 5;
}
finally
{
    if (scenarios is not null) { try { await scenarios.RestoreAsync(); } catch (Exception) { /* report what we have */ } }
    if (sims is not null) await sims.DisposeAsync();
}

var zip = report.Write();
var (fail, warn, pass, skip) = report.Counts();
Console.WriteLine();
Console.WriteLine($"Finished: {fail} problems, {warn} warnings, {pass} passed, {skip} skipped.");
Console.WriteLine($"Report: {Path.Combine(o.Out, "report.html")}");
Console.WriteLine($"Send this file to Claude: {zip}");
return exit != 0 ? exit : fail > 0 ? 1 : 0;

static async Task SignInRefusalsAsync(Bot bot, DemoInfo demo)
{
    var user = demo.Accounts[0].UserName;
    await using var s = await bot.NewContextAsync("(nobody)");
    var p = s.Page;
    await p.GotoAsync(bot.O.Url + "/signin");
    await p.Locator("input[autocomplete=username]").FillAsync(user);
    await p.Locator("input[type=password]").FillAsync("not-the-password");
    await p.Locator("button.btn-primary").ClickAsync();
    await p.WaitForTimeoutAsync(1500);
    var msg = string.Join(" ", await p.Locator(".error:visible, [role=alert]:visible").AllInnerTextsAsync());
    // Good practice: the message must not reveal whether the user name exists ("incorrect password" would).
    var leaked = msg.Contains("incorrect password", StringComparison.OrdinalIgnoreCase) || msg.Contains("no such user", StringComparison.OrdinalIgnoreCase);
    bot.R.Add("signin", "(nobody)", "/signin", "Wrong password is refused with a clear message", msg.Length > 0 && await p.Locator(".code-input").CountAsync() == 0 ? (leaked ? Outcome.Warn : Outcome.Pass) : Outcome.Fail,
        msg.Length > 0 ? $"Said: {msg}" : "No message shown");
    await p.GotoAsync(bot.O.Url + "/");
    await p.WaitForTimeoutAsync(800);
    var onSignIn = p.Url.Contains("/signin", StringComparison.Ordinal) || await p.Locator("input[type=password]").CountAsync() > 0;
    bot.R.Add("permissions", "(nobody)", "/", "Not signed in: the system shows only the sign-in page", onSignIn ? Outcome.Pass : Outcome.Fail, onSignIn ? "" : "Shows something other than sign-in: " + p.Url);
    var api = await s.GetAsync("/api/patients?q=a");
    bot.R.Add("permissions", "(nobody)", "/api/patients", "Not signed in: patient data is refused", api.Status is 401 or 403 ? Outcome.Pass : Outcome.Fail, $"Answered {api.Status}");
}
