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
public sealed record XeroInvoice(string ContactName, string? Email, string InvoiceNumber, string? Reference, DateOnly Date, DateOnly DueDate, IReadOnlyList<XeroLineItem> Lines, string? ContactId = null);
/// <summary>A Xero ACCRECCREDIT credit note allocated to an invoice already in Xero (0.34.0). Amount includes GST.</summary>
public sealed record XeroCreditNote(string ContactName, string? ContactId, string Number, string InvoiceXeroId, DateOnly Date, decimal Amount, string AccountCode, string TaxType, string? Reference);
public sealed record XeroPayment(string InvoiceXeroId, string AccountCode, DateOnly Date, decimal Amount, string? Reference);
public sealed record XeroTokens(string AccessToken, string RefreshToken);

/// <summary>Xero Accounting API (OAuth 2.0). Implemented in the web app over HttpClient; faked in tests.</summary>
public interface IXeroApi
{
    Task<XeroTokens> RefreshAsync(string clientId, string clientSecret, string refreshToken, CancellationToken ct);
    Task<string> CreateInvoiceAsync(string accessToken, string tenantId, XeroInvoice invoice, CancellationToken ct);
    Task<string> CreatePaymentAsync(string accessToken, string tenantId, XeroPayment payment, CancellationToken ct);
    /// <summary>The Xero contact ID for this name (and email if given), or null if there isn't one (0.34.0).</summary>
    Task<string?> FindContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct);
    Task<string> CreateContactAsync(string accessToken, string tenantId, string name, string? email, CancellationToken ct);
    Task<string> CreateCreditNoteAsync(string accessToken, string tenantId, XeroCreditNote note, CancellationToken ct);
}

public sealed record XeroSettingsEdit(XeroMode Mode, string SalesAccountCode, string GstTaxType, string NoGstTaxType, string? PaymentAccountCode, string? TenantId, string? ClientId, string? ClientSecret, string? RefreshToken);
public sealed record XeroRun(XeroExport Export, byte[]? File, IReadOnlyList<string> Errors, IReadOnlyList<string>? ByHand = null);

/// <summary>
/// Sends issued invoices (and, in API mode, payments) to Xero. File mode makes Xero's sales-invoice import CSV – always available as the fallback.
/// Each invoice and payment goes once; XeroLinks records what went and when. Secrets are stored protected (DPAPI/Data Protection).
/// </summary>
public sealed class XeroService(ClaimsDbContext db, BillingDbContext billingDb, PatientsDbContext patients, ISecretProtector protector, IXeroApi api, AuditWriter audit, TimeProvider clock)
{
    public const string InvoiceSource = "Invoice";
    public const string PaymentSource = "Payment";
    public const string CreditNoteSource = "CreditNote";
    public const string RefundSource = "Refund";

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
        var byHand = new List<string>();
        byte[]? file = null;
        if (s.Mode == XeroMode.File)
        {
            var notes = await PendingCreditNotesAsync(@from, to, ct);
            if (invoices.Count == 0 && notes.Count == 0) return new(export, null, ["No invoices to send – everything in this period has already gone to Xero."]);
            file = Csv(xi.Select(x => x.Invoice).Concat(await CreditNotesAsCsvAsync(notes, s, ct)));
            foreach (var n in notes) db.XeroLinks.Add(new XeroLink { SourceId = n.Id, SourceType = CreditNoteSource, ExportId = export.Id, ExportedAt = export.CreatedAt });
            export.Payments = notes.Count;
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
            var contacts = new Dictionary<string, string?>(StringComparer.Ordinal);
            async Task<string?> ContactAsync(string name, string? email)
            {
                var key = Trunc($"{name.Trim().ToUpperInvariant()}|{email?.Trim().ToUpperInvariant()}", 300);
                if (contacts.TryGetValue(key, out var known)) return known;
                var saved = await db.XeroContacts.SingleOrDefaultAsync(c => c.Key == key, ct);
                if (saved is not null) return contacts[key] = saved.XeroContactId;
                var found = await api.FindContactAsync(token, s.TenantId!, name, email, ct);
                var how = found is null ? "Created" : "Matched";
                found ??= await api.CreateContactAsync(token, s.TenantId!, name, email, ct);
                db.XeroContacts.Add(new XeroContact { Key = key, Name = Trunc(name, 200), XeroContactId = Trunc(found, 50), How = how, At = clock.GetUtcNow() });
                return contacts[key] = found;
            }
            foreach (var (doc, inv0) in xi)
            {
                try
                {
                    var inv = inv0 with { ContactId = await ContactAsync(inv0.ContactName, inv0.Email) };
                    var id = await api.CreateInvoiceAsync(token, s.TenantId!, inv, ct);
                    db.XeroLinks.Add(new XeroLink { SourceId = doc.Id, SourceType = InvoiceSource, ExportId = export.Id, XeroId = Trunc(id, 50), ExportedAt = clock.GetUtcNow() });
                    export.Invoices++;
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { errors.Add($"{doc.Number}: {ex.Message}"); }
            }
            await db.SaveChangesAsync(ct);
            // Payments for invoices already in Xero.
            var links = await db.XeroLinks.AsNoTracking().Where(l => l.SourceType == InvoiceSource && l.XeroId != null).ToDictionaryAsync(l => l.SourceId, l => l.XeroId!, ct);
            var paidSent = await db.XeroLinks.Where(l => l.SourceType == PaymentSource || l.SourceType == CreditNoteSource || l.SourceType == RefundSource).Select(l => l.SourceId).ToListAsync(ct);
            var invIds = links.Keys.ToList();
            var payments = await billingDb.Payments.AsNoTracking().Where(p => invIds.Contains(p.DocumentId)).ToListAsync(ct);
            var invDocs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Where(d => invIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
            var who = (await ToXeroAsync([.. invDocs.Values], s, ct)).ToDictionary(t => t.Doc.Id, t => t.Invoice);
            foreach (var p in payments.Where(p => !paidSent.Contains(p.Id)))
            {
                if (p.Amount < 0)
                {
                    byHand.Add($"{invDocs[p.DocumentId].Number}: refund of {-p.Amount:0.00} on {p.PaidOn:d MMM yyyy} ({p.Method}) – record it in Xero as a refund.");
                    db.XeroLinks.Add(new XeroLink { SourceId = p.Id, SourceType = RefundSource, ExportId = export.Id, ExportedAt = clock.GetUtcNow() });
                    continue;
                }
                try
                {
                    if (p.Method == PaymentMethod.CreditNote)
                    {
                        var inv = who[p.DocumentId];
                        var d = invDocs[p.DocumentId];
                        var number = p.Reference?.Split(' ')[0] ?? $"CN-{p.Id:N}"[..12];
                        var noteId = await api.CreateCreditNoteAsync(token, s.TenantId!, new XeroCreditNote(inv.ContactName, await ContactAsync(inv.ContactName, inv.Email), number, links[p.DocumentId], p.PaidOn, p.Amount,
                            s.SalesAccountCode, d.Lines.Any(l => l.GstApplicable) ? s.GstTaxType : s.NoGstTaxType, p.Reference), ct);
                        db.XeroLinks.Add(new XeroLink { SourceId = p.Id, SourceType = CreditNoteSource, ExportId = export.Id, XeroId = Trunc(noteId, 50), ExportedAt = clock.GetUtcNow() });
                        export.Payments++;
                        continue;
                    }
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
        return new(export, file, errors, byHand);
    }

    /// <summary>Credit notes (0.34.0) on invoices issued in the period that haven't gone to Xero.</summary>
    private async Task<List<Payment>> PendingCreditNotesAsync(DateOnly @from, DateOnly to, CancellationToken ct)
    {
        var sent = await db.XeroLinks.Where(l => l.SourceType == CreditNoteSource).Select(l => l.SourceId).ToListAsync(ct);
        var list = await billingDb.Payments.AsNoTracking().Where(p => p.Method == PaymentMethod.CreditNote && p.PaidOn >= @from && p.PaidOn <= to).OrderBy(p => p.Reference).ToListAsync(ct);
        return [.. list.Where(p => !sent.Contains(p.Id))];
    }

    /// <summary>Xero's sales import turns a negative-total line set into a credit note; the invoice number goes in the reference so it can be allocated.</summary>
    private async Task<List<XeroInvoice>> CreditNotesAsCsvAsync(List<Payment> notes, XeroSettings s, CancellationToken ct)
    {
        var ids = notes.Select(n => n.DocumentId).Distinct().ToList();
        var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Where(d => ids.Contains(d.Id)).ToListAsync(ct);
        var inv = (await ToXeroAsync(docs, s, ct)).ToDictionary(t => t.Doc.Id);
        return [.. notes.Select(n =>
        {
            var (d, x) = inv[n.DocumentId];
            var gst = d.Lines.Any(l => l.GstApplicable) && d.GstPercent > 0;
            var exGst = gst ? BillingService.Money(n.Amount / (1 + d.GstPercent / 100m)) : n.Amount;
            var number = n.Reference?.Split(' ')[0] ?? "CN";
            return new XeroInvoice(x.ContactName, x.Email, number, $"Credit for {d.Number}", n.PaidOn, n.PaidOn,
                [new XeroLineItem($"Credit note for {d.Number}: {n.Reference}", 1, -exGst, s.SalesAccountCode, gst ? s.GstTaxType : s.NoGstTaxType, null)]);
        })];
    }

    /// <summary>The CSV for an earlier file-mode export, rebuilt from the invoices it sent.</summary>
    public async Task<(XeroExport? Export, byte[]? File)> ExportFileAsync(Guid exportId, CancellationToken ct = default)
    {
        var x = await db.XeroExports.AsNoTracking().SingleOrDefaultAsync(e => e.Id == exportId && e.Mode == XeroMode.File, ct);
        if (x is null) return (null, null);
        var ids = await db.XeroLinks.AsNoTracking().Where(l => l.ExportId == exportId && l.SourceType == InvoiceSource).Select(l => l.SourceId).ToListAsync(ct);
        var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Where(d => ids.Contains(d.Id)).OrderBy(d => d.Number).ToListAsync(ct);
        var s = await SettingsAsync(ct);
        var noteIds = await db.XeroLinks.AsNoTracking().Where(l => l.ExportId == exportId && l.SourceType == CreditNoteSource).Select(l => l.SourceId).ToListAsync(ct);
        var notes = await billingDb.Payments.AsNoTracking().Where(p => noteIds.Contains(p.Id)).OrderBy(p => p.Reference).ToListAsync(ct);
        return (x, Csv((await ToXeroAsync(docs, s, ct)).Select(t => t.Invoice).Concat(await CreditNotesAsCsvAsync(notes, s, ct))));
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
