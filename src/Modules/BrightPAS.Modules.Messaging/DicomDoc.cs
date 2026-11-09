using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FellowOakDicom;
using FellowOakDicom.Serialization;

namespace BrightPAS.Modules.Messaging;

/// <summary>
/// DICOM header as the DICOM JSON model (PS3.18 F). Paths use keywords or tags: PatientName, (0010,0010), 00100010,
/// RequestAttributesSequence[0].RequestedProcedureID or ScheduledProcedureStepSequence[*].Modality. Pixel data is never kept here.
/// </summary>
public sealed partial class DicomDoc : IMessageDoc
{
    private readonly JsonObject _root;
    public MessageFormat Format => MessageFormat.Dicom;
    private DicomDoc(JsonObject root) => _root = root;

    public static DicomDoc Parse(string json)
    {
        try { return new(JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json) as JsonObject ?? throw new MappingException("A DICOM message must be a DICOM JSON object.")); }
        catch (JsonException ex) { throw new MappingException($"The DICOM JSON could not be read: {ex.Message}"); }
    }

    [GeneratedRegex(@"^(?<k>[A-Za-z][A-Za-z0-9]*|\([0-9A-Fa-f]{4},[0-9A-Fa-f]{4}\)|[0-9A-Fa-f]{8})(\[(?<i>\d+|\*)\])?$")]
    private static partial Regex PartRx();

    public static bool IsValidPath(string path) => path.Length > 0 && path.Split('.').All(p => PartRx().IsMatch(p) && Tag(PartRx().Match(p).Groups["k"].Value) is not null);

    private static readonly Lazy<Dictionary<string, DicomDictionaryEntry>> ByKeyword = new(() =>
    {
        var d = new Dictionary<string, DicomDictionaryEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in DicomDictionary.Default) if (!string.IsNullOrEmpty(e.Keyword)) d.TryAdd(e.Keyword, e);
        return d;
    });

    /// <summary>"00100010" for a keyword or tag text, or null if unknown.</summary>
    public static string? Tag(string k)
    {
        k = k.Trim();
        if (Regex.IsMatch(k, @"^\([0-9A-Fa-f]{4},[0-9A-Fa-f]{4}\)$")) return (k[1..5] + k[6..10]).ToUpperInvariant();
        if (Regex.IsMatch(k, "^[0-9A-Fa-f]{8}$")) return k.ToUpperInvariant();
        return ByKeyword.Value.TryGetValue(k, out var e) ? $"{e.Tag.Group:X4}{e.Tag.Element:X4}" : null;
    }

    public static (string Keyword, string Name, string Vr) Describe(string tag)
    {
        var t = new DicomTag(ushort.Parse(tag[..4], NumberStyles.HexNumber), ushort.Parse(tag[4..], NumberStyles.HexNumber));
        var e = DicomDictionary.Default[t];
        return (e?.Keyword ?? "", e?.Name ?? "Private or unknown", e?.ValueRepresentations.FirstOrDefault()?.Code ?? "UN");
    }

    private static List<(string Tag, int? Index, bool Star)> Parts(string path)
    {
        if (!IsValidPath(path.Trim())) throw new MappingException($"\"{path}\" is not a DICOM path. Use a keyword or tag, e.g. PatientName, (0008,0050) or RequestAttributesSequence[0].RequestedProcedureID.");
        return path.Trim().Split('.').Select(p =>
        {
            var m = PartRx().Match(p);
            var i = m.Groups["i"].Value;
            return (Tag(m.Groups["k"].Value)!, i is "" or "*" ? (int?)null : int.Parse(i, CultureInfo.InvariantCulture), i == "*");
        }).ToList();
    }

    public string Get(string path, int occurrence = 1)
    {
        JsonObject? obj = _root;
        var parts = Parts(path);
        for (var p = 0; p < parts.Count; p++)
        {
            var (tag, index, star) = parts[p];
            if (obj?[tag] is not JsonObject el) return "";
            if (p == parts.Count - 1 && index is null && !star) return ValueOf(el);
            var items = el["Value"] as JsonArray;
            var k = index ?? (star ? occurrence - 1 : 0);
            if (items is null || k < 0 || k >= items.Count) return "";
            if (p == parts.Count - 1) return items[k] is JsonObject ? "" : items[k]?.ToString() ?? "";
            obj = items[k] as JsonObject;
        }
        return "";
    }

    private static string ValueOf(JsonObject el)
    {
        if (el["InlineBinary"] is not null || el["BulkDataURI"] is not null) return "[binary data]";
        if (el["Value"] is not JsonArray v || v.Count == 0) return "";
        return string.Join('\\', v.Select(x => x switch
        {
            JsonObject pn when pn["Alphabetic"] is { } a => a.GetValue<string>(),
            JsonObject => "",
            JsonValue jv when jv.TryGetValue<string>(out var s) => s,
            null => "",
            _ => x.ToJsonString(),
        }));
    }

    public void Set(string path, string value, int occurrence = 1)
    {
        var parts = Parts(path);
        var obj = _root;
        for (var p = 0; p < parts.Count; p++)
        {
            var (tag, index, star) = parts[p];
            var last = p == parts.Count - 1;
            if (last && index is null && !star) { obj[tag] = Element(tag, value); return; }
            if (obj[tag] is not JsonObject el || el["vr"]?.GetValue<string>() != "SQ")
            {
                if (Describe(tag).Vr != "SQ") throw new MappingException($"{Describe(tag).Keyword} is not a sequence, so it cannot have [{index?.ToString(CultureInfo.InvariantCulture) ?? "*"}].");
                el = new JsonObject { ["vr"] = "SQ", ["Value"] = new JsonArray() };
                obj[tag] = el;
            }
            if (el["Value"] is not JsonArray items) { items = []; el["Value"] = items; }
            var k = index ?? (star ? occurrence - 1 : 0);
            while (items.Count <= k) items.Add(new JsonObject());
            if (last) throw new MappingException("Choose an attribute inside the sequence item (e.g. …Sequence[0].Modality).");
            obj = items[k] as JsonObject ?? throw new MappingException("Sequence item is not an object.");
        }
    }

    private static readonly HashSet<string> Numeric = ["IS", "DS", "US", "UL", "SS", "SL", "FL", "FD", "UV", "SV"];

    private static JsonObject Element(string tag, string value)
    {
        var vr = Describe(tag).Vr;
        var el = new JsonObject { ["vr"] = vr };
        if (value.Length == 0) return el;
        var values = new JsonArray();
        foreach (var v in value.Split('\\'))
        {
            if (vr == "PN") values.Add(new JsonObject { ["Alphabetic"] = v });
            else if (Numeric.Contains(vr) && decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                values.Add(vr is "IS" or "DS" ? JsonValue.Create(v) : JsonValue.Create(d));
            else values.Add(v);
        }
        el["Value"] = values;
        return el;
    }

    public int Count(string path)
    {
        var star = path.IndexOf("[*]", StringComparison.Ordinal);
        if (star < 0) return 1;
        var parts = Parts(path[..star] + "[0]");
        JsonObject? obj = _root;
        for (var p = 0; p < parts.Count; p++)
        {
            var (tag, index, _) = parts[p];
            if (obj?[tag] is not JsonObject el || el["Value"] is not JsonArray items) return 0;
            if (p == parts.Count - 1) return items.Count;
            obj = index is { } k && k < items.Count ? items[k] as JsonObject : null;
        }
        return 0;
    }

    public string Render()
    {
        var sorted = new JsonObject();
        foreach (var (k, v) in _root.OrderBy(x => x.Key, StringComparer.Ordinal)) sorted[k] = v?.DeepClone();
        return sorted.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public IReadOnlyList<DocNode> Tree() => Nodes(_root, "");

    private static IReadOnlyList<DocNode> Nodes(JsonObject obj, string prefix)
    {
        var list = new List<DocNode>();
        foreach (var (tag, node) in obj.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (node is not JsonObject el || tag.Length != 8) continue;
            var (kw, name, _) = Describe(tag);
            var key = kw.Length > 0 ? kw : $"({tag[..4]},{tag[4..]})";
            var path = prefix.Length == 0 ? key : $"{prefix}.{key}";
            var label = $"({tag[..4]},{tag[4..]}) {(kw.Length > 0 ? kw : name)}";
            if (el["vr"]?.GetValue<string>() == "SQ" && el["Value"] is JsonArray items)
                list.Add(new(path, label, null, items.Select((it, i) => new DocNode($"{path}[{i}]", $"Item {i}", null, it is JsonObject o ? Nodes(o, $"{path}[{i}]") : [])).ToList()));
            else list.Add(new(path, label, ValueOf(el), []));
        }
        return list;
    }

    public static IReadOnlyList<string> Validate(string json)
    {
        var errors = new List<string>();
        JsonObject? o;
        try { o = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException ex) { return [$"This is not valid DICOM JSON: {ex.Message}"]; }
        if (o is null || o.Count == 0) return ["The DICOM message has no attributes."];
        foreach (var (k, v) in o)
        {
            if (!Regex.IsMatch(k, "^[0-9A-F]{8}$")) errors.Add($"\"{k}\" is not a DICOM tag (8 hexadecimal digits, e.g. 00100010).");
            else if (v is not JsonObject el || el["vr"] is null) errors.Add($"({k[..4]},{k[4..]}) has no value representation (vr).");
        }
        return errors;
    }

    /// <summary>Common attributes offered in the designer's target picker.</summary>
    public static IReadOnlyList<object> Catalogue() => new[]
    {
        "PatientName", "PatientID", "IssuerOfPatientID", "PatientBirthDate", "PatientSex", "OtherPatientIDsSequence", "AccessionNumber", "StudyInstanceUID", "SeriesInstanceUID",
        "SOPInstanceUID", "SOPClassUID", "StudyDate", "StudyTime", "StudyDescription", "SeriesDescription", "Modality", "ReferringPhysicianName",
        "PerformingPhysicianName", "RequestedProcedureID", "RequestedProcedureDescription", "ScheduledProcedureStepSequence", "RequestAttributesSequence",
        "PerformedProcedureStepStartDate", "PerformedProcedureStepStartTime", "PerformedProcedureStepStatus", "InstitutionName", "StationName", "BodyPartExamined",
    }.Select(k => (object)new { path = k, tag = Tag(k) is { } t ? $"({t[..4]},{t[4..]})" : "", name = ByKeyword.Value.TryGetValue(k, out var e) ? e.Name : k }).ToList();
}

/// <summary>Converts between DICOM Part 10 files and the stored DICOM JSON (pixel data and other bulk data are dropped).</summary>
public static class DicomConvert
{
    public static string ToJson(byte[] part10)
    {
        DicomFile file;
        try { file = DicomFile.Open(new MemoryStream(part10)); }
        catch (Exception ex) when (ex is DicomFileException or DicomDataException or EndOfStreamException or InvalidOperationException)
        { throw new MappingException($"This is not a readable DICOM file: {ex.Message}"); }
        return ToJson(file.Dataset);
    }

    public static string ToJson(DicomDataset dataset)
    {
        var copy = dataset.Clone();
        foreach (var t in copy.Where(i => i.Tag == DicomTag.PixelData || i.ValueRepresentation == DicomVR.OB || i.ValueRepresentation == DicomVR.OW
                     || i.ValueRepresentation == DicomVR.OF || i.ValueRepresentation == DicomVR.UN || i.Tag.IsPrivate).Select(i => i.Tag).ToList())
            copy.Remove(t);
        var json = DicomJson.ConvertDicomToJson(copy);
        return DicomDoc.Parse(json).Render();
    }

    /// <summary>A DICOM Part 10 file from DICOM JSON (adds SOP class/instance UIDs if they are missing).</summary>
    public static byte[] ToFile(string json)
    {
        DicomDataset ds;
        try { ds = DicomJson.ConvertJsonToDicom(json); }
        catch (Exception ex) when (ex is JsonException or DicomDataException or FormatException or InvalidOperationException)
        { throw new MappingException($"The DICOM JSON could not be turned into a DICOM file: {ex.Message}"); }
        if (!ds.Contains(DicomTag.SOPClassUID)) ds.AddOrUpdate(DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage);
        if (!ds.Contains(DicomTag.SOPInstanceUID)) ds.AddOrUpdate(DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID());
        using var ms = new MemoryStream();
        new DicomFile(ds).Save(ms);
        return ms.ToArray();
    }
}
