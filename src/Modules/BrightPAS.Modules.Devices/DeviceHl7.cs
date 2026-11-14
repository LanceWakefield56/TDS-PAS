using System.Globalization;
using System.Text;
using BrightPAS.Messaging.Hl7;

namespace BrightPAS.Modules.Devices;

/// <summary>One parsed measurement: BrightPAS code (HR, SpO2 …), value, unit, time.</summary>
public sealed record Measurement(string Code, decimal Value, string? Unit, DateTimeOffset At);

/// <summary>A parsed monitor message: which monitor (keys to try), the NHI it carries (if any) and the measurements.</summary>
public sealed record MonitorMessage(IReadOnlyList<string> DeviceKeys, string? Nhi, IReadOnlyList<Measurement> Values);

public sealed record SaferSleepDrug(DateTimeOffset At, string Name, decimal? Dose, string? Unit, string? Route, string? Rate, string? Code);
public sealed record SaferSleepRecord(string ControlId, string CaseNumber, string? VisitNumber, string? Nhi, bool IsCorrection, string? Anaesthetist, DateTimeOffset? Start, DateTimeOffset? End,
    string? Asa, string? Technique, string? Airway, string? Comments, IReadOnlyList<SaferSleepDrug> Drugs, IReadOnlyList<(DateTimeOffset At, string Text)> Events,
    IReadOnlyList<Measurement> Vitals, byte[]? Pdf);

/// <summary>Data for an ADT message to SaferSleep.</summary>
public sealed record AdtData(Guid EpisodeId, string Nhi, string Mrn, string FamilyName, string GivenNames, DateOnly DateOfBirth, string Sex, string? Address, string? City, string? Postcode,
    string Location, string? Surgeon, string? Anaesthetist, string Procedure, DateTimeOffset? PlannedStart, DateTimeOffset? AdmittedAt, DateTimeOffset? DischargedAt,
    IReadOnlyList<string> Allergies);

/// <summary>
/// HL7 v2 for devices. Monitor results follow the IHE PCD-01 (DEC) style the GE CARESCAPE Gateway sends: ORU^R01, PV1-3 bed, OBX-3 IEEE 11073 MDC
/// codes, OBX-18 equipment id. Plain labels (HR, SPO2, NIBP_SYS …) are accepted too. The exact GE output (MDC or MDIL codes, units, interval)
/// must be checked against TDS's gateway configuration with GE before go-live.
/// </summary>
public static class DeviceHl7
{
    // MDC code → BrightPAS code. Labels (component 2, or plain codes) are matched through Aliases.
    private static readonly Dictionary<string, string> Mdc = new()
    {
        ["147842"] = "HR",
        ["149530"] = "PR",
        ["149522"] = "PR",
        ["150456"] = "SpO2",
        ["150021"] = "NIBP-S",
        ["150022"] = "NIBP-D",
        ["150023"] = "NIBP-M",
        ["150017"] = "ART-S",
        ["150033"] = "ART-S",
        ["150034"] = "ART-D",
        ["150035"] = "ART-M",
        ["151562"] = "RR",
        ["151594"] = "RR",
        ["150344"] = "Temp",
        ["150364"] = "Temp",
        ["150392"] = "Temp",
        ["151708"] = "EtCO2",
        ["152196"] = "RR",
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MDC_ECG_HEART_RATE"] = "HR",
        ["HR"] = "HR",
        ["HEART RATE"] = "HR",
        ["MDC_PULS_OXIM_PULS_RATE"] = "PR",
        ["PR"] = "PR",
        ["PULSE"] = "PR",
        ["MDC_PULS_OXIM_SAT_O2"] = "SpO2",
        ["SPO2"] = "SpO2",
        ["SPO2%"] = "SpO2",
        ["MDC_PRESS_BLD_NONINV_SYS"] = "NIBP-S",
        ["NIBP_SYS"] = "NIBP-S",
        ["NBP-S"] = "NIBP-S",
        ["NIBP-S"] = "NIBP-S",
        ["MDC_PRESS_BLD_NONINV_DIA"] = "NIBP-D",
        ["NIBP_DIA"] = "NIBP-D",
        ["NBP-D"] = "NIBP-D",
        ["NIBP-D"] = "NIBP-D",
        ["MDC_PRESS_BLD_NONINV_MEAN"] = "NIBP-M",
        ["NIBP_MEAN"] = "NIBP-M",
        ["NBP-M"] = "NIBP-M",
        ["NIBP-M"] = "NIBP-M",
        ["MDC_PRESS_BLD_ART_ABP_SYS"] = "ART-S",
        ["MDC_PRESS_BLD_ART_SYS"] = "ART-S",
        ["ART-S"] = "ART-S",
        ["MDC_PRESS_BLD_ART_ABP_DIA"] = "ART-D",
        ["MDC_PRESS_BLD_ART_DIA"] = "ART-D",
        ["ART-D"] = "ART-D",
        ["MDC_PRESS_BLD_ART_ABP_MEAN"] = "ART-M",
        ["MDC_PRESS_BLD_ART_MEAN"] = "ART-M",
        ["ART-M"] = "ART-M",
        ["MDC_RESP_RATE"] = "RR",
        ["MDC_AWAY_RESP_RATE"] = "RR",
        ["MDC_CO2_RESP_RATE"] = "RR",
        ["RR"] = "RR",
        ["RESP"] = "RR",
        ["MDC_TEMP"] = "Temp",
        ["MDC_TEMP_BODY"] = "Temp",
        ["TEMP"] = "Temp",
        ["T1"] = "Temp",
        ["MDC_AWAY_CO2_ET"] = "EtCO2",
        ["ETCO2"] = "EtCO2",
        ["CO2-ET"] = "EtCO2",
    };

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["HR"] = "Heart rate",
        ["PR"] = "Pulse rate (SpO2)",
        ["SpO2"] = "SpO2",
        ["NIBP-S"] = "NIBP systolic",
        ["NIBP-D"] = "NIBP diastolic",
        ["NIBP-M"] = "NIBP mean",
        ["ART-S"] = "Arterial systolic",
        ["ART-D"] = "Arterial diastolic",
        ["ART-M"] = "Arterial mean",
        ["RR"] = "Respiratory rate",
        ["Temp"] = "Temperature",
        ["EtCO2"] = "End-tidal CO2",
    };

    /// <summary>Maps an OBX-3 to a BrightPAS code, or null when it is not a vital sign BrightPAS keeps.</summary>
    public static string? Code(string id, string text)
    {
        if (Mdc.TryGetValue(id, out var c)) return c;
        if (Aliases.TryGetValue(id, out c)) return c;
        if (Aliases.TryGetValue(text, out c)) return c;
        return null;
    }

    public static string? Nhi(Hl7Message m)
    {
        var pid = m.First("PID");
        if (pid is null) return null;
        for (var r = 0; r < Math.Max(1, pid.Repetitions(3)); r++)
        {
            var id = pid.Get(3, 1, 0, r).ToUpperInvariant();
            var type = pid.Get(3, 5, 0, r);
            if (id.Length == 7 && (type is "" or "NHI" || pid.Get(3, 4, 0, r).Contains("NHI", StringComparison.OrdinalIgnoreCase) || pid.Get(3, 4, 0, r).Contains("MOH", StringComparison.OrdinalIgnoreCase))
                && char.IsLetter(id[0]) && char.IsLetter(id[1]) && char.IsLetter(id[2])) return id;
        }
        return null;
    }

    private static decimal? Number(string v) => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Key(string v) => string.Join('/', v.Split('^').Select(p => p.Trim()).Where(p => p.Length > 0)).ToUpperInvariant();

    /// <summary>Parses a monitor ORU^R01. Throws Hl7Exception when it is not one.</summary>
    public static MonitorMessage ParseMonitor(string raw, TimeZoneInfo zone, DateTimeOffset now)
    {
        var m = Hl7Message.Parse(raw);
        if (m.Msh.Get(9, 1) != "ORU") throw new Hl7Exception($"Expected ORU^R01 from the monitor gateway, got {m.MessageType}.");
        var keys = new List<string>();
        void AddKey(string k) { k = Key(k); if (k.Length > 0 && !keys.Contains(k)) keys.Add(k); }
        if (m.First("PV1") is { } pv1) AddKey(pv1.Raw(3).Split(m.RepetitionSep)[0].Replace(m.ComponentSep, '^'));
        var obr = m.First("OBR");
        var obrTime = obr is null ? null : Hl7Message.ParseTimestamp(obr.Get(7), zone);
        var values = new List<Measurement>();
        foreach (var obx in m.All("OBX"))
        {
            AddKey(obx.Get(18, 1));
            if (obx.Get(2) is not ("NM" or "SN")) continue;
            var status = obx.Get(11);
            if (status is "X" or "D" or "W") continue;
            var code = Code(obx.Get(3, 1), obx.Get(3, 2));
            if (code is null) continue;
            var raw5 = obx.Get(2) == "SN" ? obx.Get(5, 2) : obx.Get(5);
            if (Number(raw5) is not { } v) continue;
            var u1 = obx.Get(6, 1); var u2 = obx.Get(6, 2);
            var unit = u2.StartsWith("MDC_DIM_", StringComparison.Ordinal) ? Unit(u2) : u1.Length > 0 && !u1.All(char.IsDigit) ? u1 : u2.Length > 0 ? u2 : null;
            var at = Hl7Message.ParseTimestamp(obx.Get(14), zone) ?? obrTime ?? now;
            (v, unit) = Normalise(code, v, unit);
            values.Add(new(code, v, unit, at));
        }
        return new(keys, Nhi(m), values);
    }

    private static string? Unit(string mdcDim) => mdcDim switch
    {
        "MDC_DIM_BEAT_PER_MIN" => "/min",
        "MDC_DIM_RESP_PER_MIN" => "/min",
        "MDC_DIM_PERCENT" => "%",
        "MDC_DIM_MMHG" => "mmHg",
        "MDC_DIM_KILO_PASCAL" => "kPa",
        "MDC_DIM_DEGC" => "°C",
        "MDC_DIM_FAHR" => "°F",
        "" => null,
        _ => mdcDim.Replace("MDC_DIM_", "", StringComparison.Ordinal).ToLowerInvariant(),
    };

    /// <summary>Temperature to °C, end-tidal CO2 to mmHg.</summary>
    public static (decimal Value, string? Unit) Normalise(string code, decimal v, string? unit)
    {
        var u = unit?.Trim() ?? "";
        if (code == "Temp" && (u.Contains('F') || u.Equals("degF", StringComparison.OrdinalIgnoreCase))) return (Math.Round((v - 32) * 5 / 9, 1), "°C");
        if (code == "Temp") return (v, "°C");
        if (code == "EtCO2" && u.Equals("kPa", StringComparison.OrdinalIgnoreCase)) return (Math.Round(v * 7.50062m, 1), "mmHg");
        if (code == "EtCO2") return (v, "mmHg");
        if (code.Contains('-') || code.EndsWith("BP", StringComparison.Ordinal)) return (v, "mmHg");
        if (code == "SpO2") return (v, "%");
        return (v, "/min");
    }

    /// <summary>Plausibility limits: values outside are treated as artefact and not stored.</summary>
    public static bool Plausible(string code, decimal v) => code switch
    {
        "HR" or "PR" => v is >= 10 and <= 300,
        "SpO2" => v is >= 40 and <= 100,
        "RR" => v is >= 0 and <= 80,
        "Temp" => v is >= 25 and <= 45,
        "EtCO2" => v is >= 0 and <= 120,
        _ when code.EndsWith("-S", StringComparison.Ordinal) => v is >= 30 and <= 300,
        _ when code.EndsWith("-D", StringComparison.Ordinal) => v is >= 10 and <= 200,
        _ when code.EndsWith("-M", StringComparison.Ordinal) => v is >= 15 and <= 250,
        _ => true,
    };

    // ------------------------------------------------------------------ SaferSleep
    /// <summary>
    /// Parses the anaesthetic record ORU^R01 from SaferSleep (BrightPAS interface profile, to be confirmed with the vendor):
    /// OBR-3 case number, OBR-7/8 start/end, OBR-25 F (final) or C (correction), OBR-32 anaesthetist; PV1-19 the visit number BrightPAS sent;
    /// OBX ASA / TECH / AIRWAY / COMMENT (ST/TX); OBX DRUG (NM dose, OBX-6 unit, OBX-14 time, OBX-17 route, OBX-3.4 drug code);
    /// OBX DRUGINF (ST rate); OBX EVENT (ST, OBX-14 time); vital signs as MDC-coded NM; OBX ED the PDF (Base64).
    /// </summary>
    public static SaferSleepRecord ParseSaferSleep(string raw, TimeZoneInfo zone)
    {
        var m = Hl7Message.Parse(raw);
        if (m.Msh.Get(9, 1) != "ORU") throw new Hl7Exception($"Expected ORU^R01 (anaesthetic record), got {m.MessageType}.");
        var obr = m.First("OBR") ?? throw new Hl7Exception("No OBR segment (anaesthetic record header).");
        var caseNo = obr.Get(3, 1) is { Length: > 0 } c ? c : obr.Get(2, 1);
        if (caseNo.Length == 0) throw new Hl7Exception("No case number (OBR-3).");
        var visit = m.First("PV1")?.Get(19, 1) is { Length: > 0 } v ? v : obr.Get(2, 1) is { Length: > 0 } p ? p : null;
        // OBR-32 principal result interpreter (NDL): ID&Family&Given in component 1.
        var anaes = obr.Get(32, 1, 2) is { Length: > 0 } fam ? $"Dr {obr.Get(32, 1, 3)} {fam}".Replace("  ", " ").Trim() : obr.Get(32, 1) is { Length: > 0 } a ? a : null;
        string? asa = null, tech = null, airway = null, comments = null; byte[]? pdf = null;
        var drugs = new List<SaferSleepDrug>(); var events = new List<(DateTimeOffset, string)>(); var vitals = new List<Measurement>();
        var start = Hl7Message.ParseTimestamp(obr.Get(7), zone);
        foreach (var obx in m.All("OBX"))
        {
            var id = obx.Get(3, 1).ToUpperInvariant();
            var at = Hl7Message.ParseTimestamp(obx.Get(14), zone) ?? start ?? DateTimeOffset.MinValue;
            switch (id)
            {
                case "ASA": asa = Clip(obx.Get(5), 10); break;
                case "TECH": tech = Clip(obx.Get(5), 100); break;
                case "AIRWAY": airway = Clip(obx.Get(5), 100); break;
                case "COMMENT":
                    var text = string.Join("\n", Enumerable.Range(0, Math.Max(1, obx.Repetitions(5))).Select(r => obx.Get(5, 0, 0, r)));
                    comments = Clip(comments is null ? text : comments + "\n" + text, 2000); break;
                case "EVENT": if (obx.Get(5) is { Length: > 0 } ev) events.Add((at, Clip(ev, 300)!)); break;
                case "DRUG":
                case "DRUGINF":
                    var name = obx.Get(3, 2);
                    if (name.Length == 0) break;
                    var isInf = id == "DRUGINF";
                    drugs.Add(new(at, Clip(name, 150)!, isInf ? null : Number(obx.Get(5)), isInf ? null : Clip(obx.Get(6, 1), 20), Clip(obx.Get(17, 1), 30), isInf ? Clip(obx.Get(5), 60) : null, Clip(obx.Get(3, 4), 60)));
                    break;
                case "PDF":
                    if (obx.Get(2) == "ED")
                    {
                        var data = obx.Get(5, 5);
                        if (obx.Get(5, 4).Equals("Base64", StringComparison.OrdinalIgnoreCase) && data.Length > 0)
                        {
                            try { pdf = Convert.FromBase64String(data); } catch (FormatException) { throw new Hl7Exception("The PDF (OBX ED) is not valid Base64."); }
                            if (pdf.Length < 5 || Encoding.ASCII.GetString(pdf, 0, 5) != "%PDF-") throw new Hl7Exception("The attached document is not a PDF.");
                        }
                    }
                    break;
                default:
                    if (obx.Get(2) == "NM" && Code(obx.Get(3, 1), obx.Get(3, 2)) is { } code && Number(obx.Get(5)) is { } val)
                    {
                        var (nv, nu) = Normalise(code, val, obx.Get(6, 1));
                        if (Plausible(code, nv)) vitals.Add(new(code, nv, nu, at));
                    }
                    break;
            }
        }
        return new(m.ControlId, caseNo, visit, Nhi(m), obr.Get(25) == "C", Clip(anaes, 100), start, Hl7Message.ParseTimestamp(obr.Get(8), zone), asa, tech, airway, comments, drugs, events, vitals, pdf);
    }

    private static string? Clip(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max];

    /// <summary>HL7 escaping for values we write.</summary>
    public static string Esc(string? v) => (v ?? "").Replace("\\", "\\E\\").Replace("|", "\\F\\").Replace("^", "\\S\\").Replace("&", "\\T\\").Replace("~", "\\R\\").Replace("\r", " ").Replace("\n", " ");

    private static string Ts(DateTimeOffset t) => t.ToString("yyyyMMddHHmmsszzz", CultureInfo.InvariantCulture).Replace(":", "");

    /// <summary>Builds ADT^A01 (admit), A08 (update) or A03 (discharge) for SaferSleep: PID, PV1 (location, surgeon, visit number = episode), AL1 allergies, PR1 planned procedure.</summary>
    public static string BuildAdt(string evt, AdtData d, string toApp, string toFacility, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        var ctrl = $"BP{now:yyyyMMddHHmmssfff}{d.EpisodeId.ToString("N")[..4]}";
        sb.Append($"MSH|^~\\&|BrightPAS|TDS|{Esc(toApp)}|{Esc(toFacility)}|{Ts(now)}||ADT^{evt}^ADT_A01|{ctrl}|P|2.4\r");
        sb.Append($"EVN|{evt}|{Ts(now)}\r");
        sb.Append($"PID|1||{Esc(d.Nhi)}^^^NZLMOH^NHI~{Esc(d.Mrn)}^^^TDS^MR||{Esc(d.FamilyName)}^{Esc(d.GivenNames)}||{d.DateOfBirth:yyyyMMdd}|{d.Sex}|||{Esc(d.Address)}^^{Esc(d.City)}^^{Esc(d.Postcode)}^NZ\r");
        var surgeon = d.Surgeon is null ? "" : $"^{Esc(d.Surgeon)}";
        var anaes = d.Anaesthetist is null ? "" : $"^{Esc(d.Anaesthetist)}";
        sb.Append($"PV1|1|I|{Esc(d.Location)}^^^TDS||||{surgeon}||||||||||{anaes}|DAY|{d.EpisodeId:N}|||||||||||||||||||||||||{(d.AdmittedAt is { } a ? Ts(a) : "")}|{(d.DischargedAt is { } x ? Ts(x) : "")}\r");
        var i = 1;
        foreach (var al in d.Allergies) sb.Append($"AL1|{i++}|DA|^{Esc(al)}\r");
        // No AL1 when BrightPAS holds no allergy: silence must not read as "no known allergies" on the anaesthetic record.
        sb.Append($"PR1|1||^{Esc(d.Procedure)}||{(d.PlannedStart is { } ps ? Ts(ps) : "")}\r");
        return sb.ToString();
    }
}
