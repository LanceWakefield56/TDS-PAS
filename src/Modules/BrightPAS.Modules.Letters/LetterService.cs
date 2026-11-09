using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Organisation;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;

namespace BrightPAS.Modules.Letters;

public sealed record TemplateEdit(string Code, string Name, LetterKind Kind, RecipientType Recipient, string Subject, string Body, bool IsActive = true);
public sealed record PracticeEdit(Guid? Id, string Name, string? HpiFacilityId, string? HealthLinkEdi, string? AddressLine1, string? AddressLine2, string? City,
    string? Postcode, string? Phone, string? Email, DeliveryChannel PreferredChannel, bool IsActive);
public sealed record NewLetter(Guid PatientId, string TemplateCode, Guid? BookingId, Guid? ReferralId);
public sealed record DraftEdit(string RecipientName, string? RecipientAddress, string Subject, string Body, string? Author);

public sealed record LetterRow(Guid Id, string Number, Guid PatientId, string PatientName, string? Nhi, string Mrn, string Kind, string Recipient, string RecipientName,
    string Subject, string Status, string Channel, DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset? SentAt, string? AckCode, bool CanHealthLink);

/// <summary>Letters: templates with placeholders, drafts filled in from the patient/booking/referral, finalising (locks the text), printing and voiding.</summary>
public sealed partial class LetterService(LettersDbContext db, PatientsDbContext patients, TheatreDbContext theatre, ReferralsDbContext referrals,
    OrganisationService org, AuditWriter audit, TimeProvider clock)
{
    /// <summary>Placeholders that templates may use. Unlike SMS, letters may include the procedure (they go to the patient or their clinicians).</summary>
    public static readonly IReadOnlyDictionary<string, string> Placeholders = new Dictionary<string, string>
    {
        ["Today"] = "Today's date", ["PatientName"] = "Patient's full name", ["PatientFirstName"] = "Patient's first (or preferred) name",
        ["PatientTitleName"] = "Name for the greeting line", ["Nhi"] = "NHI number", ["Mrn"] = "TDS record number", ["DateOfBirth"] = "Date of birth",
        ["PatientAddress"] = "Patient's address (one line)", ["BookingDate"] = "Booked date (e.g. Tuesday 17 November 2026)", ["ArrivalTime"] = "Time to arrive",
        ["StartTime"] = "Planned start time", ["Procedure"] = "Booked or waitlisted procedure", ["Side"] = "Side (left/right/both), blank if not applicable",
        ["Surgeon"] = "Surgeon or endoscopist", ["Anaesthetist"] = "Anaesthetist", ["Theatre"] = "Theatre or room", ["Anaesthetic"] = "Type of anaesthetic",
        ["FastingFrom"] = "Fasting start time (6 hours before arrival)", ["ReferralNumber"] = "Referral number", ["ReferralDate"] = "Date the referral was received",
        ["Referrer"] = "Referring clinician", ["ReferrerPractice"] = "Referrer's practice", ["ReferralReason"] = "Reason for referral",
        ["DeclineReason"] = "Reason the referral was declined", ["Priority"] = "Waitlist priority", ["TargetDate"] = "Waitlist target date",
        ["GpPractice"] = "Patient's GP practice", ["ClinicName"] = "Clinic name", ["ClinicAddress"] = "Clinic address (one line)", ["ClinicPhone"] = "Clinic phone",
        ["ClinicEmail"] = "Clinic email", ["Author"] = "Person signing the letter",
    };

    /// <summary>Minutes before the planned start that the patient should arrive.</summary>
    public const int ArrivalLeadMinutes = 60;

    [GeneratedRegex(@"\{([A-Za-z]+)\}")] private static partial Regex Token();
    [GeneratedRegex("^[A-Z0-9-]{2,30}$")] private static partial Regex CodeRx();
    private static readonly CultureInfo Nz = CultureInfo.GetCultureInfo("en-NZ");

    public static IReadOnlyList<string> UnknownPlaceholders(string text) =>
        Token().Matches(text).Select(m => m.Groups[1].Value).Where(t => !Placeholders.ContainsKey(t)).Distinct().ToList();

    public static string Render(string text, IReadOnlyDictionary<string, string> values) =>
        Token().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
    private static string LongDate(DateOnly d) => d.ToString("dddd d MMMM yyyy", Nz);
    private static string Time(TimeOnly t) => t.ToString("h:mm tt", Nz).ToLowerInvariant();

    // ------------------------------------------------------------------ templates

    public static readonly IReadOnlyList<TemplateEdit> DefaultTemplates =
    [
        new("APPT", "Appointment letter (patient)", LetterKind.Appointment, RecipientType.Patient, "Your procedure at {ClinicName} on {BookingDate}",
            "Dear {PatientTitleName}\n\nYour {Procedure}{Side} with {Surgeon} is booked for {BookingDate}.\n\nPlease arrive at {ClinicName}, {ClinicAddress}, at {ArrivalTime}. Your procedure is planned to start at about {StartTime}, but times can change on the day.\n\nIf you cannot come, or if you become unwell before your procedure, please phone us on {ClinicPhone} as soon as possible.\n\nWe will send you information about fasting and your medicines before your procedure.\n\nYours sincerely\n\n{Author}\n{ClinicName}"),
        new("ADMIT", "Admission instructions (patient)", LetterKind.Admission, RecipientType.Patient, "Getting ready for your procedure on {BookingDate}",
            "Dear {PatientTitleName}\n\nThis letter explains how to get ready for your {Procedure} on {BookingDate}.\n\nArrival: please arrive at {ArrivalTime} at {ClinicName}, {ClinicAddress}.\n\nFasting: do not eat anything after {FastingFrom}. You may drink small sips of clear water until two hours before you arrive, unless you have been told otherwise.\n\nMedicines: keep taking your usual medicines unless the nurse or your specialist has told you to stop any of them. Bring all your medicines with you.\n\nGoing home: you must have a responsible adult to drive you home and stay with you overnight. You cannot drive for 24 hours after a general anaesthetic or sedation.\n\nIf you have questions, phone {ClinicPhone}.\n\nYours sincerely\n\n{Author}\n{ClinicName}"),
        new("GPBOOK", "Booking notification (GP)", LetterKind.GpNotification, RecipientType.Gp, "{PatientName}, NHI {Nhi}, DOB {DateOfBirth}: booked for {Procedure}",
            "Dear Doctor\n\nRe: {PatientName}, NHI {Nhi}, born {DateOfBirth}, {PatientAddress}\n\nFor your information, your patient is booked for {Procedure}{Side} with {Surgeon} at {ClinicName} on {BookingDate}.\n\nPlease let us know of any recent changes to their health or medicines that may affect their procedure.\n\nKind regards\n\n{Author}\n{ClinicName}, {ClinicPhone}"),
        new("REFACK", "Referral received (referrer)", LetterKind.ReferralAcknowledgement, RecipientType.Referrer, "Referral received: {PatientName}, NHI {Nhi}",
            "Dear {Referrer}\n\nRe: {PatientName}, NHI {Nhi}, born {DateOfBirth}\n\nThank you for your referral received on {ReferralDate} ({ReferralNumber}). It is being reviewed by the specialist, and we will contact your patient with an appointment or let you know the outcome.\n\nKind regards\n\n{Author}\n{ClinicName}, {ClinicPhone}"),
        new("REFDEC", "Referral declined (referrer)", LetterKind.ReferralDeclined, RecipientType.Referrer, "Referral not accepted: {PatientName}, NHI {Nhi}",
            "Dear {Referrer}\n\nRe: {PatientName}, NHI {Nhi}, born {DateOfBirth}\n\nThank you for your referral received on {ReferralDate} ({ReferralNumber}). After review, we are unable to accept it.\n\nReason: {DeclineReason}\n\nPlease contact us on {ClinicPhone} if you would like to discuss this.\n\nKind regards\n\n{Author}\n{ClinicName}"),
    ];

    public async Task<int> EnsureDefaultTemplatesAsync(CancellationToken ct = default)
    {
        var have = (await db.Templates.Select(t => t.Code).ToListAsync(ct)).ToHashSet();
        var added = 0;
        foreach (var t in DefaultTemplates.Where(t => !have.Contains(t.Code)))
        {
            db.Templates.Add(new LetterTemplate { Code = t.Code, Name = t.Name, Kind = t.Kind, Recipient = t.Recipient, Subject = t.Subject, Body = t.Body, IsActive = true });
            added++;
        }
        if (added > 0) await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<IReadOnlyList<LetterTemplate>> TemplatesAsync(bool activeOnly, CancellationToken ct = default)
    {
        await EnsureDefaultTemplatesAsync(ct);
        return await db.Templates.AsNoTracking().Where(t => !activeOnly || t.IsActive).OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> SaveTemplateAsync(TemplateEdit t, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = t.Code?.Trim().ToUpperInvariant() ?? "";
        if (!CodeRx().IsMatch(code)) errors.Add("Enter a short code (letters, numbers or -), e.g. APPT2.");
        if (string.IsNullOrWhiteSpace(t.Name)) errors.Add("Enter the template name.");
        if (string.IsNullOrWhiteSpace(t.Subject) || string.IsNullOrWhiteSpace(t.Body)) errors.Add("Enter the subject and the letter text.");
        var unknown = UnknownPlaceholders((t.Subject ?? "") + " " + (t.Body ?? ""));
        if (unknown.Count > 0) errors.Add($"Unknown placeholder(s): {string.Join(", ", unknown.Select(u => "{" + u + "}"))}. Use only the placeholders listed.");
        if (errors.Count > 0) return errors;
        await EnsureDefaultTemplatesAsync(ct);
        var row = await db.Templates.SingleOrDefaultAsync(x => x.Code == code, ct);
        if (row is null) { row = new LetterTemplate { Code = code }; db.Templates.Add(row); }
        else row.Version++;
        row.Name = t.Name.Trim(); row.Kind = t.Kind; row.Recipient = t.Recipient; row.Subject = t.Subject!.Trim(); row.Body = t.Body!.Replace("\r\n", "\n").Trim();
        row.IsActive = t.IsActive; row.UpdatedAt = clock.GetUtcNow(); row.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "LetterTemplate", code, reason: $"{row.Name} v{row.Version}{(row.IsActive ? "" : " (inactive)")}", ct: ct);
        return errors;
    }

    // ------------------------------------------------------------------ GP practices

    public async Task<IReadOnlyList<GpPractice>> PracticesAsync(CancellationToken ct = default) =>
        await db.Practices.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<IReadOnlyList<string>> SavePracticeAsync(PracticeEdit p, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var hpi = string.IsNullOrWhiteSpace(p.HpiFacilityId) ? null : p.HpiFacilityId.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("Enter the practice name.");
        if (hpi is not null && !OrganisationService.IsHpiFacility(hpi)) errors.Add("The HPI facility number looks wrong (format F12345-A).");
        if (hpi is not null && await db.Practices.AnyAsync(x => x.HpiFacilityId == hpi && x.Id != p.Id, ct)) errors.Add("Another practice already has this HPI facility number.");
        if (p.PreferredChannel == DeliveryChannel.HealthLink && string.IsNullOrWhiteSpace(p.HealthLinkEdi)) errors.Add("Enter the practice's HealthLink EDI account, or choose post/email.");
        if (errors.Count > 0) return errors;
        GpPractice row;
        if (p.Id is { } id) row = await db.Practices.SingleAsync(x => x.Id == id, ct);
        else { row = new GpPractice(); db.Practices.Add(row); }
        row.Name = p.Name.Trim(); row.HpiFacilityId = hpi; row.HealthLinkEdi = Blank(p.HealthLinkEdi)?.ToLowerInvariant(); row.AddressLine1 = Blank(p.AddressLine1);
        row.AddressLine2 = Blank(p.AddressLine2); row.City = Blank(p.City); row.Postcode = Blank(p.Postcode); row.Phone = Blank(p.Phone); row.Email = Blank(p.Email);
        row.PreferredChannel = p.PreferredChannel; row.IsActive = p.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, p.Id is null ? "Create" : "Update", "GpPractice", row.Id.ToString(), reason: $"{row.Name} {row.HpiFacilityId}", ct: ct);
        return errors;
    }

    // ------------------------------------------------------------------ letters

    /// <summary>Values for the placeholders, from the patient, the booking or waitlist entry, the referral and the clinic details.</summary>
    public async Task<Dictionary<string, string>> ValuesAsync(Patient p, Guid? bookingId, Guid? referralId, string? author, CancellationToken ct = default)
    {
        var v = new Dictionary<string, string>();
        foreach (var k in Placeholders.Keys) v[k] = "";
        var site = await org.MainSiteAsync(ct);
        v["Today"] = LongDate(Today);
        v["PatientName"] = $"{p.GivenNames} {p.FamilyName}";
        v["PatientFirstName"] = p.PreferredName ?? p.GivenNames.Split(' ')[0];
        v["PatientTitleName"] = $"{p.PreferredName ?? p.GivenNames.Split(' ')[0]} {p.FamilyName}";
        v["Nhi"] = p.Nhi ?? "(no NHI)";
        v["Mrn"] = p.Mrn;
        v["DateOfBirth"] = p.DateOfBirth.ToString("d MMM yyyy", Nz);
        v["PatientAddress"] = string.Join(", ", new[] { p.AddressLine1, p.AddressLine2, p.Suburb, p.City, p.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (site is { } s)
        {
            v["ClinicName"] = s.Org.Name;
            v["ClinicAddress"] = string.Join(", ", new[] { s.Site.AddressLine1, s.Site.AddressLine2, s.Site.Suburb, s.Site.City, s.Site.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x)));
            v["ClinicPhone"] = s.Site.Phone ?? "";
            v["ClinicEmail"] = s.Site.Email ?? "";
        }
        v["Author"] = author ?? "";
        if (p.GpPracticeHpiFacilityId is { } gp && await db.Practices.AsNoTracking().FirstOrDefaultAsync(x => x.HpiFacilityId == gp, ct) is { } practice) v["GpPractice"] = practice.Name;

        if (bookingId is { } bid && await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bid, ct) is { } b
            && await theatre.Sessions.AsNoTracking().Include(x => x.Bookings).SingleOrDefaultAsync(x => x.Id == b.SessionId, ct) is { } sess)
        {
            var start = sess.StartTime;
            foreach (var other in sess.Bookings.Where(o => o.Status is not (BookingStatus.Cancelled or BookingStatus.DidNotAttend)).OrderBy(o => o.Order))
            {
                if (other.Id == b.Id) break;
                start = start.AddMinutes(other.DurationMinutes + TheatreService.TurnoverMinutes);
            }
            var arrive = start.AddMinutes(-ArrivalLeadMinutes) < sess.StartTime.AddMinutes(-30) ? sess.StartTime.AddMinutes(-30) : start.AddMinutes(-ArrivalLeadMinutes);
            v["BookingDate"] = LongDate(sess.Date);
            v["StartTime"] = Time(start);
            v["ArrivalTime"] = Time(arrive);
            v["FastingFrom"] = Time(arrive.AddHours(-6)) + (arrive.Hour < 6 ? " the night before" : "");
            v["Procedure"] = b.Procedure;
            v["Side"] = Side(b.Laterality);
            var people = await org.ClinicianNamesAsync(ct);
            v["Surgeon"] = people.GetValueOrDefault(sess.SurgeonId, "");
            v["Anaesthetist"] = sess.AnaesthetistId is { } an ? people.GetValueOrDefault(an, "") : "";
            v["Theatre"] = (await org.LocationNamesAsync(ct)).GetValueOrDefault(sess.LocationId, "");
            v["Anaesthetic"] = b.Anaesthetic.ToString().ToLowerInvariant();
        }
        if (referralId is { } rid && await referrals.Referrals.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rid, ct) is { } r)
        {
            v["ReferralNumber"] = r.Number;
            v["ReferralDate"] = r.ReceivedOn.ToString("d MMMM yyyy", Nz);
            v["Referrer"] = r.ReferrerName;
            v["ReferrerPractice"] = r.ReferrerOrganisation ?? "";
            v["ReferralReason"] = r.Reason;
            v["DeclineReason"] = r.DeclineReason ?? "";
            if (string.IsNullOrEmpty(v["Surgeon"])) v["Surgeon"] = (await org.ClinicianNamesAsync(ct)).GetValueOrDefault(r.SpecialistId, "");
            if (await referrals.Waitlist.AsNoTracking().FirstOrDefaultAsync(w => w.ReferralId == rid, ct) is { } w)
            {
                v["Priority"] = w.Priority == Priority.SemiUrgent ? "semi-urgent" : w.Priority.ToString().ToLowerInvariant();
                v["TargetDate"] = w.TargetDate.ToString("d MMMM yyyy", Nz);
                if (string.IsNullOrEmpty(v["Procedure"])) { v["Procedure"] = w.Procedure; v["Side"] = Side(w.Laterality); }
            }
        }
        return v;
    }

    private static string Side(Laterality l) => l switch { Laterality.Left => " (left)", Laterality.Right => " (right)", Laterality.Bilateral => " (both sides)", _ => "" };

    public async Task<(Letter? Letter, IReadOnlyList<string> Errors)> CreateDraftAsync(NewLetter n, string user, string? author, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == n.PatientId, ct);
        if (p is null) return (null, ["Choose the patient."]);
        await EnsureDefaultTemplatesAsync(ct);
        var t = await db.Templates.AsNoTracking().SingleOrDefaultAsync(x => x.Code == n.TemplateCode && x.IsActive, ct);
        if (t is null) return (null, ["Choose a letter template."]);
        if (n.BookingId is { } bid && !await theatre.Bookings.AnyAsync(b => b.Id == bid && b.PatientId == p.Id, ct)) return (null, ["That booking is not for this patient."]);
        if (n.ReferralId is { } rid && !await referrals.Referrals.AnyAsync(r => r.Id == rid && r.PatientId == p.Id, ct)) return (null, ["That referral is not for this patient."]);
        if (t.Kind is LetterKind.Appointment or LetterKind.Admission or LetterKind.GpNotification && n.BookingId is null) return (null, ["Choose the booking this letter is about."]);
        if (t.Kind is LetterKind.ReferralAcknowledgement or LetterKind.ReferralDeclined && n.ReferralId is null) return (null, ["Choose the referral this letter is about."]);

        var values = await ValuesAsync(p, n.BookingId, n.ReferralId, author, ct);
        string name, address;
        Guid? practiceId = null;
        switch (t.Recipient)
        {
            case RecipientType.Patient:
                name = values["PatientName"]; address = Lines(p.AddressLine1, p.AddressLine2, p.Suburb, Join(p.City, p.Postcode)); break;
            case RecipientType.Gp:
                var gp = p.GpPracticeHpiFacilityId is { } hpi ? await db.Practices.AsNoTracking().FirstOrDefaultAsync(x => x.HpiFacilityId == hpi, ct) : null;
                name = gp?.Name ?? "(GP practice not recorded – enter it)";
                address = gp is null ? "" : Lines(gp.AddressLine1, gp.AddressLine2, Join(gp.City, gp.Postcode));
                practiceId = gp?.Id; break;
            case RecipientType.Referrer:
                var r = n.ReferralId is { } id ? await referrals.Referrals.AsNoTracking().SingleAsync(x => x.Id == id, ct) : null;
                name = r?.ReferrerName ?? ""; address = r?.ReferrerOrganisation ?? "";
                if (r?.ReferrerOrganisation is { } orgName && await db.Practices.AsNoTracking().FirstOrDefaultAsync(x => x.Name == orgName, ct) is { } refPractice)
                { practiceId = refPractice.Id; address = Lines(refPractice.Name, refPractice.AddressLine1, refPractice.AddressLine2, Join(refPractice.City, refPractice.Postcode)); }
                break;
            default: name = ""; address = ""; break;
        }
        var settings = await SettingsAsync(ct);
        var letter = new Letter
        {
            Number = $"L{settings.NextLetterNumber:000000}", PatientId = p.Id, TemplateCode = t.Code, TemplateVersion = t.Version, Kind = t.Kind, Recipient = t.Recipient,
            RecipientName = name, RecipientAddress = address, GpPracticeId = practiceId, Subject = Render(t.Subject, values), Body = Render(t.Body, values),
            BookingId = n.BookingId, ReferralId = n.ReferralId, Author = author, CreatedAt = clock.GetUtcNow(), CreatedBy = user
        };
        settings.NextLetterNumber++;
        db.Letters.Add(letter);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "Letter", letter.Id.ToString(), p.Id.ToString(), reason: $"{letter.Number} {t.Name} to {name}", ct: ct);
        return (letter, []);
    }

    public async Task<string?> UpdateDraftAsync(Guid id, DraftEdit d, string user, CancellationToken ct = default)
    {
        var l = await db.Letters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return "Letter not found.";
        if (l.Status != LetterStatus.Draft) return "Only draft letters can be changed. Void it and write a new one.";
        if (string.IsNullOrWhiteSpace(d.RecipientName) || string.IsNullOrWhiteSpace(d.Subject) || string.IsNullOrWhiteSpace(d.Body)) return "Enter the recipient, subject and letter text.";
        if (d.Body.Length > 20_000) return "The letter is too long.";
        l.RecipientName = d.RecipientName.Trim(); l.RecipientAddress = Blank(d.RecipientAddress); l.Subject = d.Subject.Trim(); l.Body = d.Body.Replace("\r\n", "\n").Trim();
        l.Author = Blank(d.Author);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "Letter", l.Id.ToString(), l.PatientId.ToString(), reason: l.Number, ct: ct);
        return null;
    }

    public async Task<string?> FinaliseAsync(Guid id, string user, CancellationToken ct = default)
    {
        var l = await db.Letters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return "Letter not found.";
        if (l.Status != LetterStatus.Draft) return "This letter is already final.";
        var leftover = Token().Matches(l.Subject + " " + l.Body).Select(m => m.Value).Distinct().ToList();
        if (leftover.Count > 0) return $"Fill in or remove: {string.Join(", ", leftover)}.";
        if (l.Body.Contains("(GP practice not recorded", StringComparison.Ordinal) || l.RecipientName.StartsWith("(GP practice not recorded", StringComparison.Ordinal))
            return "Enter the GP practice (recipient) first.";
        l.Status = LetterStatus.Final; l.FinalisedAt = clock.GetUtcNow(); l.FinalisedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Finalise", "Letter", l.Id.ToString(), l.PatientId.ToString(), reason: l.Number, ct: ct);
        return null;
    }

    /// <summary>Records a print. Printing a final letter marks it sent by post/hand.</summary>
    public async Task<string?> RecordPrintAsync(Guid id, string user, CancellationToken ct = default)
    {
        var l = await db.Letters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return "Letter not found.";
        if (l.Status is LetterStatus.Draft or LetterStatus.Void) return "Finalise the letter before printing it.";
        l.PrintCount++;
        if (l.Status == LetterStatus.Final) { l.Status = LetterStatus.Sent; l.Channel = LetterChannel.Print; l.SentAt = clock.GetUtcNow(); l.SentBy = user; }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Print", "Letter", l.Id.ToString(), l.PatientId.ToString(), reason: $"{l.Number} (print {l.PrintCount})", ct: ct);
        return null;
    }

    public async Task<string?> VoidAsync(Guid id, string reason, string user, CancellationToken ct = default)
    {
        var l = await db.Letters.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return "Letter not found.";
        if (l.Status is LetterStatus.Sent or LetterStatus.Acknowledged) return "This letter has been sent and cannot be voided. Write a correction letter instead.";
        if (l.Status == LetterStatus.Void) return "Already void.";
        if (string.IsNullOrWhiteSpace(reason)) return "Enter the reason for voiding.";
        l.Status = LetterStatus.Void; l.VoidReason = reason.Trim();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Void", "Letter", l.Id.ToString(), l.PatientId.ToString(), reason: $"{l.Number}: {reason}", ct: ct);
        return null;
    }

    public async Task<Letter?> GetAsync(Guid id, string user, string? workstation, CancellationToken ct = default)
    {
        var l = await db.Letters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is not null) await audit.WriteAsync(user, "View", "Letter", l.Id.ToString(), l.PatientId.ToString(), workstation: workstation, ct: ct);
        return l;
    }

    public async Task<IReadOnlyList<LetterRow>> ListAsync(string view, Guid? patientId, CancellationToken ct = default)
    {
        var q = db.Letters.AsNoTracking().AsQueryable();
        if (patientId is { } pid) q = q.Where(l => l.PatientId == pid);
        else q = view switch
        {
            "drafts" => q.Where(l => l.Status == LetterStatus.Draft),
            "to-send" => q.Where(l => l.Status == LetterStatus.Final),
            "problems" => q.Where(l => l.Status == LetterStatus.Failed || (l.Status == LetterStatus.Sent && l.Channel == LetterChannel.HealthLink && l.SentAt < clock.GetUtcNow().AddDays(-1))),
            _ => q.Where(l => l.CreatedAt >= clock.GetUtcNow().AddDays(-60)),
        };
        var list = await q.OrderByDescending(l => l.CreatedAt).Take(500).ToListAsync(ct);
        var ids = list.Select(l => l.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var practiceIds = list.Where(l => l.GpPracticeId != null).Select(l => l.GpPracticeId!.Value).Distinct().ToList();
        var edi = await db.Practices.AsNoTracking().Where(p => practiceIds.Contains(p.Id) && p.HealthLinkEdi != null).Select(p => p.Id).ToListAsync(ct);
        return list.Select(l =>
        {
            var p = people.GetValueOrDefault(l.PatientId);
            return new LetterRow(l.Id, l.Number, l.PatientId, p is null ? "(unknown)" : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}", p?.Nhi, p?.Mrn ?? "", l.Kind.ToString(),
                l.Recipient.ToString(), l.RecipientName, l.Subject, l.Status.ToString(), l.Channel.ToString(), l.CreatedAt, l.CreatedBy, l.SentAt, l.AckCode,
                l.GpPracticeId is { } g && edi.Contains(g));
        }).ToList();
    }

    public async Task<HealthLinkSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not null) return s;
        s = new HealthLinkSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    private static string Lines(params string?[] parts) => string.Join("\n", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static string? Join(string? a, string? b) => string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } s ? s : null;
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
