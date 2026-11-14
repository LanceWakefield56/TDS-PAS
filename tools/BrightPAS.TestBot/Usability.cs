using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;

namespace BrightPAS.TestBot;

/// <summary>
/// "Does this screen make sense?" checks: accessibility rules (axe-core, WCAG 2.2 AA), plus BrightPAS house rules for clear screens –
/// no computer jargon or internal codes shown to staff, NZ dates, one obvious main action, labelled fields, readable text, targets big
/// enough to tap, page title matching the menu, nothing running off the side of the screen.
/// </summary>
public static class Usability
{
    private static readonly Lazy<string> Axe = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BrightPAS.TestBot.Assets.axe.min.js")
            ?? throw new InvalidOperationException("axe.min.js missing from the robot.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    });

    public sealed record Note(string Check, Outcome Outcome, string Detail);

    /// <summary>Text that must never reach a staff screen (Fail) or should not (Warn).</summary>
    public const string LeakScript = """
        () => {
          const main = document.querySelector('main') || document.body;
          // Visible text, one text node at a time (so "Patient accounts" + a separate "Optional" badge is not read as "accountsOptional"),
          // leaving out raw-message and log viewers (pre, code, textarea).
          const parts = [];
          const w = document.createTreeWalker(main, NodeFilter.SHOW_TEXT, { acceptNode: n => {
            const e = n.parentElement; if (!e || e.closest('pre,code,textarea,script,style,.sr-only')) return NodeFilter.FILTER_REJECT;
            const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_REJECT; } });
          while (w.nextNode()) parts.push(w.currentNode.textContent.trim());
          const t = parts.filter(x => x).join(' \n ');
          const out = [];
          const fail = [/\bundefined\b/, /\bNaN\b/, /\[object Object\]/, /\bException\b/, /Stack ?trace/i, /\bSystem\.[A-Z]\w+/, /Invalid Date/];
          for (const r of fail) { const m = t.match(r); if (m) out.push({ level: 'Fail', what: 'Computer error text on screen', text: t.substr(Math.max(0, m.index - 60), 160) }); }
          const guid = t.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i);
          if (guid) out.push({ level: 'Warn', what: 'Internal record id (GUID) shown to staff', text: t.substr(Math.max(0, guid.index - 60), 160) });
          const nul = t.match(/(:|\|)\s*null\b|\bnull\s*(,|\||$)/m);
          if (nul) out.push({ level: 'Warn', what: 'The word "null" shown instead of a blank', text: t.substr(Math.max(0, nul.index - 60), 160) });
          // Checked within each piece of text (joining pieces adds spaces of its own).
          const garbledPart = parts.find(x => /\)\)|\(\s*\)|\s,\s|,\s*,|\.\.(?!\.)/.test(x));
          if (garbledPart) out.push({ level: 'Warn', what: 'Text looks garbled (doubled brackets or punctuation – something missing?)', text: garbledPart.slice(0, 160) });
          const iso = t.match(/\b20\d\d-[01]\d-[0-3]\d(T[0-2]\d:\d\d)?/);
          if (iso) out.push({ level: 'Warn', what: 'Computer-style date (yyyy-mm-dd) instead of an NZ date like 13 Nov 2026', text: t.substr(Math.max(0, iso.index - 60), 160) });
          const enumLike = t.match(/(?<![\w.@\/-])[a-z]+[A-Z][a-zA-Z]+(?![\w.@\/-])/);
          if (enumLike && !/iPhone|iPad|HealthLink|DYMO|eMR|mmHg|SpO2|EtCO2|kPa|MyPortal|LabelWriter|PDFs?|JavaScript|SaferSleep|TeamUp|BrightPAS|McAfee|McKenzie|McDonald|MacKenzie/.test(enumLike[0]))
            out.push({ level: 'Warn', what: 'Code-style word shown to staff (looks like an internal name)', text: enumLike[0] });
          return out;
        }
        """;

    private const string LayoutScript = """
        () => {
          const vis = e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none'; };
          const main = document.querySelector('main') || document.body;
          const res = {};
          res.overflowX = document.documentElement.scrollWidth - window.innerWidth;
          res.culprit = '';
          if (res.overflowX > 2) { let worst = null, wr = window.innerWidth;
            for (const e of document.querySelectorAll('main *')) { const r = e.getBoundingClientRect(); if (r.right > wr + 1 && vis(e)) { worst = e; wr = r.right; } }
            if (worst) res.culprit = worst.tagName.toLowerCase() + (worst.className ? '.' + String(worst.className).split(' ').join('.') : '') + ' "' + (worst.innerText || '').trim().slice(0, 50) + '"'; }
          res.h1 = [...main.querySelectorAll('h1')].filter(vis).map(h => h.innerText.trim());
          res.primary = [...main.querySelectorAll('.btn-primary')].filter(vis).map(b => b.innerText.trim()).filter(x => x);
          res.mainText = (main.innerText || '').trim().length;
          let small = 0, smallEx = '';
          for (const e of main.querySelectorAll('p,td,th,label,span,a,button,li,dd,dt,small')) {
            if (!vis(e) || !e.childNodes.length || ![...e.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) continue;
            const fs = parseFloat(getComputedStyle(e).fontSize); if (fs < 12) { small++; if (!smallEx) smallEx = e.innerText.trim().slice(0, 60) + ' (' + fs + 'px)'; }
          }
          res.small = small; res.smallEx = smallEx;
          let tiny = 0, tinyEx = '';
          for (const e of main.querySelectorAll('button,a[href],input[type=checkbox],input[type=radio],select')) {
            if (!vis(e)) continue; const r = e.getBoundingClientRect();
            if (e.matches('a') && getComputedStyle(e).display === 'inline') continue; // inline text links are exempt (WCAG 2.5.8)
            if (r.width < 24 || r.height < 24) { tiny++; if (!tinyEx) tinyEx = (e.innerText || e.getAttribute('aria-label') || e.tagName).trim().slice(0, 40) + ` (${Math.round(r.width)}×${Math.round(r.height)})`; }
          }
          res.tiny = tiny; res.tinyEx = tinyEx;
          res.wideTables = [...main.querySelectorAll('table')].filter(vis).map(t => (t.rows[0] || { cells: [] }).cells.length).filter(n => n > 10);
          res.headless = [...main.querySelectorAll('table')].filter(vis).filter(t => !t.querySelector('th')).length;
          res.clipped = [...main.querySelectorAll('td,th,button,label')].filter(vis).filter(e => e.scrollWidth > e.clientWidth + 4 && getComputedStyle(e).overflow === 'hidden').length;
          res.loading = /Loading…|Loading\.\.\./.test(main.innerText || '');
          res.placeholderOnly = [...main.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio]),select,textarea')].filter(vis)
              .filter(i => !i.labels?.length && !i.getAttribute('aria-label') && !i.getAttribute('aria-labelledby')).map(i => i.placeholder || i.name || i.type).slice(0, 5);
          return res;
        }
        """;

    /// <summary>Screens for technical staff (audit log, system log, messaging engine), where record ids, codes and raw dates are expected.</summary>
    private static bool Technical(string? pattern) => pattern is not null && (pattern.StartsWith("/audit", StringComparison.Ordinal) || pattern.StartsWith("/messaging", StringComparison.Ordinal)
        || pattern.StartsWith("/admin/security", StringComparison.Ordinal) || pattern.StartsWith("/admin/interfaces", StringComparison.Ordinal));

    public static async Task<List<Note>> LeaksAsync(IPage page, string? pattern = null)
    {
        var list = new List<Note>();
        JsonElement r;
        try { r = await page.EvaluateAsync<JsonElement>(LeakScript); } catch (PlaywrightException) { return list; }
        foreach (var x in r.EnumerateArray())
        {
            var o = x.GetProperty("level").GetString() == "Fail" ? Outcome.Fail : Outcome.Warn;
            if (o == Outcome.Warn && Technical(pattern)) o = Outcome.Info;
            list.Add(new(x.GetProperty("what").GetString()!, o, x.GetProperty("text").GetString()!.Replace('\n', ' ')));
        }
        return list;
    }

    public static async Task<List<Note>> CheckAsync(IPage page, string? menuLabel, double seconds, bool desktop = true)
    {
        var notes = new List<Note>();
        if (seconds > 8) notes.Add(new("Page speed", Outcome.Fail, $"Took {seconds:0.0} s to finish loading (staff will think it has hung)."));
        else if (seconds > 3) notes.Add(new("Page speed", Outcome.Warn, $"Took {seconds:0.0} s to finish loading (aim for under 3 s)."));

        var l = await page.EvaluateAsync<JsonElement>(LayoutScript);
        var overflow = l.GetProperty("overflowX").GetInt32();
        if (overflow > 2) notes.Add(new("Fits the screen", desktop ? Outcome.Fail : Outcome.Warn, $"Page is {overflow}px wider than the window – staff must scroll sideways. Widest part: {l.GetProperty("culprit").GetString()}"));
        var h1 = l.GetProperty("h1").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        if (h1.Count == 0) notes.Add(new("Page heading", Outcome.Warn, "No main heading – staff (and screen readers) cannot tell which page this is."));
        else if (menuLabel is not null && !SharesWord(menuLabel, h1[0])) notes.Add(new("Heading matches menu", Outcome.Warn, $"Menu says \"{menuLabel}\" but the page heading is \"{h1[0]}\"."));
        var primary = l.GetProperty("primary").EnumerateArray().Select(x => x.GetString()).Distinct().ToList();
        if (primary.Count > 3) notes.Add(new("One obvious next step", Outcome.Warn, $"{primary.Count} main (blue) buttons on one screen: {string.Join(", ", primary.Take(6))}. It is unclear which to press."));
        if (l.GetProperty("mainText").GetInt32() < 40) notes.Add(new("Empty page", Outcome.Warn, "The page shows almost nothing and no hint of what to do."));
        if (l.GetProperty("loading").GetBoolean()) notes.Add(new("Still loading", Outcome.Warn, "\"Loading…\" still showing after the page settled."));
        var small = l.GetProperty("small").GetInt32();
        if (small > 3) notes.Add(new("Readable text", Outcome.Warn, $"{small} pieces of text smaller than 12px, e.g. {l.GetProperty("smallEx").GetString()}."));
        var tiny = l.GetProperty("tiny").GetInt32();
        if (tiny > 0) notes.Add(new("Big enough to click/tap", Outcome.Warn, $"{tiny} buttons/links smaller than 24×24px (WCAG 2.5.8), e.g. {l.GetProperty("tinyEx").GetString()}."));
        foreach (var w in l.GetProperty("wideTables").EnumerateArray()) notes.Add(new("Table width", Outcome.Warn, $"A table has {w.GetInt32()} columns – hard to read; consider fewer columns or a details view."));
        if (l.GetProperty("headless").GetInt32() > 0) notes.Add(new("Table headings", Outcome.Warn, "A table has no column headings."));
        if (l.GetProperty("clipped").GetInt32() > 0) notes.Add(new("Text cut off", Outcome.Warn, $"{l.GetProperty("clipped").GetInt32()} cells/buttons have text cut off."));
        var unlabelled = l.GetProperty("placeholderOnly").EnumerateArray().Select(x => x.GetString()).ToList();
        if (unlabelled.Count > 0) notes.Add(new("Fields have labels", Outcome.Warn, $"Fields with no label (only a hint inside the box, which disappears when typing): {string.Join(", ", unlabelled)}."));

        // Accessibility rules (axe-core). Evaluated through the browser's developer channel, so the site's content-security policy is not loosened.
        try
        {
            if (!await page.EvaluateAsync<bool>("() => typeof window.axe !== 'undefined'")) await page.EvaluateAsync(Axe.Value);
            var v = await page.EvaluateAsync<JsonElement>("""
                async () => { const r = await axe.run(document.querySelector('main') || document, { runOnly: { type: 'tag', values: ['wcag2a','wcag2aa','wcag21a','wcag21aa','wcag22aa'] }, resultTypes: ['violations'] });
                  return r.violations.map(v => ({ id: v.id, impact: v.impact || '', help: v.help, n: v.nodes.length, where: v.nodes.slice(0, 2).map(n => (n.target || []).join(' ') + ' – ' + (n.html || '').slice(0, 90)) })); }
                """);
            foreach (var x in v.EnumerateArray())
            {
                var impact = x.GetProperty("impact").GetString();
                notes.Add(new($"Accessibility: {x.GetProperty("help").GetString()}", impact is "critical" or "serious" ? Outcome.Warn : Outcome.Info,
                    $"[{x.GetProperty("id").GetString()}, {impact}] {x.GetProperty("n").GetInt32()} place(s): {string.Join(" | ", x.GetProperty("where").EnumerateArray().Select(w => w.GetString()))}"));
            }
        }
        catch (PlaywrightException ex) { notes.Add(new("Accessibility rules", Outcome.Skip, ex.Message.Split('\n')[0])); }
        return notes;
    }

    private static readonly HashSet<string> Small = new(StringComparer.OrdinalIgnoreCase) { "a", "an", "and", "the", "of", "my", "&", "set-up", "to", "for" };

    public static bool SharesWord(string a, string b)
    {
        static IEnumerable<string> W(string s) => s.ToLowerInvariant().Split([' ', '&', ',', '-', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Small.Contains(w)).Select(w => w.TrimEnd('s'));
        var wa = W(a).ToHashSet();
        return W(b).Any(wa.Contains);
    }
}
