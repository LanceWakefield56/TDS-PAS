using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Data;
using BrightPAS.Core.Organisation;
using BrightPAS.Messaging.Hl7;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.EBookings;

public sealed record ImportOutcome(int Received, IReadOnlyList<string> Numbers, IReadOnlyList<string> Problems);

/// <summary>
/// The file fallback for practice systems: BrightPAS booking JSON (one booking, an array, or {"bookings":[…]}) or HL7 v2 REF^I12 referral messages
/// (one or more in a file). The specialist is found by HPI CPN or by name on TDS's clinician register.
/// </summary>
public sealed class EBookingImporter(EBookingService service, PasDbContext core, TimeProvider clock)
{
    public const int MaxBytes = 60 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public async Task<ImportOutcome> ImportAsync(string fileName, byte[] bytes, string user, CancellationToken ct = default)
    {
        if (bytes.Length == 0) return new(0, [], ["The file is empty."]);
        if (bytes.Length > MaxBytes) return new(0, [], ["The file is larger than 60 MB."]);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        var trimmed = text.TrimStart();
        var parsed = trimmed.StartsWith("MSH", StringComparison.Ordinal) || trimmed.StartsWith('\v')
            ? await FromHl7Async(text, ct)
            : trimmed.StartsWith('{') || trimmed.StartsWith('[') ? await FromJsonAsync(text, ct)
            : [(null, "This is not a BrightPAS booking file (JSON) or an HL7 REF^I12 message.", EBookingSource.File, "")];

        var numbers = new List<string>();
        var problems = new List<string>();
        var who = $"file:{DocumentService.SafeFileName(fileName)}";
        var user2 = user.Length > 0 ? user : who;
        foreach (var (input, problem, source, label) in parsed)
        {
            if (input is null) { problems.Add(Label(label, problem)); continue; }
            var r = await service.SubmitAsync(input with { ConfirmDuplicate = true }, source, user2, null, ct);
            if (r.Request is null) problems.Add(Label(label, string.Join(" ", r.Errors)));
            else numbers.Add(r.Request.Number);
        }
        return new(numbers.Count, numbers, problems);
    }

    private static string Label(string label, string? problem) => label.Length > 0 ? $"{label}: {problem}" : problem ?? "";

    // ================================================================= JSON

    /// <summary>One booking in a BrightPAS booking file.</summary>
    public sealed class JsonBooking
    {
        public string? Reference { get; set; }
        public Guid? SpecialistId { get; set; }
        public string? SpecialistCpn { get; set; }
        public string? SpecialistName { get; set; }
        public string? RoomsName { get; set; }
        public string? ContactName { get; set; }
        public string? ContactPhone { get; set; }
        public string? ContactEmail { get; set; }
        public JsonPatient? Patient { get; set; }
        public string? Service { get; set; }
        public string? ProcedureCode { get; set; }
        public string? Procedure { get; set; }
        public string? Side { get; set; }
        public string? Anaesthetic { get; set; }
        public int? DurationMinutes { get; set; }
        public string? Priority { get; set; }
        public string? Funder { get; set; }
        public string? FunderReference { get; set; }
        public string? InsurerName { get; set; }
        public DateOnly? EarliestDate { get; set; }
        public DateOnly? LatestDate { get; set; }
        public string? Availability { get; set; }
        public string? ClinicalNotes { get; set; }
        public decimal? WeightKg { get; set; }
        public decimal? HeightCm { get; set; }
        public int? Asa { get; set; }
        public bool Diabetic { get; set; }
        public bool SleepApnoea { get; set; }
        public bool Anticoagulated { get; set; }
        public string? AnticoagulantDetails { get; set; }
        public string? Allergies { get; set; }
        public string? Comorbidities { get; set; }
        public bool ResponsibleAdultArranged { get; set; }
        public bool ConsentSigned { get; set; }
        public DateOnly? ConsentSignedOn { get; set; }
        public List<JsonFile>? Attachments { get; set; }
    }

    public sealed class JsonPatient
    {
        public string? Nhi { get; set; }
        public string? FamilyName { get; set; }
        public string? GivenNames { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Sex { get; set; }
        public string? MobilePhone { get; set; }
        public string? Email { get; set; }
        public string? AddressLine1 { get; set; }
        public string? Suburb { get; set; }
        public string? City { get; set; }
        public string? Postcode { get; set; }
        public string? Interpreter { get; set; }
    }

    public sealed class JsonFile
    {
        public string? Category { get; set; }
        public string? FileName { get; set; }
        public string? ContentBase64 { get; set; }
    }

    private sealed class JsonEnvelope { public List<JsonBooking>? Bookings { get; set; } }

    private async Task<List<(EBookingInput?, string?, EBookingSource, string)>> FromJsonAsync(string text, CancellationToken ct)
    {
        List<JsonBooking> bookings;
        try
        {
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            bookings = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement.Deserialize<List<JsonBooking>>(Json) ?? [],
                JsonValueKind.Object when doc.RootElement.TryGetProperty("bookings", out _) => doc.RootElement.Deserialize<JsonEnvelope>(Json)?.Bookings ?? [],
                JsonValueKind.Object => [doc.RootElement.Deserialize<JsonBooking>(Json)!],
                _ => [],
            };
        }
        catch (JsonException ex) { return [(null, $"The booking file is not valid JSON ({ex.Message.Split('.')[0]}).", EBookingSource.File, "")]; }
        if (bookings.Count == 0) return [(null, "The file has no bookings in it.", EBookingSource.File, "")];
        var list = new List<(EBookingInput?, string?, EBookingSource, string)>();
        var n = 0;
        foreach (var b in bookings)
        {
            n++;
            var label = b.Reference is { Length: > 0 } rf ? $"Booking {rf}" : $"Booking {n}";
            var spec = await SpecialistAsync(b.SpecialistId, b.SpecialistCpn, b.SpecialistName, ct);
            if (spec is null) { list.Add((null, "the specialist was not found on TDS's clinician register (send their HPI CPN or exact name).", EBookingSource.File, label)); continue; }
            var item = b.ProcedureCode is { Length: > 0 } code ? await core.Procedures.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code && p.IsActive, ct) : null;
            var p = b.Patient ?? new JsonPatient();
            var problems = new List<string>();
            var files = new List<EBookingFileInput>();
            foreach (var f in b.Attachments ?? [])
                files.Add(new EBookingFileInput(Category(f.Category), f.FileName ?? "attachment", f.ContentBase64 ?? ""));
            var input = new EBookingInput(spec.Id, b.RoomsName ?? spec.PracticeName, b.ContactName ?? spec.Name, b.ContactPhone ?? spec.Phone, b.ContactEmail ?? spec.Email,
                p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth, Sex(p.Sex), p.MobilePhone, p.Email, p.AddressLine1, p.Suburb, p.City, p.Postcode, p.Interpreter,
                Parse(b.Service, ServiceType.Surgical, problems, "service"), item?.Id, b.Procedure, Side(b.Side, b.Procedure), Parse(b.Anaesthetic, Anaesthetic(item), problems, "anaesthetic"),
                b.DurationMinutes, PriorityOf(b.Priority), FunderOf(b.Funder, b.InsurerName), b.FunderReference, b.InsurerName, b.EarliestDate, b.LatestDate, b.Availability, b.ClinicalNotes,
                b.WeightKg, b.HeightCm, b.Asa, b.Diabetic, b.SleepApnoea, b.Anticoagulated, b.AnticoagulantDetails, b.Allergies, b.Comorbidities, b.ResponsibleAdultArranged,
                b.ConsentSigned, b.ConsentSignedOn, b.Reference, files);
            list.Add(problems.Count > 0 ? (null, string.Join(" ", problems), EBookingSource.File, label) : (input, null, EBookingSource.File, label));
        }
        return list;
    }

    // ================================================================= HL7 REF^I12

    private async Task<List<(EBookingInput?, string?, EBookingSource, string)>> FromHl7Async(string text, CancellationToken ct)
    {
        var list = new List<(EBookingInput?, string?, EBookingSource, string)>();
        foreach (var raw in BrightPAS.Messaging.MessageFile.SplitHl7(text))
        {
            Hl7Message m;
            try { m = Hl7Message.Parse(raw); }
            catch (Hl7Exception ex) { list.Add((null, $"HL7 message could not be read: {ex.Message}", EBookingSource.Hl7, "")); continue; }
            var label = $"HL7 message {m.ControlId}";
            if (!m.MessageType.StartsWith("REF^", StringComparison.Ordinal) && !m.MessageType.StartsWith("RRI^", StringComparison.Ordinal))
            { list.Add((null, $"is {m.MessageType}, not a referral (REF^I12).", EBookingSource.Hl7, label)); continue; }
            list.Add(await FromRefAsync(m, label, ct));
        }
        if (list.Count == 0) list.Add((null, "The file has no HL7 messages in it.", EBookingSource.Hl7, ""));
        return list;
    }

    private async Task<(EBookingInput?, string?, EBookingSource, string)> FromRefAsync(Hl7Message m, string label, CancellationToken ct)
    {
        var pid = m.First("PID");
        if (pid is null) return (null, "has no PID (patient) segment.", EBookingSource.Hl7, label);
        var rf1 = m.First("RF1");
        var prds = m.All("PRD").ToList();
        var to = prds.FirstOrDefault(p => p.Get(1, 1) is "RT" or "CP") ?? prds.FirstOrDefault(p => p.Get(1, 1) != "RP");
        var from = prds.FirstOrDefault(p => p.Get(1, 1) == "RP");
        Clinician? spec = null;
        foreach (var p in new[] { to, from }.Where(p => p is not null))
        {
            spec = await SpecialistAsync(null, Cpn(p!), ProviderName(p!), ct);
            if (spec is not null) break;
        }
        if (spec is null) return (null, "the referred-to specialist (PRD RT) was not found on TDS's clinician register by HPI CPN or name.", EBookingSource.Hl7, label);

        string? nhi = null;
        for (var rep = 0; rep < Math.Max(1, pid.Repetitions(3)); rep++)
        {
            var id = pid.Get(3, 1, 0, rep);
            var authority = pid.Get(3, 4, 0, rep) + " " + pid.Get(3, 5, 0, rep);
            if (authority.Contains("NHI", StringComparison.OrdinalIgnoreCase) || (nhi is null && BrightPAS.Core.Patients.NhiValidator.Validate(id).IsValid)) { nhi = id; if (authority.Contains("NHI", StringComparison.OrdinalIgnoreCase)) break; }
        }
        var dob = Date(pid.Get(7, 1));
        var phone = pid.Get(13, 1);
        var email = pid.Get(13, 4);

        var pr1 = m.First("PR1");
        var procCode = pr1?.Get(3, 1) ?? "";
        var procText = pr1?.Get(3, 2) is { Length: > 0 } t ? t : pr1?.Get(4) ?? "";
        var item = procCode.Length > 0 ? await core.Procedures.AsNoTracking().FirstOrDefaultAsync(p => p.Code == procCode && p.IsActive, ct) : null;
        if (procText.Length == 0) procText = rf1?.Get(10, 2) is { Length: > 0 } rr ? rr : item?.Name ?? "";

        var notes = new List<string>();
        foreach (var dg in m.All("DG1")) if (Pick(dg.Get(3, 2), dg.Get(4)) is { } d) notes.Add($"Diagnosis: {d}");
        if (rf1?.Get(10, 2) is { Length: > 0 } reason && reason != procText) notes.Add($"Reason: {reason}");
        foreach (var nte in m.All("NTE")) if (nte.Get(3) is { Length: > 0 } n) notes.Add(n);
        var allergies = m.All("AL1").Select(a => Pick(a.Get(3, 2), a.Get(3, 1))).Where(a => a is not null).ToList();

        decimal? weight = null, height = null; int? asa = null;
        var files = new List<EBookingFileInput>();
        foreach (var obx in m.All("OBX"))
        {
            var type = obx.Get(2);
            var what = (obx.Get(3, 1) + " " + obx.Get(3, 2)).ToUpperInvariant();
            if (type == "ED")
            {
                if (obx.Get(5, 4).Equals("Base64", StringComparison.OrdinalIgnoreCase) && obx.Get(5, 5) is { Length: > 0 } data)
                {
                    var ext = obx.Get(5, 3).ToLowerInvariant() switch { "pdf" => ".pdf", "jpeg" or "jpg" => ".jpg", "png" => ".png", "tiff" => ".tif", _ => "" };
                    var cat = what.Contains("CONSENT", StringComparison.Ordinal) ? DocumentCategory.Consent : what.Contains("REFERRAL", StringComparison.Ordinal) || what.Contains("LETTER", StringComparison.Ordinal) ? DocumentCategory.Referral : DocumentCategory.Clinical;
                    files.Add(new EBookingFileInput(cat, (Pick(obx.Get(3, 2), "attachment") ?? "attachment") + ext, data));
                }
                continue;
            }
            var value = obx.Get(5);
            if (what.Contains("29463-7", StringComparison.Ordinal) || what.Contains("WEIGHT", StringComparison.Ordinal)) weight = Num(value);
            else if (what.Contains("8302-2", StringComparison.Ordinal) || what.Contains("HEIGHT", StringComparison.Ordinal)) height = Num(value);
            else if (what.Contains("ASA", StringComparison.Ordinal)) asa = int.TryParse(value.TrimStart('A', 'S', ' ').Split(' ')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? a : null;
        }

        var in1 = m.First("IN1");
        var insurer = in1 is null ? null : Pick(in1.Get(4, 1), in1.Get(3, 1));
        var funder = insurer is null ? Funder.SelfPay : insurer.Contains("ACC", StringComparison.OrdinalIgnoreCase) ? Funder.Acc
            : insurer.Contains("Health NZ", StringComparison.OrdinalIgnoreCase) || insurer.Contains("Te Whatu Ora", StringComparison.OrdinalIgnoreCase) ? Funder.HealthNz : Funder.Insurer;
        var funderRef = in1 is null ? null : Pick(in1.Get(14, 1), in1.Get(36));

        var external = Pick(rf1?.Get(6, 1), rf1?.Get(11, 1), m.ControlId);
        var input = new EBookingInput(spec.Id, Pick(from?.Get(4, 1), m.SendingFacility, spec.PracticeName), Pick(from is null ? null : ProviderName(from), spec.Name),
            Pick(from?.Get(5, 1), spec.Phone), Pick(from?.Get(5, 4), spec.Email),
            nhi, pid.Get(5, 1), string.Join(' ', new[] { pid.Get(5, 2), pid.Get(5, 3) }.Where(x => x.Length > 0)), dob, pid.Get(8) switch { "F" => BirthSex.Female, "M" => BirthSex.Male, "O" or "A" => BirthSex.Indeterminate, _ => BirthSex.Unknown },
            phone.Length > 0 ? phone : null, email.Length > 0 ? email : null, Pick(pid.Get(11, 1)), Pick(pid.Get(11, 2)), Pick(pid.Get(11, 3)), Pick(pid.Get(11, 5)), null,
            ServiceType.Surgical, item?.Id, procText.Length > 0 ? procText : null, Side(null, procText), Anaesthetic(item), null,
            rf1?.Get(2, 1) switch { "S" or "U" => Priority.Urgent, "A" => Priority.SemiUrgent, _ => Priority.Routine },
            funder, funderRef, funder == Funder.Insurer ? insurer : null, null, null, null, notes.Count > 0 ? string.Join("\n", notes) : null,
            weight, height, asa, false, false, false, null, allergies.Count > 0 ? string.Join(", ", allergies) : null, null, false, false, null, external, files);
        return (input, null, EBookingSource.Hl7, label);
    }

    // ================================================================= helpers

    private async Task<Clinician?> SpecialistAsync(Guid? id, string? cpn, string? name, CancellationToken ct)
    {
        var active = core.Clinicians.AsNoTracking().Where(c => c.IsActive && c.Type != ClinicianType.Anaesthetist);
        if (id is { } g && await active.SingleOrDefaultAsync(c => c.Id == g, ct) is { } byId) return byId;
        if (cpn is { Length: > 0 }) { var u = cpn.Trim().ToUpperInvariant(); if (await active.FirstOrDefaultAsync(c => c.HpiCpn == u, ct) is { } byCpn) return byCpn; }
        if (name is { Length: > 0 })
        {
            var key = NameKey(name);
            var all = await active.ToListAsync(ct);
            return all.FirstOrDefault(c => NameKey(c.Name) == key) ?? all.FirstOrDefault(c => key.Length > 3 && NameKey(c.Name).EndsWith(key, StringComparison.Ordinal));
        }
        return null;
    }

    /// <summary>"Dr Jane Smith", "SMITH, Jane" and "Jane Smith" all compare the same.</summary>
    public static string NameKey(string name)
    {
        var n = name.Trim();
        if (n.Contains(',', StringComparison.Ordinal)) { var parts = n.Split(',', 2); n = parts[1].Trim() + " " + parts[0].Trim(); }
        var words = n.Replace(".", " ", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !new[] { "DR", "MR", "MRS", "MS", "MISS", "PROF", "PROFESSOR", "ASSOC" }.Contains(w.ToUpperInvariant()));
        return PatientService.SearchKey(string.Join(' ', words));
    }

    private static string? Cpn(Hl7Segment prd)
    {
        for (var rep = 0; rep < Math.Max(1, prd.Repetitions(7)); rep++)
        {
            var v = prd.Get(7, 1, 0, rep);
            if (System.Text.RegularExpressions.Regex.IsMatch(v.ToUpperInvariant(), "^[0-9]{2}[A-Z]{4}$")) return v;
        }
        return null;
    }

    private static string ProviderName(Hl7Segment prd)
    {
        var family = prd.Get(2, 1); var given = prd.Get(2, 2);
        return given.Length > 0 ? $"{given} {family}" : family;
    }

    private static string? Pick(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static DateOnly? Date(string v) =>
        v.Length >= 8 && DateOnly.TryParseExact(v[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Num(string v) =>
        decimal.TryParse(new string(v.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static T Parse<T>(string? v, T fallback, List<string> problems, string what) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        if (Enum.TryParse<T>(v.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal), true, out var r) && Enum.IsDefined(r)) return r;
        problems.Add($"\"{v}\" is not a known {what} ({string.Join(", ", Enum.GetNames<T>())}).");
        return fallback;
    }

    public static Laterality Side(string? side, string? procedure)
    {
        var s = (side ?? "").Trim().ToUpperInvariant();
        if (s is "L" or "LEFT") return Laterality.Left;
        if (s is "R" or "RIGHT") return Laterality.Right;
        if (s is "B" or "BOTH" or "BILATERAL") return Laterality.Bilateral;
        var p = " " + (procedure ?? "").ToUpperInvariant() + " ";
        if (p.Contains(" BILATERAL ", StringComparison.Ordinal)) return Laterality.Bilateral;
        if (p.Contains(" LEFT ", StringComparison.Ordinal) || p.Contains(" (L) ", StringComparison.Ordinal)) return Laterality.Left;
        if (p.Contains(" RIGHT ", StringComparison.Ordinal) || p.Contains(" (R) ", StringComparison.Ordinal)) return Laterality.Right;
        return Laterality.NotApplicable;
    }

    private static AnaestheticType Anaesthetic(ProcedureItem? item) =>
        item is not null && Enum.TryParse<AnaestheticType>(item.DefaultAnaesthetic, true, out var a) ? a : AnaestheticType.General;

    public static BirthSex Sex(string? v) => (v ?? "").Trim().ToUpperInvariant() switch
    {
        "F" or "FEMALE" => BirthSex.Female,
        "M" or "MALE" => BirthSex.Male,
        "I" or "X" or "O" or "INDETERMINATE" => BirthSex.Indeterminate,
        _ => BirthSex.Unknown,
    };

    public static Priority PriorityOf(string? v) => (v ?? "").Trim().ToUpperInvariant().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal) switch
    {
        "URGENT" or "1" or "U" => Priority.Urgent,
        "SEMIURGENT" or "2" or "S" => Priority.SemiUrgent,
        _ => Priority.Routine,
    };

    public static Funder FunderOf(string? v, string? insurer)
    {
        var s = (v ?? "").Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        return s switch
        {
            "ACC" => Funder.Acc,
            "INSURER" or "INSURANCE" or "PRIVATE" => Funder.Insurer,
            "HEALTHNZ" or "PUBLIC" or "TEWHATUORA" => Funder.HealthNz,
            "SELF" or "SELFPAY" or "SELFFUNDED" => Funder.SelfPay,
            "OTHER" => Funder.Other,
            _ => insurer is { Length: > 0 } ? Funder.Insurer : Funder.SelfPay,
        };
    }

    private static DocumentCategory Category(string? v) =>
        Enum.TryParse<DocumentCategory>(v ?? "", true, out var c) && Enum.IsDefined(c) ? c : DocumentCategory.Referral;

    /// <summary>An example BrightPAS booking file, offered to practices as a template.</summary>
    public static string ExampleJson(DateOnly today) => $$"""
        {
          "bookings": [
            {
              "reference": "ROOMS-000123",
              "specialistCpn": "12ABCD",
              "roomsName": "Nelson Surgical Rooms",
              "contactName": "Practice assistant",
              "contactPhone": "03 546 0000",
              "contactEmail": "bookings@example.co.nz",
              "patient": { "nhi": "ZZZ0016", "familyName": "Example", "givenNames": "Pat", "dateOfBirth": "1970-05-01", "sex": "F",
                           "mobilePhone": "021 000 0000", "addressLine1": "1 Example Street", "city": "Nelson", "postcode": "7010" },
              "service": "Surgical",
              "procedureCode": "",
              "procedure": "Laparoscopic cholecystectomy",
              "side": "",
              "anaesthetic": "General",
              "durationMinutes": 75,
              "priority": "Routine",
              "funder": "Insurer",
              "insurerName": "Southern Cross",
              "funderReference": "PA-123456",
              "earliestDate": "{{today.AddDays(14):yyyy-MM-dd}}",
              "latestDate": "{{today.AddDays(60):yyyy-MM-dd}}",
              "availability": "Not Fridays",
              "clinicalNotes": "Symptomatic gallstones.",
              "weightKg": 82, "heightCm": 168, "asa": 2,
              "diabetic": false, "sleepApnoea": false, "anticoagulated": false,
              "allergies": "", "comorbidities": "Hypertension",
              "responsibleAdultArranged": true, "consentSigned": true, "consentSignedOn": "{{today:yyyy-MM-dd}}",
              "attachments": [ { "category": "Referral", "fileName": "letter.pdf", "contentBase64": "<base64 of the PDF>" } ]
            }
          ]
        }
        """;

    internal DateOnly Today => NzTime.Today(clock);
}
