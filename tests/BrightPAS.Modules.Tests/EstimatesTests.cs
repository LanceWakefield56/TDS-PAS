using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Modules.Estimates;

namespace BrightPAS.Modules.Tests;

/// <summary>
/// 0.21.0 Procedure estimates: the TDS Estimates v1.5 self-test, run against the module with TDS's real pricing spreadsheet
/// (Fixtures/estimates-pricing.xlsx = the bundled "Estimates - Facility &amp; Anaesthetist.xlsx") and stock list.
/// Totals must match the standalone app and the old 2026 estimates exactly.
/// </summary>
public class EstimatesTests
{
    private const string User = "finance";

    // Totals from the old 2026 estimates / gynae workbooks (default anaesthetic, no add-ons) – the standalone self-test list.
    private static readonly Dictionary<string, decimal> Known = new(StringComparer.Ordinal)
    {
        ["END-001"] = 4750,
        ["END-002"] = 4662,
        ["END-003"] = 4010,
        ["END-004"] = 3910,
        ["END-005"] = 7120,
        ["END-006"] = 6610,
        ["END-007"] = 3885,
        ["END-008"] = 5600,
        ["END-009"] = 3350,
        ["GEN-001"] = 6415,
        ["GEN-003"] = 8356,
        ["GEN-005"] = 6909,
        ["GEN-007"] = 7040,
        ["GEN-009"] = 3525,
        ["GEN-011"] = 9168,
        ["GEN-013"] = 15077,
        ["GEN-017"] = 6350.40m,
        ["GEN-018"] = 7736.40m,
        ["GEN-020"] = 7235,
        ["GEN-021"] = 4320.40m,
        ["DEN-001"] = 7440,
        ["DEN-004"] = 8391.74m,
        ["DEN-013"] = 7111.74m,
        ["CAR-001"] = 3000,
        ["INF-001"] = 2064,
        ["GYN-001"] = 6959.95m + 1950 + 3450,
        ["GYN-004"] = 15944.5m + 2950 + 9200,
        ["GYN-013"] = 3819.4m + 2950 + 3450,
    };

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Loads the real pricing and stock spreadsheets through the import (as the standalone app's first run does) and seeds the templates.</summary>
    private static async Task<ModulesHost> HostAsync()
    {
        var h = new ModulesHost();
        await using var scope = h.Scope();
        var imp = scope.ServiceProvider.GetRequiredService<EstimateImport>();
        await using (var f = File.OpenRead(Fixture("estimates-pricing.xlsx")))
        {
            var pv = await imp.PreviewPricingAsync(f, "pricing.xlsx");
            Assert.Empty(pv.Errors);
            Assert.True(pv.New >= 69, $"{pv.New} procedures");
            await imp.ApplyPricingAsync(pv, true, true, false, User);
        }
        await using (var f = File.OpenRead(Fixture("estimates-stock.xlsx")))
        {
            var pv = await imp.PreviewStockAsync(f, "stock.xlsx");
            Assert.Empty(pv.Errors);
            await imp.ApplyStockAsync(pv, User);
        }
        await scope.ServiceProvider.GetRequiredService<EstimateService>().SeedTemplatesOnceAsync(User);
        return h;
    }

    private static EstimateForm Form(EstProcedure p, string last = "Patient", string? nhi = null, Action<EstimateForm>? edit = null)
    {
        var f = new EstimateForm
        {
            FirstName = "Test",
            LastName = last,
            DateOfBirth = new DateOnly(1970, 1, 1),
            Nhi = nhi,
            Email = "test@example.co.nz",
            Address = "1 Test Street",
            Town = "Richmond",
            Postcode = "7020",
            ProcedureId = p.Id,
            Anaesthetic = p.DefaultAnaesthetic,
            ProcedureDate = new DateOnly(2026, 12, 9),
            PreparedBy = "Self test"
        };
        edit?.Invoke(f);
        return f;
    }

    private static async Task<Dictionary<string, EstProcedure>> ProcsAsync(IServiceProvider sp) =>
        await sp.GetRequiredService<EstDbContext>().Procedures.AsNoTracking().ToDictionaryAsync(p => p.Code, StringComparer.Ordinal);

    private static AddOnChoice A(string code, bool ifRequired = false) => new() { Code = code, IfRequired = ifRequired };

    [Fact]
    public async Task Every_procedure_prices_as_the_standalone_app_and_the_old_estimates()
    {
        await using var h = await HostAsync();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<EstimateService>();
        var db = sp.GetRequiredService<EstDbContext>();
        var procs = await db.Procedures.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Code).ToListAsync();
        Assert.True(procs.Count >= 69, $"{procs.Count} active procedures");
        var report = new StringBuilder();
        var fails = 0;
        foreach (var p in procs)
        {
            var res = await svc.SaveAsync(null, Form(p, "Patient" + p.Code.Replace("-", "", StringComparison.Ordinal), "ZZZ0016"), true, User);
            Assert.Empty(res.Errors);
            var ctx = await svc.ContextAsync(p.Id);
            var expected = (p.Facility ?? 0) + (EstimateCalculator.FeeFor(ctx, null, false) ?? 0) + (Anaesthetics.HasAnaesthetist(p.DefaultAnaesthetic) ? p.Anaesthetist ?? 0 : 0) + (p.ConsumablesLine ?? 0);
            var ok = res.Estimate!.Subtotal == expected && (!Known.TryGetValue(p.Code, out var k) || k == res.Estimate.Subtotal);
            if (!ok) { fails++; report.AppendLine($"FAIL {p.Code} {res.Estimate.Subtotal:0.00} expected {expected:0.00} old {(Known.TryGetValue(p.Code, out var kk) ? kk.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "–")}"); }
        }
        Assert.True(fails == 0, report.ToString());
        Assert.All(Known.Keys, code => Assert.Contains(procs, p => p.Code == code));
        // Every issued estimate kept a PDF copy.
        Assert.Equal(procs.Count, await db.Archive.CountAsync());
    }

    [Fact]
    public async Task Add_ons_included_add_ons_itemised_lines_special_package_and_extended_time()
    {
        await using var h = await HostAsync();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<EstimateService>();
        var p = await ProcsAsync(sp);

        // Colonoscopy + biopsies (required) + polypectomy (if required), insured: no deposit, polypectomy listed but not totalled.
        var colon = p["END-001"];
        var s1 = (await svc.SaveAsync(null, Form(colon, "Test", edit: f => { f.Funding = Funding.Insurance; f.Insurer = "Southern Cross"; f.Inputs.AddOns.AddRange([A("END-A01"), A("END-A02", true)]); }), false, User)).Estimate!;
        Assert.Equal(4750m + 290, s1.Subtotal);
        Assert.Equal(0, s1.Deposit);
        var l1 = await svc.FindAsync(s1.Id);
        Assert.Contains(l1.Lines, l => l.Kind == LineKind.IfRequired && l.Amount == 820);
        Assert.Contains(l1.Lines, l => l.Kind == LineKind.Fee && l.Label == "Add-on – Biopsies");

        // Ablation includes NovaSure – it must not be charged twice; the IUD add-on prints as its own line under the consultant fee.
        var abl = p["GYN-003"];
        var s2 = (await svc.SaveAsync(null, Form(abl, "Charge", edit: f => { f.Anaesthetic = Anaesthetic.General; f.Inputs.AddOns.AddRange([A("GYN-A02"), A("GYN-A03")]); }), false, User)).Estimate!;
        Assert.Equal(8398m + 1950 + 5750 + 1150, s2.Subtotal);
        var l2 = await svc.FindAsync(s2.Id);
        Assert.Contains(l2.Lines, l => l.Label == "Add-on – IUD insertion and/or removal" && l.Amount == 1150);
        Assert.Contains(l2.Lines, l => l.Label == "Consultant Fee" && l.Amount == 5750);
        Assert.DoesNotContain(l2.Lines, l => l.Label.Contains("NovaSure", StringComparison.OrdinalIgnoreCase));
        var (preview, available, _) = await svc.PreviewAsync(Form(abl, edit: f => f.Inputs.AddOns.Add(A("GYN-A02"))));
        Assert.DoesNotContain(available, a => a.Code == "GYN-A02");
        Assert.Contains(preview.Warnings, w => w.Contains("already included", StringComparison.Ordinal));

        // References: EST-yymmdd-NHI, or the last name in capitals; -2 when needed.
        Assert.Equal("EST-261109-CHARGE", s2.Ref);
        Assert.Equal("EST-261109-TEST", s1.Ref);
        var again = (await svc.SaveAsync(null, Form(colon, "Test"), false, User)).Estimate!;
        Assert.Equal("EST-261109-TEST-2", again.Ref);
        Assert.Equal("EST-261109-OBRIEN", EstimateService.BaseRef(new DateOnly(2026, 11, 9), null, "O'Brien"));
        Assert.Equal("EST-261109-ZZZ0016", EstimateService.BaseRef(new DateOnly(2026, 11, 9), " zzz0016 ", "Smith"));

        // Colpocleisis special self-funding package: $4,000 + $2,000 + $4,000.
        var colp = p["GYN-014"];
        Assert.Equal(10000m, (await svc.SaveAsync(null, Form(colp, "Package", edit: f => { f.Anaesthetic = Anaesthetic.General; f.UseSpecial = true; }), false, User)).Estimate!.Subtotal);

        // Extended time: 50 min planned, 30 scheduled → 20 over → 2 × $90.
        var ext = (await svc.SaveAsync(null, Form(colon, "Case", edit: f => f.PlannedMinutes = 50), false, User)).Estimate!;
        Assert.Equal(4750m + 180, ext.Subtotal);

        // A manual surgeon fee replaces the schedule fee and surgeon add-ons (not itemised).
        var man = (await svc.SaveAsync(null, Form(abl, "Manual", edit: f => { f.SurgeonOverride = 7000; f.Inputs.AddOns.Add(A("GYN-A03")); }), false, User)).Estimate!;
        var lm = await svc.FindAsync(man.Id);
        Assert.Contains(lm.Lines, l => l.Label == "Consultant Fee" && l.Amount == 7000);
        Assert.DoesNotContain(lm.Lines, l => l.Label.StartsWith("Add-on – IUD", StringComparison.Ordinal));

        // Sedation by the consultant: no anaesthetist line or charge.
        var sed = (await svc.SaveAsync(null, Form(colon, "Sedate", edit: f => f.Anaesthetic = Anaesthetic.SedationConsultant), false, User)).Estimate!;
        Assert.DoesNotContain((await svc.FindAsync(sed.Id)).Lines, l => l.Label == "Anaesthetist Fee");

        // Self-funding: 50% deposit due 14 days before surgery; valid 3 months.
        var sf = (await svc.SaveAsync(null, Form(colon, "Selffund"), false, User)).Estimate!;
        Assert.Equal(Math.Round(sf.Subtotal / 2, 2), sf.Deposit);
        Assert.Equal(sf.Subtotal - sf.Deposit, sf.Balance);
        Assert.Equal(new DateOnly(2027, 2, 9), sf.ValidUntil);
    }

    [Fact]
    public void Dates_handle_month_ends_leap_years_and_the_deposit_rule()
    {
        var s = new EstSettings();
        var p = new EstProcedure { Code = "X", Name = "X", PrintTitle = "X", Minutes = 30, Facility = 1000 };
        var ctx = new CalcContext { Procedure = p, Fees = [new EstSurgeonFee { Fee = 500 }] };
        // 30 Nov + 3 months = 28 Feb 2027 (not a leap year) / 29 Feb 2028 (leap year).
        Assert.Equal(new DateOnly(2027, 2, 28), EstimateCalculator.Calculate(new Estimate { EstimateDate = new DateOnly(2026, 11, 30) }, new(), ctx, s).ValidUntil);
        Assert.Equal(new DateOnly(2028, 2, 29), EstimateCalculator.Calculate(new Estimate { EstimateDate = new DateOnly(2027, 11, 30) }, new(), ctx, s).ValidUntil);
        // Deposit due counts calendar days across the April daylight-saving change.
        var r = EstimateCalculator.Calculate(new Estimate { EstimateDate = new DateOnly(2027, 3, 20), ProcedureDate = new DateOnly(2027, 4, 12) }, new(), ctx, s);
        Assert.Equal(new DateOnly(2027, 3, 29), r.DepositDue);
        // Procedure within 14 days: deposit due immediately.
        var soon = EstimateCalculator.Calculate(new Estimate { EstimateDate = new DateOnly(2026, 11, 9), ProcedureDate = new DateOnly(2026, 11, 16) }, new(), ctx, s);
        Assert.Contains(soon.Warnings, w => w.Contains("deposit is due immediately", StringComparison.Ordinal));
        // Multiplier 200% doubles every line.
        Assert.Equal(3000m, EstimateCalculator.Calculate(new Estimate { EstimateDate = new DateOnly(2026, 11, 9), Multiplier = 2, Anaesthetic = Anaesthetic.Local }, new(), ctx, s).Subtotal);
        // Supplied-price charge-out: price × 1.15 × 1.25 × 1.15.
        Assert.Equal(165.31m, s.ChargeOut(100m));
    }

    [Fact]
    public async Task Email_safety_password_rules_recipient_checks_hold_and_stop()
    {
        await using var h = await HostAsync();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<EstimateService>();
        var mail = sp.GetRequiredService<EstimateMail>();
        var mailer = sp.GetRequiredService<TestEstimateMailer>();
        var colon = (await ProcsAsync(sp))["END-001"];
        var s = await svc.SettingsAsync();

        var first = (await svc.SaveAsync(null, Form(colon, "Patient", "ZZZ0016"), true, User)).Estimate!;
        var e = await svc.FindAsync(first.Id);
        Assert.Equal("010170", EstimateMail.PasswordFor(e, EstimateMail.DefaultPassword(s, Recipient.Patient), null));
        Assert.Equal("01011970", EstimateMail.PasswordFor(e, PdfPassword.DobLong, null));
        Assert.Equal("ZZZ0016", EstimateMail.PasswordFor(e, PdfPassword.Nhi, null));
        Assert.Equal(PdfPassword.None, EstimateMail.DefaultPassword(s, Recipient.Insurer));
        Assert.Throws<InvalidOperationException>(() => EstimateMail.PasswordFor(e, PdfPassword.Typed, "abc"));

        // A draft can't be emailed.
        var draft = (await svc.SaveAsync(null, Form(colon, "Draftonly"), false, User)).Estimate!;
        Assert.Contains(await mail.ChecksAsync(draft.Id, Recipient.Patient, "test@example.co.nz"), c => c.Level == CheckLevel.Stop);

        // Same email used for a different patient is flagged.
        var twin = (await svc.SaveAsync(null, Form(colon, "Person", "ZZZ0024", f => { f.FirstName = "Other"; f.DateOfBirth = new DateOnly(1980, 5, 5); }), true, User)).Estimate!;
        Assert.Contains(await mail.ChecksAsync(twin.Id, Recipient.Patient, "test@example.co.nz"), c => c.Level == CheckLevel.Warning && c.Message.Contains("different patient", StringComparison.Ordinal));
        // Insurer: patient's own address is flagged.
        Assert.Contains(await mail.ChecksAsync(twin.Id, Recipient.Insurer, "test@example.co.nz"), c => c.Message.Contains("patient’s own address", StringComparison.Ordinal));

        // Must tick to confirm.
        await Assert.ThrowsAsync<InvalidOperationException>(() => mail.QueueAsync(twin.Id, new SendRequest(Recipient.Patient, "test@example.co.nz", null, null, false), User));

        // The email states the rule, never the password; the PDF carries it.
        var q = await mail.QueueAsync(twin.Id, new SendRequest(Recipient.Patient, "test@example.co.nz", null, null, true), User);
        Assert.True(q.PasswordProtected);
        Assert.Contains("DDMMYY", q.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("050580", q.Body, StringComparison.Ordinal);
        Assert.Equal(h.Clock.GetUtcNow().AddMinutes(10), q.SendAfter);

        // Held: nothing goes before the hold period; stop works.
        Assert.Equal(0, await mail.ProcessDueAsync());
        Assert.Null(await mail.StopAsync(q.Id, User));
        h.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(0, await mail.ProcessDueAsync());
        Assert.Empty(mailer.Sent);

        // Editing an estimate stops any held email for it.
        var q2 = await mail.QueueAsync(twin.Id, new SendRequest(Recipient.Patient, "test@example.co.nz", null, null, true), User);
        var tf = EstimateService.FormOf(await svc.FindAsync(twin.Id));
        tf.Notes = "changed after issue";
        var upd = await svc.SaveAsync(twin.Id, tf, false, User);
        Assert.Equal(1, upd.StoppedEmails);
        Assert.Contains("_updated_", upd.ArchiveMessage, StringComparison.Ordinal);
        Assert.Equal(OutboxStatus.Stopped, (await mail.OutboxAsync(true)).Single(o => o.Id == q2.Id).Status);

        // Sent after the hold; insurer gets no password; attachment content removed once sent.
        var q3 = await mail.QueueAsync(twin.Id, new SendRequest(Recipient.Insurer, "approvals@insurer.example.nz", null, null, true), User);
        Assert.False(q3.PasswordProtected);
        h.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(1, await mail.ProcessDueAsync());
        Assert.Single(mailer.Sent);
        Assert.DoesNotContain("/Encrypt", Encoding.UTF8.GetString(mailer.Sent[0].Attachment), StringComparison.Ordinal);
        var db = sp.GetRequiredService<EstDbContext>();
        Assert.Empty((await db.Outbox.AsNoTracking().SingleAsync(o => o.Id == q3.Id)).AttachmentCipher);
        Assert.NotNull((await svc.FindAsync(twin.Id)).EmailedAt);

        // Outlook draft is marked unsent and the PDF password follows the rule.
        var (eml, name) = await mail.DraftAsync(first.Id, new SendRequest(Recipient.Patient, "test@example.co.nz", PdfPassword.Nhi, null, true), User);
        Assert.StartsWith("X-Unsent: 1", Encoding.UTF8.GetString(eml), StringComparison.Ordinal);
        Assert.EndsWith(".eml", name, StringComparison.Ordinal);

        // A failing server retries, then fails after 5 attempts.
        mailer.Fail = true;
        var q4 = await mail.QueueAsync(twin.Id, new SendRequest(Recipient.Patient, "test@example.co.nz", null, null, true), User);
        for (var i = 0; i < 6; i++) { h.Clock.Advance(TimeSpan.FromMinutes(15)); await mail.ProcessDueAsync(); }
        var failed = await db.Outbox.AsNoTracking().SingleAsync(o => o.Id == q4.Id);
        Assert.Equal(OutboxStatus.Failed, failed.Status);
        Assert.Equal(5, failed.Attempts);
    }

    [Fact]
    public async Task Templates_safe_delete_cancel_duplicate_patient_link_and_incisive()
    {
        await using var h = await HostAsync();
        var patient = await h.PatientAsync(nhi: "ZSC21TN");
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<EstimateService>();
        var cat = sp.GetRequiredService<EstimateCatalogue>();
        var procs = await ProcsAsync(sp);

        var tpls = await svc.TemplatesAsync(true, null);
        Assert.Equal(7, tpls.Count);
        var ct = tpls.Single(t => t.Name.StartsWith("Colonoscopy + biopsies", StringComparison.Ordinal));
        var f = await svc.ApplyTemplateAsync(ct.Id, new EstimateForm { FirstName = "Template", LastName = "User", Nhi = "ZZZ0032" });
        var ts = (await svc.SaveAsync(null, f, false, User)).Estimate!;
        var tl = await svc.FindAsync(ts.Id);
        Assert.Equal(5040m, ts.Subtotal);
        Assert.Contains(tl.Lines, l => l.Label == "Add-on – Biopsies");
        Assert.Equal(2, tl.Lines.Count(l => l.Kind == LineKind.IfRequired));
        var (newId, err) = await svc.SaveAsTemplateAsync(null, "Test – save as template", null, EstimateService.FormOf(tl), User);
        Assert.Null(err);
        var reapplied = await svc.ApplyTemplateAsync(newId!.Value, new EstimateForm());
        Assert.Equal(3, reapplied.Inputs.AddOns.Count);

        // Safe delete: used things refuse (hide instead); unused things go.
        Assert.NotNull(await svc.DeleteTemplateAsync(ct.Id, User));
        Assert.NotNull(await cat.DeleteProcedureAsync(procs["END-001"].Id, User));
        var biopsy = (await cat.AddOnsAsync()).Single(a => a.Code == "END-A01");
        Assert.NotNull(await cat.DeleteAddOnAsync(biopsy.Id, User));
        var spec = (await cat.SpecialtiesAsync()).First();
        var (unusedId, errs) = await cat.SaveProcedureAsync(null, new ProcedureEdit("TST-001", spec.Id, null, "Unused test procedure", "Test", 10, Anaesthetic.Local, 1, null, null, null, null, null, null, true, null), User);
        Assert.Empty(errs);
        Assert.Null(await cat.SaveFeesAsync(unusedId!.Value, [new FeeEdit(null, 1, null, null)], User));
        Assert.Null(await cat.DeleteProcedureAsync(unusedId.Value, User));
        Assert.DoesNotContain(await cat.ProceduresAsync(), p => p.Id == unusedId);
        Assert.NotNull(await cat.DeleteSpecialtyAsync(spec.Id, User));
        // Procedure codes are permanent once used.
        var c = procs["END-001"];
        var (_, codeErr) = await cat.SaveProcedureAsync(c.Id, new ProcedureEdit("END-999", c.SpecialtyId, c.Group, c.Name, c.PrintTitle, c.Minutes, c.DefaultAnaesthetic, c.Facility, c.FacilitySpecial, c.Anaesthetist, c.AnaesthetistSpecial, c.ConsumablesLine, c.IncludedAddOnCodes, c.EffectiveFrom, true, c.Source), User);
        Assert.Contains(codeErr, x => x.Contains("permanent", StringComparison.Ordinal));

        // New estimate from a patient record prefills the details; it shows on the patient.
        var pf = await svc.FromPatientAsync(patient.Id);
        Assert.Equal("ZSC21TN", pf.Nhi);
        Assert.Equal(patient.FamilyName, pf.LastName);
        pf.ProcedureId = procs["GYN-001"].Id;
        var pe = (await svc.SaveAsync(null, pf, true, User)).Estimate!;
        Assert.Equal("EST-261109-ZSC21TN", pe.Ref);
        Assert.Single(await svc.ListAsync(null, null, null, null, null, patient.Id));
        // A returning patient's next estimate carries their funding and insurer.
        var pe2f = EstimateService.FormOf(pe); pe2f.Funding = Funding.Insurance; pe2f.Insurer = "nib"; pe2f.PolicyNumber = "N123";
        await svc.SaveAsync(pe.Id, pe2f, false, User);
        Assert.Equal("nib", (await svc.FromPatientAsync(patient.Id)).Insurer);
        // Linking checks the NHI.
        var other = (await svc.SaveAsync(null, Form(procs["END-001"], "Other", "ZZZ0024"), false, User)).Estimate!;
        Assert.NotNull(await svc.LinkPatientAsync(other.Id, patient.Id, User));

        // Duplicate makes a new draft; cancel needs a reason and stops editing.
        var dup = (await svc.DuplicateAsync(pe.Id, User)).Estimate!;
        Assert.Equal(EstimateStatus.Draft, dup.Status);
        Assert.Equal("EST-261109-ZSC21TN-2", dup.Ref);
        Assert.NotNull(await svc.CancelAsync(dup.Id, null, User));
        Assert.Null(await svc.CancelAsync(dup.Id, "Duplicate", User));
        Assert.NotEmpty((await svc.SaveAsync(dup.Id, EstimateService.FormOf(dup), false, User)).Errors);

        // Incisive: only issued estimates, file named by the pattern, inside the folder.
        var folder = Directory.CreateTempSubdirectory("incisive-").FullName;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendToIncisiveAsync(pe.Id, User));
            var s = await svc.SettingsAsync();
            s.IncisiveFolder = folder;
            Assert.Empty(await cat.SaveSettingsAsync(s, User));
            var path = await svc.SendToIncisiveAsync(pe.Id, User);
            Assert.Equal($"ZSC21TN_{patient.FamilyName.ToUpperInvariant()}_{patient.GivenNames.Replace(' ', '_')}_Estimate_20261109.pdf".Normalize(), Path.GetFileName(path).Normalize());
            await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SendToIncisiveAsync(dup.Id, User));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task Pricing_import_warns_schedules_future_prices_and_round_trips()
    {
        await using var h = await HostAsync();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var imp = sp.GetRequiredService<EstimateImport>();
        var svc = sp.GetRequiredService<EstimateService>();
        var db = sp.GetRequiredService<EstDbContext>();

        // The export imports back unchanged.
        var bytes = await imp.ExportPricingAsync();
        await using (var ms = new MemoryStream(bytes))
        {
            var pv = await imp.PreviewPricingAsync(ms, "round-trip.xlsx");
            Assert.Empty(pv.Errors);
            Assert.Equal(0, pv.Changed);
            Assert.Equal(0, pv.New);
        }

        // Edit the export: a >25% facility change effective in the future, and a blank anaesthetist fee.
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(bytes));
        var ws = wb.Worksheet("Export");
        var row = ws.RowsUsed().First(r => r.Cell(1).GetString() == "END-001").RowNumber();
        var oldFacility = ws.Cell(row, 8).GetValue<decimal>();
        ws.Cell(row, 8).Value = oldFacility * 2;
        ws.Cell(row, 14).Value = new DateTime(2026, 12, 1);
        var row2 = ws.RowsUsed().First(r => r.Cell(1).GetString() == "END-003").RowNumber();
        ws.Cell(row2, 10).Value = "";
        using var edited = new MemoryStream();
        wb.SaveAs(edited);
        edited.Position = 0;
        var pv2 = await imp.PreviewPricingAsync(edited, "edited.xlsx");
        Assert.Empty(pv2.Errors);
        Assert.Contains(pv2.Warnings, w => w.Contains("more than 25%", StringComparison.Ordinal));
        Assert.Contains(pv2.Warnings, w => w.Contains("anaesthetist fee would become blank", StringComparison.Ordinal));
        Assert.True(pv2.Procedures.Single(p => p.Code == "END-001").Scheduled);
        await imp.ApplyPricingAsync(pv2, true, true, false, User);
        // Not yet: still the old price; scheduled change waiting.
        Assert.Equal(oldFacility, (await db.Procedures.AsNoTracking().SingleAsync(p => p.Code == "END-001")).Facility);
        Assert.Contains(await sp.GetRequiredService<EstimateCatalogue>().PriceImpactAsync(), i => i.Code == "END-001" && i.Next == oldFacility * 2);
        // On 1 December it applies.
        h.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.FromHours(13)));
        Assert.Equal(1, await svc.ApplyDuePricesAsync(User));
        Assert.Equal(oldFacility * 2, (await db.Procedures.AsNoTracking().SingleAsync(p => p.Code == "END-001")).Facility);

        // A changed layout stops the import.
        ws.Cell(3, 2).Value = "Something else";
        using var broken = new MemoryStream();
        wb.SaveAs(broken);
        broken.Position = 0;
        Assert.Contains((await imp.PreviewPricingAsync(broken, "broken.xlsx")).Errors, e => e.Contains("Layout has changed", StringComparison.Ordinal));
        // Not a workbook at all.
        Assert.NotEmpty((await imp.PreviewPricingAsync(new MemoryStream("not excel"u8.ToArray()), "x.xlsx")).Errors);

        // Stock: imported from the real list, exported in the same layout.
        Assert.True(await db.Stock.CountAsync() > 50);
        await using var stock = new MemoryStream(await imp.ExportStockAsync());
        var spv = await imp.PreviewStockAsync(stock, "stock-round-trip.xlsx");
        Assert.Empty(spv.Errors);
        Assert.Equal(0, spv.New);
    }

    [Fact]
    public async Task Archived_pdfs_are_encrypted_at_rest_and_audit_is_written()
    {
        await using var h = await HostAsync();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<EstimateService>();
        var colon = (await ProcsAsync(sp))["END-001"];
        var e = (await svc.SaveAsync(null, Form(colon, "Archive", "ZZZ0016"), true, User)).Estimate!;
        var db = sp.GetRequiredService<EstDbContext>();
        var a = await db.Archive.AsNoTracking().SingleAsync(x => x.EstimateId == e.Id);
        Assert.DoesNotContain(e.Ref, Encoding.UTF8.GetString(a.PdfCipher), StringComparison.Ordinal); // TestDocumentCipher scrambles
        var (pdf, name, _) = await svc.ArchivedPdfAsync(a.Id);
        Assert.Contains(e.Ref, Encoding.UTF8.GetString(pdf), StringComparison.Ordinal);
        Assert.StartsWith("Estimate_EST-261109-ZZZ0016_Archive_Test", name, StringComparison.Ordinal);
        var core = sp.GetRequiredService<BrightPAS.Core.Data.PasDbContext>();
        Assert.True(await core.AuditEvents.CountAsync(x => x.EntityType == "Estimate" && x.EntityId == e.Id.ToString()) >= 1);
        Assert.True(await core.AuditEvents.AnyAsync(x => x.EntityType == "EstPricing" && x.Action == "Import"));
    }
    [Fact]
    public async Task Follow_up_statuses_lists_expiry_and_report()
    {
        await using var h = await HostAsync();
        await using var scope = h.Scope();
        var sp = scope.ServiceProvider;
        var svc = sp.GetRequiredService<EstimateService>();
        var fu = sp.GetRequiredService<EstimateFollowUpService>();
        var procs = await ProcsAsync(sp);
        var p = procs["END-001"];

        var draft = (await svc.SaveAsync(null, Form(p, "Draft"), false, User)).Estimate!;
        Assert.Null(draft.FollowUp);
        Assert.Contains("Only an issued", await fu.SetAsync(draft.Id, new(EstFollowUp.Accepted, null, null, null, null, null, null), User));

        var self = (await svc.SaveAsync(null, Form(p, "Self"), true, User)).Estimate!;
        var ins = (await svc.SaveAsync(null, Form(p, "Insured", edit: f => { f.Funding = Funding.Insurance; f.Insurer = "Southern Cross"; f.PolicyNumber = "SX1"; }), true, User)).Estimate!;
        var lost = (await svc.SaveAsync(null, Form(p, "Lost"), true, User)).Estimate!;
        Assert.Equal(EstFollowUp.Waiting, self.FollowUp);

        // Rules.
        Assert.Contains("Say why", await fu.SetAsync(lost.Id, new(EstFollowUp.Declined, null, null, null, null, null, null), User));
        Assert.Contains("only for insured", await fu.SetAsync(self.Id, new(EstFollowUp.ApprovalRequested, null, null, null, null, null, null), User));
        Assert.Contains("approval number", await fu.SetAsync(ins.Id, new(EstFollowUp.Approved, null, null, null, null, null, null), User));
        Assert.Contains("deposit paid", await fu.SetAsync(self.Id, new(EstFollowUp.DepositPaid, null, null, null, 0m, null, null), User));
        Assert.Contains("Choose what happened", await fu.SetAsync(self.Id, new(EstFollowUp.Invoiced, null, null, null, null, null, null), User));

        // Nothing heard: on the chase list after the follow-up days (7).
        Assert.Empty(await fu.ListAsync("chase"));
        h.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(3, (await fu.ListAsync("chase")).Count);
        Assert.Contains("Nothing heard for 8 days", (await fu.ListAsync("chase")).First().Why, StringComparison.Ordinal);

        Assert.Null(await fu.SetAsync(lost.Id, new(EstFollowUp.Declined, "Going public", null, null, null, null, null), User));
        Assert.Null(await fu.SetAsync(ins.Id, new(EstFollowUp.ApprovalRequested, "Sent to Southern Cross", NzTime.Today(h.Clock).AddDays(5), null, null, null, null), User));
        Assert.Null(await fu.SetAsync(self.Id, new(EstFollowUp.Accepted, "Phoned", null, null, null, null, null), User));
        Assert.Null(await fu.SetAsync(self.Id, new(EstFollowUp.DepositPaid, null, null, null, 500m, null, null), User));
        Assert.Empty(await fu.ListAsync("chase")); // the insured one has a date in 5 days; the others are answered
        h.Clock.Advance(TimeSpan.FromDays(5));
        Assert.Equal("Follow-up date " + NzTime.Today(h.Clock).ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture), (await fu.ListAsync("chase")).Single().Why);
        Assert.Null(await fu.SetAsync(ins.Id, new(EstFollowUp.Approved, null, null, "SX-APP-778", null, null, null), User));
        Assert.Equal("SX-APP-778", (await svc.FindAsync(ins.Id)).PriorApproval);
        Assert.Equal(["Waiting", "ApprovalRequested", "Approved"], (await fu.HistoryAsync(ins.Id)).Select(x => x.Status));
        Assert.Equal(2, (await fu.ListAsync("won")).Count);

        // Running out and expired (valid 3 months).
        var validUntil = self.ValidUntil!.Value;
        h.Clock.Advance(TimeSpan.FromDays(validUntil.DayNumber - NzTime.Today(h.Clock).DayNumber - 10));
        Assert.Equal(2, (await fu.ListAsync("expiring")).Count);
        Assert.Null(await fu.SetAsync(self.Id, new(EstFollowUp.Booked, null, null, null, null, null, Guid.NewGuid()), User));
        h.Clock.Advance(TimeSpan.FromDays(11));
        Assert.Equal(1, await fu.ExpireAsync("system")); // the approved insured one; booked and declined stay
        Assert.Equal(0, await fu.ExpireAsync("system"));
        Assert.Equal(EstFollowUp.Expired, (await svc.FindAsync(ins.Id)).FollowUp);
        await fu.MarkInvoicedAsync(self.Id, Guid.NewGuid(), "INV-000123", User);
        Assert.Contains("has been invoiced", await fu.SetAsync(self.Id, new(EstFollowUp.Accepted, null, null, null, null, null, null), User));

        // Report: 3 issued; self won and booked; lost declined; insured expired (accept rate 1 of 3).
        var today = NzTime.Today(h.Clock);
        var rep = await fu.ReportAsync(today.AddYears(-1), today);
        Assert.Equal((3, 1, 1, 1, 1), (rep.Total.Issued, rep.Total.Accepted, rep.Total.Declined, rep.Total.Booked, rep.Total.Expired));
        Assert.Equal(33.3, rep.Total.AcceptRate);
        Assert.Equal(8, rep.Total.AvgDaysToAccept!.Value, 0);
        Assert.Equal(2, rep.ByFunding.Count);
    }
}
