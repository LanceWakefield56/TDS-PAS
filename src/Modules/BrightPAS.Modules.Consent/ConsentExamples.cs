namespace BrightPAS.Modules.Consent;

/// <summary>
/// Example information sheets for demonstration and tests. The wording is general patient information written for BrightPAS – NOT clinically
/// reviewed. TDS's surgeons must rewrite and approve their own sheets (with their own figures) before patients see them.
/// </summary>
public static class ConsentExamples
{
    public const string Disclaimer = "Example wording for demonstration – TDS clinicians must review and approve their own sheet before use.";

    public static IReadOnlyList<SheetEdit> Sheets { get; } =
    [
        new("LAPCHOLE", "Laparoscopic cholecystectomy (keyhole gallbladder removal)", "cholecystectomy, gallbladder, gall bladder",
            "Your gallbladder is removed through four small cuts in your tummy using a camera (keyhole surgery). You are asleep under a general anaesthetic. It usually takes about an hour and most people go home the same day.\n" + Disclaimer,
            "Stops attacks of gallstone pain and lowers the chance of problems such as inflammation of the gallbladder, jaundice or pancreatitis.",
            "Changing your diet may reduce attacks for some people. Open surgery (one larger cut) is sometimes needed instead. You can also choose to wait and see.",
            "Gallstone pain may come back and may get worse. Some people develop infection of the gallbladder, jaundice or pancreatitis, which can be serious and may need emergency treatment.",
            "You will have a general anaesthetic. Your anaesthetist will talk with you on the day about the anaesthetic and its risks.",
            "Do not eat for 6 hours before your admission time; you may drink water until 2 hours before. Bring a list of your medicines. Someone must take you home and stay with you overnight.",
            "Expect some shoulder-tip pain from the gas for a day or two. Most people return to desk work in 1–2 weeks. Avoid heavy lifting for 4 weeks. Phone us if you have a fever, worsening pain, yellow skin or eyes, or redness at the cuts.",
            [
                new("Shoulder-tip and tummy pain for a few days", RiskFrequency.Common, false),
                new("Wound infection or bruising at the cuts", RiskFrequency.Common, false),
                new("Change to open surgery (a larger cut)", RiskFrequency.Uncommon, false),
                new("Bile leak needing another procedure", RiskFrequency.Uncommon, true),
                new("Bleeding needing a blood transfusion or further surgery", RiskFrequency.Rare, true),
                new("Injury to the bile duct or bowel needing major surgery", RiskFrequency.Rare, true),
                new("Blood clot in the leg or lungs", RiskFrequency.Rare, true),
            ],
            [new("Gallbladder removal – what to expect (Healthify He Puna Waiora)", "https://healthify.nz/")]),
        new("COLON", "Colonoscopy", "colonoscopy, bowel scope",
            "A thin flexible tube with a camera is passed through your back passage to look at the lining of your large bowel. Small growths (polyps) can be removed and samples (biopsies) taken. You are usually given sedation to make you relaxed and sleepy. It takes 20–45 minutes.\n" + Disclaimer,
            "Finds the cause of your symptoms, and finds and removes polyps before they can turn into bowel cancer.",
            "A CT scan of the bowel (CT colonography) can show the bowel but samples cannot be taken and polyps cannot be removed. A stool test (FIT) can look for hidden blood but cannot find the cause.",
            "The cause of your symptoms may not be found, and polyps or cancer may be missed.",
            "Sedation is given through a drip in your arm. You must not drive, sign legal documents or drink alcohol for 24 hours afterwards.",
            "You will be sent bowel preparation with instructions. Follow the low-fibre diet and drink plenty of clear fluids. Iron tablets should be stopped 7 days before. Tell us if you take blood thinners or diabetes medicines.",
            "You may feel bloated for a few hours. Eat normally afterwards. Phone us or go to the emergency department if you have heavy bleeding, severe tummy pain or a fever.",
            [
                new("Bloating and cramping for a few hours", RiskFrequency.Common, false),
                new("A polyp or abnormality is missed", RiskFrequency.Uncommon, false),
                new("Bleeding, usually after a polyp is removed", RiskFrequency.Uncommon, true),
                new("A tear (perforation) in the bowel needing surgery", RiskFrequency.Rare, true),
                new("Reaction to the sedation (breathing or heart problems)", RiskFrequency.Rare, true),
            ],
            [new("Colonoscopy – patient information (Health New Zealand)", "https://www.healthnz.govt.nz/")]),
        new("CTR", "Carpal tunnel release", "carpal tunnel",
            "The tight band (ligament) pressing on the nerve at the front of your wrist is cut through a small cut in your palm, so the nerve has more room. It is usually done under local anaesthetic with you awake and takes about 20 minutes.\n" + Disclaimer,
            "Relieves numbness, tingling and pain in the hand, and helps stop the nerve being damaged further.",
            "A wrist splint at night, a steroid injection, or waiting to see whether symptoms settle.",
            "Symptoms may continue or get worse, and long-term pressure can permanently weaken the hand.",
            "The hand is numbed with local anaesthetic injected near the wrist. You will be awake.",
            "Remove rings from that hand. You can eat and drink normally unless you are told otherwise. Arrange for someone to drive you home.",
            "Keep the dressing dry and your hand raised for 2 days. Move your fingers often. Stitches come out after 10–14 days. Avoid heavy gripping for about 6 weeks.",
            [
                new("Tenderness of the scar and palm for some weeks", RiskFrequency.Common, false),
                new("Wound infection", RiskFrequency.Uncommon, false),
                new("Symptoms not fully relieved or coming back", RiskFrequency.Uncommon, false),
                new("Nerve or blood vessel injury", RiskFrequency.Rare, true),
                new("Complex regional pain syndrome (long-lasting pain and stiffness)", RiskFrequency.Rare, true),
            ],
            []),
    ];
}
