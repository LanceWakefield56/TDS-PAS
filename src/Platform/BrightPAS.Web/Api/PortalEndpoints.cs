using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Setup;
using BrightPAS.Identity;
using BrightPAS.Modules.Credentialing;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Portal;
using BrightPAS.Modules.Referrals;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Credentialing module, and the specialist/PA and patient portals (0.10.0).</summary>
public static class PortalEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record DelegateRequest(string UserName, Guid ClinicianId);
    public sealed record HandleRequest(PortalRequestStatus Status, string? Response);
    public sealed record PortalDob(DateOnly DateOfBirth);
    public sealed record PortalRequestBody(DateOnly DateOfBirth, PortalRequestKind Kind, Guid? BookingId, string Message);
    public sealed record PortalUpload(DateOnly DateOfBirth, string FileName, string ContentBase64, string? Title);
    public sealed record LinkRequest(bool SendSms = false, bool SendEmail = false);
    public sealed record PortalSettings(bool AutoSendOnBooking);
    public const string AutoSendKey = "portal.autoSendOnBooking";

    /// <summary>Creates a portal link and queues it by SMS/email through the Reminders module. Returns the URL and what was queued.</summary>
    public static async Task<(string? Url, DateTimeOffset ExpiresAt, object? Sent, string? Error)> CreateAndSendAsync(IServiceProvider sp, HttpContext http, Guid patientId, bool sms, bool email, string user)
    {
        var svc = sp.GetRequiredService<PortalService>();
        var (token, expires, error) = await svc.CreateLinkAsync(patientId, user, (await sp.GetRequiredService<SettingsService>().SecurityAsync()).PortalLinkDays);
        if (token is null) return (null, default, null, error);
        var url = $"{http.Request.Scheme}://{http.Request.Host}/my/{token}";
        object? sent = null;
        if ((sms || email) && RemindersEndpoints.Licensed(sp.GetRequiredService<LicenceCache>()))
        {
            var reminders = sp.GetRequiredService<BrightPAS.Modules.Reminders.ReminderService>();
            var msgs = await reminders.QueuePortalLinkAsync(patientId, url, sms, email, user);
            await reminders.SendDueAsync();
            sent = (await reminders.MessagesAsync(null, patientId)).Where(m => msgs.Any(x => x.Id == m.Id)).Select(m => new { m.Channel, m.Status, m.LastError }).ToList();
        }
        return (url, expires, sent, null);
    }

    /// <summary>After a theatre booking: if the administrator switched it on, send the patient a portal link by SMS (once – not when a link is already active).</summary>
    public static async Task AutoSendAfterBookingAsync(IServiceProvider sp, HttpContext http, Guid patientId, string user)
    {
        var licence = sp.GetRequiredService<LicenceCache>().Status;
        if (licence.AccessTo(PortalModule.Id) != BrightPAS.Licensing.ModuleAccess.Full || !RemindersEndpoints.Licensed(sp.GetRequiredService<LicenceCache>())) return;
        if (await sp.GetRequiredService<SettingsService>().GetAsync(AutoSendKey) != "true") return;
        if (await sp.GetRequiredService<PortalService>().HasActiveLinkAsync(patientId)) return;
        try { await CreateAndSendAsync(sp, http, patientId, sms: true, email: false, user); }
        catch (Exception ex) when (ex is not OperationCanceledException) { sp.GetRequiredService<ILoggerFactory>().CreateLogger("Portal").LogWarning(ex, "Automatic portal link failed"); }
    }

    public static void MapPortalEndpoints(this IEndpointRouteBuilder app)
    {
        MapCredentialing(app);
        MapSpecialistPortal(app);
        MapPatientPortal(app);
    }

    // ================================================================== credentialing
    private static void MapCredentialing(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/credentialing").RequireModule(CredentialingModule.Id);
        g.MapGet("/status", async (CredentialingService svc) => Results.Ok(await svc.StatusAsync())).RequireAuthorization(Permissions.CredentialingView);
        g.MapGet("/settings", async (CredentialingService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok(new { requiredTypes = (await svc.RequiredAsync()).Select(t => t.ToString()), s.WarningDays, s.CheckPrivileges,
                types = Enum.GetValues<CredentialType>().Select(t => new { id = t.ToString(), label = CredentialingService.Label(t) }), services = CredentialingService.Services });
        }).RequireAuthorization(Permissions.CredentialingView);
        g.MapPut("/settings", async (CredSettingsEdit e, ClaimsPrincipal u, CredentialingService svc) => Errors(await svc.UpdateSettingsAsync(e, Me(u))))
            .RequireAuthorization(Permissions.CredentialingManage);
        g.MapGet("/clinicians/{id:guid}", async (Guid id, CredentialingService svc) =>
            Results.Ok(new { credentials = await svc.CredentialsAsync(id), privileges = await svc.PrivilegesAsync(id) })).RequireAuthorization(Permissions.CredentialingView);
        g.MapPost("/credentials", async (CredentialEdit e, ClaimsPrincipal u, CredentialingService svc) => Errors(await svc.SaveCredentialAsync(e, Me(u))))
            .RequireAuthorization(Permissions.CredentialingManage);
        g.MapPost("/privileges", async (PrivilegeEdit e, ClaimsPrincipal u, CredentialingService svc) => Errors(await svc.SavePrivilegeAsync(e, Me(u))))
            .RequireAuthorization(Permissions.CredentialingManage);
        g.MapGet("/check", async (DateOnly date, Guid? surgeon, Guid? anaesthetist, string? procedure, CredentialingService svc) =>
            Results.Ok(await svc.CheckCaseAsync(date, surgeon, anaesthetist, procedure))).RequireAuthorization(Permissions.TheatreBook);
    }

    /// <summary>Credential warnings for a theatre booking (empty when the module is not licensed).</summary>
    public static async Task<IReadOnlyList<string>> BookingCredentialWarningsAsync(IServiceProvider sp, Guid sessionId, string procedure)
    {
        if (sp.GetRequiredService<LicenceCache>().Status.AccessTo(CredentialingModule.Id) == BrightPAS.Licensing.ModuleAccess.None) return [];
        var s = await sp.GetRequiredService<TheatreDbContext>().Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId);
        if (s is null) return [];
        return await sp.GetRequiredService<CredentialingService>().CheckCaseAsync(s.Date, s.SurgeonId, s.AnaesthetistId, procedure);
    }

    // ================================================================== specialist / PA portal
    /// <summary>The clinician-register entries this user is (linked by user name) or works for (PA delegation).</summary>
    private static async Task<List<Guid>> MyClinicianIdsAsync(ClaimsPrincipal u, PasDbContext core, PortalService portal)
    {
        var delegated = await portal.DelegatedCliniciansAsync(Me(u));
        var me = Me(u);
        return await core.Clinicians.AsNoTracking().Where(c => c.UserName == me || delegated.Contains(c.Id)).Select(c => c.Id).ToListAsync();
    }

    private static async Task<bool> LinkedAsync(Guid patientId, List<Guid> mine, TheatreDbContext theatre, ReferralsDbContext referrals)
    {
        if (mine.Count == 0) return false;
        return await theatre.Bookings.Where(b => b.PatientId == patientId).Join(theatre.Sessions, b => b.SessionId, s => s.Id, (b, s) => s)
                   .AnyAsync(s => mine.Contains(s.SurgeonId) || (s.AnaesthetistId != null && mine.Contains(s.AnaesthetistId.Value)))
               || await theatre.Appointments.AnyAsync(a => a.PatientId == patientId && mine.Contains(a.ClinicianId))
               || await referrals.Waitlist.AnyAsync(w => w.PatientId == patientId && mine.Contains(w.SpecialistId));
    }

    private static void MapSpecialistPortal(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/portal").RequireModule(PortalModule.Id);
        g.MapGet("/specialist", async (ClaimsPrincipal u, PasDbContext core, PortalService portal, TheatreDbContext theatre, ReferralsDbContext referrals, PatientsDbContext patients,
            TimeProvider clock, AuditWriter audit, HttpContext http) =>
        {
            var mine = await MyClinicianIdsAsync(u, core, portal);
            var names = await core.Clinicians.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
            var rooms = await core.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name);
            string C(Guid? id) => id is { } x ? names.GetValueOrDefault(x, "?") : "";
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var from = today.AddDays(-14); var to = today.AddDays(120);
            var cases = await theatre.Bookings.AsNoTracking().Join(theatre.Sessions, b => b.SessionId, s => s.Id, (b, s) => new { b, s })
                .Where(x => x.s.Date >= from && x.s.Date <= to && !x.s.IsCancelled && (mine.Contains(x.s.SurgeonId) || (x.s.AnaesthetistId != null && mine.Contains(x.s.AnaesthetistId.Value)))).ToListAsync();
            var appts = await theatre.Appointments.AsNoTracking().Where(a => a.Date >= from && a.Date <= to && mine.Contains(a.ClinicianId)).ToListAsync();
            var waits = await referrals.Waitlist.AsNoTracking().Where(w => mine.Contains(w.SpecialistId) && (w.Status == WaitlistStatus.Waiting || w.Status == WaitlistStatus.OnHold || w.Status == WaitlistStatus.Booked)).ToListAsync();
            var ids = cases.Select(c => c.b.PatientId).Concat(appts.Select(a => a.PatientId)).Concat(waits.Select(w => w.PatientId)).Distinct().ToList();
            var people = await patients.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            string N(Guid id) => people.TryGetValue(id, out var p) ? $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames}" : "?";
            await audit.WriteAsync(Me(u), "View", "SpecialistPortal", Me(u), reason: $"{cases.Count} cases, {appts.Count} appointments, {waits.Count} waiting", workstation: AuthEndpoints.Workstation(http));
            return Results.Ok(new
            {
                clinicians = mine.Select(id => names.GetValueOrDefault(id, "?")).ToList(),
                cases = cases.OrderBy(c => c.s.Date).ThenBy(c => c.b.Order).Select(c => new { c.b.Id, c.b.PatientId, patient = N(c.b.PatientId), nhi = people.GetValueOrDefault(c.b.PatientId)?.Nhi,
                    c.s.Date, c.s.StartTime, location = rooms.GetValueOrDefault(c.s.LocationId, "?"), surgeon = C(c.s.SurgeonId), anaesthetist = c.s.AnaesthetistId is null ? null : C(c.s.AnaesthetistId), c.b.Procedure, status = c.b.Status.ToString(), c.b.IsTentative, c.b.Order }),
                appointments = appts.OrderBy(a => a.Date).ThenBy(a => a.StartTime).Select(a => new { a.Id, a.PatientId, patient = N(a.PatientId), a.Date, a.StartTime, kind = a.Kind.ToString(),
                    clinician = C(a.ClinicianId), status = a.Status.ToString() }),
                waitlist = waits.OrderBy(w => w.TargetDate).Select(w => new { w.Id, w.PatientId, patient = N(w.PatientId), specialist = C(w.SpecialistId), w.Procedure, priority = w.Priority.ToString(), w.AddedOn, w.TargetDate,
                    status = w.Status.ToString(), w.BookedFor, overdue = w.Status == WaitlistStatus.Waiting && w.TargetDate < today }),
            });
        }).RequireAuthorization(Permissions.PortalSpecialist);
        g.MapGet("/specialist/patients/{id:guid}", async (Guid id, ClaimsPrincipal u, HttpContext http, PasDbContext core, PortalService portal, TheatreDbContext theatre,
            ReferralsDbContext referrals, PatientService patients, DocumentService docs) =>
        {
            if (!await LinkedAsync(id, await MyClinicianIdsAsync(u, core, portal), theatre, referrals)) return Results.Json(new { errors = new[] { "This patient is not one of your patients at TDS." } }, statusCode: 403);
            var p = await patients.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            if (p is null) return Results.NotFound();
            return Results.Ok(new
            {
                p.Id, p.Mrn, p.Nhi, p.FamilyName, p.GivenNames, p.PreferredName, p.DateOfBirth, sex = p.Sex.ToString(), p.MobilePhone,
                alerts = p.Alerts.Where(a => a.IsActive).Select(a => new { type = a.Type.ToString(), severity = a.Severity.ToString(), a.Description }),
                documents = await docs.ListAsync(id),
            });
        }).RequireAuthorization(Permissions.PortalSpecialist);
        g.MapGet("/specialist/documents/{docId:guid}", async (Guid docId, ClaimsPrincipal u, HttpContext http, PasDbContext core, PortalService portal, TheatreDbContext theatre,
            ReferralsDbContext referrals, PatientsDbContext pdb, DocumentService docs) =>
        {
            var meta = await pdb.Documents.AsNoTracking().Where(d => d.Id == docId && !d.IsDeleted).Select(d => new { d.PatientId }).SingleOrDefaultAsync();
            if (meta is null) return Results.NotFound();
            if (!await LinkedAsync(meta.PatientId, await MyClinicianIdsAsync(u, core, portal), theatre, referrals)) return Results.Json(new { errors = new[] { "Not your patient." } }, statusCode: 403);
            var r = await docs.OpenAsync(docId, Me(u), AuthEndpoints.Workstation(http));
            if (r is not { } d) return Results.NotFound();
            http.Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; object-src 'self'";
            return Results.File(d.Bytes, d.Meta.ContentType, d.Meta.FileName);
        }).RequireAuthorization(Permissions.PortalSpecialist);

        // Staff side of the patient portal.
        g.MapGet("/patients/{id:guid}/link", async (Guid id, PortalService svc) => Results.Ok(await svc.LinkStatusAsync(id))).RequireAuthorization(Permissions.PortalManage);
        g.MapPost("/patients/{id:guid}/link", async (Guid id, LinkRequest? r, ClaimsPrincipal u, HttpContext http, IServiceProvider sp) =>
        {
            var (url, expires, sent, error) = await CreateAndSendAsync(sp, http, id, r?.SendSms == true, r?.SendEmail == true, Me(u));
            return url is null ? Results.BadRequest(new { errors = new[] { error } }) : Results.Ok(new { path = new Uri(url).PathAndQuery, expiresAt = expires, sent });
        }).RequireAuthorization(Permissions.PortalManage);
        g.MapGet("/settings", async (SettingsService s, LicenceCache licence) =>
            Results.Ok(new { autoSendOnBooking = await s.GetAsync(AutoSendKey) == "true", remindersLicensed = RemindersEndpoints.Licensed(licence) })).RequireAuthorization(Permissions.UsersManage);
        g.MapPut("/settings", async (PortalSettings r, ClaimsPrincipal u, SettingsService s) => { await s.SetAsync(AutoSendKey, r.AutoSendOnBooking ? "true" : "false", Me(u)); return Results.Ok(); })
            .RequireAuthorization(Permissions.UsersManage);
        g.MapPost("/patients/{id:guid}/revoke", async (Guid id, ClaimsPrincipal u, PortalService svc) => Done(await svc.RevokeAsync(id, Me(u)))).RequireAuthorization(Permissions.PortalManage);
        g.MapGet("/requests", async (bool? all, PortalService svc) => Results.Ok(await svc.RequestsAsync(all != true))).RequireAuthorization(Permissions.PortalManage);
        g.MapPost("/requests/{id:guid}/handle", async (Guid id, HandleRequest r, ClaimsPrincipal u, PortalService svc) => Done(await svc.HandleRequestAsync(id, r.Status, r.Response, Me(u))))
            .RequireAuthorization(Permissions.PortalManage);
        // PA ↔ specialist links (administrator).
        g.MapGet("/delegates", async (PortalService svc, PasDbContext core) =>
        {
            var names = await core.Clinicians.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
            return Results.Ok((await svc.DelegatesAsync()).Select(d => new { d.Id, d.UserName, d.ClinicianId, clinician = names.GetValueOrDefault(d.ClinicianId, "?"), d.CreatedAt, d.CreatedBy }));
        }).RequireAuthorization(Permissions.UsersManage);
        g.MapPost("/delegates", async (DelegateRequest r, ClaimsPrincipal u, PortalService svc, PasDbContext core) =>
            !await core.Clinicians.AnyAsync(c => c.Id == r.ClinicianId) ? Results.BadRequest(new { errors = new[] { "Choose the specialist." } }) : Done(await svc.AddDelegateAsync(r.UserName, r.ClinicianId, Me(u))))
            .RequireAuthorization(Permissions.UsersManage);
        g.MapPost("/delegates/remove", async (DelegateRequest r, ClaimsPrincipal u, PortalService svc) =>
            await svc.RemoveDelegatesAsync(r.UserName, r.ClinicianId, Me(u)) > 0 ? Results.Ok() : Results.BadRequest(new { errors = new[] { "Link not found." } }))
            .RequireAuthorization(Permissions.UsersManage);
    }

    // ================================================================== patient portal (public: secret link + date of birth)
    private static IResult OpenError(PortalOpenError e) => e switch
    {
        PortalOpenError.Expired => Results.BadRequest(new { errors = new[] { "This link has expired. Please phone Tasman Day Surgery for a new one." } }),
        PortalOpenError.Locked => Results.BadRequest(new { errors = new[] { "Too many attempts. Please try again in 30 minutes, or phone us." } }),
        PortalOpenError.WrongDetails => Results.BadRequest(new { errors = new[] { "The date of birth does not match our records." } }),
        _ => Results.NotFound(),
    };

    private static void MapPatientPortal(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/my/{token}").RequireModule(PortalModule.Id).RequireRateLimiting("patient-portal").AllowAnonymous();
        g.MapPost("/open", async (string token, PortalDob r, HttpContext http, PortalService svc, TheatreDbContext theatre, BrightPAS.Core.Organisation.OrganisationService org, TimeProvider clock, IServiceProvider sp) =>
        {
            var (access, p, error) = await svc.OpenAsync(token, r.DateOfBirth, AuthEndpoints.Workstation(http));
            if (access is null || p is null) return OpenError(error);
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var cases = await theatre.Bookings.AsNoTracking().Where(b => b.PatientId == p.Id && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.DidNotAttend)
                .Join(theatre.Sessions, b => b.SessionId, s => s.Id, (b, s) => new { b, s }).Where(x => x.s.Date >= today && !x.s.IsCancelled).ToListAsync();
            var appts = await theatre.Appointments.AsNoTracking().Where(a => a.PatientId == p.Id && a.Date >= today && a.Status != AppointmentStatus.Cancelled).ToListAsync();
            var people = await org.ClinicianNamesAsync();
            var rooms = await org.LocationNamesAsync();
            return Results.Ok(new
            {
                givenName = p.PreferredName ?? p.GivenNames.Split(' ')[0],
                procedures = cases.OrderBy(c => c.s.Date).Select(c => new { c.b.Id, c.s.Date, listStarts = c.s.StartTime, location = rooms.GetValueOrDefault(c.s.LocationId, ""), c.b.Procedure, surgeon = people.GetValueOrDefault(c.s.SurgeonId, ""), confirmed = !c.b.IsTentative }),
                appointments = appts.OrderBy(a => a.Date).ThenBy(a => a.StartTime).Select(a => new { a.Id, a.Date, a.StartTime, kind = a.Kind.ToString(), clinician = people.GetValueOrDefault(a.ClinicianId, "") }),
                requests = (await svc.PatientRequestsAsync(p.Id)).Select(x => new { x.Id, kind = x.Kind.ToString(), x.Message, status = x.Status.ToString(), x.CreatedAt, x.Response }),
                contact = new { mobile = Mask(p.MobilePhone), email = Mask(p.Email) },
                questionnaire = await QuestionnaireStateAsync(sp, p.Id, cases.Count > 0),
                estimate = await EstimateAsync(sp, p.Id),
            });
        });
        // Health questionnaire from the portal: a fresh questionnaire link for the next procedure (the Forms module must be licensed).
        g.MapPost("/questionnaire", async (string token, PortalDob r, HttpContext http, PortalService svc, TheatreDbContext theatre, TimeProvider clock, IServiceProvider sp) =>
        {
            var (access, p, error) = await svc.OpenAsync(token, r.DateOfBirth, AuthEndpoints.Workstation(http));
            if (access is null || p is null) return OpenError(error);
            if (sp.GetRequiredService<LicenceCache>().Status.AccessTo(BrightPAS.Modules.Forms.FormsModule.Id) != BrightPAS.Licensing.ModuleAccess.Full)
                return Results.BadRequest(new { errors = new[] { "The online questionnaire is not available – please phone us." } });
            var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var next = await theatre.Bookings.AsNoTracking().Where(b => b.PatientId == p.Id && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.DidNotAttend)
                .Join(theatre.Sessions, b => b.SessionId, s => s.Id, (b, s) => new { b.Id, s.Date }).Where(x => x.Date >= today).OrderBy(x => x.Date).FirstOrDefaultAsync();
            if (await QuestionnaireStateAsync(sp, p.Id, next is not null) is "submitted") return Results.BadRequest(new { errors = new[] { "We already have your questionnaire – thank you." } });
            var (form, formToken, ferror) = await sp.GetRequiredService<BrightPAS.Modules.Forms.FormService>().CreateAsync(p.Id, next?.Id, "patient-portal");
            return form is null ? Results.BadRequest(new { errors = new[] { ferror } }) : Results.Ok(new { path = $"/q/{formToken}" });
        });
        g.MapPost("/estimates/{id:guid}/accept", async (string token, Guid id, PortalDob r, HttpContext http, PortalService svc, IServiceProvider sp) =>
        {
            var (access, p, error) = await svc.OpenAsync(token, r.DateOfBirth, AuthEndpoints.Workstation(http));
            if (access is null || p is null) return OpenError(error);
            var billing = sp.GetRequiredService<BrightPAS.Modules.Billing.BillingDbContext>();
            var est = await billing.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.PatientId == p.Id && d.Kind == BrightPAS.Modules.Billing.DocumentKind.Estimate);
            if (est is null || est.Status != BrightPAS.Modules.Billing.DocumentStatus.Issued) return Results.BadRequest(new { errors = new[] { "This estimate can no longer be accepted online – please phone us." } });
            var e = await sp.GetRequiredService<BrightPAS.Modules.Billing.BillingService>().SetStatusAsync(id, BrightPAS.Modules.Billing.DocumentStatus.Accepted, "Accepted by the patient in the portal", "patient-portal");
            if (e is not null) return Results.BadRequest(new { errors = new[] { e } });
            await svc.AddRequestAsync(access, p.Id, new PortalRequestCreate(PortalRequestKind.EstimateAccepted, est.BookingId, $"Estimate {est.Number} accepted in the portal."));
            return Results.Ok();
        });
        g.MapPost("/requests", async (string token, PortalRequestBody r, HttpContext http, PortalService svc) =>
        {
            var (access, p, error) = await svc.OpenAsync(token, r.DateOfBirth, AuthEndpoints.Workstation(http));
            if (access is null || p is null) return OpenError(error);
            var (_, e) = await svc.AddRequestAsync(access, p.Id, new PortalRequestCreate(r.Kind, r.BookingId, r.Message));
            return e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
        });
        g.MapPost("/documents", async (string token, PortalUpload r, HttpContext http, PortalService svc, DocumentService docs) =>
        {
            var (access, p, error) = await svc.OpenAsync(token, r.DateOfBirth, AuthEndpoints.Workstation(http));
            if (access is null || p is null) return OpenError(error);
            if (!await svc.TryCountUploadAsync(access)) return Results.BadRequest(new { errors = new[] { $"You can upload up to {PortalService.UploadsPerDay} files a day." } });
            var (row, errors) = await docs.AddAsync(new NewPatientDocument(p.Id, null, DocumentCategory.Other, string.IsNullOrWhiteSpace(r.Title) ? "Uploaded by patient" : "Patient: " + r.Title,
                r.FileName, r.ContentBase64, DocumentSource.Portal), "patient-portal", maxMegabytes: 10);
            return row is null ? Results.BadRequest(new { errors }) : Results.Ok();
        });
    }

    /// <summary>"none" (no procedure or Forms not licensed), "needed", "started" or "submitted".</summary>
    private static async Task<string> QuestionnaireStateAsync(IServiceProvider sp, Guid patientId, bool hasProcedure)
    {
        if (!hasProcedure || sp.GetRequiredService<LicenceCache>().Status.AccessTo(BrightPAS.Modules.Forms.FormsModule.Id) == BrightPAS.Licensing.ModuleAccess.None) return "none";
        var forms = sp.GetRequiredService<BrightPAS.Modules.Forms.FormsDbContext>();
        var since = sp.GetRequiredService<TimeProvider>().GetUtcNow().AddDays(-90);
        var latest = (await forms.Requests.AsNoTracking().IgnoreAutoIncludes().Where(f => f.PatientId == patientId && f.CreatedAt > since).Select(f => new { f.Status, f.CreatedAt }).ToListAsync())
            .OrderByDescending(f => f.CreatedAt).FirstOrDefault();
        return latest?.Status switch
        {
            BrightPAS.Modules.Forms.FormStatus.Submitted or BrightPAS.Modules.Forms.FormStatus.Triaged => "submitted",
            BrightPAS.Modules.Forms.FormStatus.Opened => "started",
            _ => "needed",
        };
    }

    /// <summary>The patient's current estimate (issued or accepted, not expired), when the billing module is licensed.</summary>
    private static async Task<object?> EstimateAsync(IServiceProvider sp, Guid patientId)
    {
        if (sp.GetRequiredService<LicenceCache>().Status.AccessTo(BrightPAS.Modules.Billing.BillingModule.Id) == BrightPAS.Licensing.ModuleAccess.None) return null;
        var billing = sp.GetRequiredService<BrightPAS.Modules.Billing.BillingDbContext>();
        var today = DateOnly.FromDateTime(sp.GetRequiredService<TimeProvider>().GetLocalNow().DateTime);
        var d = (await billing.Documents.AsNoTracking().Include(x => x.Lines).Include(x => x.Payments)
                .Where(x => x.PatientId == patientId && x.Kind == BrightPAS.Modules.Billing.DocumentKind.Estimate
                    && (x.Status == BrightPAS.Modules.Billing.DocumentStatus.Issued || x.Status == BrightPAS.Modules.Billing.DocumentStatus.Accepted)).ToListAsync())
            .Where(x => x.DueOn is null || x.DueOn >= today).OrderByDescending(x => x.IssuedOn).FirstOrDefault();
        if (d is null) return null;
        var t = BrightPAS.Modules.Billing.BillingService.Calculate(d);
        return new
        {
            d.Id, d.Number, d.IssuedOn, validUntil = d.DueOn, status = d.Status.ToString(), payer = d.Payer.ToString(), d.PayerName,
            lines = d.Lines.OrderBy(l => l.Sequence).Select(l => new { l.Description, amount = BrightPAS.Modules.Billing.BillingService.Money(l.Quantity * l.UnitPrice) }),
            t.Subtotal, t.Gst, t.Total,
        };
    }

    /// <summary>Shows just enough for the patient to recognise their details (e.g. 021•••••67).</summary>
    public static string? Mask(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Contains('@')) { var at = s.IndexOf('@'); return (at <= 1 ? s[..at] : s[0] + new string('•', at - 1)) + s[at..]; }
        return s.Length <= 5 ? new string('•', s.Length) : s[..3] + new string('•', s.Length - 5) + s[^2..];
    }
}
