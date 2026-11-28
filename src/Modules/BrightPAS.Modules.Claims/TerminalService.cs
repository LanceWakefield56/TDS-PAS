using System.Security.Cryptography;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Claims;

public sealed record TerminalResult(bool Approved, string? AuthCode, string? CardType, string? Last4, string? ResponseText);
public sealed record SettlementRow(string CardType, int Count, decimal Total);
public sealed record Settlement(DateOnly Date, int Approved, decimal Total, int Declined, int Pending, IReadOnlyList<SettlementRow> ByCard, IReadOnlyList<TerminalTransaction> Transactions,
    int Refunds = 0, decimal RefundTotal = 0m);
public sealed record TerminalStart(bool Ok, string? Error);
/// <summary>What the terminal says about a transaction: not finished yet (Complete false), or the result.</summary>
public sealed record TerminalPoll(bool Complete, TerminalResult? Result);

/// <summary>An integrated EFTPOS terminal (0.34.0, Windcave HIT – the web project has the HTTP client; faked in tests).</summary>
public interface ITerminalApi
{
    Task<TerminalStart> StartAsync(string url, string user, string key, string station, bool refund, decimal amount, string txnRef, CancellationToken ct);
    Task<TerminalPoll> StatusAsync(string url, string user, string key, string station, string txnRef, CancellationToken ct);
}

public sealed record TerminalSettingsEdit(TerminalProvider Provider, string? TerminalId, string? WindcaveUrl = null, string? WindcaveUser = null, string? WindcaveKey = null);

/// <summary>
/// EFTPOS at the front desk. Manual: staff key the amount into the terminal and record the result here (the fallback when no integration).
/// Simulator: for training and the test robot (amounts ending .51 decline, .52 fail). A real terminal integration (Windcave/Verifone) needs a merchant agreement.
/// Card numbers are never stored – only the card type and last four digits. Approved transactions record an EFTPOS payment on the invoice.
/// </summary>
public sealed class TerminalService(ClaimsDbContext db, BillingDbContext billingDb, BillingService billing, AuditWriter audit, TimeProvider clock,
    ITerminalApi terminal, BrightPAS.Modules.Reminders.ISecretProtector protector)
{
    private static readonly string[] CardTypes = ["Visa", "Mastercard", "EFTPOS", "Amex", "UnionPay"];
    public static IReadOnlyList<string> Cards => CardTypes;

    public async Task<ClaimsSettings> SettingsAsync(CancellationToken ct = default) => await db.Settings.SingleAsync(ct);

    public Task<string?> SaveSettingsAsync(TerminalProvider provider, string? terminalId, string user, CancellationToken ct = default) =>
        SaveSettingsAsync(new TerminalSettingsEdit(provider, terminalId), user, ct);

    public async Task<string?> SaveSettingsAsync(TerminalSettingsEdit e, string user, CancellationToken ct = default)
    {
        var (provider, terminalId) = (e.Provider, e.TerminalId);
        if (!Enum.IsDefined(provider)) return "Choose the terminal type.";
        var s = await db.Settings.SingleAsync(ct);
        var url = string.IsNullOrWhiteSpace(e.WindcaveUrl) ? s.WindcaveUrl ?? "https://uat.windcave.com" : e.WindcaveUrl.Trim().TrimEnd('/');
        if (provider == TerminalProvider.Windcave)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return "The Windcave address must start with https://.";
            if (string.IsNullOrWhiteSpace(terminalId)) return "Enter the terminal's station ID (on the terminal's set-up screen or from Windcave).";
            if (string.IsNullOrWhiteSpace(e.WindcaveUser ?? s.WindcaveUser)) return "Enter the Windcave HIT user name.";
            if (string.IsNullOrWhiteSpace(e.WindcaveKey) && s.WindcaveKeyProtected is null) return "Enter the Windcave HIT key.";
        }
        s.WindcaveUrl = url;
        if (!string.IsNullOrWhiteSpace(e.WindcaveUser)) s.WindcaveUser = e.WindcaveUser.Trim()[..Math.Min(100, e.WindcaveUser.Trim().Length)];
        if (!string.IsNullOrWhiteSpace(e.WindcaveKey)) s.WindcaveKeyProtected = protector.Protect(e.WindcaveKey.Trim());
        s.TerminalProvider = provider; s.TerminalId = string.IsNullOrWhiteSpace(terminalId) ? null : terminalId.Trim()[..Math.Min(30, terminalId.Trim().Length)];
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "TerminalSettings", "1", reason: provider.ToString(), ct: ct);
        return null;
    }

    public async Task<(TerminalTransaction? Txn, string? Error)> StartAsync(Guid invoiceId, decimal amount, string user, CancellationToken ct = default)
    {
        var d = await billingDb.Documents.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == invoiceId && x.Kind == DocumentKind.Invoice, ct);
        if (d is null) return (null, "Invoice not found.");
        if (d.Status is not (DocumentStatus.Issued or DocumentStatus.PartPaid)) return (null, "Issue the invoice before taking payment.");
        var balance = BillingService.Calculate(d).Balance;
        amount = BillingService.Money(amount);
        if (amount <= 0) return (null, "Enter the amount to charge.");
        if (amount > balance) return (null, $"The amount is more than the balance owing ({balance:0.00}).");
        if (await db.Terminal.AnyAsync(t => t.InvoiceId == invoiceId && t.Status == TerminalStatus.Pending, ct)) return (null, "A terminal payment for this invoice is already waiting – finish or cancel it first.");
        var s = await db.Settings.SingleAsync(ct);
        var now = clock.GetUtcNow();
        var t = new TerminalTransaction
        {
            InvoiceId = invoiceId,
            Amount = amount,
            Provider = s.TerminalProvider,
            TerminalId = s.TerminalId,
            StartedAt = now,
            StartedBy = user,
            MerchantReference = $"{d.Number}-{now.ToNz():HHmmss}-{RandomNumberGenerator.GetInt32(100, 999)}",
        };
        db.Terminal.Add(t);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Start", "TerminalTransaction", t.Id.ToString(), d.PatientId.ToString(), reason: $"{d.Number} {amount:0.00} {t.Provider}", ct: ct);
        if (t.Provider == TerminalProvider.Simulator)
        {
            var cents = (int)(amount * 100 % 100);
            var r = cents switch
            {
                51 => new TerminalResult(false, null, "Visa", "0002", "DECLINED – insufficient funds"),
                52 => null,
                _ => new TerminalResult(true, RandomNumberGenerator.GetInt32(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture), "Visa", "4242", "APPROVED"),
            };
            if (r is null)
            {
                t.Status = TerminalStatus.Failed; t.ResponseText = "No response from the terminal – check the terminal before trying again."; t.CompletedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                return (t, null);
            }
            var err = await CompleteAsync(t.Id, r, user, ct);
            if (err is not null) return (t, err);
        }
        else if (t.Provider == TerminalProvider.Windcave && await SendToTerminalAsync(t, s, false, ct) is { } sendErr) return (t, sendErr);
        return (t, null);
    }

    /// <summary>Sends a purchase or refund to the Windcave terminal; the result is read back with CheckAsync (the screen asks every few seconds).</summary>
    private async Task<string?> SendToTerminalAsync(TerminalTransaction t, ClaimsSettings s, bool refund, CancellationToken ct)
    {
        var key = protector.Unprotect(s.WindcaveKeyProtected);
        TerminalStart r;
        if (s.WindcaveUrl is null || s.WindcaveUser is null || key is null || s.TerminalId is null) r = new(false, "The Windcave terminal is not set up.");
        else
        {
            try { r = await terminal.StartAsync(s.WindcaveUrl, s.WindcaveUser, key, s.TerminalId, refund, t.Amount, t.MerchantReference, ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException) { r = new(false, "Windcave could not be reached: " + ex.Message); }
        }
        if (r.Ok) return null;
        t.Status = TerminalStatus.Failed; t.ResponseText = Trunc(r.Error ?? "The terminal did not start.", 300); t.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return t.ResponseText;
    }

    /// <summary>Asks the integrated terminal for the result of a waiting transaction and records it.</summary>
    public async Task<(string? Status, string? Error)> CheckAsync(Guid id, string user, CancellationToken ct = default)
    {
        var t = await db.Terminal.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return (null, "Terminal transaction not found.");
        if (t.Status != TerminalStatus.Pending || t.Provider != TerminalProvider.Windcave) return (t.Status.ToString(), null);
        var s = await db.Settings.AsNoTracking().SingleAsync(ct);
        var key = protector.Unprotect(s.WindcaveKeyProtected);
        if (s.WindcaveUrl is null || s.WindcaveUser is null || key is null || s.TerminalId is null) return (t.Status.ToString(), "The Windcave terminal is not set up.");
        TerminalPoll p;
        try { p = await terminal.StatusAsync(s.WindcaveUrl, s.WindcaveUser, key, t.TerminalId ?? s.TerminalId, t.MerchantReference, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException) { return (t.Status.ToString(), "Windcave could not be reached: " + ex.Message); }
        if (!p.Complete || p.Result is null) return (t.Status.ToString(), null);
        var card = p.Result.CardType is null ? null : CardTypes.FirstOrDefault(c => c.Equals(p.Result.CardType, StringComparison.OrdinalIgnoreCase));
        var last4 = p.Result.Last4 is { Length: >= 4 } l && l[^4..].All(char.IsAsciiDigit) ? l[^4..] : null;
        var err = await CompleteAsync(id, p.Result with { CardType = card, Last4 = last4, AuthCode = p.Result.Approved ? p.Result.AuthCode ?? "TERMINAL" : p.Result.AuthCode }, user, ct);
        var after = await db.Terminal.AsNoTracking().Where(x => x.Id == id).Select(x => x.Status).SingleAsync(ct);
        return (after.ToString(), err);
    }

    /// <summary>Refunds part or all of an approved EFTPOS purchase back to the card (0.34.0). The refund comes off the invoice as a negative payment once approved.</summary>
    public async Task<(TerminalTransaction? Txn, string? Error)> RefundAsync(Guid purchaseId, decimal amount, string? reason, string user, CancellationToken ct = default)
    {
        var p = await db.Terminal.SingleOrDefaultAsync(x => x.Id == purchaseId, ct);
        if (p is null) return (null, "Terminal transaction not found.");
        if (p.IsRefund || p.Status != TerminalStatus.Approved) return (null, "Only an approved EFTPOS payment can be refunded.");
        amount = BillingService.Money(amount);
        var left = p.Amount - p.Refunded;
        if (amount <= 0) return (null, "Enter the amount to refund.");
        if (amount > left) return (null, $"The most that can be refunded on this payment is {left:0.00}.");
        if (string.IsNullOrWhiteSpace(reason)) return (null, "Say why the money is being refunded.");
        if (await db.Terminal.AnyAsync(t => t.RefundOfId == p.Id && t.Status == TerminalStatus.Pending, ct)) return (null, "A refund for this payment is already waiting – finish or cancel it first.");
        var s = await db.Settings.SingleAsync(ct);
        var now = clock.GetUtcNow();
        var r = new TerminalTransaction
        {
            InvoiceId = p.InvoiceId,
            Amount = amount,
            Provider = s.TerminalProvider,
            TerminalId = s.TerminalId,
            StartedAt = now,
            StartedBy = user,
            IsRefund = true,
            RefundOfId = p.Id,
            Reason = reason.Trim()[..Math.Min(300, reason.Trim().Length)],
            MerchantReference = $"RF-{p.MerchantReference}"[..Math.Min(34, $"RF-{p.MerchantReference}".Length)] + $"-{RandomNumberGenerator.GetInt32(10, 99)}",
        };
        db.Terminal.Add(r);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "RefundStart", "TerminalTransaction", r.Id.ToString(), reason: $"{p.MerchantReference} -{amount:0.00} {r.Provider}: {r.Reason}", ct: ct);
        if (r.Provider == TerminalProvider.Simulator)
            return (r, await CompleteAsync(r.Id, new TerminalResult(true, RandomNumberGenerator.GetInt32(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture), p.CardType, p.Last4, "REFUND APPROVED"), user, ct));
        if (r.Provider == TerminalProvider.Windcave && await SendToTerminalAsync(r, s, true, ct) is { } err) return (r, err);
        return (r, null);
    }

    /// <summary>Records the terminal's result (manual terminals) – the receipt shows approved/declined, the auth code, card type and last four digits.</summary>
    public async Task<string?> CompleteAsync(Guid id, TerminalResult r, string user, CancellationToken ct = default)
    {
        var t = await db.Terminal.FindAsync([id], ct);
        if (t is null) return "Terminal transaction not found.";
        if (t.Status != TerminalStatus.Pending) return "This terminal payment has already been completed.";
        var last4 = (r.Last4 ?? "").Trim();
        if (last4.Length > 0 && (last4.Length != 4 || !last4.All(char.IsAsciiDigit))) return "Enter only the last four digits of the card – never the full card number.";
        if (r.Approved && string.IsNullOrWhiteSpace(r.AuthCode)) return "Enter the authorisation code from the receipt.";
        if (r.CardType is not null && !CardTypes.Contains(r.CardType, StringComparer.Ordinal)) return "Choose the card type.";
        t.Status = r.Approved ? TerminalStatus.Approved : TerminalStatus.Declined;
        t.AuthCode = string.IsNullOrWhiteSpace(r.AuthCode) ? null : r.AuthCode.Trim()[..Math.Min(20, r.AuthCode.Trim().Length)];
        t.CardType = r.CardType; t.Last4 = last4.Length == 4 ? last4 : null;
        t.ResponseText = string.IsNullOrWhiteSpace(r.ResponseText) ? (r.Approved ? "APPROVED" : "DECLINED") : r.ResponseText.Trim()[..Math.Min(300, r.ResponseText.Trim().Length)];
        t.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        if (r.Approved && t.IsRefund)
        {
            var orig = t.RefundOfId is { } oid ? await db.Terminal.FindAsync([oid], ct) : null;
            var err = await billing.RecordRefundAsync(t.InvoiceId, t.Amount, PaymentMethod.Eftpos, $"EFTPOS refund {t.CardType} {t.Last4} auth {t.AuthCode}".Trim(), user, ct);
            if (orig is not null) orig.Refunded += t.Amount;
            if (err is not null)
            {
                t.ResponseText = Trunc($"{t.ResponseText} – refund not recorded on the invoice: {err}", 300);
                await db.SaveChangesAsync(ct);
                return $"The terminal refunded the money but it could not be recorded on the invoice: {err} Record it by hand and tell Finance.";
            }
            t.PaymentRecorded = true;
            await db.SaveChangesAsync(ct);
        }
        else if (r.Approved)
        {
            var err = await billing.RecordPaymentAsync(t.InvoiceId, new NewPayment(t.CompletedAt.Value.NzDate(), t.Amount, PaymentMethod.Eftpos, $"EFTPOS {t.CardType} {t.Last4} auth {t.AuthCode}".Trim()), user, ct);
            if (err is not null)
            {
                t.ResponseText = Trunc($"{t.ResponseText} – payment not recorded: {err}", 300);
                await db.SaveChangesAsync(ct);
                return $"The terminal approved the payment but it could not be recorded on the invoice: {err} Record it by hand and tell Finance.";
            }
            t.PaymentRecorded = true;
            await db.SaveChangesAsync(ct);
        }
        await audit.WriteAsync(user, t.Status.ToString(), "TerminalTransaction", t.Id.ToString(), reason: $"{t.MerchantReference} {t.Amount:0.00} {t.CardType} {t.Last4}", ct: ct);
        return null;
    }

    public async Task<string?> CancelAsync(Guid id, string user, CancellationToken ct = default)
    {
        var t = await db.Terminal.FindAsync([id], ct);
        if (t is null) return "Terminal transaction not found.";
        if (t.Status != TerminalStatus.Pending) return "Only a waiting payment can be cancelled.";
        t.Status = TerminalStatus.Cancelled; t.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "TerminalTransaction", t.Id.ToString(), reason: t.MerchantReference, ct: ct);
        return null;
    }

    public Task<List<TerminalTransaction>> ForInvoiceAsync(Guid invoiceId, CancellationToken ct = default) =>
        db.Terminal.AsNoTracking().Where(t => t.InvoiceId == invoiceId).OrderByDescending(t => t.StartedAt).ToListAsync(ct);

    /// <summary>End-of-day settlement: compare with the terminal's settlement slip (NZ day, daylight saving aware).</summary>
    public async Task<Settlement> SettlementAsync(DateOnly date, CancellationToken ct = default)
    {
        var start = NzTime.StartOfDay(date); var end = NzTime.StartOfDay(date.AddDays(1));
        var list = await db.Terminal.AsNoTracking().Where(t => t.StartedAt >= start && t.StartedAt < end).OrderBy(t => t.StartedAt).ToListAsync(ct);
        var ok = list.Where(t => t.Status == TerminalStatus.Approved && !t.IsRefund).ToList();
        var refunds = list.Where(t => t.Status == TerminalStatus.Approved && t.IsRefund).ToList();
        return new(date, ok.Count, ok.Sum(t => t.Amount) - refunds.Sum(t => t.Amount), list.Count(t => t.Status == TerminalStatus.Declined), list.Count(t => t.Status == TerminalStatus.Pending),
            [.. ok.Concat(refunds).GroupBy(t => t.CardType ?? "Unknown").OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new SettlementRow(g.Key, g.Count(), g.Sum(t => t.IsRefund ? -t.Amount : t.Amount)))], list,
            refunds.Count, refunds.Sum(t => t.Amount));
    }

    private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;
}
