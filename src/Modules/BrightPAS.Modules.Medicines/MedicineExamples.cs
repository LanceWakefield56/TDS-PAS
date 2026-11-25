namespace BrightPAS.Modules.Medicines;

public sealed record ExampleIngredient(string Ingredient, decimal? Amount, string? Unit, decimal? PerMl = null);
public sealed record ExampleMedicine(string Name, string Form, string Route, IReadOnlyList<ExampleIngredient> Ingredients);

/// <summary>
/// BrightPAS's starter medicines list and EXAMPLE medicine rules for a NZ day-surgery unit. The rules are a starting point only: they are loaded
/// as a draft that TDS's clinicians must check line by line against current NZ references (NZ Formulary, Medsafe data sheets, ANZCA guidance)
/// and approve before patients are checked against them. Not clinically approved.
/// </summary>
public static class MedicineExamples
{
    private static ExampleMedicine M(string name, string form, string route, params ExampleIngredient[] i) => new(name, form, route, i);
    private static ExampleIngredient I(string ing, decimal? amount, string? unit, decimal? perMl = null) => new(ing, amount, unit, perMl);

    /// <summary>Common day-surgery and endoscopy medicines (strength per tablet/vial, or per mL for liquids).</summary>
    public static readonly IReadOnlyList<ExampleMedicine> Medicines =
    [
        M("Paracetamol 500 mg tablet", "tablet", "Oral", I("paracetamol", 500, "mg")),
        M("Paracetamol 1 g/100 mL infusion", "injection", "IV", I("paracetamol", 1000, "mg", 100)),
        M("Paracetamol 120 mg/5 mL oral liquid", "oral liquid", "Oral", I("paracetamol", 120, "mg", 5)),
        M("Ibuprofen 200 mg tablet", "tablet", "Oral", I("ibuprofen", 200, "mg")),
        M("Ibuprofen 400 mg tablet", "tablet", "Oral", I("ibuprofen", 400, "mg")),
        M("Diclofenac 50 mg tablet", "tablet", "Oral", I("diclofenac", 50, "mg")),
        M("Diclofenac 100 mg suppository", "suppository", "PR", I("diclofenac", 100, "mg")),
        M("Parecoxib 40 mg injection", "injection", "IV", I("parecoxib", 40, "mg")),
        M("Celecoxib 200 mg capsule", "capsule", "Oral", I("celecoxib", 200, "mg")),
        M("Codeine phosphate 30 mg tablet", "tablet", "Oral", I("codeine", 30, "mg")),
        M("Tramadol 50 mg capsule", "capsule", "Oral", I("tramadol", 50, "mg")),
        M("Tramadol 50 mg/mL injection", "injection", "IV", I("tramadol", 50, "mg", 1)),
        M("Oxycodone 5 mg capsule", "capsule", "Oral", I("oxycodone", 5, "mg")),
        M("Oxycodone 10 mg/mL injection", "injection", "IV", I("oxycodone", 10, "mg", 1)),
        M("Morphine 10 mg/mL injection", "injection", "IV", I("morphine", 10, "mg", 1)),
        M("Fentanyl 50 microgram/mL injection", "injection", "IV", I("fentanyl", 50, "microgram", 1)),
        M("Pethidine 50 mg/mL injection", "injection", "IM", I("pethidine", 50, "mg", 1)),
        M("Naloxone 400 microgram/mL injection", "injection", "IV", I("naloxone", 400, "microgram", 1)),
        M("Ondansetron 4 mg tablet", "tablet", "Oral", I("ondansetron", 4, "mg")),
        M("Ondansetron 2 mg/mL injection", "injection", "IV", I("ondansetron", 2, "mg", 1)),
        M("Droperidol 2.5 mg/mL injection", "injection", "IV", I("droperidol", 2.5m, "mg", 1)),
        M("Dexamethasone 4 mg/mL injection", "injection", "IV", I("dexamethasone", 4, "mg", 1)),
        M("Cyclizine 50 mg/mL injection", "injection", "IV", I("cyclizine", 50, "mg", 1)),
        M("Metoclopramide 10 mg tablet", "tablet", "Oral", I("metoclopramide", 10, "mg")),
        M("Metoclopramide 5 mg/mL injection", "injection", "IV", I("metoclopramide", 5, "mg", 1)),
        M("Cefazolin 1 g injection", "injection", "IV", I("cefazolin", 1000, "mg")),
        M("Cefazolin 2 g injection", "injection", "IV", I("cefazolin", 2000, "mg")),
        M("Amoxicillin 500 mg capsule", "capsule", "Oral", I("amoxicillin", 500, "mg")),
        M("Amoxicillin with clavulanic acid 500 mg/125 mg tablet", "tablet", "Oral", I("amoxicillin", 500, "mg"), I("clavulanic acid", 125, "mg")),
        M("Flucloxacillin 500 mg capsule", "capsule", "Oral", I("flucloxacillin", 500, "mg")),
        M("Clindamycin 150 mg capsule", "capsule", "Oral", I("clindamycin", 150, "mg")),
        M("Gentamicin 40 mg/mL injection", "injection", "IV", I("gentamicin", 40, "mg", 1)),
        M("Metronidazole 500 mg/100 mL infusion", "injection", "IV", I("metronidazole", 500, "mg", 100)),
        M("Midazolam 5 mg/5 mL injection", "injection", "IV", I("midazolam", 1, "mg", 1)),
        M("Propofol 10 mg/mL injection", "injection", "IV", I("propofol", 10, "mg", 1)),
        M("Lidocaine 1% injection (10 mg/mL)", "injection", "Infiltration", I("lidocaine", 10, "mg", 1)),
        M("Lidocaine 2% injection (20 mg/mL)", "injection", "Infiltration", I("lidocaine", 20, "mg", 1)),
        M("Bupivacaine 0.5% injection (5 mg/mL)", "injection", "Infiltration", I("bupivacaine", 5, "mg", 1)),
        M("Ropivacaine 0.75% injection (7.5 mg/mL)", "injection", "Infiltration", I("ropivacaine", 7.5m, "mg", 1)),
        M("Adrenaline 1 mg/mL injection", "injection", "IM", I("adrenaline", 1, "mg", 1)),
        M("Atropine 600 microgram/mL injection", "injection", "IV", I("atropine", 600, "microgram", 1)),
        M("Enoxaparin 40 mg/0.4 mL injection", "injection", "SC", I("enoxaparin", 100, "mg", 1)),
        M("Sugammadex 100 mg/mL injection", "injection", "IV", I("sugammadex", 100, "mg", 1)),
        M("Aspirin 100 mg tablet", "tablet", "Oral", I("aspirin", 100, "mg")),
    ];

    /// <summary>Other names for ingredients (brand, older and overseas names), used to read allergy alerts and the patient's own medicines list.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["paracetamol"] = ["panadol", "acetaminophen", "pamol", "paracare"],
        ["ibuprofen"] = ["nurofen", "brufen", "ibugesic"],
        ["diclofenac"] = ["voltaren", "diclax"],
        ["parecoxib"] = ["dynastat"],
        ["celecoxib"] = ["celebrex"],
        ["naproxen"] = ["naprosyn", "noflam"],
        ["codeine"] = ["codeine phosphate"],
        ["tramadol"] = ["tramal", "arrow tramadol"],
        ["oxycodone"] = ["oxynorm", "oxycontin"],
        ["pethidine"] = ["meperidine", "demerol"],
        ["ondansetron"] = ["zofran"],
        ["cyclizine"] = ["valoid", "nausicalm"],
        ["metoclopramide"] = ["maxolon"],
        ["cefazolin"] = ["cephazolin", "kefzol"],
        ["cefalexin"] = ["cephalexin", "keflex"],
        ["cefuroxime"] = ["zinnat"],
        ["amoxicillin"] = ["amoxycillin", "amoxil", "augmentin", "synermox", "curam"],
        ["flucloxacillin"] = ["floxapen", "staphlex", "flucil"],
        ["benzylpenicillin"] = ["penicillin g"],
        ["phenoxymethylpenicillin"] = ["penicillin v", "cilicaine vk"],
        ["sulfamethoxazole"] = ["bactrim", "co trimoxazole", "cotrimoxazole", "septrin"],
        ["clarithromycin"] = ["klacid"],
        ["erythromycin"] = ["erythrocin", "ery max"],
        ["azithromycin"] = ["zithromax"],
        ["midazolam"] = ["hypnovel"],
        ["diazepam"] = ["valium"],
        ["lorazepam"] = ["ativan"],
        ["temazepam"] = ["normison"],
        ["lidocaine"] = ["lignocaine", "xylocaine"],
        ["bupivacaine"] = ["marcain"],
        ["ropivacaine"] = ["naropin"],
        ["adrenaline"] = ["epinephrine"],
        ["enoxaparin"] = ["clexane"],
        ["warfarin"] = ["marevan", "coumadin"],
        ["aspirin"] = ["cartia", "disprin", "acetylsalicylic acid"],
        ["clopidogrel"] = ["plavix"],
        ["dabigatran"] = ["pradaxa"],
        ["rivaroxaban"] = ["xarelto"],
        ["apixaban"] = ["eliquis"],
        ["escitalopram"] = ["lexapro", "loxalate"],
        ["citalopram"] = ["celapram", "cipramil"],
        ["fluoxetine"] = ["prozac", "fluox"],
        ["sertraline"] = ["zoloft"],
        ["paroxetine"] = ["aropax"],
        ["venlafaxine"] = ["efexor", "enlafax"],
        ["moclobemide"] = ["aurorix"],
        ["haloperidol"] = ["serenace"],
        ["amiodarone"] = ["cordarone"],
        ["levodopa"] = ["sinemet", "madopar"],
        ["sugammadex"] = ["bridion"],
        ["dexamethasone"] = ["decadron"],
    };

    /// <summary>Ingredients used by the example rules that are not in the starter medicines list (so they can be found in allergy text and the patient's medicines).</summary>
    public static readonly IReadOnlyList<string> ExtraIngredients =
    [
        "naproxen", "mefenamic acid", "ketorolac", "etoricoxib", "hydromorphone", "tapentadol", "remifentanil", "alfentanil", "methadone",
        "cefalexin", "cefuroxime", "ceftriaxone", "cefoxitin", "ceftazidime", "cefaclor", "ampicillin", "benzylpenicillin", "phenoxymethylpenicillin", "piperacillin",
        "meropenem", "imipenem", "ertapenem", "sulfamethoxazole", "clarithromycin", "erythromycin", "azithromycin", "diazepam", "lorazepam", "temazepam",
        "clonazepam", "prilocaine", "levobupivacaine", "articaine", "warfarin", "dabigatran", "rivaroxaban", "apixaban", "heparin", "dalteparin",
        "clopidogrel", "ticagrelor", "prasugrel", "dipyridamole", "citalopram", "escitalopram", "fluoxetine", "sertraline", "paroxetine", "venlafaxine",
        "duloxetine", "moclobemide", "phenelzine", "tranylcypromine", "selegiline", "linezolid", "methylthioninium chloride", "haloperidol", "amiodarone",
        "quinine", "levodopa", "ethinylestradiol", "levonorgestrel", "norethisterone", "desogestrel", "etonogestrel", "medroxyprogesterone", "drospirenone",
    ];

    private static DoseRule D(string code, string ing, string? route, int? minAge, int? maxAge, string unit, decimal? single, decimal? daily, decimal? singleKg, decimal? dailyKg, decimal? interval, CheckSeverity sev, string? note) =>
        new() { Code = code, Ingredient = ing, Route = route, MinAgeYears = minAge, MaxAgeYears = maxAge, Unit = unit, MaxSingle = single, MaxDaily = daily, MaxSinglePerKg = singleKg, MaxDailyPerKg = dailyKg, MinIntervalHours = interval, Severity = sev, Note = note, Reference = "Example – check against the NZ Formulary / data sheet" };

    private static MedicineGroup G(string code, string name, bool allergy, CheckSeverity same, string[] members, string[] keywords)
    {
        var g = new MedicineGroup { Code = code, Name = name, AllergyClass = allergy, SameGroupSeverity = same };
        g.Members.AddRange(members.Select(m => new MedicineGroupMember { GroupId = g.Id, Term = m }));
        g.Members.AddRange(keywords.Select(k => new MedicineGroupMember { GroupId = g.Id, Term = k, IsKeyword = true }));
        return g;
    }

    private static InteractionRule X(string code, string a, string b, CheckSeverity sev, string effect, string? advice) =>
        new() { Code = code, TermA = a, TermB = b, Severity = sev, Effect = effect, Advice = advice, Reference = "Example – check against the NZ Formulary interactions" };

    /// <summary>The example rule set, as a draft. Adults are 16 and over.</summary>
    public static MedRuleSet ExampleRuleSet(int version, string user, DateTimeOffset at)
    {
        var rs = new MedRuleSet
        {
            Version = version,
            Name = "Example medicine rules (not yet clinically approved)",
            IsExample = true,
            Notes = "BrightPAS example rules for a day-surgery unit. Every dose limit, group and interaction must be checked by TDS's clinicians against current NZ references before approval.",
            CreatedBy = user,
            CreatedAt = at,
        };
        rs.DoseRules.AddRange(
        [
            D("PARA-A", "paracetamol", null, 16, null, "mg", 1000, 4000, null, null, 4, CheckSeverity.Stop, "Adults under 50 kg: 15 mg/kg."),
            D("PARA-C", "paracetamol", null, null, 16, "mg", 1000, 4000, 15, 60, 4, CheckSeverity.Stop, null),
            D("IBU-A", "ibuprofen", null, 16, null, "mg", 800, 2400, null, null, 6, CheckSeverity.Stop, null),
            D("IBU-C", "ibuprofen", null, null, 16, "mg", 400, 1200, 10, 30, 6, CheckSeverity.Stop, null),
            D("DICLO-A", "diclofenac", null, 16, null, "mg", 100, 150, null, null, 8, CheckSeverity.Stop, null),
            D("PAREC-A", "parecoxib", null, 16, null, "mg", 40, 80, null, null, 6, CheckSeverity.Stop, null),
            D("TRAM-A", "tramadol", null, 16, null, "mg", 100, 400, null, null, 4, CheckSeverity.Stop, "Over 75: maximum 300 mg a day."),
            D("TRAM-75", "tramadol", null, 75, null, "mg", null, 300, null, null, null, CheckSeverity.Warn, null),
            D("CODE-A", "codeine", null, 16, null, "mg", 60, 240, null, null, 4, CheckSeverity.Stop, null),
            D("MORPH-IV", "morphine", "IV", 16, null, "mg", 10, null, null, null, null, CheckSeverity.Warn, "Titrate in small increments."),
            D("OXY-PO", "oxycodone", "Oral", 16, null, "mg", 10, null, null, null, 4, CheckSeverity.Warn, null),
            D("FENT-IV", "fentanyl", "IV", 16, null, "microgram", 100, null, null, null, null, CheckSeverity.Warn, "Single bolus – titrate."),
            D("ONDAN-A", "ondansetron", null, 16, null, "mg", 8, 24, null, null, null, CheckSeverity.Warn, null),
            D("ONDAN-C", "ondansetron", null, null, 16, "mg", 4, null, 0.15m, null, null, CheckSeverity.Warn, null),
            D("DROP-A", "droperidol", "IV", 16, null, "mg", 1.25m, null, null, null, null, CheckSeverity.Warn, "PONV dose."),
            D("DEXA-A", "dexamethasone", "IV", 16, null, "mg", 8, null, null, null, null, CheckSeverity.Warn, "PONV dose."),
            D("CYCL-A", "cyclizine", null, 16, null, "mg", 50, 150, null, null, 4, CheckSeverity.Stop, null),
            D("METO-A", "metoclopramide", null, 16, null, "mg", 10, 30, null, null, 6, CheckSeverity.Stop, "Maximum 5 days."),
            D("CEFAZ-A", "cefazolin", "IV", 16, null, "mg", 2000, null, null, null, null, CheckSeverity.Warn, "3 g if 120 kg or more."),
            D("MIDAZ-A", "midazolam", "IV", 16, 70, "mg", 5, null, null, null, null, CheckSeverity.Warn, "Sedation – titrate."),
            D("MIDAZ-70", "midazolam", "IV", 70, null, "mg", 2, null, null, null, null, CheckSeverity.Warn, "Over 70 – titrate slowly."),
            D("LIDO-A", "lidocaine", null, null, null, "mg", 200, null, 3, null, null, CheckSeverity.Warn, "Plain. With adrenaline up to 7 mg/kg (500 mg)."),
            D("BUPI-A", "bupivacaine", null, null, null, "mg", 150, null, 2, null, null, CheckSeverity.Warn, "Total of all local anaesthetic counts."),
            D("ROPI-A", "ropivacaine", null, null, null, "mg", 225, null, 3, null, null, CheckSeverity.Warn, null),
            D("ADR-IM", "adrenaline", "IM", 12, null, "mg", 0.5m, null, null, null, null, CheckSeverity.Stop, "Anaphylaxis IM dose."),
        ]);
        rs.Groups.AddRange(
        [
            G("PEN", "Penicillins", true, CheckSeverity.Stop, ["amoxicillin", "ampicillin", "benzylpenicillin", "phenoxymethylpenicillin", "flucloxacillin", "piperacillin"], ["penicillin", "penicillins"]),
            G("CEPH", "Cephalosporins", true, CheckSeverity.Stop, ["cefazolin", "cefalexin", "cefuroxime", "ceftriaxone", "cefoxitin", "ceftazidime", "cefaclor"], ["cephalosporin", "cephalosporins"]),
            G("CARB", "Carbapenems", true, CheckSeverity.Stop, ["meropenem", "imipenem", "ertapenem"], ["carbapenem", "carbapenems"]),
            G("SULFA", "Sulfonamide antibiotics", true, CheckSeverity.Stop, ["sulfamethoxazole"], ["sulfa", "sulpha", "sulfonamide", "sulphonamide", "sulfonamides", "sulphonamides"]),
            G("NSAID", "NSAIDs", true, CheckSeverity.Stop, ["ibuprofen", "diclofenac", "naproxen", "mefenamic acid", "ketorolac", "aspirin"], ["nsaid", "nsaids", "anti inflammatory", "anti inflammatories"]),
            G("COX2", "COX-2 inhibitors", true, CheckSeverity.Stop, ["parecoxib", "celecoxib", "etoricoxib"], ["coxib", "cox 2"]),
            G("OPIOID", "Opioids", true, CheckSeverity.Warn, ["morphine", "oxycodone", "fentanyl", "codeine", "tramadol", "pethidine", "tapentadol", "remifentanil", "alfentanil", "hydromorphone", "methadone"], ["opioid", "opioids", "opiate", "opiates"]),
            G("LA", "Amide local anaesthetics", true, CheckSeverity.Warn, ["lidocaine", "bupivacaine", "ropivacaine", "prilocaine", "levobupivacaine", "articaine"], ["local anaesthetic", "local anesthetic"]),
            G("QT", "QT-prolonging medicines", false, CheckSeverity.Info, ["ondansetron", "droperidol", "haloperidol", "erythromycin", "clarithromycin", "azithromycin", "citalopram", "escitalopram", "amiodarone", "methadone", "quinine"], []),
            G("SSRI", "SSRIs and SNRIs", false, CheckSeverity.Info, ["citalopram", "escitalopram", "fluoxetine", "sertraline", "paroxetine", "venlafaxine", "duloxetine"], []),
            G("MAOI", "MAO inhibitors", false, CheckSeverity.Info, ["moclobemide", "phenelzine", "tranylcypromine", "selegiline", "linezolid", "methylthioninium chloride"], []),
            G("ANTICOAG", "Anticoagulants", false, CheckSeverity.Info, ["warfarin", "dabigatran", "rivaroxaban", "apixaban", "enoxaparin", "heparin", "dalteparin"], []),
            G("ANTIPLT", "Antiplatelets", false, CheckSeverity.Info, ["aspirin", "clopidogrel", "ticagrelor", "prasugrel", "dipyridamole"], []),
            G("BENZO", "Benzodiazepines", false, CheckSeverity.Info, ["midazolam", "diazepam", "lorazepam", "temazepam", "clonazepam"], []),
            G("HC", "Hormonal contraceptives", false, CheckSeverity.Info, ["ethinylestradiol", "levonorgestrel", "norethisterone", "desogestrel", "etonogestrel", "medroxyprogesterone", "drospirenone"], []),
        ]);
        rs.CrossReactions.AddRange(
        [
            new CrossReaction { FromGroup = "PEN", ToGroup = "CEPH", Severity = CheckSeverity.Warn, Message = "Cross-reactivity is low, but avoid if the penicillin reaction was anaphylaxis or severe, unless an allergy specialist has advised." },
            new CrossReaction { FromGroup = "PEN", ToGroup = "CARB", Severity = CheckSeverity.Warn, Message = "Cross-reactivity is low – check what the reaction was." },
            new CrossReaction { FromGroup = "CEPH", ToGroup = "PEN", Severity = CheckSeverity.Warn, Message = "Check what the reaction was." },
            new CrossReaction { FromGroup = "NSAID", ToGroup = "COX2", Severity = CheckSeverity.Warn, Message = "COX-2 inhibitors are often tolerated – check what the reaction was." },
            new CrossReaction { FromGroup = "SULFA", ToGroup = "COX2", Severity = CheckSeverity.Info, Message = "Celecoxib contains a sulfonamide group; cross-reaction is unlikely." },
        ]);
        rs.Interactions.AddRange(
        [
            X("QT-QT", "group:QT", "group:QT", CheckSeverity.Warn, "both can prolong the QT interval", "Check the ECG/QTc and potassium and magnesium; consider another antiemetic."),
            X("TRAM-SSRI", "tramadol", "group:SSRI", CheckSeverity.Warn, "risk of serotonin syndrome and seizures", "Use the lowest dose or another analgesic; watch for agitation, tremor, fever."),
            X("TRAM-MAOI", "tramadol", "group:MAOI", CheckSeverity.Stop, "serotonin syndrome – do not combine", "Choose another analgesic."),
            X("PETH-MAOI", "pethidine", "group:MAOI", CheckSeverity.Stop, "serotonin syndrome and severe reactions – do not combine", "Choose another opioid."),
            X("FENT-MAOI", "fentanyl", "group:MAOI", CheckSeverity.Warn, "serotonin syndrome reported", "Use with caution and monitor."),
            X("OPI-BENZO", "group:OPIOID", "group:BENZO", CheckSeverity.Warn, "increased sedation and breathing depression", "Reduce doses, monitor sedation and SpO2, have naloxone ready."),
            X("NSAID-AC", "group:NSAID", "group:ANTICOAG", CheckSeverity.Warn, "increased bleeding risk", "Avoid if possible; check with the surgeon or anaesthetist."),
            X("COX2-AC", "group:COX2", "group:ANTICOAG", CheckSeverity.Warn, "increased bleeding risk with anticoagulants", "Check with the surgeon or anaesthetist."),
            X("NSAID-NSAID", "group:NSAID", "group:NSAID", CheckSeverity.Warn, "two NSAIDs together increase stomach, kidney and bleeding risk (low-dose aspirin excepted)", "Give one NSAID only."),
            X("NSAID-COX2", "group:NSAID", "group:COX2", CheckSeverity.Warn, "an NSAID with a COX-2 inhibitor increases stomach and kidney risk", "Give one only."),
            X("METO-LDOPA", "metoclopramide", "levodopa", CheckSeverity.Stop, "blocks levodopa and worsens Parkinson's disease", "Use ondansetron or domperidone instead."),
            X("DROP-LDOPA", "droperidol", "levodopa", CheckSeverity.Stop, "blocks levodopa and worsens Parkinson's disease", "Use ondansetron instead."),
            X("MIDAZ-MACRO", "midazolam", "clarithromycin", CheckSeverity.Warn, "clarithromycin raises midazolam levels (longer sedation)", "Reduce the midazolam dose and monitor."),
            X("MIDAZ-ERY", "midazolam", "erythromycin", CheckSeverity.Warn, "erythromycin raises midazolam levels (longer sedation)", "Reduce the midazolam dose and monitor."),
            X("SUG-HC", "sugammadex", "group:HC", CheckSeverity.Warn, "sugammadex can make hormonal contraception less effective for 7 days", "Tell the patient to use extra contraception for 7 days (as if a pill were missed)."),
            X("GENT-NSAID", "gentamicin", "group:NSAID", CheckSeverity.Info, "both can affect the kidneys", "Check kidney function if repeated."),
        ]);
        foreach (var g in rs.Groups) g.RuleSetId = rs.Id;
        foreach (var r in rs.DoseRules) r.RuleSetId = rs.Id;
        foreach (var x in rs.CrossReactions) x.RuleSetId = rs.Id;
        foreach (var x in rs.Interactions) x.RuleSetId = rs.Id;
        return rs;
    }
}
