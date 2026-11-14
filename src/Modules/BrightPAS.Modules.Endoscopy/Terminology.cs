namespace BrightPAS.Modules.Endoscopy;

/// <summary>
/// Structured endoscopy terminology (MST 3.0-style minimal standard terminology, ESGE/BSG quality measures, NZ NBSP practice).
/// Codes are stored; labels are shown. Clinical lists to be reviewed by the TDS endoscopy lead before live use.
/// </summary>
public static class Terminology
{
    public sealed record Item(string Code, string Label, string? Patient = null);
    public sealed record FindingKind(string Code, string Label, bool Upper, bool Lower, string? GradeLabel, string[] Grades, string Patient);

    public static readonly Item[] UpperSites =
    [
        new("UpperOesophagus", "Upper oesophagus", "the top of the gullet"), new("MidOesophagus", "Mid oesophagus", "the middle of the gullet"),
        new("LowerOesophagus", "Lower oesophagus", "the lower gullet"), new("Goj", "Gastro-oesophageal junction", "where the gullet joins the stomach"),
        new("Cardia", "Cardia", "the top of the stomach"), new("Fundus", "Fundus", "the top of the stomach"), new("Body", "Gastric body", "the main part of the stomach"),
        new("Incisura", "Incisura (angle)", "the bend of the stomach"), new("Antrum", "Antrum", "the lower part of the stomach"), new("Pylorus", "Pylorus", "the stomach outlet"),
        new("D1", "Duodenal bulb (D1)", "the first part of the small bowel"), new("D2", "Second part of duodenum (D2)", "the small bowel"),
    ];

    public static readonly Item[] LowerSites =
    [
        new("TerminalIleum", "Terminal ileum", "the end of the small bowel"), new("Caecum", "Caecum", "the start of the large bowel"),
        new("Ascending", "Ascending colon", "the right side of the bowel"), new("HepaticFlexure", "Hepatic flexure", "the right bend of the bowel"),
        new("Transverse", "Transverse colon", "the middle of the bowel"), new("SplenicFlexure", "Splenic flexure", "the left bend of the bowel"),
        new("Descending", "Descending colon", "the left side of the bowel"), new("Sigmoid", "Sigmoid colon", "the lower left bowel"),
        new("Rectum", "Rectum", "the back passage"), new("Anus", "Anal canal", "the anus"), new("Anastomosis", "Anastomosis", "the join from previous surgery"),
    ];

    public static IEnumerable<Item> AllSites => UpperSites.Concat(LowerSites);
    public static bool IsUpperSite(string code) => UpperSites.Any(s => s.Code == code);
    public static bool IsLowerSite(string code) => LowerSites.Any(s => s.Code == code);
    public static string SiteLabel(string? code) => AllSites.FirstOrDefault(s => s.Code == code)?.Label ?? code ?? "";
    public static string SitePatient(string? code) => AllSites.FirstOrDefault(s => s.Code == code)?.Patient ?? SiteLabel(code).ToLowerInvariant();

    public static readonly Item[] UpperIndications =
    [
        new("Dyspepsia", "Dyspepsia"), new("Reflux", "Reflux / heartburn"), new("Dysphagia", "Dysphagia"), new("IdaUpper", "Iron deficiency anaemia"),
        new("WeightLossUpper", "Weight loss"), new("Haematemesis", "Haematemesis / melaena"), new("Vomiting", "Nausea / vomiting"),
        new("BarrettSurveillance", "Barrett's surveillance"), new("Coeliac", "Coeliac disease assessment"), new("Varices", "Variceal screening"),
        new("UlcerFollowUp", "Ulcer follow-up"), new("AbnormalImaging", "Abnormal imaging"),
    ];

    public static readonly Item[] LowerIndications =
    [
        new("PositiveFit", "Positive FIT (bowel screening)"), new("RectalBleeding", "Rectal bleeding"), new("ChangeInBowelHabit", "Change in bowel habit"),
        new("Diarrhoea", "Diarrhoea"), new("IdaLower", "Iron deficiency anaemia"), new("WeightLossLower", "Weight loss"), new("AbdominalPain", "Abdominal pain"),
        new("PolypSurveillance", "Polyp surveillance"), new("CancerSurveillance", "Post-cancer surveillance"), new("FamilyHistory", "Family history of bowel cancer"),
        new("IbdAssessment", "IBD assessment / surveillance"), new("AbnormalImagingLower", "Abnormal imaging"),
    ];

    public static readonly FindingKind[] FindingKinds =
    [
        new("Normal", "Normal", true, true, null, [], "This looked normal."),
        new("Oesophagitis", "Oesophagitis", true, false, "LA grade", ["LA A", "LA B", "LA C", "LA D"], "Inflammation of the gullet, usually caused by acid reflux."),
        new("Barretts", "Barrett's oesophagus", true, false, "Prague", ["C0M1", "C0M2", "C1M2", "C1M3", "C2M3", "C2M4", "C3M5", "C4M6"], "A change in the lining of the lower gullet caused by long-term reflux. It needs regular checks."),
        new("HiatusHernia", "Hiatus hernia", true, false, "Size", ["< 2 cm", "2–4 cm", "> 4 cm"], "Part of the stomach sits above the diaphragm. This is common and can cause reflux."),
        new("Varices", "Varices", true, false, "Grade", ["Grade 1", "Grade 2", "Grade 3"], "Enlarged veins in the gullet or stomach."),
        new("Ulcer", "Ulcer", true, true, "Forrest", ["Ia", "Ib", "IIa", "IIb", "IIc", "III"], "A sore in the lining."),
        new("Gastritis", "Gastritis", true, false, null, [], "Inflammation of the stomach lining."),
        new("Erosions", "Erosions", true, true, null, [], "Small, shallow breaks in the lining."),
        new("CoeliacAppearance", "Coeliac appearance", true, false, null, [], "The small bowel lining may suggest coeliac disease – the biopsies will confirm."),
        new("Stricture", "Stricture", true, true, null, [], "A narrowing."),
        new("Mass", "Mass / tumour", true, true, null, [], "An abnormal area that has been biopsied. Your doctor will discuss the results with you."),
        new("Diverticulosis", "Diverticulosis", false, true, "Extent", ["Mild", "Moderate", "Severe"], "Small pouches in the bowel wall. This is very common and usually harmless."),
        new("Haemorrhoids", "Haemorrhoids", false, true, "Grade", ["Grade 1", "Grade 2", "Grade 3", "Grade 4"], "Swollen blood vessels at the back passage (piles)."),
        new("Colitis", "Colitis", false, true, "Mayo", ["Mayo 0", "Mayo 1", "Mayo 2", "Mayo 3"], "Inflammation of the bowel lining."),
        new("Angiodysplasia", "Angiodysplasia", true, true, null, [], "Small abnormal blood vessels that can bleed."),
        new("Fissure", "Anal fissure", false, true, null, [], "A small tear at the back passage."),
        new("Melanosis", "Melanosis coli", false, true, null, [], "Harmless darkening of the bowel lining, often from laxatives."),
        new("Other", "Other", true, true, null, [], ""),
    ];

    public static readonly string[] Paris = ["Ip", "Isp", "Is", "IIa", "IIb", "IIc", "IIa+IIc", "III", "LST-G", "LST-NG"];
    public static readonly string[] Nice = ["1", "2", "3"];
    public static readonly string[] Jnet = ["1", "2A", "2B", "3"];
    public static readonly Item[] Techniques =
    [
        new("ColdSnare", "Cold snare"), new("HotSnare", "Hot snare"), new("Emr", "EMR (lift and snare)"), new("ColdForceps", "Cold forceps"),
        new("HotForceps", "Hot forceps"), new("Esd", "ESD"), new("NotRemoved", "Not removed"),
    ];
    public static readonly string[] Resections = ["EnBloc", "Piecemeal", "NotRemoved"];
    public static readonly string[] Retrievals = ["Yes", "No", "Partial", "NotApplicable"];
    public static readonly string[] Completeness = ["Yes", "No", "Uncertain"];
    public static readonly Item[] Histologies =
    [
        new("TubularAdenoma", "Tubular adenoma"), new("TubulovillousAdenoma", "Tubulovillous adenoma"), new("VillousAdenoma", "Villous adenoma"),
        new("SessileSerrated", "Sessile serrated lesion"), new("TraditionalSerrated", "Traditional serrated adenoma"), new("Hyperplastic", "Hyperplastic polyp"),
        new("Inflammatory", "Inflammatory polyp"), new("Adenocarcinoma", "Adenocarcinoma"), new("NormalMucosa", "Normal mucosa"), new("Other", "Other"), new("NotRetrieved", "Not retrieved"),
    ];
    public static bool IsAdenoma(string? h) => h is "TubularAdenoma" or "TubulovillousAdenoma" or "VillousAdenoma" or "TraditionalSerrated" or "Adenocarcinoma";
    public static bool IsSerrated(string? h) => h is "SessileSerrated" or "TraditionalSerrated";
    public static readonly string[] Dysplasias = ["None", "Low", "High"];
    public static readonly string[] TattooPositions = ["Distal", "Proximal", "Both", "Opposite wall"];
    public static readonly Item[] InterventionKinds =
    [
        new("Biopsy", "Biopsy"), new("Clo", "CLO / urease test"), new("Clip", "Clip"), new("Banding", "Band ligation"), new("Dilatation", "Dilatation"),
        new("Apc", "Argon plasma coagulation"), new("Injection", "Injection"), new("Haemostasis", "Haemostasis (other)"), new("ForeignBody", "Foreign body removal"), new("Other", "Other"),
    ];
    public static readonly string[] BiopsyPurposes = ["Sydney protocol", "Coeliac (D1 + D2)", "Barrett's (Seattle)", "Random colonic (microscopic colitis)", "IBD mapping", "Lesion", "Ulcer edge", "Other"];
    public static readonly string[] Complications = ["None", "Bleeding", "Perforation", "Hypoxia", "Hypotension", "Vasovagal", "Aspiration", "Sedation reversal", "Other"];

    public static string IndicationLabel(string code) => UpperIndications.Concat(LowerIndications).FirstOrDefault(i => i.Code == code)?.Label ?? code;
    public static string FindingLabel(string code) => FindingKinds.FirstOrDefault(f => f.Code == code)?.Label ?? code;
    public static string Label(Item[] list, string? code) => list.FirstOrDefault(i => i.Code == code)?.Label ?? code ?? "";

    public static bool HasUpper(ProcedureKind k) => k is ProcedureKind.Gastroscopy or ProcedureKind.Bidirectional;
    public static bool HasLower(ProcedureKind k) => k is ProcedureKind.Colonoscopy or ProcedureKind.FlexibleSigmoidoscopy or ProcedureKind.Bidirectional;
    public static bool IsColonoscopy(ProcedureKind k) => k is ProcedureKind.Colonoscopy or ProcedureKind.Bidirectional;

    public static string KindLabel(ProcedureKind k) => k switch
    {
        ProcedureKind.Gastroscopy => "Gastroscopy",
        ProcedureKind.Colonoscopy => "Colonoscopy",
        ProcedureKind.FlexibleSigmoidoscopy => "Flexible sigmoidoscopy",
        _ => "Gastroscopy and colonoscopy",
    };

    /// <summary>Best guess of the procedure from the booking text ("Colonoscopy +/- polypectomy", "Gastroscopy & colonoscopy", "OGD").</summary>
    public static ProcedureKind? Guess(string? procedure)
    {
        var p = (procedure ?? "").ToLowerInvariant();
        var colon = p.Contains("colonoscop");
        var gastro = p.Contains("gastroscop") || p.Contains("ogd") || p.Contains("upper endoscopy") || p.Contains("upper gi");
        if (colon && gastro) return ProcedureKind.Bidirectional;
        if (colon) return ProcedureKind.Colonoscopy;
        if (p.Contains("sigmoidoscop")) return ProcedureKind.FlexibleSigmoidoscopy;
        if (gastro) return ProcedureKind.Gastroscopy;
        return null;
    }

    /// <summary>Default patient-report wording (administrators can change it in Endoscopy › Settings).</summary>
    public static readonly (string Key, string Title, string Body)[] DefaultAdvice =
    [
        ("Gastroscopy", "About your gastroscopy", "A gastroscopy is a look at your gullet (oesophagus), stomach and the first part of your small bowel (duodenum) with a thin, flexible camera."),
        ("Colonoscopy", "About your colonoscopy", "A colonoscopy is a look at the lining of your large bowel (colon) with a thin, flexible camera."),
        ("FlexibleSigmoidoscopy", "About your flexible sigmoidoscopy", "A flexible sigmoidoscopy is a look at the lower part of your large bowel with a thin, flexible camera."),
        ("Sedation", "After sedation", "For the next 24 hours you must not drive, operate machinery, drink alcohol, sign legal documents or look after young children on your own. A responsible adult must stay with you tonight."),
        ("Polypectomy", "After polyp removal", "You may notice a small amount of blood in your bowel motions for a day or two. Avoid heavy lifting and strenuous exercise for 48 hours. Avoid long-haul flights for 2 weeks if a large polyp was removed."),
        ("Biopsy", "Biopsies", "Small samples (biopsies) were taken and sent to the laboratory. Results usually take 1–3 weeks and will be sent to you and your GP, or discussed at your follow-up."),
        ("Tattoo", "Tattoo marking", "A small ink mark (tattoo) was placed in the bowel wall so the area can be found again. It is harmless and permanent."),
        ("WhenToSeekHelp", "When to get help", "Phone Tasman Day Surgery, your GP or Healthline (0800 611 116) – or go to the Emergency Department – if you have severe or worsening tummy pain, a swollen hard tummy, fever, vomiting, or pass more than a cupful of blood."),
        ("Diet", "Eating and drinking", "You can eat and drink normally once you are fully awake, unless you were told otherwise. Start with something light."),
    ];
}
