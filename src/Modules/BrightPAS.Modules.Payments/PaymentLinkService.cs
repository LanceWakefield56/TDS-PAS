using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;

namespace BrightPAS.Modules.Payments;

public sealed record GatewayStart(string? RedirectUrl, string? SessionId, string? Error);
/// <summary>What the provider says about a payment session. Complete = the patient has finished (approved, declined or cancelled).</summary>
public sealed record GatewayResult(bool Complete, bool Approved, bool Cancelled, string? CardType, string? Last4, string? AuthCode, string? ResponseText, decimal? Amount);

/// <summary>Windcave REST API (hosted payment page). Faked in tests; the real one lives in the web project.</summary>
public interface IWindcaveApi
{
    Task<GatewayStart> CreateSessionAsync(string baseUrl, string user, string key, decimal amount, string merchantReference, string returnUrl, string notificationUrl, CancellationToken ct);
    Task<GatewayResult> QuerySessionAsync(string baseUrl, string user, string key, string sessionId, CancellationToken ct);
}

public sealed record InvoiceDue(Guid InvoiceId, string Number, DateOnly? IssuedOn, DateOnly? DueOn, decimal Total, decimal Paid, decimal Balance);
public sealed record PublicPayView(string ClinicName, string ClinicPhone, string FirstName, string? StatementNumber, DateOnly? StatementDate, IReadOnlyList<InvoiceDue> Invoices,
    decimal Balance, decimal Minimum, bool CanPay, string? WhyNot, IReadOnlyList<PublicPaymentRow> Payments, bool Simulator);
public sealed record PublicPaymentRow(string Status, decimal Amount, DateTimeOffset StartedAt, string? Card, string? Message);
public sealed record LinkRow(Guid Id, Guid PatientId, string Patient, Guid? InvoiceId, string? InvoiceNumber, Guid? StatementId, string? StatementNumber, string Status,
    DateTimeOffset CreatedAt, string CreatedBy, DateTimeOffset ExpiresAt, DateTimeOffset? LastOpenedAt, bool Expired);
public sealed record PaymentRow(Guid Id, Guid PatientId, string Patient, decimal Amount, string Status, string Provider, string MerchantReference, string? Card, string? AuthCode,
    string? ResponseText, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, bool NeedsAttention, string? AttentionReason, string? ResolvedNote, IReadOnlyList<string> AppliedTo);
public sealed record SettingsInput(GatewayProvider Provider, string? PublicUrl, int LinkDays, decimal MinimumPayment, string? WindcaveUrl, string? WindcaveUser, string? WindcaveKey,
    decimal StatementMinimum, string? StatementMessage);

/// <summary>
/// Payment links and online card payments (0.30.0, ADR 0043). The patient pays on the provider's hosted page; BrightPAS never sees the card number.
/// A result is believed only when BrightPAS asks the provider itself (never from the browser's return address). Each approved payment is applied to
/// the invoice(s) through BillingService, oldest first, exactly once even if the return page and the provider's notification arrive together.
/// </summary>
public sealed class PaymentLinkService(PaymentsDbContext db, BillingDbContext billingDb, BillingService billing, PatientsDbContext patients, PasDbContext core,
    ReminderService reminders, ISecretProtector protector, IWindcaveApi windcave, AuditWriter audit, TimeProvider clock)
{
    public const string PatientUser = "patient-link";
    public const int MaxDobAttempts = 5;
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private DateOnly Today => NzTime.Today(clock);

    // ------------------------------------------------------------------ settings
    public async Task<PaymentSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(ct);
        if (s is not null) return s;
        db.Settings.Add(s = new PaymentSettings());
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<string>> SaveSettingsAsync(SettingsInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(r.Provider)) errors.Add("Choose how patients pay online.");
        var url = string.IsNullOrWhiteSpace(r.PublicUrl) ? null : r.PublicUrl.Trim().TrimEnd('/');
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)) errors.Add("The public web address must start with https://.");
        if (r.LinkDays is < 2 or > 90) errors.Add("Links must last between 2 and 90 days.");
        if (r.MinimumPayment is < 1 or > 10000) errors.Add("The smallest part-payment must be between $1 and $10,000.");
        if (r.StatementMinimum is < 0 or > 10000) errors.Add("The smallest balance for a statement must be between $0 and $10,000.");
        var wurl = string.IsNullOrWhiteSpace(r.WindcaveUrl) ? "https://uat.windcave.com" : r.WindcaveUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(wurl, UriKind.Absolute, out var wu) || wu.Scheme != Uri.UriSchemeHttps) errors.Add("The Windcave address must start with https://.");
        var s = await SettingsAsync(ct);
        if (r.Provider == GatewayProvider.Windcave)
        {
            if (string.IsNullOrWhiteSpace(r.WindcaveUser)) errors.Add("Enter the Windcave API user name.");
            if (string.IsNullOrWhiteSpace(r.WindcaveKey) && s.WindcaveKeyProtected is null) errors.Add("Enter the Windcave API key.");
            if (url is null) errors.Add("Enter the public web address – Windcave sends patients back to it.");
        }
        if (errors.Count > 0) return errors;
        s.Provider = r.Provider; s.PublicUrl = url; s.LinkDays = r.LinkDays; s.MinimumPayment = BillingService.Money(r.MinimumPayment);
        s.WindcaveUrl = wurl; s.WindcaveUser = Blank(r.WindcaveUser);
        if (!string.IsNullOrWhiteSpace(r.WindcaveKey)) s.WindcaveKeyProtected = protector.Protect(r.WindcaveKey.Trim());
        s.StatementMinimum = BillingService.Money(r.StatementMinimum); s.StatementMessage = Blank(r.StatementMessage) is { } m ? Trunc(m, 300) : null;
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Settings", "PaymentSettings", "1", reason: $"Provider {r.Provider}; links {r.LinkDays} days", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ what is owing
    /// <summary>Current balances of the invoices a link covers, oldest first.</summary>
    public async Task<List<InvoiceDue>> DueAsync(PayLink link, CancellationToken ct = default)
    {
        List<Guid> ids;
        if (link.InvoiceId is { } one) ids = [one];
        else if (link.StatementId is { } sid && await db.Statements.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sid, ct) is { } st) ids = [.. StatementService.ReadSnapshot(st.Snapshot).Select(l => l.InvoiceId)];
        else ids = [];
        return await DueAsync(ids, ct);
    }

    public async Task<List<InvoiceDue>> DueAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct = default)
    {
        var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Include(d => d.Payments)
            .Where(d => invoiceIds.Contains(d.Id) && d.Kind == DocumentKind.Invoice).ToListAsync(ct);
        return [.. docs.Select(d => (d, t: BillingService.Calculate(d)))
            .OrderBy(x => x.d.IssuedOn ?? DateOnly.MaxValue).ThenBy(x => x.d.Number, StringComparer.Ordinal)
            .Select(x => new InvoiceDue(x.d.Id, x.d.Number ?? "", x.d.IssuedOn, x.d.DueOn, x.t.Total, x.t.Paid,
                x.d.Status is DocumentStatus.Issued or DocumentStatus.PartPaid ? x.t.Balance : 0m))];
    }

    // ------------------------------------------------------------------ staff: links
    /// <summary>Makes a payment link for one issued invoice and, if asked, sends it by SMS/email. Older open links for the invoice stop working.</summary>
    public async Task<(string? Link, int Sent, string? Error)> CreateForInvoiceAsync(Guid invoiceId, bool send, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s.Provider == GatewayProvider.Off) return (null, 0, "Online payments are off. Choose a payment provider in Online payments › Settings.");
        if (s.PublicUrl is null) return (null, 0, "Enter the public web address in Online payments › Settings first.");
        var d = await billingDb.Documents.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == invoiceId && x.Kind == DocumentKind.Invoice, ct);
        if (d is null) return (null, 0, "Invoice not found.");
        if (d.Status is not (DocumentStatus.Issued or DocumentStatus.PartPaid)) return (null, 0, "Only an issued invoice with money owing can be paid online.");
        if (BillingService.Calculate(d).Balance <= 0) return (null, 0, "Nothing is owing on this invoice.");
        var now = clock.GetUtcNow();
        foreach (var old in await db.Links.Where(l => l.InvoiceId == invoiceId && l.Status == PayLinkStatus.Open).ToListAsync(ct))
            (old.Status, old.CancelledAt, old.CancelledBy) = (PayLinkStatus.Cancelled, now, user);
        var (link, url) = NewLink(s, d.PatientId, user);
        link.InvoiceId = invoiceId;
        db.Links.Add(link);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Create", "PayLink", link.Id.ToString(), d.PatientId.ToString(), reason: $"Invoice {d.Number}", ct: ct);
        var sent = 0;
        if (send) sent = (await reminders.QueuePaymentLinkAsync(d.PatientId, MessageKind.PaymentLink, url, $"paylink:{link.Id}", user, ct)).Count;
        return (url, sent, null);
    }

    /// <summary>Statement links are made by StatementService when statements are sent or printed.</summary>
    public async Task<string> CreateForStatementAsync(Statement st, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var old in await db.Links.Where(l => l.StatementId == st.Id && l.Status == PayLinkStatus.Open).ToListAsync(ct))
            (old.Status, old.CancelledAt, old.CancelledBy) = (PayLinkStatus.Cancelled, now, user);
        var (link, url) = NewLink(s, st.PatientId, user);
        link.StatementId = st.Id;
        db.Links.Add(link);
        await db.SaveChangesAsync(ct);
        return url;
    }

    private (PayLink Link, string Url) NewLink(PaymentSettings s, Guid patientId, string user)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var now = clock.GetUtcNow();
        return (new PayLink { PatientId = patientId, TokenHash = Hash(token), CreatedAt = now, CreatedBy = user, ExpiresAt = now.AddDays(s.LinkDays) }, $"{s.PublicUrl}/pay/{token}");
    }

    public async Task<string?> CancelLinkAsync(Guid id, string user, CancellationToken ct = default)
    {
        var l = await db.Links.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return "Link not found.";
        if (l.Status != PayLinkStatus.Open) return "This link is already closed.";
        l.Status = PayLinkStatus.Cancelled; l.CancelledAt = clock.GetUtcNow(); l.CancelledBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "PayLink", l.Id.ToString(), l.PatientId.ToString(), ct: ct);
        return null;
    }

    public async Task<List<LinkRow>> LinksAsync(string? view, Guid? invoiceId, CancellationToken ct = default)
    {
        var q = db.Links.AsNoTracking().AsQueryable();
        if (invoiceId is not null) q = q.Where(l => l.InvoiceId == invoiceId);
        if (view == "open") q = q.Where(l => l.Status == PayLinkStatus.Open);
        var links = await q.OrderByDescending(l => l.CreatedAt).Take(300).ToListAsync(ct);
        var names = await Names(links.Select(l => l.PatientId), ct);
        var invIds = links.Where(l => l.InvoiceId != null).Select(l => l.InvoiceId!.Value).Distinct().ToList();
        var invNo = await billingDb.Documents.AsNoTracking().Where(d => invIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
        var stIds = links.Where(l => l.StatementId != null).Select(l => l.StatementId!.Value).Distinct().ToList();
        var stNo = await db.Statements.AsNoTracking().Where(s => stIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Number, ct);
        var now = clock.GetUtcNow();
        return [.. links.Select(l => new LinkRow(l.Id, l.PatientId, names.GetValueOrDefault(l.PatientId, ""), l.InvoiceId, l.InvoiceId is { } i ? invNo.GetValueOrDefault(i) : null,
            l.StatementId, l.StatementId is { } sid ? stNo.GetValueOrDefault(sid) : null, l.Status.ToString(), l.CreatedAt, l.CreatedBy, l.ExpiresAt, l.LastOpenedAt,
            l.Status == PayLinkStatus.Open && l.ExpiresAt <= now))];
    }

    public async Task<List<PaymentRow>> PaymentsAsync(string? view, Guid? invoiceId, CancellationToken ct = default)
    {
        var q = db.Payments.AsNoTracking().Include(p => p.Allocations).AsQueryable();
        if (view == "attention") q = q.Where(p => p.NeedsAttention && p.ResolvedAt == null);
        else if (view == "approved") q = q.Where(p => p.Status == OnlinePaymentStatus.Approved);
        else if (view == "unfinished") q = q.Where(p => p.Status == OnlinePaymentStatus.Started);
        if (invoiceId is not null)
        {
            var linkIds = db.Links.Where(l => l.InvoiceId == invoiceId).Select(l => l.Id);
            q = q.Where(p => linkIds.Contains(p.PayLinkId) || p.Allocations.Any(a => a.InvoiceId == invoiceId));
        }
        var list = await q.OrderByDescending(p => p.StartedAt).Take(300).ToListAsync(ct);
        var names = await Names(list.Select(p => p.PatientId), ct);
        return [.. list.Select(p => new PaymentRow(p.Id, p.PatientId, names.GetValueOrDefault(p.PatientId, ""), p.Amount, p.Status.ToString(), p.Provider.ToString(), p.MerchantReference,
            Card(p), p.AuthCode, p.ResponseText, p.StartedAt, p.CompletedAt, p.NeedsAttention && p.ResolvedAt == null, p.AttentionReason, p.ResolvedNote,
            [.. p.Allocations.Select(a => $"{a.InvoiceNumber} ${a.Amount:0.00}")]))];
    }

    public Task<int> AttentionCountAsync(CancellationToken ct = default) => db.Payments.CountAsync(p => p.NeedsAttention && p.ResolvedAt == null, ct);

    public async Task<string?> ResolveAsync(Guid id, string? note, string user, CancellationToken ct = default)
    {
        var p = await db.Payments.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return "Payment not found.";
        if (!p.NeedsAttention || p.ResolvedAt is not null) return "This payment does not need attention.";
        if (string.IsNullOrWhiteSpace(note)) return "Say what was done (for example refunded in Windcave, or applied to another invoice).";
        p.ResolvedAt = clock.GetUtcNow(); p.ResolvedBy = user; p.ResolvedNote = Trunc(note.Trim(), 500);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Resolve", "OnlinePayment", p.Id.ToString(), p.PatientId.ToString(), reason: p.ResolvedNote, ct: ct);
        return null;
    }

    private static string? Card(OnlinePayment p) => p.CardType is null && p.Last4 is null ? null : $"{p.CardType} ••{p.Last4}".Trim();

    private async Task<Dictionary<Guid, string>> Names(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await patients.Patients.AsNoTracking().Where(p => list.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FamilyName.ToUpperInvariant() + ", " + p.GivenNames, ct);
    }

    // ------------------------------------------------------------------ patient: open, verify, view
    private async Task<PayLink?> FindAsync(string token, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 100 ? null : await db.Links.SingleOrDefaultAsync(l => l.TokenHash == Hash(token), ct);

    /// <summary>"open", "paid" or "closed" – says nothing else before the date of birth is given.</summary>
    public async Task<string> PublicStatusAsync(string token, CancellationToken ct = default)
    {
        var l = await FindAsync(token, ct);
        if (l is null || l.Status == PayLinkStatus.Cancelled || l.ExpiresAt <= clock.GetUtcNow()) return "closed";
        return l.Status == PayLinkStatus.Paid ? "paid" : "open";
    }

    public async Task<(PayLink? Link, string? Error)> VerifyAsync(string token, DateOnly dob, bool paidOk = false, CancellationToken ct = default)
    {
        var l = await FindAsync(token, ct);
        var now = clock.GetUtcNow();
        if (l is null || l.Status == PayLinkStatus.Cancelled || l.ExpiresAt <= now) return (null, "This link has expired or been replaced. Please phone us for a new one.");
        if (l.Status == PayLinkStatus.Paid && !paidOk) return (null, "This account has been paid. Thank you.");
        if (l.LockedUntil > now) return (null, "Too many attempts. Please try again in 30 minutes or phone us.");
        var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == l.PatientId, ct);
        if (p is null || p.DateOfBirth != dob)
        {
            l.FailedAttempts++;
            if (l.FailedAttempts >= MaxDobAttempts) { l.LockedUntil = now.AddMinutes(30); l.FailedAttempts = 0; }
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(PatientUser, "PayLinkVerifyFailed", "PayLink", l.Id.ToString(), l.PatientId.ToString(), ct: ct);
            return (null, "That date of birth does not match our records.");
        }
        if (l.FailedAttempts > 0 || l.LastOpenedAt is null || now - l.LastOpenedAt > TimeSpan.FromMinutes(10))
        {
            l.FailedAttempts = 0; l.LastOpenedAt = now;
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(PatientUser, "PayLinkOpened", "PayLink", l.Id.ToString(), l.PatientId.ToString(), ct: ct);
        }
        return (l, null);
    }

    public async Task<(PublicPayView? View, string? Error)> ViewAsync(string token, DateOnly dob, CancellationToken ct = default)
    {
        var (l, error) = await VerifyAsync(token, dob, paidOk: true, ct);
        if (l is null) return (null, error);
        return (await BuildViewAsync(l, ct), null);
    }

    private async Task<PublicPayView> BuildViewAsync(PayLink l, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var due = await DueAsync(l, ct);
        var balance = due.Sum(d => d.Balance);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == l.PatientId, ct);
        var site = await core.Sites.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).FirstOrDefaultAsync(ct);
        var clinic = await core.Organisations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Tasman Day Surgery";
        var st = l.StatementId is { } sid ? await db.Statements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sid, ct) : null;
        var pays = await db.Payments.AsNoTracking().Where(x => x.PayLinkId == l.Id).OrderByDescending(x => x.StartedAt).Take(10).ToListAsync(ct);
        var why = s.Provider == GatewayProvider.Off ? "Online payment is not available at the moment. Please phone us to pay."
            : balance <= 0 ? "Nothing is owing. Thank you."
            : null;
        return new PublicPayView(clinic, site?.Phone ?? "", p.GivenNames.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? p.GivenNames,
            st?.Number, st?.StatementDate, due.Where(d => d.Balance > 0 || l.InvoiceId is not null).ToList(), balance, Math.Min(s.MinimumPayment, balance), why is null, why,
            [.. pays.Select(x => new PublicPaymentRow(x.Status.ToString(), x.Amount, x.StartedAt, Card(x), x.Status == OnlinePaymentStatus.Approved ? null : x.ResponseText))],
            s.Provider == GatewayProvider.Simulator);
    }

    // ------------------------------------------------------------------ patient: pay
    /// <summary>Starts a card payment: returns the address of the provider's payment page to send the patient to.</summary>
    public async Task<(string? RedirectUrl, string? Error)> StartAsync(string token, DateOnly dob, decimal amount, CancellationToken ct = default)
    {
        var (l, error) = await VerifyAsync(token, dob, ct: ct);
        if (l is null) return (null, error);
        var s = await SettingsAsync(ct);
        if (s.Provider == GatewayProvider.Off) return (null, "Online payment is not available at the moment. Please phone us to pay.");
        if (s.PublicUrl is null) return (null, "Online payment is not set up yet. Please phone us to pay.");
        var balance = (await DueAsync(l, ct)).Sum(d => d.Balance);
        amount = BillingService.Money(amount);
        if (balance <= 0) return (null, "Nothing is owing. Thank you.");
        if (amount > balance) return (null, $"The most you can pay is the balance owing, ${balance:0.00}.");
        var min = Math.Min(s.MinimumPayment, balance);
        if (amount < min) return (null, $"The smallest payment you can make online is ${min:0.00}.");
        var now = clock.GetUtcNow();
        var pay = new OnlinePayment
        {
            PayLinkId = l.Id,
            PatientId = l.PatientId,
            Amount = amount,
            Provider = s.Provider,
            StartedAt = now,
            MerchantReference = $"BP{now.ToNz():yyMMddHHmmss}{RandomNumberGenerator.GetInt32(1000, 9999)}",
        };
        db.Payments.Add(pay);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(PatientUser, "PaymentStarted", "OnlinePayment", pay.Id.ToString(), l.PatientId.ToString(), reason: $"{amount:0.00} {s.Provider}", ct: ct);
        var back = $"{s.PublicUrl}/pay/{token}";
        if (s.Provider == GatewayProvider.Simulator)
        {
            pay.SessionId = "SIM-" + pay.MerchantReference;
            await db.SaveChangesAsync(ct);
            return ($"{back}/simulator?ref={pay.MerchantReference}", null);
        }
        var key = protector.Unprotect(s.WindcaveKeyProtected);
        if (s.WindcaveUser is null || key is null) return (null, await FailAsync(pay, "Online payment is not set up correctly. Please phone us to pay.", ct));
        GatewayStart start;
        try { start = await windcave.CreateSessionAsync(s.WindcaveUrl, s.WindcaveUser, key, amount, pay.MerchantReference, $"{back}/done", $"{s.PublicUrl}/api/pay-notify", ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { start = new GatewayStart(null, null, ex.Message); }
        if (start.RedirectUrl is null || start.SessionId is null)
            return (null, await FailAsync(pay, "We could not reach the payment page. Please try again in a few minutes or phone us.", ct, start.Error));
        pay.SessionId = start.SessionId;
        await db.SaveChangesAsync(ct);
        return (start.RedirectUrl, null);
    }

    private async Task<string> FailAsync(OnlinePayment pay, string message, CancellationToken ct, string? detail = null)
    {
        pay.Status = OnlinePaymentStatus.Failed; pay.ResponseText = Trunc(detail is null ? message : $"{message} ({detail})", 300); pay.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return message;
    }

    /// <summary>The simulator's payment page: approve, decline or cancel (training and the robot; no money moves).</summary>
    public async Task<(string? Status, string? Error)> SimulateAsync(string token, string reference, string outcome, CancellationToken ct = default)
    {
        if ((await SettingsAsync(ct)).Provider != GatewayProvider.Simulator) return (null, "The payment simulator is off.");
        var l = await FindAsync(token, ct);
        var pay = l is null ? null : await db.Payments.SingleOrDefaultAsync(p => p.PayLinkId == l.Id && p.MerchantReference == reference && p.Provider == GatewayProvider.Simulator, ct);
        if (pay is null) return (null, "Payment not found.");
        var r = outcome switch
        {
            "approve" => new GatewayResult(true, true, false, "Visa", "4242", RandomNumberGenerator.GetInt32(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture), "APPROVED", pay.Amount),
            "decline" => new GatewayResult(true, false, false, "Visa", "0002", null, "DECLINED", pay.Amount),
            _ => new GatewayResult(true, false, true, null, null, null, "Cancelled by the patient", null),
        };
        var status = await CompleteAsync(pay.Id, r, ct);
        return (status.ToString(), null);
    }

    /// <summary>The patient is back from Windcave (or the notification arrived): ask Windcave what happened and apply it. Returns the payment's status.</summary>
    public async Task<(string? Status, string? Error)> CheckAsync(string? token, string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || sessionId.Length > 100) return (null, "Payment not found.");
        var pay = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.SessionId == sessionId, ct);
        if (pay is null) return (null, "Payment not found.");
        if (token is not null)
        {
            var l = await FindAsync(token, ct);
            if (l is null || l.Id != pay.PayLinkId) return (null, "Payment not found.");
        }
        if (pay.Status != OnlinePaymentStatus.Started) return (pay.Status.ToString(), null);
        return await QueryAsync(pay.Id, ct);
    }

    /// <summary>Asks the provider about an unfinished payment (return page, notification, the background check or Finance's "Check now").</summary>
    public async Task<(string? Status, string? Error)> QueryAsync(Guid paymentId, CancellationToken ct = default)
    {
        var pay = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, ct);
        if (pay is null) return (null, "Payment not found.");
        if (pay.Status != OnlinePaymentStatus.Started) return (pay.Status.ToString(), null);
        if (pay.Provider == GatewayProvider.Simulator)
        {
            if (pay.StartedAt < clock.GetUtcNow().AddHours(-1))
                return ((await CompleteAsync(pay.Id, new GatewayResult(true, false, true, null, null, null, "Not finished – the patient left the payment page", null), ct)).ToString(), null);
            return (pay.Status.ToString(), null);
        }
        var s = await SettingsAsync(ct);
        var key = protector.Unprotect(s.WindcaveKeyProtected);
        if (s.WindcaveUser is null || key is null || pay.SessionId is null) return (null, "Windcave is not set up.");
        GatewayResult r;
        try { r = await windcave.QuerySessionAsync(s.WindcaveUrl, s.WindcaveUser, key, pay.SessionId, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return (pay.Status.ToString(), "Windcave could not be reached: " + ex.Message); }
        if (!r.Complete)
        {
            if (pay.StartedAt < clock.GetUtcNow().AddDays(-2))
                return ((await CompleteAsync(pay.Id, new GatewayResult(true, false, true, null, null, null, "Not finished – the patient left the payment page", null), ct)).ToString(), null);
            return (pay.Status.ToString(), null);
        }
        return ((await CompleteAsync(pay.Id, r, ct)).ToString(), null);
    }

    /// <summary>Records the provider's answer exactly once (an atomic status change decides who applies it) and puts approved money on the invoices.</summary>
    private async Task<OnlinePaymentStatus> CompleteAsync(Guid paymentId, GatewayResult r, CancellationToken ct)
    {
        var status = r.Approved ? OnlinePaymentStatus.Approved : r.Cancelled ? OnlinePaymentStatus.Cancelled : OnlinePaymentStatus.Declined;
        var now = clock.GetUtcNow();
        var last4 = r.Last4 is { Length: >= 4 } l4 && l4[^4..].All(char.IsAsciiDigit) ? l4[^4..] : null;
        var won = await db.Payments.Where(p => p.Id == paymentId && p.Status == OnlinePaymentStatus.Started).ExecuteUpdateAsync(x => x
            .SetProperty(p => p.Status, status).SetProperty(p => p.CompletedAt, now)
            .SetProperty(p => p.CardType, r.CardType == null ? null : Trunc(r.CardType, 30)).SetProperty(p => p.Last4, last4)
            .SetProperty(p => p.AuthCode, r.AuthCode == null ? null : Trunc(r.AuthCode, 20)).SetProperty(p => p.ResponseText, r.ResponseText == null ? null : Trunc(r.ResponseText, 300)), ct);
        if (won == 0) return await db.Payments.AsNoTracking().Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync(ct);
        var pay = await db.Payments.SingleAsync(p => p.Id == paymentId, ct);
        await db.Entry(pay).ReloadAsync(ct); // the row was changed by the atomic update above
        await audit.WriteAsync(PatientUser, "Payment" + status, "OnlinePayment", pay.Id.ToString(), pay.PatientId.ToString(), reason: $"{pay.Amount:0.00} {r.ResponseText}", ct: ct);
        if (status != OnlinePaymentStatus.Approved) return status;

        if (r.Amount is { } paid && BillingService.Money(paid) != pay.Amount)
            Attention(pay, $"The provider approved ${paid:0.00} but ${pay.Amount:0.00} was asked for. Nothing was applied automatically – check the payment in the provider's portal.");
        else
        {
            var link = await db.Links.SingleAsync(x => x.Id == pay.PayLinkId, ct);
            var remaining = pay.Amount;
            foreach (var inv in await DueAsync(link, ct))
            {
                if (remaining <= 0) break;
                if (inv.Balance <= 0) continue;
                var amt = Math.Min(remaining, inv.Balance);
                var err = await billing.RecordPaymentAsync(inv.InvoiceId, new NewPayment(Today, amt, PaymentMethod.CreditCard, $"Online {pay.MerchantReference} {pay.AuthCode}".Trim()), PatientUser, ct);
                if (err is not null) continue;
                db.Allocations.Add(new PaymentAllocation { OnlinePaymentId = pay.Id, InvoiceId = inv.InvoiceId, InvoiceNumber = inv.Number, Amount = amt });
                remaining -= amt;
            }
            if (remaining > 0)
                Attention(pay, $"${remaining:0.00} was paid online but there was no balance left to put it on (it may have been paid another way meanwhile). Refund it or apply it by hand.");
            if ((await DueAsync(link, ct)).Sum(d => d.Balance) <= 0) link.Status = PayLinkStatus.Paid;
        }
        await db.SaveChangesAsync(ct);
        return status;
    }

    private static void Attention(OnlinePayment p, string why) { p.NeedsAttention = true; p.AttentionReason = Trunc(why, 500); }

    // ------------------------------------------------------------------ background and merge
    /// <summary>Unfinished payments started 5 minutes to 3 days ago: ask the provider again (the patient may have closed the browser before returning).</summary>
    public async Task<int> CheckUnfinishedAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var ids = await db.Payments.AsNoTracking().Where(p => p.Status == OnlinePaymentStatus.Started && p.StartedAt < now.AddMinutes(-5) && p.StartedAt > now.AddDays(-3))
            .OrderBy(p => p.StartedAt).Select(p => p.Id).Take(50).ToListAsync(ct);
        var done = 0;
        foreach (var id in ids)
            if ((await QueryAsync(id, ct)).Status is { } st && st != nameof(OnlinePaymentStatus.Started)) done++;
        return done;
    }

    public async Task<int> MergeAsync(Guid survivorId, Guid duplicateId, CancellationToken ct = default) =>
        await db.Links.Where(l => l.PatientId == duplicateId).ExecuteUpdateAsync(s => s.SetProperty(l => l.PatientId, survivorId), ct)
        + await db.Payments.Where(p => p.PatientId == duplicateId).ExecuteUpdateAsync(s => s.SetProperty(p => p.PatientId, survivorId), ct)
        + await db.Statements.Where(p => p.PatientId == duplicateId).ExecuteUpdateAsync(s => s.SetProperty(p => p.PatientId, survivorId), ct);

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string Trunc(string v, int max) => v.Length <= max ? v : v[..max];
}
