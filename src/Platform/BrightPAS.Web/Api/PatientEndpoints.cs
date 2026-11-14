using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Web.Api;

/// <summary>Patient API: search, register, view and update demographics, alerts and merge – every read and change is audit-logged.</summary>
public static class PatientEndpoints
{
    public sealed record AlertRequest(AlertType Type, AlertSeverity Severity, string Description);

    public static void MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/patients");

        g.MapGet("/", async (string? q, DateOnly? dob, PatientService svc) =>
            Results.Ok((await svc.SearchAsync(q ?? string.Empty, dob)).Select(Summary)))
            .RequireAuthorization(Permissions.PatientView);

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, HttpContext http, PatientService svc) =>
        {
            var p = await svc.GetAsync(id, user.Identity!.Name!, AuthEndpoints.Workstation(http));
            return p is null ? Results.NotFound() : Results.Ok(Detail(p));
        }).RequireAuthorization(Permissions.PatientView);

        g.MapPost("/", async (PatientRegistration req, ClaimsPrincipal user, PatientService svc) =>
        {
            var r = await svc.RegisterAsync(req, user.Identity!.Name!);
            return r.Succeeded
                ? Results.Created($"/api/patients/{r.Patient!.Id}", Detail(r.Patient))
                : Results.BadRequest(new { errors = r.Errors, possibleDuplicates = r.PossibleDuplicates });
        }).RequireAuthorization(Permissions.PatientRegister);

        g.MapPut("/{id:guid}/contact", async (Guid id, ContactUpdate req, ClaimsPrincipal user, PatientService svc) =>
        {
            var errors = await svc.UpdateContactAsync(id, req, user.Identity!.Name!);
            return errors.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors });
        }).RequireAuthorization(Permissions.PatientEdit);

        g.MapPost("/{id:guid}/alerts", async (Guid id, AlertRequest req, ClaimsPrincipal user, PatientService svc) =>
        {
            try
            {
                var a = await svc.AddAlertAsync(id, req.Type, req.Severity, req.Description, user.Identity!.Name!);
                return Results.Ok(a);
            }
            catch (KeyNotFoundException) { return Results.NotFound(); }
            catch (ArgumentException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
        }).RequireAuthorization(Permissions.PatientAlertsManage);
    }

    private static object Summary(Patient p) => new
    {
        p.Id,
        p.Mrn,
        p.Nhi,
        p.FamilyName,
        p.GivenNames,
        p.PreferredName,
        p.DateOfBirth,
        sex = p.Sex.ToString(),
        p.IsTestPatient
    };

    private static object Detail(Patient p) => new
    {
        p.Id,
        p.Mrn,
        p.Nhi,
        p.FamilyName,
        p.GivenNames,
        p.PreferredName,
        p.DateOfBirth,
        sex = p.Sex.ToString(),
        p.Gender,
        p.MobilePhone,
        p.HomePhone,
        p.Email,
        p.AddressLine1,
        p.AddressLine2,
        p.Suburb,
        p.City,
        p.Postcode,
        p.GpPracticeHpiFacilityId,
        p.IsTestPatient,
        ethnicities = p.Ethnicities.OrderBy(e => e.Order).Select(e => e.Code),
        alerts = p.Alerts.Where(a => a.IsActive).OrderByDescending(a => a.Severity).ThenByDescending(a => a.RecordedAt)
            .Select(a => new { a.Id, type = a.Type.ToString(), severity = a.Severity.ToString(), a.Description, a.RecordedBy, a.RecordedAt })
    };
}
