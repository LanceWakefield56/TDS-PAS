using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Assets;

namespace BrightPAS.Modules.Tests;

/// <summary>0.13.0 Assets &amp; CSSD: equipment and lockout, tray lifecycle and parametric release, recall, machine log import, endoscopes, stock and purchasing.</summary>
public class AssetsTests
{
    private static readonly DateOnly Mon = new(2026, 11, 9);
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(13));

    private static async Task<Guid> MachineAsync(EquipmentService eq, string no, EquipmentCategory cat) =>
        (await eq.SaveAsync(new(null, no, cat.ToString(), cat, "Belimed", "WD 290 IQ", "SN" + no, null, "CSSD", null, null, null, null, RiskClass.High, null, null), "lee")).Equipment!.Id;

    [Fact]
    public async Task Failed_electrical_test_locks_equipment_out_and_return_to_service_needs_a_passed_test()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var eq = s.ServiceProvider.GetRequiredService<EquipmentService>();
        var (bad, errors) = await eq.SaveAsync(new(null, "", "", EquipmentCategory.Electromedical, null, null, null, null, null, null, null, null, null, RiskClass.High, null, null), "admin");
        Assert.Null(bad); Assert.Equal(2, errors.Count);
        var id = (await eq.SaveAsync(new(null, "tds-0101", "Diathermy unit", EquipmentCategory.Electromedical, "Valleylab", "FT10", "F1", null, "Theatre", null, Mon.AddYears(-2), 25000, null, RiskClass.High, null, null), "admin")).Equipment!.Id;
        Assert.NotEmpty((await eq.SaveAsync(new(null, "TDS-0101", "Dup", EquipmentCategory.Other, null, null, null, null, null, null, null, null, null, RiskClass.Low, null, null), "admin")).Errors);
        Assert.Empty(await eq.SavePlanAsync(new(id, MaintenanceKind.TestAndTag, 12, Mon.AddDays(10), "Nelson Electrical"), "admin"));
        Assert.Equal("DueSoon", (await eq.ListAsync(null, null, false)).Single().DueState);
        Assert.NotEmpty((await eq.RecordAsync(new(id, MaintenanceKind.TestAndTag, Mon, "Nelson Electrical", true, null, null), "admin")).Errors); // tag number needed
        await eq.RecordAsync(new(id, MaintenanceKind.TestAndTag, Mon, "Nelson Electrical", false, null, "Earth continuity failed"), "admin");
        var row = (await eq.ListAsync(null, null, false)).Single();
        Assert.Equal("OutOfService", row.Status);
        Assert.Contains("Failed electrical test-and-tag", row.OutOfServiceReason);
        h.Clock.Advance(TimeSpan.FromDays(20));
        Assert.Contains("overdue", await eq.ReturnToServiceAsync(id, "Lead replaced", "admin"));
        await eq.RecordAsync(new(id, MaintenanceKind.TestAndTag, Mon.AddDays(20), "Nelson Electrical", true, "T-5521", "Lead replaced"), "admin");
        Assert.Null(await eq.ReturnToServiceAsync(id, "Lead replaced, retested", "admin"));
        Assert.Equal(Mon.AddDays(20).AddMonths(12), (await eq.PlansAsync(id)).Single().NextDue);
    }

    [Fact]
    public async Task Tray_goes_dirty_washed_packed_sterile_issued_and_a_recall_finds_the_patient()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var eq = s.ServiceProvider.GetRequiredService<EquipmentService>();
        var cssd = s.ServiceProvider.GetRequiredService<CssdService>();
        var washer = await MachineAsync(eq, "WD1", EquipmentCategory.Washer);
        var autoclave = await MachineAsync(eq, "ST1", EquipmentCategory.Steriliser);
        var set = (await cssd.SaveTraySetAsync(new(null, "lapchole", "Laparoscopic cholecystectomy", "General", 90, true, [new("Maryland grasper", 2), new("Clip applier", 1)]), "lee")).Set!;
        var (trays, _) = await cssd.AddTraysAsync(set.Id, 2, "lee");
        Assert.Equal(["LAPCHOLE-01", "LAPCHOLE-02"], trays.Select(t => t.Barcode));

        // Not packed yet → cannot be sterilised; washer cannot take a steriliser cycle.
        Assert.Contains((await cssd.CreateCycleAsync(new(autoclave, CycleKind.Steam, null, "134 4min", Now, null, 134, 240, 210, null, true, IndicatorResult.Pass, IndicatorResult.NotUsed, false, ["LAPCHOLE-01"], null), "Manual", null, "lee")).Errors,
            e => e.Contains("only packed trays"));
        Assert.Contains((await cssd.CreateCycleAsync(new(washer, CycleKind.Steam, null, null, Now, null, 134, 240, null, null, true, IndicatorResult.Pass, IndicatorResult.NotUsed, false, ["LAPCHOLE-01"], null), "Manual", null, "lee")).Errors,
            e => e.Contains("not a steriliser"));

        var (wash, werr) = await cssd.CreateCycleAsync(new(washer, CycleKind.Wash, "4411", "Instruments", Now, Now.AddMinutes(55), 93, 300, null, 3000, true, IndicatorResult.NotUsed, IndicatorResult.NotUsed, false, ["LAPCHOLE-01"], null), "Manual", null, "lee");
        Assert.True(wash is not null, string.Join(" ", werr));
        Assert.True((await cssd.ReleaseAsync(wash!.Id, true, null, "lee")).Ok);
        Assert.Contains("contents list", await cssd.PackAsync("lapchole-01", false, null, "lee"));
        Assert.Null(await cssd.PackAsync("lapchole-01", true, null, "lee"));

        h.Clock.Advance(TimeSpan.FromHours(3));
        var (steam, _) = await cssd.CreateCycleAsync(new(autoclave, CycleKind.Steam, "882", "134C 4 min", Now.AddHours(1), Now.AddHours(2), 134.2m, 240, 212, null, true, IndicatorResult.Pending, IndicatorResult.Pending, true, ["LAPCHOLE-01"], [new("Steri packs", 6)]), "Manual", null, "lee");
        var blocked = await cssd.ReleaseAsync(steam!.Id, true, null, "lee");
        Assert.False(blocked.Ok);
        Assert.Contains("chemical indicator has not been checked", blocked.Message);
        Assert.Contains("biological indicator result is not in yet", blocked.Message);
        Assert.Null(await cssd.UpdateIndicatorsAsync(steam.Id, IndicatorResult.Pass, IndicatorResult.Pass, "lee"));
        Assert.True((await cssd.ReleaseAsync(steam.Id, true, null, "lee")).Ok);
        var tray = (await cssd.TraysAsync(null, "LAPCHOLE-01")).Single();
        Assert.Equal("Sterile", tray.Status);
        Assert.Equal(DateOnly.FromDateTime(h.Clock.GetLocalNow().DateTime).AddDays(90), tray.SterileUntil);

        Assert.Contains("only sterile", (await cssd.IssueAsync("LAPCHOLE-02", null, p.Id, "rn")).Error);
        var (issue, err) = await cssd.IssueAsync("LAPCHOLE-01", null, p.Id, "rn");
        Assert.Null(err);
        Assert.Equal(steam.LoadNumber, issue!.LoadNumber);

        var (q, used, nc) = await cssd.RecallAsync(steam.Id, "BI later found positive", "lee");
        Assert.Equal(0, q); // already used, nothing left in store
        Assert.Equal(p.Id, used.Single().PatientId);
        Assert.NotNull(nc);
        Assert.Null(await cssd.ReturnDirtyAsync("LAPCHOLE-01", "lee"));
        Assert.Equal("Dirty", (await cssd.TraysAsync(null, "LAPCHOLE-01")).Single().Status);
        Assert.NotNull((await cssd.IssuesAsync(null, null, p.Id)).Single().ReturnedAt);
    }

    [Fact]
    public async Task Imported_machine_record_needs_its_trays_scanned_before_release()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var eq = s.ServiceProvider.GetRequiredService<EquipmentService>();
        var cssd = s.ServiceProvider.GetRequiredService<CssdService>();
        var washer = await MachineAsync(eq, "WD9", EquipmentCategory.Washer);
        var set = (await cssd.SaveTraySetAsync(new(null, "EYE", "Eye set", null, 90, true, [new("Speculum", 1)]), "lee")).Set!;
        await cssd.AddTraysAsync(set.Id, 1, "lee");
        var edit = new CycleEdit(washer, CycleKind.Wash, "77", "Instruments", Now, Now.AddMinutes(50), 93, 300, null, 3000, true, IndicatorResult.NotUsed, IndicatorResult.NotUsed, false, [], []);
        Assert.Contains((await cssd.CreateCycleAsync(edit, "Manual", null, "lee")).Errors, e => e.Contains("Scan the trays"));
        var (c, errs) = await cssd.CreateCycleAsync(edit, "00000077.csv", "log", "lee", contentsLater: true);
        Assert.True(c is not null, string.Join(" ", errs));
        Assert.Contains("contents are not listed", (await cssd.ReleaseAsync(c!.Id, true, null, "kim")).Message);
        Assert.Contains(await cssd.AddContentsAsync(c.Id, ["NOPE-01"], null, "lee"), e => e.Contains("No tray"));
        Assert.Empty(await cssd.AddContentsAsync(c.Id, ["eye-01"], [new("Steri packs", 2)], "lee"));
        Assert.True((await cssd.ReleaseAsync(c.Id, true, null, "kim")).Ok);
        Assert.Contains(await cssd.AddContentsAsync(c.Id, ["EYE-01"], null, "lee"), e => e.Contains("already been released"));
    }

    [Fact]
    public async Task Failed_load_quarantines_trays_raises_a_nonconformance_and_needs_root_cause_to_close()
    {
        await using var h = new ModulesHost();
        await using var s = h.Scope();
        var eq = s.ServiceProvider.GetRequiredService<EquipmentService>();
        var cssd = s.ServiceProvider.GetRequiredService<CssdService>();
        var washer = await MachineAsync(eq, "WD1", EquipmentCategory.Washer);
        var set = (await cssd.SaveTraySetAsync(new(null, "MINOR", "Minor ops", null, 90, true, [new("Scissors", 1)]), "lee")).Set!;
        await cssd.AddTraysAsync(set.Id, 1, "lee");
        var (c, _) = await cssd.CreateCycleAsync(new(washer, CycleKind.Wash, null, null, Now, null, 60, null, null, 120, false, IndicatorResult.NotUsed, IndicatorResult.NotUsed, false, ["MINOR-01"], null), "Manual", null, "lee");
        Assert.Contains("machine did not report a good cycle", (await cssd.ReleaseAsync(c!.Id, true, null, "lee")).Message);
        Assert.False((await cssd.ReleaseAsync(c.Id, false, null, "lee")).Ok); // reason needed
        var failed = await cssd.ReleaseAsync(c.Id, false, "A0 too low – door seal", "lee");
        Assert.True(failed.Ok);
        Assert.Equal("Quarantine", (await cssd.TraysAsync(null, null)).Single().Status);
        var nc = (await cssd.NonconformancesAsync(true)).Single();
        Assert.Contains("root cause", (await cssd.CloseNcAsync(nc.Id, "lee"))!);
        await cssd.RaiseAsync(new(nc.Id, nc.Category, nc.Description, nc.CycleId, null, nc.EquipmentId, nc.ImmediateAction, "Worn door seal", "Seal replaced by Belimed", "Seal checked weekly"), "lee");
        Assert.Null(await cssd.CloseNcAsync(nc.Id, "lee"));

        // Out-of-service machine cannot run loads.
        await eq.LockOutAsync(washer, "Door seal leaking", "lee");
        Assert.Contains((await cssd.CreateCycleAsync(new(washer, CycleKind.Wash, null, null, Now, null, 93, null, null, 3000, true, IndicatorResult.NotUsed, IndicatorResult.NotUsed, false, ["MINOR-01"], null), "Manual", null, "lee")).Errors,
            e => e.Contains("out of service"));
    }

    [Fact]
    public void Machine_log_parser_reads_labelled_values_and_reports_what_is_missing()
    {
        var log = """
            BELIMED WD 290 IQ
            Batch no.: 004411
            Program: Instruments Standard
            Start: 09.11.2026 07:42:10
            End: 09.11.2026 08:37:55
            Disinfection temp: 93.4 °C
            Holding time: 05:00
            A0 value: 3010
            Result: Cycle OK
            """;
        var p = CssdService.ParseMachineLog(log);
        Assert.Equal("004411", p.MachineCycleNumber);
        Assert.Equal("Instruments Standard", p.Program);
        Assert.Equal(new DateTime(2026, 11, 9, 7, 42, 10), p.StartedAt!.Value.DateTime);
        Assert.Equal(93.4m, p.MaxTemperatureC);
        Assert.Equal(300, p.HoldSeconds);
        Assert.Equal(3010, p.A0);
        Assert.True(p.Passed);
        Assert.Empty(p.Notes);

        var fault = CssdService.ParseMachineLog("Cycle 77\nFAULT 214: water pressure low\ntemperature 41 °C");
        Assert.False(fault.Passed);
        Assert.Contains(fault.Notes, n => n.Contains("Start time"));
        var steam = CssdService.ParseMachineLog("Cycle no: 882\nStart: 2026-11-09 10:00:00\nSterilisation temp: 134.1\nPressure: 2.1 bar\nHold: 4 min\nStatus: PASSED");
        Assert.Equal(210m, steam.PressureKpa);
        Assert.Equal(240, steam.HoldSeconds);
        Assert.True(steam.Passed);
    }

    [Fact]
    public async Task Endoscope_is_ready_only_after_leak_test_and_AER_and_within_storage_time()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var eq = s.ServiceProvider.GetRequiredService<EquipmentService>();
        var cssd = s.ServiceProvider.GetRequiredService<CssdService>();
        var scope = (await eq.SaveAsync(new(null, "GIF-01", "Gastroscope", EquipmentCategory.Endoscope, "Olympus", "GIF-HQ190", "2912345", null, "Endoscopy", null, null, null, null, RiskClass.High, null, null), "lee")).Equipment!.Id;
        Assert.Equal("NeedsReprocessing", (await cssd.ScopesAsync()).Single().State);
        Assert.Contains("not ready", (await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.Use, null, null, null, p.Id, null), "rn")).Error);
        await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.LeakTest, true, null, null, null, null), "lee");
        await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.ManualClean, null, null, null, null, null), "lee");
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.AerCycle, true, null, null, null, "AER cycle 551"), "lee");
        Assert.Equal("Ready", (await cssd.ScopesAsync()).Single().State);
        h.Clock.Advance(TimeSpan.FromHours(73));
        Assert.Equal("StorageExpired", (await cssd.ScopesAsync()).Single().State);
        await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.LeakTest, true, null, null, null, null), "lee");
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.AerCycle, true, null, null, null, null), "lee");
        h.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Null((await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.Use, null, null, null, p.Id, null), "rn")).Error);
        Assert.Equal("NeedsReprocessing", (await cssd.ScopesAsync()).Single().State);
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        var (_, _, warning) = await cssd.RecordScopeEventAsync(new(scope, ScopeEventKind.LeakTest, false, null, null, null, "Leak at bending section"), "lee");
        Assert.Contains("quarantined", warning);
        Assert.Equal("Quarantined", (await cssd.ScopesAsync()).Single().State);
        Assert.Single(await cssd.NonconformancesAsync(true));
    }

    [Fact]
    public async Task Stock_uses_first_expiry_first_records_implants_and_purchasing_matches_invoices_for_xero()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var eq = s.ServiceProvider.GetRequiredService<EquipmentService>();
        var stock = s.ServiceProvider.GetRequiredService<StockService>();
        var sup = (await eq.SaveSupplierAsync(new(null, "Medical Supplies NZ", "Sam", "orders@msnz.example.nz", null, "Medical Supplies NZ Ltd", true), "admin")).Supplier!;
        var mesh = (await stock.SaveItemAsync(new(null, "mesh-15", "Hernia mesh 15x15", "each", true, sup.Id, "HM15", 2, 5, 120m, "310", true), "admin")).Item!;
        var gauze = (await stock.SaveItemAsync(new(null, "GAUZE", "Gauze swabs (pk 10)", "pack", false, sup.Id, null, 10, 20, 3.5m, "310", true), "admin")).Item!;
        Assert.Equal(2, (await stock.ItemsAsync(null, true)).Count);

        var drafted = await stock.DraftReordersAsync("lee");
        Assert.Single(drafted);
        var po = (await stock.OrdersAsync(true)).Single();
        Assert.Contains("approved", await stock.ReceiveAsync(po.Id, [], "lee"));
        Assert.Null(await stock.SetOrderStatusAsync(po.Id, PoStatus.Approved, "grace"));
        var meshLine = po.Items.Single(i => i.StockItemId == mesh.Id);
        var gauzeLine = po.Items.Single(i => i.StockItemId == gauze.Id);
        Assert.Contains("lot", await stock.ReceiveAsync(po.Id, [new(meshLine.Id, 2, "", null)], "lee"));
        Assert.Null(await stock.ReceiveAsync(po.Id, [new(meshLine.Id, 2, "L-OLD", Mon.AddMonths(2)), new(meshLine.Id, 3, "L-NEW", Mon.AddYears(2)), new(gauzeLine.Id, gauzeLine.Quantity, "G1", null)], "lee"));
        Assert.Equal("Received", (await stock.OrdersAsync(true)).Single().Status);

        // Gauze used on a case comes off stock; implant needs a patient and records the lot.
        Assert.Null(await stock.UseAsync(new(gauze.Id, 3, null, null, p.Id, "Case"), "rn"));
        Assert.Contains("Record implant", await stock.UseAsync(new(mesh.Id, 1, null, null, null, null), "rn"));
        var (imp, ierr) = await stock.RecordImplantAsync(new(mesh.Id, null, "L-OLD", "SN-1", "Medtronic", "Left inguinal", p.Id, null, null), "rn");
        Assert.Null(ierr);
        Assert.Equal(p.Id, (await stock.ImplantsAsync(null, mesh.Id, "L-OLD")).Single().PatientId);
        Assert.Equal(1, (await stock.LotsAsync(mesh.Id)).Single(l => l.Lot == "L-OLD").OnHand);

        // Invoice: 5 x 120 + 20 x 3.50 = 670 received. A wrong total is flagged; the right one matches and exports.
        var (wrong, _) = await stock.AddInvoiceAsync(po.Id, new("INV-100", Mon, Mon.AddDays(20), 700m), "grace");
        Assert.False(wrong!.Matched);
        Assert.Contains("Does not match", wrong.MatchNote);
        Assert.Null(await stock.ApproveMismatchAsync(wrong.Id, "Freight $30 agreed", "grace"));
        var (csv, count) = await stock.XeroBillsCsvAsync("grace", true);
        Assert.Equal(1, count);
        Assert.Contains("Medical Supplies NZ Ltd", csv);
        Assert.Contains("INV-100", csv);
        Assert.Contains("15% GST on Expenses", csv);
        Assert.Equal(0, (await stock.XeroBillsCsvAsync("grace", true)).Count); // not exported twice
        Assert.Contains("already recorded", (await stock.AddInvoiceAsync(po.Id, new("INV-100", Mon, Mon.AddDays(20), 1m), "grace")).Error);
    }
}
