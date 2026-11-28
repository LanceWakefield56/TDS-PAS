using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Modules.Reminders;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Payments;

public sealed record RefundRow(Guid Id, string Number, Guid OnlinePaymentId, string PaymentReference, Guid PatientId, string Patient, decimal Amount, string Status, string Reason,
    string? ProviderReference, string? Error, string? AppliedTo, DateTimeOffset CreatedAt, string CreatedBy);

/// <summary>
/// Refunds from BrightPAS (0.32.0, ADR 0045). Finance refunds part or all of an approved online payment back to the patient's card through the provider
/// (Windcave refund transaction, or the simulator). Money that was never applied to an invoice is refunded first (and clears the needs-attention flag);
/// the rest comes off the invoices it paid, newest first, as a negative payment in Billing so balances and statements stay right.
/// </summary>
public sealed class RefundService(PaymentsDbContext db, BillingService billing, PatientsDbContext patients, PaymentLinkService links, ISecretProtector protector,
    IWindcaveApi windcave, AuditWriter audit, TimeProvider clock)
{
    public async Task<(Refund? Refund, string? Error)> RefundAsync(Guid paymentId, decimal amount, string? reason, string user, CancellationToken ct = default)
    {
        var pay = await db.Payments.Include(p => p.Allocations).SingleOrDefaultAsync(p => p.Id == paymentId, ct);
        if (pay is null) return (null, "Payment not found.");
        if (pay.Status != OnlinePaymentStatus.Approved) return (null, "Only an approved payment can be refunded.");
        amount = BillingService.Money(amount);
        var left = pay.Amount - pay.Refunded;
        if (amount <= 0) return (null, "Enter the amount to refund.");
        if (amount > left) return (null, $"The most that can be refunded on this payment is ${left:0.00}.");
        if (string.IsNullOrWhiteSpace(reason)) return (null, "Say why the money is being refunded.");
        var s = await links.SettingsAsync(ct);
        var number = $"RF{s.NextRefundNumber++:D6}";
        var r = new Refund { Number = number, OnlinePaymentId = pay.Id, PatientId = pay.PatientId, Amount = amount, Reason = Trunc(reason.Trim(), 300), CreatedAt = clock.GetUtcNow(), CreatedBy = user };

        GatewayRefund g;
        if (pay.Provider == GatewayProvider.Simulator) g = new(true, $"SIMRF-{RandomNumberGenerator.GetInt32(100000, 999999).ToString(CultureInfo.InvariantCulture)}", null);
        else
        {
            var key = protector.Unprotect(s.WindcaveKeyProtected);
            if (s.WindcaveUser is null || key is null) g = new(false, null, "Windcave is not set up.");
            else if (pay.TransactionId is null) g = new(false, null, "Windcave's transaction id for this payment is not known – refund it in the Windcave portal and record it there.");
            else
            {
                try { g = await windcave.RefundAsync(s.WindcaveUrl, s.WindcaveUser, key, pay.TransactionId, amount, number, ct); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { g = new(false, null, "Windcave could not be reached: " + ex.Message); }
            }
        }
        r.Status = g.Ok ? RefundStatus.Done : RefundStatus.Failed;
        r.ProviderReference = g.Reference is null ? null : Trunc(g.Reference, 100);
        r.Error = g.Error is null ? null : Trunc(g.Error, 300);
        db.Refunds.Add(r);
        if (!g.Ok)
        {
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(user, "RefundFailed", "OnlinePayment", pay.Id.ToString(), pay.PatientId.ToString(), reason: $"{number} {amount:0.00}: {r.Error}", ct: ct);
            return (r, "The refund did not go through: " + (r.Error ?? "the provider declined it."));
        }

        // Money never applied to an invoice goes back first, then the invoices it paid, newest first.
        var unapplied = pay.Amount - pay.Allocations.Sum(a => a.Amount) - await db.Refunds.Where(x => x.OnlinePaymentId == pay.Id && x.Status == RefundStatus.Done).SumAsync(x => x.FromUnapplied, ct);
        r.FromUnapplied = Math.Max(0, Math.Min(amount, unapplied));
        var rest = amount - r.FromUnapplied;
        var parts = new List<string>();
        if (r.FromUnapplied > 0) parts.Add($"unapplied ${r.FromUnapplied:0.00}");
        foreach (var a in pay.Allocations.OrderByDescending(a => a.InvoiceNumber, StringComparer.Ordinal))
        {
            if (rest <= 0) break;
            var part = Math.Min(rest, a.Amount - a.Refunded);
            if (part <= 0) continue;
            var err = await billing.RecordRefundAsync(a.InvoiceId, part, PaymentMethod.CreditCard, $"Refund {number} ({pay.MerchantReference})", user, ct);
            if (err is not null) { parts.Add($"{a.InvoiceNumber} not changed ({err})"); continue; }
            a.Refunded += part; rest -= part;
            parts.Add($"{a.InvoiceNumber} ${part:0.00}");
        }
        r.AppliedTo = Trunc(string.Join("; ", parts), 500);
        pay.Refunded += amount;
        if (pay.NeedsAttention && pay.ResolvedAt is null && r.FromUnapplied > 0 && r.FromUnapplied >= unapplied)
        {
            pay.ResolvedAt = clock.GetUtcNow(); pay.ResolvedBy = user; pay.ResolvedNote = $"Refunded ${r.FromUnapplied:0.00} to the card ({number}).";
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Refund", "OnlinePayment", pay.Id.ToString(), pay.PatientId.ToString(), reason: $"{number} {amount:0.00} {r.AppliedTo}: {r.Reason}", ct: ct);
        return (r, null);
    }

    public async Task<List<RefundRow>> ListAsync(Guid? paymentId, CancellationToken ct = default)
    {
        var q = db.Refunds.AsNoTracking().AsQueryable();
        if (paymentId is not null) q = q.Where(r => r.OnlinePaymentId == paymentId);
        var list = await q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Number).Take(300).ToListAsync(ct);
        var payIds = list.Select(r => r.OnlinePaymentId).Distinct().ToList();
        var refs = await db.Payments.AsNoTracking().Where(p => payIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.MerchantReference, ct);
        var ids = list.Select(r => r.PatientId).Distinct().ToList();
        var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FamilyName.ToUpperInvariant() + ", " + p.GivenNames, ct);
        return [.. list.Select(r => new RefundRow(r.Id, r.Number, r.OnlinePaymentId, refs.GetValueOrDefault(r.OnlinePaymentId, ""), r.PatientId, names.GetValueOrDefault(r.PatientId, ""),
            r.Amount, r.Status.ToString(), r.Reason, r.ProviderReference, r.Error, r.AppliedTo, r.CreatedAt, r.CreatedBy))];
    }

    private static string Trunc(string v, int max) => v.Length <= max ? v : v[..max];
}
