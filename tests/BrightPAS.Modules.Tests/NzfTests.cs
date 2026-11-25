using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using BrightPAS.Modules.Medicines;

namespace BrightPAS.Modules.Tests;

/// <summary>0.27.0 NZ Formulary import: reading dosing lines, monograph structure, the PDF reader, import, hints and rule suggestions.</summary>
public class NzfTests
{
    [Fact]
    public void Dosing_lines_are_read_into_limits()
    {
        var a = NzfParser.ReadPopulationLine("Mild to moderate pain", "Oral, immediate release", "Adult, body-weight over 50 kg 0.5–1 g every 4–6 hours; maximum 4 g daily")!;
        Assert.Equal(("Adult, body-weight over 50 kg", 18, (decimal?)50, "g", 1m, 4m, 4m), (a.Population, a.MinAgeYears!.Value, a.MinWeightKg, a.Unit!, a.MaxSingle!.Value, a.MaxDaily!.Value, a.IntervalHours!.Value));
        Assert.Equal("Oral", a.RouteCodes);
        var b = NzfParser.ReadPopulationLine("Pain", "Intravenous infusion", "Adult, body-weight 33–50 kg 15 mg/kg every 4–6 hours; maximum 60 mg/kg daily, without exceeding 3 g daily (see also Cautions)")!;
        Assert.Equal((33m, 50m, 15m, 60m, 3000m), (b.MinWeightKg!.Value, b.MaxWeightKg!.Value, b.MaxSinglePerKg!.Value, b.MaxDailyPerKg!.Value, b.MaxDaily!.Value));
        Assert.Null(b.MaxSingle);
        var c = NzfParser.ReadPopulationLine("Pain", "Oral", "Child 1 month–18 years 15 mg/kg per dose (maximum 1 g) every 4 hours; maximum 75 mg/kg per day (without exceeding 4 g) for 48 hours")!;
        Assert.Equal((0, 18, 15m, 1000m, 75m, 4000m), (c.MinAgeYears!.Value, c.MaxAgeYears!.Value, c.MaxSinglePerKg!.Value, c.MaxSingle!.Value, c.MaxDailyPerKg!.Value, c.MaxDaily!.Value));
        var d = NzfParser.ReadPopulationLine("Pain", "Oral", "Adult 50 mg up to 3 times daily")!;
        Assert.Equal((50m, 150m), (d.MaxSingle!.Value, d.MaxDaily!.Value));
        var t = NzfParser.ReadPopulationLine("Hypertension", "Oral", "Adult initially 10 mg daily, increased as tolerated to usual maintenance 25–100 mg daily; maximum 150 mg daily")!;
        Assert.True(t.Titration);
        Assert.Null(t.MaxSingle);
        Assert.Equal(150m, t.MaxDaily);
        var inf = NzfParser.ReadPopulationLine("Seizures", "Continuous subcutaneous infusion", "Adult 10–60 mg over 24 hours")!;
        Assert.Null(inf.MaxSingle);
        Assert.Equal("IV,IM", NzfParser.RouteCodes("Intravenous injection or intramuscular injection"));
        Assert.True(NzfParser.ReadPopulationLine("x", "Oral", "Neonate 28–32 weeks corrected gestational age 20 mg/kg as a single dose")!.Neonate);
        Assert.Equal("ibuprofen", NzfParser.BaseName("ibuprofen (systemic)"));
        Assert.Equal("tramadol", NzfParser.BaseName("tramadol hydrochloride"));
        Assert.Equal("morphine", NzfParser.BaseName("morphine salts"));
        Assert.Equal("lignocaine", NzfParser.BaseName("lignocaine hydrochloride"));
        Assert.Equal(new DateOnly(2026, 11, 2), NzfService.FirstWorkingDay(2026, 11)); // 1 Nov 2026 is a Sunday
        Assert.Equal(new DateOnly(2027, 1, 4), NzfService.FirstWorkingDay(2027, 1));
    }

    private static NzfLine L(double x, double size, bool bold, string text) => new(1, x, size, bold, text);

    private static IEnumerable<NzfLine> Mono(string name, string? syn, string route, string population)
    {
        yield return L(28.3, 13.5, true, name);
        if (syn is not null) yield return L(28.3, 7.6, true, $"( {syn} )");
        yield return L(28.3, 7.6, true, "Indications");
        yield return L(28.3, 7.2, false, "pain");
        yield return L(28.3, 7.6, true, "Contra-indications");
        yield return L(28.3, 7.2, false, "severe hepatic impairment");
        yield return L(28.3, 7.6, true, "Renal impairment");
        yield return L(28.3, 7.2, false, "use with caution");
        yield return L(28.3, 7.6, true, "Dosing regimen");
        yield return L(33.0, 8.9, true, "Pain");
        yield return L(39.7, 8.0, true, route);
        yield return L(48.2, 7.6, true, population);
        yield return L(48.2, 7.6, false, "maximum 4 g daily");
        yield return L(28.3, 7.6, true, "Patient advice");
        yield return L(34.7, 7.2, false, "Do not take other paracetamol.");
        yield return L(34.2, 6.8, true, name);
        yield return L(42.7, 6.8, true, "tablet");
        yield return L(44.4, 6.3, true, "500 mg   full subsidy (restrictions)");
        yield return L(42.7, 6.8, true, "injection: intravenous infusion");
        yield return L(44.4, 6.3, true, "1 g/100 mL");
    }

    [Fact]
    public void Monographs_sections_dosing_and_preparations_are_found()
    {
        var lines = new List<NzfLine> { L(28.3, 7.2, false, "New Zealand Formulary release 173—1 November 2026 | ISSN: 2253-5446") };
        lines.Add(L(28.3, 12.3, true, "4.7.1 Non-opioid analgesics"));
        lines.AddRange(Mono("paracetamol", "acetaminophen", "Oral, immediate release", "Adult, body-weight over 50 kg 0.5–1 g every 4–6 hours;"));
        lines.Add(L(28.3, 13.5, true, "Non-steroidal anti-inflammatory drugs (NSAIDs)")); // a topic page, not a drug
        lines.Add(L(28.3, 7.2, false, "NSAIDs are useful."));
        var doc = NzfParser.Parse(lines);
        Assert.Equal(("NZF", 173, new DateOnly(2026, 11, 1)), (doc.Edition, doc.Release!.Value, doc.ReleaseDate!.Value));
        var m = Assert.Single(doc.Monographs);
        Assert.Equal(("paracetamol", "acetaminophen"), (m.Name, m.Synonyms));
        Assert.Equal("severe hepatic impairment", m.Sections["Contra-indications"]);
        var dose = Assert.Single(m.Doses);
        Assert.Equal((1m, 4m), (dose.MaxSingle!.Value, dose.MaxDaily!.Value)); // the continuation line carries the maximum
        Assert.Equal(2, m.Preparations.Count);
        Assert.Equal(("tablet", "500 mg", "full subsidy (restrictions)"), (m.Preparations[0].Form, m.Preparations[0].Strength, m.Preparations[0].Subsidy));
        Assert.Equal(("injection: intravenous infusion", "1 g/100 mL"), (m.Preparations[1].Form, m.Preparations[1].Strength));
    }

    /// <summary>A small PDF laid out like the NZF (standard fonts; bold = Helvetica-Bold) with 60 monographs.</summary>
    private static byte[] FakeNzfPdf(bool children = false, int count = 60)
    {
        var b = new PdfDocumentBuilder();
        var reg = b.AddStandard14Font(Standard14Font.Helvetica);
        var bold = b.AddStandard14Font(Standard14Font.HelveticaBold);
        PdfPageBuilder page = b.AddPage(595, 842);
        double y = 800;
        void W(double x, double size, bool isBold, string text)
        {
            if (y < 40) { page = b.AddPage(595, 842); y = 800; }
            page.AddText(text, size, new PdfPoint(x, y), isBold ? bold : reg);
            y -= size + 4;
        }
        W(28.3, 7.2, false, $"New Zealand Formulary{(children ? " for Children" : "")} release 173 - 1 November 2026");
        string[] names = ["paracetamol", "ondansetron", .. Enumerable.Range(1, count - 2).Select(i => $"testdrug{i:D2}")];
        foreach (var n in names)
        {
            W(28.3, 13.5, true, n);
            W(28.3, 7.6, true, "Indications"); W(28.3, 7.2, false, "pain");
            W(28.3, 7.6, true, "Cautions"); W(28.3, 7.2, false, "hepatic impairment");
            W(28.3, 7.6, true, "Dosing regimen");
            W(33.0, 8.9, true, "Pain");
            W(39.7, 8.0, true, "Intravenous infusion");
            if (children) W(48.2, 7.6, true, "Child 1 month-18 years 15 mg/kg every 4 hours; maximum 60 mg/kg per day");
            else W(48.2, 7.6, true, n == "ondansetron" ? "Adult 4 mg every 8 hours; maximum 16 mg daily" : "Adult, body-weight over 50 kg 1 g every 4-6 hours; maximum 4 g daily");
            W(28.3, 7.6, true, "Patient advice"); W(34.7, 7.2, false, "Ask your pharmacist.");
            W(34.2, 6.8, true, n); W(42.7, 6.8, true, "tablet"); W(44.4, 6.3, true, "500 mg");
        }
        return b.Build();
    }

    [Fact]
    public async Task Import_reads_the_pdf_adds_names_gives_hints_and_suggests_rules()
    {
        var pdf = FakeNzfPdf();
        var lines = NzfPdf.ReadLines(pdf);
        Assert.Contains(lines, l => l.Bold && l.Text == "paracetamol" && l.Size > 13);
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var nzf = s.ServiceProvider.GetRequiredService<NzfService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => nzf.ImportAsync("x.pdf", "not a pdf"u8.ToArray(), "admin"));
        var r = await nzf.ImportAsync("nzf.pdf", pdf, "admin");
        Assert.Equal(("NZF", 173, 60, false), (r.Edition, r.Release!.Value, r.Monographs, r.AlreadyImported));
        Assert.True(r.IngredientsAdded >= 60);
        Assert.True((await nzf.ImportAsync("nzf.pdf", pdf, "admin")).AlreadyImported);
        Assert.False(await nzf.DueAsync("NZF", new DateOnly(2026, 11, 20)));
        Assert.True(await nzf.DueAsync("NZF", new DateOnly(2026, 12, 1)));
        Assert.False(await nzf.DueAsync("NZF", new DateOnly(2027, 1, 1))); // before the first working day

        var found = await nzf.ForIngredientAsync("paracetamol");
        var mono = (await nzf.MonographAsync(Assert.Single(found).Id))!.Value.Monograph;
        Assert.Equal("hepatic impairment", mono.Cautions);
        Assert.Single(mono.Doses);
        var hints = await nzf.HintsAsync("paracetamol", "IV", 45, 80, 2);
        Assert.Contains("maximum 4 g daily", Assert.Single(hints).Text, StringComparison.Ordinal);
        Assert.Empty(await nzf.HintsAsync("paracetamol", "Oral", 45, 80, 2)); // no oral line in this file
        Assert.Empty(await nzf.HintsAsync("paracetamol", "IV", 45, 40, 2)); // the line is for over 50 kg

        // Children's edition is kept alongside; hints for a child come from it.
        var rc = await nzf.ImportAsync("nzfc.pdf", FakeNzfPdf(children: true), "admin");
        Assert.Equal("NZFC", rc.Edition);
        Assert.Contains("15 mg/kg", Assert.Single(await nzf.HintsAsync("paracetamol", "IV", 8, 25, 2)).Text, StringComparison.Ordinal);

        var sug = await nzf.SuggestAsync(["paracetamol", "ondansetron"], children: true);
        var para = sug.Single(x => x.Rule.Ingredient == "paracetamol" && x.Edition == "NZF").Rule;
        Assert.Equal(("IV", 18, (decimal?)50, "mg", 1000m, 4000m, 4m, CheckSeverity.Stop), (para.Route, para.MinAgeYears!.Value, para.MinWeightKg, para.Unit, para.MaxSingle!.Value, para.MaxDaily!.Value, para.MinIntervalHours!.Value, para.Severity));
        Assert.Contains(sug, x => x.Edition == "NZFC" && x.Rule.MaxSinglePerKg == 15 && x.Rule.MaxAgeYears == 18);
        Assert.Empty(MedicineRulesService.Validate(new MedRuleSetEdit("NZF", null, [.. sug.Select(x => x.Rule)], [], [], [])));

        // The check shows NZF dosing where TDS has no dose limit (information only).
        var safety = s.ServiceProvider.GetRequiredService<MedicineSafetyService>();
        var p = await h.PatientAsync();
        var res = await safety.CheckAsync(new SafetyRequest(p.Id, null, "Paracetamol", "1 g", "IV", h.Clock.GetUtcNow(), null, null, 80, null, "rn", false),
            new PatientFacts(45, 80, "entered", [], [], null));
        Assert.Contains(res.Findings, f => f.Severity == CheckSeverity.Info && f.RuleRef == "nzf:paracetamol" && f.Text.Contains("NZF (release 173)", StringComparison.Ordinal));
        Assert.Contains("paracetamol", res.Ingredients!);
    }

    [Fact]
    public void Weight_banded_dose_rules_need_the_weight()
    {
        var rules = new MedRuleSet();
        rules.DoseRules.Add(new DoseRule { Code = "P-50", Ingredient = "paracetamol", MinAgeYears = 18, MinWeightKg = 50, Unit = "g", MaxSingle = 1, Severity = CheckSeverity.Stop });
        rules.DoseRules.Add(new DoseRule { Code = "P-U50", Ingredient = "paracetamol", MinAgeYears = 18, MaxWeightKg = 50, Unit = "mg", MaxSinglePerKg = 15, Severity = CheckSeverity.Stop });
        var index = new TermIndex([("paracetamol", "paracetamol")]);
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
        var at = DateTimeOffset.UtcNow;
        CheckContext C(decimal? kg) => new(40, kg, [], [], [], 60, tz);
        var p = new ProposedMedicine("Paracetamol", "1 g", "IV", [new IngredientAmount("paracetamol", 1000, "mg")], at, null);
        Assert.DoesNotContain(MedicineChecks.Check(p, C(70), rules, index), f => f.Kind == FindingKind.Dose);
        Assert.Contains(MedicineChecks.Check(p, C(40), rules, index), f => f.Kind == FindingKind.Dose && f.RuleRef == "P-U50" && f.Text.Contains("under 50 kg", StringComparison.Ordinal));
        Assert.Contains(MedicineChecks.Check(p, C(null), rules, index), f => f.Kind == FindingKind.NotChecked && f.Text.Contains("body-weight", StringComparison.Ordinal));
    }
}
