using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Medicines;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Tests;

/// <summary>0.27.0 medicines and medicine safety: dose reading, the checks, rules governance, the medicines list import and the eMR hook.</summary>
public class MedicinesTests
{
    private static readonly TimeZoneInfo Nz = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
    private static readonly DateTimeOffset At = new(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(13));
    private static readonly MedRuleSet Rules = MedicineExamples.ExampleRuleSet(1, "t", At);
    private static readonly TermIndex Index = new(MedicineExamples.Medicines.SelectMany(m => m.Ingredients).Select(i => (i.Ingredient, i.Ingredient))
        .Concat(MedicineExamples.ExtraIngredients.Select(i => (i, i)))
        .Concat(MedicineExamples.Aliases.SelectMany(kv => kv.Value.Select(a => (a, kv.Key)))));

    private static ProposedMedicine P(string text, string dose, string route, params (string Ing, decimal? Mg)[] ings) =>
        new(text, dose, route, [.. ings.Select(i => new IngredientAmount(i.Ing, i.Mg, i.Mg is null ? null : "mg"))], At, null);
    private static CheckContext C(int age = 45, decimal? kg = 80, IReadOnlyList<PatientAllergy>? allergies = null, IReadOnlyList<GivenBefore>? given = null, IReadOnlyList<RegularMedicine>? regular = null) =>
        new(age, kg, allergies ?? [], given ?? [], regular ?? [], 60, Nz);

    [Fact]
    public void Doses_are_read_as_written()
    {
        Assert.Equal((1m, "g"), DoseParser.Parse("1 g"));
        Assert.Equal((500m, "mg"), DoseParser.Parse("500mg"));
        Assert.Equal((50m, "microgram"), DoseParser.Parse("50 mcg"));
        Assert.Equal((2m, "each"), DoseParser.Parse("1-2 tablets"));
        Assert.Equal((7.5m, "mL"), DoseParser.Parse("7,5 ml"));
        Assert.Equal((5000m, "unit"), DoseParser.Parse("5000 units"));
        Assert.Equal((null, null), DoseParser.Parse("as needed"));
        Assert.Equal("1 g", DoseParser.Format(1000, "mg"));
        Assert.Equal("50 microgram", DoseParser.Format(0.05m, "mg"));

        var tab = new List<(string, decimal?, string?, decimal?)> { ("paracetamol", 500, "mg", null) };
        Assert.Equal(1000m, MedicineChecks.Amounts(tab, "2 tablets").Amounts[0].Amount);
        Assert.Equal(1000m, MedicineChecks.Amounts(tab, "1 g").Amounts[0].Amount);
        Assert.NotNull(MedicineChecks.Amounts(tab, "2").Problem); // no unit – not guessed
        var liquid = new List<(string, decimal?, string?, decimal?)> { ("paracetamol", 120, "mg", 5) };
        Assert.Equal(240m, MedicineChecks.Amounts(liquid, "10 mL").Amounts[0].Amount);
        var fent = new List<(string, decimal?, string?, decimal?)> { ("fentanyl", 50, "microgram", 1) };
        Assert.Equal(0.1m, MedicineChecks.Amounts(fent, "2 mL").Amounts[0].Amount);
        var combo = new List<(string, decimal?, string?, decimal?)> { ("amoxicillin", 500, "mg", null), ("clavulanic acid", 125, "mg", null) };
        var two = MedicineChecks.Amounts(combo, "2 tablets");
        Assert.Equal([1000m, 250m], two.Amounts.Select(a => a.Amount!.Value));
        Assert.NotNull(MedicineChecks.Amounts(combo, "1 g").Problem); // a mass for a combination is ambiguous

        Assert.Equal("IV", Routes.Normal("intravenous"));
        Assert.Equal("Oral", Routes.Normal("PO"));
        Assert.Equal("Infiltration", Routes.Normal("local infiltration"));
    }

    [Fact]
    public void Free_text_is_read_by_ingredient_and_other_names()
    {
        Assert.Equal(["lidocaine"], Index.Find("Allergic to LIGNOCAINE (rash)"));
        Assert.Equal(["warfarin", "escitalopram"], Index.Find("Marevan 3mg daily, Lexapro 10 mg"));
        Assert.Equal(["benzylpenicillin"], Index.Find("penicillin G – anaphylaxis"));
        Assert.Empty(Index.Find("sticking plaster"));
        Assert.Equal("09348223459122", MedicineCatalogue.NormaliseGtin("9348223459122")); // GTIN-13 padded to 14
        Assert.Null(MedicineCatalogue.NormaliseGtin("9348223459121")); // wrong check digit
        Assert.Equal("09348223459122", MedicineCatalogue.NormaliseGtin("(01)09348223459122"));
        Assert.Equal("09348223459122", MedicineCatalogue.NormaliseGtin("010934822345912217270101")); // GS1 DataMatrix: GTIN then expiry
    }

    [Fact]
    public void Allergies_are_checked_by_name_even_without_rules()
    {
        var f = MedicineChecks.Check(P("Augmentin", "1 tablet", "Oral", ("amoxicillin", 500), ("clavulanic acid", 125)), C(allergies: [new("Amoxil – hives", null)]), null, Index);
        Assert.Contains(f, x => x.Kind == FindingKind.Allergy && x.Severity == CheckSeverity.Stop && x.Text.Contains("amoxicillin", StringComparison.Ordinal));
        Assert.Contains(f, x => x.Kind == FindingKind.NotChecked && x.Text.Contains("no approved medicine rules", StringComparison.Ordinal));
        // An allergy linked to an ingredient counts even when the text says something else.
        var linked = MedicineChecks.Check(P("Cefazolin 2 g", "2 g", "IV", ("cefazolin", 2000)), C(allergies: [new("antibiotic reaction 2019", "cefazolin")]), null, Index);
        Assert.Contains(linked, x => x.Kind == FindingKind.Allergy && x.Severity == CheckSeverity.Stop);
        // An allergy that matches nothing is reported as not checked.
        var unknown = MedicineChecks.Check(P("Paracetamol", "1 g", "Oral", ("paracetamol", 1000)), C(allergies: [new("Elastoplast", null)]), null, Index);
        Assert.Contains(unknown, x => x.Kind == FindingKind.NotChecked && x.Text.Contains("Elastoplast", StringComparison.Ordinal));
    }

    [Fact]
    public void Allergy_groups_and_cross_reactions()
    {
        var pen = C(allergies: [new("Penicillin – anaphylaxis 2015", null)]);
        var amox = MedicineChecks.Check(P("Amoxicillin 500 mg", "500 mg", "Oral", ("amoxicillin", 500)), pen, Rules, Index);
        Assert.Contains(amox, x => x.Kind == FindingKind.Allergy && x.Severity == CheckSeverity.Stop && x.Text.Contains("penicillins", StringComparison.Ordinal));
        var cef = MedicineChecks.Check(P("Cefazolin 2 g", "2 g", "IV", ("cefazolin", 2000)), pen, Rules, Index);
        Assert.Contains(cef, x => x.Kind == FindingKind.Allergy && x.Severity == CheckSeverity.Warn && x.Text.StartsWith("Possible cross-reaction", StringComparison.Ordinal));
        Assert.DoesNotContain(cef, x => x.Severity == CheckSeverity.Stop);
        // Opioid "allergy" (often intolerance) is a warning for another opioid, a stop for the same one.
        var codeine = C(allergies: [new("codeine – vomiting", null)]);
        Assert.Contains(MedicineChecks.Check(P("Fentanyl", "50 microgram", "IV", ("fentanyl", 0.05m)), codeine, Rules, Index), x => x.Kind == FindingKind.Allergy && x.Severity == CheckSeverity.Warn);
        Assert.Contains(MedicineChecks.Check(P("Codeine", "30 mg", "Oral", ("codeine", 30)), codeine, Rules, Index), x => x.Kind == FindingKind.Allergy && x.Severity == CheckSeverity.Stop);
        // A group linked by code.
        Assert.Contains(MedicineChecks.Check(P("Ibuprofen", "400 mg", "Oral", ("ibuprofen", 400)), C(allergies: [new("asthma with anti-inflammatories", "group:NSAID")]), Rules, Index),
            x => x.Kind == FindingKind.Allergy && x.Severity == CheckSeverity.Stop);
    }

    [Fact]
    public void Dose_limits_single_daily_weight_and_interval()
    {
        var two = MedicineChecks.Check(P("Paracetamol", "2 g", "Oral", ("paracetamol", 2000)), C(), Rules, Index);
        Assert.Contains(two, x => x.Kind == FindingKind.Dose && x.Severity == CheckSeverity.Stop && x.Text.Contains("maximum single dose of 1 g", StringComparison.Ordinal));
        Assert.DoesNotContain(MedicineChecks.Check(P("Paracetamol", "1 g", "Oral", ("paracetamol", 1000)), C(), Rules, Index), x => x.Severity > CheckSeverity.Info);

        // 3 × 1 g earlier today (every 5 hours) + 1 g now = 4 g: allowed; a fifth gram is not.
        var earlier = new[] { 15, 10, 5 }.Select(h => new GivenBefore("Paracetamol 1 g", "paracetamol", 1000, "mg", At.AddHours(-h))).ToList();
        Assert.DoesNotContain(MedicineChecks.Check(P("Paracetamol", "1 g", "Oral", ("paracetamol", 1000)), C(given: earlier), Rules, Index), x => x.Kind == FindingKind.Dose);
        var fifth = earlier.Append(new GivenBefore("Paracetamol 1 g", "paracetamol", 1000, "mg", At.AddHours(-4.5))).ToList();
        Assert.Contains(MedicineChecks.Check(P("Paracetamol", "1 g", "Oral", ("paracetamol", 1000)), C(given: fifth), Rules, Index), x => x.Text.StartsWith("Too much in 24 hours: 5 g", StringComparison.Ordinal));
        // Doses more than 24 hours ago do not count.
        var old = new[] { 30, 28, 26, 25 }.Select(h => new GivenBefore("Paracetamol 1 g", "paracetamol", 1000, "mg", At.AddHours(-h))).ToList();
        Assert.DoesNotContain(MedicineChecks.Check(P("Paracetamol", "1 g", "Oral", ("paracetamol", 1000)), C(given: old), Rules, Index), x => x.Kind == FindingKind.Dose);

        // Too soon.
        var soon = MedicineChecks.Check(P("Paracetamol", "1 g", "IV", ("paracetamol", 1000)), C(given: [new("Paracetamol 1 g", "paracetamol", 1000, "mg", At.AddHours(-2))]), Rules, Index);
        Assert.Contains(soon, x => x.Kind == FindingKind.Duplicate && x.Text.StartsWith("Too soon", StringComparison.Ordinal) && x.Text.Contains("08:00", StringComparison.Ordinal));

        // Child: per-kg limits; weight needed.
        var child = MedicineChecks.Check(P("Paracetamol", "500 mg", "Oral", ("paracetamol", 500)), C(age: 8, kg: 25), Rules, Index);
        Assert.Contains(child, x => x.Text.StartsWith("Dose too high for weight: 500 mg paracetamol is 20 mg/kg for 25 kg", StringComparison.Ordinal));
        Assert.Contains(MedicineChecks.Check(P("Paracetamol", "250 mg", "Oral", ("paracetamol", 250)), C(age: 8, kg: null), Rules, Index), x => x.Severity == CheckSeverity.Warn && x.Text.StartsWith("Weight not known", StringComparison.Ordinal));
        Assert.DoesNotContain(MedicineChecks.Check(P("Paracetamol", "375 mg", "Oral", ("paracetamol", 375)), C(age: 8, kg: 25), Rules, Index), x => x.Kind == FindingKind.Dose);

        // Route-specific rules only apply on that route; dose not understood is said.
        Assert.DoesNotContain(MedicineChecks.Check(P("Morphine", "15 mg", "Oral", ("morphine", 15)), C(), Rules, Index), x => x.Kind == FindingKind.Dose);
        Assert.Contains(MedicineChecks.Check(P("Morphine", "15 mg", "IV", ("morphine", 15)), C(), Rules, Index), x => x.Kind == FindingKind.Dose && x.Severity == CheckSeverity.Warn);
        var unk = MedicineChecks.Check(new ProposedMedicine("Paracetamol", "two", "Oral", [new IngredientAmount("paracetamol", null, null)], At, "no number found"), C(), Rules, Index);
        Assert.Contains(unk, x => x.Kind == FindingKind.NotChecked && x.Text.StartsWith("Dose \"two\" could not be worked out", StringComparison.Ordinal));
        // Age bands: over 70 midazolam limit.
        Assert.Contains(MedicineChecks.Check(P("Midazolam", "3 mg", "IV", ("midazolam", 3)), C(age: 82), Rules, Index), x => x.RuleRef == "MIDAZ-70");
        Assert.DoesNotContain(MedicineChecks.Check(P("Midazolam", "3 mg", "IV", ("midazolam", 3)), C(age: 50), Rules, Index), x => x.Kind == FindingKind.Dose);
    }

    [Fact]
    public void Interactions_with_given_and_regular_medicines()
    {
        var reg = MedicineChecks.Check(P("Tramadol", "50 mg", "Oral", ("tramadol", 50)), C(regular: [new("citalopram", "regular medicine in the questionnaire")]), Rules, Index);
        Assert.Contains(reg, x => x.Kind == FindingKind.Interaction && x.RuleRef == "TRAM-SSRI" && x.Text.Contains("questionnaire", StringComparison.Ordinal));
        var qt = MedicineChecks.Check(P("Droperidol", "0.625 mg", "IV", ("droperidol", 0.625m)), C(given: [new("Ondansetron 4 mg", "ondansetron", 4, "mg", At.AddMinutes(-40))]), Rules, Index);
        Assert.Contains(qt, x => x.RuleRef == "QT-QT" && x.Text.Contains("given at 09:20", StringComparison.Ordinal));
        var maoi = MedicineChecks.Check(P("Pethidine", "50 mg", "IM", ("pethidine", 50)), C(regular: [new("moclobemide", "regular")]), Rules, Index);
        Assert.Contains(maoi, x => x.Severity == CheckSeverity.Stop && x.RuleRef == "PETH-MAOI");
        // The same medicine again is not an "interaction"; a different medicine without a rule is clear.
        Assert.DoesNotContain(MedicineChecks.Check(P("Ondansetron", "4 mg", "IV", ("ondansetron", 4)), C(given: [new("Ondansetron 4 mg", "ondansetron", 4, "mg", At.AddHours(-5))]), Rules, Index), x => x.Kind == FindingKind.Interaction);
        Assert.DoesNotContain(MedicineChecks.Check(P("Cefazolin", "2 g", "IV", ("cefazolin", 2000)), C(regular: [new("paracetamol", "regular")]), Rules, Index), x => x.Severity > CheckSeverity.Info);
        // A possible double dose of a medicine with no interval rule.
        Assert.Contains(MedicineChecks.Check(P("Dexamethasone", "4 mg", "IV", ("dexamethasone", 4)), C(given: [new("Dexamethasone 4 mg", "dexamethasone", 4, "mg", At.AddMinutes(-20))]), Rules, Index),
            x => x.Kind == FindingKind.Duplicate && x.Text.Contains("double dose", StringComparison.Ordinal));
        // Not in the list at all.
        Assert.Contains(MedicineChecks.Check(new ProposedMedicine("Mystery drops", "2 drops", "Eye", [], At, null), C(), Rules, Index), x => x.Kind == FindingKind.NotChecked && x.Severity == CheckSeverity.Warn);
    }

    [Fact]
    public void Example_rules_are_valid_and_rule_validation_explains_problems()
    {
        Assert.Empty(MedicineRulesService.Validate(MedicineRulesService.ToEdit(Rules)));
        var bad = new MedRuleSetEdit("x", null,
            [new DoseRuleEdit("A", "paracetamol", null, 16, 10, "ml", null, null, null, null, null, CheckSeverity.Info, null, null), new DoseRuleEdit("A", "ibuprofen", null, null, null, "mg", 800, 400, null, null, null, CheckSeverity.Stop, null, null)],
            [new GroupEdit("G", "Group", true, CheckSeverity.Stop, [], [])],
            [new CrossReactionEdit("G", "H", CheckSeverity.Warn, null)],
            [new InteractionEdit("I", "group:ZZ", "tramadol", CheckSeverity.Warn, "", null, null), new InteractionEdit("J", "tramadol", "tramadol", CheckSeverity.Warn, "x", null, null)]);
        var e = MedicineRulesService.Validate(bad);
        foreach (var expected in new[] { "used twice", "unit must be", "give at least one limit", "age band is empty", "must be Stop or Warn", "daily maximum is less", "at least one ingredient", "both groups", "no group ZZ", "say what the effect", "cannot interact with itself" })
            Assert.Contains(e, x => x.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rules_are_versioned_and_need_approval_and_a_note()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var rules = s.ServiceProvider.GetRequiredService<MedicineRulesService>();
        var safety = s.ServiceProvider.GetRequiredService<MedicineSafetyService>();
        var draft = await rules.SeedExampleAsync("anaes");
        Assert.NotNull(draft);
        Assert.Null(await rules.SeedExampleAsync("anaes"));
        Assert.Contains("Approve a medicine rule set", (await safety.SaveSettingsAsync(new(CheckMode.Enforce, true, 60), "anaes")).Single());
        Assert.Contains("approval note", await rules.ApproveAsync(draft!.Id, "Dr Ford", "Anaesthetist", " "));
        Assert.Null(await rules.ApproveAsync(draft.Id, "Dr Ford", "Anaesthetist", "Clinical governance 12 Nov 2026 – checked against NZF"));
        var v1 = await rules.ApprovedAsync();
        Assert.Equal(1, v1!.Version);
        Assert.DoesNotContain("not yet clinically approved", v1.Name, StringComparison.Ordinal);
        Assert.Contains("Only a draft", (await rules.SaveDraftAsync(v1.Id, MedicineRulesService.ToEdit(v1), "anaes")).Single());

        // Edit a copy: drop paracetamol's daily limit to 3 g, approve – v1 retires.
        var (copyId, err) = await rules.CopyAsync(v1.Id, "anaes");
        Assert.Null(err);
        var edit = MedicineRulesService.ToEdit((await rules.GetAsync(copyId!.Value))!);
        edit = edit with { DoseRules = [.. edit.DoseRules.Select(r => r.Code == "PARA-A" ? r with { MaxDaily = 3000 } : r)] };
        Assert.Empty(await rules.SaveDraftAsync(copyId.Value, edit, "anaes"));
        Assert.Equal(3000m, (await rules.GetAsync(copyId.Value))!.DoseRules.Single(r => r.Code == "PARA-A").MaxDaily);
        Assert.Equal(Rules.Groups.Single(g => g.Code == "PEN").Members.Count, (await rules.GetAsync(copyId.Value))!.Groups.Single(g => g.Code == "PEN").Members.Count);
        Assert.Null(await rules.ApproveAsync(copyId.Value, "Dr Ford", "Anaesthetist", "PARA daily 3 g"));
        var list = await rules.ListAsync();
        Assert.Equal(MedRuleSetStatus.Retired, list.Single(r => r.Version == 1).Status);
        Assert.Equal(MedRuleSetStatus.Approved, list.Single(r => r.Version == 2).Status);
        Assert.Contains("Only a draft", await rules.DeleteDraftAsync(copyId.Value, "anaes"));
    }

    private sealed class TestGate(MedicineSafetyService safety, PatientsDbContext patients, EmrDbContext emr) : IMedicineSafetyCheck
    {
        public async Task<MedicineCheckOutcome> CheckAsync(MedicineGiveCheck c, CancellationToken ct)
        {
            var p = await patients.Patients.AsNoTracking().Include(x => x.Alerts).SingleAsync(x => x.Id == c.PatientId, ct);
            var given = await emr.Medications.AsNoTracking().Where(m => m.EpisodeId == c.EpisodeId).ToListAsync(ct);
            var facts = new PatientFacts(BrightPAS.Core.NzTime.Age(p.DateOfBirth, BrightPAS.Core.NzTime.NzDate(c.At)), 70, "test", [.. p.Alerts.Where(a => a.IsActive && a.Type == AlertType.Allergy).Select(a => (a.Id, a.Description))],
                [.. given.Select(g => (g.Medicine, g.Dose, g.Route, g.GivenAt))], "Lexapro 10 mg");
            var r = await safety.CheckAsync(new SafetyRequest(c.PatientId, c.EpisodeId, c.Medicine, c.Dose, c.Route, c.At, c.Barcode, c.MedicineId, c.WeightKg, c.OverrideReason, c.User, true), facts, ct);
            return new(r.Allowed, r.Error, r.CheckId, [.. r.Findings.Select(x => new MedicineCheckNote(x.Kind.ToString(), x.Severity.ToString(), x.Text))]);
        }

        public Task LinkAsync(Guid checkId, Guid medicationId, CancellationToken ct) => safety.LinkCheckAsync(checkId, medicationId, ct);
    }

    [Fact]
    public async Task Charting_a_medicine_runs_the_checks_and_enforce_needs_a_reason()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var alert = await sp.GetRequiredService<PatientService>().AddAlertAsync(p.Id, AlertType.Allergy, AlertSeverity.Critical, "Penicillin – anaphylaxis", "rn");
        var cat = sp.GetRequiredService<MedicineCatalogue>();
        Assert.True(await cat.LoadStarterAsync("admin") > 40);
        Assert.Equal(0, await cat.LoadStarterAsync("admin")); // only once
        var rules = sp.GetRequiredService<MedicineRulesService>();
        var draft = await rules.SeedExampleAsync("anaes");
        Assert.Null(await rules.ApproveAsync(draft!.Id, "Dr Ford", "Anaesthetist", "Test approval"));
        var safety = sp.GetRequiredService<MedicineSafetyService>();
        var gate = new TestGate(safety, sp.GetRequiredService<PatientsDbContext>(), sp.GetRequiredService<EmrDbContext>());
        var emr = ActivatorUtilities.CreateInstance<EmrService>(sp, new object[] { new IMedicineSafetyCheck[] { gate } });
        var e = await emr.EpisodeAsync(Guid.NewGuid(), p.Id, "rn");

        // Advise (default): saved, with the warnings returned.
        var (m1, err1, f1) = await emr.GiveCheckedAsync(e.Id, new("Cefazolin 2 g injection", "2 g", "IV", null, null, null, null, null), "rn");
        Assert.Null(err1);
        Assert.Contains(f1, x => x.Kind == "Allergy" && x.Severity == "Warn");

        // Enforce: a Stop needs a reason; with a reason it is saved and the override recorded against the medicine.
        Assert.Empty(await safety.SaveSettingsAsync(new(CheckMode.Enforce, true, 60), "Dr Ford"));
        var (m2, err2, f2) = await emr.GiveCheckedAsync(e.Id, new("Amoxicillin 500 mg capsule", "1 capsule", "Oral", null, null, null, null, null), "rn");
        Assert.Null(m2);
        Assert.StartsWith("Stop – Allergy", err2, StringComparison.Ordinal);
        Assert.Contains(f2, x => x.Severity == "Stop");
        var (m3, err3, _) = await emr.GiveCheckedAsync(e.Id, new("Amoxicillin 500 mg capsule", "1 capsule", "Oral", null, null, null, null, null, OverrideReason: "Tolerated amoxicillin 2024 – allergy service letter on file"), "rn");
        Assert.Null(err3);
        // Tramadol with escitalopram from the questionnaire's medicines list (by brand name).
        var (_, _, f4) = await emr.GiveCheckedAsync(e.Id, new("Tramadol 50 mg capsule", "2 capsules", "Oral", null, null, null, null, null), "rn");
        Assert.Contains(f4, x => x.Kind == "Interaction" && x.Text.Contains("escitalopram", StringComparison.Ordinal));

        var checks = await safety.ChecksAsync(p.Id, null, null, null, null, 50);
        Assert.Equal(4, checks.Count);
        Assert.Contains(checks, c => c.Result == CheckResult.Stopped && c.Medicine.StartsWith("Amoxicillin", StringComparison.Ordinal));
        var over = checks.Single(c => c.Result == CheckResult.Overridden);
        Assert.Contains("allergy service", over.OverrideReason, StringComparison.Ordinal);
        var db = sp.GetRequiredService<MedicinesDbContext>();
        Assert.Equal(m3!.Id, (await db.Checks.AsNoTracking().SingleAsync(c => c.Id == over.Id)).MedicationId);
        Assert.Equal(m1!.Id, (await db.Checks.AsNoTracking().SingleAsync(c => c.Result == CheckResult.Warnings && c.MedicineText.StartsWith("Cefazolin"))).MedicationId);
        var (byResult, byKind) = await safety.SummaryAsync(At.AddDays(-30), At.AddDays(30));
        Assert.Equal(1, byResult[CheckResult.Overridden]);
        Assert.True(byKind[FindingKind.Allergy] >= 2);

        // Allergy links: unknown terms refused; linking to a group works.
        Assert.Contains("not an ingredient", await safety.LinkAsync(alert.Id, p.Id, "unobtainium", "rn"));
        Assert.Null(await safety.LinkAsync(alert.Id, p.Id, "group:pen", "rn"));
        Assert.Equal("group:PEN", (await safety.LinksAsync(p.Id)).Single().Term);
        Assert.Null(await safety.LinkAsync(alert.Id, p.Id, "Amoxil", "rn"));
        Assert.Equal("amoxicillin", (await safety.LinksAsync(p.Id)).Single().Term);

        // Off: no checks at all.
        Assert.Empty(await safety.SaveSettingsAsync(new(CheckMode.Off, true, 60), "Dr Ford"));
        var (_, err5, f5) = await emr.GiveCheckedAsync(e.Id, new("Amoxicillin 500 mg capsule", "1 capsule", "Oral", null, null, null, null, null), "rn");
        Assert.Null(err5);
        Assert.Empty(f5);
    }

    [Fact]
    public async Task Medicines_list_import_with_a_saved_layout_updates_and_retires()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var cat = s.ServiceProvider.GetRequiredService<MedicineCatalogue>();
        const string month1 = "MPUU_ID|MPUU_TERM|SUBSTANCE|STRENGTH|GTIN\n" +
            "10001|Paracetamol 500 mg tablet|paracetamol|500 mg|09348223459122\n" +
            "10002|Paracetamol 120 mg/5 mL oral liquid|paracetamol|120 mg/5 mL|\n" +
            "10003|Codeine with paracetamol 8 mg/500 mg tablet|codeine;paracetamol|8;500 mg|\n" +
            "10004|\"Ondansetron 4 mg tablet, orally disintegrating\"|ondansetron|4 mg|\n" +
            "10005|Oldmed 10 mg tablet|oldmedine|10 mg|\n";
        var (headers, sample, d) = MedicineCatalogue.Peek(month1);
        Assert.Equal('|', d);
        Assert.Equal(["MPUU_ID", "MPUU_TERM", "SUBSTANCE", "STRENGTH", "GTIN"], headers);
        Assert.Equal("Ondansetron 4 mg tablet, orally disintegrating", sample[3][1]);
        var map = new ImportMapping("NZULM MPUU", "NZULM", "MPUU_TERM", "MPUU_ID", null, null, "SUBSTANCE", "STRENGTH", null, null, "GTIN", true, true);
        var (run, errors) = await cat.ImportAsync("nzulm_2026_11.csv", month1, map, "admin");
        Assert.Empty(errors);
        Assert.Equal((5, 5, 0, 0), (run!.Rows, run.Added, run.Updated, run.Deactivated));
        var liquid = (await cat.SearchAsync("oral liquid", false, false, 10)).Single();
        Assert.Contains("paracetamol 120 mg/5 mL", liquid.Ingredients, StringComparison.Ordinal);
        Assert.Equal("Paracetamol 500 mg tablet", (await cat.ByBarcodeAsync("9348223459122"))!.Name);
        var combo = (await cat.SearchAsync("codeine", false, false, 10)).Single();
        Assert.Contains("codeine 8 mg", combo.Ingredients, StringComparison.Ordinal);
        Assert.Contains("paracetamol 500 mg", combo.Ingredients, StringComparison.Ordinal);
        Assert.Single(await cat.ProfilesAsync());

        // Next month: 10001 renamed, 10005 gone → made inactive; profile reused.
        const string month2 = "MPUU_ID|MPUU_TERM|SUBSTANCE|STRENGTH|GTIN\n10001|Paracetamol 500 mg tablet (new name)|paracetamol|500 mg|09348223459122\n10002|Paracetamol 120 mg/5 mL oral liquid|paracetamol|120 mg/5 mL|\n10003|Codeine with paracetamol 8 mg/500 mg tablet|codeine;paracetamol|8;500 mg|\n10004|Ondansetron 4 mg tablet|ondansetron|4 mg|\n";
        var (run2, _) = await cat.ImportAsync("nzulm_2026_12.csv", month2, map with { SaveProfile = false }, "admin");
        Assert.Equal((0, 4, 1), (run2!.Added, run2.Updated, run2.Deactivated));
        Assert.Empty(await cat.SearchAsync("Oldmed", false, false, 10));
        Assert.Single(await cat.SearchAsync("Oldmed", false, true, 10));
        Assert.Single(await cat.SearchAsync("(new name)", false, false, 10));

        // Resolving by name for the checks.
        var (med, ings) = await cat.ResolveAsync("Codeine with paracetamol 8 mg/500 mg tablet", null, null);
        Assert.NotNull(med);
        Assert.Equal(2, ings.Count);
        var bad = await cat.ImportAsync("x.csv", month2, map with { NameColumn = "NOPE" }, "admin");
        Assert.Contains(bad.Errors, x => x.Contains("NOPE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TDS_medicines_are_validated_when_saved()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var cat = s.ServiceProvider.GetRequiredService<MedicineCatalogue>();
        var (_, errs) = await cat.SaveAsync(null, new MedicineEdit("", null, null, null, true, true, [new("paracetamol", 500, "mL", null)], ["123"]), "admin");
        Assert.Contains(errs, x => x.Contains("name", StringComparison.Ordinal));
        Assert.Contains(errs, x => x.Contains("choose the unit", StringComparison.Ordinal));
        Assert.Contains(errs, x => x.Contains("not a valid GTIN", StringComparison.Ordinal));
        var (id, ok) = await cat.SaveAsync(null, new MedicineEdit("Ketamine 10 mg/mL injection", null, "injection", "IV", true, true, [new("Ketamine", 10, "mg", 1)], ["09348223459122"]), "admin");
        Assert.Empty(ok);
        var (_, dup) = await cat.SaveAsync(null, new MedicineEdit("Other", null, null, null, true, true, [new("x", null, null, null)], ["9348223459122"]), "admin");
        Assert.Contains(dup, x => x.Contains("already belongs", StringComparison.Ordinal));
        Assert.Null(await cat.AddAliasAsync("ketamine", "Ketalar", "admin"));
        Assert.Contains("already", await cat.AddAliasAsync("ketamine", "ketalar", "admin"));
        Assert.Equal(["ketamine"], (await cat.TermIndexAsync()).Find("allergic to ketalar"));
        Assert.Empty((await cat.SaveAsync(id, new MedicineEdit("Ketamine 10 mg/mL injection", null, "injection", "IV", false, true, [new("ketamine", 10, "mg", 1)], []), "admin")).Errors);
        Assert.Null(await cat.ByBarcodeAsync("09348223459122"));
    }
}
