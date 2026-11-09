using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BrightPAS.Modules.Messaging;

/// <summary>
/// XML / HL7 CDA document. Paths are slash-separated element names (namespaces ignored), with [n] (1-based) or [*], and an optional final @attribute:
/// ClinicalDocument/recordTarget/patientRole/id[1]/@extension or component/structuredBody/component[*]/section/title.
/// </summary>
public sealed partial class XmlDoc : IMessageDoc
{
    private readonly XDocument _doc;
    public MessageFormat Format => MessageFormat.Xml;
    private XmlDoc(XDocument doc) => _doc = doc;

    public static XmlDoc Parse(string text)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 };
            using var r = XmlReader.Create(new StringReader(string.IsNullOrWhiteSpace(text) ? "<root/>" : text), settings);
            return new(XDocument.Load(r));
        }
        catch (XmlException ex) { throw new MappingException($"The XML could not be read: {ex.Message}"); }
    }

    [GeneratedRegex(@"^(?<n>[A-Za-z_][\w\-.]*)(\[(?<i>\d+|\*)\])?$")]
    private static partial Regex StepRx();
    [GeneratedRegex(@"^@(?<a>[A-Za-z_][\w\-.]*)$")]
    private static partial Regex AttrRx();

    public static bool IsValidPath(string path)
    {
        var parts = path.Trim().Trim('/').Split('/');
        if (parts.Length == 0 || parts[0].Length == 0) return false;
        for (var i = 0; i < parts.Length; i++)
            if (!(StepRx().IsMatch(parts[i]) || (i == parts.Length - 1 && i > 0 && AttrRx().IsMatch(parts[i])))) return false;
        return true;
    }

    private static (List<(string Name, int? Index, bool Star)> Steps, string? Attr) Parts(string path)
    {
        if (!IsValidPath(path)) throw new MappingException($"\"{path}\" is not an XML path. Use e.g. ClinicalDocument/recordTarget/patientRole/id[1]/@extension.");
        var parts = path.Trim().Trim('/').Split('/').ToList();
        string? attr = null;
        if (parts[^1].StartsWith('@')) { attr = parts[^1][1..]; parts.RemoveAt(parts.Count - 1); }
        return (parts.Select(p =>
        {
            var m = StepRx().Match(p);
            var i = m.Groups["i"].Value;
            return (m.Groups["n"].Value, i is "" or "*" ? (int?)null : int.Parse(i, CultureInfo.InvariantCulture), i == "*");
        }).ToList(), attr);
    }

    private XElement? Find(List<(string Name, int? Index, bool Star)> steps, int occurrence, bool create)
    {
        if (_doc.Root is null) return null;
        if (steps[0].Name != _doc.Root.Name.LocalName) return null;
        var el = _doc.Root;
        for (var s = 1; s < steps.Count; s++)
        {
            var (name, index, star) = steps[s];
            var k = index ?? (star ? occurrence : 1);
            var matches = el.Elements().Where(e => e.Name.LocalName == name).ToList();
            if (matches.Count >= k) { el = matches[k - 1]; continue; }
            if (!create) return null;
            for (var c = matches.Count; c < k; c++)
            {
                var made = new XElement(el.Name.Namespace + name);
                var lastSame = el.Elements().LastOrDefault(e => e.Name.LocalName == name);
                if (lastSame is not null) lastSame.AddAfterSelf(made); else el.Add(made);
                matches.Add(made);
            }
            el = matches[k - 1];
        }
        return el;
    }

    public string Get(string path, int occurrence = 1)
    {
        var (steps, attr) = Parts(path);
        var el = Find(steps, occurrence, false);
        if (el is null) return "";
        return attr is null ? (el.HasElements ? string.Concat(el.Nodes().OfType<XText>().Select(t => t.Value)).Trim() : el.Value.Trim()) : el.Attribute(attr)?.Value ?? "";
    }

    public void Set(string path, string value, int occurrence = 1)
    {
        var (steps, attr) = Parts(path);
        if (_doc.Root is null || steps[0].Name != _doc.Root.Name.LocalName) throw new MappingException($"The document's top element is {_doc.Root?.Name.LocalName}, not {steps[0].Name}.");
        var el = Find(steps, occurrence, true)!;
        if (attr is null) { if (el.HasElements) throw new MappingException($"{steps[^1].Name} contains other elements – set one of those instead."); el.Value = value; }
        else el.SetAttributeValue(attr, value);
    }

    public int Count(string path)
    {
        var star = path.IndexOf("[*]", StringComparison.Ordinal);
        if (star < 0) return 1;
        var (steps, _) = Parts(path[..star]);
        var parent = steps.Count == 1 ? null : Find(steps.Take(steps.Count - 1).ToList(), 1, false);
        if (steps.Count == 1) return _doc.Root?.Name.LocalName == steps[0].Name ? 1 : 0;
        return parent?.Elements().Count(e => e.Name.LocalName == steps[^1].Name) ?? 0;
    }

    public string Render() => _doc.Declaration is null ? _doc.ToString() : _doc.Declaration + "\n" + _doc;

    public IReadOnlyList<DocNode> Tree() => _doc.Root is null ? [] : [Node(_doc.Root, _doc.Root.Name.LocalName)];

    private static DocNode Node(XElement el, string path)
    {
        var children = new List<DocNode>();
        foreach (var a in el.Attributes().Where(a => !a.IsNamespaceDeclaration)) children.Add(new($"{path}/@{a.Name.LocalName}", "@" + a.Name.LocalName, a.Value, []));
        foreach (var g in el.Elements().GroupBy(e => e.Name.LocalName))
        {
            var list = g.ToList();
            for (var i = 0; i < list.Count; i++)
                children.Add(Node(list[i], list.Count > 1 ? $"{path}/{g.Key}[{i + 1}]" : $"{path}/{g.Key}"));
        }
        return new(path, el.Name.LocalName, el.HasElements ? null : el.Value.Trim(), children);
    }

    public static IReadOnlyList<string> Validate(string text)
    {
        try
        {
            var d = Parse(text);
            if (d._doc.Root?.Name.LocalName == "ClinicalDocument" && d._doc.Root.Elements().All(e => e.Name.LocalName != "recordTarget"))
                return ["A CDA ClinicalDocument needs a recordTarget (the patient)."];
            return [];
        }
        catch (MappingException ex) { return [ex.Message]; }
    }
}

/// <summary>
/// ASC X12 EDI interchange (e.g. 837 claims, 835 remittances). Separators come from the ISA segment. Paths like HL7: NM1-3, NM1[2]-3, CLM-5.1, SV1[*]-2.
/// </summary>
public sealed partial class X12Doc : IMessageDoc
{
    private readonly List<List<string>> _segments = [];
    private char _el = '*', _comp = ':', _seg = '~';
    public MessageFormat Format => MessageFormat.X12;

    public static X12Doc Parse(string text)
    {
        var d = new X12Doc();
        var t = (text ?? "").Trim();
        if (t.StartsWith("ISA", StringComparison.Ordinal) && t.Length >= 106)
        {
            d._el = t[3]; d._comp = t[104]; d._seg = t[105];
        }
        foreach (var s in t.Split(d._seg).Select(x => x.Trim('\r', '\n', ' ')).Where(x => x.Length > 0)) d._segments.Add([.. s.Split(d._el)]);
        return d;
    }

    [GeneratedRegex(@"^(?<seg>[A-Z0-9]{2,3})(\[(?<occ>\d+|\*)\])?(-(?<f>\d+)(\.(?<c>\d+))?)?$")]
    private static partial Regex PathRx();
    public static bool IsValidPath(string path) => PathRx().IsMatch(path.Trim());

    private static (string Seg, int? Occ, int F, int C) ParsePath(string path)
    {
        var m = PathRx().Match(path.Trim());
        if (!m.Success) throw new MappingException($"\"{path}\" is not an X12 path. Use e.g. NM1-3, NM1[2]-3 or CLM-5.1.");
        var occ = m.Groups["occ"].Value;
        return (m.Groups["seg"].Value, occ is "" or "*" ? null : int.Parse(occ, CultureInfo.InvariantCulture),
            m.Groups["f"].Success ? int.Parse(m.Groups["f"].Value, CultureInfo.InvariantCulture) : 0, m.Groups["c"].Success ? int.Parse(m.Groups["c"].Value, CultureInfo.InvariantCulture) : 0);
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
            var at = last >= 0 ? last + 1 : Math.Max(0, _segments.FindIndex(s => s[0] is "SE" or "GE" or "IEA") is var e and >= 0 ? e : _segments.Count);
            _segments.Insert(at, made);
            last = at;
        }
        return made;
    }

    public string Get(string path, int occurrence = 1)
    {
        var p = ParsePath(path);
        var seg = Segment(p.Seg, p.Occ ?? occurrence, false);
        if (seg is null) return "";
        if (p.F == 0) return string.Join(_el, seg);
        if (p.F >= seg.Count) return "";
        var v = seg[p.F];
        if (p.C == 0) return v.Trim();
        var comps = v.Split(_comp);
        return p.C <= comps.Length ? comps[p.C - 1].Trim() : "";
    }

    public void Set(string path, string value, int occurrence = 1)
    {
        var p = ParsePath(path);
        if (p.F == 0) throw new MappingException($"\"{path}\": choose an element (e.g. {p.Seg}-1).");
        if (p.Seg == "ISA") throw new MappingException("The ISA envelope has fixed-width elements and is not changed by mappings.");
        var clean = value.Replace(_el.ToString(), " ").Replace(_seg.ToString(), " ").Replace(_comp.ToString(), " ");
        var seg = Segment(p.Seg, p.Occ ?? occurrence, true)!;
        while (seg.Count <= p.F) seg.Add("");
        if (p.C == 0) { seg[p.F] = clean; return; }
        var comps = seg[p.F].Split(_comp).ToList();
        while (comps.Count < p.C) comps.Add("");
        comps[p.C - 1] = clean;
        seg[p.F] = string.Join(_comp, comps).TrimEnd(_comp);
    }

    public int Count(string path) => _segments.Count(s => s[0] == ParsePath(path).Seg);

    public string Render() => string.Concat(_segments.Select(s => (s[0] == "ISA" ? string.Join(_el, s) : string.Join(_el, s).TrimEnd(_el)) + _seg + "\n"));

    public IReadOnlyList<DocNode> Tree()
    {
        var seen = new Dictionary<string, int>();
        var list = new List<DocNode>();
        foreach (var seg in _segments)
        {
            var name = seg[0];
            var n = seen[name] = seen.GetValueOrDefault(name) + 1;
            var prefix = _segments.Count(s => s[0] == name) > 1 ? $"{name}[{n}]" : name;
            var els = new List<DocNode>();
            for (var f = 1; f < seg.Count; f++)
                if (seg[f].Trim().Length > 0) els.Add(new($"{prefix}-{f}", $"{name}{f:00}", seg[f].Trim(), []));
            list.Add(new(prefix, $"{prefix} {X12Names.GetValueOrDefault(name, "")}".Trim(), null, els));
        }
        return list;
    }

    private static readonly Dictionary<string, string> X12Names = new()
    {
        ["ISA"] = "Interchange header", ["GS"] = "Functional group", ["ST"] = "Transaction set header", ["BHT"] = "Beginning of hierarchical transaction",
        ["NM1"] = "Name", ["N3"] = "Address", ["N4"] = "City/state/postcode", ["REF"] = "Reference", ["HL"] = "Hierarchical level", ["SBR"] = "Subscriber",
        ["DMG"] = "Demographics", ["CLM"] = "Claim", ["DTP"] = "Date/time", ["HI"] = "Diagnosis codes", ["LX"] = "Service line number", ["SV1"] = "Professional service",
        ["SV2"] = "Institutional service", ["CLP"] = "Claim payment", ["SVC"] = "Service payment", ["SE"] = "Transaction set trailer", ["GE"] = "Group trailer", ["IEA"] = "Interchange trailer",
    };

    public static IReadOnlyList<string> Validate(string text)
    {
        var t = (text ?? "").Trim();
        var errors = new List<string>();
        if (!t.StartsWith("ISA", StringComparison.Ordinal)) errors.Add("An X12 interchange must start with an ISA segment.");
        else if (t.Length < 106) errors.Add("The ISA segment is shorter than the fixed 106 characters.");
        else
        {
            var d = Parse(t);
            if (d.Count("ST") == 0) errors.Add("There is no ST (transaction set) segment.");
            if (d.Count("ST") != d.Count("SE")) errors.Add("Every ST segment needs a matching SE trailer.");
        }
        return errors;
    }
}
