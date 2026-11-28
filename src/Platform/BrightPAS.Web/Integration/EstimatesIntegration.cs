using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Licensing;
using BrightPAS.Modules.Estimates;
using BrightPAS.Modules.Reminders;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Estimate emails go through the mail server set up in Reminders › Settings (one SMTP set-up for BrightPAS). Test outbox mode sends nothing.
/// Outlook drafts are written as .eml files marked "X-Unsent: 1", which Outlook opens as a new message ready to check and send.
/// </summary>
public sealed class EstimateMailer(ReminderService reminders, ISecretProtector secrets) : IEstimateMailer
{
    public async Task<string?> ConfigurationProblemAsync(CancellationToken ct)
    {
        var s = await reminders.SettingsAsync(ct);
        return s.EmailProvider switch
        {
            EmailProvider.Off => "Email is switched off (Reminders › Settings). Use ‘Outlook draft’ instead, or switch email on.",
            EmailProvider.Smtp when string.IsNullOrWhiteSpace(s.SmtpHost) || string.IsNullOrWhiteSpace(s.FromAddress) =>
                "The mail server has not been set up (Reminders › Settings: SMTP server and ‘from’ address). Use ‘Outlook draft’ until it is.",
            _ => null
        };
    }

    private static MailMessage Build(OutgoingEmail m, string from, string? fromName)
    {
        var msg = new MailMessage(new MailAddress(from, fromName), new MailAddress(m.To))
        {
            Subject = m.Subject,
            Body = m.Body,
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            // Quoted-printable keeps the text readable in the saved .eml; Outlook decodes it either way.
            BodyTransferEncoding = System.Net.Mime.TransferEncoding.QuotedPrintable
        };
        if (!string.IsNullOrWhiteSpace(m.Bcc)) msg.Bcc.Add(new MailAddress(m.Bcc));
        msg.Attachments.Add(new Attachment(new MemoryStream(m.Attachment), m.AttachmentName, "application/pdf"));
        return msg;
    }

    public async Task<MailSendResult> SendAsync(OutgoingEmail m, CancellationToken ct)
    {
        if (await ConfigurationProblemAsync(ct) is { } problem) return new(false, problem);
        var s = await reminders.SettingsAsync(ct);
        if (s.EmailProvider == EmailProvider.TestOutbox) return new(true, null);
        try
        {
            using var msg = Build(m, s.FromAddress!, m.FromName ?? s.FromName);
            msg.Headers.Add("Message-ID", $"<{Guid.NewGuid():N}@brightpas>");
            using var client = new SmtpClient(s.SmtpHost, s.SmtpPort) { EnableSsl = s.SmtpStartTls, DeliveryMethod = SmtpDeliveryMethod.Network };
            if (!string.IsNullOrWhiteSpace(s.SmtpUser)) client.Credentials = new NetworkCredential(s.SmtpUser, secrets.Unprotect(s.SmtpPasswordProtected));
            await client.SendMailAsync(msg, ct);
            return new(true, null);
        }
        catch (FormatException ex) { return new(false, $"Invalid email address: {ex.Message}", true); }
        catch (SmtpFailedRecipientException ex) { return new(false, $"Email address refused: {ex.Message}", true); }
        catch (Exception ex) when (ex is SmtpException or IOException or InvalidOperationException) { return new(false, $"Email server problem: {ex.Message}"); }
    }

    public byte[] Draft(OutgoingEmail m)
    {
        var dir = Directory.CreateTempSubdirectory("bp-eml-");
        try
        {
            using (var msg = Build(m, m.FromAddress, m.FromName))
            {
                msg.Headers.Add("X-Unsent", "1");
                using var client = new SmtpClient { DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory, PickupDirectoryLocation = dir.FullName };
                client.Send(msg);
            }
            var file = dir.GetFiles("*.eml").Single();
            return File.ReadAllBytes(file.FullName);
        }
        finally { dir.Delete(true); }
    }
}

/// <summary>Sends held estimate emails when their hold period ends (every 20 seconds) and applies scheduled price changes (hourly).</summary>
public sealed class EstimatesWorker(IServiceScopeFactory scopes, LicenceCache licence, ILogger<EstimatesWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        var lastPrices = DateTimeOffset.MinValue;
        do
        {
            if (licence.Status.AccessTo(EstimatesModule.Id) != ModuleAccess.Full) continue;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sent = await scope.ServiceProvider.GetRequiredService<EstimateMail>().ProcessDueAsync(stoppingToken);
                if (sent > 0) log.LogInformation("Estimates: {Sent} held email(s) sent.", sent);
                if (DateTimeOffset.UtcNow - lastPrices > TimeSpan.FromHours(1))
                {
                    lastPrices = DateTimeOffset.UtcNow;
                    var applied = await scope.ServiceProvider.GetRequiredService<EstimateService>().ApplyDuePricesAsync("system", stoppingToken);
                    if (applied > 0) log.LogInformation("Estimates: {Applied} scheduled price change(s) applied.", applied);
                    var expired = await scope.ServiceProvider.GetRequiredService<EstimateFollowUpService>().ExpireAsync("system", stoppingToken);
                    if (expired > 0) log.LogInformation("Estimates: {Expired} estimate(s) expired.", expired);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Estimates outbox check failed; it will run again shortly."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// Estimate → Billing invoice (0.33.0, ADR 0046). Makes a draft invoice from an issued estimate for a registered patient: one line per priced line
/// (estimate prices include GST, so each line is entered without GST and Billing adds it back – a cent may differ through rounding), the payer from the
/// funding (insurer, policy and approval number as the reference), the surgeon, and a note of any deposit paid. Staff check and issue it in Billing.
/// </summary>
public sealed class EstimateInvoicer(BrightPAS.Modules.Estimates.EstDbContext est, BrightPAS.Modules.Billing.BillingService billing, EstimateFollowUpService followUp, LicenceCache licence)
{
    public async Task<(Guid? InvoiceId, string? Number, IReadOnlyList<string> Errors)> InvoiceAsync(Guid estimateId, string user, CancellationToken ct = default)
    {
        if (licence.Status.AccessTo(BrightPAS.Modules.Billing.BillingModule.Id) != ModuleAccess.Full) return (null, null, ["Invoices (Billing) are not licensed."]);
        var e = await est.Estimates.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == estimateId, ct);
        if (e is null) return (null, null, ["Estimate not found."]);
        if (e.Status != EstimateStatus.Issued) return (null, null, ["Only an issued estimate can be invoiced."]);
        if (e.InvoiceId is not null) return (null, null, [$"Invoice {e.InvoiceNumber ?? "already"} has been made from this estimate."]);
        if (e.PatientId is not { } patientId) return (null, null, ["Link the estimate to the patient's record first (Link to patient)."]);
        if (e.Funding == Funding.Insurance && string.IsNullOrWhiteSpace(e.Insurer)) return (null, null, ["Enter the insurer on the estimate first."]);
        var gst = (await billing.SettingsAsync(ct)).GstPercent;
        var lines = e.Lines.Where(l => l.Kind == LineKind.Fee && l.Amount is > 0).OrderBy(l => l.Sequence)
            .Select(l => new BrightPAS.Modules.Billing.LineInput(null, l.Label, 1, BrightPAS.Modules.Billing.BillingService.Money(l.Amount!.Value * 100m / (100m + gst)))).ToList();
        if (lines.Count == 0) return (null, null, ["The estimate has no priced lines to invoice."]);
        var payer = e.Funding == Funding.Insurance ? BrightPAS.Modules.Billing.Payer.Insurer : BrightPAS.Modules.Billing.Payer.SelfFunded;
        var reference = string.Join(" ", new[] { e.PolicyNumber, string.IsNullOrWhiteSpace(e.PriorApproval) ? null : "approval " + e.PriorApproval }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var note = $"From estimate {e.Ref} ({e.Subtotal:0.00} incl. GST).{(e.DepositPaid is { } dep ? $" Deposit {dep:0.00} paid {e.DepositPaidOn:d MMM yyyy} – record it as a payment once issued." : "")}";
        var (doc, errors) = await billing.CreateAsync(BrightPAS.Modules.Billing.DocumentKind.Invoice,
            new(patientId, payer, payer == BrightPAS.Modules.Billing.Payer.Insurer ? e.Insurer : null, reference.Length == 0 ? null : reference, e.SurgeonName, null, note, lines, e.BookingId), user, ct);
        if (doc is null) return (null, null, errors);
        await followUp.MarkInvoicedAsync(e.Id, doc.Id, doc.Number, user, ct);
        return (doc.Id, doc.Number, []);
    }
}
