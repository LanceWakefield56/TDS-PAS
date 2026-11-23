using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Forms;

namespace BrightPAS.Modules.Tests;

/// <summary>0.25.0 Form designer and form import (spec 7.23, ADR 0038).</summary>
public class FormDesignerTests
{
    private static FieldEdit F(string key, string label, FieldType t, bool req = false, string? showKey = null, string? showVal = null, string? calc = null, string? maps = null, decimal? min = null, decimal? max = null, params (string, int?)[] opts) =>
        new(key, null, label, t, req, min, max, showKey, showVal, calc, maps, null, [.. opts.Select(o => new OptionEdit(o.Item1, o.Item2))]);

    private static readonly FieldEdit[] Stopbang =
    [
        F("snore", "Do you snore loudly?", FieldType.YesNo, true), F("tired", "Tired in the day?", FieldType.YesNo, true),
        F("weight", "Weight (kg)", FieldType.Number, true, maps: "weight_kg", min: 20, max: 300), F("height", "Height (cm)", FieldType.Number, true, maps: "height_cm", min: 100, max: 230),
        F("bmi", "BMI", FieldType.Calculated, calc: "bmi(weight,height)"), F("score", "STOP score", FieldType.Calculated, calc: "count(snore,tired)"),
        F("allergies", "Any allergies?", FieldType.YesNo, true), F("allergy_detail", "Which?", FieldType.Text, true, "allergies", "yes", maps: "allergy"),
        F("pain", "Pain", FieldType.Choice, false, opts: [("None", 0), ("Mild", 1), ("Severe", 3)]), F("total", "Total", FieldType.Calculated, calc: "sum(pain,snore)"), F("nhi", "Partner NHI", FieldType.Nhi),
    ];

    [Fact]
    public void Show_hide_validation_and_calculations()
    {
        var fields = Stopbang.Select((f, i) => new FormField
        {
            Key = f.Key!,
            Sequence = i,
            Label = f.Label,
            Type = f.Type,
            Required = f.Required,
            Min = f.Min,
            Max = f.Max,
            ShowIfKey = f.ShowIfKey,
            ShowIfValue = f.ShowIfValue,
            Calculation = f.Calculation,
            Options = [.. (f.Options ?? []).Select((o, j) => new FieldOption { FieldKey = f.Key!, Sequence = j, Value = o.Value, Score = o.Score })]
        }).ToList();
        var (_, errs) = FormLogic.Validate(fields, new Dictionary<string, string> { ["snore"] = "Yes", ["tired"] = "no", ["weight"] = "500", ["height"] = "170", ["allergies"] = "yes", ["nhi"] = "ZZZ0015" }, complete: true);
        Assert.Contains(errs, e => e.StartsWith("Weight", StringComparison.Ordinal));
        Assert.Contains(errs, e => e.StartsWith("Which?", StringComparison.Ordinal)); // shown because allergies = yes
        Assert.Contains(errs, e => e.Contains("NHI", StringComparison.Ordinal));
        var (clean, errs2) = FormLogic.Validate(fields, new Dictionary<string, string> { ["snore"] = "Yes", ["tired"] = "no", ["weight"] = "90", ["height"] = "175", ["allergies"] = "no", ["allergy_detail"] = "hidden answer", ["pain"] = "Severe", ["nhi"] = "zzz0016" }, complete: true);
        Assert.Empty(errs2);
        Assert.False(clean.ContainsKey("allergy_detail")); // hidden answers are dropped
        Assert.Equal("yes", clean["snore"]);
        Assert.Equal("ZZZ0016", clean["nhi"]);
        var calc = FormLogic.Calculate(fields, clean);
        Assert.Equal("29.4", calc["bmi"]);
        Assert.Equal("1", calc["score"]);
        Assert.Equal("4", calc["total"]); // Severe 3 + yes 1
        Assert.Equal("how_are_you", FormLogic.KeyFrom("How are you?", new HashSet<string>()));
        Assert.Equal("q_2nd_question", FormLogic.KeyFrom("2nd question", new HashSet<string>()));
    }

    [Fact]
    public void Csv_and_fhir_import_and_fhir_export()
    {
        var (fields, errors) = FormDesignerService.ParseCsv("question,type,options,required,section\r\nHeading here,heading,,,About you\r\n\"Do you smoke, at all?\",yesno,,yes,About you\r\nPain,choice,None=0|Mild=1|Severe=3,no,Pain\r\nOdd,wibble,,,\r\n");
        Assert.Single(errors);
        Assert.Equal(3, fields.Count);
        Assert.Equal("Do you smoke, at all?", fields[1].Label);
        Assert.True(fields[1].Required);
        Assert.Equal(3, fields[2].Options![2].Score);
        var fhir = """{"resourceType":"Questionnaire","title":"Pre-op check","item":[{"linkId":"g1","type":"group","text":"Health","item":[{"linkId":"diab","type":"boolean","text":"Diabetes?","required":true},{"linkId":"ins","type":"boolean","text":"On insulin?","enableWhen":[{"question":"diab","operator":"=","answerBoolean":true}]},{"linkId":"asa","type":"choice","text":"ASA","answerOption":[{"valueCoding":{"code":"1","display":"ASA 1"}},{"valueCoding":{"code":"2","display":"ASA 2"}}]}]}]}""";
        var (name, ff, fe) = FormDesignerService.ParseFhir(fhir);
        Assert.Empty(fe);
        Assert.Equal("Pre-op check", name);
        Assert.Equal(FieldType.Heading, ff[0].Type);
        Assert.Equal("diab", ff[2].ShowIfKey); Assert.Equal("yes", ff[2].ShowIfValue);
        Assert.Equal("ASA 2", ff[3].Options![1].Value);
        Assert.NotEmpty(FormDesignerService.ParseFhir("""{"resourceType":"Patient"}""").Errors);
        Assert.NotEmpty(FormDesignerService.ParseFhir("not json").Errors);
    }

    private sealed class Sink : IFormDataSink
    {
        public Dictionary<string, string> Got { get; } = [];
        public Task<IReadOnlyList<string>> ApplyAsync(FormEntry entry, IReadOnlyDictionary<string, string> mapped, string user, CancellationToken ct)
        { foreach (var kv in mapped) Got[kv.Key] = kv.Value; return Task.FromResult<IReadOnlyList<string>>(["applied"]); }
    }

    [Fact]
    public async Task Versions_governance_entries_keep_their_version_and_mapped_answers_flow_on_completion()
    {
        await using var h = new ModulesHost();
        await using var scope = h.Scope();
        var sink = new Sink();
        var svc = ActivatorUtilities.CreateInstance<FormDesignerService>(scope.ServiceProvider, new object[] { new IFormDataSink[] { sink } });
        var p = await h.PatientAsync();
        Assert.NotEmpty((await svc.CreateAsync("SB", "STOP-BANG", FormCategory.Clinical, DesignSource.Designer, [F("x", "", FieldType.Choice)], "quality")).Errors);
        var (v1, errors) = await svc.CreateAsync("SB", "STOP-BANG", FormCategory.Clinical, DesignSource.Designer, Stopbang, "quality");
        Assert.Empty(errors);
        Assert.NotEmpty((await svc.CreateAsync("SB", "Again", FormCategory.Audit, DesignSource.Designer, Stopbang, "quality")).Errors);
        Assert.NotNull((await svc.StartAsync(v1!.Id, p.Id, null, "nurse")).Error); // draft
        Assert.Null(await svc.TestAsync(v1.Id, "quality"));
        Assert.NotNull(await svc.PublishAsync(v1.Id, false, null, "quality")); // clinical: needs approval
        Assert.NotNull(await svc.PublishAsync(v1.Id, true, null, "quality")); // and a note
        Assert.Null(await svc.PublishAsync(v1.Id, true, "Clinical Governance 20 Nov 2026, item 4", "quality"));
        Assert.NotEmpty(await svc.SaveAsync(v1.Id, new VersionEdit("x", null, null, false, null, Stopbang), "quality")); // published can't change

        var (e, err) = await svc.StartAsync(v1.Id, p.Id, null, "nurse");
        Assert.Null(err);
        Assert.NotNull((await svc.StartAsync(v1.Id, null, null, "nurse")).Error); // clinical needs a patient
        var answers = new Dictionary<string, string> { ["snore"] = "yes", ["tired"] = "yes", ["weight"] = "110", ["height"] = "170", ["allergies"] = "yes", ["allergy_detail"] = "Penicillin – rash" };
        Assert.Empty((await svc.SaveEntryAsync(e!.Id, answers, false, "nurse")).Errors);
        Assert.Empty(sink.Got);

        // A new version is published while the entry is in progress: the entry stays on v1.
        var (v2, _) = await svc.NewVersionAsync(v1.DesignId, "quality");
        Assert.NotNull((await svc.NewVersionAsync(v1.DesignId, "quality")).Error);
        Assert.Null(await svc.PublishAsync(v2!.Id, true, "CG approved v2", "quality"));
        Assert.Equal(DesignStatus.Retired, (await svc.VersionAsync(v1.Id))!.Status);
        Assert.NotNull((await svc.StartAsync(v1.Id, p.Id, null, "nurse")).Error); // retired
        var (_, applied) = await svc.SaveEntryAsync(e.Id, answers, true, "nurse");
        Assert.Equal(["applied"], applied);
        Assert.Equal("110", sink.Got["weight_kg"]);
        Assert.Equal("Penicillin – rash", sink.Got["allergy"]);
        Assert.NotEmpty((await svc.SaveEntryAsync(e.Id, answers, true, "nurse")).Errors); // completed
        var entry = await svc.EntryAsync(e.Id, "nurse");
        Assert.Equal(v1.Id, entry!.VersionId);
        Assert.Contains(entry.Values, x => x.FieldKey == "bmi" && x.Value == "38.1");

        // Licensed content can't be published without the licence; FHIR export works.
        var (lic, _) = await svc.CreateAsync("PROM1", "Licensed PROM", FormCategory.Audit, DesignSource.Designer, Stopbang, "quality");
        Assert.Empty(await svc.SaveAsync(lic!.Id, new VersionEdit("Licensed PROM", null, null, true, null, Stopbang), "quality"));
        Assert.NotNull(await svc.PublishAsync(lic.Id, false, null, "quality"));
        Assert.Contains("\"resourceType\": \"Questionnaire\"", FormDesignerService.ToFhir((await svc.DesignsAsync()).Single(d => d.Code == "SB"), (await svc.VersionAsync(v2.Id))!), StringComparison.Ordinal);
    }
}
