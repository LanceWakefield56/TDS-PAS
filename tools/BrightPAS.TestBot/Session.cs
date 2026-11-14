using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

public sealed record DemoAccount(string UserName, string DisplayName, string Purpose);
public sealed record DemoInfo(string Password, string TotpSecret, IReadOnlyList<DemoAccount> Accounts);
public sealed record NetIssue(string Kind, string Method, string Path, int Status, string Detail, string PageUrl);

public static class Totp
{
    public static long Step(DateTimeOffset now) => now.ToUnixTimeSeconds() / 30;

    public static string Code(string base32, long step)
    {
        const string a = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = new StringBuilder();
        foreach (var c in base32.Replace("=", "").Replace(" ", "").ToUpperInvariant()) bits.Append(Convert.ToString(a.IndexOf(c), 2).PadLeft(5, '0'));
        var key = new byte[bits.Length / 8];
        for (var i = 0; i < key.Length; i++) key[i] = Convert.ToByte(bits.ToString(i * 8, 8), 2);
        var msg = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
#pragma warning disable CA5350 // TOTP (RFC 6238) is defined over HMAC-SHA1
        var h = HMACSHA1.HashData(key, msg);
#pragma warning restore CA5350
        var o = h[^1] & 15;
        var v = ((h[o] & 0x7f) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (v % 1_000_000).ToString("000000", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>Owns the browser. Each account gets its own browser context (separate cookies), like a separate workstation.</summary>
public sealed class Bot(Options o, Report report, IPlaywright pw, IBrowser browser, DemoInfo demo) : IAsyncDisposable
{
    public Options O { get; } = o;
    public Report R { get; } = report;
    public DemoInfo Demo { get; } = demo;
    public IPlaywright Pw { get; } = pw;
    private readonly Dictionary<string, long> _lastStep = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<Bot> StartAsync(Options o, Report r, DemoInfo demo)
    {
        var pw = await Playwright.CreateAsync();
        // Fake camera = a virtual endoscopy capture card for the capture screen (0.15.0).
        var launch = new BrowserTypeLaunchOptions { Headless = !o.Headed, SlowMo = o.SlowMo, Args = ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"] };
        if (o.Browser is "msedge" or "chrome") launch.Channel = o.Browser;
        IBrowser b;
        try { b = await pw.Chromium.LaunchAsync(launch); }
        catch (PlaywrightException ex) when (o.Browser is "msedge" or "chrome")
        {
            throw new InvalidOperationException($"Could not start {o.Browser}. Install Microsoft Edge (or use --browser chrome). {ex.Message.Split('\n')[0]}");
        }
        r.Facts["browser"] = $"{o.Browser} {b.Version}";
        return new Bot(o, r, pw, b, demo);
    }

    public async Task<Session> NewContextAsync(string account, ViewportSize? size = null, bool mobile = false)
    {
        var ctx = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true, ViewportSize = size ?? new ViewportSize { Width = 1366, Height = 900 }, IsMobile = mobile, HasTouch = mobile,
            AcceptDownloads = true, Locale = "en-NZ", TimezoneId = "Pacific/Auckland", Permissions = ["camera", "microphone"],
        });
        ctx.SetDefaultTimeout(15000);
        var page = await ctx.NewPageAsync();
        return new Session(this, account, ctx, page);
    }

    /// <summary>Signs in through the screens (user name, password, authenticator code) and returns the signed-in session.</summary>
    public async Task<Session> SignInAsync(string user, ViewportSize? size = null, bool mobile = false)
    {
        for (var attempt = 1; ; attempt++)
        {
            var s = await NewContextAsync(user, size, mobile);
            try
            {
                var p = s.Page;
                await p.GotoAsync(O.Url + "/signin");
                await p.Locator("input[autocomplete=username]").FillAsync(user);
                await p.Locator("input[type=password]").FillAsync(Demo.Password);
                await p.Locator("button.btn-primary").ClickAsync();
                await p.Locator(".code-input").WaitForAsync();
                // Each authenticator code works once per account: wait for a fresh 30-second window if this account just used one.
                var step = Totp.Step(DateTimeOffset.UtcNow);
                lock (_lastStep)
                {
                    if (_lastStep.TryGetValue(user, out var last) && last >= step) step = last + 1;
                    _lastStep[user] = step;
                }
                while (Totp.Step(DateTimeOffset.UtcNow) < step) await Task.Delay(500);
                await p.Locator(".code-input").FillAsync(Totp.Code(Demo.TotpSecret, step));
                await p.Locator("button.btn-primary").ClickAsync();
                await p.Locator("nav#mainnav").WaitForAsync(new() { Timeout = 20000, State = WaitForSelectorState.Attached });
                try { await p.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 10000 }); } catch (TimeoutException) { }
                await p.WaitForTimeoutAsync(800);
                s.ClearIssues();
                return s;
            }
            catch (Exception ex) when (attempt < 3 && ex is TimeoutException or PlaywrightException)
            {
                var text = await s.SafeTextAsync();
                await s.DisposeAsync();
                if (text.Contains("Too many", StringComparison.OrdinalIgnoreCase) || text.Contains("429", StringComparison.Ordinal))
                {
                    R.Add("signin", user, "/signin", "Sign-in rate limit reached (expected protection) – waiting a minute", Outcome.Info);
                    await Task.Delay(61000);
                }
                else await Task.Delay(2000);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await browser.CloseAsync();
        Pw.Dispose();
    }
}

/// <summary>One signed-in browser. Records every failed server call, script error and download while it is used.</summary>
public sealed class Session : IAsyncDisposable
{
    public Bot Bot { get; }
    public string Account { get; }
    public IBrowserContext Ctx { get; }
    public IPage Page { get; }
    private readonly List<NetIssue> _issues = [];
    public List<string> Downloads { get; } = [];
    public List<string> Dialogs { get; } = [];
    public int PrintCalls { get; set; }
    public JsonElement? Me { get; set; }

    public Session(Bot bot, string account, IBrowserContext ctx, IPage page)
    {
        Bot = bot; Account = account; Ctx = ctx; Page = page;
        page.Response += (_, r) =>
        {
            try
            {
                var u = new Uri(r.Url);
                if (r.Status >= 400 && u.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
                {
                    var at = page.Url;
                    var t = Task.Run(async () =>
                    {
                        string body = "";
                        try { body = await r.TextAsync(); } catch (PlaywrightException) { }
                        lock (_issues) _issues.Add(new NetIssue("http", r.Request.Method, u.PathAndQuery, r.Status, Report.Trim(body, 400), at));
                    });
                    lock (_pending) _pending.Add(t);
                }
                else if (r.Status >= 500) lock (_issues) _issues.Add(new NetIssue("http", r.Request.Method, u.PathAndQuery, r.Status, "", page.Url));
            }
            catch (UriFormatException) { }
        };
        page.RequestFailed += (_, r) =>
        {
            if (r.Url.StartsWith(bot.O.Url, StringComparison.OrdinalIgnoreCase) && r.Failure is { } f && !f.Contains("ERR_ABORTED", StringComparison.Ordinal))
                lock (_issues) _issues.Add(new NetIssue("network", r.Method, new Uri(r.Url).PathAndQuery, 0, f, page.Url));
        };
        page.PageError += (_, e) => { lock (_issues) _issues.Add(new NetIssue("script", "", "", 0, e, page.Url)); };
        page.Console += (_, m) =>
        {
            if (m.Type == "error" && !m.Text.Contains("Failed to load resource", StringComparison.Ordinal))
                lock (_issues) _issues.Add(new NetIssue("console", "", "", 0, m.Text, page.Url));
        };
        page.Dialog += async (_, d) => { lock (Dialogs) Dialogs.Add($"{d.Type}: {d.Message}"); await d.DismissAsync(); };
        page.Download += async (_, d) =>
        {
            var name = Path.Combine(bot.R.DeviceFolder, "downloads", $"{account}-{DateTime.Now:HHmmss}-{d.SuggestedFilename}");
            Directory.CreateDirectory(Path.GetDirectoryName(name)!);
            try { await d.SaveAsAsync(name); lock (Downloads) Downloads.Add(name); } catch (PlaywrightException) { }
        };
        // Printing: count window.print() calls instead of opening the print dialog (the virtual DYMO / page printer).
        page.AddInitScriptAsync("window.print = () => { window.__bpPrints = (window.__bpPrints || 0) + 1; document.documentElement.setAttribute('data-bp-printed', String(window.__bpPrints)); };").GetAwaiter().GetResult();
    }

    private readonly List<Task> _pending = [];

    public int Mark() { lock (_issues) return _issues.Count; }

    /// <summary>Waits for response bodies still being read, then returns the issues recorded since <paramref name="mark"/>.</summary>
    public async Task<List<NetIssue>> IssuesSinceAsync(int mark)
    {
        Task[] p; lock (_pending) { p = _pending.ToArray(); _pending.Clear(); }
        await Task.WhenAny(Task.WhenAll(p), Task.Delay(5000));
        lock (_issues) return _issues.Skip(mark).ToList();
    }
    public void ClearIssues() { lock (_issues) _issues.Clear(); }

    public async Task<string> SafeTextAsync()
    {
        try { return await Page.InnerTextAsync("body", new() { Timeout = 2000 }); } catch (Exception ex) when (ex is TimeoutException or PlaywrightException) { return ""; }
    }

    public async Task<string> ShotAsync(string label, bool fullPage = false)
    {
        var name = Bot.R.NextShotName($"{Account}-{label}");
        try { await Page.ScreenshotAsync(new() { Path = Path.Combine(Bot.R.ShotFolder, name), FullPage = fullPage, Timeout = 10000 }); }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException) { return ""; }
        return name;
    }

    private static readonly Dictionary<string, string> Csrf = new() { ["X-BrightPAS-Request"] = "1" };

    public async Task<(int Status, JsonElement? Json, string Text)> ApiAsync(string method, string path, object? body = null)
    {
        var opts = new APIRequestContextOptions { Method = method, Headers = Csrf, IgnoreHTTPSErrors = true, FailOnStatusCode = false };
        if (body is not null) opts.DataObject = body;
        var r = await Ctx.APIRequest.FetchAsync(Bot.O.Url + path, opts);
        var text = await r.TextAsync();
        JsonElement? json = null;
        if (text.Length > 0 && (text[0] == '{' || text[0] == '['))
        {
            try { json = JsonDocument.Parse(text).RootElement.Clone(); } catch (JsonException) { }
        }
        return (r.Status, json, text);
    }

    public Task<(int Status, JsonElement? Json, string Text)> GetAsync(string path) => ApiAsync("GET", path);
    public Task<(int Status, JsonElement? Json, string Text)> PostAsync(string path, object body) => ApiAsync("POST", path, body);
    public Task<(int Status, JsonElement? Json, string Text)> PutAsync(string path, object body) => ApiAsync("PUT", path, body);

    public async ValueTask DisposeAsync()
    {
        try { await Ctx.CloseAsync(); } catch (PlaywrightException) { }
    }
}
