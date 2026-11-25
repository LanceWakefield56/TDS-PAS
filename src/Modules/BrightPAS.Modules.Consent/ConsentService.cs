using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Consent;

/// <summary>The sheet as the patient saw it, kept with the consent.</summary>
public sealed record SheetSnapshot(string Code, int Version, string Name, string About, string Benefits, string Alternatives, string NoTreatment,
    string? Anaesthetic, string? Preparation, string? Recovery, IReadOnlyList<SnapshotRisk> Risks, IReadOnlyList<SnapshotLink> Links, string? ApprovedBy);
public sealed record SnapshotRisk(string Text, string Frequency, bool Serious);
public sealed record SnapshotLink(string Title, string Url);

/// <summary>One section the patient reads (and marks as read) before signing.</summary>
public sealed record ConsentSection(string Key, string Title, string? Text, IReadOnlyList<SnapshotRisk>? Risks);

public sealed record SignInput(SignerKind Signer, string? SignerName, string? Relationship, string? Interpreter, string? SignatureBase64,
    bool Understood, bool HadChanceToAsk, bool Agree);

public sealed record PaperInput(Guid BookingId, Guid? SheetId, SignerKind Signer, string? SignerName, string? Relationship, string? Interpreter, DateOnly SignedOn,
    string FileName, string ContentBase64);

public sealed record ConsentRow(Guid Id, string Number, Guid PatientId, string Patient, string? Nhi, Guid BookingId, DateOnly? SurgeryDate, string Procedure, string? Clinician,
    string Status, string Method, string Sheet, DateTimeOffset? SentAt, DateTimeOffset? SignedAt, int OpenQuestions, IReadOnlyList<string> Flags);

/// <summary>A booking coming up that has no consent yet (the "To prepare" list).</summary>
public sealed record PrepareRow(Guid BookingId, Guid PatientId, string Patient, string? Nhi, DateOnly SurgeryDate, string Procedure, string? Clinician, Guid? SuggestedSheetId, string? SuggestedSheet);

/// <summary>Everything the signed PDF shows.</summary>
public sealed record ConsentPdfModel(string Number, string Patient, string? Nhi, DateOnly DateOfBirth, string Procedure, string Side, string? Clinician, SheetSnapshot Sheet,
    IReadOnlyList<string> ExtraRisks, IReadOnlyList<(string Question, string? Answer)> Questions, string Method, string? Signer, string? SignerName, string? Relationship,
    string? Interpreter, byte[]? Signature, DateTimeOffset? SignedAt, string? ConfirmedBy, DateTimeOffset? ConfirmedAt, string? ConfirmNote, string ClinicName);

/// <summary>Draws the signed consent as a PDF (MigraDoc in the web host).</summary>
public interface IConsentPdf
{
    byte[] Render(ConsentPdfModel model);
}

/// <summary>
/// Consent for a booking: prepare from a sheet, send the patient a secret link, the patient reads, asks and signs (or paper is scanned in),
/// a clinician confirms on the day; withdrawn at any time before surgery. Every step is audited against the patient.
/// </summary>
public sealed class ConsentService(ConsentDbContext db, TheatreDbContext theatre, PatientsDbContext patients, PasDbContext core, DocumentService documents,
    ReminderService reminders, IDocumentCipher cipher, IConsentPdf pdf, AuditWriter audit, TimeProvider clock)
{
    public const int MaxDobAttempts = 5;
    public const string PatientUser = "patient-link";
    private DateOnly Today => NzTime.Today(clock);
    private static readonly BookingStatus[] Gone = [BookingStatus.Cancelled, BookingStatus.DidNotAttend];

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // ------------------------------------------------------------------ settings
    public async Task<ConsentSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(ct);
        if (s is not null) return s;
        db.Settings.Add(s = new ConsentSettings());
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(string? publicUrl, int linkDays, bool requireBeforeConfirm, int prepareDaysAhead, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var url = string.IsNullOrWhiteSpace(publicUrl) ? null : publicUrl.Trim().TrimEnd('/');
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)) errors.Add("The public web address must start with https://.");
        if (linkDays is < 2 or > 60) errors.Add("Links must last between 2 and 60 days.");
        if (prepareDaysAhead is < 1 or > 120) errors.Add("Show bookings 1 to 120 days ahead.");
        if (errors.Count > 0) return errors;
        var s = await SettingsAsync(ct);
        (s.PublicUrl, s.LinkDays, s.RequireBeforeConfirm, s.PrepareDaysAhead, s.UpdatedAt, s.UpdatedBy) = (url, linkDays, requireBeforeConfirm, prepareDaysAhead, clock.GetUtcNow(), user);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Settings", "ConsentSettings", "1", reason: $"Link {linkDays} days; required before confirming: {(requireBeforeConfirm ? "yes" : "no")}", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ snapshot and sections
    public static SheetSnapshot Snap(ConsentSheet s) => new(s.Code, s.Version, s.Name, s.About, s.Benefits, s.Alternatives, s.NoTreatment, s.Anaesthetic, s.Preparation, s.Recovery,
        [.. s.Risks.OrderBy(r => r.Serious ? 0 : 1).ThenBy(r => r.Frequency).ThenBy(r => r.Order).Select(r => new SnapshotRisk(r.Text, ConsentSheetService.FrequencyWords[r.Frequency], r.Serious))],
        [.. s.Links.OrderBy(l => l.Order).Select(l => new SnapshotLink(l.Title, l.Url))], s.ApprovedBy);

    public static SheetSnapshot ReadSnapshot(ConsentRecord c) => JsonSerializer.Deserialize<SheetSnapshot>(c.Snapshot, JsonSerializerOptions.Web)!;

    public static IReadOnlyList<string> ExtraRiskLines(ConsentRecord c) =>
        (c.ExtraRisks ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The sections in reading order (sections the sheet leaves empty are not shown).</summary>
    public static IReadOnlyList<ConsentSection> Sections(ConsentRecord c)
    {
        var s = ReadSnapshot(c);
        var risks = s.Risks.Concat(ExtraRiskLines(c).Select(r => new SnapshotRisk(r, "Applies to you in particular", true))).ToList();
        var list = new List<ConsentSection>
        {
            new("about", "What the procedure is", s.About, null),
            new("benefits", "Benefits", s.Benefits, null),
            new("risks", "Risks", null, risks),
            new("alternatives", "Alternatives", s.Alternatives, null),
            new("notreatment", "If you do not have the procedure", s.NoTreatment, null),
        };
        if (s.Anaesthetic is not null) list.Add(new("anaesthetic", "Your anaesthetic", s.Anaesthetic, null));
        if (s.Preparation is not null) list.Add(new("preparation", "Getting ready", s.Preparation, null));
        if (s.Recovery is not null) list.Add(new("recovery", "After the procedure", s.Recovery, null));
        return list;
    }

    private static HashSet<string> ReadKeys(ConsentRecord c) => (c.SectionsRead ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    // ------------------------------------------------------------------ lists
    public async Task<IReadOnlyList<PrepareRow>> ToPrepareAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var today = Today;
        var until = today.AddDays(s.PrepareDaysAhead);
        var books = await (from b in theatre.Bookings.AsNoTracking()
                           join x in theatre.Sessions.AsNoTracking() on b.SessionId equals x.Id
                           where x.Date >= today && x.Date <= until && !x.IsCancelled && (b.Status == BookingStatus.Booked || b.Status == BookingStatus.Confirmed)
                           select new { b.Id, b.PatientId, b.Procedure, x.Date, x.SurgeonId }).ToListAsync(ct);
        var ids = books.Select(b => b.Id).ToList();
        var have = await db.Consents.AsNoTracking().Where(c => ids.Contains(c.BookingId) && c.Status != ConsentStatus.Withdrawn).Select(c => c.BookingId).ToListAsync(ct);
        var todo = books.Where(b => !have.Contains(b.Id)).ToList();
        var names = await PatientNamesAsync(todo.Select(b => b.PatientId), ct);
        var docs = await core.Clinicians.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var sheets = new ConsentSheetService(db, audit, clock);
        var rows = new List<PrepareRow>();
        foreach (var b in todo.OrderBy(b => b.Date))
        {
            var sug = await sheets.SuggestAsync(b.Procedure, ct);
            var (n, nhi) = names.GetValueOrDefault(b.PatientId, ("Unknown", null));
            rows.Add(new(b.Id, b.PatientId, n, nhi, b.Date, b.Procedure, docs.GetValueOrDefault(b.SurgeonId), sug?.Id, sug is null ? null : $"{sug.Name} (v{sug.Version})"));
        }
        return rows;
    }

    private async Task<Dictionary<Guid, (string Name, string? Nhi)>> PatientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await patients.Patients.AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => ($"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p.Nhi), ct);
    }

    public async Task<IReadOnlyList<ConsentRow>> ListAsync(string view, Guid? patientId = null, CancellationToken ct = default)
    {
        var q = db.Consents.AsNoTracking().Include(c => c.Questions).AsQueryable();
        if (patientId is { } pid) q = q.Where(c => c.PatientId == pid);
        q = view switch
        {
            "sent" => q.Where(c => c.Status == ConsentStatus.Sent || c.Status == ConsentStatus.Draft),
            "questions" => q.Where(c => c.Questions.Any(x => x.AnsweredAt == null) && c.Status != ConsentStatus.Withdrawn),
            "signed" => q.Where(c => c.Status == ConsentStatus.Signed),
            "confirmed" => q.Where(c => c.Status == ConsentStatus.Confirmed),
            "withdrawn" => q.Where(c => c.Status == ConsentStatus.Withdrawn),
            _ => q,
        };
        var rows = await q.ToListAsync(ct);
        var names = await PatientNamesAsync(rows.Select(r => r.PatientId), ct);
        var bookingIds = rows.Select(r => r.BookingId).ToList();
        var dates = await (from b in theatre.Bookings.AsNoTracking() join x in theatre.Sessions.AsNoTracking() on b.SessionId equals x.Id
                           where bookingIds.Contains(b.Id) select new { b.Id, x.Date }).ToDictionaryAsync(b => b.Id, b => b.Date, ct);
        var ages = await patients.Patients.AsNoTracking().Where(p => rows.Select(r => r.PatientId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DateOfBirth, ct);
        return rows.Select(c =>
        {
            var (n, nhi) = names.GetValueOrDefault(c.PatientId, ("Unknown", null));
            DateOnly? date = dates.TryGetValue(c.BookingId, out var d) ? d : null;
            return new ConsentRow(c.Id, c.Number, c.PatientId, n, nhi, c.BookingId, date, c.Procedure, c.ClinicianName, c.Status.ToString(), c.Method.ToString(),
                SheetName(c), c.LinkSentAt, c.SignedAt, c.Questions.Count(x => x.AnsweredAt is null), Flags(c, ages.GetValueOrDefault(c.PatientId), date, Today));
        }).OrderBy(r => r.SurgeryDate ?? DateOnly.MaxValue).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
    }

    private static string SheetName(ConsentRecord c)
    {
        var s = ReadSnapshot(c);
        return $"{s.Name} (v{s.Version.ToString(CultureInfo.InvariantCulture)})";
    }

    /// <summary>Points for staff to check (advice only).</summary>
    public static IReadOnlyList<string> Flags(ConsentRecord c, DateOnly dob, DateOnly? surgery, DateOnly today)
    {
        var f = new List<string>();
        if (c.Questions.Any(q => q.AnsweredAt is null)) f.Add("The patient has a question waiting for an answer.");
        if (c.Status is ConsentStatus.Draft or ConsentStatus.Sent && surgery is { } s && s <= today.AddDays(2)) f.Add("Not signed and the procedure is within 2 days.");
        if (c.Status == ConsentStatus.Sent && c.LinkExpiresAt is { } exp && exp.NzDate() < today) f.Add("The link has expired – send a new one.");
        if (c.Signer == SignerKind.Patient && dob != default && NzTime.Age(dob, c.SignedAt?.NzDate() ?? today) < 16) f.Add("The patient is under 16 and signed for themselves – check they are competent to consent, or ask a parent or guardian.");
        if (c.Signer is not null and not SignerKind.Patient) f.Add($"Signed by {c.SignerName} ({c.SignerRelationship ?? c.Signer.ToString()}) – check their authority.");
        if (c.Interpreter is not null) f.Add($"Interpreter: {c.Interpreter}.");
        if (c.Status == ConsentStatus.Signed && surgery is { } d && d < today) f.Add("The procedure date has passed and the consent was not confirmed on the day.");
        return f;
    }

    public async Task<ConsentRecord?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Consents.AsNoTracking().Include(c => c.Questions).SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<int> OpenQuestionsAsync(CancellationToken ct = default) =>
        await db.Consents.CountAsync(c => c.Status != ConsentStatus.Withdrawn && c.Questions.Any(q => q.AnsweredAt == null), ct);

    /// <summary>The live consent for a booking (not withdrawn), if any.</summary>
    public async Task<ConsentRecord?> ForBookingAsync(Guid bookingId, CancellationToken ct = default) =>
        await db.Consents.AsNoTracking().Include(c => c.Questions).Where(c => c.BookingId == bookingId && c.Status != ConsentStatus.Withdrawn).OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

    // ------------------------------------------------------------------ prepare and send
    private async Task<(TheatreBooking? Booking, DateOnly Date, Guid SurgeonId)> BookingAsync(Guid bookingId, CancellationToken ct)
    {
        var x = await (from b in theatre.Bookings.AsNoTracking() join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                       where b.Id == bookingId select new { b, s.Date, s.SurgeonId }).SingleOrDefaultAsync(ct);
        return x is null ? (null, default, default) : (x.b, x.Date, x.SurgeonId);
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var n = $"CN{s.NextNumber.ToString("D6", CultureInfo.InvariantCulture)}";
        s.NextNumber++;
        return n;
    }

    public async Task<(ConsentRecord? Consent, IReadOnlyList<string> Errors)> PrepareAsync(Guid bookingId, Guid sheetId, string? extraRisks, string? noteToPatient, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var (b, _, surgeon) = await BookingAsync(bookingId, ct);
        if (b is null) return (null, ["Booking not found."]);
        if (Gone.Contains(b.Status) || b.Status == BookingStatus.Discharged) errors.Add("This booking is finished or cancelled.");
        var sheet = await db.Sheets.AsNoTracking().Include(s => s.Risks).Include(s => s.Links).SingleOrDefaultAsync(s => s.Id == sheetId, ct);
        if (sheet is null || sheet.Status != SheetStatus.Published) errors.Add("Choose a published information sheet.");
        if ((extraRisks ?? "").Length > 2000) errors.Add("Keep the extra risks under 2,000 characters.");
        if ((noteToPatient ?? "").Length > 1000) errors.Add("Keep the note to the patient under 1,000 characters.");
        if (await db.Consents.AnyAsync(c => c.BookingId == bookingId && c.Status != ConsentStatus.Withdrawn, ct)) errors.Add("This booking already has a consent – open it instead.");
        if (errors.Count > 0) return (null, errors);
        var clinician = await core.Clinicians.AsNoTracking().Where(c => c.Id == surgeon).Select(c => c.Name).SingleOrDefaultAsync(ct);
        var c = new ConsentRecord
        {
            Number = await NextNumberAsync(ct),
            PatientId = b.PatientId,
            BookingId = b.Id,
            SheetId = sheet!.Id,
            Procedure = b.Procedure,
            Laterality = b.Laterality,
            ClinicianId = surgeon == Guid.Empty ? null : surgeon,
            ClinicianName = clinician,
            Snapshot = JsonSerializer.Serialize(Snap(sheet), JsonSerializerOptions.Web),
            ExtraRisks = Blank(extraRisks),
            NoteToPatient = Blank(noteToPatient),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
        };
        db.Consents.Add(c);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Prepare", "Consent", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number}: {c.Procedure} – {sheet.Code} v{sheet.Version.ToString(CultureInfo.InvariantCulture)}", ct: ct);
        return (c, []);
    }

    public async Task<string?> EditDraftAsync(Guid id, string? extraRisks, string? noteToPatient, string user, CancellationToken ct = default)
    {
        var c = await db.Consents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Consent not found.";
        if (c.Status != ConsentStatus.Draft) return "The consent has been sent – withdraw it and prepare a new one to change what the patient reads.";
        if ((extraRisks ?? "").Length > 2000 || (noteToPatient ?? "").Length > 1000) return "The text is too long.";
        c.ExtraRisks = Blank(extraRisks); c.NoteToPatient = Blank(noteToPatient);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "EditDraft", "Consent", c.Id.ToString(), c.PatientId.ToString(), ct: ct);
        return null;
    }

    /// <summary>Makes a new secret link (older links stop working) and texts/emails it to the patient. Returns the link so staff can also copy it.</summary>
    public async Task<(string? Link, bool Delivered, string? Error)> SendAsync(Guid id, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s.PublicUrl is not { } linkBase) return (null, false, "Set the public web address in Consent › Settings first.");
        var c = await db.Consents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return (null, false, "Consent not found.");
        if (c.Status is not (ConsentStatus.Draft or ConsentStatus.Sent)) return (null, false, "This consent is already signed or withdrawn.");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.GetUtcNow();
        c.TokenHash = Hash(token); c.LinkSentAt = now; c.LinkExpiresAt = now.AddDays(s.LinkDays); c.FailedAttempts = 0; c.LockedUntil = null; c.Status = ConsentStatus.Sent;
        await db.SaveChangesAsync(ct);
        var link = $"{linkBase.TrimEnd('/')}/e/{token}";
        var sent = await reminders.QueueConsentLinkAsync(c.PatientId, c.BookingId, link, $"consent:{c.Id}:{now:yyyyMMddHHmmss}", user, ct);
        var delivered = sent.Any(m => m.Status != MessageStatus.Suppressed);
        await audit.WriteAsync(user, "SendLink", "Consent", c.Id.ToString(), c.PatientId.ToString(), reason: delivered ? "Link sent by text/email" : "Link made – no mobile or email, give it to the patient another way", ct: ct);
        return (link, delivered, null);
    }

    public async Task<string?> AnswerAsync(Guid questionId, string answer, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(answer)) return "Write the answer.";
        if (answer.Length > 2000) return "Keep the answer under 2,000 characters.";
        var q = await db.Questions.SingleOrDefaultAsync(x => x.Id == questionId, ct);
        if (q is null) return "Question not found.";
        var c = await db.Consents.SingleAsync(x => x.Id == q.ConsentId, ct);
        q.Answer = answer.Trim(); q.AnsweredAt = clock.GetUtcNow(); q.AnsweredBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "AnswerQuestion", "Consent", c.Id.ToString(), c.PatientId.ToString(), ct: ct);
        return null;
    }

    // ------------------------------------------------------------------ patient link (no staff sign-in)
    private async Task<ConsentRecord?> ByToken(string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 100 ? null : await db.Consents.Include(c => c.Questions).SingleOrDefaultAsync(c => c.TokenHash == Hash(token), ct);

    /// <summary>open, signed (thank you page), or closed.</summary>
    public async Task<string?> PublicStatusAsync(string token, CancellationToken ct = default)
    {
        var c = await ByToken(token, ct);
        if (c is null) return null;
        return c.Status switch
        {
            ConsentStatus.Sent when c.LinkExpiresAt > clock.GetUtcNow() => "open",
            ConsentStatus.Signed or ConsentStatus.Confirmed => "signed",
            _ => "closed",
        };
    }

    public async Task<(ConsentRecord? Consent, string? Error, Patient? Patient)> VerifyAsync(string token, DateOnly dob, bool signedOk = false, CancellationToken ct = default)
    {
        var c = await ByToken(token, ct);
        var now = clock.GetUtcNow();
        var active = c is not null && (c.Status == ConsentStatus.Sent && c.LinkExpiresAt > now || signedOk && c.Status == ConsentStatus.Signed);
        if (!active) return (null, "This link is no longer active. Please phone us if you have any questions.", null);
        if (c!.LockedUntil > now) return (null, "Too many attempts. Please try again in 30 minutes or phone us.", null);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == c.PatientId, ct);
        if (p.DateOfBirth != dob)
        {
            c.FailedAttempts++;
            if (c.FailedAttempts >= MaxDobAttempts) { c.LockedUntil = now.AddMinutes(30); c.FailedAttempts = 0; }
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(PatientUser, "ConsentVerifyFailed", "Consent", c.Id.ToString(), c.PatientId.ToString(), ct: ct);
            return (null, "That date of birth does not match our records.", null);
        }
        c.FailedAttempts = 0;
        if (c.OpenedAt is null) { c.OpenedAt = now; await audit.WriteAsync(PatientUser, "ConsentOpened", "Consent", c.Id.ToString(), c.PatientId.ToString(), ct: ct); }
        await db.SaveChangesAsync(ct);
        return (c, null, p);
    }

    public async Task<(string? Error, IReadOnlyList<string> Read)> MarkReadAsync(string token, DateOnly dob, string section, CancellationToken ct = default)
    {
        var (c, error, _) = await VerifyAsync(token, dob, ct: ct);
        if (c is null) return (error, []);
        if (Sections(c).All(s => s.Key != section)) return ("Unknown section.", []);
        var read = ReadKeys(c);
        if (read.Add(section)) { c.SectionsRead = string.Join(",", read.Order(StringComparer.Ordinal)); await db.SaveChangesAsync(ct); }
        return (null, [.. read]);
    }

    public async Task<string?> AskAsync(string token, DateOnly dob, string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Write your question.";
        if (text.Length > 1000) return "Please keep your question under 1,000 characters (or phone us).";
        var (c, error, _) = await VerifyAsync(token, dob, ct: ct);
        if (c is null) return error;
        if (c.Questions.Count(q => q.AnsweredAt is null) >= 5) return "You have several questions waiting – we will answer them soon, or please phone us.";
        db.Questions.Add(new ConsentQuestion { ConsentId = c.Id, Text = text.Trim(), AskedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(PatientUser, "ConsentQuestion", "Consent", c.Id.ToString(), c.PatientId.ToString(), ct: ct);
        return null;
    }

    public async Task<IReadOnlyList<string>> SignAsync(string token, DateOnly dob, SignInput s, CancellationToken ct = default)
    {
        var (c, error, p) = await VerifyAsync(token, dob, ct: ct);
        if (c is null) return [error!];
        var errors = new List<string>();
        var read = ReadKeys(c);
        if (Sections(c).Any(x => !read.Contains(x.Key))) errors.Add("Please read every section and tick \"I have read this\" before signing.");
        if (c.Questions.Any(q => q.AnsweredAt is null)) errors.Add("We have not answered your question yet. You can sign once we have replied (or phone us).");
        if (!s.Understood || !s.HadChanceToAsk || !s.Agree) errors.Add("Please tick all three statements to sign.");
        if (!Enum.IsDefined(s.Signer)) errors.Add("Say who is signing.");
        var name = Blank(s.SignerName);
        if (name is null || name.Length > 150) errors.Add("Type your full name as your signature.");
        if (s.Signer != SignerKind.Patient && string.IsNullOrWhiteSpace(s.Relationship)) errors.Add("Say how you are related to the patient, or your authority (for example: mother, welfare guardian, EPOA attorney).");
        if ((s.Interpreter ?? "").Length > 150 || (s.Relationship ?? "").Length > 100) errors.Add("Some details are too long.");
        byte[]? image = null;
        if (!string.IsNullOrWhiteSpace(s.SignatureBase64))
        {
            try { image = Convert.FromBase64String(s.SignatureBase64.Contains(',', StringComparison.Ordinal) ? s.SignatureBase64[(s.SignatureBase64.IndexOf(',', StringComparison.Ordinal) + 1)..] : s.SignatureBase64); }
            catch (FormatException) { errors.Add("The drawn signature could not be read – please draw it again."); }
            if (image is not null && (image.Length > 300_000 || DocumentService.DetectType(image) != "image/png")) errors.Add("The drawn signature could not be read – please draw it again.");
        }
        if (errors.Count > 0) return errors;
        var now = clock.GetUtcNow();
        c.Signer = s.Signer; c.SignerName = name; c.SignerRelationship = s.Signer == SignerKind.Patient ? null : Blank(s.Relationship); c.Interpreter = Blank(s.Interpreter);
        c.SignatureImage = image is null ? null : cipher.Protect(image); c.SignedAt = now; c.SignedBy = PatientUser; c.Status = ConsentStatus.Signed; c.Method = ConsentMethod.Online;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(PatientUser, "ConsentSigned", "Consent", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} signed online by {(s.Signer == SignerKind.Patient ? "the patient" : $"{name} ({c.SignerRelationship})")}", ct: ct);
        await FileAsync(c, p!, PatientUser, null, ct);
        return [];
    }

    /// <summary>The patient withdraws through their link (signed or not). Staff are told on the consent list.</summary>
    public async Task<string?> PatientWithdrawAsync(string token, DateOnly dob, string? reason, CancellationToken ct = default)
    {
        var (c, error, _) = await VerifyAsync(token, dob, signedOk: true, ct);
        if (c is null) return error;
        await WithdrawCoreAsync(c, string.IsNullOrWhiteSpace(reason) ? "Withdrawn by the patient online" : $"Withdrawn by the patient online: {reason.Trim()}", PatientUser, ct);
        return null;
    }

    public sealed record PublicView(string Number, string GivenName, string Procedure, string Side, string? Clinician, DateOnly? SurgeryDate, string? NoteToPatient,
        IReadOnlyList<ConsentSection> Sections, IReadOnlyList<SnapshotLink> Links, IReadOnlyList<string> Read, IReadOnlyList<QuestionAnswer> Questions, string Status);
    public sealed record QuestionAnswer(string Question, string? Answer);

    public async Task<PublicView> ViewAsync(ConsentRecord c, Patient p, CancellationToken ct = default)
    {
        var (_, date, _) = await BookingAsync(c.BookingId, ct);
        return new(c.Number, p.PreferredName ?? p.GivenNames.Split(' ')[0], c.Procedure, SideText(c.Laterality), c.ClinicianName, date == default ? null : date, c.NoteToPatient,
            Sections(c), ReadSnapshot(c).Links, [.. ReadKeys(c).Order(StringComparer.Ordinal)], [.. c.Questions.OrderBy(q => q.AskedAt).Select(q => new QuestionAnswer(q.Text, q.Answer))], c.Status.ToString());
    }

    public static string SideText(Laterality l) => l switch { Laterality.Left => "Left", Laterality.Right => "Right", Laterality.Bilateral => "Both sides", _ => "" };

    // ------------------------------------------------------------------ paper, confirm, withdraw
    /// <summary>A paper consent form signed in the rooms or at TDS, scanned in. Creates (or completes) the booking's consent as signed on paper.</summary>
    public async Task<(ConsentRecord? Consent, IReadOnlyList<string> Errors)> RecordPaperAsync(PaperInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.SignerName)) errors.Add("Enter the name of the person who signed.");
        if (r.Signer != SignerKind.Patient && string.IsNullOrWhiteSpace(r.Relationship)) errors.Add("Say how the person who signed is related to the patient, or their authority.");
        if (r.SignedOn > Today) errors.Add("The signing date cannot be in the future.");
        var (b, _, _) = await BookingAsync(r.BookingId, ct);
        if (b is null) errors.Add("Booking not found.");
        if (errors.Count > 0) return (null, errors);
        var c = await db.Consents.Include(x => x.Questions).Where(x => x.BookingId == r.BookingId && x.Status != ConsentStatus.Withdrawn).FirstOrDefaultAsync(ct);
        if (c is { Status: ConsentStatus.Signed or ConsentStatus.Confirmed }) return (null, ["This booking already has a signed consent."]);
        if (c is null)
        {
            if (r.SheetId is null)
            {
                var first = await new ConsentSheetService(db, audit, clock).SuggestAsync(b!.Procedure, ct);
                if (first is null) return (null, ["Choose the information sheet the paper form matches."]);
                r = r with { SheetId = first.Id };
            }
            var (made, e2) = await PrepareAsync(r.BookingId, r.SheetId!.Value, null, null, user, ct);
            if (made is null) return (null, e2);
            c = await db.Consents.Include(x => x.Questions).SingleAsync(x => x.Id == made.Id, ct);
        }
        var (doc, docErrors) = await documents.AddAsync(new NewPatientDocument(c.PatientId, c.BookingId, DocumentCategory.Consent, $"Consent {c.Number} (paper) – {c.Procedure}", r.FileName, r.ContentBase64, DocumentSource.Scan), user, ct: ct);
        if (doc is null) return (null, docErrors);
        c.Method = ConsentMethod.Paper; c.Status = ConsentStatus.Signed; c.Signer = r.Signer; c.SignerName = r.SignerName!.Trim(); c.SignerRelationship = r.Signer == SignerKind.Patient ? null : Blank(r.Relationship);
        c.Interpreter = Blank(r.Interpreter); c.SignedAt = NzTime.FromLocal(r.SignedOn, new TimeOnly(12, 0)); c.SignedBy = user; c.DocumentId = doc.Id; c.TokenHash = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ConsentPaper", "Consent", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} signed on paper {r.SignedOn:yyyy-MM-dd}", ct: ct);
        return (c, []);
    }

    /// <summary>On the day: a clinician goes through the consent with the patient and confirms it. Ticks the theatre "consent" check and files the confirmed copy.</summary>
    public async Task<string?> ConfirmAsync(Guid id, string? note, string user, CancellationToken ct = default)
    {
        var c = await db.Consents.Include(x => x.Questions).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Consent not found.";
        if (c.Status != ConsentStatus.Signed) return c.Status == ConsentStatus.Confirmed ? "This consent is already confirmed." : "The consent must be signed before it can be confirmed.";
        if (c.Questions.Any(q => q.AnsweredAt is null)) return "Answer the patient's question first.";
        var now = clock.GetUtcNow();
        c.Status = ConsentStatus.Confirmed; c.ConfirmedAt = now; c.ConfirmedBy = user; c.ConfirmNote = Blank(note) is { } n && n.Length <= 1000 ? n : null; c.TokenHash = null;
        if (!await theatre.Checks.AnyAsync(k => k.BookingId == c.BookingId && k.ItemKey == "consent", ct))
            theatre.Checks.Add(new BookingCheck { BookingId = c.BookingId, ItemKey = "consent", CheckedAt = now, CheckedBy = user });
        await theatre.SaveChangesAsync(ct);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ConsentConfirmed", "Consent", c.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Number} confirmed with the patient", ct: ct);
        if (c.Method == ConsentMethod.Online)
        {
            var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == c.PatientId, ct);
            await FileAsync(c, p, user, "Replaced by the copy confirmed on the day", ct);
        }
        return null;
    }

    public async Task<string?> WithdrawAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5) return "Give a reason (for example: the patient changed their mind; the booking changed).";
        var c = await db.Consents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return "Consent not found.";
        if (c.Status == ConsentStatus.Withdrawn) return "This consent is already withdrawn.";
        await WithdrawCoreAsync(c, reason.Trim(), user, ct);
        return null;
    }

    private async Task WithdrawCoreAsync(ConsentRecord c, string reason, string user, CancellationToken ct)
    {
        c.Status = ConsentStatus.Withdrawn; c.WithdrawnAt = clock.GetUtcNow(); c.WithdrawnBy = user; c.WithdrawnReason = Trunc(reason, 500); c.TokenHash = null;
        await theatre.Checks.Where(k => k.BookingId == c.BookingId && k.ItemKey == "consent").ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "ConsentWithdrawn", "Consent", c.Id.ToString(), c.PatientId.ToString(), reason: c.WithdrawnReason, ct: ct);
    }

    // ------------------------------------------------------------------ PDF
    public async Task<ConsentPdfModel> PdfModelAsync(ConsentRecord c, Patient p, CancellationToken ct = default)
    {
        var clinic = await core.Organisations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Tasman Day Surgery";
        return new(c.Number, $"{p.GivenNames} {p.FamilyName}", p.Nhi, p.DateOfBirth, c.Procedure, SideText(c.Laterality), c.ClinicianName, ReadSnapshot(c), ExtraRiskLines(c),
            [.. c.Questions.OrderBy(q => q.AskedAt).Select(q => (q.Text, q.Answer))], c.Method.ToString(), c.Signer?.ToString(), c.SignerName, c.SignerRelationship, c.Interpreter,
            c.SignatureImage is null ? null : cipher.Unprotect(c.SignatureImage), c.SignedAt, c.ConfirmedBy, c.ConfirmedAt, c.ConfirmNote, clinic);
    }

    public async Task<byte[]?> PdfAsync(Guid id, CancellationToken ct = default)
    {
        var c = await GetAsync(id, ct);
        if (c is null) return null;
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == c.PatientId, ct);
        return pdf.Render(await PdfModelAsync(c, p, ct));
    }

    private async Task FileAsync(ConsentRecord c, Patient p, string user, string? replaceReason, CancellationToken ct)
    {
        var bytes = pdf.Render(await PdfModelAsync(c, p, ct));
        var title = $"Consent {c.Number}{(c.Status == ConsentStatus.Confirmed ? " (confirmed)" : " (signed)")} – {c.Procedure}";
        var (doc, errors) = await documents.AddAsync(new NewPatientDocument(c.PatientId, c.BookingId, DocumentCategory.Consent, Trunc(title, 150), $"consent-{c.Number}.pdf", Convert.ToBase64String(bytes), DocumentSource.Portal), user, ct: ct);
        if (doc is null) throw new InvalidOperationException("The signed consent could not be filed: " + string.Join(" ", errors));
        if (replaceReason is not null && c.DocumentId is { } old) await documents.DeleteAsync(old, replaceReason, user, ct);
        c.DocumentId = doc.Id;
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ gate and merge
    /// <summary>For the booking gate: null when the booking may be confirmed.</summary>
    public async Task<string?> GateAsync(Guid bookingId, CancellationToken ct = default)
    {
        if (!(await SettingsAsync(ct)).RequireBeforeConfirm) return null;
        var c = await ForBookingAsync(bookingId, ct);
        return c is { Status: ConsentStatus.Signed or ConsentStatus.Confirmed } ? null : "The patient's consent is not signed yet (Consent). TDS requires it before a booking is confirmed.";
    }

    public async Task<int> MergeAsync(Guid survivorId, Guid duplicateId, CancellationToken ct = default) =>
        await db.Consents.Where(c => c.PatientId == duplicateId).ExecuteUpdateAsync(s => s.SetProperty(c => c.PatientId, survivorId), ct);

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string Trunc(string v, int max) => v.Length <= max ? v : v[..max];
}
