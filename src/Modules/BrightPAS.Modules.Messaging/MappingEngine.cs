using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BrightPAS.Modules.Messaging;

/// <summary>A message the mapping engine can read and change by path.</summary>
public interface IMessageDoc
{
    MessageFormat Format { get; }
    /// <summary>Value at a path; <paramref name="occurrence"/> replaces [*] (1-based).</summary>
    string Get(string path, int occurrence = 1);
    void Set(string path, string value, int occurrence = 1);
    /// <summary>How many occurrences the [*] part of a path has.</summary>
    int Count(string path);
    string Render();
}

public sealed record DocNode(string Path, string Label, string? Value, IReadOnlyList<DocNode> Children);

public static class MessageDocs
{
    public static IMessageDoc Parse(MessageFormat format, string text) => format switch
    {
        MessageFormat.Hl7v2 => Hl7Doc.Parse(text),
        MessageFormat.Json or MessageFormat.Fhir => JsonDoc.Parse(text, format),
        MessageFormat.Dicom => DicomDoc.Parse(text),
        MessageFormat.Xml => XmlDoc.Parse(text),
        MessageFormat.X12 => X12Doc.Parse(text),
        _ => new TextDoc(text),
    };

    public static IReadOnlyList<DocNode> Tree(MessageFormat format, string text) => Parse(format, text) switch
    {
        Hl7Doc h => h.Tree(), JsonDoc j => j.Tree(), DicomDoc d => d.Tree(), XmlDoc x => x.Tree(), X12Doc e => e.Tree(), var t => [new DocNode("text", "Text", t.Render(), [])],
    };

    public static IMessageDoc Empty(MessageFormat format) => format switch
    {
        MessageFormat.Hl7v2 => Hl7Doc.Parse("MSH|^~\\&|BrightPAS|TDS||||||||2.4"),
        MessageFormat.Json or MessageFormat.Fhir => JsonDoc.Parse("{}", format),
        MessageFormat.Dicom => DicomDoc.Parse("{}"),
        MessageFormat.Xml => XmlDoc.Parse("<root/>"),
        MessageFormat.X12 => X12Doc.Parse(""),
        _ => new TextDoc(""),
    };

    /// <summary>Plain-English problems, empty when the message looks right for its format.</summary>
    public static IReadOnlyList<string> Validate(MessageFormat format, string text)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) { errors.Add("The message is empty."); return errors; }
        switch (format)
        {
            case MessageFormat.Hl7v2:
                var segs = Hl7Doc.SplitSegments(text);
                if (!segs[0].StartsWith("MSH", StringComparison.Ordinal)) { errors.Add("An HL7 message must start with an MSH segment."); return errors; }
                if (segs[0].Length < 8) { errors.Add("The MSH segment is too short to hold the separators."); return errors; }
                var doc = Hl7Doc.Parse(text);
                if (doc.Get("MSH-9.1").Length == 0) errors.Add("MSH-9 (message type, e.g. ORU^R01) is missing.");
                if (doc.Get("MSH-10").Length == 0) errors.Add("MSH-10 (message control ID) is missing – the receiver cannot acknowledge it.");
                if (doc.Get("MSH-12").Length == 0) errors.Add("MSH-12 (HL7 version, e.g. 2.4) is missing.");
                foreach (var s in segs.Where(s => s.Length > 0))
                    if (s.Length < 3 || !Regex.IsMatch(s[..3], "^[A-Z][A-Z0-9]{2}$")) errors.Add($"\"{Trim(s, 20)}\" is not a valid segment (segments start with three capital letters, e.g. PID).");
                if (doc.Get("MSH-9.1") is "ORU" && doc.Count("OBX[*]") == 0) errors.Add("An ORU result message has no OBX (result) segments.");
                break;
            case MessageFormat.Json:
            case MessageFormat.Fhir:
                try
                {
                    var node = JsonNode.Parse(text);
                    if (format == MessageFormat.Fhir)
                    {
                        if (node is not JsonObject o || o["resourceType"]?.GetValue<string>() is not { Length: > 0 } rt) errors.Add("A FHIR message must be a JSON object with a resourceType (e.g. \"Patient\" or \"Bundle\").");
                        else if (rt == "Bundle" && o["entry"] is not JsonArray) errors.Add("A FHIR Bundle needs an entry list.");
                    }
                }
                catch (JsonException ex) { errors.Add($"This is not valid JSON: {ex.Message}"); }
                break;
            case MessageFormat.Dicom:
                errors.AddRange(DicomDoc.Validate(text));
                break;
            case MessageFormat.Xml:
                errors.AddRange(XmlDoc.Validate(text));
                break;
            case MessageFormat.X12:
                errors.AddRange(X12Doc.Validate(text));
                break;
        }
        return errors;
    }

    internal static string Trim(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

/// <summary>HL7 v2 message as segments of fields. Paths: SEG-f, SEG-f.c, SEG-f.c.s, SEG[n]-f (nth occurrence, 1-based) or SEG[*]-f.</summary>
public sealed partial class Hl7Doc : IMessageDoc
{
    private readonly List<List<string>> _segments;
    private char _f = '|', _c = '^', _r = '~', _s = '&';
    public MessageFormat Format => MessageFormat.Hl7v2;

    private Hl7Doc(List<List<string>> segments) => _segments = segments;

    public static string[] SplitSegments(string text) =>
        text.Replace("\r\n", "\r").Replace('\n', '\r').Split('\r', StringSplitOptions.RemoveEmptyEntries).Select(s => s.TrimEnd()).ToArray();

    public static Hl7Doc Parse(string text)
    {
        var lines = SplitSegments(text);
        var doc = new Hl7Doc([]);
        if (lines.Length > 0 && lines[0].StartsWith("MSH", StringComparison.Ordinal) && lines[0].Length >= 8)
        {
            doc._f = lines[0][3];
            var enc = lines[0][4..8];
            doc._c = enc[0]; doc._r = enc[1]; doc._s = enc[3];
        }
        foreach (var l in lines) doc._segments.Add([.. l.Split(doc._f)]);
        return doc;
    }

    [GeneratedRegex(@"^(?<seg>[A-Z][A-Z0-9]{2})(\[(?<occ>\d+|\*)\])?(-(?<f>\d+)(\.(?<c>\d+))?(\.(?<s>\d+))?)?$")]
    private static partial Regex PathRx();

    public static bool IsValidPath(string path) => PathRx().IsMatch(path.Trim());

    private static (string Seg, int? Occ, bool Star, int F, int C, int S) ParsePath(string path)
    {
        var m = PathRx().Match(path.Trim());
        if (!m.Success) throw new MappingException($"\"{path}\" is not an HL7 path. Use e.g. PID-5.1, OBX[*]-5 or MSH-9.");
        var occ = m.Groups["occ"].Value;
        return (m.Groups["seg"].Value, occ is "" or "*" ? null : int.Parse(occ, CultureInfo.InvariantCulture), occ == "*",
            m.Groups["f"].Success ? int.Parse(m.Groups["f"].Value, CultureInfo.InvariantCulture) : 0,
            m.Groups["c"].Success ? int.Parse(m.Groups["c"].Value, CultureInfo.InvariantCulture) : 0,
            m.Groups["s"].Success ? int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture) : 0);
    }

    private List<string>? Segment(string name, int occurrence, bool create)
    {
        var found = 0;
        var last = -1;
        for (var i = 0; i < _segments.Count; i++)
        {
            if (_segments[i][0] != name) continue;
            last = i;
            if (++found == occurrence) return _segments[i];
        }
        if (!create) return null;
        List<string>? made = null;
        for (; found < occurrence; found++)
        {
            made = [name];
            var at = last >= 0 ? last + 1 : _segments.Count;
            _segments.Insert(at, made);
            last = at;
        }
        return made;
    }

    // MSH-1 is the field separator itself, so MSH field n is at index n-1.
    private static int Index(string seg, int field) => seg == "MSH" ? field - 1 : field;

    public string Get(string path, int occurrence = 1)
    {
        var p = ParsePath(path);
        var seg = Segment(p.Seg, p.Occ ?? occurrence, false);
        if (seg is null) return "";
        if (p.F == 0) return string.Join(_f, seg);
        if (p.Seg == "MSH" && p.F == 1) return _f.ToString();
        var i = Index(p.Seg, p.F);
        if (i >= seg.Count) return "";
        var field = seg[i];
        if (p.Seg == "MSH" && p.F == 2) return field;
        field = field.Split(_r)[0];
        if (p.C == 0) return Unescape(field);
        var comps = field.Split(_c);
        if (p.C > comps.Length) return "";
        var comp = comps[p.C - 1];
        if (p.S == 0) return Unescape(comp);
        var subs = comp.Split(_s);
        return p.S > subs.Length ? "" : Unescape(subs[p.S - 1]);
    }

    public void Set(string path, string value, int occurrence = 1)
    {
        var p = ParsePath(path);
        if (p.F == 0) throw new MappingException($"\"{path}\": choose a field to set (e.g. {p.Seg}-3).");
        if (p.Seg == "MSH" && p.F <= 2) throw new MappingException("MSH-1 and MSH-2 hold the separators and cannot be changed.");
        var seg = Segment(p.Seg, p.Occ ?? occurrence, true)!;
        var i = Index(p.Seg, p.F);
        while (seg.Count <= i) seg.Add("");
        var v = Escape(value);
        if (p.C == 0) { seg[i] = v; return; }
        var comps = seg[i].Split(_r)[0].Split(_c).ToList();
        while (comps.Count < p.C) comps.Add("");
        if (p.S == 0) comps[p.C - 1] = v;
        else
        {
            var subs = comps[p.C - 1].Split(_s).ToList();
            while (subs.Count < p.S) subs.Add("");
            subs[p.S - 1] = value.Replace(_s.ToString(), "");
            comps[p.C - 1] = string.Join(_s, subs);
        }
        seg[i] = string.Join(_c, comps).TrimEnd(_c);
    }

    public int Count(string path) => _segments.Count(s => s[0] == ParsePath(path).Seg);

    public string Render() => string.Join("\r", _segments.Select(s => string.Join(_f, s).TrimEnd(_f))) + "\r";

    private string Escape(string v) => v.Replace("\\", "\\E\\").Replace(_f.ToString(), "\\F\\").Replace(_c.ToString(), "\\S\\").Replace(_r.ToString(), "\\R\\").Replace(_s.ToString(), "\\T\\").Replace("\r", " ").Replace("\n", " ");

    private string Unescape(string v) => !v.Contains('\\') ? v : v.Replace("\\F\\", _f.ToString()).Replace("\\S\\", _c.ToString()).Replace("\\R\\", _r.ToString()).Replace("\\T\\", _s.ToString()).Replace("\\.br\\", " ").Replace("\\E\\", "\\");

    /// <summary>Readable tree for the designer: segments → fields → components, with standard field names.</summary>
    public IReadOnlyList<DocNode> Tree()
    {
        var seen = new Dictionary<string, int>();
        var list = new List<DocNode>();
        foreach (var seg in _segments)
        {
            var name = seg[0];
            var n = seen[name] = seen.GetValueOrDefault(name) + 1;
            var total = _segments.Count(s => s[0] == name);
            var prefix = total > 1 ? $"{name}[{n}]" : name;
            var fields = new List<DocNode>();
            var max = name == "MSH" ? seg.Count + 1 : seg.Count;
            for (var f = 1; f < max; f++)
            {
                var path = $"{prefix}-{f}";
                var value = Get(path);
                var fieldName = Hl7Fields.Name(name, f);
                if (value.Length == 0 && fieldName is null) continue;
                var comps = new List<DocNode>();
                var raw = name == "MSH" && f <= 2 ? "" : (Index(name, f) < seg.Count ? seg[Index(name, f)].Split(_r)[0] : "");
                var parts = raw.Split(_c);
                if (parts.Length > 1)
                    for (var c = 1; c <= parts.Length; c++)
                        if (parts[c - 1].Length > 0) comps.Add(new($"{path}.{c}", $"{name}-{f}.{c} {Hl7Fields.Component(name, f, c) ?? ""}".Trim(), Unescape(parts[c - 1]), []));
                fields.Add(new(path, $"{name}-{f} {fieldName ?? ""}".Trim(), comps.Count > 0 ? null : value, comps));
            }
            list.Add(new(prefix, $"{prefix} {Hl7Fields.Segment(name) ?? ""}".Trim(), null, fields));
        }
        return list;
    }
}

/// <summary>JSON / FHIR document. Paths: a.b.c, a.list[0].b (0-based) or a.list[*].b.</summary>
public sealed partial class JsonDoc : IMessageDoc
{
    private JsonNode _root;
    public MessageFormat Format { get; }
    private JsonDoc(JsonNode root, MessageFormat format) { _root = root; Format = format; }

    public static JsonDoc Parse(string text, MessageFormat format)
    {
        try { return new(JsonNode.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text) ?? new JsonObject(), format); }
        catch (JsonException ex) { throw new MappingException($"The JSON could not be read: {ex.Message}"); }
    }

    [GeneratedRegex(@"^(?<name>[A-Za-z_$][A-Za-z0-9_\-$]*)?(\[(?<i>\d+|\*)\])?$")]
    private static partial Regex PartRx();

    public static bool IsValidPath(string path) => path.Length > 0 && path.Split('.').All(p => PartRx().IsMatch(p) && p.Length > 0);

    private static List<(string? Name, int? Index, bool Star)> Parts(string path)
    {
        if (!IsValidPath(path.Trim())) throw new MappingException($"\"{path}\" is not a JSON path. Use e.g. patient.name[0].family or entry[*].resource.id.");
        return path.Trim().Split('.').Select(p =>
        {
            var m = PartRx().Match(p);
            var i = m.Groups["i"].Value;
            return (m.Groups["name"].Success && m.Groups["name"].Value.Length > 0 ? m.Groups["name"].Value : null,
                i is "" or "*" ? (int?)null : int.Parse(i, CultureInfo.InvariantCulture), i == "*");
        }).ToList();
    }

    public string Get(string path, int occurrence = 1)
    {
        JsonNode? n = _root;
        foreach (var (name, index, star) in Parts(path))
        {
            if (name is not null) n = (n as JsonObject)?[name];
            if (index is not null || star) n = n is JsonArray a && (index ?? occurrence - 1) is var k && k >= 0 && k < a.Count ? a[k] : null;
            if (n is null) return "";
        }
        return n switch
        {
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            null => "",
            _ => n.ToJsonString(),
        };
    }

    public void Set(string path, string value, int occurrence = 1)
    {
        var parts = Parts(path);
        JsonNode parent = _root;
        for (var i = 0; i < parts.Count; i++)
        {
            var (name, index, star) = parts[i];
            var last = i == parts.Count - 1;
            var isArray = index is not null || star;
            if (name is not null)
            {
                if (parent is not JsonObject o) throw new MappingException($"\"{path}\": {name} is inside something that is not an object.");
                if (last && !isArray) { o[name] = JsonValue.Create(value); return; }
                if (o[name] is null) o[name] = isArray ? new JsonArray() : new JsonObject();
                parent = o[name]!;
            }
            if (isArray)
            {
                if (parent is not JsonArray a) throw new MappingException($"\"{path}\": {name ?? "value"} is not a list.");
                var k = index ?? occurrence - 1;
                while (a.Count <= k) a.Add(last ? null : (JsonNode)new JsonObject());
                if (last) { a[k] = JsonValue.Create(value); return; }
                a[k] ??= new JsonObject();
                parent = a[k]!;
            }
        }
    }

    public int Count(string path)
    {
        var star = path.IndexOf("[*]", StringComparison.Ordinal);
        if (star < 0) return 1;
        JsonNode? n = _root;
        foreach (var (name, index, _) in Parts(path[..star]))
        {
            if (name is not null) n = (n as JsonObject)?[name];
            if (index is not null) n = n is JsonArray a && index < a.Count ? a[index.Value] : null;
            if (n is null) return 0;
        }
        return n is JsonArray arr ? arr.Count : 0;
    }

    public string Render() => _root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    public IReadOnlyList<DocNode> Tree() => Children(_root, "");

    private static IReadOnlyList<DocNode> Children(JsonNode? n, string prefix)
    {
        var list = new List<DocNode>();
        switch (n)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    var path = prefix.Length == 0 ? k : $"{prefix}.{k}";
                    list.Add(Node(v, path, k));
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) list.Add(Node(a[i], $"{prefix}[{i}]", $"[{i}]"));
                break;
        }
        return list;
    }

    private static DocNode Node(JsonNode? v, string path, string label) => v is JsonObject or JsonArray
        ? new(path, label, null, Children(v, path))
        : new(path, label, v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v?.ToJsonString() ?? "", []);
}

public sealed class TextDoc(string text) : IMessageDoc
{
    private string _text = text;
    public MessageFormat Format => MessageFormat.Text;
    public string Get(string path, int occurrence = 1) => _text;
    public void Set(string path, string value, int occurrence = 1) => _text = value;
    public int Count(string path) => 1;
    public string Render() => _text;
}

public sealed class MappingException(string message) : Exception(message);

/// <summary>A translation table in memory for one run.</summary>
public sealed record Translation(string Name, MissingCode WhenMissing, string? DefaultValue, IReadOnlyDictionary<string, string> Codes);

public sealed record MappingResult(string? Output, bool FilteredOut, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, IReadOnlyList<string> Steps);

/// <summary>Runs filters and rules against a message (used for live messages and the designer preview).</summary>
public static class MappingEngine
{
    public static MappingResult Run(MessageFormat sourceFormat, MessageFormat targetFormat, string input, StartFrom start, string? template,
        IReadOnlyList<FilterRule> filters, IReadOnlyList<MappingRule> rules, IReadOnlyDictionary<Guid, Translation> tables)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var steps = new List<string>();
        IMessageDoc src;
        try { src = MessageDocs.Parse(sourceFormat, input); }
        catch (MappingException ex) { return new(null, false, [ex.Message], [], []); }

        foreach (var f in filters.OrderBy(f => f.Sequence))
        {
            try
            {
                var v = src.Get(f.Source);
                if (!Test(v, f.Operator, f.Value))
                    return new(null, true, [], [], [$"Filtered out: {f.Source} is \"{MessageDocs.Trim(v, 40)}\" – needs {Describe(f.Operator, f.Value)}."]);
            }
            catch (MappingException ex) { errors.Add($"Filter {f.Sequence}: {ex.Message}"); }
        }
        if (errors.Count > 0) return new(null, false, errors, warnings, steps);

        IMessageDoc dst;
        try
        {
            dst = start == StartFrom.CopySource && sourceFormat == targetFormat ? MessageDocs.Parse(targetFormat, input)
                : !string.IsNullOrWhiteSpace(template) ? MessageDocs.Parse(targetFormat, template) : MessageDocs.Empty(targetFormat);
        }
        catch (MappingException ex) { return new(null, false, [$"Template: {ex.Message}"], [], []); }

        foreach (var r in rules.OrderBy(r => r.Sequence))
        {
            try
            {
                var sources = (r.Source ?? "").Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var repeat = sources.FirstOrDefault(s => s.Contains("[*]"));
                var times = repeat is null ? 1 : src.Count(repeat);
                if (repeat is not null && !r.Target.Contains("[*]")) times = Math.Min(times, 1);
                for (var i = 1; i <= times; i++)
                {
                    if (r.ConditionSource is { Length: > 0 } cs && r.ConditionOperator is { } op && !Test(src.Get(cs, i), op, r.ConditionValue)) continue;
                    var values = sources.Select(s => src.Get(s, i)).ToArray();
                    var value = Apply(r, values, tables, warnings);
                    if (value is null) continue;
                    dst.Set(r.Target, value, i);
                }
                steps.Add($"{r.Target} ← {(r.Function == RuleFunction.Constant ? $"\"{r.Argument}\"" : r.Source)}{(r.Function is RuleFunction.Copy or RuleFunction.Constant ? "" : $" ({r.Function})")}{(times > 1 ? $" ×{times}" : "")}");
            }
            catch (MappingException ex) { errors.Add($"Rule {r.Sequence} ({r.Target}): {ex.Message}"); }
        }
        if (errors.Count > 0) return new(null, false, errors, warnings, steps);
        var output = dst.Render();
        errors.AddRange(MessageDocs.Validate(targetFormat, output).Select(e => "Output: " + e));
        return new(output, false, errors, warnings, steps);
    }

    private static string? Apply(MappingRule r, string[] values, IReadOnlyDictionary<Guid, Translation> tables, List<string> warnings)
    {
        var v = values.FirstOrDefault() ?? "";
        switch (r.Function)
        {
            case RuleFunction.Copy: return v;
            case RuleFunction.Constant: return r.Argument ?? "";
            case RuleFunction.Clear: return "";
            case RuleFunction.Upper: return v.ToUpperInvariant();
            case RuleFunction.Lower: return v.ToLowerInvariant();
            case RuleFunction.Default: return v.Length > 0 ? v : r.Argument ?? "";
            case RuleFunction.Join: return string.Join(r.Argument ?? " ", values.Where(x => x.Length > 0));
            case RuleFunction.Split:
            {
                var (sep, idx) = SplitArg(r.Argument);
                var parts = v.Split(sep);
                return idx >= 1 && idx <= parts.Length ? parts[idx - 1].Trim() : "";
            }
            case RuleFunction.Substring:
            {
                var a = (r.Argument ?? "0").Split(',');
                var start = Math.Max(0, int.TryParse(a[0], out var s) ? s : 0);
                if (start >= v.Length) return "";
                var len = a.Length > 1 && int.TryParse(a[1], out var l) ? Math.Min(l, v.Length - start) : v.Length - start;
                return v.Substring(start, len);
            }
            case RuleFunction.Date:
            {
                if (v.Length == 0) return "";
                var a = (r.Argument ?? "").Split('>', 2);
                if (a.Length != 2) throw new MappingException("Date needs \"from>to\" formats, e.g. yyyyMMdd>yyyy-MM-dd.");
                var from = a[0].Trim();
                var input = v.Length > from.Length ? v[..from.Length] : v;
                if (!DateTime.TryParseExact(input, from, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    throw new MappingException($"\"{v}\" does not match the date format {from}.");
                return d.ToString(a[1].Trim(), CultureInfo.InvariantCulture);
            }
            case RuleFunction.Translate:
            {
                if (r.TranslationTableId is not { } tid || !tables.TryGetValue(tid, out var t)) throw new MappingException("Choose a translation table.");
                if (t.Codes.TryGetValue(v, out var to)) return to;
                switch (t.WhenMissing)
                {
                    case MissingCode.PassThrough: if (v.Length > 0) warnings.Add($"\"{v}\" is not in {t.Name} – passed through unchanged."); return v;
                    case MissingCode.Blank: if (v.Length > 0) warnings.Add($"\"{v}\" is not in {t.Name} – left blank."); return "";
                    case MissingCode.UseDefault: if (v.Length > 0) warnings.Add($"\"{v}\" is not in {t.Name} – default used."); return t.DefaultValue ?? "";
                    default: throw new MappingException($"\"{v}\" is not in the translation table {t.Name}.");
                }
            }
            default: return v;
        }
    }

    private static (string Sep, int Index) SplitArg(string? arg)
    {
        var a = (arg ?? " |1").Split('|');
        return (a[0].Length == 0 ? " " : a[0], a.Length > 1 && int.TryParse(a[1], out var i) ? i : 1);
    }

    public static bool Test(string value, ConditionOperator op, string? expected)
    {
        var e = expected ?? "";
        return op switch
        {
            ConditionOperator.Equals => string.Equals(value, e, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEquals => !string.Equals(value, e, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Contains => value.Contains(e, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.StartsWith => value.StartsWith(e, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.IsEmpty => value.Length == 0,
            ConditionOperator.NotEmpty => value.Length > 0,
            ConditionOperator.In => e.Split(',', StringSplitOptions.TrimEntries).Contains(value, StringComparer.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static string Describe(ConditionOperator op, string? v) => op switch
    {
        ConditionOperator.IsEmpty => "empty", ConditionOperator.NotEmpty => "a value",
        ConditionOperator.In => $"one of {v}", _ => $"{op.ToString().ToLowerInvariant()} \"{v}\"",
    };

    /// <summary>Checks a path for the format before it is saved.</summary>
    public static string? PathProblem(MessageFormat format, string path) => format switch
    {
        MessageFormat.Hl7v2 when !Hl7Doc.IsValidPath(path) => $"\"{path}\" is not an HL7 path (e.g. PID-5.1, OBX[*]-5).",
        MessageFormat.Json or MessageFormat.Fhir when !JsonDoc.IsValidPath(path) => $"\"{path}\" is not a JSON path (e.g. name[0].family).",
        MessageFormat.Xml when !XmlDoc.IsValidPath(path) => $"\"{path}\" is not an XML path (e.g. ClinicalDocument/recordTarget/patientRole/id/@extension).",
        MessageFormat.X12 when !X12Doc.IsValidPath(path) => $"\"{path}\" is not an X12 path (e.g. NM1[2]-3 or CLM-1).",
        MessageFormat.Dicom when !DicomDoc.IsValidPath(path) => $"\"{path}\" is not a DICOM keyword or tag (e.g. PatientName or (0008,0050)).",
        _ => null,
    };
}

/// <summary>Standard names for common HL7 v2.4 segments and fields so the designer shows "PID-5 Patient name" rather than raw positions.</summary>
public static class Hl7Fields
{
    private static readonly Dictionary<string, string> Segments = new()
    {
        ["MSH"] = "Message header", ["EVN"] = "Event", ["PID"] = "Patient identification", ["PD1"] = "Additional demographics", ["NK1"] = "Next of kin",
        ["PV1"] = "Patient visit", ["PV2"] = "Visit – additional", ["AL1"] = "Allergy", ["ORC"] = "Common order", ["OBR"] = "Observation request",
        ["OBX"] = "Observation / result", ["NTE"] = "Notes and comments", ["MSA"] = "Message acknowledgement", ["ERR"] = "Error", ["RF1"] = "Referral information",
        ["PRD"] = "Provider data", ["SCH"] = "Schedule activity", ["AIS"] = "Appointment – service", ["ZDS"] = "Additional data (local)",
    };

    private static readonly Dictionary<string, string[]> Fields = new()
    {
        ["MSH"] = ["Field separator", "Encoding characters", "Sending application", "Sending facility", "Receiving application", "Receiving facility", "Date/time of message", "Security", "Message type", "Message control ID", "Processing ID", "Version ID", "Sequence number", "Continuation pointer", "Accept acknowledgement type", "Application acknowledgement type", "Country code", "Character set"],
        ["EVN"] = ["Event type code", "Recorded date/time", "Date/time planned event", "Event reason code", "Operator ID", "Event occurred"],
        ["PID"] = ["Set ID", "Patient ID (external)", "Patient identifier list (NHI/MRN)", "Alternate patient ID", "Patient name", "Mother's maiden name", "Date/time of birth", "Administrative sex", "Patient alias", "Race / ethnicity", "Patient address", "County code", "Phone – home", "Phone – business", "Primary language", "Marital status", "Religion", "Patient account number", "SSN", "Driver's licence", "Mother's identifier", "Ethnic group", "Birth place", "Multiple birth", "Birth order", "Citizenship", "Veterans status", "Nationality", "Death date/time", "Death indicator"],
        ["PV1"] = ["Set ID", "Patient class", "Assigned location", "Admission type", "Preadmit number", "Prior location", "Attending doctor", "Referring doctor", "Consulting doctor", "Hospital service", "Temporary location", "Preadmit test indicator", "Re-admission indicator", "Admit source", "Ambulatory status", "VIP indicator", "Admitting doctor", "Patient type", "Visit number", "Financial class"],
        ["NK1"] = ["Set ID", "Name", "Relationship", "Address", "Phone number", "Business phone", "Contact role"],
        ["AL1"] = ["Set ID", "Allergen type", "Allergen code/description", "Severity", "Reaction", "Identification date"],
        ["ORC"] = ["Order control", "Placer order number", "Filler order number", "Placer group number", "Order status", "Response flag", "Quantity/timing", "Parent", "Date/time of transaction", "Entered by", "Verified by", "Ordering provider", "Enterer's location", "Call back phone", "Order effective date/time"],
        ["OBR"] = ["Set ID", "Placer order number", "Filler order number (accession)", "Universal service ID (test)", "Priority", "Requested date/time", "Observation date/time", "Observation end date/time", "Collection volume", "Collector identifier", "Specimen action code", "Danger code", "Relevant clinical info", "Specimen received date/time", "Specimen source", "Ordering provider", "Order callback phone", "Placer field 1", "Placer field 2", "Filler field 1", "Filler field 2", "Results report/status change", "Charge to practice", "Diagnostic service section", "Result status", "Parent result", "Quantity/timing", "Result copies to"],
        ["OBX"] = ["Set ID", "Value type", "Observation identifier", "Observation sub-ID", "Observation value", "Units", "Reference range", "Abnormal flags", "Probability", "Nature of abnormal test", "Result status", "Date last normal value", "User-defined access checks", "Date/time of observation", "Producer's ID", "Responsible observer"],
        ["NTE"] = ["Set ID", "Source of comment", "Comment", "Comment type"],
        ["MSA"] = ["Acknowledgement code", "Message control ID", "Text message", "Expected sequence number", "Delayed acknowledgement type", "Error condition"],
        ["ERR"] = ["Error code and location"],
        ["RF1"] = ["Referral status", "Referral priority", "Referral type", "Referral disposition", "Referral category", "Originating referral identifier", "Effective date", "Expiration date", "Process date", "Referral reason"],
        ["PRD"] = ["Provider role", "Provider name", "Provider address", "Provider location", "Provider communication", "Preferred contact method", "Provider identifiers"],
    };

    private static readonly Dictionary<string, string[]> Components = new()
    {
        ["PID-5"] = ["Family name", "Given name", "Second/further names", "Suffix", "Prefix", "Degree", "Name type"],
        ["PID-3"] = ["ID", "Check digit", "Check digit scheme", "Assigning authority", "Identifier type (NHI/MR)"],
        ["PID-11"] = ["Street", "Other designation", "City", "State/region", "Postcode", "Country", "Address type"],
        ["MSH-9"] = ["Message code", "Trigger event", "Message structure"],
        ["OBX-3"] = ["Identifier (code)", "Text", "Coding system"],
        ["OBR-4"] = ["Identifier (code)", "Text", "Coding system"],
        ["OBR-16"] = ["ID (CPN)", "Family name", "Given name"],
        ["PV1-7"] = ["ID (CPN)", "Family name", "Given name"],
        ["NK1-2"] = ["Family name", "Given name"],
    };

    public static string? Segment(string seg) => Segments.GetValueOrDefault(seg);
    public static string? Name(string seg, int field) => Fields.TryGetValue(seg, out var f) && field >= 1 && field <= f.Length ? f[field - 1] : null;
    public static string? Component(string seg, int field, int comp) => Components.TryGetValue($"{seg}-{field}", out var c) && comp >= 1 && comp <= c.Length ? c[comp - 1] : null;

    /// <summary>Field list for the designer's target picker.</summary>
    public static IReadOnlyList<object> Catalogue() =>
        Fields.Select(kv => (object)new { segment = kv.Key, name = Segments.GetValueOrDefault(kv.Key), fields = kv.Value.Select((n, i) => new { path = $"{kv.Key}-{i + 1}", name = n }).ToList() }).ToList();
}
