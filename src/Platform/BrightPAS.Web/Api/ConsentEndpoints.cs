using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Identity;
using BrightPAS.Modules.Consent;
using BrightPAS.Modules.Patients;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>E-consent and patient education (0.29.0, ADR 0042): information sheets, consents per booking, and the patient's own secure link.</summary>
public static class ConsentEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record SheetRequest(string? Code, string? Name, string? ProcedureWords, string? About, string? Benefits, string? Alternatives, string? NoTreatment,
        string? Anaesthetic, string? Preparation, string? Recovery, IReadOnlyList<RiskEdit>? Risks, IReadOnlyList<LinkEdit>? Links);
    public sealed record ApproveRequest(string? Note);
    public sealed record PrepareRequest(Guid BookingId, Guid SheetId, string? ExtraRisks, string? NoteToPatient);
    public sealed record DraftRequest(string? ExtraRisks, string? NoteToPatient);
    public sealed record TextRequest(string? Text);
    public sealed record ReasonRequest(string? Reason);
    public sealed record SettingsRequest(string? PublicUrl, int LinkDays, bool RequireBeforeConfirm, int PrepareDaysAhead);
    public sealed record DobRequest(DateOnly DateOfBirth);
    public sealed record ReadRequest(DateOnly DateOfBirth, string? Section);
    public sealed record AskRequest(DateOnly DateOfBirth, string? Text);
    public sealed record SignRequest(DateOnly DateOfBirth, SignerKind Signer, string? SignerName, string? Relationship, string? Interpreter, string? Signature,
        bool Understood, bool HadChanceToAsk, bool Agree);
    public sealed record WithdrawRequest(DateOnly DateOfBirth, string? Reason);

    private static SheetEdit Edit(SheetRequest r) => new(r.Code ?? "", r.Name ?? "", r.ProcedureWords, r.About ?? "", r.Benefits ?? "", r.Alternatives ?? "", r.NoTreatment ?? "",
        r.Anaesthetic, r.Preparation, r.Recovery, r.Risks ?? [], r.Links ?? []);

    private static object SheetView(ConsentSheet s) => new
    {
        s.Id,
        s.Code,
        s.Version,
        status = s.Status.ToString(),
        s.Name,
        s.ProcedureWords,
        s.About,
        s.Benefits,
        s.Alternatives,
        s.NoTreatment,
        s.Anaesthetic,
        s.Preparation,
        s.Recovery,
        risks = s.Risks.OrderBy(r => r.Order).Select(r => new { r.Text, frequency = r.Frequency.ToString(), r.Serious }),
        links = s.Links.OrderBy(l => l.Order).Select(l => new { l.Title, l.Url }),
        s.CreatedBy,
        s.CreatedAt,
        s.UpdatedBy,
        s.UpdatedAt,
        s.ApprovedBy,
        s.ApprovedAt,
        s.ApprovalNote,
        s.RetiredAt,
    };

    private static object ConsentView(ConsentRecord c, Patient? p, DateOnly? surgery, DateOnly today) => new
    {
        c.Id,
        c.Number,
        c.PatientId,
        patient = p is null ? null : $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}",
        nhi = p?.Nhi,
        c.BookingId,
        surgeryDate = surgery,
        status = c.Status.ToString(),
        method = c.Method.ToString(),
        c.Procedure,
        side = ConsentService.SideText(c.Laterality),
        c.ClinicianName,
        sheet = ConsentService.ReadSnapshot(c),
        sections = ConsentService.Sections(c).Select(s => s.Key),
        sectionsRead = (c.SectionsRead ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
        c.ExtraRisks,
        c.NoteToPatient,
        c.LinkSentAt,
        c.LinkExpiresAt,
        c.OpenedAt,
        locked = c.LockedUntil > DateTimeOffset.UtcNow,
        signer = c.Signer?.ToString(),
        c.SignerName,
        c.SignerRelationship,
        c.Interpreter,
        hasSignature = c.SignatureImage is not null,
        c.SignedAt,
        c.SignedBy,
        c.DocumentId,
        c.ConfirmedAt,
        c.ConfirmedBy,
        c.ConfirmNote,
        c.WithdrawnAt,
        c.WithdrawnBy,
        c.WithdrawnReason,
        c.CreatedAt,
        c.CreatedBy,
        questions = c.Questions.OrderBy(q => q.AskedAt).Select(q => new { q.Id, q.Text, q.AskedAt, q.Answer, q.AnsweredAt, q.AnsweredBy }),
        flags = ConsentService.Flags(c, p?.DateOfBirth ?? default, surgery, today),
    };

    private static async Task<DateOnly?> SurgeryDateAsync(Guid bookingId, BrightPAS.Modules.Theatre.TheatreDbContext theatre) =>
        await (from b in theatre.Bookings.AsNoTracking() join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id where b.Id == bookingId select (DateOnly?)s.Date).FirstOrDefaultAsync();

    public static void MapConsentEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/consent").RequireModule(ConsentModule.Id);

        // ---- information sheets
        g.MapGet("/sheets", async (bool? retired, ConsentSheetService svc) => Results.Ok((await svc.ListAsync(retired == true)).Select(SheetView))).RequireAuthorization(Permissions.ConsentAny);
        g.MapGet("/sheets/{id:guid}", async (Guid id, ConsentSheetService svc) => await svc.GetAsync(id) is { } s ? Results.Ok(SheetView(s)) : Results.NotFound()).RequireAuthorization(Permissions.ConsentAny);
        g.MapPost("/sheets", async (SheetRequest r, ClaimsPrincipal u, ConsentSheetService svc) =>
        {
            var (s, e) = await svc.SaveDraftAsync(null, Edit(r), Me(u));
            return s is null ? Errs(e) : Results.Ok(new { s.Id });
        }).RequireAuthorization(Permissions.ConsentSheets);
        g.MapPut("/sheets/{id:guid}", async (Guid id, SheetRequest r, ClaimsPrincipal u, ConsentSheetService svc) =>
        {
            if (await svc.GetAsync(id) is null) return Results.NotFound();
            var (s, e) = await svc.SaveDraftAsync(id, Edit(r), Me(u));
            return s is null ? Errs(e) : Results.Ok(new { s.Id });
        }).RequireAuthorization(Permissions.ConsentSheets);
        g.MapPost("/sheets/examples", async (ClaimsPrincipal u, ConsentSheetService svc) => Results.Ok(new { added = await svc.SeedExamplesAsync(Me(u)) })).RequireAuthorization(Permissions.ConsentSheets);
        g.MapPost("/sheets/{id:guid}/new-version", async (Guid id, ClaimsPrincipal u, ConsentSheetService svc) =>
        {
            var (s, e) = await svc.NewVersionAsync(id, Me(u));
            return s is null ? Err(e) : Results.Ok(new { s.Id });
        }).RequireAuthorization(Permissions.ConsentSheets);
        g.MapPost("/sheets/{id:guid}/approve", async (Guid id, ApproveRequest r, ClaimsPrincipal u, ConsentSheetService svc) => Err(await svc.ApproveAsync(id, r.Note, Me(u))))
            .RequireAuthorization(Permissions.ConsentApprove);
        g.MapPost("/sheets/{id:guid}/retire", async (Guid id, ClaimsPrincipal u, ConsentSheetService svc) => Err(await svc.RetireAsync(id, Me(u)))).RequireAuthorization(Permissions.ConsentSheetsAny);
        g.MapGet("/sheets/suggest", async (string? procedure, ConsentSheetService svc) =>
            await svc.SuggestAsync(procedure ?? "") is { } s ? Results.Ok(new { s.Id, s.Name, s.Version }) : Results.Ok(new { id = (Guid?)null })).RequireAuthorization(Permissions.ConsentAny);

        // ---- consents
        g.MapGet("/", async (string? view, Guid? patientId, ConsentService svc) => Results.Ok(await svc.ListAsync(view ?? "sent", patientId))).RequireAuthorization(Permissions.ConsentAny);
        g.MapGet("/prepare", async (ConsentService svc) => Results.Ok(await svc.ToPrepareAsync())).RequireAuthorization(Permissions.ConsentAny);
        g.MapGet("/count", async (ConsentService svc) => Results.Ok(new { questions = await svc.OpenQuestionsAsync() })).RequireAuthorization(Permissions.ConsentAny);
        g.MapGet("/booking/{bookingId:guid}", async (Guid bookingId, ConsentService svc) =>
            await svc.ForBookingAsync(bookingId) is { } c ? Results.Ok(new { c.Id, c.Number, status = c.Status.ToString() }) : Results.Ok(new { id = (Guid?)null })).RequireAuthorization(Permissions.ConsentAny);
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, ConsentService svc, PatientsDbContext patients, BrightPAS.Modules.Theatre.TheatreDbContext theatre, BrightPAS.Core.Audit.AuditWriter audit, TimeProvider clock) =>
        {
            var c = await svc.GetAsync(id);
            if (c is null) return Results.NotFound();
            var p = await patients.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.PatientId);
            await audit.WriteAsync(Me(u), "View", "Consent", c.Id.ToString(), c.PatientId.ToString());
            return Results.Ok(ConsentView(c, p, await SurgeryDateAsync(c.BookingId, theatre), BrightPAS.Core.NzTime.Today(clock)));
        }).RequireAuthorization(Permissions.ConsentAny);
        g.MapGet("/{id:guid}/pdf", async (Guid id, ClaimsPrincipal u, ConsentService svc, BrightPAS.Core.Audit.AuditWriter audit) =>
        {
            var c = await svc.GetAsync(id);
            if (c is null) return Results.NotFound();
            await audit.WriteAsync(Me(u), "Print", "Consent", c.Id.ToString(), c.PatientId.ToString());
            return Results.File((await svc.PdfAsync(id))!, "application/pdf", $"consent-{c.Number}.pdf");
        }).RequireAuthorization(Permissions.ConsentAny);
        g.MapPost("/", async (PrepareRequest r, ClaimsPrincipal u, ConsentService svc) =>
        {
            var (c, e) = await svc.PrepareAsync(r.BookingId, r.SheetId, r.ExtraRisks, r.NoteToPatient, Me(u));
            return c is null ? Errs(e) : Results.Ok(new { c.Id, c.Number });
        }).RequireAuthorization(Permissions.ConsentManage);
        g.MapPut("/{id:guid}", async (Guid id, DraftRequest r, ClaimsPrincipal u, ConsentService svc) => Err(await svc.EditDraftAsync(id, r.ExtraRisks, r.NoteToPatient, Me(u))))
            .RequireAuthorization(Permissions.ConsentManage);
        g.MapPost("/{id:guid}/send", async (Guid id, ClaimsPrincipal u, ConsentService svc) =>
        {
            var (link, delivered, e) = await svc.SendAsync(id, Me(u));
            return link is null ? Err(e) : Results.Ok(new { link, delivered });
        }).RequireAuthorization(Permissions.ConsentManage);
        g.MapPost("/questions/{questionId:guid}/answer", async (Guid questionId, TextRequest r, ClaimsPrincipal u, ConsentService svc) => Err(await svc.AnswerAsync(questionId, r.Text ?? "", Me(u))))
            .RequireAuthorization(Permissions.ConsentManage);
        g.MapPost("/paper", async (PaperInput r, ClaimsPrincipal u, ConsentService svc) =>
        {
            var (c, e) = await svc.RecordPaperAsync(r, Me(u));
            return c is null ? Errs(e) : Results.Ok(new { c.Id, c.Number });
        }).RequireAuthorization(Permissions.ConsentManage).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(40L * 1024 * 1024));
        g.MapPost("/{id:guid}/confirm", async (Guid id, TextRequest r, ClaimsPrincipal u, ConsentService svc) => Err(await svc.ConfirmAsync(id, r.Text, Me(u))))
            .RequireAuthorization(Permissions.ConsentConfirm);
        g.MapPost("/{id:guid}/withdraw", async (Guid id, ReasonRequest r, ClaimsPrincipal u, ConsentService svc) => Err(await svc.WithdrawAsync(id, r.Reason ?? "", Me(u))))
            .RequireAuthorization(Permissions.ConsentManage);
        g.MapGet("/settings", async (ConsentService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { s.PublicUrl, s.LinkDays, s.RequireBeforeConfirm, s.PrepareDaysAhead, s.UpdatedAt, s.UpdatedBy });
        }).RequireAuthorization(Permissions.ConsentAny);
        g.MapPut("/settings", async (SettingsRequest r, ClaimsPrincipal u, ConsentService svc) => Errs(await svc.SaveSettingsAsync(r.PublicUrl, r.LinkDays, r.RequireBeforeConfirm, r.PrepareDaysAhead, Me(u))))
            .RequireAuthorization(Permissions.ConsentSheetsAny);

        // ---- the patient's own link (no staff sign-in): secret link + date of birth; rate-limited per IP; locks after 5 wrong dates.
        var e = app.MapGroup("/api/e/{token}").RequireModule(ConsentModule.Id).RequireRateLimiting("patient-links").AllowAnonymous();
        e.MapGet("/", async (string token, ConsentService svc) => await svc.PublicStatusAsync(token) is { } st ? Results.Ok(new { status = st }) : Results.NotFound());
        e.MapPost("/open", async (string token, DobRequest r, ConsentService svc) =>
        {
            var (c, error, p) = await svc.VerifyAsync(token, r.DateOfBirth, signedOk: true);
            return c is null ? Err(error) : Results.Ok(await svc.ViewAsync(c, p!));
        });
        e.MapPost("/read", async (string token, ReadRequest r, ConsentService svc) =>
        {
            var (error, read) = await svc.MarkReadAsync(token, r.DateOfBirth, r.Section ?? "");
            return error is null ? Results.Ok(new { read }) : Err(error);
        });
        e.MapPost("/ask", async (string token, AskRequest r, ConsentService svc) => Err(await svc.AskAsync(token, r.DateOfBirth, r.Text ?? "")));
        e.MapPost("/sign", async (string token, SignRequest r, ConsentService svc) =>
            Errs(await svc.SignAsync(token, r.DateOfBirth, new SignInput(r.Signer, r.SignerName, r.Relationship, r.Interpreter, r.Signature, r.Understood, r.HadChanceToAsk, r.Agree))));
        e.MapPost("/withdraw", async (string token, WithdrawRequest r, ConsentService svc) => Err(await svc.PatientWithdrawAsync(token, r.DateOfBirth, r.Reason)));
    }
}
