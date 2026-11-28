using BrightPAS.Core.Integration;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Reminders;

/// <summary>Installation defaults chosen in Setup (written to appsettings.Production.json). Used only when the settings row is first created.</summary>
public sealed record ReminderDefaults(SmsProvider SmsProvider = SmsProvider.Modica, EmailProvider EmailProvider = EmailProvider.Smtp);

public sealed record MessageRow(Guid Id, Guid? PatientId, string PatientName, string Mrn, Guid? BookingId, DateOnly? AppointmentDate, string Kind, string Channel,
    string Address, string? Subject, string Body, string Status, string? Provider, int Attempts, string? LastError, DateTimeOffset CreatedAt, string CreatedBy,
    DateTimeOffset? SentAt, DateTimeOffset? DeliveredAt);
public sealed record ReplyRow(Guid Id, string From, string Body, DateTimeOffset ReceivedAt, Guid? PatientId, string? PatientName, Guid? BookingId,
    string Meaning, string ActionTaken, string Status, string? HandledBy, DateTimeOffset? HandledAt, string? Note);
public sealed record TemplateRow(Guid Id, string Kind, string Channel, string Language, string? Subject, string Body, bool IsActive, DateTimeOffset UpdatedAt, string UpdatedBy);
public sealed record PreferenceView(Guid PatientId, bool AllowSms, bool AllowEmail, string Language, string? Mobile, string? Email, bool MobileOptedOut, bool EmailOptedOut);

public sealed class ReminderService(
    RemindersDbContext db, PatientsDbContext patients, TheatreDbContext theatre, TheatreService theatreService,
    IEnumerable<ISmsGateway> smsGateways, IEnumerable<IEmailGateway> emailGateways, ISecretProtector secrets,
    ReminderDefaults defaults, AuditWriter audit, TimeProvider clock, IEnumerable<BrightPAS.Core.Integration.IMessageTap> taps)
{
    public const int MaxAttempts = 5;
    private static readonly int[] BackoffMinutes = [1, 5, 15, 60, 240];
    public static readonly TimeSpan FormLinkMessageLifetime = TimeSpan.FromDays(14);
    public const string SystemUser = "reminders";

    // ---------------------------------------------------------------- settings and templates

    public async Task<ReminderSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new ReminderSettings
        {
            SmsProvider = defaults.SmsProvider,
            EmailProvider = defaults.EmailProvider,
            ConfirmationsFrom = clock.GetUtcNow(),
            WebhookKeyProtected = secrets.Protect(NewKey()),
            UpdatedAt = clock.GetUtcNow(),
            UpdatedBy = SystemUser
        };
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        await EnsureTemplatesAsync(ct);
        return s;
    }

    private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public async Task<string> WebhookKeyAsync(CancellationToken ct = default) => secrets.Unprotect((await SettingsAsync(ct)).WebhookKeyProtected) ?? "";

    public async Task<bool> WebhookKeyMatchesAsync(string? key, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 200) return false;
        var expected = await WebhookKeyAsync(ct);
        return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(expected));
    }

    public async Task<string> RotateWebhookKeyAsync(string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var key = NewKey();
        s.WebhookKeyProtected = secrets.Protect(key);
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "RotateKey", "ReminderSettings", "1", reason: "SMS vendor callback key changed", ct: ct);
        return key;
    }

    public sealed record SettingsUpdate(
        SmsProvider SmsProvider, EmailProvider EmailProvider, bool SendBookingConfirmation, bool SendDayBeforeReminder, bool SendEmailCopies,
        int DayBeforeHour, int ArrivalLeadMinutes, TimeOnly QuietStart, TimeOnly QuietEnd, string ClinicName, string? ClinicPhone,
        string? ModicaBaseUrl, string? ModicaApplication, string? ModicaPassword, string? ModicaMask,
        string? GenericUrl, string? GenericAuthHeader, string? GenericAuthValue, string? GenericBodyTemplate,
        string? SmtpHost, int SmtpPort, bool SmtpStartTls, string? SmtpUser, string? SmtpPassword, string? FromAddress, string? FromName);

    /// <summary>Saves settings. Secret fields are only changed when a new value is typed (blank keeps the stored one).</summary>
    public async Task<IReadOnlyList<string>> UpdateSettingsAsync(SettingsUpdate u, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (u.DayBeforeHour is < 6 or > 19) errors.Add("Send next-day reminders between 6 am and 7 pm (hour 6–19).");
        if (u.ArrivalLeadMinutes is < 0 or > 240) errors.Add("Arrival lead time must be 0–240 minutes.");
        if (string.IsNullOrWhiteSpace(u.ClinicName)) errors.Add("Enter the clinic name shown in messages.");
        if (u.SmtpPort is < 1 or > 65535) errors.Add("SMTP port must be 1–65535.");
        if (!string.IsNullOrWhiteSpace(u.FromAddress) && Addresses.NormaliseEmail(u.FromAddress) is null) errors.Add("The 'from' email address is not valid.");
        if (!string.IsNullOrWhiteSpace(u.ModicaBaseUrl) && !u.ModicaBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) errors.Add("The Modica address must start with https://.");
        if (!string.IsNullOrWhiteSpace(u.GenericUrl) && !u.GenericUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) errors.Add("The SMS vendor address must start with https://.");
        if (errors.Count > 0) return errors;

        var s = await SettingsAsync(ct);
        var before = $"{s.SmsProvider}/{s.EmailProvider}";
        if (!s.SendBookingConfirmation && u.SendBookingConfirmation) s.ConfirmationsFrom = clock.GetUtcNow();
        s.SmsProvider = u.SmsProvider; s.EmailProvider = u.EmailProvider;
        s.SendBookingConfirmation = u.SendBookingConfirmation; s.SendDayBeforeReminder = u.SendDayBeforeReminder; s.SendEmailCopies = u.SendEmailCopies;
        s.DayBeforeHour = u.DayBeforeHour; s.ArrivalLeadMinutes = u.ArrivalLeadMinutes; s.QuietStart = u.QuietStart; s.QuietEnd = u.QuietEnd;
        s.ClinicName = u.ClinicName.Trim(); s.ClinicPhone = u.ClinicPhone?.Trim() ?? "";
        s.ModicaBaseUrl = string.IsNullOrWhiteSpace(u.ModicaBaseUrl) ? ModicaGateway.DefaultBaseUrl : u.ModicaBaseUrl.Trim();
        s.ModicaApplication = Blank(u.ModicaApplication); s.ModicaMask = Blank(u.ModicaMask);
        if (!string.IsNullOrEmpty(u.ModicaPassword)) s.ModicaPasswordProtected = secrets.Protect(u.ModicaPassword);
        s.GenericUrl = Blank(u.GenericUrl); s.GenericAuthHeader = Blank(u.GenericAuthHeader);
        if (!string.IsNullOrEmpty(u.GenericAuthValue)) s.GenericAuthValueProtected = secrets.Protect(u.GenericAuthValue);
        if (!string.IsNullOrWhiteSpace(u.GenericBodyTemplate)) s.GenericBodyTemplate = u.GenericBodyTemplate.Trim();
        s.SmtpHost = Blank(u.SmtpHost); s.SmtpPort = u.SmtpPort; s.SmtpStartTls = u.SmtpStartTls; s.SmtpUser = Blank(u.SmtpUser);
        if (!string.IsNullOrEmpty(u.SmtpPassword)) s.SmtpPasswordProtected = secrets.Protect(u.SmtpPassword);
        s.FromAddress = Blank(u.FromAddress); s.FromName = Blank(u.FromName) ?? s.ClinicName;
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "ReminderSettings", "1", reason: $"Vendors {before} -> {s.SmsProvider}/{s.EmailProvider}", ct: ct);
        return [];
    }

    /// <summary>Problems that stop messages going out, in plain English (shown as a banner).</summary>
    public async Task<IReadOnlyList<string>> ProblemsAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var list = new List<string>();
        if (s.SmsProvider == SmsProvider.Off) list.Add("SMS is switched off.");
        else if (Sms(s)?.ConfigurationProblem(s) is { } p) list.Add(p);
        if (s.EmailProvider == EmailProvider.Off) list.Add("Email is switched off.");
        else if (Email(s)?.ConfigurationProblem(s) is { } e) list.Add(e);
        if (s.SmsProvider == SmsProvider.TestOutbox || s.EmailProvider == EmailProvider.TestOutbox) list.Add("TEST MODE: messages are recorded but not sent to patients.");
        return list;
    }

    private ISmsGateway? Sms(ReminderSettings s) => smsGateways.FirstOrDefault(g => g.Provider == s.SmsProvider);
    private IEmailGateway? Email(ReminderSettings s) => emailGateways.FirstOrDefault(g => g.Provider == s.EmailProvider);

    public static readonly IReadOnlyList<(MessageKind Kind, Channel Channel, string? Subject, string Body)> DefaultTemplates =
    [
        (MessageKind.BookingConfirmation, Channel.Sms, null,
            "Kia ora {FirstName}, your appointment at {ClinicName} is booked for {Date}. Please arrive at {ArrivalTime}. Reply YES to confirm or NO if you can't come. Questions: {ClinicPhone}. Reply STOP to opt out."),
        (MessageKind.BookingConfirmation, Channel.Email, "Your appointment at {ClinicName}",
            "Kia ora {FirstName},\n\nYour appointment at {ClinicName} is booked for {Date}. Please arrive at {ArrivalTime}.\n\nIf you cannot come, or have any questions, please phone us on {ClinicPhone}.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.DayBeforeReminder, Channel.Sms, null,
            "Reminder from {ClinicName}: your appointment is tomorrow, {Date}. Please arrive at {ArrivalTime} and follow the fasting instructions you were given. Reply YES to confirm or NO if you can't come. {ClinicPhone}"),
        (MessageKind.DayBeforeReminder, Channel.Email, "Reminder: your appointment tomorrow at {ClinicName}",
            "Kia ora {FirstName},\n\nThis is a reminder that your appointment at {ClinicName} is tomorrow, {Date}. Please arrive at {ArrivalTime} and follow the fasting and medicine instructions you were given.\n\nIf you cannot come, please phone us on {ClinicPhone} as soon as possible.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.FormLink, Channel.Sms, null,
            "{ClinicName}: please fill in your health questionnaire before your appointment: {Link} You will need your date of birth. Reply STOP to opt out."),
        (MessageKind.FormLink, Channel.Email, "Health questionnaire from {ClinicName}",
            "Kia ora {FirstName},\n\nBefore your appointment, please fill in our health questionnaire. It takes about 10 minutes:\n\n{Link}\n\nYou will need your date of birth to open it. The link works for 14 days.\n\nIf you have any questions, phone us on {ClinicPhone}.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.PortalLink, Channel.Sms, null,
            "{ClinicName}: see your appointment details, send us a question or a document, and fill in your forms here: {Link} You will need your date of birth. Reply STOP to opt out."),
        (MessageKind.PortalLink, Channel.Email, "Your {ClinicName} patient portal",
            "Kia ora {FirstName},\n\nYou can see your appointment details, send us questions or documents, fill in your health questionnaire and see your estimate here:\n\n{Link}\n\nYou will need your date of birth to open it. Please keep this link private.\n\nIf you have any questions, phone us on {ClinicPhone}.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.FollowUpCheckIn, Channel.Sms, null,
            "{ClinicName}: how are you getting on after your procedure? Please answer a few quick questions: {Link} You will need your date of birth. If you are worried now, phone {ClinicPhone} or 111 in an emergency."),
        (MessageKind.FollowUpCheckIn, Channel.Email, "How are you getting on? – {ClinicName}",
            "Kia ora {FirstName},\n\nWe hope your recovery is going well. Please tell us how you are getting on – it takes two minutes:\n\n{Link}\n\nYou will need your date of birth to open it. If you are worried about your wound, pain, bleeding or a fever, please phone us on {ClinicPhone} straight away, or 111 in an emergency.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.ConsentLink, Channel.Sms, null,
            "{ClinicName}: please read about your procedure and sign your consent form before your appointment: {Link} You will need your date of birth. Questions? Phone {ClinicPhone}."),
        (MessageKind.ConsentLink, Channel.Email, "Your procedure information and consent form – {ClinicName}",
            "Kia ora {FirstName},\n\nBefore your procedure, please read the information about it – what it involves, the benefits, the risks and the alternatives – and sign your consent form online:\n\n{Link}\n\nYou will need your date of birth to open it. You can ask your surgeon a question on the page, and you don't have to sign until you are happy. Please keep this link private.\n\nIf you have any questions, phone us on {ClinicPhone}.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.PaymentLink, Channel.Sms, null,
            "{ClinicName}: you can pay your account securely online here: {Link} You will need your date of birth. Questions? Phone {ClinicPhone}."),
        (MessageKind.PaymentLink, Channel.Email, "Pay your account online – {ClinicName}",
            "Kia ora {FirstName},\n\nYou can see your account and pay it securely by card here:\n\n{Link}\n\nYou will need your date of birth to open it. Please keep this link private.\n\nIf you have any questions about your account, phone us on {ClinicPhone}.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.Statement, Channel.Sms, null,
            "{ClinicName}: your statement is ready. See it and pay securely online: {Link} You will need your date of birth. Questions? Phone {ClinicPhone}."),
        (MessageKind.Statement, Channel.Email, "Your statement from {ClinicName}",
            "Kia ora {FirstName},\n\nYour statement is ready. You can see what is owing, download the statement and pay securely by card here:\n\n{Link}\n\nYou will need your date of birth to open it. Please keep this link private. If you have already paid, thank you – please ignore this message.\n\nIf you have any questions about your account, phone us on {ClinicPhone}.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.OverdueReminder, Channel.Sms, null,
            "{ClinicName}: a reminder that invoice {InvoiceNumber} ({Amount}) was due on {Date}. Pay securely online: {Link} You will need your date of birth. Can't pay right now? Phone {ClinicPhone} about a payment plan."),
        (MessageKind.OverdueReminder, Channel.Email, "Payment reminder – {ClinicName}",
            "Kia ora {FirstName},\n\nOur records show that invoice {InvoiceNumber} ({Amount} owing) was due on {Date}. You can see your account and pay securely by card here:\n\n{Link}\n\nYou will need your date of birth to open it. Please keep this link private. If you have already paid, thank you – please ignore this message.\n\nIf paying all at once is hard, phone us on {ClinicPhone} and we can set up a payment plan.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.PlanInstalment, Channel.Sms, null,
            "{ClinicName}: your payment plan instalment of {Amount} is due on {Date}. Pay securely online: {Link} You will need your date of birth. Questions? Phone {ClinicPhone}."),
        (MessageKind.PlanInstalment, Channel.Email, "Your payment plan instalment – {ClinicName}",
            "Kia ora {FirstName},\n\nYour next payment plan instalment of {Amount} is due on {Date}. You can pay it securely by card here:\n\n{Link}\n\nYou will need your date of birth to open it. Please keep this link private. If you have already paid, thank you – please ignore this message.\n\nNgā mihi,\n{ClinicName}"),
        (MessageKind.ReplyConfirmed, Channel.Sms, null, "Thank you, your appointment on {Date} is confirmed. {ClinicName}"),
        (MessageKind.ReplyCancelRequested, Channel.Sms, null, "Thank you for letting us know. Our team will phone you about your appointment. {ClinicName} {ClinicPhone}"),
        (MessageKind.OptOutConfirmed, Channel.Sms, null, "You will no longer get text messages from {ClinicName}. Reply START to get them again."),
        (MessageKind.Test, Channel.Sms, null, "Test message from {ClinicName} (BrightPAS). No reply needed."),
        (MessageKind.Test, Channel.Email, "Test email from {ClinicName}", "This is a test email from BrightPAS at {ClinicName}. No reply is needed."),
    ];

    public async Task EnsureTemplatesAsync(CancellationToken ct = default)
    {
        var existing = await db.Templates.Select(t => new { t.Kind, t.Channel, t.Language }).ToListAsync(ct);
        foreach (var (kind, channel, subject, body) in DefaultTemplates)
            if (!existing.Any(e => e.Kind == kind && e.Channel == channel && e.Language == "en"))
                db.Templates.Add(new MessageTemplate { Kind = kind, Channel = channel, Language = "en", Subject = subject, Body = body, UpdatedAt = clock.GetUtcNow(), UpdatedBy = SystemUser });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TemplateRow>> TemplatesAsync(CancellationToken ct = default)
    {
        await SettingsAsync(ct);
        await EnsureTemplatesAsync(ct);
        return (await db.Templates.AsNoTracking().ToListAsync(ct)).OrderBy(t => t.Kind).ThenBy(t => t.Channel).ThenBy(t => t.Language)
            .Select(t => new TemplateRow(t.Id, t.Kind.ToString(), t.Channel.ToString(), t.Language, t.Subject, t.Body, t.IsActive, t.UpdatedAt, t.UpdatedBy)).ToList();
    }

    public static readonly string[] Placeholders = ["{FirstName}", "{ClinicName}", "{ClinicPhone}", "{Date}", "{ArrivalTime}", "{Location}", "{Surgeon}", "{Link}", "{Amount}", "{InvoiceNumber}"];

    public async Task<string?> SaveTemplateAsync(MessageKind kind, Channel channel, string language, string? subject, string body, bool active, string user, CancellationToken ct = default)
    {
        if (language is not ("en" or "mi")) return "Language must be English (en) or te reo Māori (mi).";
        if (string.IsNullOrWhiteSpace(body)) return "The message text is empty.";
        if (body.Length > (channel == Channel.Sms ? 612 : 4000)) return channel == Channel.Sms ? "SMS text is too long (612 characters = 4 SMS parts maximum)." : "Email text is too long.";
        if (channel == Channel.Email && string.IsNullOrWhiteSpace(subject)) return "Enter an email subject.";
        if (kind is MessageKind.FormLink or MessageKind.PortalLink or MessageKind.FollowUpCheckIn or MessageKind.ConsentLink or MessageKind.PaymentLink or MessageKind.Statement or MessageKind.OverdueReminder or MessageKind.PlanInstalment && !body.Contains("{Link}", StringComparison.Ordinal)) return "This message must contain {Link}.";
        var unknown = System.Text.RegularExpressions.Regex.Matches(body + subject, @"\{[A-Za-z]+\}").Select(m => m.Value).Where(p => !Placeholders.Contains(p)).Distinct().ToList();
        if (unknown.Count > 0) return $"Unknown placeholder(s): {string.Join(", ", unknown)}. Allowed: {string.Join(" ", Placeholders)}";
        var t = await db.Templates.SingleOrDefaultAsync(x => x.Kind == kind && x.Channel == channel && x.Language == language, ct);
        if (t is null) db.Templates.Add(t = new MessageTemplate { Kind = kind, Channel = channel, Language = language });
        t.Subject = channel == Channel.Email ? subject!.Trim() : null;
        t.Body = body.Trim(); t.IsActive = active; t.UpdatedAt = clock.GetUtcNow(); t.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "MessageTemplate", t.Id.ToString(), reason: $"{kind}/{channel}/{language} active={active}", ct: ct);
        return null;
    }

    public static string Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var sb = new StringBuilder(template);
        foreach (var p in Placeholders) sb.Replace(p, values.GetValueOrDefault(p.Trim('{', '}')) ?? "");
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "[ ]{2,}", " ").Trim();
    }

    // ---------------------------------------------------------------- preferences and opt-outs

    public async Task<PreferenceView?> PreferenceAsync(Guid patientId, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return null;
        var pref = await db.Preferences.AsNoTracking().SingleOrDefaultAsync(x => x.PatientId == patientId, ct) ?? new ContactPreference { PatientId = patientId };
        var mobile = Addresses.ToE164(p.MobilePhone);
        var email = Addresses.NormaliseEmail(p.Email);
        var mo = mobile is not null && await db.OptOuts.AnyAsync(o => o.Channel == Channel.Sms && o.Address == mobile, ct);
        var eo = email is not null && await db.OptOuts.AnyAsync(o => o.Channel == Channel.Email && o.Address == email, ct);
        return new PreferenceView(patientId, pref.AllowSms, pref.AllowEmail, pref.Language, mobile, email, mo, eo);
    }

    public async Task<string?> SetPreferenceAsync(Guid patientId, bool allowSms, bool allowEmail, string language, string user, CancellationToken ct = default)
    {
        if (language is not ("en" or "mi")) return "Language must be en or mi.";
        if (!await patients.Patients.AnyAsync(x => x.Id == patientId, ct)) return "Patient not found.";
        var pref = await db.Preferences.SingleOrDefaultAsync(x => x.PatientId == patientId, ct);
        if (pref is null) db.Preferences.Add(pref = new ContactPreference { PatientId = patientId });
        pref.AllowSms = allowSms; pref.AllowEmail = allowEmail; pref.Language = language; pref.UpdatedAt = clock.GetUtcNow(); pref.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "ContactPreference", patientId.ToString(), patientId.ToString(), reason: $"SMS={allowSms} Email={allowEmail} Lang={language}", ct: ct);
        return null;
    }

    public async Task SetOptOutAsync(Channel channel, string address, bool optedOut, string source, CancellationToken ct = default)
    {
        var existing = await db.OptOuts.SingleOrDefaultAsync(o => o.Channel == channel && o.Address == address, ct);
        if (optedOut && existing is null) db.OptOuts.Add(new OptOut { Channel = channel, Address = address, CreatedAt = clock.GetUtcNow(), Source = source });
        if (!optedOut && existing is not null) db.OptOuts.Remove(existing);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(source, optedOut ? "OptOut" : "OptIn", "MessageAddress", Addresses.Mask(address), reason: channel.ToString(), ct: ct);
    }

    // ---------------------------------------------------------------- queueing

    private sealed record Appointment(Guid BookingId, Guid PatientId, DateOnly Date, TimeOnly PlannedStart, string Location, string Surgeon, BookingStatus Status, DateTimeOffset CreatedAt);

    private async Task<List<Appointment>> AppointmentsAsync(DateOnly from, DateOnly to, Guid? bookingId, CancellationToken ct)
    {
        var sessions = await theatre.Sessions.AsNoTracking().Include(s => s.Bookings)
            .Where(s => !s.IsCancelled && s.Date >= from && s.Date <= to && (bookingId == null || s.Bookings.Any(b => b.Id == bookingId))).ToListAsync(ct);
        var list = new List<Appointment>();
        var people = sessions.Count == 0 ? [] : await theatreService.ClinicianNamesAsync(ct);
        var rooms = sessions.Count == 0 ? [] : await theatreService.LocationNamesAsync(ct);
        foreach (var s in sessions)
        {
            var t = s.StartTime;
            foreach (var b in s.Bookings.OrderBy(b => b.Order))
            {
                var active = b.Status is not (BookingStatus.Cancelled or BookingStatus.DidNotAttend);
                list.Add(new Appointment(b.Id, b.PatientId, s.Date, t, rooms.GetValueOrDefault(s.LocationId, ""), people.GetValueOrDefault(s.SurgeonId, ""), b.Status, b.CreatedAt));
                if (active) t = t.AddMinutes(b.DurationMinutes + TheatreService.TurnoverMinutes);
            }
        }
        return list;
    }

    private static string FirstName(Patient p) => p.PreferredName ?? p.GivenNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? p.GivenNames;

    private Dictionary<string, string?> Values(ReminderSettings s, Patient p, Appointment? a, string? link) => new()
    {
        ["FirstName"] = FirstName(p),
        ["ClinicName"] = s.ClinicName,
        ["ClinicPhone"] = s.ClinicPhone,
        ["Date"] = a is null ? null : Addresses.FormatDate(a.Date),
        ["ArrivalTime"] = a is null ? null : Addresses.FormatTime(a.PlannedStart.AddMinutes(-s.ArrivalLeadMinutes)),
        ["Location"] = a?.Location,
        ["Surgeon"] = a?.Surgeon,
        ["Link"] = link
    };

    private async Task<MessageTemplate?> TemplateAsync(MessageKind kind, Channel channel, string language, CancellationToken ct)
    {
        await EnsureTemplatesAsync(ct);
        return await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.Kind == kind && t.Channel == channel && t.Language == language && t.IsActive, ct)
            ?? (language == "en" ? null : await db.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.Kind == kind && t.Channel == channel && t.Language == "en" && t.IsActive, ct));
    }

    /// <summary>Creates the SMS (and email, when available and allowed) for one patient. Returns the messages created.</summary>
    /// <summary>
    /// Outbox hold (spec 7.19.1): a message a person sends waits <see cref="ReminderSettings.HoldSeconds"/> so it can be undone; bulk reminders wait
    /// <see cref="ReminderSettings.BulkHoldSeconds"/>; automatic replies and system messages go straight away.
    /// </summary>
    public static int HoldFor(ReminderSettings s, MessageKind kind, string user) => user switch
    {
        SystemUser => kind is MessageKind.BookingConfirmation or MessageKind.DayBeforeReminder ? Math.Max(0, s.BulkHoldSeconds) : 0,
        "system" or "patient" or "patient-link" => 0,
        _ => Math.Max(0, s.HoldSeconds)
    };

    public static bool NeedsRelease(ReminderSettings s, MessageKind kind, string user) =>
        user is not (SystemUser or "system") && (s.ReviewKinds ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(kind.ToString(), StringComparer.Ordinal);

    // ------------------------------------------------------------------ send safety (7.19 / 7.20)
    public async Task<IReadOnlyList<string>> SaveSafetyAsync(int holdSeconds, int bulkHoldSeconds, string? reviewKinds, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (holdSeconds is < 0 or > 3600) errors.Add("The hold for single messages must be 0 to 60 minutes.");
        if (bulkHoldSeconds is < 0 or > 7200) errors.Add("The hold for bulk reminders must be 0 to 120 minutes.");
        var kinds = (reviewKinds ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
        foreach (var k in kinds) if (!Enum.TryParse<MessageKind>(k, out _)) errors.Add($"Unknown message type: {k}.");
        if (errors.Count > 0) return errors;
        var s = await SettingsAsync(ct);
        s.HoldSeconds = holdSeconds; s.BulkHoldSeconds = bulkHoldSeconds; s.ReviewKinds = kinds.Count == 0 ? null : string.Join(",", kinds);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "SendSafety", "1", reason: $"hold {holdSeconds}s, bulk {bulkHoldSeconds}s, review: {s.ReviewKinds ?? "none"}", ct: ct);
        return [];
    }

    /// <summary>Undo send / recall while the message is still in the outbox.</summary>
    public async Task<string?> RecallAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return "Message not found.";
        if (string.IsNullOrWhiteSpace(reason)) return "Give a reason.";
        if (m.Status != MessageStatus.Queued) return m.Status is MessageStatus.Sent or MessageStatus.Delivered ? "Already sent – use \"Sent to the wrong person\" to send a correction." : "This message is not waiting to be sent.";
        m.Status = MessageStatus.Cancelled; m.RecalledBy = user; m.RecalledAt = clock.GetUtcNow(); m.RecallReason = reason.Trim().Length > 500 ? reason.Trim()[..500] : reason.Trim(); m.SecretLink = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Recall", "OutboundMessage", id.ToString(), m.PatientId?.ToString(), reason: reason, ct: ct);
        return null;
    }

    /// <summary>A second person releases a message held for review (the person who wrote it cannot).</summary>
    public async Task<string?> ReleaseAsync(Guid id, string user, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return "Message not found.";
        if (m.Status != MessageStatus.Queued || !m.RequiresRelease || m.ReleasedAt is not null) return "This message is not waiting for release.";
        if (string.Equals(m.CreatedBy, user, StringComparison.OrdinalIgnoreCase)) return "A second person must release it.";
        m.ReleasedBy = user; m.ReleasedAt = clock.GetUtcNow();
        if (m.NextAttemptAt > clock.GetUtcNow()) m.NextAttemptAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Release", "OutboundMessage", id.ToString(), m.PatientId?.ToString(), ct: ct);
        return null;
    }

    /// <summary>Clinically urgent: skip the hold (still needs release if held for review). Audited.</summary>
    public async Task<string?> SendNowAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return "Message not found.";
        if (m.Status != MessageStatus.Queued) return "This message is not waiting to be sent.";
        if (string.IsNullOrWhiteSpace(reason)) return "Say why it must go now.";
        m.NextAttemptAt = clock.GetUtcNow(); m.IgnoreQuietHours = true;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "SendNow", "OutboundMessage", id.ToString(), m.PatientId?.ToString(), reason: reason, ct: ct);
        return null;
    }

    /// <summary>
    /// "Sent to the wrong person" (7.20.3): recalls it if still held; otherwise sends a correction notice to the same address (no patient details)
    /// and marks the message. The caller opens the privacy incident.
    /// </summary>
    public async Task<(bool Recalled, Guid? CorrectionId, string? Error)> WrongRecipientAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return (false, null, "Message not found.");
        if (string.IsNullOrWhiteSpace(reason)) return (false, null, "Say what happened.");
        if (m.WrongRecipientAt is not null) return (false, null, "Already reported.");
        m.WrongRecipientAt = clock.GetUtcNow();
        if (m.Status == MessageStatus.Queued)
        {
            m.Status = MessageStatus.Cancelled; m.RecalledBy = user; m.RecalledAt = clock.GetUtcNow(); m.RecallReason = "Wrong recipient: " + reason.Trim(); m.SecretLink = null;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "WrongRecipient", "OutboundMessage", id.ToString(), m.PatientId?.ToString(), reason: "Recalled before sending. " + reason, ct: ct);
            return (true, null, null);
        }
        if (m.Status is not (MessageStatus.Sent or MessageStatus.Delivered)) { await db.SaveChangesAsync(ct); return (false, null, null); }
        var s = await SettingsAsync(ct);
        var when = (m.SentAt ?? m.CreatedAt).ToOffset(BrightPAS.Core.NzTime.Zone.GetUtcOffset((m.SentAt ?? m.CreatedAt).UtcDateTime));
        var c = new OutboundMessage
        {
            Kind = MessageKind.Test,
            Channel = m.Channel,
            Address = m.Address,
            CorrectionForId = m.Id,
            IgnoreQuietHours = false,
            NextAttemptAt = clock.GetUtcNow(),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user,
            Subject = m.Channel == Channel.Email ? $"Please disregard our earlier message – {s.ClinicName}" : null,
            Body = $"{s.ClinicName}: we sent a message to this {(m.Channel == Channel.Sms ? "number" : "address")} on {when:d MMM} at {when:h:mm tt} in error. Please delete it and do not act on it. Sorry for the trouble. Questions: {s.ClinicPhone}"
        };
        db.Messages.Add(c);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "WrongRecipient", "OutboundMessage", id.ToString(), m.PatientId?.ToString(), reason: $"Correction notice {c.Id} sent. {reason}", ct: ct);
        return (false, c.Id, null);
    }

    private async Task<List<OutboundMessage>> QueueAsync(ReminderSettings s, Patient p, MessageKind kind, Appointment? a, string? link, string dedupePrefix,
        bool sms, bool email, bool ignoreQuiet, string user, CancellationToken ct, bool bypassOptOut = false, IReadOnlyDictionary<string, string?>? extra = null)
    {
        var created = new List<OutboundMessage>();
        var pref = await db.Preferences.AsNoTracking().SingleOrDefaultAsync(x => x.PatientId == p.Id, ct) ?? new ContactPreference { PatientId = p.Id };
        var now = clock.GetUtcNow();
        foreach (var channel in new[] { Channel.Sms, Channel.Email })
        {
            if (channel == Channel.Sms && !sms || channel == Channel.Email && !email) continue;
            var dedupe = dedupePrefix.Length == 0 ? null : $"{dedupePrefix}:{channel}";
            if (dedupe is not null && await db.Messages.AnyAsync(m => m.DedupeKey == dedupe, ct)) continue;
            var address = channel == Channel.Sms ? Addresses.ToE164(p.MobilePhone) : Addresses.NormaliseEmail(p.Email);
            string? suppressed = null;
            if (channel == Channel.Sms && (address is null || !Addresses.IsNzMobile(address))) suppressed = "No NZ mobile number on the patient record.";
            if (channel == Channel.Email && address is null) continue; // no email: simply not sent
            if (channel == Channel.Sms && !pref.AllowSms || channel == Channel.Email && !pref.AllowEmail) suppressed ??= $"Patient prefers no {(channel == Channel.Sms ? "text messages" : "email")}.";
            if (!bypassOptOut && address is not null && await db.OptOuts.AnyAsync(o => o.Channel == channel && o.Address == address, ct)) suppressed ??= "This address has opted out (STOP).";
            var tpl = await TemplateAsync(kind, channel, pref.Language, ct);
            if (tpl is null) { if (channel == Channel.Email) continue; suppressed ??= "No active message template."; }
            var values = Values(s, p, a, link is null ? null : "{Link}");
            if (extra is not null) foreach (var (k, v) in extra) values[k] = v;
            var m = new OutboundMessage
            {
                PatientId = p.Id,
                BookingId = a?.BookingId,
                AppointmentDate = a?.Date,
                Kind = kind,
                Channel = channel,
                Address = address ?? "(none)",
                Subject = tpl?.Subject is null ? null : Render(tpl.Subject, values),
                Body = tpl is null ? "" : Render(tpl.Body, values).Replace("{Link}", LinkPlaceholder(kind)),
                SecretLink = link,
                Status = suppressed is null ? MessageStatus.Queued : MessageStatus.Suppressed,
                LastError = suppressed,
                IgnoreQuietHours = ignoreQuiet,
                NextAttemptAt = now.AddSeconds(HoldFor(s, kind, user)),
                RequiresRelease = NeedsRelease(s, kind, user),
                DedupeKey = dedupe,
                CreatedAt = now,
                CreatedBy = user
            };
            db.Messages.Add(m);
            created.Add(m);
        }
        await db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>Sends a questionnaire link (staff action). The link is a secret: it is kept only until the message is sent.</summary>
    public async Task<IReadOnlyList<OutboundMessage>> QueueFormLinkAsync(Guid patientId, Guid? bookingId, string link, bool sms, bool email, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return [];
        foreach (var old in await db.Messages.Where(m => m.PatientId == patientId && m.Kind == MessageKind.FormLink && m.Status == MessageStatus.Queued).ToListAsync(ct))
        { old.Status = MessageStatus.Cancelled; old.LastError = "Replaced by a newer questionnaire link."; old.SecretLink = null; }
        var list = await QueueAsync(s, p, MessageKind.FormLink, null, link, "", sms, email, ignoreQuiet: true, user, ct);
        await audit.WriteAsync(user, "Queue", "OutboundMessage", string.Join(",", list.Select(m => m.Id)), patientId.ToString(), reason: "Questionnaire link", ct: ct);
        return list;
    }

    /// <summary>Queues the patient-portal link by SMS (and email when allowed). Older unsent portal-link messages are cancelled.</summary>
    public async Task<IReadOnlyList<OutboundMessage>> QueuePortalLinkAsync(Guid patientId, string link, bool sms, bool email, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        await EnsureTemplatesAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return [];
        foreach (var old in await db.Messages.Where(m => m.PatientId == patientId && m.Kind == MessageKind.PortalLink && m.Status == MessageStatus.Queued).ToListAsync(ct))
        { old.Status = MessageStatus.Cancelled; old.LastError = "Replaced by a newer portal link."; old.SecretLink = null; }
        var list = await QueueAsync(s, p, MessageKind.PortalLink, null, link, "", sms, email, ignoreQuiet: false, user, ct);
        await audit.WriteAsync(user, "Queue", "OutboundMessage", string.Join(",", list.Select(m => m.Id)), patientId.ToString(), reason: "Patient portal link", ct: ct);
        return list;
    }

    /// <summary>Queues a post-operative check-in link (follow-up, 0.24.0). The link is a secret: kept only until the message is sent.</summary>
    public async Task<IReadOnlyList<OutboundMessage>> QueueFollowUpLinkAsync(Guid patientId, Guid? bookingId, string link, string dedupeKey, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        await EnsureTemplatesAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return [];
        var list = await QueueAsync(s, p, MessageKind.FollowUpCheckIn, null, link, dedupeKey, true, true, ignoreQuiet: false, user, ct);
        foreach (var m in list) m.BookingId = bookingId;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Queue", "OutboundMessage", string.Join(",", list.Select(m => m.Id)), patientId.ToString(), reason: "Follow-up check-in link", ct: ct);
        return list;
    }

    /// <summary>The patient's e-consent link (0.29.0): text and email, like the questionnaire link.</summary>
    public async Task<IReadOnlyList<OutboundMessage>> QueueConsentLinkAsync(Guid patientId, Guid? bookingId, string link, string dedupeKey, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        await EnsureTemplatesAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return [];
        var list = await QueueAsync(s, p, MessageKind.ConsentLink, null, link, dedupeKey, true, true, ignoreQuiet: false, user, ct);
        foreach (var m in list) m.BookingId = bookingId;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Queue", "OutboundMessage", string.Join(",", list.Select(m => m.Id)), patientId.ToString(), reason: "Consent link", ct: ct);
        return list;
    }

    /// <summary>Payment link or statement (0.30.0): SMS and/or email with the patient's secret payment link.</summary>
    public async Task<IReadOnlyList<OutboundMessage>> QueuePaymentLinkAsync(Guid patientId, MessageKind kind, string link, string dedupeKey, string user, CancellationToken ct = default,
        IReadOnlyDictionary<string, string?>? extra = null)
    {
        if (kind is not (MessageKind.PaymentLink or MessageKind.Statement or MessageKind.OverdueReminder or MessageKind.PlanInstalment)) throw new ArgumentOutOfRangeException(nameof(kind));
        var s = await SettingsAsync(ct);
        await EnsureTemplatesAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == patientId, ct);
        if (p is null) return [];
        var list = await QueueAsync(s, p, kind, null, link, dedupeKey, true, true, ignoreQuiet: false, user, ct, extra: extra);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Queue", "OutboundMessage", string.Join(",", list.Select(m => m.Id)), patientId.ToString(),
            reason: kind switch { MessageKind.Statement => "Statement", MessageKind.OverdueReminder => "Overdue reminder", MessageKind.PlanInstalment => "Payment plan reminder", _ => "Payment link" }, ct: ct);
        return list;
    }

    /// <summary>Alerts the duty nurse by SMS (a staff number, not a patient): used when a patient's check-in answer needs a call.</summary>
    public async Task<OutboundMessage?> QueueStaffAlertAsync(string mobile, string text, string user, CancellationToken ct = default)
    {
        var to = Addresses.ToE164(mobile);
        if (to is null || !Addresses.IsNzMobile(to)) return null;
        var m = new OutboundMessage { Kind = MessageKind.Test, Channel = Channel.Sms, Address = to, Body = text, Status = MessageStatus.Queued, IgnoreQuietHours = true, NextAttemptAt = clock.GetUtcNow(), CreatedAt = clock.GetUtcNow(), CreatedBy = user };
        db.Messages.Add(m);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Queue", "OutboundMessage", m.Id.ToString(), reason: "Duty nurse alert", ct: ct);
        return m;
    }

    public async Task<IReadOnlyList<OutboundMessage>> QueueTestAsync(Channel channel, string to, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var address = channel == Channel.Sms ? Addresses.ToE164(to) : Addresses.NormaliseEmail(to);
        if (address is null || channel == Channel.Sms && !Addresses.IsNzMobile(address)) return [];
        var tpl = await TemplateAsync(MessageKind.Test, channel, "en", ct);
        var values = new Dictionary<string, string?> { ["ClinicName"] = s.ClinicName };
        var m = new OutboundMessage
        {
            PatientId = null,
            Kind = MessageKind.Test,
            Channel = channel,
            Address = address,
            Subject = tpl?.Subject is null ? null : Render(tpl.Subject, values),
            Body = Render(tpl?.Body ?? "Test from {ClinicName}", values),
            IgnoreQuietHours = true,
            NextAttemptAt = clock.GetUtcNow(),
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = user
        };
        db.Messages.Add(m);
        await db.SaveChangesAsync(ct);
        return [m];
    }

    /// <summary>Queues automatic confirmations and next-day reminders. Idempotent: each message has a de-duplication key.</summary>
    public async Task<int> ScheduleAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var local = clock.GetLocalNow();
        var today = DateOnly.FromDateTime(local.DateTime);
        var created = 0;
        var appts = await AppointmentsAsync(today, today.AddDays(120), null, ct);
        var active = appts.Where(a => a.Status is BookingStatus.Booked or BookingStatus.Confirmed).ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => active.Select(a => a.PatientId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        if (s.SendBookingConfirmation)
            foreach (var a in active.Where(a => a.CreatedAt >= s.ConfirmationsFrom && a.Date >= today))
                if (people.TryGetValue(a.PatientId, out var p))
                    created += (await QueueAsync(s, p, MessageKind.BookingConfirmation, a, null, $"conf:{a.BookingId}:{a.Date:yyyyMMdd}", true, s.SendEmailCopies, false, SystemUser, ct)).Count;
        if (s.SendDayBeforeReminder && local.Hour >= s.DayBeforeHour)
        {
            var tomorrow = today.AddDays(1);
            var recentCutoff = clock.GetUtcNow().AddHours(-24);
            foreach (var a in active.Where(a => a.Date == tomorrow))
            {
                if (!people.TryGetValue(a.PatientId, out var p)) continue;
                // A confirmation sent in the last 24 hours already covers tomorrow.
                if (await db.Messages.AnyAsync(m => m.BookingId == a.BookingId && m.Kind == MessageKind.BookingConfirmation && m.CreatedAt >= recentCutoff, ct)) continue;
                created += (await QueueAsync(s, p, MessageKind.DayBeforeReminder, a, null, $"dbr:{a.BookingId}:{a.Date:yyyyMMdd}", true, s.SendEmailCopies, false, SystemUser, ct)).Count;
            }
        }
        return created;
    }

    // ---------------------------------------------------------------- sending

    public bool InQuietHours(ReminderSettings s, DateTimeOffset localNow)
    {
        var t = TimeOnly.FromDateTime(localNow.DateTime);
        return s.QuietStart > s.QuietEnd ? t >= s.QuietStart || t < s.QuietEnd : t >= s.QuietStart && t < s.QuietEnd;
    }

    /// <summary>Sends due messages. Booking messages are re-checked first (cancelled or moved bookings are not messaged).</summary>
    public async Task<(int Sent, int Failed)> SendDueAsync(CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var now = clock.GetUtcNow();
        var quiet = InQuietHours(s, clock.GetLocalNow());
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

        // Messages left "Sending" by a crash: we cannot know whether the vendor sent them, so flag them for staff instead of risking a duplicate.
        foreach (var stuck in await db.Messages.Where(m => m.Status == MessageStatus.Sending).ToListAsync(ct))
            if (stuck.NextAttemptAt < now.AddMinutes(-10)) { stuck.Status = MessageStatus.Failed; stuck.LastError = "Interrupted while sending – it may or may not have been delivered. Check before retrying."; }
        await db.SaveChangesAsync(ct);

        var due = await db.Messages.Where(m => m.Status == MessageStatus.Queued && m.NextAttemptAt <= now && (!m.RequiresRelease || m.ReleasedAt != null)).OrderBy(m => m.CreatedAt).Take(100).ToListAsync(ct);
        int sent = 0, failed = 0;
        foreach (var m in due)
        {
            if (quiet && !m.IgnoreQuietHours) continue;
            if (m.Kind == MessageKind.FormLink && m.CreatedAt + FormLinkMessageLifetime < now) { Close(m, MessageStatus.Cancelled, "The questionnaire link has expired."); continue; }
            if (m.BookingId is { } bid && m.Kind is MessageKind.BookingConfirmation or MessageKind.DayBeforeReminder)
            {
                var a = (await AppointmentsAsync(today, today.AddDays(366), bid, ct)).FirstOrDefault(x => x.BookingId == bid);
                if (a is null || a.Status is not (BookingStatus.Booked or BookingStatus.Confirmed) || a.Date != m.AppointmentDate || a.Date < today)
                { Close(m, MessageStatus.Cancelled, "The booking was cancelled or changed before the message was sent."); continue; }
            }
            if (m.Kind is not (MessageKind.OptOutConfirmed or MessageKind.Test) && await db.OptOuts.AnyAsync(o => o.Channel == m.Channel && o.Address == m.Address, ct))
            { Close(m, MessageStatus.Suppressed, "This address has opted out (STOP)."); continue; }

            SendResult r;
            string provider;
            var text = m.SecretLink is null ? m.Body : m.Body.Replace(LinkPlaceholder(m.Kind), m.SecretLink);
            if (m.Channel == Channel.Sms)
            {
                if (s.SmsProvider == SmsProvider.Off) { m.LastError = "SMS is switched off."; continue; }
                var g = Sms(s);
                if (g is null || g.ConfigurationProblem(s) is { } prob) { m.LastError = Problem(g, s, "SMS"); continue; }
                provider = g.Provider.ToString();
                m.Status = MessageStatus.Sending; m.Provider = provider; m.NextAttemptAt = now;
                await db.SaveChangesAsync(ct);
                r = await g.SendAsync(s, m.Address, text, m.Id.ToString("N"), ct);
            }
            else
            {
                if (s.EmailProvider == EmailProvider.Off) { m.LastError = "Email is switched off."; continue; }
                var g = Email(s);
                if (g is null || g.ConfigurationProblem(s) is not null) { m.LastError = g?.ConfigurationProblem(s) ?? "No email sender."; continue; }
                provider = g.Provider.ToString();
                m.Status = MessageStatus.Sending; m.Provider = provider; m.NextAttemptAt = now;
                await db.SaveChangesAsync(ct);
                r = await g.SendAsync(s, m.Address, m.Subject ?? s.ClinicName, text, ct);
            }
            m.Attempts++;
            // Copy to the messaging engine's store (the body holds a placeholder, never the secret link).
            if (r.Ok || r.Permanent || m.Attempts >= MaxAttempts)
                await taps.RecordAllAsync(new BrightPAS.Core.Integration.TappedMessage(m.Channel == Channel.Sms ? "sms" : "email", false, m.Kind.ToString(), r.ProviderMessageId ?? m.Id.ToString("N"),
                    m.PatientId, (m.Channel == Channel.Email ? $"To: {m.Address}\nSubject: {m.Subject}\n\n" : $"To: {m.Address}\n\n") + m.Body, r.Ok, r.Ok ? $"Sent via {provider}" : r.Error), ct);
            if (r.Ok)
            {
                m.Status = MessageStatus.Sent; m.SentAt = clock.GetUtcNow(); m.ProviderMessageId = r.ProviderMessageId; m.LastError = null; m.SecretLink = null;
                sent++;
            }
            else if (r.Permanent || m.Attempts >= MaxAttempts)
            {
                m.Status = MessageStatus.Failed; m.LastError = r.Error; m.SecretLink = null; failed++;
            }
            else
            {
                m.Status = MessageStatus.Queued; m.LastError = r.Error; m.NextAttemptAt = clock.GetUtcNow().AddMinutes(BackoffMinutes[Math.Min(m.Attempts - 1, BackoffMinutes.Length - 1)]);
            }
            await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        return (sent, failed);
    }

    private static string Problem(ISmsGateway? g, ReminderSettings s, string what) => g?.ConfigurationProblem(s) ?? $"No {what} sender is available for {s.SmsProvider}.";

    private static string LinkPlaceholder(MessageKind kind) => kind switch { MessageKind.PortalLink => "[portal link]", MessageKind.FollowUpCheckIn => "[check-in link]", MessageKind.ConsentLink => "[consent link]", MessageKind.PaymentLink or MessageKind.Statement or MessageKind.OverdueReminder or MessageKind.PlanInstalment => "[payment link]", _ => "[questionnaire link]" };

    private static void Close(OutboundMessage m, MessageStatus status, string why) { m.Status = status; m.LastError = why; m.SecretLink = null; }

    public async Task<(int Queued, int Sent, int Failed)> RunCycleAsync(CancellationToken ct = default)
    {
        var q = await ScheduleAsync(ct);
        var (sent, failed) = await SendDueAsync(ct);
        return (q, sent, failed);
    }

    // ---------------------------------------------------------------- replies and delivery receipts

    public static ReplyMeaning Interpret(string? body)
    {
        var word = new string((body ?? "").Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD).Where(c => char.IsLetter(c) && c < 128 || c == ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return word switch
        {
            "YES" or "Y" or "YEP" or "YEAH" or "YUP" or "CONFIRM" or "CONFIRMED" or "OK" or "OKAY" or "AE" => ReplyMeaning.Confirm,
            "NO" or "N" or "NOPE" or "CANCEL" or "CANT" or "CANNOT" or "KAO" => ReplyMeaning.Cancel,
            "STOP" or "STOPALL" or "UNSUBSCRIBE" or "END" or "QUIT" or "OPTOUT" => ReplyMeaning.OptOut,
            "START" or "UNSTOP" or "SUBSCRIBE" => ReplyMeaning.OptIn,
            _ => ReplyMeaning.Unknown
        };
    }

    /// <summary>Handles an SMS reply from a patient. Confirmations update the booking automatically; cancellations are passed to staff to phone the patient.</summary>
    public async Task<InboundMessage> HandleReplyAsync(string from, string body, string provider, string? providerMessageId, string? replyToProviderId, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var now = clock.GetUtcNow();
        if (providerMessageId is not null && await db.Replies.SingleOrDefaultAsync(r => r.Provider == provider && r.ProviderMessageId == providerMessageId, ct) is { } dup) return dup;
        var number = Addresses.ToE164(from) ?? from.Trim();
        var reply = new InboundMessage
        {
            From = number,
            Body = body.Length > 2000 ? body[..2000] : body,
            ReceivedAt = now,
            Provider = provider,
            ProviderMessageId = providerMessageId,
            ReplyToProviderId = replyToProviderId,
            Meaning = Interpret(body)
        };
        OutboundMessage? original = null;
        if (!string.IsNullOrEmpty(replyToProviderId)) original = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.ProviderMessageId == replyToProviderId, ct);
        var since = now.AddDays(-14);
        original ??= (await db.Messages.AsNoTracking().Where(m => m.Channel == Channel.Sms && m.Address == number && m.BookingId != null && m.CreatedAt >= since).ToListAsync(ct))
            .OrderByDescending(m => m.CreatedAt).FirstOrDefault();
        original ??= (await db.Messages.AsNoTracking().Where(m => m.Channel == Channel.Sms && m.Address == number && m.PatientId != null && m.CreatedAt >= since).ToListAsync(ct))
            .OrderByDescending(m => m.CreatedAt).FirstOrDefault();
        reply.MatchedMessageId = original?.Id;
        reply.PatientId = original?.PatientId;
        reply.BookingId = original?.BookingId;
        var patient = reply.PatientId is { } pid ? await patients.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid, ct) : null;
        Appointment? appt = null;
        if (reply.BookingId is { } bid)
        {
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            appt = (await AppointmentsAsync(today.AddDays(-1), today.AddDays(366), bid, ct)).FirstOrDefault(a => a.BookingId == bid);
        }

        switch (reply.Meaning)
        {
            case ReplyMeaning.Confirm when appt is { Status: BookingStatus.Booked }:
                var err = await theatreService.SetStatusAsync(appt.BookingId, BookingStatus.Confirmed, null, "sms-reply", ct);
                reply.ActionTaken = err is null ? "Booking confirmed by the patient's SMS reply." : $"Could not confirm the booking: {err}";
                reply.Status = err is null ? ReplyStatus.Handled : ReplyStatus.New;
                if (err is null && patient is not null) await QueueAsync(s, patient, MessageKind.ReplyConfirmed, appt, null, "", true, false, true, SystemUser, ct);
                break;
            case ReplyMeaning.Confirm when appt is { Status: BookingStatus.Confirmed }:
                reply.ActionTaken = "Already confirmed – no change."; reply.Status = ReplyStatus.Handled; break;
            case ReplyMeaning.Confirm:
                reply.ActionTaken = "Patient replied YES but no open booking was found – please check."; break;
            case ReplyMeaning.Cancel:
                reply.ActionTaken = appt is null ? "Patient replied NO – no open booking found. Please phone them." : $"Patient cannot come on {Addresses.FormatDate(appt.Date)}. Please phone them and cancel or rebook.";
                if (patient is not null) await QueueAsync(s, patient, MessageKind.ReplyCancelRequested, appt, null, "", true, false, true, SystemUser, ct);
                break;
            case ReplyMeaning.OptOut:
                await SetOptOutAsync(Channel.Sms, number, true, "SMS reply STOP", ct);
                reply.ActionTaken = "Number opted out of text messages."; reply.Status = ReplyStatus.Handled;
                if (patient is not null) await QueueAsync(s, patient, MessageKind.OptOutConfirmed, null, null, "", true, false, true, SystemUser, ct, bypassOptOut: true);
                break;
            case ReplyMeaning.OptIn:
                await SetOptOutAsync(Channel.Sms, number, false, "SMS reply START", ct);
                reply.ActionTaken = "Number opted back in to text messages."; reply.Status = ReplyStatus.Handled; break;
            default:
                reply.ActionTaken = "Reply not understood – please read it and follow up."; break;
        }
        db.Replies.Add(reply);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync("sms-reply", "Receive", "InboundMessage", reply.Id.ToString(), reply.PatientId?.ToString(), reason: $"{reply.Meaning}: {reply.ActionTaken}", ct: ct);
        return reply;
    }

    /// <summary>Applies a vendor delivery receipt (Modica: sent, received, rejected, expired …).</summary>
    public async Task<bool> HandleDeliveryReceiptAsync(string providerMessageId, string status, CancellationToken ct = default)
    {
        var m = await db.Messages.FirstOrDefaultAsync(x => x.ProviderMessageId == providerMessageId, ct);
        if (m is null) return false;
        switch (status.Trim().ToLowerInvariant())
        {
            case "received" or "delivered": m.Status = MessageStatus.Delivered; m.DeliveredAt = clock.GetUtcNow(); break;
            case "rejected" or "expired" or "failed" or "dead" or "undeliverable": m.Status = MessageStatus.Failed; m.LastError = $"Vendor reported: {status}"; break;
            default: if (m.Status == MessageStatus.Sending) m.Status = MessageStatus.Sent; break;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<string?> MarkReplyHandledAsync(Guid id, string? note, string user, CancellationToken ct = default)
    {
        var r = await db.Replies.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return "Reply not found.";
        r.Status = ReplyStatus.Handled; r.HandledBy = user; r.HandledAt = clock.GetUtcNow(); r.Note = Blank(note);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Handled", "InboundMessage", r.Id.ToString(), r.PatientId?.ToString(), reason: note, ct: ct);
        return null;
    }

    // ---------------------------------------------------------------- staff lists and actions

    public async Task<IReadOnlyList<MessageRow>> MessagesAsync(string? status, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Messages.AsNoTracking().AsQueryable();
        // Guid.Empty = messages with no patient (test messages, 0.12.3).
        if (patientId == Guid.Empty) q = q.Where(m => m.PatientId == null);
        else if (patientId is not null) q = q.Where(m => m.PatientId == patientId);
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<MessageStatus>(status, true, out var st)) q = q.Where(m => m.Status == st);
        var list = await q.OrderByDescending(m => m.CreatedAt).Take(500).ToListAsync(ct);
        var ids = list.Where(m => m.PatientId != null).Select(m => m.PatientId!.Value).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return list.Select(m =>
        {
            var p = m.PatientId is { } pid ? people.GetValueOrDefault(pid) : null;
            return new MessageRow(m.Id, m.PatientId, p is null ? (m.Kind == MessageKind.Test ? "(test)" : "") : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Mrn ?? "",
                m.BookingId, m.AppointmentDate, m.Kind.ToString(), m.Channel.ToString(), m.Address, m.Subject, m.Body, m.Status.ToString(), m.Provider, m.Attempts,
                m.LastError, m.CreatedAt, m.CreatedBy, m.SentAt, m.DeliveredAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<ReplyRow>> RepliesAsync(string view, CancellationToken ct = default)
    {
        var q = db.Replies.AsNoTracking().AsQueryable();
        if (view == "new") q = q.Where(r => r.Status == ReplyStatus.New);
        var list = await q.OrderByDescending(r => r.ReceivedAt).Take(300).ToListAsync(ct);
        var ids = list.Where(r => r.PatientId != null).Select(r => r.PatientId!.Value).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return list.Select(r => new ReplyRow(r.Id, r.From, r.Body, r.ReceivedAt, r.PatientId,
            r.PatientId is { } id && people.TryGetValue(id, out var p) ? $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}" : null,
            r.BookingId, r.Meaning.ToString(), r.ActionTaken, r.Status.ToString(), r.HandledBy, r.HandledAt, r.Note)).ToList();
    }

    public async Task<object> CountsAsync(CancellationToken ct = default) => new
    {
        newReplies = await db.Replies.CountAsync(r => r.Status == ReplyStatus.New, ct),
        failed = await db.Messages.CountAsync(m => m.Status == MessageStatus.Failed, ct),
        queued = await db.Messages.CountAsync(m => m.Status == MessageStatus.Queued, ct)
    };

    public async Task<string?> RetryAsync(Guid id, string user, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return "Message not found.";
        if (m.Status is not (MessageStatus.Failed or MessageStatus.Suppressed)) return "Only failed or held messages can be retried.";
        if (m.Kind is MessageKind.FormLink or MessageKind.PortalLink or MessageKind.FollowUpCheckIn or MessageKind.ConsentLink or MessageKind.PaymentLink or MessageKind.Statement or MessageKind.OverdueReminder or MessageKind.PlanInstalment && m.SecretLink is null) return "Secure links cannot be resent (the link is not stored). Create a new link instead.";
        m.Status = MessageStatus.Queued; m.Attempts = 0; m.NextAttemptAt = clock.GetUtcNow(); m.IgnoreQuietHours = true; m.LastError = null;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Retry", "OutboundMessage", m.Id.ToString(), m.PatientId?.ToString(), ct: ct);
        return null;
    }

    public async Task<string?> CancelAsync(Guid id, string user, CancellationToken ct = default)
    {
        var m = await db.Messages.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return "Message not found.";
        if (m.Status != MessageStatus.Queued) return "Only messages waiting to be sent can be cancelled.";
        Close(m, MessageStatus.Cancelled, $"Cancelled by {user}.");
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "OutboundMessage", m.Id.ToString(), m.PatientId?.ToString(), ct: ct);
        return null;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
