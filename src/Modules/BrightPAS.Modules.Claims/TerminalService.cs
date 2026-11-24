using System.Security.Cryptography;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Claims;

public sealed record TerminalResult(bool Approved, string? AuthCode, string? CardType, string? Last4, string? ResponseText);
public sealed record SettlementRow(string CardType, int Count, decimal Total);
public sealed record Settlement(DateOnly Date, int Approved, decimal Total, int Declined, int Pending, IReadOnlyList<SettlementRow> ByCard, IReadOnlyList<TerminalTransaction> Transactions);

/// <summary>
/// EFTPOS at the front desk. Manual: staff key the amount into the terminal and record the result here (the fallback when no integration).
/// Simulator: for training and the test robot (amounts ending .51 decline, .52 fail). A real terminal integration (Windcave/Verifone) needs a merchant agreement.
/// Card numbers are never stored – only the card type and last four digits. Approved transactions record an EFTPOS payment on the invoice.
/// </summary>
public sealed class TerminalService(ClaimsDbContext db, BillingDbContext billingDb, BillingService billing, AuditWriter audit, TimeProvider clock)
{
    private static readonly string[] CardTypes = ["Visa", "Mastercard", "EFTPOS", "Amex", "UnionPay"];
    public static IReadOnlyList<string> Cards => CardTypes;

    public async Task<ClaimsSettings> SettingsAsync(CancellationToken ct = default) => await db.Settings.SingleAsync(ct);

    public async Task<string?> SaveSettingsAsync(TerminalProvider provider, string? terminalId, string user, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(provider)) return "Choose the terminal type.";
        var s = await db.Settings.SingleAsync(ct);
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
        return (t, null);
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
        if (r.Approved)
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
        var ok = list.Where(t => t.Status == TerminalStatus.Approved).ToList();
        return new(date, ok.Count, ok.Sum(t => t.Amount), list.Count(t => t.Status == TerminalStatus.Declined), list.Count(t => t.Status == TerminalStatus.Pending),
            [.. ok.GroupBy(t => t.CardType ?? "Unknown").OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new SettlementRow(g.Key, g.Count(), g.Sum(t => t.Amount)))], list);
    }

    private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;
}
