using System.Security.Claims;
using System.Text;
using BrightPAS.Identity;
using BrightPAS.Modules.Forms;
using BrightPAS.Web.Integration;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Form designer, form import and filling in forms (spec 7.23, ADR 0038).</summary>
public static class FormDesignerEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    public sealed record CreateRequest(string Code, string Name, FormCategory Category);
    public sealed record ImportRequest(string Code, string Name, FormCategory Category, string Kind, string? Text, string? Base64);
    public sealed record PublishRequest(string? Note);
    public sealed record StartRequest(Guid VersionId, Guid? PatientId, Guid? BookingId);
    public sealed record SaveRequest(Dictionary<string, string>? Answers, bool Complete);
    public sealed record VoidRequest(string? Reason);

    private static object Field(FormField f) => new
    {
        f.Key,
        f.Sequence,
        f.Section,
        f.Label,
        type = f.Type.ToString(),
        f.Required,
        f.Min,
        f.Max,
        f.ShowIfKey,
        f.ShowIfValue,
        f.Calculation,
        f.MapsTo,
        f.Help,
        options = f.Options.OrderBy(o => o.Sequence).Select(o => new { o.Value, o.Score })
    };

    public static void MapFormDesignerEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/forms/designer").RequireModule(FormsModule.Id);
        var any = Permissions.AnyOf(Permissions.FormsDesign, Permissions.FormsFill, Permissions.FormsApprove);

        g.MapGet("/meta", (ClaimsPrincipal u) => Results.Ok(new
        {
            types = Enum.GetNames<FieldType>(),
            categories = Enum.GetNames<FormCategory>(),
            dataElements = FormLogic.DataElements.Select(kv => new { key = kv.Key, label = kv.Value }),
            canDesign = u.HasClaim(Permissions.ClaimType, Permissions.FormsDesign),
            canApprove = u.HasClaim(Permissions.ClaimType, Permissions.FormsApprove),
            canFill = u.HasClaim(Permissions.ClaimType, Permissions.FormsFill)
        })).RequireAuthorization(any);
        g.MapGet("/forms", async (FormDesignerService svc) => Results.Ok((await svc.DesignsAsync()).Select(d => new
        {
            d.Id,
            d.Code,
            d.Name,
            category = d.Category.ToString(),
            versions = d.Versions.OrderByDescending(v => v.Version).Select(v => new { v.Id, v.Version, status = v.Status.ToString(), source = v.Source.ToString(), v.PublishedAt, v.ApprovedBy, v.UpdatedAt, v.UpdatedBy })
        }))).RequireAuthorization(any);
        g.MapGet("/versions/{id:guid}", async (Guid id, FormDesignerService svc) =>
        {
            var v = await svc.VersionAsync(id);
            if (v is null) return Results.NotFound();
            var d = (await svc.DesignsAsync()).Single(x => x.Id == v.DesignId);
            return Results.Ok(new { v.Id, v.DesignId, d.Code, d.Name, category = d.Category.ToString(), v.Version, status = v.Status.ToString(), source = v.Source.ToString(), v.Header, v.Footer, v.ThirdParty, v.LicenceNote, v.ApprovedBy, v.ApprovedAt, v.ApprovalNote, v.PublishedAt, fields = v.Fields.OrderBy(f => f.Sequence).Select(Field) });
        }).RequireAuthorization(any);
        g.MapPost("/forms", async (CreateRequest r, ClaimsPrincipal u, FormDesignerService svc) =>
        {
            var (v, errors) = await svc.CreateAsync(r.Code, r.Name, r.Category, DesignSource.Designer, [new FieldEdit(null, null, "First question", FieldType.Text, false, null, null, null, null, null, null, null, null)], Me(u));
            return v is null ? Results.BadRequest(new { errors }) : Results.Ok(new { v.Id });
        }).RequireAuthorization(Permissions.FormsDesign);
        g.MapPost("/import", async (ImportRequest r, ClaimsPrincipal u, FormDesignerService svc) =>
        {
            List<FieldEdit> fields; List<string> errors; var name = r.Name; DesignSource source;
            switch (r.Kind)
            {
                case "csv": (fields, errors) = FormDesignerService.ParseCsv(r.Text ?? ""); source = DesignSource.Csv; break;
                case "fhir": { var (n, f, e) = FormDesignerService.ParseFhir(r.Text ?? ""); fields = f; errors = e; if (string.IsNullOrWhiteSpace(name)) name = n; source = DesignSource.Fhir; break; }
                case "pdf":
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(r.Base64 ?? ""); } catch (FormatException) { return Results.BadRequest(new { errors = new[] { "The file could not be read." } }); }
                    if (bytes.Length > 20 * 1024 * 1024) return Results.BadRequest(new { errors = new[] { "The PDF is larger than 20 MB." } });
                    (fields, errors) = PdfFormImport.Read(bytes); source = DesignSource.Pdf; break;
                default: return Results.BadRequest(new { errors = new[] { "Choose CSV, FHIR Questionnaire or fillable PDF." } });
            }
            if (errors.Count > 0 || fields.Count == 0) return Results.BadRequest(new { errors = errors.Count > 0 ? errors : ["Nothing to import."] });
            var (v, err2) = await svc.CreateAsync(r.Code, name, r.Category, source, fields, Me(u));
            return v is null ? Results.BadRequest(new { errors = err2 }) : Results.Ok(new { v.Id, fields = fields.Count });
        }).RequireAuthorization(Permissions.FormsDesign);
        g.MapPut("/versions/{id:guid}", async (Guid id, VersionEdit e, ClaimsPrincipal u, FormDesignerService svc) =>
            await svc.SaveAsync(id, e, Me(u)) is { Count: > 0 } errors ? Results.BadRequest(new { errors }) : Results.Ok()).RequireAuthorization(Permissions.FormsDesign);
        g.MapPost("/forms/{id:guid}/new-version", async (Guid id, ClaimsPrincipal u, FormDesignerService svc) =>
        {
            var (v, error) = await svc.NewVersionAsync(id, Me(u));
            return v is null ? Results.BadRequest(new { errors = new[] { error } }) : Results.Ok(new { v.Id });
        }).RequireAuthorization(Permissions.FormsDesign);
        g.MapPost("/versions/{id:guid}/test", async (Guid id, ClaimsPrincipal u, FormDesignerService svc) => await svc.TestAsync(id, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.FormsDesign);
        g.MapPost("/versions/{id:guid}/publish", async (Guid id, PublishRequest r, ClaimsPrincipal u, FormDesignerService svc) =>
            await svc.PublishAsync(id, u.HasClaim(Permissions.ClaimType, Permissions.FormsApprove), r.Note, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok())
            .RequireAuthorization(Permissions.AnyOf(Permissions.FormsDesign, Permissions.FormsApprove));
        g.MapPost("/versions/{id:guid}/retire", async (Guid id, ClaimsPrincipal u, FormDesignerService svc) => await svc.RetireAsync(id, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.FormsDesign);
        g.MapDelete("/versions/{id:guid}", async (Guid id, ClaimsPrincipal u, FormDesignerService svc) => await svc.DeleteDraftAsync(id, Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.FormsDesign);
        g.MapGet("/versions/{id:guid}/fhir", async (Guid id, FormDesignerService svc) =>
        {
            var v = await svc.VersionAsync(id);
            if (v is null) return Results.NotFound();
            var d = (await svc.DesignsAsync()).Single(x => x.Id == v.DesignId);
            return Results.File(Encoding.UTF8.GetBytes(FormDesignerService.ToFhir(d, v)), "application/fhir+json", $"{d.Code}-v{v.Version}.json");
        }).RequireAuthorization(any);

        // ---- entries
        g.MapPost("/entries", async (StartRequest r, ClaimsPrincipal u, FormDesignerService svc) =>
        {
            var (e, error) = await svc.StartAsync(r.VersionId, r.PatientId, r.BookingId, Me(u));
            return e is null ? Results.BadRequest(new { errors = new[] { error } }) : Results.Ok(new { e.Id });
        }).RequireAuthorization(Permissions.FormsFill);
        g.MapGet("/entries", async (Guid? patientId, Guid? versionId, FormDesignerService svc) =>
            Results.Ok((await svc.EntriesForAsync(patientId, versionId)).Select(x => new { x.Entry.Id, x.Entry.VersionId, x.Entry.PatientId, x.Code, x.Name, x.Version, status = x.Entry.Status.ToString(), x.Entry.StartedBy, x.Entry.StartedAt, x.Entry.CompletedBy, x.Entry.CompletedAt })))
            .RequireAuthorization(any);
        g.MapGet("/entries/{id:guid}", async (Guid id, ClaimsPrincipal u, FormDesignerService svc) =>
        {
            var e = await svc.EntryAsync(id, Me(u));
            if (e is null) return Results.NotFound();
            return Results.Ok(new { e.Id, e.VersionId, e.PatientId, e.BookingId, status = e.Status.ToString(), e.StartedBy, e.StartedAt, e.CompletedBy, e.CompletedAt, e.VoidReason, values = e.Values.ToDictionary(v => v.FieldKey, v => v.Value, StringComparer.Ordinal) });
        }).RequireAuthorization(any);
        g.MapPut("/entries/{id:guid}", async (Guid id, SaveRequest r, ClaimsPrincipal u, FormDesignerService svc) =>
        {
            var (errors, applied) = await svc.SaveEntryAsync(id, r.Answers ?? [], r.Complete, Me(u));
            return errors.Count > 0 ? Results.BadRequest(new { errors }) : Results.Ok(new { applied });
        }).RequireAuthorization(Permissions.FormsFill);
        g.MapPost("/entries/{id:guid}/void", async (Guid id, VoidRequest r, ClaimsPrincipal u, FormDesignerService svc) => await svc.VoidEntryAsync(id, r.Reason ?? "", Me(u)) is { } e ? Results.BadRequest(new { errors = new[] { e } }) : Results.Ok()).RequireAuthorization(Permissions.FormsFill);
    }
}
