using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Billing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;

namespace BrightPAS.Modules.Payments;

public sealed record StatementLine(Guid InvoiceId, string Number, DateOnly? IssuedOn, DateOnly? DueOn, decimal Total, decimal Paid, decimal Balance);
public sealed record RunRow(Guid Id, DateOnly StatementDate, DateTimeOffset CreatedAt, string CreatedBy, int Count, decimal Total, DateTimeOffset? SentAt, int ToPrint, int Printed);
public sealed record StatementRow(Guid Id, string Number, Guid PatientId, string Patient, decimal TotalDue, decimal Overdue, string Delivery, string Status, DateTimeOffset? SentAt, string? Note, decimal BalanceNow);
public sealed record StatementPdfModel(string ClinicName, IReadOnlyList<string> ClinicAddress, string? ClinicPhone, string? ClinicEmail, string? GstNumber, string? BankAccount,
    string Number, DateOnly StatementDate, string PatientName, IReadOnlyList<string> PatientAddress, string? Nhi, IReadOnlyList<StatementLine> Lines, decimal TotalDue, decimal Overdue,
    string? PayUrl, string? Message);

public interface IStatementPdf
{
    /// <summary>One or more statements in one PDF (each on its own page) – one for download, many for the posting batch.</summary>
    byte[] Render(IReadOnlyList<StatementPdfModel> statements);
}

/// <summary>
/// Patient statements (0.30.0, ADR 0043). A run takes every patient with self-funded invoices still owing (issued or part paid, total at least the
/// statement minimum) and snapshots them. Sending: email or SMS with a payment link where the patient has them and has not opted out; everyone else is
/// in the posting batch (one PDF, each statement with its own payment link). Insurer, ACC and Health NZ invoices go through Claims, not statements.
/// </summary>
public sealed class StatementService(PaymentsDbContext db, BillingDbContext billingDb, PatientsDbContext patients, PasDbContext core, PaymentLinkService links,
    ReminderService reminders, IStatementPdf pdf, AuditWriter audit, TimeProvider clock)
{
    public static List<StatementLine> ReadSnapshot(string json) => JsonSerializer.Deserialize<List<StatementLine>>(json, JsonSerializerOptions.Web) ?? [];

    public async Task<List<RunRow>> RunsAsync(CancellationToken ct = default) =>
        await db.Runs.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(60)
            .Select(r => new RunRow(r.Id, r.StatementDate, r.CreatedAt, r.CreatedBy, r.Count, r.Total, r.SentAt,
                r.Statements.Count(s => s.Delivery == StatementDelivery.Post && s.Status != StatementStatus.Printed),
                r.Statements.Count(s => s.Status == StatementStatus.Printed)))
            .ToListAsync(ct);

    /// <summary>Makes a run (not sent yet) for the given statement date. Only one unsent run at a time.</summary>
    public async Task<(StatementRun? Run, string? Error)> PrepareAsync(DateOnly statementDate, string user, CancellationToken ct = default)
    {
        var today = NzTime.Today(clock);
        if (statementDate > today) return (null, "The statement date cannot be in the future.");
        if (statementDate < today.AddDays(-31)) return (null, "The statement date must be within the last month.");
        if (await db.Runs.AnyAsync(r => r.SentAt == null, ct)) return (null, "A prepared run has not been sent yet. Send it or delete it first.");
        var s = await links.SettingsAsync(ct);
        var docs = await billingDb.Documents.AsNoTracking().Include(d => d.Lines).Include(d => d.Payments)
            .Where(d => d.Kind == DocumentKind.Invoice && d.Payer == Payer.SelfFunded && (d.Status == DocumentStatus.Issued || d.Status == DocumentStatus.PartPaid)
                && d.IssuedOn != null && d.IssuedOn <= statementDate)
            .ToListAsync(ct);
        var byPatient = docs.Select(d => (d, t: BillingService.Calculate(d))).Where(x => x.t.Balance > 0).GroupBy(x => x.d.PatientId).ToList();
        var ids = byPatient.Select(g => g.Key).ToList();
        var live = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id) && p.MergedIntoId == null)
            .Select(p => new { p.Id, p.Email, p.MobilePhone }).ToDictionaryAsync(p => p.Id, ct);
        var run = new StatementRun { StatementDate = statementDate, CreatedAt = clock.GetUtcNow(), CreatedBy = user };
        foreach (var g in byPatient)
        {
            if (!live.TryGetValue(g.Key, out var p)) continue;
            var lines = g.OrderBy(x => x.d.IssuedOn).ThenBy(x => x.d.Number, StringComparer.Ordinal)
                .Select(x => new StatementLine(x.d.Id, x.d.Number ?? "", x.d.IssuedOn, x.d.DueOn, x.t.Total, x.t.Paid, x.t.Balance)).ToList();
            var total = lines.Sum(l => l.Balance);
            if (total < s.StatementMinimum || total <= 0) continue;
            run.Statements.Add(new Statement
            {
                RunId = run.Id,
                Number = $"ST{s.NextStatementNumber++:D6}",
                PatientId = g.Key,
                StatementDate = statementDate,
                TotalDue = total,
                Overdue = lines.Where(l => l.DueOn < statementDate).Sum(l => l.Balance),
                Delivery = !string.IsNullOrWhiteSpace(p.Email) ? StatementDelivery.Email : !string.IsNullOrWhiteSpace(p.MobilePhone) ? StatementDelivery.Sms : StatementDelivery.Post,
                Snapshot = JsonSerializer.Serialize(lines, JsonSerializerOptions.Web),
            });
        }
        if (run.Statements.Count == 0) return (null, "No patient has a self-funded balance to put on a statement.");
        run.Count = run.Statements.Count; run.Total = run.Statements.Sum(x => x.TotalDue);
        db.Runs.Add(run);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Prepare", "StatementRun", run.Id.ToString(), reason: $"{statementDate:yyyy-MM-dd}: {run.Count} statements, {run.Total:0.00}", ct: ct);
        return (run, null);
    }

    public async Task<List<StatementRow>> StatementsAsync(Guid runId, CancellationToken ct = default)
    {
        var list = await db.Statements.AsNoTracking().Where(s => s.RunId == runId).OrderBy(s => s.Number).ToListAsync(ct);
        return await RowsAsync(list, ct);
    }

    public async Task<List<StatementRow>> ForPatientAsync(Guid patientId, CancellationToken ct = default)
    {
        var list = await db.Statements.AsNoTracking().Where(s => s.PatientId == patientId).OrderByDescending(s => s.StatementDate).ThenByDescending(s => s.Number).Take(24).ToListAsync(ct);
        return await RowsAsync(list, ct);
    }

    private async Task<List<StatementRow>> RowsAsync(List<Statement> list, CancellationToken ct)
    {
        var ids = list.Select(s => s.PatientId).Distinct().ToList();
        var names = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FamilyName.ToUpperInvariant() + ", " + p.GivenNames, ct);
        var invIds = list.SelectMany(s => ReadSnapshot(s.Snapshot).Select(l => l.InvoiceId)).Distinct().ToList();
        var now = (await links.DueAsync(invIds, ct)).ToDictionary(d => d.InvoiceId, d => d.Balance);
        return [.. list.Select(s => new StatementRow(s.Id, s.Number, s.PatientId, names.GetValueOrDefault(s.PatientId, ""), s.TotalDue, s.Overdue, s.Delivery.ToString(), s.Status.ToString(),
            s.SentAt, s.Note, ReadSnapshot(s.Snapshot).Sum(l => now.GetValueOrDefault(l.InvoiceId))))];
    }

    public async Task<string?> DeleteRunAsync(Guid runId, string user, CancellationToken ct = default)
    {
        var run = await db.Runs.Include(r => r.Statements).SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return "Run not found.";
        if (run.SentAt is not null || run.Statements.Any(s => s.Status != StatementStatus.Prepared)) return "Statements in this run have been sent or printed, so it is kept.";
        db.Runs.Remove(run);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Delete", "StatementRun", run.Id.ToString(), reason: $"{run.StatementDate:yyyy-MM-dd} ({run.Count})", ct: ct);
        return null;
    }

    /// <summary>Sends every emailed/texted statement in the run with a payment link. Statements that cannot be sent (opted out, number wrong) move to the posting batch.</summary>
    public async Task<(int Sent, int ToPost, string? Error)> SendAsync(Guid runId, string user, CancellationToken ct = default)
    {
        var s = await links.SettingsAsync(ct);
        if (s.Provider == GatewayProvider.Off || s.PublicUrl is null) return (0, 0, "Set up online payments (provider and public web address) before sending statements – each one carries a payment link.");
        var run = await db.Runs.Include(r => r.Statements).SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return (0, 0, "Run not found.");
        if (run.SentAt is not null) return (0, 0, "This run has already been sent.");
        int sent = 0, post = 0;
        foreach (var st in run.Statements.Where(x => x.Status == StatementStatus.Prepared).OrderBy(x => x.Number))
        {
            if (st.Delivery == StatementDelivery.Post) { post++; continue; }
            var url = await links.CreateForStatementAsync(st, user, ct);
            var queued = await reminders.QueuePaymentLinkAsync(st.PatientId, MessageKind.Statement, url, $"statement:{st.Id}", user, ct);
            if (queued.Count == 0) { st.Delivery = StatementDelivery.Post; st.Note = "Could not be emailed or texted (no consent or no valid address) – posted instead."; post++; continue; }
            st.Status = StatementStatus.Sent; st.SentAt = clock.GetUtcNow(); sent++;
        }
        run.SentAt = clock.GetUtcNow(); run.SentBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Send", "StatementRun", run.Id.ToString(), reason: $"{sent} sent, {post} to post", ct: ct);
        return (sent, post, null);
    }

    /// <summary>The posting batch: every statement in the run to post, in one PDF, each with a fresh payment link (when online payments are on). Marks them printed.</summary>
    public async Task<(byte[]? Pdf, string? Error)> PrintBatchAsync(Guid runId, bool reprint, string user, CancellationToken ct = default)
    {
        var run = await db.Runs.Include(r => r.Statements).SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null) return (null, "Run not found.");
        if (run.SentAt is null) return (null, "Send the run first – that decides which statements are posted.");
        var batch = run.Statements.Where(x => x.Delivery == StatementDelivery.Post && (reprint || x.Status != StatementStatus.Printed)).OrderBy(x => x.Number).ToList();
        if (batch.Count == 0) return (null, "There are no statements to post in this run.");
        var s = await links.SettingsAsync(ct);
        var models = new List<StatementPdfModel>();
        foreach (var st in batch)
        {
            var url = s.Provider != GatewayProvider.Off && s.PublicUrl is not null ? await links.CreateForStatementAsync(st, user, ct) : null;
            models.Add(await ModelAsync(st, url, ct));
            st.Status = StatementStatus.Printed; st.SentAt ??= clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Print", "StatementRun", run.Id.ToString(), reason: $"{batch.Count} statements for posting", ct: ct);
        return (pdf.Render(models), null);
    }

    /// <summary>One statement as a PDF (staff, or the patient through their link). No payment link: links are only ever in the message or the posted letter.</summary>
    public async Task<(byte[]? Pdf, string? Number)> PdfAsync(Guid statementId, string user, CancellationToken ct = default)
    {
        var st = await db.Statements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == statementId, ct);
        if (st is null) return (null, null);
        await audit.WriteAsync(user, "View", "Statement", st.Id.ToString(), st.PatientId.ToString(), ct: ct);
        return (pdf.Render([await ModelAsync(st, null, ct)]), st.Number);
    }

    private async Task<StatementPdfModel> ModelAsync(Statement st, string? payUrl, CancellationToken ct)
    {
        var s = await links.SettingsAsync(ct);
        var b = await billingDb.Settings.AsNoTracking().SingleOrDefaultAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == st.PatientId, ct);
        var org = await core.Organisations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Tasman Day Surgery";
        var site = await core.Sites.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).FirstOrDefaultAsync(ct);
        static IReadOnlyList<string> Lines(params string?[] v) => [.. v.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())];
        var cityLine = string.Join(" ", new[] { p.City, p.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var siteCity = site is null ? null : string.Join(" ", new[] { site.City, site.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new StatementPdfModel(org, site is null ? [] : Lines(site.AddressLine1, site.AddressLine2, site.Suburb, siteCity), site?.Phone, site?.Email, b?.GstNumber, b?.BankAccount,
            st.Number, st.StatementDate, $"{p.GivenNames} {p.FamilyName}", Lines(p.AddressLine1, p.AddressLine2, p.Suburb, cityLine), p.Nhi,
            ReadSnapshot(st.Snapshot), st.TotalDue, st.Overdue, payUrl, s.StatementMessage);
    }

    /// <summary>The statement PDF for the patient's own link (after the date of birth check).</summary>
    public async Task<(byte[]? Pdf, string? Number, string? Error)> PublicPdfAsync(string token, DateOnly dob, CancellationToken ct = default)
    {
        var (l, error) = await links.VerifyAsync(token, dob, paidOk: true, ct);
        if (l is null) return (null, null, error);
        if (l.StatementId is not { } sid) return (null, null, "There is no statement for this link.");
        var (bytes, number) = await PdfAsync(sid, PaymentLinkService.PatientUser, ct);
        return (bytes, number, bytes is null ? "There is no statement for this link." : null);
    }
}
