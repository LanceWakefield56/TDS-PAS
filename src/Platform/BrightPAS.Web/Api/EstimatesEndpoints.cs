using System.Security.Claims;
using BrightPAS.Identity;
using BrightPAS.Modules.Estimates;
using BrightPAS.Web.Licensing;
using BrightPAS.Web.Security;

namespace BrightPAS.Web.Api;

/// <summary>Procedure estimates (0.21.0, ADR 0034): builder, estimates list, PDF/email/Incisive, outbox, templates, price list admin and imports.</summary>
public static class EstimatesEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static string DisplayName(ClaimsPrincipal u) => u.FindFirst(PasClaimsFactory.DisplayNameClaim)?.Value is { Length: > 0 } n ? n : Me(u);

    private static async Task<IResult> Guard(Func<Task<IResult>> f)
    {
        try { return await f(); }
        catch (KeyNotFoundException ex) { return Results.NotFound(new { errors = new[] { ex.Message } }); }
        catch (InvalidOperationException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
        catch (IOException ex) { return Results.BadRequest(new { errors = new[] { "The file could not be written: " + ex.Message } }); }
        catch (UnauthorizedAccessException) { return Results.BadRequest(new { errors = new[] { "BrightPAS is not allowed to write to that folder – ask IT to give the website's account write access." } }); }
    }

    private static IResult Err(string? e) => e is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { e } });
    private static IResult Errs(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record SaveRequest(EstimateForm Form, bool Issue);
    public sealed record ReasonRequest(string? Reason);
    public sealed record LinkRequest(Guid PatientId);
    public sealed record ExportRequest(ExportOptions? Export);
    public sealed record TemplateSaveRequest(string? Name, string? Description, EstimateForm Form);
    public sealed record FeesRequest(IReadOnlyList<FeeEdit> Fees);
    public sealed record ConsumablesRequest(IReadOnlyList<ConsumableEdit> Lines);

    private static object Calc(CalcResult r, bool costing) => new
    {
        r.Title,
        lines = r.Lines.Select(l => new { l.Label, l.Amount, l.Display, l.Note }),
        additional = r.Additional.Select(l => new { l.Label, l.Amount, l.Display, l.Note }),
        r.Facility,
        r.Surgeon,
        r.Anaesthetist,
        r.Consumables,
        r.ExtendedTime,
        r.AllInAddOns,
        r.Other,
        r.Subtotal,
        r.Deposit,
        r.Balance,
        r.ScheduledMinutes,
        r.PlannedMinutes,
        r.ExtraMinutes,
        r.ValidUntil,
        r.DepositDue,
        r.Warnings,
        costing = costing ? new { r.Revenue, r.Cost, r.Margin, r.CostingComplete } : null
    };

    private static object AddOn(EstAddOn a) => new { a.Id, a.Code, a.Name, a.Fee, type = a.Type.ToString(), a.Group, a.SurgeonId };

    private static bool IsAdmin(ClaimsPrincipal u) => u.HasClaim(Permissions.ClaimType, Permissions.EstimatesManage);

    public static void MapEstimatesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/estimates").RequireModule(EstimatesModule.Id);

        // ------------------------------------------------------------ builder
        g.MapGet("/meta", async (ClaimsPrincipal u, EstimateService svc, EstimateCatalogue cat) =>
        {
            var s = await svc.SettingsAsync();
            var links = await cat.SurgeonSpecialtiesAsync();
            return Results.Ok(new
            {
                specialties = (await cat.SpecialtiesAsync()).Select(x => new { x.Id, x.Name, x.DefaultNotes, x.IsActive }),
                procedures = (await cat.ProceduresAsync()).Select(p => new { p.Id, p.Code, p.SpecialtyId, p.Group, p.Name, p.Minutes, defaultAnaesthetic = p.DefaultAnaesthetic.ToString(), p.IsActive }),
                surgeons = (await cat.SurgeonsAsync()).Select(x => new { x.Id, x.Name, x.IsActive, specialtyIds = links.Where(l => l.SurgeonId == x.Id).Select(l => l.SpecialtyId) }),
                anaesthetics = Anaesthetics.Labels.Select(kv => new { value = kv.Key.ToString(), label = kv.Value, anaesthetist = Anaesthetics.HasAnaesthetist(kv.Key) }),
                settings = new { sendMode = s.SendMode.ToString(), s.HoldMinutes, patientPassword = s.PatientPassword.ToString(), s.InsurerSameAsPatient, incisive = !string.IsNullOrWhiteSpace(s.IncisiveFolder), s.SurgeonFeeLabel },
                preparedBy = DisplayName(u),
                admin = IsAdmin(u)
            });
        }).RequireAuthorization(Permissions.EstimatesAny);

        g.MapPost("/preview", (EstimateForm f, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var (r, available, proc) = await svc.PreviewAsync(f);
            return Results.Ok(new
            {
                result = Calc(r, IsAdmin(u)),
                available = available.Select(AddOn),
                included = proc?.IncludedCodes(),
                procedure = proc is null ? null : new { proc.Id, proc.Code, proc.Name, proc.Minutes, defaultAnaesthetic = proc.DefaultAnaesthetic.ToString(), proc.Facility, proc.Anaesthetist, proc.FacilitySpecial, proc.AnaesthetistSpecial }
            });
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapGet("/", async (string? q, EstimateStatus? status, Guid? surgeonId, DateOnly? from, DateOnly? to, Guid? patientId, EstimateService svc) =>
            Results.Ok(await svc.ListAsync(q, status, surgeonId, from, to, patientId))).RequireAuthorization(Permissions.EstimatesAny);

        g.MapGet("/new", (Guid? patientId, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var f = patientId is null ? new EstimateForm() : await svc.FromPatientAsync(patientId.Value);
            f.EstimateDate ??= BrightPAS.Core.NzTime.Today(TimeProvider.System);
            f.PreparedBy = DisplayName(u);
            return Results.Ok(f);
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapGet("/{id:guid}", (Guid id, HttpContext http, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var e = await svc.GetAsync(id, Me(u), AuthEndpoints.Workstation(http));
            if (e is null) return Results.NotFound();
            return Results.Ok(new
            {
                estimate = new
                {
                    e.Id,
                    e.Ref,
                    status = e.Status.ToString(),
                    e.EstimateDate,
                    e.Title,
                    e.Subtotal,
                    e.Deposit,
                    e.Balance,
                    e.ValidUntil,
                    e.CreatedBy,
                    e.CreatedAt,
                    e.UpdatedBy,
                    e.UpdatedAt,
                    e.IssuedAt,
                    e.EmailedAt,
                    e.SentToIncisiveAt,
                    e.CancelReason,
                    e.ProcedureCode,
                    e.ProcedureName,
                    e.SpecialtyName,
                    e.SurgeonName,
                    e.PatientId,
                    lines = e.Lines.Select(l => new { kind = l.Kind.ToString(), l.Label, l.Amount, l.DisplayText, l.Note })
                },
                form = EstimateService.FormOf(e),
                archive = (await svc.ArchiveListAsync(id)).Select(a => new { a.Id, a.CreatedAt, a.CreatedBy, a.Reason, a.FileName })
            });
        })).RequireAuthorization(Permissions.EstimatesAny);

        g.MapPost("/", (SaveRequest r, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var res = await svc.SaveAsync(null, r.Form, r.Issue, Me(u));
            return res.Estimate is null ? Errs(res.Errors) : Results.Ok(new { res.Estimate.Id, res.Estimate.Ref, status = res.Estimate.Status.ToString(), res.ArchiveMessage });
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapPut("/{id:guid}", (Guid id, SaveRequest r, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var res = await svc.SaveAsync(id, r.Form, r.Issue, Me(u));
            return res.Estimate is null ? Errs(res.Errors) : Results.Ok(new { res.Estimate.Id, res.Estimate.Ref, status = res.Estimate.Status.ToString(), res.ArchiveMessage, res.StoppedEmails });
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapPost("/{id:guid}/duplicate", (Guid id, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var res = await svc.DuplicateAsync(id, Me(u));
            return res.Estimate is null ? Errs(res.Errors) : Results.Ok(new { res.Estimate.Id, res.Estimate.Ref });
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapPost("/{id:guid}/cancel", async (Guid id, ReasonRequest r, ClaimsPrincipal u, EstimateService svc) => Err(await svc.CancelAsync(id, r.Reason, Me(u))))
            .RequireAuthorization(Permissions.EstimatesBuild);
        g.MapPost("/{id:guid}/link-patient", async (Guid id, LinkRequest r, ClaimsPrincipal u, EstimateService svc) => Err(await svc.LinkPatientAsync(id, r.PatientId, Me(u))))
            .RequireAuthorization(Permissions.EstimatesBuild);

        // PDF (every download offers password protection – ADR 0018/0020) and archived copies.
        g.MapPost("/{id:guid}/pdf", (Guid id, ExportRequest r, ClaimsPrincipal u, EstimateService svc, SecureExportService export) => Guard(async () =>
        {
            var e = await svc.FindAsync(id);
            var s = await svc.SettingsAsync();
            return await export.ExportPdfAsync((r.Export ?? new(true, null)) with { Format = "pdf" }, Path.GetFileNameWithoutExtension(EstimateService.PdfFileName(e)),
                pw => svc.Pdf(e, s, pw), Me(u), "EstimatePdf", e.PatientId?.ToString());
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapPost("/archive/{archiveId:guid}", (Guid archiveId, ExportRequest r, ClaimsPrincipal u, EstimateService svc, SecureExportService export) => Guard(async () =>
        {
            var (pdf, name, pid) = await svc.ArchivedPdfAsync(archiveId);
            return await export.ExportAsync(r.Export, Path.GetFileNameWithoutExtension(name), [new(name, pdf)], Me(u), "EstimateArchivedPdf", pid?.ToString());
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapPost("/{id:guid}/incisive", (Guid id, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var path = await svc.SendToIncisiveAsync(id, Me(u));
            return Results.Ok(new { file = Path.GetFileName(path) });
        })).RequireAuthorization(Permissions.EstimatesBuild);

        // ------------------------------------------------------------ email
        g.MapGet("/{id:guid}/email-check", (Guid id, Recipient recipient, string? to, EstimateService svc, EstimateMail mail) => Guard(async () =>
        {
            var s = await svc.SettingsAsync();
            var e = await svc.FindAsync(id);
            var suggested = to ?? (recipient == Recipient.Insurer ? EstimateMail.InsurerAddress(s, e) : recipient == Recipient.Patient ? e.Email : null);
            var checks = await mail.ChecksAsync(id, recipient, suggested);
            var rule = EstimateMail.DefaultPassword(s, recipient);
            return Results.Ok(new
            {
                to = suggested,
                checks = checks.Select(c => new { level = c.Level.ToString(), c.Message }),
                password = rule.ToString(),
                hint = EstimateMail.Hint(s, rule),
                sendMode = s.SendMode.ToString(),
                s.HoldMinutes,
                insurers = EstimateMail.InsurerList(s).Where(x => x.Address.Length > 0).Select(x => new { name = x.Name, address = x.Address }),
                e.Ref,
                hasDob = e.DateOfBirth is not null,
                hasNhi = e.Nhi is { Length: 7 }
            });
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapPost("/{id:guid}/email/draft", (Guid id, SendRequest r, ClaimsPrincipal u, EstimateMail mail) => Guard(async () =>
        {
            var (eml, name) = await mail.DraftAsync(id, r, Me(u));
            return Results.File(eml, "message/rfc822", name);
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapPost("/{id:guid}/email/send", (Guid id, SendRequest r, ClaimsPrincipal u, EstimateMail mail) => Guard(async () =>
        {
            var o = await mail.QueueAsync(id, r, Me(u));
            return Results.Ok(new { o.Id, o.SendAfter, o.ToAddress });
        })).RequireAuthorization(Permissions.EstimatesBuild);

        g.MapGet("/outbox", async (bool? all, EstimateMail mail) => Results.Ok((await mail.OutboxAsync(all == true)).Select(o => new
        {
            o.Id,
            o.EstimateId,
            o.EstimateRef,
            o.PatientName,
            recipient = o.Recipient.ToString(),
            o.ToAddress,
            o.Subject,
            o.PasswordProtected,
            status = o.Status.ToString(),
            o.CreatedAt,
            o.CreatedBy,
            o.SendAfter,
            o.SentAt,
            o.StoppedAt,
            o.StoppedBy,
            o.Error,
            o.Attempts
        }))).RequireAuthorization(Permissions.EstimatesBuild);
        g.MapPost("/outbox/{id:guid}/stop", async (Guid id, ClaimsPrincipal u, EstimateMail mail) => Err(await mail.StopAsync(id, Me(u)))).RequireAuthorization(Permissions.EstimatesBuild);
        g.MapPost("/outbox/{id:guid}/send-now", async (Guid id, ClaimsPrincipal u, EstimateMail mail) => Err(await mail.SendNowAsync(id, Me(u)))).RequireAuthorization(Permissions.EstimatesBuild);

        // ------------------------------------------------------------ templates
        g.MapGet("/templates", async (bool? all, Guid? surgeonId, ClaimsPrincipal u, EstimateService svc) =>
            Results.Ok(await svc.TemplatesAsync(!(all == true && IsAdmin(u)), surgeonId))).RequireAuthorization(Permissions.EstimatesAny);
        g.MapPost("/templates/{id:guid}/apply", (Guid id, EstimateForm f, EstimateService svc) => Guard(async () => Results.Ok(await svc.ApplyTemplateAsync(id, f))))
            .RequireAuthorization(Permissions.EstimatesBuild);
        g.MapPost("/templates", (TemplateSaveRequest r, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var (id, err) = await svc.SaveAsTemplateAsync(null, r.Name, r.Description, r.Form, Me(u));
            return err is null ? Results.Ok(new { id }) : Err(err);
        })).RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/templates/{id:guid}/from-estimate", (Guid id, TemplateSaveRequest r, ClaimsPrincipal u, EstimateService svc) => Guard(async () =>
        {
            var (_, err) = await svc.SaveAsTemplateAsync(id, r.Name, r.Description, r.Form, Me(u));
            return Err(err);
        })).RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/templates/{id:guid}", async (Guid id, EstimateService.TemplateProps p, ClaimsPrincipal u, EstimateService svc) => Err(await svc.UpdateTemplateAsync(id, p, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapDelete("/templates/{id:guid}", async (Guid id, ClaimsPrincipal u, EstimateService svc) => Err(await svc.DeleteTemplateAsync(id, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);

        // ------------------------------------------------------------ admin: price list
        g.MapGet("/admin/catalogue", async (EstimateCatalogue cat) =>
        {
            var links = await cat.SurgeonSpecialtiesAsync();
            var fees = await cat.FeesAsync(null);
            return Results.Ok(new
            {
                specialties = await cat.SpecialtiesAsync(),
                procedures = (await cat.ProceduresAsync()).Select(p => new
                {
                    p.Id,
                    p.Code,
                    p.SpecialtyId,
                    p.Group,
                    p.Name,
                    p.PrintTitle,
                    p.Minutes,
                    defaultAnaesthetic = p.DefaultAnaesthetic.ToString(),
                    p.Facility,
                    p.FacilitySpecial,
                    p.Anaesthetist,
                    p.AnaesthetistSpecial,
                    p.ConsumablesLine,
                    p.IncludedAddOnCodes,
                    p.EffectiveFrom,
                    p.IsActive,
                    p.Source,
                    allSurgeonsFee = fees.Find(f => f.ProcedureId == p.Id && f.SurgeonId == null)?.Fee,
                    feeRows = fees.Count(f => f.ProcedureId == p.Id)
                }),
                surgeons = (await cat.SurgeonsAsync()).Select(x => new { x.Id, x.Name, x.ClinicianId, x.IsActive, specialtyIds = links.Where(l => l.SurgeonId == x.Id).Select(l => l.SpecialtyId) }),
                addOns = (await cat.AddOnsAsync()).Select(a => new { a.Id, a.Code, a.SpecialtyId, a.Group, a.Name, a.Fee, type = a.Type.ToString(), a.SurgeonId, a.Source, a.IsActive }),
                pending = await cat.PriceImpactAsync()
            });
        }).RequireAuthorization(Permissions.EstimatesManage);

        g.MapGet("/admin/procedures/{id:guid}", async (Guid id, EstimateCatalogue cat) =>
        {
            var p = (await cat.ProceduresAsync()).Find(x => x.Id == id);
            if (p is null) return Results.NotFound();
            return Results.Ok(new
            {
                procedure = p,
                fees = await cat.FeesAsync(id),
                consumables = await cat.ConsumablesAsync(id),
                usage = await cat.ProcedureUsageAsync(id)
            });
        }).RequireAuthorization(Permissions.EstimatesManage);

        g.MapPost("/admin/specialties", async (SpecialtyEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => { var (id, err) = await cat.SaveSpecialtyAsync(null, e, Me(u)); return err is null ? Results.Ok(new { id }) : Err(err); })
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/admin/specialties/{id:guid}", async (Guid id, SpecialtyEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => Err((await cat.SaveSpecialtyAsync(id, e, Me(u))).Error))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapDelete("/admin/specialties/{id:guid}", async (Guid id, ClaimsPrincipal u, EstimateCatalogue cat) => Err(await cat.DeleteSpecialtyAsync(id, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);

        g.MapPost("/admin/procedures", async (ProcedureEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => { var (id, errs) = await cat.SaveProcedureAsync(null, e, Me(u)); return errs.Count == 0 ? Results.Ok(new { id }) : Errs(errs); })
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/admin/procedures/{id:guid}", async (Guid id, ProcedureEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => Errs((await cat.SaveProcedureAsync(id, e, Me(u))).Errors))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/admin/procedures/{id:guid}/fees", async (Guid id, FeesRequest r, ClaimsPrincipal u, EstimateCatalogue cat) => Err(await cat.SaveFeesAsync(id, r.Fees, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/admin/procedures/{id:guid}/consumables", async (Guid id, ConsumablesRequest r, ClaimsPrincipal u, EstimateCatalogue cat) => Err(await cat.SaveConsumablesAsync(id, r.Lines, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapDelete("/admin/procedures/{id:guid}", async (Guid id, ClaimsPrincipal u, EstimateCatalogue cat) => Err(await cat.DeleteProcedureAsync(id, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);

        g.MapPost("/admin/surgeons", async (SurgeonEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => { var (id, err) = await cat.SaveSurgeonAsync(null, e, Me(u)); return err is null ? Results.Ok(new { id }) : Err(err); })
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/admin/surgeons/{id:guid}", async (Guid id, SurgeonEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => Err((await cat.SaveSurgeonAsync(id, e, Me(u))).Error))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapDelete("/admin/surgeons/{id:guid}", async (Guid id, ClaimsPrincipal u, EstimateCatalogue cat) => Err(await cat.DeleteSurgeonAsync(id, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);

        g.MapPost("/admin/addons", async (AddOnEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => { var (id, errs) = await cat.SaveAddOnAsync(null, e, Me(u)); return errs.Count == 0 ? Results.Ok(new { id }) : Errs(errs); })
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/admin/addons/{id:guid}", async (Guid id, AddOnEdit e, ClaimsPrincipal u, EstimateCatalogue cat) => Errs((await cat.SaveAddOnAsync(id, e, Me(u))).Errors))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapDelete("/admin/addons/{id:guid}", async (Guid id, ClaimsPrincipal u, EstimateCatalogue cat) => Err(await cat.DeleteAddOnAsync(id, Me(u))))
            .RequireAuthorization(Permissions.EstimatesManage);

        g.MapGet("/admin/stock", async (EstimateCatalogue cat, EstimateService svc) =>
        {
            var s = await svc.SettingsAsync();
            return Results.Ok((await cat.StockAsync()).Select(i => new { i.Id, i.Supplier, i.Reference, i.Name, i.PackPrice, i.UnitsPerPack, i.UnitPrice, chargeOut = i.UnitPrice is null ? (decimal?)null : s.ChargeOut(i.UnitPrice.Value), i.InStockQty, i.NeededInHouse, i.WhereUsed, i.Issues }));
        }).RequireAuthorization(Permissions.EstimatesManage);

        g.MapGet("/admin/settings", async (EstimateService svc) => Results.Ok(await svc.SettingsAsync())).RequireAuthorization(Permissions.EstimatesManage);
        g.MapPut("/admin/settings", async (EstSettings s, ClaimsPrincipal u, EstimateCatalogue cat) => Errs(await cat.SaveSettingsAsync(s, Me(u)))).RequireAuthorization(Permissions.EstimatesManage);

        // ------------------------------------------------------------ imports (raw .xlsx body, name in X-File-Name) and downloads
        g.MapPost("/admin/import/pricing", (HttpRequest req, bool? apply, bool? fees, bool? addOns, bool? retire, bool? acceptWarnings, ClaimsPrincipal u, EstimateImport imp) => Guard(async () =>
        {
            using var ms = await Body(req);
            var pv = await imp.PreviewPricingAsync(ms, FileName(req));
            if (apply == true && pv.Errors.Count == 0 && (pv.Warnings.Count == 0 || acceptWarnings == true))
                return Results.Ok(new { applied = await imp.ApplyPricingAsync(pv, fees != false, addOns != false, retire == true, Me(u)) });
            if (apply == true && pv.Warnings.Count > 0 && acceptWarnings != true && pv.Errors.Count == 0)
                return Results.BadRequest(new { errors = new[] { "Tick to confirm the warnings before importing." } });
            return Results.Ok(new
            {
                pv.FileName,
                pv.Errors,
                pv.Warnings,
                pv.New,
                pv.Changed,
                pv.Unchanged,
                pv.NotInFile,
                fees = pv.Fees.Count,
                addOns = pv.AddOns.Count,
                procedures = pv.Procedures.Where(p => p.Status != ChangeStatus.Unchanged).Select(p => new { p.Code, p.Name, status = p.Status.ToString(), p.Scheduled, effectiveFrom = p.Row?.EffectiveFrom, p.Diffs })
            });
        })).RequireAuthorization(Permissions.EstimatesManage);

        g.MapPost("/admin/import/stock", (HttpRequest req, bool? apply, ClaimsPrincipal u, EstimateImport imp) => Guard(async () =>
        {
            using var ms = await Body(req);
            var pv = await imp.PreviewStockAsync(ms, FileName(req));
            if (apply == true && pv.Errors.Count == 0) return Results.Ok(new { applied = await imp.ApplyStockAsync(pv, Me(u)) });
            return Results.Ok(new { pv.FileName, pv.Errors, items = pv.Items.Count, pv.Suppliers, pv.New, pv.Updated, issues = pv.Items.Where(i => i.Issues is not null).Take(200).Select(i => new { i.Supplier, i.Name, i.Reference, i.Issues }) });
        })).RequireAuthorization(Permissions.EstimatesManage);

        g.MapPost("/admin/files/pricing", (ExportRequest r, ClaimsPrincipal u, EstimateImport imp, SecureExportService export) => Guard(async () =>
            await export.ExportAsync(r.Export, "BrightPAS-estimates-pricing", [new("Estimates pricing.xlsx", await imp.ExportPricingAsync())], Me(u), "EstimatesPricing")))
            .RequireAuthorization(Permissions.EstimatesManage);
        g.MapPost("/admin/files/stock", (ExportRequest r, ClaimsPrincipal u, EstimateImport imp, SecureExportService export) => Guard(async () =>
            await export.ExportAsync(r.Export, "BrightPAS-estimates-stock", [new("Estimates stock.xlsx", await imp.ExportStockAsync())], Me(u), "EstimatesStock")))
            .RequireAuthorization(Permissions.EstimatesManage);
    }

    private static string FileName(HttpRequest req) => Path.GetFileName(req.Headers["X-File-Name"].ToString()) is { Length: > 0 } n ? n : "upload.xlsx";

    private static async Task<MemoryStream> Body(HttpRequest req)
    {
        var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await req.Body.ReadAsync(buf)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > EstimateImport.MaxFileBytes) { await ms.DisposeAsync(); throw new InvalidOperationException("The file is larger than 20 MB."); }
        }
        if (ms.Length == 0) { await ms.DisposeAsync(); throw new InvalidOperationException("The file is empty."); }
        ms.Position = 0;
        return ms;
    }
}
