using System.Text;

namespace BrightPAS.Modules.Devices;

/// <summary>A one-page text PDF (demonstration data and tests stand in for SaferSleep's own PDF).</summary>
public static class SimplePdf
{
    public static byte[] Make(IEnumerable<string> lines)
    {
        static string Esc(string s) => new string(s.Select(c => c < 32 || c > 126 ? '?' : c).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var text = new StringBuilder("BT /F1 11 Tf 50 790 Td 14 TL\n");
        foreach (var l in lines.Take(50)) text.Append('(').Append(Esc(l)).Append(") Tj T*\n");
        text.Append("ET");
        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(text.ToString())} >>\nstream\n{text}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objs.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString())); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>A SaferSleep-style anaesthetic record ORU^R01 (BrightPAS interface profile) – used by demonstration data, tests and the robot's virtual SaferSleep.</summary>
    public static string SaferSleepOru(string controlId, string caseNumber, string? visitNumber, string nhi, string family, string given, DateTimeOffset start, bool correction,
        IReadOnlyList<(int Minute, string Name, string Dose, string Unit, string Route)> drugs, string? infusion = null)
    {
        static string Ts(DateTimeOffset t) => t.ToString("yyyyMMddHHmmsszzz", System.Globalization.CultureInfo.InvariantCulture).Replace(":", "");
        var end = start.AddMinutes(55);
        var sb = new StringBuilder();
        sb.Append($"MSH|^~\\&|SAFERSLEEP|TDS|BrightPAS|TDS|{Ts(end.AddMinutes(10))}||ORU^R01|{controlId}|P|2.4\r");
        sb.Append($"PID|1||{nhi}^^^NZLMOH^NHI||{family}^{given}\r");
        sb.Append($"PV1|1|I|THEATRE 1|||||||||||||||||{visitNumber}\r");
        sb.Append($"OBR|1|{visitNumber}|{caseNumber}|ANREC^Anaesthetic record^L|||{Ts(start)}|{Ts(end)}|||||||||||||||||{(correction ? "C" : "F")}|||||||ANAES01&Lee&Sarah\r");
        var n = 1;
        void Obx(string type, string id, string value, string unit = "", DateTimeOffset? at = null, string method = "") =>
            sb.Append($"OBX|{n++}|{type}|{id}||{value}|{unit}|||||F|||{(at is { } a ? Ts(a) : "")}|||{method}\r");
        Obx("ST", "ASA^ASA status^L", "2");
        Obx("ST", "TECH^Technique^L", "General anaesthesia – TIVA");
        Obx("ST", "AIRWAY^Airway^L", "LMA size 4, easy");
        foreach (var d in drugs) Obx("NM", $"DRUG^{d.Name}^SSDRUG", d.Dose, d.Unit, start.AddMinutes(d.Minute), d.Route);
        if (infusion is not null) Obx("ST", "DRUGINF^Propofol infusion^SSDRUG", infusion, "", start.AddMinutes(4), "IV");
        Obx("ST", "EVENT^Event^L", "Induction", "", start.AddMinutes(3));
        Obx("ST", "EVENT^Event^L", "LMA inserted", "", start.AddMinutes(5));
        Obx("ST", "EVENT^Event^L", "End of anaesthesia", "", end);
        var rnd = new Random(caseNumber.GetHashCode());
        for (var m = 0; m <= 55; m += 5)
        {
            Obx("NM", "147842^MDC_ECG_HEART_RATE^MDC", (68 + rnd.Next(-6, 8)).ToString(), "/min", start.AddMinutes(m));
            Obx("NM", "150456^MDC_PULS_OXIM_SAT_O2^MDC", (97 + rnd.Next(-1, 3)).ToString(), "%", start.AddMinutes(m));
            Obx("NM", "150021^MDC_PRESS_BLD_NONINV_SYS^MDC", (118 + rnd.Next(-12, 10)).ToString(), "mmHg", start.AddMinutes(m));
        }
        if (correction) Obx("TX", "COMMENT^Comments^L", "Corrected: ondansetron dose amended.");
        var pdf = Make(["SaferSleep anaesthetic record (demonstration)", $"Case {caseNumber}{(correction ? " (corrected)" : "")}", $"Patient {family}, {given}  NHI {nhi}", $"Start {start:dd MMM yyyy HH:mm}", "",
            .. drugs.Select(d => $"{start.AddMinutes(d.Minute):HH:mm}  {d.Name} {d.Dose} {d.Unit} {d.Route}")]);
        Obx("ED", "PDF^Anaesthetic record^L", $"^AP^PDF^Base64^{Convert.ToBase64String(pdf)}");
        return sb.ToString();
    }
}
