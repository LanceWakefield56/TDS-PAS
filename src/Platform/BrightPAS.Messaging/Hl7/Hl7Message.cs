using BrightPAS.Core;
using System.Globalization;
using System.Text;

namespace BrightPAS.Messaging.Hl7;

/// <summary>
/// Minimal, tolerant HL7 v2 parser for inbound messages (ORU results etc.).
/// Field numbers follow the HL7 standard: for MSH, MSH-1 is the field separator; for other segments field 1 is the first after the name.
/// </summary>
public sealed class Hl7Message
{
    public char FieldSep { get; }
    public char ComponentSep { get; }
    public char RepetitionSep { get; }
    public char EscapeChar { get; }
    public char SubcomponentSep { get; }
    public IReadOnlyList<Hl7Segment> Segments { get; }

    private Hl7Message(char f, char c, char r, char e, char s, List<Hl7Segment> segs)
    { FieldSep = f; ComponentSep = c; RepetitionSep = r; EscapeChar = e; SubcomponentSep = s; Segments = segs; }

    public static Hl7Message Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new Hl7Exception("The message is empty.");
        text = text.Trim('\v', '\x1c', '\r', '\n', ' ', '\t');
        var start = text.IndexOf("MSH", StringComparison.Ordinal);
        if (start < 0 || text.Length < start + 8) throw new Hl7Exception("This is not an HL7 v2 message (no MSH segment).");
        text = text[start..];
        var f = text[3];
        var enc = text[4..8];
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var msg = new Hl7Message(f, enc[0], enc[1], enc[2], enc[3], []);
        var segs = (List<Hl7Segment>)msg.Segments;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length < 3) continue;
            var fields = line.Split(f);
            if (fields[0] == "MSH")
            {
                // Insert MSH-1 (the separator itself) so numbering matches the standard.
                var list = new List<string> { "MSH", f.ToString() };
                list.AddRange(fields.Skip(1));
                fields = [.. list];
            }
            segs.Add(new Hl7Segment(msg, fields[0], fields));
        }
        if (segs.Count == 0 || segs[0].Name != "MSH") throw new Hl7Exception("The message must start with an MSH segment.");
        return msg;
    }

    public Hl7Segment Msh => Segments[0];
    public string MessageType => $"{Msh.Get(9, 1)}^{Msh.Get(9, 2)}";
    public string ControlId => Msh.Get(10);
    public string SendingApplication => Msh.Get(3, 1);
    public string SendingFacility => Msh.Get(4, 1);
    public string Version => Msh.Get(12, 1);

    public IEnumerable<Hl7Segment> All(string name) => Segments.Where(s => s.Name == name);
    public Hl7Segment? First(string name) => Segments.FirstOrDefault(s => s.Name == name);

    /// <summary>Decodes HL7 escape sequences (\F\ \S\ \T\ \R\ \E\ \.br\).</summary>
    public string Unescape(string v)
    {
        if (v.IndexOf(EscapeChar) < 0) return v;
        var e = EscapeChar.ToString();
        return v.Replace($"{e}F{e}", FieldSep.ToString()).Replace($"{e}S{e}", ComponentSep.ToString()).Replace($"{e}T{e}", SubcomponentSep.ToString())
            .Replace($"{e}R{e}", RepetitionSep.ToString()).Replace($"{e}.br{e}", "\n").Replace($"{e}E{e}", e);
    }

    /// <summary>Builds an ACK for this message (AA accepted, AE error, AR rejected).</summary>
    public string Ack(string code, string? text, DateTimeOffset now, string receivingApp = "BrightPAS", string receivingFacility = "BrightPAS")
    {
        var f = FieldSep;
        var sb = new StringBuilder();
        sb.Append($"MSH{f}{ComponentSep}{RepetitionSep}{EscapeChar}{SubcomponentSep}{f}{receivingApp}{f}{receivingFacility}{f}{Msh.Get(3)}{f}{Msh.Get(4)}{f}")
          .Append(now.ToString("yyyyMMddHHmmsszzz", CultureInfo.InvariantCulture).Replace(":", "")).Append($"{f}{f}ACK^{Msh.Get(9, 2)}^ACK{f}")
          .Append($"ACK{now:yyyyMMddHHmmssfff}{f}{Msh.Get(11)}{f}{(string.IsNullOrEmpty(Version) ? "2.4" : Version)}\r");
        sb.Append($"MSA{f}{code}{f}{ControlId}");
        if (!string.IsNullOrEmpty(text)) sb.Append(f).Append(Clean(text));
        sb.Append('\r');
        return sb.ToString();
    }

    private string Clean(string t) => new(t.Select(c => c == FieldSep || c == ComponentSep || c == RepetitionSep || c == EscapeChar || c == SubcomponentSep || char.IsControl(c) ? ' ' : c).Take(200).ToArray());

    public static DateTimeOffset? ParseTimestamp(string v, TimeZoneInfo defaultZone)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        var offsetAt = v.IndexOfAny(['+', '-'], 8);
        TimeSpan? offset = null;
        if (offsetAt > 0 && v.Length - offsetAt == 5 && int.TryParse(v[(offsetAt + 1)..(offsetAt + 3)], out var oh) && int.TryParse(v[(offsetAt + 3)..], out var om))
        {
            offset = new TimeSpan(oh, om, 0) * (v[offsetAt] == '-' ? -1 : 1);
            v = v[..offsetAt];
        }
        var dot = v.IndexOf('.');
        if (dot > 0) v = v[..dot];
        string[] formats = ["yyyyMMddHHmmss", "yyyyMMddHHmm", "yyyyMMddHH", "yyyyMMdd"];
        if (!DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return null;
        if (offset is { } o) return new DateTimeOffset(dt, o);
        // No offset in the message: the sender's wall-clock time (daylight saving gaps and repeats resolved by NzTime for NZ senders).
        return defaultZone == NzTime.Zone || defaultZone.Id == NzTime.Zone.Id ? NzTime.FromLocal(dt) : new DateTimeOffset(dt, defaultZone.GetUtcOffset(dt));
    }
}

public sealed class Hl7Segment(Hl7Message message, string name, string[] fields)
{
    public string Name { get; } = name;
    public int FieldCount => fields.Length - 1;

    /// <summary>Raw field (all repetitions).</summary>
    public string Raw(int field) => field < fields.Length ? fields[field] : "";

    /// <summary>Field / component / subcomponent of the first repetition, unescaped. Components are 1-based; 0 = whole field.</summary>
    public string Get(int field, int component = 0, int sub = 0, int repetition = 0)
    {
        var f = Raw(field);
        if (Name == "MSH" && field <= 2) return f;
        var reps = f.Split(message.RepetitionSep);
        if (repetition >= reps.Length) return "";
        var v = reps[repetition];
        if (component > 0)
        {
            var comps = v.Split(message.ComponentSep);
            v = component <= comps.Length ? comps[component - 1] : "";
            if (sub > 0)
            {
                var subs = v.Split(message.SubcomponentSep);
                v = sub <= subs.Length ? subs[sub - 1] : "";
            }
        }
        return message.Unescape(v).Trim();
    }

    public int Repetitions(int field) => Raw(field).Length == 0 ? 0 : Raw(field).Split(message.RepetitionSep).Length;
}

public sealed class Hl7Exception(string message) : Exception(message);

/// <summary>MLLP framing: &lt;VT&gt; message &lt;FS&gt;&lt;CR&gt;.</summary>
public static class Mllp
{
    public const byte StartBlock = 0x0B;
    public const byte EndBlock = 0x1C;
    public const byte CarriageReturn = 0x0D;

    public static byte[] Frame(string message)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var buf = new byte[body.Length + 3];
        buf[0] = StartBlock;
        body.CopyTo(buf, 1);
        buf[^2] = EndBlock;
        buf[^1] = CarriageReturn;
        return buf;
    }

    /// <summary>Reads one framed message from the stream. Returns null at end of stream. Throws if a message exceeds maxBytes.</summary>
    public static async Task<string?> ReadAsync(Stream s, int maxBytes, CancellationToken ct)
    {
        var one = new byte[1];
        int b;
        do
        {
            if (await s.ReadAsync(one, ct) == 0) return null;
            b = one[0];
        } while (b != StartBlock);
        using var ms = new MemoryStream();
        while (true)
        {
            if (await s.ReadAsync(one, ct) == 0) return null;
            if (one[0] == EndBlock)
            {
                _ = await s.ReadAsync(one, ct); // trailing CR (optional)
                return Encoding.UTF8.GetString(ms.ToArray());
            }
            ms.WriteByte(one[0]);
            if (ms.Length > maxBytes) throw new Hl7Exception($"Message larger than {maxBytes} bytes.");
        }
    }
}
