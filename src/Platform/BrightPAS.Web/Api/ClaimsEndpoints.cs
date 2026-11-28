using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Claims;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Api;

/// <summary>Claims & payments (spec 7.12, ADR 0039): funders, prior approvals, claims, remittances, Xero, EFTPOS and NHI/HPI lookups.</summary>
public static class ClaimsEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record NewClaim(Guid InvoiceId, Guid FunderId, string? MemberNumber, string? AccClaimNumber, Guid? ApprovalId);
    public sealed record ReadyRequest(string? MemberNumber, string? AccClaimNumber, Guid? ApprovalId);
    public sealed record ResponseRequest(bool Accepted, string? Text);
    public sealed record ReasonRequest(string? Reason);
    public sealed record BatchRequest(Guid FunderId, ExportOptions? Export);
    public sealed record RemittanceRequest(Guid FunderId, string? FileName, string? Text);
    public sealed record XeroExportRequest(DateOnly From, DateOnly To, ExportOptions? Export);
    public sealed record TerminalStart(Guid InvoiceId, decimal Amount);
    public sealed record TerminalSettingsRequest(TerminalProvider Provider, string? TerminalId, string? WindcaveUrl = null, string? WindcaveUser = null, string? WindcaveKey = null);
    public sealed record TerminalRefundRequest(decimal Amount, string? Reason);
    public sealed record AcceptRequest(IReadOnlyList<string>? Fields);
    public sealed record ImportRequest(string? Text);

    private static object ClaimView(ClaimRow r) => new
    {
        r.Claim.Id,
        r.Claim.Number,
        r.Claim.InvoiceId,
        invoice = r.Invoice,
        r.Claim.PatientId,
        patient = r.Patient,
        mrn = r.Mrn,
        r.Claim.FunderId,
        funder = r.Funder,
        status = r.Claim.Status.ToString(),
        r.Claim.Amount,
        r.Claim.Paid,
        r.Claim.MemberNumber,
        r.Claim.AccClaimNumber,
        r.Claim.PriorApprovalId,
        r.Claim.SubmittedAt,
        r.Claim.ResponseAt,
        r.Claim.ResponseText,
        r.Claim.ExternalReference,
        r.Claim.LastCheckedAt,
        r.AgeDays,
        r.Claim.CreatedAt,
    };

    private static object ApprovalView(PriorApproval a) => new
    {
        a.Id,
        a.Number,
        a.PatientId,
        a.BookingId,
        a.FunderId,
        a.MemberNumber,
        a.AccClaimNumber,
        a.Procedure,
        a.AmountRequested,
        status = a.Status.ToString(),
        a.ApprovalReference,
        a.ApprovedAmount,
        a.ValidUntil,
        a.Notes,
        a.CreatedAt,
        a.CreatedBy,
        a.DecidedAt,
        a.DecidedBy,
        a.ExternalReference,
    };

    private static object TxnView(TerminalTransaction t) => new
    {
        t.Id,
        t.InvoiceId,
        t.Amount,
        provider = t.Provider.ToString(),
        t.MerchantReference,
        status = t.Status.ToString(),
        t.AuthCode,
        t.CardType,
        t.Last4,
        t.ResponseText,
        t.StartedAt,
        t.StartedBy,
        t.CompletedAt,
        t.PaymentRecorded,
        t.IsRefund,
        t.RefundOfId,
        t.Reason,
        t.Refunded,
    };

    public static void MapClaimsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/claims").RequireModule(ClaimsModule.Id);
        var any = Permissions.ClaimsAny;

        g.MapGet("/meta", async (ClaimsService svc, TerminalService term) => Results.Ok(new
        {
            funders = (await svc.FundersAsync(false)).Select(f => new
            {
                f.Id,
                f.Code,
                f.Name,
                kind = f.Kind.ToString(),
                submitBy = f.SubmitBy.ToString(),
                f.PriorApprovalRequired,
                f.ClaimsEmail,
                f.ProviderNumber,
                f.IsActive,
                f.Notes,
                api = f.Api.ToString(),
                f.ApiUrl,
                apiKeySet = f.ApiKeyProtected != null,
                f.LastApiAt,
                f.LastApiError
            }),
            cardTypes = TerminalService.Cards,
            terminal = (await term.SettingsAsync()).TerminalProvider.ToString(),
            terminalSettings = await term.SettingsAsync() is var ts ? new { provider = ts.TerminalProvider.ToString(), ts.TerminalId, ts.WindcaveUrl, ts.WindcaveUser, windcaveKeySet = ts.WindcaveKeyProtected != null } : null,
        })).RequireAuthorization(Permissions.AnyOf(Permissions.ClaimsView, Permissions.ClaimsManage, Permissions.ClaimsAdmin));
        // EFTPOS staff need the card types without seeing claims.
        g.MapGet("/terminal/meta", async (TerminalService term) => Results.Ok(new { cardTypes = TerminalService.Cards, terminal = (await term.SettingsAsync()).TerminalProvider.ToString() }))
            .RequireAuthorization(Permissions.AnyOf(Permissions.ClaimsManage, Permissions.PaymentsTake, Permissions.ClaimsAdmin));

        // Funders
        g.MapPost("/funders", async (FunderEdit e, ClaimsPrincipal u, ClaimsService svc) =>
        {
            var (f, error) = await svc.SaveFunderAsync(e, Me(u));
            return f is null ? Err(error) : Results.Ok(new { f.Id });
        }).RequireAuthorization(Permissions.ClaimsAdmin);
        g.MapPut("/funders/{id:guid}/api", async (Guid id, FunderApiEdit e, ClaimsPrincipal u, DirectClaimService d) => Err(await d.SaveApiAsync(id, e, Me(u)))).RequireAuthorization(Permissions.ClaimsAdmin);
        g.MapPost("/direct/check", async (ClaimsPrincipal u, DirectClaimService d) =>
        {
            var r = await d.CheckAsync(Me(u));
            return Results.Ok(new { r.Checked, r.Changed, r.Paid, r.Errors });
        }).RequireAuthorization(Permissions.ClaimsManage);

        // Claims
        g.MapGet("/", async (string? status, Guid? funderId, Guid? patientId, ClaimsService svc) =>
            Results.Ok((await svc.ClaimsAsync(Enum.TryParse<ClaimStatus>(status, out var st) ? st : null, funderId, patientId)).Select(ClaimView))).RequireAuthorization(any);
        g.MapGet("/{id:guid}", async (Guid id, ClaimsService svc) =>
        {
            var c = await svc.GetAsync(id);
            if (c is null) return Results.NotFound();
            var row = (await svc.ClaimsAsync(null, null, c.PatientId)).Single(r => r.Claim.Id == id);
            return Results.Ok(new { claim = ClaimView(row), lines = c.Lines.Select(l => new { l.Sequence, l.Code, l.Description, l.Amount }), checks = await svc.ChecksAsync(id) });
        }).RequireAuthorization(any);
        g.MapGet("/invoice/{invoiceId:guid}", async (Guid invoiceId, ClaimsService svc, Modules.Claims.ClaimsDbContext db) =>
        {
            var c = db.Claims.Where(x => x.InvoiceId == invoiceId).OrderByDescending(x => x.CreatedAt).Select(x => x.Id).FirstOrDefault();
            return Results.Ok(new { claimId = c == Guid.Empty ? (Guid?)null : c });
        }).RequireAuthorization(Permissions.AnyOf(Permissions.ClaimsView, Permissions.ClaimsManage, Permissions.ClaimsAdmin));
        g.MapPost("/", async (NewClaim r, ClaimsPrincipal u, ClaimsService svc) =>
        {
            var (c, errors) = await svc.CreateFromInvoiceAsync(r.InvoiceId, r.FunderId, r.MemberNumber, r.AccClaimNumber, r.ApprovalId, Me(u));
            return c is null ? Errs(errors) : Results.Ok(new { c.Id, c.Number });
        }).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/{id:guid}/ready", async (Guid id, ReadyRequest r, ClaimsPrincipal u, ClaimsService svc) =>
        {
            var (ready, checks, error) = await svc.MarkReadyAsync(id, r.MemberNumber, r.AccClaimNumber, r.ApprovalId, Me(u));
            return error is not null ? Err(error) : Results.Ok(new { ready, checks });
        }).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/{id:guid}/send", async (Guid id, ClaimsPrincipal u, DirectClaimService d) => Err(await d.SubmitAsync(id, Me(u)))).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/{id:guid}/submitted", async (Guid id, ClaimsPrincipal u, ClaimsService svc) => Err(await svc.MarkSubmittedAsync(id, Me(u)))).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/{id:guid}/response", async (Guid id, ResponseRequest r, ClaimsPrincipal u, ClaimsService svc) => Err(await svc.RecordResponseAsync(id, r.Accepted, r.Text, Me(u)))).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/{id:guid}/write-off", async (Guid id, ReasonRequest r, ClaimsPrincipal u, ClaimsService svc) => Err(await svc.WriteOffAsync(id, r.Reason ?? "", Me(u)))).RequireAuthorization(Permissions.ClaimsManage);
        g.MapGet("/aged", async (ClaimsService svc) => Results.Ok((await svc.AgedAsync()).Select(a => new { a.Funder, a.Open, a.Owing, a.Over30, a.Over60 }))).RequireAuthorization(any);

        // Batches (claim files)
        g.MapGet("/batches", async (ClaimsService svc) => Results.Ok(await svc.BatchesAsync())).RequireAuthorization(any);
        g.MapPost("/batches", async (BatchRequest r, ClaimsPrincipal u, ClaimsService svc, SecureExportService export) =>
        {
            var (b, file, error) = await svc.BuildBatchAsync(r.FunderId, Me(u));
            if (b is null) return Err(error);
            return await export.ExportAsync(r.Export, Path.GetFileNameWithoutExtension(b.FileName), [new(b.FileName, file!)], Me(u), "ClaimFile");
        }).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/batches/{id:guid}/download", async (Guid id, CodingEndpoints.DownloadRequest r, ClaimsPrincipal u, ClaimsService svc, SecureExportService export) =>
        {
            var (b, file) = await svc.BatchFileAsync(id);
            return b is null ? Results.NotFound() : await export.ExportAsync(r.Export, Path.GetFileNameWithoutExtension(b.FileName), [new(b.FileName, file!)], Me(u), "ClaimFile");
        }).RequireAuthorization(Permissions.ClaimsManage);

        // Prior approvals
        g.MapGet("/approvals", async (Guid? patientId, Guid? bookingId, bool? open, ClaimsService svc) =>
            Results.Ok((await svc.ApprovalsAsync(patientId, bookingId, open == true)).Select(ApprovalView))).RequireAuthorization(any);
        g.MapPost("/approvals", async (ApprovalEdit e, ClaimsPrincipal u, ClaimsService svc) =>
        {
            var (a, errors) = await svc.CreateApprovalAsync(e, Me(u));
            return a is null ? Errs(errors) : Results.Ok(new { a.Id, a.Number });
        }).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/approvals/{id:guid}/send", async (Guid id, ClaimsPrincipal u, DirectClaimService d) => Err(await d.RequestApprovalAsync(id, Me(u)))).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/approvals/{id:guid}/requested", async (Guid id, ClaimsPrincipal u, ClaimsService svc) => Err(await svc.MarkRequestedAsync(id, Me(u)))).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/approvals/{id:guid}/decide", async (Guid id, ApprovalDecision d, ClaimsPrincipal u, ClaimsService svc) => Err(await svc.DecideApprovalAsync(id, d, Me(u)))).RequireAuthorization(Permissions.ClaimsManage);

        // Remittances
        g.MapGet("/remittances", async (ClaimsService svc) => Results.Ok(await svc.RemittancesAsync())).RequireAuthorization(any);
        g.MapGet("/remittances/{id:guid}", async (Guid id, ClaimsService svc) => await svc.RemittanceAsync(id) is { } r ? Results.Ok(r) : Results.NotFound()).RequireAuthorization(any);
        g.MapPost("/remittances", async (RemittanceRequest r, ClaimsPrincipal u, ClaimsService svc) =>
        {
            var res = await svc.ImportRemittanceAsync(r.FunderId, r.FileName ?? "remittance.csv", r.Text ?? "", Me(u));
            return res.Remittance is null ? Errs(res.Errors) : Results.Ok(new { res.Remittance.Id, res.Remittance.LineCount, res.Remittance.Matched, res.Remittance.Total });
        }).RequireAuthorization(Permissions.ClaimsManage);

        // Xero
        g.MapGet("/xero/settings", async (XeroService x) =>
        {
            var s = await x.SettingsAsync();
            return Results.Ok(new
            {
                mode = s.Mode.ToString(),
                s.SalesAccountCode,
                s.GstTaxType,
                s.NoGstTaxType,
                s.PaymentAccountCode,
                s.TenantId,
                s.ClientId,
                hasSecret = s.ClientSecretProtected is not null,
                connected = s.RefreshTokenProtected is not null,
                s.LastSyncAt,
                s.LastError
            });
        }).RequireAuthorization(Permissions.AnyOf(Permissions.ClaimsManage, Permissions.ClaimsAdmin));
        g.MapPut("/xero/settings", async (XeroSettingsEdit e, ClaimsPrincipal u, XeroService x) => Err(await x.SaveSettingsAsync(e, Me(u)))).RequireAuthorization(Permissions.ClaimsAdmin);
        g.MapGet("/xero/pending", async (DateOnly from, DateOnly to, XeroService x) =>
            Results.Ok((await x.PendingInvoicesAsync(from, to)).Select(d => new { d.Id, d.Number, d.IssuedOn, payer = d.Payer.ToString(), d.PayerName, total = BrightPAS.Modules.Billing.BillingService.Calculate(d).Total })))
            .RequireAuthorization(Permissions.ClaimsManage);
        g.MapGet("/xero/exports", async (XeroService x) => Results.Ok((await x.ExportsAsync()).Select(e => new { e.Id, mode = e.Mode.ToString(), e.Invoices, e.Payments, e.FileName, e.Errors, e.CreatedAt, e.CreatedBy })))
            .RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/xero/export", async (XeroExportRequest r, ClaimsPrincipal u, XeroService x, SecureExportService export) =>
        {
            var run = await x.ExportAsync(r.From, r.To, Me(u));
            if (run.File is not null) return await export.ExportAsync(r.Export, Path.GetFileNameWithoutExtension(run.Export.FileName!), [new(run.Export.FileName!, run.File)], Me(u), "XeroInvoices");
            if (run.Export.Invoices + run.Export.Payments == 0 && run.Errors.Count > 0) return Errs(run.Errors);
            return Results.Ok(new { run.Export.Invoices, run.Export.Payments, errors = run.Errors, byHand = run.ByHand ?? [] });
        }).RequireAuthorization(Permissions.ClaimsManage);
        g.MapPost("/xero/exports/{id:guid}/download", async (Guid id, CodingEndpoints.DownloadRequest r, ClaimsPrincipal u, XeroService x, SecureExportService export) =>
        {
            var (e, file) = await x.ExportFileAsync(id);
            return e is null ? Results.NotFound() : await export.ExportAsync(r.Export, Path.GetFileNameWithoutExtension(e.FileName!), [new(e.FileName!, file!)], Me(u), "XeroInvoices");
        }).RequireAuthorization(Permissions.ClaimsManage);

        // EFTPOS terminal
        var pay = Permissions.AnyOf(Permissions.ClaimsManage, Permissions.PaymentsTake);
        g.MapGet("/terminal/invoice/{invoiceId:guid}", async (Guid invoiceId, TerminalService t) => Results.Ok((await t.ForInvoiceAsync(invoiceId)).Select(TxnView))).RequireAuthorization(pay);
        g.MapPost("/terminal", async (TerminalStart r, ClaimsPrincipal u, TerminalService t) =>
        {
            var (txn, error) = await t.StartAsync(r.InvoiceId, r.Amount, Me(u));
            return txn is null ? Err(error) : Results.Ok(new { txn = TxnView(txn), error });
        }).RequireAuthorization(Permissions.PaymentsTake);
        g.MapPost("/terminal/{id:guid}/complete", async (Guid id, TerminalResult r, ClaimsPrincipal u, TerminalService t) => Err(await t.CompleteAsync(id, r, Me(u)))).RequireAuthorization(Permissions.PaymentsTake);
        g.MapPost("/terminal/{id:guid}/check", async (Guid id, ClaimsPrincipal u, TerminalService t) =>
        {
            var (status, error) = await t.CheckAsync(id, Me(u));
            return status is null ? Err(error) : Results.Ok(new { status, error });
        }).RequireAuthorization(Permissions.PaymentsTake);
        g.MapPost("/terminal/{id:guid}/refund", async (Guid id, TerminalRefundRequest r, ClaimsPrincipal u, TerminalService t) =>
        {
            var (txn, error) = await t.RefundAsync(id, r.Amount, r.Reason, Me(u));
            return txn is null ? Err(error) : Results.Ok(new { txn = TxnView(txn), error });
        }).RequireAuthorization(Permissions.OnlinePayRefund);
        g.MapPost("/terminal/{id:guid}/cancel", async (Guid id, ClaimsPrincipal u, TerminalService t) => Err(await t.CancelAsync(id, Me(u)))).RequireAuthorization(Permissions.PaymentsTake);
        g.MapGet("/terminal/settlement", async (DateOnly date, TerminalService t) =>
        {
            var s = await t.SettlementAsync(date);
            return Results.Ok(new { s.Date, s.Approved, s.Total, s.Declined, s.Pending, s.ByCard, s.Refunds, s.RefundTotal, transactions = s.Transactions.Select(TxnView) });
        }).RequireAuthorization(pay);
        g.MapPut("/terminal/settings", async (TerminalSettingsRequest r, ClaimsPrincipal u, TerminalService t) => Err(await t.SaveSettingsAsync(new TerminalSettingsEdit(r.Provider, r.TerminalId, r.WindcaveUrl, r.WindcaveUser, r.WindcaveKey), Me(u)))).RequireAuthorization(Permissions.ClaimsAdmin);

        // NHI / HPI
        var id = app.MapGroup("/api/identity").RequireModule(ClaimsModule.Id);
        id.MapGet("/settings", async (HealthIdentityService s) =>
        {
            var x = await s.SettingsAsync();
            return Results.Ok(new { mode = x.Mode.ToString(), x.NhiBaseUrl, x.HpiBaseUrl, x.TokenUrl, x.ClientId, hasSecret = x.ClientSecretProtected is not null, x.LastOkAt, x.LastError });
        }).RequireAuthorization(Permissions.ClaimsAdmin); // security test 0.26.0: lookup users don't need the connection settings
        id.MapPut("/settings", async (IdentitySettingsEdit e, ClaimsPrincipal u, HealthIdentityService s) => Err(await s.SaveSettingsAsync(e, Me(u)))).RequireAuthorization(Permissions.ClaimsAdmin);
        id.MapGet("/nhi/{patientId:guid}", async (Guid patientId, ClaimsPrincipal u, HealthIdentityService s) => Results.Ok(await s.CompareAsync(patientId, Me(u))))
            .RequireAuthorization(Permissions.IdentityLookup).RequireRateLimiting("lookups");
        id.MapPost("/nhi/{patientId:guid}/accept", async (Guid patientId, AcceptRequest r, ClaimsPrincipal u, HealthIdentityService s) =>
            Errs(r.Fields is null || r.Fields.Count == 0 ? ["Choose the details to update."] : await s.AcceptAsync(patientId, r.Fields, Me(u))))
            .RequireAuthorization(Permissions.PatientEdit);
        id.MapGet("/hpi/practitioners", async (string? q, ClaimsPrincipal u, HealthIdentityService s) =>
        {
            var (rows, error) = await s.PractitionersAsync(q ?? "", Me(u));
            return Results.Ok(new { rows, error });
        }).RequireAuthorization(Permissions.IdentityLookup).RequireRateLimiting("lookups");
        id.MapGet("/hpi/facilities", async (string? q, ClaimsPrincipal u, HealthIdentityService s) =>
        {
            var (rows, error) = await s.FacilitiesAsync(q ?? "", Me(u));
            return Results.Ok(new { rows, error });
        }).RequireAuthorization(Permissions.AnyOf(Permissions.IdentityLookup, Permissions.LettersTemplates)).RequireRateLimiting("lookups");
        id.MapPost("/hpi/facilities/add", async (HpiFacility f, ClaimsPrincipal u, HealthIdentityService s) => Errs(await s.AddPracticeAsync(f, Me(u))))
            .RequireAuthorization(Permissions.LettersTemplates);
        id.MapPost("/practices/import", async (ImportRequest r, ClaimsPrincipal u, HealthIdentityService s) =>
        {
            var (added, updated, errors) = await s.ImportPracticesAsync(r.Text ?? "", Me(u));
            return Results.Ok(new { added, updated, errors });
        }).RequireAuthorization(Permissions.LettersTemplates);
    }
}
