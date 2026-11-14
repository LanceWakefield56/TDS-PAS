using BrightPAS.Core.Integration;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Organisation;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Letters;

public sealed record HealthLinkSettingsEdit(HealthLinkMode Mode, string? SenderEdi, string? OutboxFolder, string? InboxFolder);

/// <summary>
/// Sends final letters to GP practices through HealthLink as HL7 v2.4 REF^I12 messages (the structure used for NZ referrals, status and discharge
/// letters, HISO 10011.2) with the letter text in an OBX FT segment. FolderDrop mode writes one .hl7 file per letter into the outbox folder that the
/// HealthLink messaging client sends, and reads ACK files back from the inbox folder. TestOutbox mode records the message without sending it.
/// The exact HealthLink profile (segments, EDI addressing) must be confirmed with HealthLink before live use.
/// </summary>
public sealed class HealthLinkService(LettersDbContext db, PatientsDbContext patients, OrganisationService org, AuditWriter audit, TimeProvider clock,
    IEnumerable<BrightPAS.Core.Integration.IMessageTap> taps)
{
    public async Task<IReadOnlyList<string>> UpdateSettingsAsync(HealthLinkSettingsEdit e, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var edi = string.IsNullOrWhiteSpace(e.SenderEdi) ? null : e.SenderEdi.Trim().ToLowerInvariant();
        if (e.Mode == HealthLinkMode.FolderDrop)
        {
            if (edi is null) errors.Add("Enter TDS's HealthLink EDI account.");
            if (string.IsNullOrWhiteSpace(e.OutboxFolder) || !Path.IsPathRooted(e.OutboxFolder)) errors.Add("Enter the full path of the HealthLink outbox folder (e.g. C:\\HealthLink\\Out).");
            if (!string.IsNullOrWhiteSpace(e.InboxFolder) && !Path.IsPathRooted(e.InboxFolder)) errors.Add("Enter the full path of the inbox folder, or leave it empty.");
            if (errors.Count == 0 && !Directory.Exists(e.OutboxFolder)) errors.Add($"The outbox folder {e.OutboxFolder} does not exist on the server, or BrightPAS cannot see it.");
        }
        if (edi is not null && !System.Text.RegularExpressions.Regex.IsMatch(edi, "^[a-z0-9]{3,12}$")) errors.Add("HealthLink EDI accounts are 3–12 letters or numbers.");
        if (errors.Count > 0) return errors;
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct) ?? db.Settings.Add(new HealthLinkSettings()).Entity;
        s.Mode = e.Mode; s.SenderEdi = edi; s.OutboxFolder = Blank(e.OutboxFolder); s.InboxFolder = Blank(e.InboxFolder);
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "HealthLinkSettings", "1", reason: $"{s.Mode} as {s.SenderEdi}", ct: ct);
        return errors;
    }

    public async Task<(bool Ok, string Message)> SendAsync(Guid letterId, string user, CancellationToken ct = default)
    {
        var l = await db.Letters.SingleOrDefaultAsync(x => x.Id == letterId, ct);
        if (l is null) return (false, "Letter not found.");
        if (l.Status != LetterStatus.Final && l.Status != LetterStatus.Failed) return (false, l.Status == LetterStatus.Draft ? "Finalise the letter first." : "This letter has already been sent.");
        var practice = l.GpPracticeId is { } pid ? await db.Practices.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid, ct) : null;
        if (practice?.HealthLinkEdi is null) return (false, "The recipient has no HealthLink EDI account. Add it under Letter templates › GP practices, or print the letter.");
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Id == 1, ct) ?? db.Settings.Add(new HealthLinkSettings()).Entity;
        if (s.Mode == HealthLinkMode.Off) return (false, "HealthLink sending is switched off (Letter templates › HealthLink).");
        var patient = await patients.Patients.AsNoTracking().SingleAsync(p => p.Id == l.PatientId, ct);
        var site = await org.MainSiteAsync(ct);
        var control = $"BP{clock.GetUtcNow():yyMMdd}{s.NextControlNumber:00000}";
        s.NextControlNumber++;
        var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.LetterId == l.Id, ct);
        var message = BuildRefI12(l, patient, practice, site?.Org.Name ?? "Clinic", site?.Site, s.SenderEdi ?? "brightpas", control, clock.GetLocalNow(), attachment);
        if (s.Mode == HealthLinkMode.FolderDrop)
        {
            try
            {
                var tmp = Path.Combine(s.OutboxFolder!, control + ".tmp");
                await File.WriteAllTextAsync(tmp, message, new UTF8Encoding(false), ct);
                File.Move(tmp, Path.Combine(s.OutboxFolder!, control + ".hl7"), overwrite: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                l.Status = LetterStatus.Failed; l.AckText = $"Could not write to the HealthLink outbox: {ex.Message}";
                await db.SaveChangesAsync(ct);
                await taps.RecordAllAsync(new("healthlink", false, "REF^I12", control, l.PatientId, message, false, l.AckText, l.Number, "Hl7v2"), ct);
                await audit.WriteAsync(user, "SendFailed", "Letter", l.Id.ToString(), l.PatientId.ToString(), reason: $"{l.Number}: {ex.Message}", ct: ct);
                return (false, l.AckText);
            }
        }
        l.Status = LetterStatus.Sent; l.Channel = LetterChannel.HealthLink; l.MessageControlId = control; l.SentAt = clock.GetUtcNow(); l.SentBy = user;
        l.AckCode = null; l.AckText = s.Mode == HealthLinkMode.TestOutbox ? "TEST MODE – recorded, not sent." : "Waiting for HealthLink acknowledgement.";
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Send", "Letter", l.Id.ToString(), l.PatientId.ToString(), reason: $"{l.Number} via HealthLink to {practice.HealthLinkEdi} ({control}, {s.Mode})", ct: ct);
        await taps.RecordAllAsync(new("healthlink", false, "REF^I12", control, l.PatientId, message, true, s.Mode == HealthLinkMode.TestOutbox ? "Test mode – recorded, not sent." : $"Written to the HealthLink outbox for {practice.HealthLinkEdi}.", l.Number, "Hl7v2"), ct);
        return (true, s.Mode == HealthLinkMode.TestOutbox ? $"Recorded in test mode as {control} (not sent)." : $"Sent to HealthLink as {control}.");
    }

    /// <summary>Reads HL7 ACK files from the inbox folder, updates the letters they acknowledge, and moves each file to a "processed" subfolder.</summary>
    public async Task<int> ProcessAcknowledgementsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        if (s is not { Mode: HealthLinkMode.FolderDrop, InboxFolder: { } inbox } || !Directory.Exists(inbox)) return 0;
        var done = Path.Combine(inbox, "processed");
        Directory.CreateDirectory(done);
        var count = 0;
        foreach (var file in Directory.GetFiles(inbox, "*.hl7").Concat(Directory.GetFiles(inbox, "*.ack")).Take(200))
        {
            var text = await File.ReadAllTextAsync(file, ct);
            if (ParseAck(text) is { } ack && await db.Letters.SingleOrDefaultAsync(l => l.MessageControlId == ack.ControlId, ct) is { } letter)
            {
                letter.AckCode = ack.Code; letter.AckText = ack.Text; letter.AckAt = clock.GetUtcNow();
                letter.Status = ack.Code is "AA" or "CA" ? LetterStatus.Acknowledged : LetterStatus.Failed;
                await db.SaveChangesAsync(ct);
                await audit.WriteAsync("healthlink", "Acknowledge", "Letter", letter.Id.ToString(), letter.PatientId.ToString(), reason: $"{letter.Number}: {ack.Code} {ack.Text}", ct: ct);
                await taps.RecordAllAsync(new("healthlink", true, "ACK", ack.ControlId, letter.PatientId, text, ack.Code is "AA" or "CA", $"{ack.Code} {ack.Text}".Trim(), letter.Number, "Hl7v2"), ct);
                count++;
            }
            File.Move(file, Path.Combine(done, Path.GetFileName(file)), overwrite: true);
        }
        return count;
    }

    public sealed record Ack(string ControlId, string Code, string Text);

    public static Ack? ParseAck(string hl7)
    {
        var msa = hl7.Replace("\r\n", "\r").Replace('\n', '\r').Split('\r').FirstOrDefault(x => x.StartsWith("MSA|", StringComparison.Ordinal));
        if (msa is null) return null;
        var f = msa.Split('|');
        return f.Length < 3 ? null : new Ack(f[2], f[1], f.Length > 3 ? f[3] : "");
    }

    /// <summary>HL7 v2.4 REF^I12 with the letter in OBX-5 (FT). Text is escaped so | ^ ~ \ &amp; in the letter cannot break the message.</summary>
    public static string BuildRefI12(Letter l, Patient p, GpPractice to, string clinic, Site? site, string senderEdi, string controlId, DateTimeOffset now, LetterAttachment? attachment = null)
    {
        var ts = now.ToString("yyyyMMddHHmmsszzz").Replace(":", "");
        var sex = p.Sex switch { BirthSex.Female => "F", BirthSex.Male => "M", _ => "U" };
        var addr = $"{E(p.AddressLine1)}^{E(p.AddressLine2)}^{E(p.City)}^^{E(p.Postcode)}";
        var siteAddr = site is null ? "" : $"{E(site.AddressLine1)}^{E(site.AddressLine2)}^{E(site.City)}^^{E(site.Postcode)}";
        var body = string.Join(@"\.br\", (l.Subject + "\n\n" + l.Body).Replace("\r\n", "\n").Split('\n').Select(E));
        var segs = new[]
        {
            $"MSH|^~\\&|BrightPAS|{E(senderEdi)}|HealthLink|{E(to.HealthLinkEdi)}|{ts}||REF^I12^REF_I12|{controlId}|P|2.4|||AL|NE|NZL",
            $"RF1|A^Accepted^HL70283|R^Routine^HL70280|{(l.Kind is LetterKind.ReferralAcknowledgement or LetterKind.ReferralDeclined ? "RES^Response" : "INF^Information")}^HL70282||O^Outgoing|{l.Number}|{ts}",
            $"PRD|RP^Referring provider^HL70286|{E(clinic)}|{siteAddr}||{E(site?.Phone)}||{E(site?.HpiFacilityId)}^^^HPI^FAC",
            $"PRD|RT^Referred to provider^HL70286|{E(to.Name)}|{E(to.AddressLine1)}^{E(to.AddressLine2)}^{E(to.City)}^^{E(to.Postcode)}||{E(to.Phone)}||{E(to.HpiFacilityId)}^^^HPI^FAC~{E(to.HealthLinkEdi)}^^^EDI",
            $"PID|1||{(p.Nhi is null ? "" : E(p.Nhi) + "^^^NHI^NHI~")}{E(p.Mrn)}^^^TDS^MR||{E(p.FamilyName)}^{E(p.GivenNames)}||{p.DateOfBirth:yyyyMMdd}|{sex}|||{addr}",
            $"OBR|1|{l.Number}||LETTER^{E(l.Kind.ToString())} letter^L|||{ts}||||||||||||||||||F",
            $"OBX|1|FT|LETTER^Letter^L||{body}||||||F|||{ts}",
        };
        // Attached PDF (e.g. an endoscopy report with pictures): OBX-2 ED, OBX-5 = ^AP^PDF^Base64^<data> (HL7 v2.4 encapsulated data).
        if (attachment is { Content.Length: > 0 })
            segs = [.. segs, $"OBX|2|ED|PDF^{E(attachment.FileName)}^L||^AP^PDF^Base64^{Convert.ToBase64String(attachment.Content)}||||||F|||{ts}"];
        return string.Join("\r", segs) + "\r";
    }

    /// <summary>HL7 v2 escaping (\F\ \S\ \R\ \E\ \T\).</summary>
    public static string E(string? s) => s is null ? "" : s.Replace("\\", @"\E\").Replace("|", @"\F\").Replace("^", @"\S\").Replace("~", @"\R\").Replace("&", @"\T\").Replace("\r", " ").Replace("\n", " ");

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
