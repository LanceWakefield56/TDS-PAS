using System.Globalization;
using System.Text;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;
using Microsoft.EntityFrameworkCore;

namespace BrightPAS.Modules.Claims;

public sealed record XeroLineItem(string Description, decimal Quantity, decimal UnitAmount, string AccountCode, string TaxType, string? ItemCode);
public sealed record XeroInvoice(string ContactName, string? Email, string InvoiceNumber, string? Reference, DateOnly Date, DateOnly DueDate, IReadOnlyList<XeroLineItem> Lines);
public sealed record XeroPayment(string InvoiceXeroId, string AccountCode, DateOnly Date, decimal Amount, string? Reference);
public sealed record XeroTokens(string AccessToken, string RefreshToken);

/// <summary>Xero Accounting API (OAuth 2.0). Implemented in the web app over HttpClient; faked in tests.</summary>
public interface IXeroApi
{
    Task<XeroTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct);
    Task<string> CreateInvoiceAsync(string accessToken, string tenantId, XeroInvoice invoice, CancellationToken ct);
    Task<string> CreatePaymentAsync(string accessToken, string tenantId, XeroPayment payment, CancellationToken ct);
}

public sealed record XeroSettingsEdit(XeroMode Mode, string SalesAccountCode, string GstTaxType, string NoGstTaxType, string? PaymentAccountCode, string? TenantId, string? ClientId, string? ClientSecret, string? RefreshToken);
public sealed record XeroRun(XeroExport Export, byte[]? File, IReadOnlyList<string> Errors);

/// <summary>
/// Sends issued invoices (and, in API mode, payments) to Xero. File mode makes Xero's sales-invoice import CSV – always available as the fallback.
/// Each invoice and payment goes once; XeroLinks records what went and when. Secrets are stored protected (DPAPI/Data Protection).
/// </summary>
public sealed class XeroService(ClaimsDbContext db, BillingDbContext billingDb, PatientsDbContext patients, ISecretProtector protector, IXeroApi api, AuditWriter audit, TimeProvider clock)
{
    public const string InvoiceSource = "Invoice";
    public const string PaymentSource = "Payment";

    public async Task<XeroSettings> SettingsAsync(CancellationToken ct = default) =>
        await db.Xero.SingleOrDefaultAsync(ct) ?? (await AddSettings(ct));

    private async Task<XeroSettings> AddSettings(CancellationToken ct) { var s = new XeroSettings(); db.Xero.Add(s); await db.SaveChangesAsync(ct); return s; }

    public async Task<string?> SaveSettingsAsync(XeroSettingsEdit e, string user, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(e.Mode)) return "Choose how BrightPAS sends to Xero.";
        if (string.IsNullOrWhiteSpace(e.SalesAccountCode) || e.SalesAccountCode.Trim().Length > 10) return "Enter the Xero sales account code (for example 200).";
        if (string.IsNullOrWhiteSpace(e.GstTaxType) || string.IsNullOrWhiteSpace(e.NoGstTaxType)) return "Enter the Xero tax types (OUTPUT2 for 15% GST, NONE for no GST).";
        var s = await SettingsAsync(ct);
        if (e.Mode == XeroMode.Api)
        {
            if (string.IsNullOrWhiteSpace(e.TenantId) || string.IsNullOrWhiteSpace(e.ClientId)) return "API mode needs the Xero tenant ID and app client ID.";
            if (string.IsNullOrWhiteSpace(e.ClientSecret) && s.ClientSecretProtected is null) return "Enter the Xero app client secret.";
            if (string.IsNullOrWhiteSpace(e.RefreshToken) && s.RefreshTokenProtected is null) return "Connect to Xero (paste the refresh token from the connection step).";
            if (string.IsNullOrWhiteSpace(e.PaymentAccountCode)) return "Enter the Xero bank account code that payments go to.";
        }
        s.Mode = e.Mode; s.SalesAccountCode = e.SalesAccountCode.Trim(); s.GstTaxType = e.GstTaxType.Trim().ToUpperInvariant(); s.NoGstTaxType = e.NoGstTaxType.Trim().ToUpperInvariant();
        s.PaymentAccountCode = Blank(e.PaymentAccountCode); s.TenantId = Blank(e.TenantId); s.ClientId = Blank(e.ClientId);
        if (!string.IsNullOrWhiteSpace(e.ClientSecret)) s.ClientSecretProtected = protector.Protect(e.ClientSecret.Trim());
        if (!string.IsNullOrWhiteSpace(e.RefreshToken)) s.RefreshTokenProtected = protector.Protect(e.RefreshToken.Trim());
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "XeroSettings", "1", reason: $"Mode {s.Mode}", ct: ct);
        return null;
    }

    /// <summary>Issued invoices not yet sent to Xero, issued in the period.</summary>
    public async Task<List<BillingDocument>> PendingInvoicesAsync(DateOnly @from, DateOnly to, CancellationToken ct = default)
    {
        var sent = db.XeroLinks.Where(l => l.SourceType == InvoiceSource).Select(l => l.SourceId);
        var sentIds = await sent.ToListAsync(ct);
        var list = await billingDb.Documents.AsNoTracking().Include(d => d.Lines)
            .Where(d => d.Kind == DocumentKind.Invoice && d.IssuedOn >= @from && d.IssuedOn <= to && d.Number != null
                && (d.Status == DocumentStatus.Issued || d.Status == DocumentStatus.PartPaid || d.Status == DocumentStatus.Paid))
            .OrderBy(d => d.Number).ToListAsync(ct);
        return [.. list.Where(d => !sentIds.Contains(d.Id))];
    }

    public async Task<XeroRun> ExportAsync(DateOnly @from, DateOnly to, string user, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s.Mode == XeroMode.Off) return new(new XeroExport(), null, ["Xero is turned off – choose file or API mode in Xero settings."]);
        if (to < @from || to.DayNumber - @from.DayNumber > 366) return new(new XeroExport(), null, ["Choose a period of up to a year."]);
        var invoices = await PendingInvoicesAsync(@from, to, ct);
        var xi = await ToXeroAsync(invoices, s, ct);
        var export = new XeroExport { Mode = s.Mode, CreatedAt = clock.GetUtcNow(), CreatedBy = user };
        var errors = new List<string>();
        byte[]? file = null;
        if (s.Mode == XeroMode.File)
        {
            if (invoices.Count == 0) return new(export, null, ["No invoices to send – everything in this period has already gone to Xero."]);
            file = Csv(xi.Select(x => x.Invoice));
            export.FileName = $"xero-invoices-{@from:yyyyMMdd}-{to:yyyyMMdd}.csv";
            db.XeroExports.Add(export);
            foreach (var d in invoices) db.XeroLinks.Add(new XeroLink { SourceId = d.Id, SourceType = InvoiceSource, ExportId = export.Id, ExportedAt = export.CreatedAt });
            export.Invoices = invoices.Count;
        }
        else
        {
            db.XeroExports.Add(export);
            string token;
            try { token = await TokenAsync(s, ct); }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                s.LastError = Trunc($"Could not connect to Xero: {ex.Message}", 1000); export.Errors = s.LastError;
                await db.SaveChangesAsync(ct);
                return new(export, null, [s.LastError + " Use file mode until it is fixed."]);
            }
            foreach (var (doc, inv) in xi)
            {
                try
                {
                    var id = await api.CreateInvoiceAsync(token, s.TenantId!, inv, ct);
                    db.XeroLinks.Add(new XeroLink { SourceId = doc.Id, SourceType = InvoiceSource, ExportId = export.Id, XeroId = Trunc(id, 50), ExportedAt = clock.GetUtcNow() });
                    export.Invoices++;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { errors.Add($"{doc.Number}: {ex.Message}"); }
            }
            await db.SaveChangesAsync(ct);
            // Payments for invoices already in Xero.
            var links = await db.XeroLinks.AsNoTracking().Where(l => l.SourceType == InvoiceSource && l.XeroId != null).ToDictionaryAsync(l => l.SourceId, l => l.XeroId!, ct);
            var paidSent = await db.XeroLinks.Where(l => l.SourceType == PaymentSource).Select(l => l.SourceId).ToListAsync(ct);
            var invIds = links.Keys.ToList();
            var payments = await billingDb.Payments.AsNoTracking().Where(p => invIds.Contains(p.DocumentId)).ToListAsync(ct);
            foreach (var p in payments.Where(p => !paidSent.Contains(p.Id)))
            {
                try
                {
                    var id = await api.CreatePaymentAsync(token, s.TenantId!, new XeroPayment(links[p.DocumentId], s.PaymentAccountCode!, p.PaidOn, p.Amount, p.Reference ?? p.Method.ToString()), ct);
                    db.XeroLinks.Add(new XeroLink { SourceId = p.Id, SourceType = PaymentSource, ExportId = export.Id, XeroId = Trunc(id, 50), ExportedAt = clock.GetUtcNow() });
                    export.Payments++;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { errors.Add($"Payment {p.PaidOn:d MMM} {p.Amount:0.00}: {ex.Message}"); }
            }
            s.LastError = errors.Count > 0 ? Trunc(string.Join("; ", errors), 1000) : null;
        }
        export.Errors = errors.Count > 0 ? Trunc(string.Join("\n", errors), 4000) : null;
        s.LastSyncAt = export.CreatedAt;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Export", "Xero", export.Id.ToString(), reason: $"{s.Mode} {export.Invoices} invoices {export.Payments} payments {errors.Count} errors", ct: ct);
        return new(export, file, errors);
    }

    /// <summary>The CSV for an earlier file-mode export, rebuilt from the invoices it sent.</summary>
    public async Task<(XeroExport? Export, byte[]? File)> ExportFileAsync(Guid exportId, CancellationToken ct = default)
    {
        var x = await db.XeroExports.AsNoTracking().SingleOrDefaultAsync(e => e.Id == exportId && e.Mode == XeroMode.File, ct);
        if (x is null) return (null, null);
        var ids = await db.XeroLinks.AsNoTracking().Where(l => l.ExportId == exportId && l.SourceType == InvoiceSource).Select(l => l.SourceId).ToListAsync(ct);
        var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Where(d => ids.Contains(d.Id)).OrderBy(d => d.Number).ToListAsync(ct);
        var s = await SettingsAsync(ct);
        return (x, Csv((await ToXeroAsync(docs, s, ct)).Select(t => t.Invoice)));
    }

    public Task<List<XeroExport>> ExportsAsync(CancellationToken ct = default) =>
        db.XeroExports.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);

    private async Task<string> TokenAsync(XeroSettings s, CancellationToken ct)
    {
        var secret = protector.Unprotect(s.ClientSecretProtected) ?? throw new InvalidOperationException("The Xero client secret can't be read – enter it again.");
        var refresh = protector.Unprotect(s.RefreshTokenProtected) ?? throw new InvalidOperationException("The Xero connection has expired – reconnect.");
        var t = await api.RefreshAsync(s.ClientId!, secret, refresh, ct);
        s.RefreshTokenProtected = protector.Protect(t.RefreshToken); // Xero rotates refresh tokens; save the new one straight away.
        await db.SaveChangesAsync(ct);
        return t.AccessToken;
    }

    private async Task<List<(BillingDocument Doc, XeroInvoice Invoice)>> ToXeroAsync(List<BillingDocument> invoices, XeroSettings s, CancellationToken ct)
    {
        var pids = invoices.Select(d => d.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return [.. invoices.Select(d =>
        {
            var p = people.GetValueOrDefault(d.PatientId);
            var patient = p is null ? "Patient" : $"{p.GivenNames} {p.FamilyName}";
            var contact = d.Payer == Payer.SelfFunded || string.IsNullOrWhiteSpace(d.PayerName) ? patient : d.PayerName!;
            var reference = d.Payer == Payer.SelfFunded ? p?.Mrn : $"{p?.Mrn} {d.PayerReference}".Trim();
            var lines = d.Lines.OrderBy(l => l.Sequence).Select(l => new XeroLineItem(l.Description, l.Quantity, l.UnitPrice, s.SalesAccountCode, l.GstApplicable ? s.GstTaxType : s.NoGstTaxType, Blank(l.Code))).ToList();
            var date = d.IssuedOn ?? d.CreatedAt.NzDate();
            return (d, new XeroInvoice(contact, d.Payer == Payer.SelfFunded ? p?.Email : null, d.Number!, reference, date, d.DueOn ?? date, lines));
        })];
    }

    /// <summary>Xero "Import sales invoices" CSV (tax-exclusive amounts; choose "Tax exclusive" on import). NZ dates d/MM/yyyy.</summary>
    public static byte[] Csv(IEnumerable<XeroInvoice> invoices)
    {
        var nz = CultureInfo.GetCultureInfo("en-NZ");
        var sb = new StringBuilder();
        sb.Append(ClaimsCsv.Row("*ContactName", "EmailAddress", "*InvoiceNumber", "Reference", "*InvoiceDate", "*DueDate", "InventoryItemCode", "*Description", "*Quantity", "*UnitAmount", "*AccountCode", "*TaxType", "Currency")).Append("\r\n");
        foreach (var i in invoices)
            foreach (var l in i.Lines)
                sb.Append(ClaimsCsv.Row(i.ContactName, i.Email, i.InvoiceNumber, i.Reference, i.Date.ToString("d/MM/yyyy", nz), i.DueDate.ToString("d/MM/yyyy", nz), l.ItemCode, l.Description,
                    l.Quantity.ToString("0.####", CultureInfo.InvariantCulture), ClaimsCsv.Money(l.UnitAmount), l.AccountCode, l.TaxType, "NZD")).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;
}
