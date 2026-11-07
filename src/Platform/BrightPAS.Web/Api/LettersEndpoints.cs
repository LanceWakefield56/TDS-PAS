using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Letters;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Letters & documents: letters, templates, GP practices and HealthLink.</summary>
public static class LettersEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static string? DisplayName(ClaimsPrincipal u) => u.FindFirstValue(PasClaimsFactory.DisplayNameClaim) ?? u.Identity?.Name;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record ReasonRequest(string Reason);

    public static void MapLettersEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/letters").RequireModule(LettersModule.Id);
        g.MapGet("/", async (string? view, Guid? patientId, LetterService svc) => Results.Ok(await svc.ListAsync(view ?? "recent", patientId)))
            .RequireAuthorization(Permissions.LettersView);
        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, LetterService svc) =>
        {
            var l = await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            if (l is null) return Results.NotFound();
            var practices = await svc.PracticesAsync();
            var practice = practices.FirstOrDefault(p => p.Id == l.GpPracticeId);
            return Results.Ok(new
            {
                l.Id, l.Number, l.PatientId, kind = l.Kind.ToString(), recipient = l.Recipient.ToString(), l.RecipientName, l.RecipientAddress, l.Subject, l.Body,
                status = l.Status.ToString(), channel = l.Channel.ToString(), l.PrintCount, l.MessageControlId, l.AckCode, l.AckText, l.AckAt, l.VoidReason,
                l.CreatedAt, l.CreatedBy, l.Author, l.FinalisedAt, l.FinalisedBy, l.SentAt, l.SentBy, l.BookingId, l.ReferralId,
                practice = practice is null ? null : new { practice.Name, practice.HealthLinkEdi, channel = practice.PreferredChannel.ToString() }
            });
        }).RequireAuthorization(Permissions.LettersView);
        g.MapPost("/", async (NewLetter n, ClaimsPrincipal u, LetterService svc) =>
        {
            var (l, errors) = await svc.CreateDraftAsync(n, Me(u), DisplayName(u));
            return l is null ? Errors(errors) : Results.Ok(new { l.Id, l.Number });
        }).RequireAuthorization(Permissions.LettersWrite);
        g.MapPut("/{id:guid}", async (Guid id, DraftEdit d, ClaimsPrincipal u, LetterService svc) => Done(await svc.UpdateDraftAsync(id, d, Me(u))))
            .RequireAuthorization(Permissions.LettersWrite);
        g.MapPost("/{id:guid}/finalise", async (Guid id, ClaimsPrincipal u, LetterService svc) => Done(await svc.FinaliseAsync(id, Me(u))))
            .RequireAuthorization(Permissions.LettersWrite);
        g.MapPost("/{id:guid}/printed", async (Guid id, ClaimsPrincipal u, LetterService svc) => Done(await svc.RecordPrintAsync(id, Me(u))))
            .RequireAuthorization(Permissions.LettersWrite);
        g.MapPost("/{id:guid}/send", async (Guid id, ClaimsPrincipal u, HealthLinkService hl) =>
        {
            var (ok, message) = await hl.SendAsync(id, Me(u));
            return ok ? Results.Ok(new { message }) : Results.BadRequest(new { errors = new[] { message } });
        }).RequireAuthorization(Permissions.LettersWrite);
        g.MapPost("/{id:guid}/void", async (Guid id, ReasonRequest r, ClaimsPrincipal u, LetterService svc) => Done(await svc.VoidAsync(id, r.Reason, Me(u))))
            .RequireAuthorization(Permissions.LettersWrite);
        // Clinic details for the printed letterhead.
        g.MapGet("/letterhead", async (BrightPAS.Core.Organisation.OrganisationService org) =>
        {
            var m = await org.MainSiteAsync();
            return m is not { } x ? Results.NotFound() : Results.Ok(new { organisationName = x.Org.Name, x.Site.AddressLine1, x.Site.AddressLine2, x.Site.Suburb, x.Site.City, x.Site.Postcode, x.Site.Phone, x.Site.Email });
        }).RequireAuthorization(Permissions.LettersView);
        g.MapGet("/templates", async (LetterService svc) => Results.Ok(new { templates = await svc.TemplatesAsync(activeOnly: false), placeholders = LetterService.Placeholders }))
            .RequireAuthorization(Permissions.LettersView);
        g.MapPut("/templates", async (TemplateEdit t, ClaimsPrincipal u, LetterService svc) => Errors(await svc.SaveTemplateAsync(t, Me(u))))
            .RequireAuthorization(Permissions.LettersTemplates);
        g.MapGet("/practices", async (LetterService svc) => Results.Ok(await svc.PracticesAsync())).RequireAuthorization(Permissions.LettersView);
        // Same lists for the template administrator (who does not need access to letters themselves).
        g.MapGet("/admin/templates", async (LetterService svc) => Results.Ok(new { templates = await svc.TemplatesAsync(activeOnly: false), placeholders = LetterService.Placeholders }))
            .RequireAuthorization(Permissions.LettersTemplates);
        g.MapGet("/admin/practices", async (LetterService svc) => Results.Ok(await svc.PracticesAsync())).RequireAuthorization(Permissions.LettersTemplates);
        g.MapPost("/practices", async (PracticeEdit p, ClaimsPrincipal u, LetterService svc) => Errors(await svc.SavePracticeAsync(p, Me(u))))
            .RequireAuthorization(Permissions.LettersTemplates);
        g.MapGet("/healthlink", async (LetterService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { mode = s.Mode.ToString(), s.SenderEdi, s.OutboxFolder, s.InboxFolder, s.UpdatedAt, s.UpdatedBy, modes = Enum.GetNames<HealthLinkMode>() });
        }).RequireAuthorization(Permissions.LettersTemplates);
        g.MapPut("/healthlink", async (HealthLinkSettingsEdit e, ClaimsPrincipal u, HealthLinkService hl) => Errors(await hl.UpdateSettingsAsync(e, Me(u))))
            .RequireAuthorization(Permissions.LettersTemplates);
    }
}
