using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Estimates;

public sealed record OutgoingEmail(string FromAddress, string? FromName, string To, string? Bcc, string Subject, string Body, string AttachmentName, byte[] Attachment);

public sealed record MailSendResult(bool Ok, string? Error, bool Permanent = false);

/// <summary>
/// Sends estimate emails with the PDF attached, and builds Outlook drafts (.eml marked unsent). Implemented in the web project on the same
/// mail server as Reminders (Reminders › Settings), so there is one SMTP set-up; in test mode nothing leaves the server.
/// </summary>
public interface IEstimateMailer
{
    Task<string?> ConfigurationProblemAsync(CancellationToken ct);
    Task<MailSendResult> SendAsync(OutgoingEmail m, CancellationToken ct);
    byte[] Draft(OutgoingEmail m);
}

public enum CheckLevel { Info = 1, Warning = 2, Stop = 3 }

public sealed record RecipientCheck(CheckLevel Level, string Message);

public sealed record SendRequest(Recipient Recipient, string To, PdfPassword? Password, string? TypedPassword, bool Confirmed);

/// <summary>Email safety: right-recipient checks, the PDF password rule, Outlook drafts and the Outbox with its hold period.</summary>
public sealed class EstimateMail(EstDbContext db, EstimateService estimates, IEstimatePdf pdf, IEstimateMailer mailer, IDocumentCipher cipher, AuditWriter audit, TimeProvider clock)
{
    /// <summary>Default password rule for who it is going to: patients use the patient rule; insurers none unless set otherwise.</summary>
    public static PdfPassword DefaultPassword(EstSettings s, Recipient r) =>
        r == Recipient.Insurer && !s.InsurerSameAsPatient ? PdfPassword.None : s.PatientPassword;

    /// <summary>The PDF password, or null for none. Throws with a plain-English reason if it can't be made.</summary>
    public static string? PasswordFor(Estimate e, PdfPassword rule, string? typed)
    {
        switch (rule)
        {
            case PdfPassword.DobLong:
                return e.DateOfBirth?.ToString("ddMMyyyy") ?? throw new InvalidOperationException("The PDF password is the date of birth, but none is entered.");
            case PdfPassword.DobShort:
                return e.DateOfBirth?.ToString("ddMMyy") ?? throw new InvalidOperationException("The PDF password is the date of birth, but none is entered.");
            case PdfPassword.Nhi:
                if (string.IsNullOrWhiteSpace(e.Nhi) || e.Nhi.Trim().Length != 7) throw new InvalidOperationException("The PDF password is the NHI, but it is missing or not 7 characters.");
                return e.Nhi.Trim().ToUpperInvariant();
            case PdfPassword.Typed:
                if (string.IsNullOrWhiteSpace(typed) || typed.Trim().Length < 6) throw new InvalidOperationException("Type a PDF password of at least 6 characters.");
                return typed.Trim();
            default:
                return null;
        }
    }

    public static string? Hint(EstSettings s, PdfPassword rule) => rule switch
    {
        PdfPassword.DobShort => s.HintDobShort,
        PdfPassword.DobLong => s.HintDobLong,
        PdfPassword.Nhi => s.HintNhi,
        PdfPassword.Typed => s.HintTyped,
        _ => null
    };

    public static List<(string Name, string Address)> InsurerList(EstSettings s) =>
        [.. s.InsurerAddresses.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.IndexOf('=', StringComparison.Ordinal) is var i and > 0 ? (l[..i].Trim(), l[(i + 1)..].Trim()) : ("", ""))
            .Where(x => x.Item1.Length > 0)];

    /// <summary>The insurer's address from Settings when the estimate's insurer matches a listed name.</summary>
    public static string? InsurerAddress(EstSettings s, Estimate e)
    {
        if (string.IsNullOrWhiteSpace(e.Insurer)) return null;
        foreach (var (name, addr) in InsurerList(s))
            if (addr.Length > 0 && (e.Insurer.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(e.Insurer.Trim(), StringComparison.OrdinalIgnoreCase))) return addr;
        return null;
    }

    private static bool ValidAddress(string a)
    {
        if (a.Length is 0 or > 200 || a.Contains(' ', StringComparison.Ordinal)) return false;
        try { var m = new MailAddress(a); return string.Equals(m.Address, a, StringComparison.OrdinalIgnoreCase) && m.Host.Contains('.', StringComparison.Ordinal); }
        catch (FormatException) { return false; }
    }

    private async Task<Estimate> LoadAsync(Guid id, CancellationToken ct)
    {
        var e = await db.Estimates.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Estimate not found.");
        e.Lines = [.. e.Lines.OrderBy(l => l.Sequence)];
        return e;
    }

    /// <summary>Checks that help stop an estimate going to the wrong person. A Stop blocks sending.</summary>
    public async Task<List<RecipientCheck>> ChecksAsync(Guid estimateId, Recipient r, string? to, CancellationToken ct = default)
    {
        var e = await LoadAsync(estimateId, ct);
        var s = await estimates.SettingsAsync(ct);
        var list = new List<RecipientCheck>();
        var a = to?.Trim() ?? "";
        if (!ValidAddress(a)) list.Add(new(CheckLevel.Stop, "The email address is missing or not valid."));
        if (s.RequireIssued && e.Status == EstimateStatus.Draft) list.Add(new(CheckLevel.Stop, "This estimate is still a draft – click ‘Save & issue’ first."));
        if (e.Status == EstimateStatus.Cancelled) list.Add(new(CheckLevel.Stop, "This estimate has been cancelled."));
        if (list.Exists(c => c.Level == CheckLevel.Stop) && !ValidAddress(a)) return list;
        if (r != Recipient.Patient)
        {
            var insurers = InsurerList(s).Select(x => x.Address).Where(x => x.Contains('@', StringComparison.Ordinal)).ToList();
            if (r == Recipient.Insurer && string.IsNullOrWhiteSpace(e.PolicyNumber)) list.Add(new(CheckLevel.Warning, "No policy / membership number on this estimate – the insurer will usually need it."));
            if (r == Recipient.Insurer && insurers.Count > 0 && !insurers.Contains(a, StringComparer.OrdinalIgnoreCase))
                list.Add(new(CheckLevel.Warning, "This address isn’t one of the insurer addresses in Settings – check it’s right."));
            if (r == Recipient.Insurer && !string.IsNullOrWhiteSpace(e.Email) && a.Equals(e.Email.Trim(), StringComparison.OrdinalIgnoreCase))
                list.Add(new(CheckLevel.Warning, "This is the patient’s own address, not an insurer’s."));
            return list;
        }
        var lower = a.ToLowerInvariant();
        var sameEmail = await db.Estimates.AsNoTracking().Where(x => x.Id != e.Id && x.Email == lower)
            .Select(x => new { x.Ref, x.FirstName, x.LastName, x.Nhi, x.DateOfBirth }).ToListAsync(ct);
        foreach (var g in sameEmail.Where(o => !SamePerson(e, o.FirstName, o.LastName, o.Nhi, o.DateOfBirth)).GroupBy(o => $"{o.FirstName} {o.LastName}").Take(3))
        {
            var x = g.First();
            list.Add(new(CheckLevel.Warning, $"This email address was used for a different patient: {x.FirstName} {x.LastName}{(x.Nhi is not null ? $" ({x.Nhi})" : "")}, estimate {x.Ref}. Family members can share an address – please confirm."));
        }
        if (!string.IsNullOrWhiteSpace(e.Nhi))
        {
            var nhi = e.Nhi.Trim().ToUpperInvariant();
            var otherEmails = await db.Estimates.AsNoTracking().Where(x => x.Id != e.Id && x.Nhi == nhi && x.Email != null && x.Email != lower)
                .Select(x => new { x.Email, x.Ref }).ToListAsync(ct);
            foreach (var o in otherEmails.GroupBy(o => o.Email!.ToLowerInvariant()).Take(2))
                list.Add(new(CheckLevel.Warning, $"Earlier estimate {o.First().Ref} for this NHI used a different email: {o.First().Email}."));
            var otherName = await db.Estimates.AsNoTracking().Where(x => x.Id != e.Id && x.Nhi == nhi && (x.LastName != e.LastName || x.FirstName != e.FirstName))
                .Select(x => new { x.FirstName, x.LastName, x.Ref }).FirstOrDefaultAsync(ct);
            if (otherName is not null)
                list.Add(new(CheckLevel.Warning, $"NHI {nhi} is on estimate {otherName.Ref} under a different name ({otherName.FirstName} {otherName.LastName}). Check the NHI."));
        }
        var local = lower.Split('@')[0];
        bool InEmail(string? n) => !string.IsNullOrWhiteSpace(n) && n.Trim().Length >= 3 && local.Contains(n.Trim().ToLowerInvariant()[..Math.Min(n.Trim().Length, 5)], StringComparison.Ordinal);
        if (!InEmail(e.FirstName) && !InEmail(e.LastName))
            list.Add(new(CheckLevel.Info, "The email address doesn’t contain the patient’s name – that’s often fine, but read it back to be sure."));
        return list;
    }

    private static bool SamePerson(Estimate e, string f, string l, string? nhi, DateOnly? dob)
    {
        if (!string.IsNullOrWhiteSpace(nhi) && !string.IsNullOrWhiteSpace(e.Nhi)) return nhi.Equals(e.Nhi.Trim(), StringComparison.OrdinalIgnoreCase);
        return f.Equals(e.FirstName, StringComparison.OrdinalIgnoreCase) && l.Equals(e.LastName, StringComparison.OrdinalIgnoreCase) && (dob is null || e.DateOfBirth is null || dob == e.DateOfBirth);
    }

    public static string Fill(string text, Estimate e, EstSettings s) => text
        .Replace("{validity}", s.ValidityMonths.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{validuntil}", e.ValidUntil?.ToString("dd.MM.yyyy") ?? "", StringComparison.Ordinal)
        .Replace("{phone}", s.Phone, StringComparison.Ordinal)
        .Replace("{deposit}", s.DepositPercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{balancedays}", s.BalanceDaysAfter.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{ref}", e.Ref, StringComparison.Ordinal)
        .Replace("{first}", e.FirstName, StringComparison.Ordinal)
        .Replace("{last}", e.LastName, StringComparison.Ordinal)
        .Replace("{procedure}", e.ProcedureName ?? "", StringComparison.Ordinal)
        .Replace("{preparedby}", e.PreparedBy ?? "", StringComparison.Ordinal)
        .Replace("{insurer}", e.Insurer ?? "", StringComparison.Ordinal)
        .Replace("{policy}", e.PolicyNumber ?? "", StringComparison.Ordinal);

    private async Task<(Estimate E, OutgoingEmail Mail, bool Passworded, EstSettings S)> BuildAsync(Guid estimateId, SendRequest req, CancellationToken ct)
    {
        var checks = await ChecksAsync(estimateId, req.Recipient, req.To, ct);
        if (checks.Find(c => c.Level == CheckLevel.Stop) is { } stop) throw new InvalidOperationException(stop.Message);
        if (!req.Confirmed) throw new InvalidOperationException("Tick ‘right patient, right email address’ to confirm before sending.");
        var e = await LoadAsync(estimateId, ct);
        var s = await estimates.SettingsAsync(ct);
        var rule = req.Password ?? DefaultPassword(s, req.Recipient);
        var pw = PasswordFor(e, rule, req.TypedPassword);
        var subject = Fill(req.Recipient == Recipient.Insurer ? s.EmailSubjectInsurer : s.EmailSubject, e, s);
        var body = Fill(req.Recipient == Recipient.Insurer ? s.EmailBodyInsurer : s.EmailBody, e, s);
        if (pw is not null && req.Recipient == Recipient.Patient && Hint(s, rule) is { } hint)
        {
            // The password line goes straight after the greeting. The email states the rule – never the password.
            var idx = body.IndexOf("\n\n", StringComparison.Ordinal);
            body = idx > 0 ? body[..(idx + 2)] + hint + "\n\n" + body[(idx + 2)..] : hint + "\n\n" + body;
        }
        var mail = new OutgoingEmail(s.Email, s.FromName, req.To.Trim(), s.BccAddress, subject, body, EstimateService.PdfFileName(e), pdf.Render(e, s, pw));
        return (e, mail, pw is not null, s);
    }

    /// <summary>Outlook draft (.eml marked unsent): staff check and send it from Outlook. No hold period.</summary>
    public async Task<(byte[] Eml, string FileName)> DraftAsync(Guid estimateId, SendRequest req, string user, CancellationToken ct = default)
    {
        var (e, mail, pw, _) = await BuildAsync(estimateId, req, ct);
        var eml = mailer.Draft(mail);
        await MarkEmailedAsync(e.Id, ct);
        await audit.WriteAsync(user, "EmailDraft", "Estimate", e.Id.ToString(), e.PatientId?.ToString(),
            reason: $"{e.Ref} → {mail.To} ({req.Recipient}){(pw ? " password protected" : " no password")}", ct: ct);
        return (eml, Path.ChangeExtension(mail.AttachmentName, ".eml"));
    }

    /// <summary>Send from BrightPAS: the email waits in the Outbox for the hold period and can be stopped or sent now.</summary>
    public async Task<EstOutbox> QueueAsync(Guid estimateId, SendRequest req, string user, CancellationToken ct = default)
    {
        if (await mailer.ConfigurationProblemAsync(ct) is { } problem) throw new InvalidOperationException(problem);
        var (e, mail, pw, s) = await BuildAsync(estimateId, req, ct);
        var now = clock.GetUtcNow();
        var o = new EstOutbox
        {
            EstimateId = e.Id,
            EstimateRef = e.Ref,
            Recipient = req.Recipient,
            ToAddress = mail.To,
            Subject = mail.Subject,
            Body = mail.Body,
            PatientName = $"{e.FirstName} {e.LastName}" + (req.Recipient == Recipient.Insurer ? $" (to {e.Insurer ?? "insurer"})" : req.Recipient == Recipient.Other ? " (other)" : ""),
            AttachmentName = mail.AttachmentName,
            AttachmentCipher = cipher.Protect(mail.Attachment),
            PasswordProtected = pw,
            CreatedAt = now,
            CreatedBy = user,
            SendAfter = now.AddMinutes(Math.Max(0, s.HoldMinutes))
        };
        db.Outbox.Add(o);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "EmailQueued", "Estimate", e.Id.ToString(), e.PatientId?.ToString(),
            reason: $"{e.Ref} → {o.ToAddress} ({req.Recipient}), sends {o.SendAfter.ToNz():HH:mm}{(pw ? ", password protected" : ", no password")}", ct: ct);
        return o;
    }

    public async Task<List<EstOutbox>> OutboxAsync(bool all, CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().AddDays(-30);
        return await db.Outbox.AsNoTracking().Where(o => all ? o.CreatedAt >= since : o.Status == OutboxStatus.Held || o.Status == OutboxStatus.Failed)
            .OrderByDescending(o => o.CreatedAt).Take(300)
            .Select(o => new EstOutbox
            {
                Id = o.Id,
                EstimateId = o.EstimateId,
                EstimateRef = o.EstimateRef,
                PatientName = o.PatientName,
                Recipient = o.Recipient,
                ToAddress = o.ToAddress,
                Subject = o.Subject,
                PasswordProtected = o.PasswordProtected,
                Status = o.Status,
                CreatedAt = o.CreatedAt,
                CreatedBy = o.CreatedBy,
                SendAfter = o.SendAfter,
                SentAt = o.SentAt,
                StoppedAt = o.StoppedAt,
                StoppedBy = o.StoppedBy,
                Error = o.Error,
                Attempts = o.Attempts
            }).ToListAsync(ct);
    }

    public async Task<string?> StopAsync(Guid outboxId, string user, CancellationToken ct = default)
    {
        var o = await db.Outbox.SingleOrDefaultAsync(x => x.Id == outboxId, ct);
        if (o is null) return "Not found.";
        if (o.Status != OutboxStatus.Held) return "This email is no longer held – it has already been sent or stopped.";
        (o.Status, o.StoppedAt, o.StoppedBy) = (OutboxStatus.Stopped, clock.GetUtcNow(), user);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "EmailStopped", "Estimate", o.EstimateId.ToString(), reason: $"{o.EstimateRef} → {o.ToAddress} stopped before sending", ct: ct);
        return null;
    }

    public async Task<string?> SendNowAsync(Guid outboxId, string user, CancellationToken ct = default)
    {
        var o = await db.Outbox.SingleOrDefaultAsync(x => x.Id == outboxId, ct);
        if (o is null) return "Not found.";
        if (o.Status != OutboxStatus.Held) return "This email is no longer held.";
        o.SendAfter = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "EmailReleased", "Estimate", o.EstimateId.ToString(), reason: $"{o.EstimateRef} → {o.ToAddress} released early", ct: ct);
        await ProcessDueAsync(ct);
        return null;
    }

    /// <summary>Sends held emails whose hold period is over (the background worker calls this every 20 seconds). Failures retry up to 5 times.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.Outbox.Where(o => o.Status == OutboxStatus.Held && o.SendAfter <= now).OrderBy(o => o.SendAfter).Take(20).ToListAsync(ct);
        var sent = 0;
        foreach (var o in due)
        {
            var s = await estimates.SettingsAsync(ct);
            var r = await mailer.SendAsync(new OutgoingEmail(s.Email, s.FromName, o.ToAddress, s.BccAddress, o.Subject, o.Body, o.AttachmentName, cipher.Unprotect(o.AttachmentCipher)), ct);
            if (r.Ok)
            {
                (o.Status, o.SentAt, o.Error) = (OutboxStatus.Sent, clock.GetUtcNow(), null);
                o.AttachmentCipher = [];
                await MarkEmailedAsync(o.EstimateId, ct);
                sent++;
            }
            else
            {
                o.Attempts++;
                o.Error = r.Error is { Length: > 1000 } err ? err[..1000] : r.Error;
                if (r.Permanent || o.Attempts >= 5) o.Status = OutboxStatus.Failed; else o.SendAfter = clock.GetUtcNow().AddMinutes(2 * o.Attempts);
            }
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(o.CreatedBy, r.Ok ? "EmailSent" : "EmailFailed", "Estimate", o.EstimateId.ToString(), reason: $"{o.EstimateRef} → {o.ToAddress}{(r.Ok ? "" : ": " + r.Error)}", ct: ct);
        }
        return sent;
    }

    private async Task MarkEmailedAsync(Guid estimateId, CancellationToken ct)
    {
        var e = await db.Estimates.SingleOrDefaultAsync(x => x.Id == estimateId, ct);
        if (e is not null) { e.EmailedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); }
    }
}
