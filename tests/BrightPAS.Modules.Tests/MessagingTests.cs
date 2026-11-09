using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Integration;
using BrightPAS.Modules.Messaging;

namespace BrightPAS.Modules.Tests;

/// <summary>0.12.0 messaging engine: message documents, mappings, flows, queue/retry, replay, approvals, taps.</summary>
public class MessagingTests
{
    private const string Oru = "MSH|^~\\&|LAB|AWANUI|BrightPAS|TDS|202611101030||ORU^R01|MSG0001|P|2.4\r" +
                               "PID|1||ZZZ0016^^^NHI^NHI||TEST^Aroha^Mere||19800517|F\r" +
                               "OBR|1|ORD123|ACC456|HB^Haemoglobin^L\r" +
                               "OBX|1|NM|HB^Haemoglobin^L||132|g/L|115-155|N|||F\r" +
                               "OBX|2|NM|PLT^Platelets^L||250|10*9/L|150-400|N|||F\r";

    [Fact]
    public void Hl7_paths_read_and_write_fields_components_and_repeats()
    {
        var d = Hl7Doc.Parse(Oru);
        Assert.Equal("ORU", d.Get("MSH-9.1"));
        Assert.Equal("MSG0001", d.Get("MSH-10"));
        Assert.Equal("|", d.Get("MSH-1"));
        Assert.Equal("TEST", d.Get("PID-5.1"));
        Assert.Equal("ZZZ0016", d.Get("PID-3.1"));
        Assert.Equal(2, d.Count("OBX[*]-5"));
        Assert.Equal("250", d.Get("OBX[*]-5", 2));
        Assert.Equal("250", d.Get("OBX[2]-5"));
        d.Set("PID-5.2", "Aroha^Jane");              // separators in values are escaped
        Assert.Equal("Aroha^Jane", d.Get("PID-5.2"));
        Assert.Contains("Aroha\\S\\Jane", d.Render());
        d.Set("NTE[2]-3", "second note");            // creates missing segments
        Assert.Equal(2, d.Count("NTE[*]"));
        Assert.Throws<MappingException>(() => d.Set("MSH-2", "x"));
        Assert.Empty(MessageDocs.Validate(MessageFormat.Hl7v2, Oru));
        Assert.Contains(MessageDocs.Validate(MessageFormat.Hl7v2, "PID|1"), e => e.Contains("MSH"));
    }

    private static MappingRule R(int seq, string target, string? source, RuleFunction f = RuleFunction.Copy, string? arg = null, Guid? table = null) =>
        new() { Sequence = seq, Target = target, Source = source, Function = f, Argument = arg, TranslationTableId = table };

    [Fact]
    public void Hl7_maps_to_fhir_with_translation_dates_join_and_repeating_results()
    {
        var sexTable = Guid.NewGuid();
        var tables = new Dictionary<Guid, Translation> { [sexTable] = new("Sex", MissingCode.UseDefault, "unknown", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["F"] = "female", ["M"] = "male" }) };
        var rules = new[]
        {
            R(1, "resourceType", null, RuleFunction.Constant, "Bundle"),
            R(2, "patient.identifier[0].value", "PID-3.1"),
            R(3, "patient.name", "PID-5.2 + PID-5.1", RuleFunction.Join, " "),
            R(4, "patient.gender", "PID-8", RuleFunction.Translate, table: sexTable),
            R(5, "patient.birthDate", "PID-7", RuleFunction.Date, "yyyyMMdd>yyyy-MM-dd"),
            R(6, "results[*].code", "OBX[*]-3.1"),
            R(7, "results[*].value", "OBX[*]-5"),
        };
        var res = MappingEngine.Run(MessageFormat.Hl7v2, MessageFormat.Json, Oru, StartFrom.Template, "{}", [], rules, tables);
        Assert.True(res.Errors.Count == 0, string.Join(" ", res.Errors));
        var j = JsonNode.Parse(res.Output!)!;
        Assert.Equal("ZZZ0016", j["patient"]!["identifier"]![0]!["value"]!.GetValue<string>());
        Assert.Equal("Aroha TEST", j["patient"]!["name"]!.GetValue<string>());
        Assert.Equal("female", j["patient"]!["gender"]!.GetValue<string>());
        Assert.Equal("1980-05-17", j["patient"]!["birthDate"]!.GetValue<string>());
        Assert.Equal(2, j["results"]!.AsArray().Count);
        Assert.Equal("250", j["results"]![1]!["value"]!.GetValue<string>());

        // Filters stop messages that do not match; a missing code with Reject is an error.
        var filtered = MappingEngine.Run(MessageFormat.Hl7v2, MessageFormat.Json, Oru, StartFrom.Template, "{}",
            [new FilterRule { Source = "MSH-9.1", Operator = ConditionOperator.Equals, Value = "ADT" }], rules, tables);
        Assert.True(filtered.FilteredOut);
        var strict = tables.ToDictionary(t => t.Key, t => t.Value with { WhenMissing = MissingCode.Reject });
        var bad = MappingEngine.Run(MessageFormat.Hl7v2, MessageFormat.Json, Oru.Replace("|F\r", "|X\r"), StartFrom.Template, "{}", [], rules, strict);
        Assert.Contains(bad.Errors, e => e.Contains("not in the translation table"));
    }

    private const string DicomJson = """
        { "00080050": { "vr": "SH", "Value": ["ACC456"] }, "00080060": { "vr": "CS", "Value": ["ES"] }, "00081030": { "vr": "LO", "Value": ["Colonoscopy"] },
          "00100010": { "vr": "PN", "Value": [{ "Alphabetic": "TEST^Aroha^Mere" }] }, "00100020": { "vr": "LO", "Value": ["ZZZ0016"] },
          "00100030": { "vr": "DA", "Value": ["19800517"] }, "00100040": { "vr": "CS", "Value": ["F"] },
          "00400275": { "vr": "SQ", "Value": [{ "00401001": { "vr": "SH", "Value": ["RP1"] } }] } }
        """;

    [Fact]
    public void Dicom_header_maps_to_hl7_and_back()
    {
        var d = DicomDoc.Parse(DicomJson);
        Assert.Equal("TEST^Aroha^Mere", d.Get("PatientName"));
        Assert.Equal("ACC456", d.Get("(0008,0050)"));
        Assert.Equal("RP1", d.Get("RequestAttributesSequence[0].RequestedProcedureID"));
        Assert.Equal(1, d.Count("RequestAttributesSequence[*].RequestedProcedureID"));
        Assert.Contains(d.Tree(), n => n.Label.Contains("PatientName"));

        var toHl7 = new[]
        {
            R(1, "PID-3.1", "PatientID"), R(2, "PID-3.5", null, RuleFunction.Constant, "NHI"),
            R(3, "PID-5.1", "PatientName", RuleFunction.Split, "^|1"), R(4, "PID-5.2", "PatientName", RuleFunction.Split, "^|2"),
            R(5, "PID-7", "PatientBirthDate"), R(6, "PID-8", "PatientSex"),
            R(7, "OBR-3", "AccessionNumber"), R(8, "OBR-4.2", "StudyDescription"), R(9, "MSH-10", "AccessionNumber"),
        };
        var res = MappingEngine.Run(MessageFormat.Dicom, MessageFormat.Hl7v2, DicomJson, StartFrom.Template, "MSH|^~\\&|BrightPAS|TDS|||||ORM^O01||P|2.4\rPID|1\rORC|NW\rOBR|1\r", [], toHl7, new Dictionary<Guid, Translation>());
        Assert.True(res.Errors.Count == 0, string.Join(" ", res.Errors));
        var h = Hl7Doc.Parse(res.Output!);
        Assert.Equal("ZZZ0016", h.Get("PID-3.1"));
        Assert.Equal("Aroha", h.Get("PID-5.2"));
        Assert.Equal("Colonoscopy", h.Get("OBR-4.2"));

        var back = MappingEngine.Run(MessageFormat.Hl7v2, MessageFormat.Dicom, res.Output!, StartFrom.Template, "{}", [],
            [R(1, "PatientID", "PID-3.1"), R(2, "PatientName", "PID-5.1 + PID-5.2", RuleFunction.Join, "^"), R(3, "AccessionNumber", "OBR-3"),
             R(4, "ScheduledProcedureStepSequence[0].Modality", null, RuleFunction.Constant, "ES")], new Dictionary<Guid, Translation>());
        Assert.True(back.Errors.Count == 0, string.Join(" ", back.Errors));
        var dd = DicomDoc.Parse(back.Output!);
        Assert.Equal("TEST^Aroha", dd.Get("PatientName"));
        Assert.Equal("ES", dd.Get("ScheduledProcedureStepSequence[0].Modality"));

        // A real Part 10 file round trip (pixel data never kept).
        var file = DicomConvert.ToFile(back.Output!);
        var json = DicomConvert.ToJson(file);
        Assert.Equal("ZZZ0016", DicomDoc.Parse(json).Get("PatientID"));
        Assert.NotEqual("", DicomDoc.Parse(json).Get("SOPInstanceUID"));
    }

    [Fact]
    public void Cda_xml_and_x12_documents_read_and_write()
    {
        var cda = XmlDoc.Parse(MappingService.SampleFor(MessageFormat.Xml));
        Assert.Equal("ZZZ0016", cda.Get("ClinicalDocument/recordTarget/patientRole/id/@extension"));
        Assert.Equal("Test", cda.Get("ClinicalDocument/recordTarget/patientRole/patient/name/family"));
        cda.Set("ClinicalDocument/component/structuredBody/component[2]/section/title", "Medications");
        Assert.Equal("Medications", cda.Get("ClinicalDocument/component/structuredBody/component[2]/section/title"));
        Assert.Empty(MessageDocs.Validate(MessageFormat.Xml, cda.Render()));
        Assert.NotEmpty(MessageDocs.Validate(MessageFormat.Xml, "<a><b></a>"));
        Assert.Throws<MappingException>(() => XmlDoc.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><x>&e;</x>")); // no external entities

        var x12 = MappingService.SampleFor(MessageFormat.X12);
        Assert.Empty(MessageDocs.Validate(MessageFormat.X12, x12));
        var e = X12Doc.Parse(x12);
        Assert.Equal("ZZZ0016", e.Get("NM1-9"));
        Assert.Equal("4800", e.Get("CLM-2"));
        Assert.Equal("B", e.Get("CLM-5.2"));
        e.Set("CLM-2", "5000");
        Assert.Equal("5000", X12Doc.Parse(e.Render()).Get("CLM-2"));
    }

    private static async Task<(Interface In, Interface OutJson, Interface OutHl7)> Interfaces(MessagingService svc)
    {
        var (i, e1) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "lab-api", "Lab via REST", InterfaceDirection.Inbound, Transport.HttpReceive, MessageFormat.Hl7v2,
            InboundHandler.None, null, null, null, null, "k" + new string('x', 30), null, null, true, 3, null, null), "ia");
        Assert.True(i is not null, string.Join(" ", e1));
        var (o1, e2) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "fhir-out", "FHIR partner", InterfaceDirection.Outbound, Transport.HttpPost, MessageFormat.Json,
            InboundHandler.None, null, null, null, "https://partner.example.nz/fhir", null, null, null, true, 2, null, null), "ia");
        Assert.True(o1 is not null, string.Join(" ", e2));
        var (o2, e3) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "other-out", "Other results", InterfaceDirection.Outbound, Transport.FolderWrite, MessageFormat.Hl7v2,
            InboundHandler.None, null, null, "/tmp/out", null, null, null, null, true, 3, null, null), "ia");
        Assert.True(o2 is not null, string.Join(" ", e3));
        return (i!, o1!, o2!);
    }

    [Fact]
    public async Task Interface_rules_refuse_unsafe_or_inconsistent_settings()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var svc = s.ServiceProvider.GetRequiredService<MessagingService>();
        var (_, errors) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "Bad Code", "", InterfaceDirection.Outbound, Transport.DicomListen, MessageFormat.Hl7v2,
            InboundHandler.LabResults, null, 80, null, null, null, null, [new ClientEdit("not-an-ip", null, null)], true, 3, null, null), "ia");
        Assert.Contains(errors, e => e.Contains("Code"));
        Assert.Contains(errors, e => e.Contains("inbound"));
        Assert.Contains(errors, e => e.Contains("DICOM format"));
        Assert.Contains(errors, e => e.Contains("1024"));
        Assert.Contains(errors, e => e.Contains("AE title"));
        var (_, http) = await svc.SaveInterfaceAsync(new InterfaceEdit(null, "web-out", "Web", InterfaceDirection.Outbound, Transport.HttpPost, MessageFormat.Json,
            InboundHandler.None, null, null, null, "http://partner.example.nz/", null, null, null, true, 3, null, null), "ia");
        Assert.Contains(http, e => e.Contains("https"));
    }

    private static async Task<Guid> LiveMapping(MappingService maps, string name, MessageFormat from, MessageFormat to, IReadOnlyList<RuleEdit> rules)
    {
        var (m, errors) = await maps.CreateMappingAsync(new NewMappingEdit(name, from, to, null), "ia");
        Assert.True(m is not null, string.Join(" ", errors));
        var v = (await maps.MappingAsync(m!.Id))!.Selected;
        Assert.Empty(await maps.SaveDraftAsync(v.Id, new VersionEdit(StartFrom.Template, "{}", Oru, "v1", rules, []), "ia"));
        Assert.Null(await maps.SubmitAsync(v.Id, "ia"));
        Assert.Contains("second person", await maps.ApproveAsync(v.Id, "ia", true));
        Assert.Null(await maps.ApproveAsync(v.Id, "boss", true));
        return m.Id;
    }

    [Fact]
    public async Task Flow_routes_by_decision_maps_and_queues_outputs_with_retry_dead_letter_and_replay()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync(); // registered patient for NHI matching
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var svc = sp.GetRequiredService<MessagingService>();
        var maps = sp.GetRequiredService<MappingService>();
        var flows = sp.GetRequiredService<FlowService>();
        var (input, outJson, outHl7) = await Interfaces(svc);
        var mapId = await LiveMapping(maps, "ORU to JSON", MessageFormat.Hl7v2, MessageFormat.Json,
            [new RuleEdit("nhi", "PID-3.1", RuleFunction.Copy, null, null, null, null, null), new RuleEdit("results[*].value", "OBX[*]-5", RuleFunction.Copy, null, null, null, null, null)]);

        NodeEdit N(string k, NodeKind kind, int x, Guid? iface = null, Guid? map = null, params FilterEdit[] c) => new(k, kind, k, x, 100, iface, map, MatchMode.All, c);
        var flow = new FlowEdit(null, "Lab results routing", null, true,
            [N("in", NodeKind.Input, 0, input.Id), N("isOru", NodeKind.Decision, 200, c: new FilterEdit("MSH-9.1", ConditionOperator.Equals, "ORU")),
             N("map", NodeKind.Mapper, 400, map: mapId), N("fhir", NodeKind.Output, 600, outJson.Id), N("other", NodeKind.Output, 400, outHl7.Id)],
            [new("in", "isOru", EdgeBranch.Next), new("isOru", "map", EdgeBranch.Yes), new("map", "fhir", EdgeBranch.Next), new("isOru", "other", EdgeBranch.No)]);

        // The checker catches drawing mistakes (output format wrong, missing branch labels).
        var noMap = flow.Nodes!.Where(n => n.Key != "map").ToList();
        var unlabelled = await flows.CheckAsync(noMap, [new("in", "isOru", EdgeBranch.Next), new("isOru", "fhir", EdgeBranch.Yes), new("isOru", "other", EdgeBranch.Next)]);
        Assert.Contains(unlabelled, e => e.Contains("Yes or No"));
        var wrongFormat = await flows.CheckAsync(noMap, [new("in", "isOru", EdgeBranch.Next), new("isOru", "fhir", EdgeBranch.Yes), new("isOru", "other", EdgeBranch.No)]);
        Assert.Contains(wrongFormat, e => e.Contains("Add a mapper"));

        // Test harness: dry run shows the path and outputs, nothing stored.
        var test = await flows.TestAsync(flow, Oru, useDrafts: false);
        Assert.Empty(test.Errors);
        Assert.Contains(test.Steps, x => x.NodeKey == "isOru" && x.Outcome == "Yes");
        var o = Assert.Single(test.Outputs);
        Assert.Equal(outJson.Id, o.InterfaceId);
        Assert.Contains("\"132\"", o.Content);
        var adt = await flows.TestAsync(flow, Oru.Replace("ORU^R01", "ADT^A08"), false);
        Assert.Equal(outHl7.Id, Assert.Single(adt.Outputs).InterfaceId);
        Assert.Equal(0, await sp.GetRequiredService<MsgDbContext>().Messages.CountAsync());

        var (saved, errs) = await flows.SaveAsync(flow, "ia");
        Assert.True(saved is not null, string.Join(" ", errs));
        var reloaded = (await flows.FlowAsync(saved!.Id))!;
        Assert.Equal(5, reloaded.Nodes.Count);
        Assert.Equal(4, reloaded.Edges.Count);

        // Live: receive → matched to the patient by NHI → flow → queued output.
        var msg = await svc.ReceiveAsync(input, Oru.Replace("ZZZ0016", p.Nhi ?? "ZZZ0016"), "test");
        await svc.CompleteInboundAsync(msg.Id, MessageStatus.Processed, "AA", null, null);
        Assert.Equal(1, await flows.ProcessAsync(msg, svc.ContentOf(msg)));
        var detail = (await svc.GetAsync(msg.Id, "viewer"))!;
        Assert.Equal(p.Nhi is null ? null : p.Id, detail.Message.PatientId);
        Assert.Contains(detail.Events, e => e.Step == "Routed");
        Assert.Equal("ACC456", detail.Message.Reference);

        // Sending: failures retry with back-off, then dead-letter after the configured attempts; alert raised.
        var settings = await svc.SettingsAsync();
        Assert.Empty(await svc.SaveSettingsAsync(new SettingsEdit(2, "it@tds.example.nz", true), "ia"));
        var due = Assert.Single(await svc.TakeDueAsync());
        await svc.FailedAsync(due.Message.Id, "connection refused", false);
        Assert.Empty(await svc.TakeDueAsync()); // waiting for the retry time
        h.Clock.Advance(TimeSpan.FromMinutes(2));
        due = Assert.Single(await svc.TakeDueAsync());
        await svc.FailedAsync(due.Message.Id, "connection refused", false);
        var dead = Assert.Single(await svc.SearchAsync(new MessageQuery(Status: "problems")));
        Assert.Equal("DeadLetter", dead.Status);
        var alerts = await svc.EvaluateAlertsAsync(new Dictionary<Guid, string>());
        Assert.Contains(alerts, a => a.Kind == "DeadLetter");

        // Resend after the fix: a new linked message, audited, with a reason.
        Assert.NotNull((await svc.ReplayAsync(dead.Id, "ia", "")).Error);
        var (copy, _) = await svc.ReplayAsync(dead.Id, "ia", "partner endpoint fixed");
        Assert.Equal(dead.Id, copy!.ParentId);
        due = Assert.Single(await svc.TakeDueAsync());
        await svc.SentAsync(due.Message.Id, null, "HTTP 200");
        Assert.Equal("Sent", (await svc.GetAsync(copy.Id, "ia"))!.Message.Status);

        // Content is stored encrypted.
        var raw = await sp.GetRequiredService<MsgDbContext>().Messages.AsNoTracking().FirstAsync(m => m.Id == msg.Id);
        Assert.DoesNotContain("TEST^Aroha", System.Text.Encoding.UTF8.GetString(raw.Content));
    }

    [Fact]
    public async Task Mapping_versions_need_second_person_and_can_roll_back()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var maps = s.ServiceProvider.GetRequiredService<MappingService>();
        var id = await LiveMapping(maps, "Map M", MessageFormat.Hl7v2, MessageFormat.Json, [new RuleEdit("a", "PID-3.1", RuleFunction.Copy, null, null, null, null, null)]);
        var v1 = (await maps.MappingAsync(id))!.Versions.Single(v => v.Status == "Live");
        var (v2, err) = await maps.NewDraftAsync(id, v1.Id, "ia");
        Assert.True(v2 is not null, err);
        Assert.NotNull((await maps.NewDraftAsync(id, v1.Id, "ia")).Error); // only one draft at a time
        Assert.Contains(await maps.SaveDraftAsync(v2!.Id, new VersionEdit(StartFrom.Template, "{}", Oru, null, [new RuleEdit("a", "NOT A PATH", RuleFunction.Copy, null, null, null, null, null)], []), "ia"),
            e => e.Contains("HL7 path"));
        Assert.Empty(await maps.SaveDraftAsync(v2.Id, new VersionEdit(StartFrom.Template, "{}", Oru, null, [new RuleEdit("b", "PID-5.1", RuleFunction.Copy, null, null, null, null, null)], []), "ia"));
        Assert.Null(await maps.SubmitAsync(v2.Id, "ia"));
        Assert.Null(await maps.ApproveAsync(v2.Id, "boss", true));
        Assert.Equal(2, (await maps.LiveAsync(id))!.Version.Number);
        Assert.Null(await maps.RollbackAsync(v1.Id, "boss", "v2 broke the partner feed"));
        Assert.Equal(1, (await maps.LiveAsync(id))!.Version.Number);

        // Translation tables import from CSV (formula-looking cells are neutralised on export).
        var entries = MappingService.ParseCsv("From,To,Description\nF,female,\nM,male,\n\"=cmd\",x,");
        Assert.Equal(3, entries.Count);
        var (t, errs) = await maps.SaveTableAsync(new TableEdit(null, "Sex codes", null, MissingCode.PassThrough, null, entries), "ia");
        Assert.True(t is not null, string.Join(" ", errs));
        Assert.Contains("'=cmd", MappingService.ToCsv(entries));
        Assert.NotNull(await maps.DeleteTableAsync(Guid.NewGuid(), "ia"));
    }

    [Fact]
    public async Task Taps_record_other_modules_traffic_on_monitored_interfaces()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var taps = s.ServiceProvider.GetServices<IMessageTap>().ToList();
        Assert.Single(taps);
        await taps.RecordAllAsync(new TappedMessage("sms", false, "BookingConfirmation", "abc", null, "To: 021\n\nHi", true, "Sent via TestOutbox"));
        await taps.RecordAllAsync(new TappedMessage("sms", false, "BookingConfirmation", "def", null, "To: 021\n\nHi", false, "Number not in service"));
        var svc = s.ServiceProvider.GetRequiredService<MessagingService>();
        var sms = Assert.Single(await svc.InterfacesAsync(), i => i.SystemKey == "sms");
        Assert.Equal("Monitored", sms.Transport);
        Assert.Equal(1, sms.Failed24h);
        var (_, error) = await svc.ReplayAsync((await svc.SearchAsync(new MessageQuery(sms.Id))).First().Id, "ia", "try again please");
        Assert.Contains("resent from that module", error);
    }
}
