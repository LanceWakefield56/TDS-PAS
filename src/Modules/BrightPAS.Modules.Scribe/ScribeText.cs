using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BrightPAS.Modules.Scribe;

public sealed record Highlight(int Start, int Length, string Kind, string Message);
public sealed record Segment(int Seq, string Speaker, decimal Start, decimal End, string Text, decimal? Confidence);
public sealed record DraftedSentence(string Section, string Text, int[] Sources);
public sealed record DictationResult(string Text, IReadOnlyList<string> Commands, IReadOnlyList<Highlight> Highlights);

/// <summary>
/// Pure text processing for BrightPAS Scribe: voice commands, custom dictionary and NZ spelling corrections, safety highlighting
/// (doses, units, look-alike/sound-alike medicines, laterality, low-confidence words), de-identification for cloud drafting,
/// the template-only drafter, prompts and parsing of AI drafts, and the edit measure for accuracy audits.
/// </summary>
public static partial class ScribeText
{
    // ------------------------------------------------------------------ voice commands
    private static readonly (string Spoken, string Replace)[] Punctuation =
    [
        ("new paragraph", "\n\n"), ("new line", "\n"), ("full stop", "."), ("comma", ","), ("question mark", "?"), ("colon mark", ":"),
        ("semicolon", ";"), ("open bracket", "("), ("close bracket", ")"), ("hyphen", "-"), ("slash", "/"),
    ];
    private static readonly string[] Commands = ["next field", "previous field", "sign off", "insert template"];

    /// <summary>Turns spoken commands into punctuation and returns UI commands ("next field", "sign off" – sign-off still needs the normal confirmation).</summary>
    public static (string Text, List<string> Commands) ApplyVoiceCommands(string spoken)
    {
        var cmds = new List<string>();
        var t = " " + spoken.Trim() + " ";
        foreach (var c in Commands)
        {
            var rx = new Regex($@"[\s,.]*\b{Regex.Escape(c)}\b[\s,.]*", RegexOptions.IgnoreCase);
            if (rx.IsMatch(t)) { cmds.Add(c); t = rx.Replace(t, " "); }
        }
        foreach (var (spokenWord, rep) in Punctuation)
            t = Regex.Replace(t, $@"[ \t]*[,.]?[ \t]*\b{Regex.Escape(spokenWord)}\b[,.]?[ \t]*", rep is "\n" or "\n\n" ? rep : rep + " ", RegexOptions.IgnoreCase);
        // "Scratch that" removes the sentence just dictated (after punctuation, so sentence ends are known).
        while (ScratchRx().Match(t) is { Success: true } m)
        {
            var before = t[..m.Index].TrimEnd();
            var cut = Math.Max(before.TrimEnd('.', '?', '!').LastIndexOfAny(['.', '?', '!', '\n']) + 1, 0);
            t = before[..cut] + " " + t[(m.Index + m.Length)..];
        }
        t = Regex.Replace(t, @"[ \t]+([.,?:;)])", "$1");
        t = Regex.Replace(t, @"\(\s+", "(");
        t = Regex.Replace(t, @"[ \t]{2,}", " ");
        t = Regex.Replace(t, @" *\n *", "\n").Trim();
        // Capitalise the start of sentences and paragraphs.
        t = SentenceStartRx().Replace(t, m => m.Value.ToUpperInvariant());
        return (t, cmds);
    }

    [GeneratedRegex(@"\bscratch that\b[\s,.]*", RegexOptions.IgnoreCase)] private static partial Regex ScratchRx();
    [GeneratedRegex(@"(^|[.?!]\s+|\n)\p{Ll}")] private static partial Regex SentenceStartRx();

    // ------------------------------------------------------------------ corrections
    /// <summary>NZ/Australian English spelling for words US-trained speech engines often produce.</summary>
    public static readonly (string Us, string Nz)[] NzSpelling =
    [
        ("anesthetic", "anaesthetic"), ("anesthesia", "anaesthesia"), ("anesthetist", "anaesthetist"), ("anesthesiologist", "anaesthetist"), ("esophagus", "oesophagus"), ("esophageal", "oesophageal"),
        ("esophagitis", "oesophagitis"), ("hemorrhage", "haemorrhage"), ("hemorrhoids", "haemorrhoids"), ("hemoglobin", "haemoglobin"), ("hematoma", "haematoma"), ("edema", "oedema"),
        ("estrogen", "oestrogen"), ("pediatric", "paediatric"), ("color", "colour"), ("center", "centre"), ("liter", "litre"), ("milliliter", "millilitre"), ("tumor", "tumour"),
        ("ischemia", "ischaemia"), ("diarrhea", "diarrhoea"), ("orthopedic", "orthopaedic"), ("labor", "labour"), ("favor", "favour"), ("behavior", "behaviour"), ("organized", "organised"),
        ("recognize", "recognise"), ("catheterization", "catheterisation"), ("hospitalized", "hospitalised"), ("anemia", "anaemia"), ("leukemia", "leukaemia"), ("fetus", "foetus"),
    ];

    /// <summary>Custom dictionary first (what the engine hears → the TDS term, e.g. "far no" → "whānau"), then NZ spelling. Keeps the case of the first letter.</summary>
    public static string Correct(string text, IEnumerable<(string Term, string? SoundsLike)> dictionary)
    {
        var t = text;
        foreach (var (term, sounds) in dictionary)
            foreach (var s in (sounds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(Strip(term)).Where(s => s.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase))
                t = Regex.Replace(t, $@"\b{Regex.Escape(s)}\b", m => KeepCase(m.Value, term), RegexOptions.IgnoreCase);
        foreach (var (us, nz) in NzSpelling)
            t = Regex.Replace(t, $@"\b{us}(s|ed|ing)?\b", m => KeepCase(m.Value, nz + m.Groups[1].Value), RegexOptions.IgnoreCase);
        return t;
    }

    /// <summary>Removes macrons so "Maori" spoken/typed without them still matches "Māori".</summary>
    public static string Strip(string s) => new(s.Normalize(NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    private static string KeepCase(string heard, string term) => heard.Length > 0 && char.IsUpper(heard[0]) && term.Length > 0 ? char.ToUpperInvariant(term[0]) + term[1..] : term;

    // ------------------------------------------------------------------ safety highlighting
    /// <summary>Look-alike/sound-alike medicine pairs (ISMP/HQSC style) – either name is highlighted with its partner.</summary>
    public static readonly (string A, string B)[] LookAlike =
    [
        ("hydralazine", "hydroxyzine"), ("clonidine", "clonazepam"), ("prednisone", "prednisolone"), ("morphine", "hydromorphone"), ("tramadol", "trandolapril"),
        ("metformin", "metronidazole"), ("celecoxib", "citalopram"), ("Losec", "Lasix"), ("oxycodone", "OxyContin"), ("novorapid", "novomix"), ("Zantac", "Xanax"),
        ("dexamethasone", "dexmedetomidine"), ("ondansetron", "olanzapine"), ("carboplatin", "cisplatin"), ("enoxaparin", "heparin"),
    ];

    [GeneratedRegex(@"\b(\d+(?:\.\d+)?|one|two|three|four|five|ten|twenty|fifty|hundred)\s*(milligrams?|mg|micrograms?|mcg|µg|grams?|g|millilit(?:re|er)s?|ml|mL|units?|iu|IU|mmol|percent|%)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DoseRx();
    [GeneratedRegex(@"\b(left|right|bilateral|both sides)\b", RegexOptions.IgnoreCase)] private static partial Regex SideRx();
    [GeneratedRegex(@"\[\?\]|\(\?\)|\b(inaudible|unclear)\b", RegexOptions.IgnoreCase)] private static partial Regex UnsureRx();

    /// <summary>Highlights what a clinician must check before signing. <paramref name="lowConfidence"/> are words the speech engine was unsure of.</summary>
    public static List<Highlight> Check(string text, IEnumerable<string>? medicines = null, IEnumerable<string>? lowConfidence = null)
    {
        var h = new List<Highlight>();
        foreach (Match m in DoseRx().Matches(text))
        {
            var unit = m.Groups[2].Value.ToLowerInvariant();
            var msg = unit.StartsWith("unit", StringComparison.Ordinal) || unit == "iu" ? "Dose in units – check the number and that 'units' (not mL) is meant; write 'units' in full." : "Dose – check the number, unit and medicine.";
            if (m.Groups[1].Value.Contains('.') && m.Groups[1].Value.EndsWith(".0", StringComparison.Ordinal)) msg = "Trailing zero (e.g. 5.0) – can be misread as ten times the dose.";
            h.Add(new(m.Index, m.Length, "dose", msg));
        }
        foreach (var (a, b) in LookAlike)
            foreach (var (name, other) in new[] { (a, b), (b, a) })
                foreach (Match m in Regex.Matches(text, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase))
                    h.Add(new(m.Index, m.Length, "lasa", $"Look-alike/sound-alike medicine – confirm it is {name}, not {other}."));
        foreach (var med in (medicines ?? []).Where(x => x.Length > 3).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (Match m in Regex.Matches(text, $@"\b{Regex.Escape(med)}\b", RegexOptions.IgnoreCase))
                if (!h.Any(x => x.Start == m.Index)) h.Add(new(m.Index, m.Length, "medicine", "Medicine name – check it and its dose."));
        foreach (Match m in SideRx().Matches(text)) h.Add(new(m.Index, m.Length, "side", "Laterality – confirm the side."));
        foreach (Match m in UnsureRx().Matches(text)) h.Add(new(m.Index, m.Length, "unsure", "The draft was unsure here – check against the recording/transcript."));
        foreach (var w in (lowConfidence ?? []).Where(x => x.Length > 2).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (Match m in Regex.Matches(text, $@"\b{Regex.Escape(w)}\b", RegexOptions.IgnoreCase))
                if (!h.Any(x => x.Start == m.Index)) h.Add(new(m.Index, m.Length, "unsure", "Speech engine was unsure of this word."));
        return h.OrderBy(x => x.Start).ToList();
    }

    public static DictationResult Dictation(string spoken, IEnumerable<(string Term, string? SoundsLike)> dictionary, IEnumerable<string> medicines)
    {
        var (text, cmds) = ApplyVoiceCommands(spoken);
        text = Correct(text, dictionary);
        return new(text, cmds, Check(text, medicines));
    }

    // ------------------------------------------------------------------ de-identification
    public sealed record Identity(string FamilyName, string GivenNames, string? PreferredName, string? Nhi, string Mrn, DateOnly DateOfBirth, string? Phone, string? Email, string? Address);

    /// <summary>Replaces the patient's identifiers with tokens before text leaves TDS; <see cref="ReIdentify"/> puts them back inside BrightPAS.</summary>
    public static (string Text, Dictionary<string, string> Map) DeIdentify(string text, Identity p)
    {
        var map = new Dictionary<string, string>();
        var t = text;
        void Swap(string? value, string token, bool word = true)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2) return;
            var rx = word ? $@"\b{Regex.Escape(value.Trim())}\b" : Regex.Escape(value.Trim());
            if (!Regex.IsMatch(t, rx, RegexOptions.IgnoreCase)) return;
            t = Regex.Replace(t, rx, token, RegexOptions.IgnoreCase);
            map[token] = value.Trim();
        }
        Swap($"{p.GivenNames} {p.FamilyName}", "[PATIENT_FULL_NAME]");
        Swap(p.FamilyName, "[PATIENT_SURNAME]");
        foreach (var (g, i) in p.GivenNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select((g, i) => (g, i))) Swap(g, i == 0 ? "[PATIENT_FIRST_NAME]" : $"[PATIENT_GIVEN_{i + 1}]");
        Swap(p.PreferredName, "[PATIENT_PREFERRED_NAME]");
        Swap(p.Nhi, "[NHI]"); Swap(p.Mrn, "[MRN]");
        Swap(p.Phone, "[PHONE]", false); Swap(p.Email, "[EMAIL]", false); Swap(p.Address, "[ADDRESS]", false);
        foreach (var f in new[] { "d MMMM yyyy", "d MMM yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" })
            Swap(p.DateOfBirth.ToString(f, System.Globalization.CultureInfo.InvariantCulture), "[DATE_OF_BIRTH]", false);
        // Every token maps back, even when the original text used another form (the model may write [PATIENT_FIRST_NAME] for a full name it saw).
        var given = p.GivenNames.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        map.TryAdd("[PATIENT_FULL_NAME]", $"{p.GivenNames} {p.FamilyName}"); map.TryAdd("[PATIENT_SURNAME]", p.FamilyName);
        if (given.Length > 0) map.TryAdd("[PATIENT_FIRST_NAME]", given[0]);
        if (p.PreferredName is { Length: > 0 } pref) map.TryAdd("[PATIENT_PREFERRED_NAME]", pref);
        if (p.Nhi is { Length: > 0 } n) map.TryAdd("[NHI]", n);
        map.TryAdd("[MRN]", p.Mrn);
        // Anything else that looks like an NHI or phone number.
        t = Regex.Replace(t, @"\b[A-HJ-NP-Z]{3}\d{2}[0-9A-HJ-NP-Z]{2}\b", "[ID]");
        t = Regex.Replace(t, @"\b0\d{1,2}[ -]?\d{3}[ -]?\d{3,4}\b", "[PHONE]");
        return (t, map);
    }

    public static string ReIdentify(string text, IReadOnlyDictionary<string, string> map) =>
        map.OrderByDescending(kv => kv.Key.Length).Aggregate(text, (t, kv) => t.Replace(kv.Key, kv.Value, StringComparison.Ordinal));

    // ------------------------------------------------------------------ drafting
    public static IReadOnlyList<(string Heading, string Instruction)> ParseSections(string sections) =>
        sections.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => { var i = l.IndexOf(':'); return i > 0 ? (l[..i].Trim(), l[(i + 1)..].Trim()) : (l, ""); }).ToList();

    /// <summary>No AI: the template headings with the transcript under them for the clinician to edit (each line keeps its source).</summary>
    public static List<DraftedSentence> TemplateOnly(string sections, IReadOnlyList<Segment> segments)
    {
        var heads = ParseSections(sections);
        var list = new List<DraftedSentence>();
        if (heads.Count == 0) heads = [("Notes", "")];
        list.AddRange(segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).Select(s => new DraftedSentence(heads[0].Heading, $"{s.Speaker}: {s.Text.Trim()}", [s.Seq])));
        foreach (var (h, _) in heads.Skip(1)) list.Add(new(h, "", []));
        return list;
    }

    public static string Prompt(string templateName, string sections, IReadOnlyList<Segment> segments, IEnumerable<string> terms)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are drafting a clinical document for a New Zealand day-surgery clinic: \"{templateName}\".");
        sb.AppendLine("Rules: use only what is said in the transcript – never invent findings, doses or plans. Use New Zealand English spelling. Keep tokens such as [PATIENT_FIRST_NAME] exactly as written.");
        sb.AppendLine("Write [?] where something is unclear. Leave a section empty if nothing was said about it. Be concise and clinical.");
        sb.AppendLine("Sections (heading: what to include):");
        foreach (var (h, i) in ParseSections(sections)) sb.AppendLine($"- {h}: {i}");
        var t = terms.Take(200).ToList();
        if (t.Count > 0) sb.AppendLine("Local terms to spell exactly: " + string.Join(", ", t));
        sb.AppendLine("Transcript (numbered segments, speaker: text):");
        foreach (var s in segments) sb.AppendLine($"[{s.Seq}] {s.Speaker}: {s.Text}");
        sb.AppendLine("Answer with JSON only, no other text: {\"sections\":[{\"heading\":\"...\",\"sentences\":[{\"text\":\"...\",\"sources\":[segment numbers]}]}]}");
        return sb.ToString();
    }

    /// <summary>Reads the JSON draft from a model answer (tolerates text or code fences around it). Unknown headings are kept; sources outside the transcript are dropped.</summary>
    public static List<DraftedSentence> ParseDraft(string answer, int segmentCount)
    {
        var start = answer.IndexOf('{'); var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) throw new FormatException("The AI answer had no JSON draft.");
        JsonNode? root;
        try { root = JsonNode.Parse(answer[start..(end + 1)]); } catch (JsonException ex) { throw new FormatException("The AI draft was not valid JSON: " + ex.Message); }
        var list = new List<DraftedSentence>();
        foreach (var sec in root?["sections"] as JsonArray ?? [])
        {
            var heading = sec?["heading"]?.GetValue<string>() ?? "Notes";
            foreach (var s in sec?["sentences"] as JsonArray ?? [])
            {
                var text = s?["text"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var src = (s?["sources"] as JsonArray ?? []).Select(x => x is JsonValue v && v.TryGetValue<int>(out var n) ? n : -1).Where(n => n >= 1 && n <= segmentCount).Distinct().ToArray();
                list.Add(new(heading.Length > 100 ? heading[..100] : heading, text.Length > 2000 ? text[..2000] : text, src));
            }
        }
        if (list.Count == 0) throw new FormatException("The AI draft was empty.");
        return list;
    }

    public static string Render(IEnumerable<DraftedSentence> sentences) =>
        string.Join("\n\n", sentences.GroupBy(s => s.Section).Select(g => g.Key + "\n" + string.Join(" ", g.Select(s => s.Text).Where(x => x.Length > 0))).Select(x => x.TrimEnd()));

    /// <summary>Percentage of words changed between the AI draft and the signed text (word-level edit distance) – for accuracy audits.</summary>
    public static decimal EditPercent(string draft, string signed)
    {
        var a = Regex.Split(draft.Trim(), @"\s+").Where(x => x.Length > 0).ToArray();
        var b = Regex.Split(signed.Trim(), @"\s+").Where(x => x.Length > 0).ToArray();
        if (a.Length == 0) return b.Length == 0 ? 0 : 100;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1]; cur[0] = i;
            for (var j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal) ? 0 : 1));
            prev = cur;
        }
        return Math.Min(100, Math.Round(100m * prev[b.Length] / Math.Max(a.Length, b.Length), 1));
    }

    // ------------------------------------------------------------------ defaults
    public static readonly (string Code, string Name, ScribeContext Context, string Sections)[] DefaultTemplates =
    [
        ("CONSULT", "Surgeon consultation note", ScribeContext.Consultation,
            "Reason for referral: why the patient was referred\nHistory: presenting complaint and relevant history\nMedicines and allergies: current medicines, allergies and reactions\nExamination: findings\nDiscussion: options, risks and benefits discussed, patient questions\nPlan: procedure planned, consent, investigations, follow-up"),
        ("PREANAES", "Pre-anaesthetic assessment", ScribeContext.PreAnaesthetic,
            "Procedure: planned procedure and anaesthetic\nMedical history: cardiovascular, respiratory, other conditions\nAirway: airway assessment and previous anaesthetic problems\nMedicines and allergies: including anticoagulants and diabetes medicines (with stopping instructions)\nPlan: ASA grade, anaesthetic plan, fasting, escort and overnight carer"),
        ("TRIAGE", "Triage phone call note", ScribeContext.TriageCall,
            "Caller: who called and why\nSymptoms: what the patient reports\nRed flags: urgent warning signs asked about and answers\nAdvice given: advice and safety-netting\nOutcome: action and follow-up"),
        ("PREADMIT", "Nurse pre-admission call", ScribeContext.PreAdmission,
            "Health check: health questionnaire items discussed\nMedicines: what to take and stop before surgery\nFasting: fasting instructions given\nEscort: escort home and overnight carer\nQuestions: patient questions and answers"),
        ("DISCHARGE", "Discharge summary", ScribeContext.Discharge,
            "Procedure: what was done\nFindings: key findings\nMedicines: medicines given and prescribed\nWound and care: wound care and activity\nFollow-up: appointments, results to come, when to seek help"),
        ("GPLETTER", "Letter to GP", ScribeContext.Consultation,
            "Summary: reason for seeing the patient and outcome\nFindings: relevant findings\nPlan: management plan\nRequests: anything asked of the GP"),
        ("PTINSTR", "Patient instructions", ScribeContext.Discharge,
            "What we did: in plain words\nLooking after yourself: care at home\nMedicines: what to take\nWhen to get help: warning signs and who to call\nNext steps: follow-up"),
        ("POSTOP", "Post-operative review / follow-up call", ScribeContext.PostOpReview,
            "Recovery: how the patient is\nPain and medicines: pain control and medicines\nWound: wound check\nConcerns: problems raised\nPlan: follow-up"),
    ];

    public static readonly (string Term, string? SoundsLike, string Category)[] DefaultDictionary =
    [
        ("Māori", "maori,mowry", "Te reo"), ("whānau", "far no,fano,whanau", "Te reo"), ("kaumātua", "kaumatua", "Te reo"), ("kia ora", null, "Te reo"), ("tamariki", null, "Te reo"),
        ("Whakatū", "whakatu,fa ka too", "Place"), ("Nelson", null, "Place"), ("Tasman", null, "Place"), ("Motueka", "mot you ekka", "Place"), ("Richmond", null, "Place"),
        ("Tasman Day Surgery", "tasman day surgery", "Clinic"), ("HealthLink", "health link", "NZ health"), ("ACC", "a c c", "NZ health"), ("NHI", "n h i", "NZ health"), ("Health NZ", "health new zealand", "NZ health"),
        ("cholecystectomy", "colecystectomy,cole assist ectomy", "Procedure"), ("colonoscopy", "colon oscopy", "Procedure"), ("gastroscopy", "gastro scopy", "Procedure"),
        ("paracetamol", null, "Medicine"), ("ibuprofen", null, "Medicine"), ("oxycodone", null, "Medicine"), ("tramadol", null, "Medicine"), ("ondansetron", null, "Medicine"),
        ("cefazolin", "cefazoline,cephazolin", "Medicine"), ("dabigatran", null, "Medicine"), ("rivaroxaban", null, "Medicine"), ("apixaban", null, "Medicine"), ("warfarin", null, "Medicine"),
        ("metformin", null, "Medicine"), ("empagliflozin", null, "Medicine"), ("midazolam", null, "Medicine"), ("fentanyl", null, "Medicine"), ("propofol", null, "Medicine"),
    ];
}
