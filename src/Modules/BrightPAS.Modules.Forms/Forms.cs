using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Forms;

public sealed class FormsModule : IPasModule
{
    public const string Id = "forms";
    public ModuleManifest Manifest { get; } = new(Id, "Pre-admission forms", typeof(FormsModule).Assembly.GetName().Version ?? new Version(0, 0),
        FormsDbContext.Schema, ["patients"], "Online patient health questionnaire and nurse triage.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<FormsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, FormsDbContext.Schema));
        services.AddScoped<FormService>();
    }
}

public sealed class FormRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? BookingId { get; set; }
    public string DefinitionVersion { get; set; } = Questionnaire.Version;
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public FormStatus Status { get; set; } = FormStatus.Sent;
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? AnswersJson { get; set; }
    public string? FlagsJson { get; set; }
    public TriageCategory? SuggestedCategory { get; set; }
    public TriageCategory? TriageCategory { get; set; }
    public string? TriageOutcome { get; set; }
    public string? TriageNotes { get; set; }
    public string? TriagedBy { get; set; }
    public DateTimeOffset? TriagedAt { get; set; }
}

public enum FormStatus { Sent = 1, Opened = 2, Submitted = 3, Triaged = 4, Cancelled = 5, Expired = 6 }
/// <summary>Green: no call needed. Amber: nurse phone call. Red: anaesthetist review before the day.</summary>
public enum TriageCategory { Green = 1, Amber = 2, Red = 3 }

public sealed record FormFlag(string Level, string Text);

public sealed class FormsDbContext(DbContextOptions<FormsDbContext> options) : DbContext(options)
{
    public const string Schema = "forms";
    public DbSet<FormRequest> Requests => Set<FormRequest>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            c.Properties<DateTimeOffset>().HaveConversion<Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.Entity<FormRequest>(e =>
        {
            e.Property(x => x.DefinitionVersion).HasMaxLength(20);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.CreatedBy).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.SuggestedCategory).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.TriageCategory).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.TriageOutcome).HasMaxLength(100);
            e.Property(x => x.TriageNotes).HasMaxLength(2000);
            e.Property(x => x.TriagedBy).HasMaxLength(100);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => new { x.Status, x.SubmittedAt });
        });
    }
}

public sealed class FormsDbContextFactory : IDesignTimeDbContextFactory<FormsDbContext>
{
    public FormsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<FormsDbContext>()
        .UseSqlServer("Server=localhost;Database=BrightPAS;Integrated Security=true;TrustServerCertificate=true",
            s => s.MigrationsHistoryTable("__EFMigrationsHistory", FormsDbContext.Schema)).Options);
}

public sealed record Question(string Id, string Text, string Type, bool Required = false, string[]? Options = null, string? DetailsPrompt = null, string? Help = null);
public sealed record Section(string Title, string? Intro, IReadOnlyList<Question> Questions);

/// <summary>The pre-admission health questionnaire (version PAQ-1). Types: number, text, multiline, yesno, choice, confirm.</summary>
public static class Questionnaire
{
    public const string Version = "PAQ-1";
    private static readonly string[] YesNo = ["Yes", "No"];

    public static readonly IReadOnlyList<Section> Sections =
    [
        new("About you", "This helps us plan your care safely. It takes about 10 minutes.",
        [
            new("height_cm", "Your height (cm)", "number", true),
            new("weight_kg", "Your weight (kg)", "number", true),
            new("support_person", "Name and phone number of the adult who will take you home and stay with you for 24 hours", "text", true),
            new("lives_alone", "Do you live alone?", "yesno", true),
        ]),
        new("Your health", "Tell us about any of these, even if they are well controlled.",
        [
            new("heart", "Heart problems – chest pain, heart attack, stents, heart failure or irregular heartbeat?", "yesno", true, DetailsPrompt: "Please describe"),
            new("pacemaker", "Do you have a pacemaker or implanted defibrillator?", "yesno", true),
            new("high_bp", "High blood pressure?", "yesno", true),
            new("lung", "Asthma, COPD or other lung problems?", "yesno", true, DetailsPrompt: "Please describe"),
            new("sleep_apnoea", "Sleep apnoea, heavy snoring, or do you use a CPAP machine?", "yesno", true),
            new("diabetes", "Diabetes", "choice", true, ["No", "Yes – diet controlled", "Yes – tablets", "Yes – insulin"]),
            new("kidney_liver", "Kidney or liver disease?", "yesno", true, DetailsPrompt: "Please describe"),
            new("stroke", "Stroke, mini-stroke (TIA) or epilepsy?", "yesno", true, DetailsPrompt: "Please describe"),
            new("bleeding", "A bleeding or clotting problem, or a past blood clot (DVT / PE)?", "yesno", true, DetailsPrompt: "Please describe"),
            new("infection", "Have you been told you carry a resistant germ (e.g. MRSA, CPE) or been in hospital overseas in the last year?", "yesno", true),
            new("pregnant", "Could you be pregnant?", "choice", true, ["No", "Possibly", "Yes", "Not applicable"]),
            new("other_conditions", "Any other health conditions, operations or recent hospital stays?", "multiline"),
        ]),
        new("Medicines", null,
        [
            new("medications", "List all medicines you take, with the dose (include inhalers, patches and eye drops)", "multiline", Help: "Write 'none' if you take no medicines."),
            new("blood_thinners", "Do you take blood thinners (e.g. warfarin, dabigatran, rivaroxaban, apixaban, clopidogrel, aspirin)?", "yesno", true, DetailsPrompt: "Which one?"),
            new("herbal", "Do you take herbal remedies or supplements?", "yesno", true, DetailsPrompt: "Which ones?"),
        ]),
        new("Allergies", null,
        [
            new("allergies", "Are you allergic to any medicines, foods, plasters or anything else?", "yesno", true, DetailsPrompt: "What, and what happens?"),
            new("latex", "Are you allergic to latex?", "yesno", true),
        ]),
        new("Anaesthetics", null,
        [
            new("anaesthetic_problems", "Have you had problems with an anaesthetic before (e.g. severe sickness, slow waking, breathing problems)?", "yesno", true, DetailsPrompt: "Please describe"),
            new("family_mh", "Has anyone in your family had a serious problem with an anaesthetic (e.g. malignant hyperthermia)?", "yesno", true, DetailsPrompt: "Please describe"),
            new("teeth", "Do you have dentures, crowns, caps or loose teeth?", "yesno", true),
            new("neck", "Do you have limited neck movement or trouble opening your mouth wide?", "yesno", true),
        ]),
        new("Lifestyle", null,
        [
            new("smoking", "Smoking or vaping", "choice", true, ["Never", "Stopped", "Smoke", "Vape"]),
            new("alcohol", "Alcohol (standard drinks a week)", "choice", true, ["None", "1–10", "11–20", "More than 20"]),
        ]),
        new("Getting around and support", null,
        [
            new("mobility", "How do you get around?", "choice", true, ["Independently", "Walking stick or frame", "Wheelchair"]),
            new("interpreter", "Would you like an interpreter?", "yesno", true, DetailsPrompt: "Which language?"),
            new("cultural", "Is there anything about your culture, beliefs or whānau we should know to support you?", "multiline"),
        ]),
        new("Declaration", null,
        [
            new("confirm", "The information I have given is correct to the best of my knowledge.", "confirm", true),
        ]),
    ];

    public static IEnumerable<Question> AllQuestions => Sections.SelectMany(s => s.Questions);

    public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string> a)
    {
        var errors = new List<string>();
        foreach (var q in AllQuestions)
        {
            a.TryGetValue(q.Id, out var v);
            if (q.Required && string.IsNullOrWhiteSpace(v)) { errors.Add($"Please answer: {q.Text}"); continue; }
            if (string.IsNullOrWhiteSpace(v)) continue;
            switch (q.Type)
            {
                case "number" when !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) || n <= 0 || n > 400:
                    errors.Add($"Please enter a number for: {q.Text}"); break;
                case "yesno" when !YesNo.Contains(v):
                case "choice" when !q.Options!.Contains(v):
                    errors.Add($"Please choose an answer for: {q.Text}"); break;
                case "confirm" when v != "Yes":
                    errors.Add("Please tick the declaration."); break;
            }
            if (q.DetailsPrompt is not null && v == "Yes" && string.IsNullOrWhiteSpace(a.GetValueOrDefault(q.Id + "_details")))
                errors.Add($"Please give details for: {q.Text}");
        }
        foreach (var (k, v) in a)
            if (v.Length > 2000) errors.Add($"Answer too long ({k}).");
        return errors;
    }

    /// <summary>Automatic triage flags (ADR 0011). A nurse always reviews; these only suggest a starting category.</summary>
    public static (IReadOnlyList<FormFlag> Flags, TriageCategory Suggested) Assess(IReadOnlyDictionary<string, string> a)
    {
        var f = new List<FormFlag>();
        bool Yes(string id) => a.GetValueOrDefault(id) == "Yes";
        string D(string id) => a.GetValueOrDefault(id + "_details") is { Length: > 0 } d ? $": {d}" : "";
        if (decimal.TryParse(a.GetValueOrDefault("height_cm"), NumberStyles.Number, CultureInfo.InvariantCulture, out var h) && h > 50
            && decimal.TryParse(a.GetValueOrDefault("weight_kg"), NumberStyles.Number, CultureInfo.InvariantCulture, out var w) && w > 0)
        {
            var bmi = Math.Round(w / (h / 100 * (h / 100)), 1);
            if (bmi >= 40) f.Add(new("Red", $"BMI {bmi} (40 or more)"));
            else if (bmi >= 35) f.Add(new("Amber", $"BMI {bmi} (35–39.9)"));
            else f.Add(new("Info", $"BMI {bmi}"));
        }
        if (Yes("pacemaker")) f.Add(new("Red", "Pacemaker or implanted defibrillator"));
        if (Yes("family_mh")) f.Add(new("Red", "Family history of anaesthetic problems (possible malignant hyperthermia)" + D("family_mh")));
        if (Yes("heart")) f.Add(new("Amber", "Heart condition" + D("heart")));
        if (Yes("sleep_apnoea")) f.Add(new("Amber", "Sleep apnoea / snoring / CPAP"));
        if (a.GetValueOrDefault("diabetes") == "Yes – insulin") f.Add(new("Amber", "Insulin-treated diabetes"));
        else if (a.GetValueOrDefault("diabetes") is { } dm && dm.StartsWith("Yes", StringComparison.Ordinal)) f.Add(new("Info", "Diabetes (" + dm[6..] + ")"));
        if (Yes("blood_thinners")) f.Add(new("Amber", "Blood thinner" + D("blood_thinners")));
        if (Yes("bleeding")) f.Add(new("Amber", "Bleeding/clotting history" + D("bleeding")));
        if (Yes("anaesthetic_problems")) f.Add(new("Amber", "Previous anaesthetic problem" + D("anaesthetic_problems")));
        if (Yes("neck")) f.Add(new("Amber", "Limited neck movement / mouth opening (airway)"));
        if (Yes("allergies")) f.Add(new("Amber", "Allergy" + D("allergies")));
        if (Yes("latex")) f.Add(new("Amber", "Latex allergy"));
        if (a.GetValueOrDefault("pregnant") is "Possibly" or "Yes") f.Add(new("Amber", "Possible pregnancy"));
        if (Yes("infection")) f.Add(new("Amber", "Resistant organism risk – infection control"));
        if (Yes("kidney_liver")) f.Add(new("Amber", "Kidney or liver disease" + D("kidney_liver")));
        if (Yes("stroke")) f.Add(new("Amber", "Stroke/TIA/epilepsy" + D("stroke")));
        if (Yes("lives_alone")) f.Add(new("Info", "Lives alone – confirm the support person will stay 24 hours"));
        if (a.GetValueOrDefault("smoking") is "Smoke" or "Vape") f.Add(new("Info", "Smokes or vapes"));
        if (a.GetValueOrDefault("alcohol") == "More than 20") f.Add(new("Amber", "Alcohol more than 20 standard drinks a week"));
        if (Yes("interpreter")) f.Add(new("Info", "Interpreter requested" + D("interpreter")));
        if (a.GetValueOrDefault("mobility") is "Wheelchair") f.Add(new("Info", "Uses a wheelchair"));
        var cat = f.Any(x => x.Level == "Red") ? TriageCategory.Red : f.Any(x => x.Level == "Amber") ? TriageCategory.Amber : TriageCategory.Green;
        return (f, cat);
    }
}

public sealed record PublicForm(string Status, string? GivenName, string Clinic, DateTimeOffset ExpiresAt);
public sealed record FormRow(Guid Id, Guid PatientId, string PatientName, string? Nhi, string Mrn, string Status, DateTimeOffset CreatedAt, string CreatedBy,
    DateTimeOffset ExpiresAt, DateTimeOffset? SubmittedAt, string? Suggested, string? Category, string? TriagedBy, IReadOnlyList<FormFlag> Flags);

public sealed class FormService(FormsDbContext db, PatientsDbContext patients, PatientService patientService, AuditWriter audit, TimeProvider clock)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(14);
    public const int MaxAttempts = 5;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Creates a questionnaire link for a patient. The token is returned once and only its hash is stored.</summary>
    public async Task<(FormRequest? Request, string? Token, string? Error)> CreateAsync(Guid patientId, Guid? bookingId, string user, CancellationToken ct = default)
    {
        if (!await patients.Patients.AnyAsync(p => p.Id == patientId, ct)) return (null, null, "Patient not found.");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow();
        foreach (var old in await db.Requests.Where(r => r.PatientId == patientId && (r.Status == FormStatus.Sent || r.Status == FormStatus.Opened)).ToListAsync(ct))
            old.Status = FormStatus.Cancelled;
        var r = new FormRequest { PatientId = patientId, BookingId = bookingId, TokenHash = Hash(token), CreatedAt = now, CreatedBy = user, ExpiresAt = now + LinkLifetime };
        db.Requests.Add(r);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "FormRequest", r.Id.ToString(), patientId.ToString(), reason: "Pre-admission questionnaire link created", ct: ct);
        return (r, token, null);
    }

    private async Task<FormRequest?> ByToken(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var r = await db.Requests.SingleOrDefaultAsync(x => x.TokenHash == Hash(token), ct);
        if (r is not null && r.ExpiresAt < clock.GetUtcNow() && r.Status is FormStatus.Sent or FormStatus.Opened) { r.Status = FormStatus.Expired; await db.SaveChangesAsync(ct); }
        return r;
    }

    public async Task<PublicForm?> PublicInfoAsync(string token, CancellationToken ct = default)
    {
        var r = await ByToken(token, ct);
        if (r is null) return null;
        var status = r.Status switch { FormStatus.Sent or FormStatus.Opened => "open", FormStatus.Submitted or FormStatus.Triaged => "submitted", _ => "closed" };
        return new PublicForm(status, null, "Tasman Day Surgery", r.ExpiresAt);
    }

    /// <summary>Second factor for the patient link: date of birth. Five wrong attempts lock the link for 30 minutes.</summary>
    public async Task<(bool Ok, string? Error, FormRequest? Request, string? GivenName)> VerifyAsync(string token, DateOnly dob, CancellationToken ct = default)
    {
        var r = await ByToken(token, ct);
        var now = clock.GetUtcNow();
        if (r is null || r.Status is not (FormStatus.Sent or FormStatus.Opened)) return (false, "This link is no longer active. Please contact us.", null, null);
        if (r.LockedUntil > now) return (false, "Too many attempts. Please try again in 30 minutes or contact us.", null, null);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == r.PatientId, ct);
        if (p.DateOfBirth != dob)
        {
            r.FailedAttempts++;
            if (r.FailedAttempts >= MaxAttempts) { r.LockedUntil = now.AddMinutes(30); r.FailedAttempts = 0; }
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync("patient-link", "FormVerifyFailed", "FormRequest", r.Id.ToString(), r.PatientId.ToString(), ct: ct);
            return (false, "That date of birth does not match our records.", null, null);
        }
        r.FailedAttempts = 0;
        if (r.Status == FormStatus.Sent) { r.Status = FormStatus.Opened; r.OpenedAt = now; await db.SaveChangesAsync(ct); }
        return (true, null, r, p.PreferredName ?? p.GivenNames.Split(' ')[0]);
    }

    public async Task<(bool Ok, IReadOnlyList<string> Errors)> SubmitAsync(string token, DateOnly dob, Dictionary<string, string> answers, bool final, CancellationToken ct = default)
    {
        var (ok, error, r, _) = await VerifyAsync(token, dob, ct);
        if (!ok) return (false, [error!]);
        var known = Questionnaire.AllQuestions.SelectMany(q => new[] { q.Id, q.Id + "_details" }).ToHashSet();
        var clean = answers.Where(kv => known.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => (kv.Value ?? "").Trim());
        if (clean.Values.Any(v => v.Length > 2000)) return (false, ["An answer is too long."]);
        if (final)
        {
            var errors = Questionnaire.Validate(clean);
            if (errors.Count > 0) return (false, errors);
        }
        r!.AnswersJson = JsonSerializer.Serialize(clean, Json);
        if (final)
        {
            var (flags, cat) = Questionnaire.Assess(clean);
            r.FlagsJson = JsonSerializer.Serialize(flags, Json);
            r.SuggestedCategory = cat;
            r.Status = FormStatus.Submitted;
            r.SubmittedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("patient-link", final ? "FormSubmitted" : "FormSaved", "FormRequest", r.Id.ToString(), r.PatientId.ToString(), ct: ct);
        return (true, []);
    }

    public static Dictionary<string, string> Draft(FormRequest r) =>
        r.AnswersJson is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(r.AnswersJson, Json) ?? [];

    public async Task<IReadOnlyList<FormRow>> ListAsync(string view, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Requests.AsNoTracking().AsQueryable();
        if (patientId is not null) q = q.Where(r => r.PatientId == patientId);
        q = view switch
        {
            "to-triage" => q.Where(r => r.Status == FormStatus.Submitted),
            "waiting" => q.Where(r => r.Status == FormStatus.Sent || r.Status == FormStatus.Opened),
            "triaged" => q.Where(r => r.Status == FormStatus.Triaged),
            _ => q
        };
        var list = await q.OrderByDescending(r => r.SubmittedAt ?? r.CreatedAt).Take(300).ToListAsync(ct);
        var ids = list.Select(r => r.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return list.Select(r =>
        {
            var p = people.GetValueOrDefault(r.PatientId);
            var flags = r.FlagsJson is null ? [] : JsonSerializer.Deserialize<List<FormFlag>>(r.FlagsJson, Json) ?? [];
            return new FormRow(r.Id, r.PatientId, p is null ? "" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, p?.Mrn ?? "", r.Status.ToString(),
                r.CreatedAt, r.CreatedBy, r.ExpiresAt, r.SubmittedAt, r.SuggestedCategory?.ToString(), r.TriageCategory?.ToString(), r.TriagedBy, flags);
        }).ToList();
    }

    public async Task<(FormRow Row, FormRequest Request, Dictionary<string, string> Answers)?> GetAsync(Guid id, string user, string? ws, CancellationToken ct = default)
    {
        var r = await db.Requests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return null;
        await audit.WriteAsync(user, "View", "FormRequest", r.Id.ToString(), r.PatientId.ToString(), workstation: ws, ct: ct);
        var row = (await ListAsync("all", r.PatientId, ct)).Single(x => x.Id == id);
        return (row, r, Draft(r));
    }

    public async Task<string?> TriageAsync(Guid id, TriageCategory category, string outcome, string? notes, bool addAllergyAlert, string user, CancellationToken ct = default)
    {
        var r = await db.Requests.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Form not found.";
        if (r.Status is not (FormStatus.Submitted or FormStatus.Triaged)) return "Only submitted forms can be triaged.";
        if (string.IsNullOrWhiteSpace(outcome)) return "Choose a triage outcome.";
        r.TriageCategory = category;
        r.TriageOutcome = outcome.Trim().Length > 100 ? outcome.Trim()[..100] : outcome.Trim();
        r.TriageNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        r.TriagedBy = user;
        r.TriagedAt = clock.GetUtcNow();
        r.Status = FormStatus.Triaged;
        await db.SaveChangesAsync(ct);
        if (addAllergyAlert)
        {
            var a = Draft(r);
            var text = a.GetValueOrDefault("allergies") == "Yes" ? a.GetValueOrDefault("allergies_details") : null;
            if (a.GetValueOrDefault("latex") == "Yes") text = string.IsNullOrEmpty(text) ? "Latex" : text + "; latex";
            if (!string.IsNullOrWhiteSpace(text))
                await patientService.AddAlertAsync(r.PatientId, AlertType.Allergy, AlertSeverity.Warning, $"{text} (patient-reported, pre-admission form)", user, ct);
        }
        await audit.WriteAsync(user, "FormTriaged", "FormRequest", r.Id.ToString(), r.PatientId.ToString(), reason: $"{category}: {r.TriageOutcome}", ct: ct);
        return null;
    }
}
