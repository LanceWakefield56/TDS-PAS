using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Medicines;
using BrightPAS.Modules.Prescribing;

namespace BrightPAS.Modules.Tests;

/// <summary>0.31.0 prescribing and barcode medicine checks: GS1 and NZePS id parsing, orders, the barcode check (right patient, right medicine,
/// in date, ordered; Advise vs Enforce), discharge prescriptions (prescriber only, sign, send, print, cancel) and merge.</summary>
public class PrescribingTests
{
    private sealed class Gate(BcmaService bcma) : IMedicineSafetyCheck
    {
        public async Task<MedicineCheckOutcome> CheckAsync(MedicineGiveCheck c, CancellationToken ct)
        {
            var r = await bcma.CheckAsync(c, ct);
            return new(r.Allowed, r.Error, r.ScanId, [.. r.Findings.Select(f => new MedicineCheckNote("Barcode", f.Severity, f.Text))]);
        }
        public Task LinkAsync(Guid checkId, Guid medicationId, CancellationToken ct) => bcma.LinkAsync(checkId, medicationId, ct);
    }

    private static RxSettingsInput Settings(BcmaMode mode, NzepsProvider provider = NzepsProvider.Off) =>
        new(mode, true, provider, provider == NzepsProvider.Nzeps ? "https://nzeps.example.health.nz/fhir" : null, "tds", "secret", "F12345-A", null);

    [Fact]
    public void Gs1_barcodes_and_nzeps_ids_are_read_and_checked()
    {
        var raw = Gs1.Parse("]d201094212345678921727063010AB12\u001d21SER9");
        Assert.NotNull(raw);
        Assert.Equal("09421234567892", raw!.Gtin);
        Assert.Equal(new DateOnly(2027, 6, 30), raw.Expiry);
        Assert.Equal("AB12", raw.Batch);
        Assert.Equal("SER9", raw.Serial);
        var text = Gs1.Parse("(01)09421234567892(17)280200(10)LOT 7");
        Assert.Equal(new DateOnly(2028, 2, 29), text!.Expiry); // day 00 = end of the month (leap year)
        Assert.Equal("LOT 7", text.Batch);
        Assert.Equal("09421234567892", Gs1.Parse("9421234567892")!.Gtin);
        Assert.Null(Gs1.Parse("9421234567891")); // wrong check digit
        Assert.Null(Gs1.Parse("ZSC21TN"));
        for (var i = 0; i < 50; i++) Assert.True(NzepsId.IsValid(NzepsId.New()));
        var id = NzepsId.New();
        var changed = id[..3] + (id[3] == 'A' ? 'B' : 'A') + id[4..];
        Assert.False(NzepsId.IsValid(changed));
    }

    [Fact]
    public async Task Barcode_check_finds_wrong_patient_wrong_medicine_expired_and_counts_orders()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        var other = await h.PatientAsync("Smith", "Jo", "ZAA0024");
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var cat = sp.GetRequiredService<MedicineCatalogue>();
        var (paraId, e1) = await cat.SaveAsync(null, new MedicineEdit("Paracetamol 1 g/100 mL injection", null, "injection", "IV", true, true, [new("paracetamol", 1000, "mg", 100)], ["9421234567892"]), "admin");
        Assert.Empty(e1);
        _ = await cat.SaveAsync(null, new MedicineEdit("Paracetamol 500 mg tablet", null, "tablet", "Oral", true, true, [new("paracetamol", 500, "mg", null)], ["9421234567809"]), "admin");
        var (ondId, _) = await cat.SaveAsync(null, new MedicineEdit("Ondansetron 4 mg/2 mL injection", null, "injection", "IV", true, true, [new("ondansetron", 2, "mg", 1)], ["9421234567816"]), "admin");
        var rx = sp.GetRequiredService<PrescriptionService>();
        var orders = sp.GetRequiredService<OrderService>();
        var bcma = sp.GetRequiredService<BcmaService>();
        var emr = ActivatorUtilities.CreateInstance<EmrService>(sp, new object[] { new IMedicineSafetyCheck[] { new Gate(bcma) } });
        var e = await emr.EpisodeAsync(Guid.NewGuid(), p.Id, "rn");

        // Off (default): nothing checked, nothing kept.
        var (m0, err0, f0) = await emr.GiveCheckedAsync(e.Id, new("Paracetamol 1 g/100 mL injection", "1 g", "IV", null, null, null, null, null, MedicineId: paraId), "rn");
        Assert.Null(err0); Assert.Empty(f0);
        Assert.Equal(0, await sp.GetRequiredService<RxDbContext>().Scans.CountAsync());

        // Orders: as-needed needs what it is for.
        Assert.Contains((await orders.PrescribeAsync(e.Id, new(ondId, null, "4 mg", "IV", OrderKind.AsNeeded, "8-hourly", null, null), "drford")).Errors, x => x.Contains("what an as-needed", StringComparison.Ordinal));
        var (ord, oe) = await orders.PrescribeAsync(e.Id, new(paraId, null, "1 g", "IV", OrderKind.Once, null, null, null), "drford");
        Assert.Empty(oe);

        // Advise: everything right → clear, linked to the order, once-only order then given.
        Assert.Empty(await rx.SaveSettingsAsync(Settings(BcmaMode.Advise), "admin"));
        var good = "]d201094212345678921730123110B7\u001d";
        var (m1, err1, f1) = await emr.GiveCheckedAsync(e.Id, new("Paracetamol 1 g/100 mL injection", "1 g", "IV", null, good, null, null, null, MedicineId: paraId, PatientBarcode: "ZSC21TN"), "rn");
        Assert.Null(err1);
        Assert.DoesNotContain(f1, x => x.Severity != "Info");
        var o1 = (await orders.ForEpisodeAsync(e.Id)).Single(x => x.Id == ord!.Id);
        Assert.Equal("Given", o1.Status); Assert.Equal(1, o1.TimesGiven);
        var scan = await sp.GetRequiredService<RxDbContext>().Scans.SingleAsync(x => x.MedicationId == m1!.Id);
        Assert.Equal("B7", scan.Batch); Assert.Equal(new DateOnly(2030, 12, 31), scan.Expiry); Assert.Equal(BcmaResult.Clear, scan.Result);

        // Advise: wrong patient, wrong medicine, no order – all shown, nothing blocked.
        var (m2, err2, f2) = await emr.GiveCheckedAsync(e.Id, new("Ondansetron 4 mg/2 mL injection", "4 mg", "IV", null, "(01)09421234567892(17)301231", null, null, null, MedicineId: ondId, PatientBarcode: other.Nhi), "rn");
        Assert.Null(err2);
        Assert.Contains(f2, x => x.Severity == "Stop" && x.Text.Contains("belongs to someone else", StringComparison.Ordinal));
        Assert.Contains(f2, x => x.Severity == "Stop" && x.Text.Contains("not Ondansetron", StringComparison.Ordinal));
        Assert.Contains(f2, x => x.Severity == "Warn" && x.Text.Contains("no current order", StringComparison.Ordinal));

        // Enforce: wrong patient cannot be overridden; expired needs a reason; no wristband scan is a stop.
        Assert.Empty(await rx.SaveSettingsAsync(Settings(BcmaMode.Enforce), "admin"));
        var (m3, err3, _) = await emr.GiveCheckedAsync(e.Id, new("Paracetamol 1 g/100 mL injection", "1 g", "IV", null, good, null, null, null, OverrideReason: "sure", MedicineId: paraId, PatientBarcode: other.Mrn), "rn");
        Assert.Null(m3); Assert.Contains("wristband of the patient", err3);
        var expired = "(01)09421234567892(17)200101(10)OLD";
        var (m4, err4, f4) = await emr.GiveCheckedAsync(e.Id, new("Paracetamol 1 g/100 mL injection", "1 g", "IV", null, expired, null, null, null, MedicineId: paraId, PatientBarcode: p.Mrn), "rn");
        Assert.Null(m4); Assert.Contains("give a reason", err4); Assert.Contains(f4, x => x.Text.Contains("expired", StringComparison.Ordinal));
        var (m4b, err4b, _) = await emr.GiveCheckedAsync(e.Id, new("Paracetamol 1 g/100 mL injection", "1 g", "IV", null, expired, null, null, null, OverrideReason: "Only pack in the unit – pharmacist agreed", MedicineId: paraId, PatientBarcode: p.Mrn), "rn");
        Assert.Null(err4b); Assert.NotNull(m4b);
        var (m5, err5, _) = await emr.GiveCheckedAsync(e.Id, new("Paracetamol 1 g/100 mL injection", "1 g", "IV", null, null, null, null, null, MedicineId: paraId), "rn");
        Assert.Null(m5); Assert.NotNull(err5);
        // Same ingredient, different product: a warning only.
        var (m6, err6, f6) = await emr.GiveCheckedAsync(e.Id, new("Paracetamol 1 g/100 mL injection", "1 g", "IV", null, "9421234567809", null, null, null, MedicineId: paraId, PatientBarcode: "ZSC21TN"), "rn");
        Assert.Null(err6); Assert.NotNull(m6);
        Assert.Contains(f6, x => x.Severity == "Warn" && x.Text.Contains("different product", StringComparison.Ordinal));

        // The order given against must be active and the same medicine.
        var (m7, err7, f7) = await emr.GiveCheckedAsync(e.Id, new("Ondansetron 4 mg/2 mL injection", "4 mg", "IV", null, "9421234567816", null, null, null, MedicineId: ondId, PatientBarcode: "ZSC21TN", OrderId: ord!.Id), "rn");
        Assert.Null(m7); Assert.Contains(f7, x => x.Text.Contains("is given", StringComparison.Ordinal));

        var lookup = await bcma.LookupAsync(p.Id, "ZAA0024", "(01)09421234567892(17)200101");
        Assert.False(lookup.PatientMatched); Assert.True(lookup.Expired); Assert.Equal("Paracetamol 1 g/100 mL injection", lookup.Medicine);

        var report = await bcma.ReportAsync(BrightPAS.Core.NzTime.Today(h.Clock).AddDays(-1), BrightPAS.Core.NzTime.Today(h.Clock));
        Assert.Equal(5, report.Given); // m0, m1, m2, m4b, m6
        Assert.Equal(4, report.Checked);
        Assert.Equal(1, report.Overridden);
        Assert.Equal(4, report.Stopped); // m3, m4, m5, m7
        Assert.Equal(4, report.PackScanned);
        Assert.Contains("no longer active", await orders.StopAsync(o1.Id, "x", "drford"));
    }

    [Fact]
    public async Task Prescriptions_need_a_prescriber_are_signed_sent_printed_and_cancelled()
    {
        await using var h = new ModulesHost();
        var p = await h.PatientAsync();
        await using var s = h.Scope();
        var sp = s.ServiceProvider;
        var rx = sp.GetRequiredService<PrescriptionService>();
        var nzeps = sp.GetRequiredService<TestNzeps>();
        var pdf = sp.GetRequiredService<TestPrescriptionPdf>();
        var items = new List<ItemInput> { new(null, "Paracetamol 500 mg tablet", "Take 2 tablets every 6 hours when needed for pain. Max 8 in 24 hours.", 32, "tablet", 0, false) };

        Assert.Contains((await rx.CreateAsync(new(p.Id, null, null, items), "drford")).Errors, x => x.Contains("registered prescriber", StringComparison.Ordinal));
        Assert.Contains("The HPI person number (CPN) looks like 12ABCD.", (await rx.SavePrescriberAsync(null, new("drford", "Dr Kate Ford", "ABC", null, "Medical practitioner", true), "admin")).Errors);
        Assert.Empty((await rx.SavePrescriberAsync(null, new("drford", "Dr Kate Ford", "12abcd", "12345", "Medical practitioner", true), "admin")).Errors);
        Assert.Empty((await rx.SavePrescriberAsync(null, new("drhall", "Dr Ben Hall", "34EFGH", null, "Medical practitioner", true), "admin")).Errors);
        Assert.Contains((await rx.CreateAsync(new(p.Id, null, null, [new(null, "Morphine 10 mg tablet", "Take 1", 10, "tablet", 2, true)]), "drford")).Errors, x => x.Contains("cannot have repeats", StringComparison.Ordinal));

        // Print only: signed, printed with a signature line.
        var (r1, e1) = await rx.CreateAsync(new(p.Id, null, "Post-op pain relief", items), "drford");
        Assert.Empty(e1);
        Assert.Equal("RX000001", r1!.Number);
        Assert.True(NzepsId.IsValid(r1.NzepsId));
        Assert.Contains("Sign the prescription", (await rx.PdfAsync(r1.Id, "rn")).Error);
        Assert.Contains("Only the prescriber who wrote it", (await rx.SignAsync(r1.Id, "drhall")).Error);
        Assert.Equal("Signed", (await rx.SignAsync(r1.Id, "drford")).Status);
        Assert.Contains("already signed", (await rx.SignAsync(r1.Id, "drford")).Error);
        var (bytes, number, _) = await rx.PdfAsync(r1.Id, "rn");
        Assert.NotNull(bytes); Assert.Equal("RX000001", number);
        Assert.True(pdf.Rendered[^1].NeedsSignature); Assert.False(pdf.Rendered[^1].Reprint);
        await rx.PdfAsync(r1.Id, "rn");
        Assert.True(pdf.Rendered[^1].Reprint);
        Assert.Empty(nzeps.Sent);

        // NZePS: refused → Failed, then sent again; FHIR has the NHI, CPN and the prescription id.
        Assert.Contains("Enter the NZePS web address (https) given by Health NZ.", await rx.SaveSettingsAsync(Settings(BcmaMode.Off, NzepsProvider.Nzeps) with { NzepsUrl = "http://x" }, "admin"));
        Assert.Empty(await rx.SaveSettingsAsync(Settings(BcmaMode.Off, NzepsProvider.Nzeps), "admin"));
        var (r2, _) = await rx.CreateAsync(new(p.Id, null, null, items), "drford");
        nzeps.Refuse = "Patient NHI not found";
        var (st2, msg2) = await rx.SignAsync(r2!.Id, "drford");
        Assert.Equal("Failed", st2); Assert.Contains("Patient NHI not found", msg2);
        nzeps.Refuse = null;
        Assert.Equal("Sent", (await rx.ResendAsync(r2.Id, "drford")).Status);
        Assert.Contains("ZSC21TN", nzeps.Sent[0]); Assert.Contains("12ABCD", nzeps.Sent[0]); Assert.Contains(r2.NzepsId, nzeps.Sent[0]);
        await rx.PdfAsync(r2.Id, "rn");
        Assert.False(pdf.Rendered[^1].NeedsSignature); Assert.True(pdf.Rendered[^1].Sent);
        Assert.Contains("Say why", await rx.CancelAsync(r2.Id, " ", "drford"));
        Assert.Null(await rx.CancelAsync(r2.Id, "Wrong strength", "drford"));
        Assert.Equal([r2.NzepsId], nzeps.Cancelled);
        Assert.Contains("cancelled prescription", (await rx.PdfAsync(r2.Id, "rn")).Error);
        var list = await rx.ListAsync(p.Id, null);
        Assert.Equal(2, list.Count);
        Assert.Equal("Dr Kate Ford", list[0].Prescriber);

        // Merge moves prescriptions and orders.
        var other = await h.PatientAsync("Smith", "Jo", "ZAA0024");
        Assert.True(await sp.GetRequiredService<OrderService>().MergeAsync(other.Id, p.Id) >= 2);
        Assert.Equal(2, (await rx.ListAsync(other.Id, null)).Count);
    }
}
