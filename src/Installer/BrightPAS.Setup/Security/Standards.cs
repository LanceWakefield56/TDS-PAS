namespace BrightPAS.Setup.Security;

/// <summary>
/// Maps every self-test finding to the module it protects and to the standards it gives evidence for, so the report can be
/// filed as audit evidence (DAA certification, HISF self-assessment, privacy impact assessment).
/// References are to chapters/domains, not to individual requirement numbers: the self-test is evidence towards a control,
/// not proof of full compliance (the independent penetration test is still required – spec 11.1).
/// </summary>
public static class Standards
{
    public const string AsvsAuth = "OWASP ASVS 4.0 V2 Authentication";
    public const string AsvsSession = "OWASP ASVS 4.0 V3 Session management";
    public const string AsvsAccess = "OWASP ASVS 4.0 V4 Access control";
    public const string AsvsInput = "OWASP ASVS 4.0 V5 Validation, sanitisation and encoding";
    public const string AsvsCrypto = "OWASP ASVS 4.0 V6 Stored cryptography";
    public const string AsvsErrors = "OWASP ASVS 4.0 V7 Error handling and logging";
    public const string AsvsData = "OWASP ASVS 4.0 V8 Data protection";
    public const string AsvsComms = "OWASP ASVS 4.0 V9 Communication";
    public const string AsvsLogic = "OWASP ASVS 4.0 V11 Business logic (anti-automation)";
    public const string AsvsFiles = "OWASP ASVS 4.0 V12 Files and resources";
    public const string AsvsApi = "OWASP ASVS 4.0 V13 API and web service";
    public const string AsvsConfig = "OWASP ASVS 4.0 V14 Configuration";
    public const string HisfAccess = "HISO 10029:2022 HISF – access control";
    public const string HisfIdentity = "HISO 10029:2022 HISF – identity and authentication";
    public const string HisfCrypto = "HISO 10029:2022 HISF – cryptography and secure communications";
    public const string HisfLogging = "HISO 10029:2022 HISF – logging and monitoring";
    public const string HisfHardening = "HISO 10029:2022 HISF – system hardening and vulnerability management";
    public const string HisfMalware = "HISO 10029:2022 HISF – malware protection";
    public const string HisfBackup = "HISO 10029:2022 HISF – backup and recovery";
    public const string HisfNetwork = "HISO 10029:2022 HISF – network security";
    public const string Ipp5 = "Privacy Act 2020 IPP 5 / HIPC 2020 Rule 5 – storage and security safeguards";

    public static readonly string[] All =
        [AsvsAuth, AsvsSession, AsvsAccess, AsvsInput, AsvsCrypto, AsvsErrors, AsvsData, AsvsComms, AsvsLogic, AsvsFiles, AsvsApi, AsvsConfig,
         HisfAccess, HisfIdentity, HisfCrypto, HisfLogging, HisfHardening, HisfMalware, HisfBackup, HisfNetwork, Ipp5];

    private static readonly (string[] Keys, string[] Controls)[] Rules =
    [
        (["login required", "requires sign-in", "anonymous", "permission", "low-privilege"], [AsvsAccess, AsvsApi, HisfAccess, Ipp5]),
        (["csrf", "forgery"], [AsvsAccess, AsvsSession]),
        (["injection", "xss", "script"], [AsvsInput, Ipp5]),
        (["callback key", "link guessing", "code guessing", "oversized", "malformed"], [AsvsAccess, AsvsLogic, AsvsApi]),
        (["password-guessing", "mfa", "multi-factor", "administrator", "sign-in", "password"], [AsvsAuth, HisfIdentity]),
        (["session", "cookie"], [AsvsSession]),
        (["certificate", "tls", "ssl", "https", "http disabled", "encryption in transit", "force encryption"], [AsvsComms, HisfCrypto]),
        (["header", "server details", "trace", "debug", "production mode"], [AsvsConfig, HisfHardening]),
        (["sensitive files", "path traversal", "directory"], [AsvsFiles, AsvsConfig]),
        (["error"], [AsvsErrors]),
        (["audit", "ledger", "integrity", "tamper"], [AsvsErrors, HisfLogging, Ipp5]),
        (["tde", "encrypted", "encryption at rest", "secret", "vendor password"], [AsvsCrypto, AsvsData, HisfCrypto, Ipp5]),
        (["backup", "recovery model"], [HisfBackup, Ipp5]),
        (["firewall", "rdp", "smb", "port"], [HisfNetwork, HisfHardening]),
        (["antivirus", "defender", "malware"], [HisfMalware]),
        (["patch", "update", "version"], [HisfHardening]),
        (["sa login", "xp_cmdshell", "database login", "least privilege", "db_owner", "sysadmin", "guest"], [AsvsConfig, HisfAccess, HisfHardening]),
        (["test patient", "licence", "sandbox"], [AsvsConfig]),
    ];

    public static IReadOnlyList<string> ControlsFor(string area, string test)
    {
        var text = (area + " " + test).ToLowerInvariant();
        var list = Rules.Where(r => r.Keys.Any(text.Contains)).SelectMany(r => r.Controls).Distinct().ToList();
        if (area.StartsWith("Server", StringComparison.OrdinalIgnoreCase) && list.Count == 0) list.Add(HisfHardening);
        if (area.StartsWith("Database", StringComparison.OrdinalIgnoreCase) && !list.Contains(Ipp5)) list.Add(Ipp5);
        return list;
    }

    private static readonly (string[] Keys, string Module)[] ModuleRules =
    [
        (["reminders", "sms", "callback"], "SMS & email reminders"),
        (["lab", "hl7"], "Lab results"),
        (["forms", "questionnaire", "patient link"], "Pre-admission forms"),
        (["referral", "waitlist"], "Referrals & waitlist"),
        (["letter", "healthlink"], "Letters & documents"),
        (["roster", "shift", "leave", "payroll", "swap"], "Rostering"),
        (["credential"], "Credentialing"),
        (["devices", "monitor", "safersleep", "anaesthetic record", "capture"], "Devices & SaferSleep"),
        (["quality", "incident", "complaint", "risk register", "corrective action"], "Quality management"),
        (["scribe", "dictation", "transcri", "ai-assisted", "consent to ai"], "BrightPAS Scribe"),
        (["endoscopy", "polyp", "colonoscopy", "gastroscopy", "worklist"], "Endoscopy"),
        (["emr", "episode", "checklist", "controlled drug", "observation"], "Clinical record (eMR)"),
        (["assets", "cssd", "steriliser", "tray", "scope", "implant", "stock", "purchase"], "Assets & CSSD"),
        (["messaging", "interface", "dicom", "mapping", "flow"], "Messaging & integration engine"),
        (["portal"], "Portals"),
        (["theatre"], "Theatre bookings"),
        (["billing", "invoice", "estimate"], "Estimates & invoices"),
        (["patients", "patient register"], "Patients"),
        (["audit"], "Audit (platform)"),
        (["licence"], "Licensing (platform)"),
    ];

    public static string ModuleFor(string area, string test)
    {
        var text = (area + " " + test).ToLowerInvariant();
        foreach (var (keys, module) in ModuleRules) if (keys.Any(text.Contains)) return module;
        return area.StartsWith("Server", StringComparison.OrdinalIgnoreCase) ? "Server" : area.StartsWith("Database", StringComparison.OrdinalIgnoreCase) ? "Database" : "Platform";
    }
}

/// <summary>
/// Black-box security checks for one licensed module. EVERY available module must have an entry (enforced by a build test),
/// so adding a module without security tests fails the build.
/// </summary>
public sealed record ModuleSuite(string ModuleId, string Name, string[] AnonymousReads, (string Method, string Path, string Body)[] AnonymousWrites);

public static class ModuleSuites
{
    public static readonly IReadOnlyList<ModuleSuite> All =
    [
        new("patients", "Patients", ["api/patients?q=a", "api/patients/" + Guid.Empty, $"api/patients/{Guid.Empty}/documents", $"api/patients/documents/{Guid.Empty}",
                "api/patients/identity-corrections", "api/patients/merges", $"api/patients/{Guid.Empty}/transfer-pack", "api/admin/security-settings"],
            [("POST", "api/patients", """{"familyName":"X","givenNames":"Y","dateOfBirth":"2000-01-01","sex":"Female"}"""), ("POST", $"api/patients/{Guid.Empty}/alerts", "{}"),
             ("POST", $"api/patients/{Guid.Empty}/documents", "{}"), ("POST", "api/patients/merges", "{}"), ("POST", $"api/patients/merges/{Guid.Empty}/decide", """{"approve":true}"""),
             ("POST", $"api/patients/{Guid.Empty}/print", "{}"), ("POST", "api/waitlist/export", "{}")]),
        new("labs", "Lab results", ["api/labs", "api/labs/unmatched", "api/labs/count"],
            [("POST", "api/labs/import", """{"message":"MSH|^~\\&|X|Y|Z|W|202601010000||ORU^R01|1|P|2.4"}"""), ("POST", $"api/labs/{Guid.Empty}/acknowledge", "{}")]),
        new("theatre", "Theatre bookings", ["api/theatre/locations", "api/theatre/sessions?from=2026-01-01", "api/theatre/calendar?from=2026-01-05&to=2026-01-11", "api/theatre/appointments?from=2026-01-05"],
            [("POST", "api/theatre/sessions", "{}"), ("POST", "api/theatre/bookings", "{}")]),
        new("forms", "Pre-admission forms", ["api/forms", "api/forms/definition"],
            [("POST", "api/forms", $$"""{"patientId":"{{Guid.Empty}}"}"""), ("POST", $"api/forms/{Guid.Empty}/triage", "{}")]),
        new("billing", "Estimates & invoices", ["api/billing/documents", "api/billing/settings", "api/billing/fees"],
            [("POST", "api/billing/fees", "{}"), ("PUT", "api/billing/settings", "{}")]),
        new("reminders", "SMS & email reminders", ["api/reminders/status", "api/reminders/messages", "api/reminders/replies", "api/reminders/settings", "api/reminders/templates"],
            [("POST", "api/reminders/test", """{"channel":"Sms","to":"0211234567"}"""), ("PUT", "api/reminders/settings", "{}"), ("POST", "api/reminders/simulate-reply", "{}")]),
        new("referrals", "Referrals & waitlist", ["api/referrals", "api/referrals/summary", "api/waitlist"],
            [("POST", "api/referrals", "{}"), ("POST", "api/waitlist", "{}"), ("POST", $"api/waitlist/{Guid.Empty}/book", "{}"), ("PUT", "api/referrals/settings", "{}")]),
        new("letters", "Letters & documents", ["api/letters", "api/letters/templates", "api/letters/practices", "api/letters/healthlink", "api/letters/admin/templates", $"api/letters/{Guid.Empty}"],
            [("POST", "api/letters", "{}"), ("POST", $"api/letters/{Guid.Empty}/send", "{}"), ("PUT", "api/letters/templates", "{}"), ("PUT", "api/letters/healthlink", "{}")]),
        new("rostering", "Rostering", ["api/roster/me", "api/roster/staff", "api/roster/shifts?from=2026-01-05&to=2026-01-11", "api/roster/leave", "api/roster/hours?from=2026-01-05&to=2026-01-11"],
            [("POST", "api/roster/staff", "{}"), ("POST", "api/roster/shifts", "{}"), ("POST", "api/roster/leave", "{}"), ("POST", $"api/roster/leave/{Guid.Empty}/decide", "{}"),
             ("POST", "api/roster/swaps", "{}"), ("POST", "api/roster/payroll-export", """{"from":"2026-01-05","to":"2026-01-18"}""")]),
        new("credentialing", "Credentialing", ["api/credentialing/status", "api/credentialing/settings", $"api/credentialing/clinicians/{Guid.Empty}"],
            [("POST", "api/credentialing/credentials", "{}"), ("POST", "api/credentialing/privileges", "{}"), ("PUT", "api/credentialing/settings", "{}")]),
        new("portal", "Specialist, PA & patient portals", ["api/portal/specialist", $"api/portal/specialist/patients/{Guid.Empty}", "api/portal/requests", "api/portal/delegates"],
            [("POST", $"api/portal/patients/{Guid.Empty}/link", "{}"), ("POST", "api/portal/delegates", "{}"), ("POST", $"api/portal/requests/{Guid.Empty}/handle", "{}")]),
        new("messaging", "Messaging & integration engine", ["api/messaging/dashboard", "api/messaging/interfaces", "api/messaging/messages", "api/messaging/flows", "api/messaging/mappings", "api/messaging/tables", "api/messaging/settings"],
            [("POST", "api/messaging/interfaces", "{}"), ("POST", "api/messaging/flows", "{}"), ("POST", "api/messaging/mappings", "{}"), ("POST", "api/messaging/tables", "{}"),
             ("POST", $"api/messaging/messages/{Guid.Empty}/replay", "{}"), ("POST", $"api/messaging/versions/{Guid.Empty}/approve", "{}"), ("PUT", "api/messaging/settings", "{}")]),
        new("assets", "Assets & CSSD", ["api/assets/dashboard", "api/assets/equipment", "api/assets/trays", "api/assets/cycles", "api/assets/scopes", "api/assets/stock", "api/assets/orders",
                $"api/assets/trace?patientId={Guid.Empty}", $"api/assets/implants?patientId={Guid.Empty}"],
            [("POST", "api/assets/equipment", "{}"), ("POST", "api/assets/cycles", "{}"), ("POST", $"api/assets/cycles/{Guid.Empty}/release", "{}"), ("POST", "api/assets/trays/issue", "{}"),
             ("POST", "api/assets/implants", "{}"), ("POST", $"api/assets/orders/{Guid.Empty}/approve", "{}"), ("POST", "api/assets/xero/bills", "{}")]),
        new("emr", "Clinical record (eMR)", ["api/emr/today", $"api/emr/bookings/{Guid.Empty}", "api/emr/controlled-drugs"],
            [("POST", $"api/emr/episodes/{Guid.Empty}/checks", "{}"), ("POST", $"api/emr/episodes/{Guid.Empty}/medications", "{}"), ("POST", "api/emr/controlled-drugs/register", "{}"),
             ("POST", $"api/emr/episodes/{Guid.Empty}/discharge", "{}")]),
        new("endoscopy", "Endoscopy", ["api/endoscopy/today", $"api/endoscopy/bookings/{Guid.Empty}", "api/endoscopy/kpis", $"api/endoscopy/media/{Guid.Empty}"],
            [("POST", $"api/endoscopy/procedures/{Guid.Empty}/polyps", "{}"), ("POST", $"api/endoscopy/procedures/{Guid.Empty}/sign", "{}"), ("POST", $"api/endoscopy/procedures/{Guid.Empty}/pictures", "{}"),
             ("PUT", "api/endoscopy/settings", "{}")]),
        new("scribe", "BrightPAS Scribe", ["api/scribe/status", "api/scribe/sessions", "api/scribe/templates", $"api/scribe/sessions/{Guid.Empty}"],
            [("POST", "api/scribe/dictate", "{}"), ("POST", "api/scribe/sessions", "{}"), ("POST", $"api/scribe/sessions/{Guid.Empty}/sign", "{}"), ("PUT", "api/scribe/settings", "{}")]),
        new("devices", "Devices & SaferSleep", ["api/devices/overview", $"api/devices/episodes/{Guid.Empty}", "api/devices/records"],
            [("POST", $"api/devices/episodes/{Guid.Empty}/connect", "{}"), ("POST", $"api/devices/captures/{Guid.Empty}/validate", "{}"), ("POST", $"api/devices/records/{Guid.Empty}/file", "{}"),
             ("PUT", "api/devices/settings", "{}")]),
        new("quality", "Quality management", ["api/quality/summary", "api/quality/incidents", $"api/quality/incidents/{Guid.Empty}", "api/quality/complaints", "api/quality/risks"],
            [("POST", "api/quality/incidents", "{}"), ("POST", $"api/quality/incidents/{Guid.Empty}/triage", "{}"), ("POST", "api/quality/complaints", "{}"), ("POST", "api/quality/pack", "{}"),
             ("PUT", "api/quality/settings", "{}")]),
    ];
}
