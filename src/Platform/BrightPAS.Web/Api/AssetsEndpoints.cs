using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Identity;
using BrightPAS.Modules.Assets;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Theatre;
using BrightPAS.Web.Licensing;

namespace BrightPAS.Web.Api;

/// <summary>Assets &amp; CSSD (0.13.0): equipment and maintenance, CSSD traceability, endoscopes, loan sets, nonconformance, stock, implants, purchasing.</summary>
public static class AssetsEndpoints
{
    private static string Me(ClaimsPrincipal u) => u.Identity!.Name!;
    private static IResult Done(string? error) => error is null ? Results.Ok() : Results.BadRequest(new { errors = new[] { error } });
    private static IResult Errors(IReadOnlyList<string> e) => e.Count == 0 ? Results.Ok() : Results.BadRequest(new { errors = e });

    public sealed record ReasonRequest(string? Reason);
    public sealed record SettingsRequest(int ScopeStorageHours, int MaintenanceWarningDays, bool ReleaseNeedsSecondPerson);
    public sealed record AddTraysRequest(int Count);
    public sealed record BarcodeRequest(string Barcode, bool ContentsChecked = false, string? Notes = null);
    public sealed record IssueRequest(string Barcode, Guid BookingId);
    public sealed record ReleaseRequest(bool Release, string? Note);
    public sealed record IndicatorRequest(IndicatorResult Chemical, IndicatorResult Biological);
    public sealed record ImportRequest(Guid MachineId, CycleKind Kind, string FileName, string Text, IReadOnlyList<string>? TrayBarcodes, IReadOnlyList<CycleItemEdit>? OtherItems,
        IndicatorResult ChemicalIndicator, IndicatorResult BiologicalIndicator, bool BowieDickTest, DateTimeOffset? StartedAt);
    public sealed record ParseRequest(string Text);
    public sealed record LoanAdvance(LoanStatus To, string? Notes, Guid? CycleId);
    public sealed record OrderRequest(Guid SupplierId, IReadOnlyList<OrderLineEdit> Lines, string? Notes);
    public sealed record StatusRequest(PoStatus To);
    public sealed record ReceiveRequest(IReadOnlyList<ReceiptLine> Lines);
    public sealed record AdjustRequest(int Change, bool Expired, string Reason);
    public sealed record XeroRequest(bool MarkExported);

    public static void MapAssetsEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/assets").RequireModule(AssetsModule.Id);

        // ---------------------------------------------------------------- settings and dashboard
        g.MapGet("/settings", async (EquipmentService svc) => Results.Ok(await svc.SettingsAsync())).RequireAuthorization(Permissions.AssetsView);
        g.MapPut("/settings", async (SettingsRequest r, ClaimsPrincipal u, EquipmentService svc) => Errors(await svc.UpdateSettingsAsync(r.ScopeStorageHours, r.MaintenanceWarningDays, r.ReleaseNeedsSecondPerson, Me(u))))
            .RequireAuthorization(Permissions.AssetsManage);
        g.MapGet("/dashboard", async (CssdService cssd, EquipmentService eq, StockService stock) =>
        {
            var due = await eq.DueAsync(30);
            var low = await stock.ItemsAsync(null, true);
            var locked = (await eq.ListAsync(null, null, false)).Where(e => e.Status == "OutOfService").Select(e => new { e.Id, e.AssetNumber, e.Name, e.OutOfServiceReason }).ToList();
            return Results.Ok(new
            {
                cssd = await cssd.DashboardAsync(),
                maintenanceOverdue = due.Count(d => d.State == "Overdue"),
                maintenanceDueSoon = due.Count(d => d.State == "DueSoon"),
                lowStock = low.Count,
                lockedOut = locked,
                due = due.Take(10)
            });
        }).RequireAuthorization(Permissions.AssetsView);

        // ---------------------------------------------------------------- suppliers
        g.MapGet("/suppliers", async (bool? all, EquipmentService svc) => Results.Ok(await svc.SuppliersAsync(all != true))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/suppliers", async (SupplierEdit e, ClaimsPrincipal u, EquipmentService svc) =>
        {
            var (row, errors) = await svc.SaveSupplierAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.AssetsManage);

        // ---------------------------------------------------------------- equipment
        g.MapGet("/equipment", async (string? category, string? q, bool? retired, EquipmentService svc) => Results.Ok(await svc.ListAsync(category, q, retired == true)))
            .RequireAuthorization(Permissions.AssetsView);
        g.MapGet("/equipment/{id:guid}", async (Guid id, EquipmentService svc) =>
            await svc.GetAsync(id) is { } e ? Results.Ok(new { equipment = e, plans = await svc.PlansAsync(id), history = await svc.HistoryAsync(id) }) : Results.NotFound())
            .RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/equipment", async (EquipmentEdit e, ClaimsPrincipal u, EquipmentService svc) =>
        {
            var (row, errors) = await svc.SaveAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.AssetsManage);
        g.MapPost("/equipment/{id:guid}/lockout", async (Guid id, ReasonRequest r, ClaimsPrincipal u, EquipmentService svc) => Done(await svc.LockOutAsync(id, r.Reason ?? "", Me(u))))
            .RequireAuthorization(Permissions.AssetsView); // anyone who finds faulty equipment can take it out of use
        g.MapPost("/equipment/{id:guid}/return", async (Guid id, ReasonRequest r, ClaimsPrincipal u, EquipmentService svc) => Done(await svc.ReturnToServiceAsync(id, r.Reason ?? "", Me(u))))
            .RequireAuthorization(Permissions.AssetsManage);
        g.MapPost("/equipment/{id:guid}/retire", async (Guid id, ReasonRequest r, ClaimsPrincipal u, EquipmentService svc) => Done(await svc.RetireAsync(id, r.Reason ?? "", Me(u))))
            .RequireAuthorization(Permissions.AssetsManage);
        g.MapPost("/equipment/plans", async (PlanEdit e, ClaimsPrincipal u, EquipmentService svc) => Errors(await svc.SavePlanAsync(e, Me(u)))).RequireAuthorization(Permissions.AssetsManage);
        g.MapPost("/equipment/maintenance", async (MaintenanceEdit e, ClaimsPrincipal u, EquipmentService svc) =>
        {
            var (row, errors) = await svc.RecordAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id, lockedOut = !row.Passed && e.Kind is MaintenanceKind.TestAndTag or MaintenanceKind.BiomedicalSafety or MaintenanceKind.Calibration });
        }).RequireAuthorization(Permissions.AssetsManage);
        g.MapGet("/maintenance/due", async (int? days, EquipmentService svc) => Results.Ok(await svc.DueAsync(Math.Clamp(days ?? 60, 0, 730)))).RequireAuthorization(Permissions.AssetsView);

        // ---------------------------------------------------------------- tray catalogue and trays
        g.MapGet("/tray-sets", async (bool? all, CssdService svc) => Results.Ok((await svc.TraySetsAsync(all != true)).Select(t => new
        { t.Id, t.Code, t.Name, t.Specialty, t.ShelfLifeDays, t.IsActive, items = t.Items.Select(i => new { i.Description, i.Quantity }) }))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/tray-sets", async (TraySetEdit e, ClaimsPrincipal u, CssdService svc) =>
        {
            var (row, errors) = await svc.SaveTraySetAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.AssetsManage);
        g.MapPost("/tray-sets/{id:guid}/trays", async (Guid id, AddTraysRequest r, ClaimsPrincipal u, CssdService svc) =>
        {
            var (trays, error) = await svc.AddTraysAsync(id, r.Count, Me(u));
            return error is null ? Results.Ok(trays.Select(t => new { t.Id, t.Barcode })) : Done(error);
        }).RequireAuthorization(Permissions.AssetsManage);
        g.MapGet("/trays", async (string? status, string? q, CssdService svc) => Results.Ok(await svc.TraysAsync(status, q))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/trays/return", async (BarcodeRequest r, ClaimsPrincipal u, CssdService svc) => Done(await svc.ReturnDirtyAsync(r.Barcode, Me(u)))).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/trays/pack", async (BarcodeRequest r, ClaimsPrincipal u, CssdService svc) => Done(await svc.PackAsync(r.Barcode, r.ContentsChecked, r.Notes, Me(u)))).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/trays/issue", async (IssueRequest r, ClaimsPrincipal u, CssdService svc, TheatreDbContext theatre) =>
        {
            var booking = await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == r.BookingId);
            if (booking is null) return Done("Choose the theatre case.");
            var (issue, error) = await svc.IssueAsync(r.Barcode, booking.Id, booking.PatientId, Me(u));
            return error is null ? Results.Ok(issue) : Done(error);
        }).RequireAuthorization(Permissions.CssdProcess);
        g.MapGet("/trays/{id:guid}/label", async (Guid id, CssdService svc, PatientsDbContext patients) =>
        {
            var printer = await DefaultZebraAsync(patients);
            var zpl = await svc.TrayLabelZplAsync(id, printer?.Dpi ?? 203, printer?.WidthMm ?? 57, printer?.LengthMm ?? 32);
            return zpl is null ? Results.NotFound() : Results.File(Encoding.UTF8.GetBytes(zpl), "application/octet-stream", "tray-label.zpl");
        }).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/trays/{id:guid}/print", async (Guid id, ClaimsPrincipal u, CssdService svc, PatientsDbContext patients, BrightPAS.Core.Audit.AuditWriter audit) =>
        {
            var printer = await DefaultZebraAsync(patients);
            if (printer is null) return Done("No Zebra label printer is set up as the default label printer (Security & printers).");
            var zpl = await svc.TrayLabelZplAsync(id, printer.Dpi, printer.WidthMm, printer.LengthMm);
            if (zpl is null) return Results.NotFound();
            var error = await SendZplAsync(printer.Host, printer.Port, zpl);
            if (error is null) await audit.WriteAsync(Me(u), "Print", "TrayLabel", id.ToString(), reason: printer.Name);
            return Done(error is null ? null : $"Could not reach the printer '{printer.Name}' ({printer.Host}:{printer.Port}).");
        }).RequireAuthorization(Permissions.CssdProcess);

        // Today's theatre cases, for issuing trays and recording scope use (CSSD staff do not need the whole theatre module).
        g.MapGet("/cases", async (DateOnly? date, TheatreService theatre, TimeProvider clock) =>
        {
            var d = date ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            return Results.Ok((await theatre.SessionsAsync(d, d)).Where(s => !s.IsCancelled).SelectMany(s => s.Bookings.Where(b => b.Status != "Cancelled" && b.Status != "DidNotAttend")
                .Select(b => new { bookingId = b.Id, b.PatientId, b.PatientName, b.Nhi, b.Mrn, b.Procedure, s.LocationName, start = b.PlannedStart, s.Surgeon, b.TraysNeeded, b.ScopeId })));
        }).RequireAuthorization(Permissions.CssdProcess);

        // ---------------------------------------------------------------- cycles (loads)
        g.MapGet("/cycles", async (string? view, DateOnly? from, DateOnly? to, CssdService svc) => Results.Ok(await svc.CyclesAsync(view, from, to))).RequireAuthorization(Permissions.AssetsView);
        g.MapGet("/cycles/{id:guid}", async (Guid id, CssdService svc) =>
        {
            var c = await svc.CycleAsync(id);
            if (c is null) return Results.NotFound();
            var trays = (await svc.TraysAsync(null, null)).Where(t => c.Items.Any(i => i.TrayInstanceId == t.Id)).Select(t => new { t.Barcode, t.Name, t.Status });
            return Results.Ok(new { cycle = (await svc.CyclesAsync(null, null, null)).FirstOrDefault(x => x.Id == id) ?? null, trays, others = c.Items.Where(i => i.TrayInstanceId == null).Select(i => new { i.Description, i.Quantity }), c.RawLog });
        }).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/cycles", async (CycleEdit e, ClaimsPrincipal u, CssdService svc) =>
        {
            var (row, errors) = await svc.CreateCycleAsync(e, "Manual", null, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id, row.LoadNumber });
        }).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/cycles/parse", (ParseRequest r) => Results.Ok(CssdService.ParseMachineLog(r.Text ?? ""))).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/cycles/import", async (ImportRequest r, ClaimsPrincipal u, CssdService svc) =>
        {
            if (string.IsNullOrWhiteSpace(r.Text) || r.Text.Length > 200_000) return Done("Choose the machine's log file (up to 200 KB of text).");
            var p = CssdService.ParseMachineLog(r.Text);
            var start = p.StartedAt ?? r.StartedAt;
            if (start is null) return Done("The start time was not found in the log – enter it.");
            var edit = new CycleEdit(r.MachineId, r.Kind, p.MachineCycleNumber, p.Program, start.Value, p.EndedAt, p.MaxTemperatureC, p.HoldSeconds, p.PressureKpa, p.A0, p.Passed == true,
                r.ChemicalIndicator, r.BiologicalIndicator, r.BowieDickTest, r.TrayBarcodes, r.OtherItems);
            var (row, errors) = await svc.CreateCycleAsync(edit, r.FileName, r.Text, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id, row.LoadNumber, parsed = p });
        }).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/cycles/{id:guid}/indicators", async (Guid id, IndicatorRequest r, ClaimsPrincipal u, CssdService svc) =>
        {
            var warning = await svc.UpdateIndicatorsAsync(id, r.Chemical, r.Biological, Me(u));
            return warning is not null && !warning.StartsWith("Recorded", StringComparison.Ordinal) ? Done(warning) : Results.Ok(new { warning });
        }).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/cycles/{id:guid}/release", async (Guid id, ReleaseRequest r, ClaimsPrincipal u, CssdService svc) =>
        {
            var (ok, message) = await svc.ReleaseAsync(id, r.Release, r.Note, Me(u));
            return ok ? Results.Ok(new { message }) : Done(message);
        }).RequireAuthorization(Permissions.CssdRelease);
        g.MapPost("/cycles/{id:guid}/recall", async (Guid id, ReasonRequest r, ClaimsPrincipal u, CssdService svc, IServiceProvider sp) =>
        {
            if (string.IsNullOrWhiteSpace(r.Reason)) return Done("Enter why the load is being recalled.");
            var (q, used, nc) = await svc.RecallAsync(id, r.Reason, Me(u));
            return Results.Ok(new { quarantined = q, nonconformance = nc, cases = await DescribeAsync(sp, used) });
        }).RequireAuthorization(Permissions.CssdRelease);

        // ---------------------------------------------------------------- traceability
        g.MapGet("/trace", async (Guid? cycleId, Guid? patientId, Guid? bookingId, string? barcode, CssdService svc, IServiceProvider sp, BrightPAS.Core.Audit.AuditWriter audit, ClaimsPrincipal u) =>
        {
            Guid? trayFilter = null;
            if (!string.IsNullOrWhiteSpace(barcode)) trayFilter = (await svc.FindTrayAsync(barcode))?.Id ?? Guid.Empty;
            var rows = (await svc.IssuesAsync(cycleId, bookingId, patientId)).Where(i => trayFilter is null || i.TrayInstanceId == trayFilter).ToList();
            var db = sp.GetRequiredService<AssetsDbContext>();
            var scopeUses = await db.ScopeEvents.AsNoTracking().Where(e => e.Kind == ScopeEventKind.Use && ((patientId != null && e.PatientId == patientId) || (bookingId != null && e.BookingId == bookingId))).ToListAsync();
            var scopes = await db.Equipment.AsNoTracking().Where(x => scopeUses.Select(s => s.ScopeId).Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Name} ({x.AssetNumber}, serial {x.SerialNumber})");
            if (patientId is { } pid) await audit.WriteAsync(Me(u), "View", "Traceability", pid.ToString(), pid.ToString());
            return Results.Ok(new { trays = await DescribeAsync(sp, rows), scopes = scopeUses.Select(s => new { s.At, scope = scopes.GetValueOrDefault(s.ScopeId), s.By, s.BookingId }) });
        }).RequireAuthorization(Permissions.AssetsView);

        // ---------------------------------------------------------------- endoscopes
        g.MapGet("/scopes", async (CssdService svc) => Results.Ok(await svc.ScopesAsync())).RequireAuthorization(Permissions.AssetsView);
        g.MapGet("/scopes/{id:guid}/history", async (Guid id, CssdService svc, IServiceProvider sp) =>
        {
            var events = await svc.ScopeHistoryAsync(id);
            var names = await PatientNamesAsync(sp, events.Where(e => e.PatientId != null).Select(e => e.PatientId!.Value));
            return Results.Ok(events.Select(e => new { e.Id, kind = e.Kind.ToString(), e.At, e.By, e.Passed, e.CycleId, e.BookingId, patient = e.PatientId is { } p ? names.GetValueOrDefault(p) : null, e.Notes }));
        }).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/scopes/events", async (ScopeEventEdit e, ClaimsPrincipal u, CssdService svc, TheatreDbContext theatre) =>
        {
            if (e.BookingId is { } bid && e.PatientId is null) e = e with { PatientId = (await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bid))?.PatientId };
            var (ev, error, warning) = await svc.RecordScopeEventAsync(e, Me(u));
            return error is not null ? Done(error) : Results.Ok(new { ev!.Id, warning });
        }).RequireAuthorization(Permissions.CssdProcess);

        // ---------------------------------------------------------------- loan sets and nonconformance
        g.MapGet("/loans", async (bool? all, CssdService svc) => Results.Ok(await svc.LoansAsync(all != true))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/loans", async (LoanEdit e, ClaimsPrincipal u, CssdService svc) =>
        {
            var (row, errors) = await svc.SaveLoanAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id, row.Number });
        }).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/loans/{id:guid}/advance", async (Guid id, LoanAdvance r, ClaimsPrincipal u, CssdService svc) => Done(await svc.AdvanceLoanAsync(id, r.To, r.Notes, r.CycleId, Me(u))))
            .RequireAuthorization(Permissions.CssdProcess);
        g.MapGet("/nonconformances", async (bool? all, CssdService svc) => Results.Ok(await svc.NonconformancesAsync(all != true))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/nonconformances", async (NcEdit e, ClaimsPrincipal u, CssdService svc) =>
        {
            var (row, errors) = await svc.RaiseAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id, row.Number });
        }).RequireAuthorization(Permissions.CssdProcess);
        g.MapPost("/nonconformances/{id:guid}/close", async (Guid id, ClaimsPrincipal u, CssdService svc) => Done(await svc.CloseNcAsync(id, Me(u)))).RequireAuthorization(Permissions.CssdRelease);

        // ---------------------------------------------------------------- stock, implants, purchasing
        g.MapGet("/stock", async (string? q, bool? low, StockService svc) => Results.Ok(await svc.ItemsAsync(q, low == true))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/stock", async (StockItemEdit e, ClaimsPrincipal u, StockService svc) =>
        {
            var (row, errors) = await svc.SaveItemAsync(e, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id });
        }).RequireAuthorization(Permissions.AssetsManage);
        g.MapGet("/stock/{id:guid}/lots", async (Guid id, StockService svc) => Results.Ok(await svc.LotsAsync(id))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/stock/use", async (UseRequest r, ClaimsPrincipal u, StockService svc, TheatreDbContext theatre) =>
        {
            if (r.BookingId is { } bid && r.PatientId is null) r = r with { PatientId = (await theatre.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bid))?.PatientId };
            return Done(await svc.UseAsync(r, Me(u)));
        }).RequireAuthorization(Permissions.StockManage);
        g.MapPost("/stock/lots/{id:guid}/adjust", async (Guid id, AdjustRequest r, ClaimsPrincipal u, StockService svc) => Done(await svc.AdjustAsync(id, r.Change, r.Expired, r.Reason, Me(u))))
            .RequireAuthorization(Permissions.StockManage);
        g.MapPost("/implants", async (ImplantRequest r, ClaimsPrincipal u, StockService svc, PatientsDbContext patients) =>
        {
            if (!await patients.Patients.AnyAsync(p => p.Id == r.PatientId)) return Done("Choose the patient.");
            var (row, error) = await svc.RecordImplantAsync(r, Me(u));
            return error is null ? Results.Ok(new { row!.Id }) : Done(error);
        }).RequireAuthorization(Permissions.StockManage);
        g.MapGet("/implants", async (Guid? patientId, Guid? stockItemId, string? lot, StockService svc, IServiceProvider sp, BrightPAS.Core.Audit.AuditWriter audit, ClaimsPrincipal u) =>
        {
            if (patientId is null && stockItemId is null) return Done("Choose a patient, or an implant (and lot) to find the patients who received it.");
            var rows = await svc.ImplantsAsync(patientId, stockItemId, lot);
            var names = await PatientNamesAsync(sp, rows.Select(r => r.PatientId));
            var items = await sp.GetRequiredService<AssetsDbContext>().StockItems.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Name);
            await audit.WriteAsync(Me(u), "View", "Implants", patientId?.ToString() ?? stockItemId.ToString()!, patientId?.ToString(), reason: lot is null ? null : $"lot {lot}");
            return Results.Ok(rows.Select(r => new { r.Id, r.PatientId, patient = names.GetValueOrDefault(r.PatientId), r.BookingId, item = items.GetValueOrDefault(r.StockItemId), r.Lot, r.SerialNumber, r.Manufacturer, r.Site, r.ImplantedOn, r.RecordedBy }));
        }).RequireAuthorization(Permissions.AssetsView);
        g.MapGet("/orders", async (bool? all, StockService svc) => Results.Ok(await svc.OrdersAsync(all != true))).RequireAuthorization(Permissions.AssetsView);
        g.MapPost("/orders", async (OrderRequest r, ClaimsPrincipal u, StockService svc) =>
        {
            var (row, errors) = await svc.CreateOrderAsync(r.SupplierId, r.Lines ?? [], r.Notes, Me(u));
            return row is null ? Errors(errors) : Results.Ok(new { row.Id, row.Number });
        }).RequireAuthorization(Permissions.StockManage);
        g.MapPost("/orders/reorder", async (ClaimsPrincipal u, StockService svc) => Results.Ok(new { created = await svc.DraftReordersAsync(Me(u)) })).RequireAuthorization(Permissions.StockManage);
        g.MapPost("/orders/{id:guid}/status", async (Guid id, StatusRequest r, ClaimsPrincipal u, StockService svc) =>
            r.To == PoStatus.Approved ? Done("Orders are approved with Approve (purchasing approvers only).") : Done(await svc.SetOrderStatusAsync(id, r.To, Me(u))))
            .RequireAuthorization(Permissions.StockManage);
        g.MapPost("/orders/{id:guid}/approve", async (Guid id, ClaimsPrincipal u, StockService svc) => Done(await svc.SetOrderStatusAsync(id, PoStatus.Approved, Me(u))))
            .RequireAuthorization(Permissions.PurchasingApprove);
        g.MapPost("/orders/{id:guid}/receive", async (Guid id, ReceiveRequest r, ClaimsPrincipal u, StockService svc) => Done(await svc.ReceiveAsync(id, r.Lines ?? [], Me(u))))
            .RequireAuthorization(Permissions.StockManage);
        g.MapPost("/orders/{id:guid}/invoices", async (Guid id, InvoiceRequest r, ClaimsPrincipal u, StockService svc) =>
        {
            var (inv, error) = await svc.AddInvoiceAsync(id, r, Me(u));
            return error is null ? Results.Ok(inv) : Done(error);
        }).RequireAuthorization(Permissions.PurchasingApprove);
        g.MapPost("/invoices/{id:guid}/accept", async (Guid id, ReasonRequest r, ClaimsPrincipal u, StockService svc) => Done(await svc.ApproveMismatchAsync(id, r.Reason ?? "", Me(u))))
            .RequireAuthorization(Permissions.PurchasingApprove);
        g.MapPost("/xero/bills", async (XeroRequest r, ClaimsPrincipal u, StockService svc) =>
        {
            var (csv, count) = await svc.XeroBillsCsvAsync(Me(u), r.MarkExported);
            return count == 0 ? Done("No matched supplier invoices are waiting to go to Xero.")
                : Results.File(BrightPAS.Web.Security.SecureExportService.Utf8Bom(csv), "text/csv", $"xero-bills-{DateTime.Now:yyyyMMdd-HHmm}.csv");
        }).RequireAuthorization(Permissions.PurchasingApprove);
    }

    private static async Task<LabelPrinter?> DefaultZebraAsync(PatientsDbContext patients) =>
        await patients.Printers.AsNoTracking().FirstOrDefaultAsync(p => p.Kind == PrinterKind.Label && p.IsDefault && p.IsActive && p.Language == PrinterLanguage.Zpl);

    private static async Task<string?> SendZplAsync(string host, int port, string zpl)
    {
        if (!LabelService.AllowedPort(port)) return "port not allowed";
        try
        {
            using var tcp = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            await tcp.ConnectAsync(host, port, cts.Token);
            await using var s = tcp.GetStream();
            await s.WriteAsync(Encoding.UTF8.GetBytes(zpl), cts.Token);
            return null;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException) { return ex.Message; }
    }

    private static async Task<Dictionary<Guid, string>> PatientNamesAsync(IServiceProvider sp, IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return await sp.GetRequiredService<PatientsDbContext>().Patients.AsNoTracking().Where(p => list.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => $"{p.FamilyName.ToUpperInvariant()}, {p.GivenNames} ({p.Nhi ?? p.Mrn})");
    }

    /// <summary>Adds the patient's name and the case (date, procedure) to tray issues, for recall lists.</summary>
    private static async Task<IReadOnlyList<object>> DescribeAsync(IServiceProvider sp, IReadOnlyList<IssueRow> rows)
    {
        var names = await PatientNamesAsync(sp, rows.Where(r => r.PatientId != null).Select(r => r.PatientId!.Value));
        var theatre = sp.GetRequiredService<TheatreDbContext>();
        var bookingIds = rows.Where(r => r.BookingId != null).Select(r => r.BookingId!.Value).Distinct().ToList();
        var cases = await (from b in theatre.Bookings.AsNoTracking()
                           join s in theatre.Sessions.AsNoTracking() on b.SessionId equals s.Id
                           where bookingIds.Contains(b.Id)
                           select new { b.Id, s.Date, b.Procedure }).ToDictionaryAsync(x => x.Id);
        return rows.Select(r => (object)new
        {
            r.Id,
            r.Barcode,
            r.Tray,
            r.LoadNumber,
            r.IssuedAt,
            r.IssuedBy,
            r.ReturnedAt,
            r.PatientId,
            r.BookingId,
            patient = r.PatientId is { } p ? names.GetValueOrDefault(p) : null,
            caseDate = r.BookingId is { } b && cases.TryGetValue(b, out var c) ? c.Date : (DateOnly?)null,
            procedure = r.BookingId is { } b2 && cases.TryGetValue(b2, out var c2) ? c2.Procedure : null,
        }).ToList();
    }
}
