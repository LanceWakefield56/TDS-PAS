using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// Uses the website as one account: every menu page, the record pages linked from them, every tab and harmless button, and every form it can
/// open (filled with sensible test values and saved). Anything that errors, refuses without saying why, or leaks computer text is reported.
/// </summary>
public sealed partial class Crawler(Bot bot)
{
    private Report R => bot.R;
    private readonly HashSet<string> _screenChecked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clicked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _submitted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _menusByRole = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> MenuByAccount { get; } = new(StringComparer.Ordinal);

    /// <summary>Pages that only make sense with a record id, or that are reached another way. Used for the "typed address" permission check.</summary>
    public static readonly string[] AllPages =
    [
        "/", "/patients", "/patients/new", "/admin/users", "/audit", "/admin/licence", "/admin/referral-targets", "/referrals", "/admin/organisation", "/admin/roles",
        "/theatre/templates", "/calendar", "/roster", "/letters", "/letters/new", "/admin/letters", "/waitlist", "/admin/interfaces", "/messaging", "/messaging/interfaces",
        "/messaging/messages", "/messaging/flows", "/messaging/mappings", "/messaging/tables", "/messaging/settings", "/labs", "/theatre", "/forms", "/billing", "/billing/new",
        "/reminders", "/admin/identity", "/credentialing", "/portal", "/portal/requests", "/admin/security", "/waitlist/nbrs", "/cssd", "/assets", "/stock", "/emr", "/endoscopy", "/scribe", "/devices",
        "/quality", "/quality/report", "/quality/mine", "/coding", "/estimates", "/estimates/new", "/estimates/outbox", "/estimates/admin", "/triage", "/triage/rules", "/triage/report", "/medicines", "/rooms-bookings", "/day", "/followup", "/followup/ssi", "/followup/plans", "/followup/report", "/outbox", "/research", "/forms/designer", "/claims", "/claims/settings", "/claims/xero", "/claims/eftpos",
    ];

    // Buttons the robot never presses while exploring: they sign out, destroy, send for real, or change security. The scripted device tests cover sending.
    [GeneratedRegex(@"sign ?out|send to (the )?terminal|start eftpos|make the claim|make the xero|send to xero|sent on the portal|close claim|use the ticked|delete|remove|\bvoid|merge|offboard|revoke|reset|deactivat|disable|switch off|rotate|break.?glass|declin|reject|archive|withdraw|discard|unlock|\block\b|retire|purge|replay|resend|retry all|run now|send all|send now|approve|print|regenerate|new key|licen[cs]e|import|upload|restore|close shift|swap|did not attend|\bdna\b|cancel (the )?(booking|appointment|list|invoice|shift)|emergency|expire|forget|erase|correct identity|log ?out|mark all|apply to all|publish|go live|activate|\bmark |needing set-up|out of service|return to service|\bfail\b|recall|release|xero|cancel order|receive goods|record implant|take from stock|\bskip|not needed|\bend\b|discharge|undo|choose file|attach|\bscan\b|\badmit|transfer|complete (sign|time)|sign note|record given|lab received|^sent$|^in theatre$|^recovery$|^ready for discharge$|^admitted$|take picture|recording|reconnect|connect monitor|disconnect|validate and chart|don.t chart|send to safersleep|file here|file to an episode|save and sign|^sign\b|amend|save .* to patient record|^add$|draft from findings|close incident|close complaint|send report|save triage|acknowledged in writing|response sent|told the complainant|sign off|record disclosure|i have read this|shared with staff|mark notified|^done$|create the file|create batch|not reported by tds|report this event after all|record health nz error|create nbrs entry|check and add|load acknowledgement|^accepted$|returned with errors|save video retention|save & issue|save changes|cancel estimate|save to incisive|duplicate|^stop$|create outlook draft|^send \(held|outlook draft instead|save as template|save template|show differences|check file|save settings|save findings|run triage|save draft|new draft|start from the example|add a rule|add a medicine|^arrived|to theatre|to recovery|gone home|save the call|no answer|send check-in|not needed|^move$|^assign$|save the ssi|save classification|raise a|^close$|save plan|another contact|start from the example plans|open an ssi record|download the ssi|new plan|undo send|^release$|send now|wrong person|report it|stop it|^approve$|^decline$|^publish|^retire$|start testing|new version|delete draft|^complete$|save for later|^import$|create and design|send for approval|check and preview|^start$|add a field|yes, send it|report and draft|sent to the wrong person|save outbox settings|save medicine|load the starter|unlink|add an ingredient|add a dose limit|add a group|add a cross|add an interaction|accept onto|ask the rooms|register as a new|different person|^use [a-z0-9]|send anyway|send message|send booking request", RegexOptions.IgnoreCase)]
    private static partial Regex Dangerous();

    [GeneratedRegex(@"^(save|create|add|send|submit|record|book|register|confirm|update|finalise|request|continue|done|ok|apply|search|find|calculate|check|preview|test)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SubmitWord();

    [GeneratedRegex(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex Guid();

    public static string Pattern(string path)
    {
        var p = path.Split('?', '#')[0];
        p = Guid().Replace(p, ":id");
        return Regex.Replace(p, @"/\d+(?=/|$)", "/:n");
    }

    public async Task RunAccountAsync(DemoAccount a, bool deep)
    {
        Console.WriteLine($"— {a.UserName}: {a.DisplayName}");
        Session s;
        var t0 = DateTimeOffset.Now;
        try { s = await bot.SignInAsync(a.UserName); }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException or InvalidOperationException)
        {
            R.Add("signin", a.UserName, "/signin", "Sign in with password and authenticator code", Outcome.Fail, ex.Message.Split('\n')[0]);
            return;
        }
        await using var _ = s;
        R.Add("signin", a.UserName, "/signin", "Sign in with password and authenticator code", Outcome.Pass, $"{(DateTimeOffset.Now - t0).TotalSeconds:0.0} s");
        var me = await s.GetAsync("/api/me");
        var roles = me.Json is { } mj && mj.TryGetProperty("roles", out var rr) ? string.Join(", ", rr.EnumerateArray().Select(x => x.GetString())) : "?";
        var perms = me.Json is { } pj && pj.TryGetProperty("permissions", out var pp) ? pp.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet() : [];

        // The menu this account sees (read once the home page has fully loaded).
        await s.Page.GotoAsync(bot.O.Url + "/");
        await Settle(s.Page, 1200);
        var links = await s.Page.Locator("nav#mainnav a").EvaluateAllAsync<JsonElement>("els => els.map(e => ({ href: e.getAttribute('href'), text: (e.textContent || '').replace(/\\d+$/, '').trim() }))");
        var menu = links.EnumerateArray().Select(x => (Href: x.GetProperty("href").GetString() ?? "/", Text: x.GetProperty("text").GetString() ?? "")).ToList();
        MenuByAccount[a.UserName] = menu.Select(m => m.Text).ToList();
        R.Add("pages", a.UserName, "menu", $"Menu ({roles})", Outcome.Info, string.Join(" · ", menu.Select(m => m.Text)));
        var key = roles;
        var firstOfRole = _menusByRole.TryAdd(key, menu.Select(m => m.Href).ToHashSet());

        var queue = new Queue<(string Path, string? Label)>(menu.Select(m => (m.Href, (string?)m.Text)));
        // A patient record, for everyone who can see patients.
        if (perms.Contains("patient.view") && await FirstPatientAsync(s) is { } pid) queue.Enqueue(($"/patients/{pid}", null));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var visited = 0;
        while (queue.Count > 0 && visited < (deep ? 45 : 30))
        {
            var (path, label) = queue.Dequeue();
            var pat = Pattern(path);
            if (!seen.Add(pat)) continue;
            visited++;
            var ok = await VisitAsync(s, path, label, checkScreens: deep && bot.O.Wants("screens"));
            if (!ok) continue;
            // Record pages linked from this page (one of each kind).
            foreach (var href in await s.Page.Locator("main a[href^='/']").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('href'))"))
            {
                if (href is null || href.StartsWith("/api/", StringComparison.Ordinal) || href.StartsWith("/q/", StringComparison.Ordinal) || href.StartsWith("/c/", StringComparison.Ordinal) || href.StartsWith("/my/", StringComparison.Ordinal) || href.EndsWith("/labels", StringComparison.Ordinal)) continue;
                if (!seen.Contains(Pattern(href)) && queue.All(q => Pattern(q.Path) != Pattern(href))) queue.Enqueue((href, null));
            }
            if (deep && (firstOfRole || label is null) && (bot.O.Wants("buttons") || bot.O.Wants("forms"))) await ExploreAsync(s, path);
        }

        if (bot.O.Wants("permissions")) await TypedAddressesAsync(s, menu.Select(m => m.Href).ToHashSet());
    }

    private static async Task<string?> FirstPatientAsync(Session s)
    {
        foreach (var q in new[] { "Wilson", "Smith", "a" })
        {
            var r = await s.GetAsync("/api/patients?q=" + q);
            if (r.Status == 200 && r.Json is { ValueKind: JsonValueKind.Array } arr && arr.GetArrayLength() > 0) return arr[0].GetProperty("id").GetString();
        }
        return null;
    }

    private async Task Settle(IPage p, int ms = 600)
    {
        try { await p.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 8000 }); } catch (TimeoutException) { }
        await p.WaitForTimeoutAsync(ms);
    }

    /// <summary>Opens one page and checks it worked. Returns false if the page failed badly.</summary>
    public async Task<bool> VisitAsync(Session s, string path, string? label, bool checkScreens)
    {
        var where = path;
        var pat = Pattern(path);
        var mark = s.Mark();
        var t0 = DateTimeOffset.Now;
        try
        {
            await s.Page.GotoAsync(bot.O.Url + path, new() { Timeout = 20000 });
        }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
        {
            R.Add("pages", s.Account, where, "Page opens", Outcome.Fail, ex.Message.Split('\n')[0], await s.ShotAsync(pat));
            return false;
        }
        await Settle(s.Page);
        var secs = (DateTimeOffset.Now - t0).TotalSeconds - 0.6;
        var problems = await ProblemsAsync(s, mark, menuPage: label is not null);
        var shot = await s.ShotAsync(pat);
        R.Gallery($"{pat} — {s.Account}", shot);
        if (problems.Count == 0) R.Add("pages", s.Account, where, label is null ? "Record page opens" : $"Menu page \"{label}\" opens", Outcome.Pass, $"{secs:0.0} s");
        foreach (var (o, d) in problems) R.Add("pages", s.Account, where, label is null ? "Record page opens" : $"Menu page \"{label}\" opens", o, d, shot);

        foreach (var n in await Usability.LeaksAsync(s.Page, pat)) R.Add("screens", s.Account, where, n.Check, n.Outcome, n.Detail, shot);
        if (checkScreens && _screenChecked.Add(pat))
        {
            var notes = await Usability.CheckAsync(s.Page, path == "/" ? null : label, secs);
            foreach (var n in notes) R.Add("screens", s.Account, where, n.Check, n.Outcome, n.Detail, n.Outcome is Outcome.Warn or Outcome.Fail ? shot : null);
            if (!notes.Any(n => n.Outcome is Outcome.Warn or Outcome.Fail)) R.Add("screens", s.Account, where, "Screen checks", Outcome.Pass);
        }
        // Don't explore a page that already failed to load: every button would repeat the same problem.
        return !problems.Any(p => p.Outcome == Outcome.Fail);
    }

    /// <summary>Failed server calls, script errors and error messages shown on the page since <paramref name="mark"/>.</summary>
    private async Task<List<(Outcome Outcome, string Detail)>> ProblemsAsync(Session s, int mark, bool menuPage)
    {
        var list = new List<(Outcome, string)>();
        foreach (var i in await s.IssuesSinceAsync(mark))
        {
            switch (i.Kind)
            {
                case "http" when i.Status >= 500: list.Add((Outcome.Fail, $"Server error {i.Status} on {i.Method} {i.Path} {i.Detail}")); break;
                case "http" when i.Status == 403: list.Add((Outcome.Fail, $"{(menuPage ? "The menu offers this page but the" : "The")} server refused {i.Method} {i.Path} (403 – no permission)")); break;
                case "http" when i.Status == 401: list.Add((Outcome.Fail, $"Signed out unexpectedly: {i.Method} {i.Path} (401)")); break;
                case "http" when i.Method == "GET": list.Add((Outcome.Fail, $"Server refused {i.Method} {i.Path} ({i.Status}) {i.Detail}")); break;
                case "http": list.Add((Outcome.Info, $"{i.Method} {i.Path} answered {i.Status} {i.Detail}")); break;
                case "network": list.Add((Outcome.Fail, $"Could not reach the server: {i.Method} {i.Path} {i.Detail}")); break;
                case "script": list.Add((Outcome.Fail, $"Page script crashed: {i.Detail}")); break;
                case "console": list.Add((Outcome.Warn, $"Browser console error: {i.Detail}")); break;
            }
        }
        try
        {
            var errs = await s.Page.Locator("main .error:not(.finding):visible").AllInnerTextsAsync();
            foreach (var e in errs.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct()) list.Add((Outcome.Fail, $"Error shown on screen: {e}"));
            if (await s.Page.Locator("main h1:has-text('Page not found')").CountAsync() > 0) list.Add((Outcome.Fail, "Page not found"));
        }
        catch (PlaywrightException) { }
        return list;
    }

    /// <summary>Presses each harmless button and tab on the page, and fills in and saves each form it opens.</summary>
    private async Task ExploreAsync(Session s, string path)
    {
        var p = s.Page;
        var pat = Pattern(path);
        List<string> labels;
        try
        {
            labels = (await p.Locator("main button:visible:not([disabled]), main [role=tab]:visible, main summary:visible").EvaluateAllAsync<string[]>(
                "els => els.map(e => (e.innerText || e.getAttribute('aria-label') || '').trim().replace(/\\s+/g, ' ')).filter(t => t && t.length < 60)")).Distinct().Take(30).ToList();
        }
        catch (PlaywrightException) { return; }
        foreach (var text in labels)
        {
            if (Dangerous().IsMatch(text)) { R.Add("buttons", s.Account, path, $"Button \"{text}\"", Outcome.Skip, "Not pressed by the explorer (changes or sends something); covered by scripted tests where it matters."); continue; }
            if (!_clicked.Add(pat + "|" + text)) continue;
            if (!Pattern(p.Url.Replace(bot.O.Url, "")).Equals(pat, StringComparison.Ordinal) || await p.Locator("main [role=dialog]:visible, main dialog[open], main .modal:visible").CountAsync() > 0)
            {
                await p.GotoAsync(bot.O.Url + path); await Settle(p, 300);
            }
            var target = p.Locator("main").GetByRole(AriaRole.Button, new() { Name = text, Exact = true }).Or(p.Locator("main").GetByRole(AriaRole.Tab, new() { Name = text, Exact = true }))
                .Or(p.Locator("main summary").Filter(new() { HasTextString = text })).First;
            if (await target.CountAsync() == 0) continue;
            var inputsBefore = await VisibleInputsAsync(p);
            var mark = s.Mark();
            try { await target.ClickAsync(new() { Timeout = 5000 }); }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException) { R.Add("buttons", s.Account, path, $"Button \"{text}\"", Outcome.Warn, "Could not be pressed (covered or not responding): " + ex.Message.Split('\n')[0]); continue; }
            await Settle(p, 500);
            var problems = await ProblemsAsync(s, mark, false);
            var bad = problems.Where(x => x.Outcome != Outcome.Info).ToList();
            // Pressing a save button on an empty form: the server refusing (400) and the screen explaining why is correct behaviour.
            var refusedPost = problems.Any(x => x.Outcome == Outcome.Info && System.Text.RegularExpressions.Regex.IsMatch(x.Detail, @"^(POST|PUT) .* answered (400|409|422)"));
            // "Check"/"Validate" buttons exist to show what is missing on an unfinished screen.
            if (Regex.IsMatch(text, @"^(check|validate)\b", RegexOptions.IgnoreCase) || pat.EndsWith("/new", StringComparison.Ordinal)) refusedPost = true;
            if (refusedPost && bad.Count > 0 && bad.All(x => x.Detail.StartsWith("Error shown on screen", StringComparison.Ordinal)))
            {
                R.Add("buttons", s.Account, path, $"Button \"{text}\"", Outcome.Pass, "Checked the entry and explained: " + string.Join(" ", bad.Select(x => x.Detail[23..])));
                bad.Clear(); problems.Clear();
            }
            if (bad.Count == 0) R.Add("buttons", s.Account, path, $"Button \"{text}\"", Outcome.Pass, problems.Count > 0 ? string.Join("; ", problems.Select(x => x.Detail)) : "");
            else foreach (var (o, d) in bad) R.Add("buttons", s.Account, path, $"Button \"{text}\"", o, d, await s.ShotAsync(pat + "-" + text));
            foreach (var n in await Usability.LeaksAsync(p, pat)) R.Add("screens", s.Account, path + " › " + text, n.Check, n.Outcome, n.Detail);

            // A form appeared: fill it in and save it (once per form across all accounts).
            if (bot.O.Wants("forms") && await VisibleInputsAsync(p) > inputsBefore && _submitted.Add(pat + "|" + text))
            {
                await FillAndSaveAsync(s, path, text);
                await p.GotoAsync(bot.O.Url + path); await Settle(p, 300); // start the next button from a clean page
            }
            if (s.Dialogs.Count > 0) { R.Add("buttons", s.Account, path, $"Button \"{text}\" asked to confirm", Outcome.Info, string.Join(" | ", s.Dialogs) + " (robot answered Cancel)"); s.Dialogs.Clear(); }
            await p.Keyboard.PressAsync("Escape");
        }
    }

    private static async Task<int> VisibleInputsAsync(IPage p)
    {
        try { return await p.Locator("main input:visible:not([type=hidden]):not([type=checkbox]):not([type=radio]):not([type=file]), main select:visible, main textarea:visible").CountAsync(); }
        catch (PlaywrightException) { return 0; }
    }

    private const string FillScript = """
        (today) => {
          const vis = e => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== 'hidden'; };
          const label = e => ((e.labels && e.labels[0] && e.labels[0].innerText) || e.getAttribute('aria-label') || e.placeholder || e.name || '').toLowerCase();
          const set = (e, v) => { const proto = e.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : e.tagName === 'SELECT' ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
            Object.getOwnPropertyDescriptor(proto, 'value').set.call(e, v); e.dispatchEvent(new Event('input', { bubbles: true })); e.dispatchEvent(new Event('change', { bubbles: true })); };
          const filled = [];
          for (const e of document.querySelectorAll('main input, main select, main textarea')) {
            if (!vis(e) || e.disabled || e.readOnly) continue;
            const t = (e.type || '').toLowerCase(), l = label(e);
            if (['hidden','checkbox','radio','file','submit','button','password','search','color','range'].includes(t)) continue;
            if (e.tagName === 'SELECT') { if (!e.value) { const o = [...e.options].find(o => o.value && !o.disabled); if (o) { set(e, o.value); filled.push(l + '=' + o.text); } } continue; }
            if (e.value) continue;
            let v = 'Robot test entry';
            if (e.tagName === 'TEXTAREA') v = 'Entered by the BrightPAS test robot.';
            else if (t === 'email' || /email/.test(l)) v = 'robot.test@example.co.nz';
            else if (t === 'tel' || /phone|mobile|cell/.test(l)) v = '021 555 0199';
            else if (t === 'date') v = /birth|dob/.test(l) ? '1980-05-17' : today;
            else if (t === 'datetime-local') v = today + 'T10:30';
            else if (t === 'time') v = '10:30';
            else if (t === 'month') v = today.slice(0, 7);
            else if (t === 'number') v = e.min && +e.min > 0 ? e.min : (/minute|duration/.test(l) ? '30' : /percent|%/.test(l) ? '10' : '1');
            else if (/nhi/.test(l)) continue;
            else if (/postcode|post code/.test(l)) v = '7020';
            else if (/amount|fee|price|cost|\$/.test(l)) v = '100';
            else if (/minute|duration/.test(l)) v = '30';
            else if (/family|surname|last name/.test(l)) v = 'Robot';
            else if (/given|first name/.test(l)) v = 'Testy';
            else if (/name/.test(l)) v = 'Robot Test';
            else if (/code/.test(l)) v = 'RBT' + Math.floor(Math.random() * 900 + 100);
            else if (/host|address|ip/.test(l) && /printer|host|ip/.test(l)) v = '127.0.0.1';
            else if (/url|https/.test(l)) v = 'https://example.co.nz';
            else if (/reason|note|comment|why/.test(l)) v = 'Robot test – checking this form works.';
            set(e, v); filled.push(l + '=' + v);
          }
          return filled;
        }
        """;

    private async Task FillAndSaveAsync(Session s, string path, string opener)
    {
        var p = s.Page;
        var where = $"{path} › {opener}";
        var today = DateTime.Today.AddDays(14).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        string[] filled;
        try { filled = await p.EvaluateAsync<string[]>(FillScript, today); }
        catch (PlaywrightException ex) { R.Add("forms", s.Account, where, "Fill in the form", Outcome.Warn, ex.Message.Split('\n')[0]); return; }
        var buttons = await p.Locator("main button:visible:not([disabled])").EvaluateAllAsync<string[]>("els => els.map(e => (e.innerText || '').trim())");
        var save = buttons.FirstOrDefault(b => SubmitWord().IsMatch(b) && !Dangerous().IsMatch(b) && !b.Equals(opener, StringComparison.OrdinalIgnoreCase))
                   ?? buttons.FirstOrDefault(b => SubmitWord().IsMatch(b) && !Dangerous().IsMatch(b));
        if (save is null) { R.Add("forms", s.Account, where, "Form has a save button", Outcome.Info, "No save-type button found after filling: " + string.Join(", ", buttons.Take(8))); return; }
        var mark = s.Mark();
        try { await p.Locator("main").GetByRole(AriaRole.Button, new() { Name = save, Exact = true }).Last.ClickAsync(new() { Timeout = 5000 }); }
        catch (Exception ex) when (ex is TimeoutException or PlaywrightException) { R.Add("forms", s.Account, where, $"Press \"{save}\"", Outcome.Warn, ex.Message.Split('\n')[0]); return; }
        await Settle(p, 800);
        var issues = await s.IssuesSinceAsync(mark);
        var shown = (await p.Locator("main .error:visible").AllInnerTextsAsync()).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        var shot = await s.ShotAsync(Pattern(path) + "-form-" + opener);
        var detail = $"Filled: {string.Join("; ", filled.Take(12))}";
        if (issues.Any(i => i.Status >= 500 || i.Kind == "script"))
            R.Add("forms", s.Account, where, $"Fill in and press \"{save}\"", Outcome.Fail, string.Join(" ", issues.Where(i => i.Status >= 500 || i.Kind == "script").Select(i => $"{i.Kind} {i.Status} {i.Method} {i.Path} {i.Detail}")) + " | " + detail, shot);
        else if (issues.Any(i => i.Status is 400 or 409 or 422) && shown.Count == 0)
            R.Add("forms", s.Account, where, $"Fill in and press \"{save}\"", Outcome.Fail, "The server refused the form but the screen did not say why. " + string.Join(" ", issues.Select(i => $"{i.Status} {i.Path} {i.Detail}")) + " | " + detail, shot);
        else if (issues.Any(i => i.Status == 403))
            R.Add("forms", s.Account, where, $"Fill in and press \"{save}\"", Outcome.Fail, "The screen offered this form but the server refused it (403 – no permission). " + detail, shot);
        else if (shown.Count > 0)
            R.Add("forms", s.Account, where, $"Fill in and press \"{save}\"", Outcome.Pass, $"Checked the entry and explained: {string.Join(" ", shown)} | {detail}", shot);
        else
            R.Add("forms", s.Account, where, $"Fill in and press \"{save}\"", Outcome.Pass, "Accepted. " + detail, shot);
        foreach (var n in await Usability.LeaksAsync(p, Pattern(path))) R.Add("screens", s.Account, where + " (after saving)", n.Check, n.Outcome, n.Detail, shot);
    }

    /// <summary>Pages the menu hides from this account: typing the address must not show data.</summary>
    private async Task TypedAddressesAsync(Session s, HashSet<string> menu)
    {
        // Pages under a menu entry (e.g. /messaging/flows under /messaging) are reached from that page, so they are not "hidden".
        foreach (var path in AllPages.Where(x => !menu.Contains(x) && !menu.Any(m => m != "/" && x.StartsWith(m + "/", StringComparison.Ordinal))))
        {
            var mark = s.Mark();
            try { await s.Page.GotoAsync(bot.O.Url + path, new() { Timeout = 15000 }); } catch (Exception ex) when (ex is TimeoutException or PlaywrightException) { continue; }
            await Settle(s.Page, 300);
            var issues = await s.IssuesSinceAsync(mark);
            if (issues.Any(i => i.Status >= 500 || i.Kind == "script"))
            {
                R.Add("permissions", s.Account, path, "Hidden page typed into the address bar", Outcome.Fail, "Crashed instead of saying no access: " + string.Join(" ", issues.Select(i => $"{i.Kind} {i.Status} {i.Path} {i.Detail}")), await s.ShotAsync("typed" + path));
                continue;
            }
            var refused = issues.Count(i => i.Status is 403 or 404);
            var text = (await s.SafeTextAsync());
            var looksBlocked = refused > 0 || text.Contains("do not have access", StringComparison.OrdinalIgnoreCase) || text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("not available", StringComparison.OrdinalIgnoreCase);
            var rows = await s.Page.Locator("main table tbody tr").CountAsync();
            if (looksBlocked && rows == 0) R.Add("permissions", s.Account, path, "Hidden page typed into the address bar", Outcome.Pass, refused > 0 ? $"Server refused ({refused} call(s))" : "Page says it is not available");
            else if (rows > 0 && refused == 0) R.Add("permissions", s.Account, path, "Hidden page typed into the address bar", Outcome.Warn, $"The menu hides this page, but typing the address shows {rows} rows of data. Check this account should see it.", await s.ShotAsync("typed" + path));
            else R.Add("permissions", s.Account, path, "Hidden page typed into the address bar", Outcome.Pass, "No data shown");
        }
    }

    /// <summary>Tablet and phone sizes: the menu button works and pages fit.</summary>
    public async Task SizesAsync(string account, IEnumerable<string> paths)
    {
        foreach (var (name, w, h, mobile) in new[] { ("tablet", 1024, 768, true), ("phone", 390, 844, true) })
        {
            Session s;
            try { s = await bot.SignInAsync(account, new ViewportSize { Width = w, Height = h }, mobile); }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException) { R.Add("sizes", account, name, "Sign in on a " + name, Outcome.Fail, ex.Message.Split('\n')[0]); continue; }
            await using var _ = s;
            R.Add("sizes", account, name, $"Sign in on a {name} ({w}×{h})", Outcome.Pass);
            var menuBtn = s.Page.Locator(".menu-btn");
            if (await menuBtn.IsVisibleAsync())
            {
                await menuBtn.ClickAsync();
                var navVisible = await s.Page.Locator("nav#mainnav a").First.IsVisibleAsync();
                R.Add("sizes", account, name, "Menu button opens the menu", navVisible ? Outcome.Pass : Outcome.Fail, navVisible ? "" : "Menu did not appear", navVisible ? null : await s.ShotAsync(name + "-menu"));
                await menuBtn.ClickAsync();
            }
            foreach (var path in paths)
            {
                await s.Page.GotoAsync(bot.O.Url + path);
                await Settle(s.Page, 400);
                var shot = await s.ShotAsync($"{name}{Pattern(path)}");
                R.Gallery($"{Pattern(path)} — {name}", shot);
                var overflow = await s.Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
                if (overflow > 2) R.Add("sizes", account, $"{path} ({name})", "Fits the screen", Outcome.Warn, $"{overflow}px wider than the {name} screen – staff must scroll sideways.", shot);
                else R.Add("sizes", account, $"{path} ({name})", "Fits the screen", Outcome.Pass);
            }
        }
    }
}
