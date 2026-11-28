using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Modules;
using BrightPAS.Modules.Emr;
using BrightPAS.Modules.Medicines;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Prescribing;

public sealed record OrderInput(Guid? MedicineId, string? Medicine, string? Dose, string? Route, OrderKind Kind, string? Frequency, string? Indication, string? Notes);
public sealed record OrderRow(Guid Id, Guid? MedicineId, string Medicine, string Dose, string Route, string Kind, string? Frequency, string? Indication, string? Notes, string Status,
    DateTimeOffset PrescribedAt, string PrescribedBy, DateTimeOffset? StoppedAt, string? StoppedBy, string? StopReason, int TimesGiven, DateTimeOffset? LastGivenAt);

/// <summary>Medicine orders on the episode's chart (0.31.0, ADR 0044): written by a prescriber, given by a nurse, stopped with a reason.</summary>
public sealed class OrderService(RxDbContext db, EmrDbContext emr, MedicinesDbContext meds, AuditWriter audit, TimeProvider clock)
{
    private static string? Clip(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(n, s.Trim().Length)];

    public static OrderRow Row(MedOrder o) => new(o.Id, o.MedicineId, o.Medicine, o.Dose, o.Route, o.Kind.ToString(), o.Frequency, o.Indication, o.Notes, o.Status.ToString(),
        o.PrescribedAt, o.PrescribedBy, o.StoppedAt, o.StoppedBy, o.StopReason, o.TimesGiven, o.LastGivenAt);

    public async Task<IReadOnlyList<OrderRow>> ForEpisodeAsync(Guid episodeId, CancellationToken ct = default) =>
        [.. (await db.Orders.AsNoTracking().Where(o => o.EpisodeId == episodeId).ToListAsync(ct))
            .OrderBy(o => o.Status == OrderStatus.Active ? 0 : 1).ThenByDescending(o => o.PrescribedAt).Select(Row)];

    public async Task<(MedOrder? Order, IReadOnlyList<string> Errors)> PrescribeAsync(Guid episodeId, OrderInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var e = await emr.Episodes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == episodeId, ct);
        if (e is null) return (null, ["Episode not found."]);
        if (e.Stage is EpisodeStage.Discharged or EpisodeStage.Transferred) errors.Add("The patient has left – write a discharge prescription instead.");
        var name = Clip(r.Medicine, 150);
        if (r.MedicineId is { } mid)
        {
            var m = await meds.Medicines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mid && x.IsActive, ct);
            if (m is null) errors.Add("Choose the medicine from the list.");
            else name = m.Name;
        }
        if (name is null) errors.Add("Choose or type the medicine.");
        if (string.IsNullOrWhiteSpace(r.Dose)) errors.Add("Enter the dose (e.g. 1 g).");
        if (string.IsNullOrWhiteSpace(r.Route)) errors.Add("Enter the route.");
        if (r.Kind is OrderKind.Regular or OrderKind.AsNeeded && string.IsNullOrWhiteSpace(r.Frequency)) errors.Add("Say how often (e.g. 6-hourly, or every 4 h – max 4 doses in 24 h).");
        if (r.Kind == OrderKind.AsNeeded && string.IsNullOrWhiteSpace(r.Indication)) errors.Add("Say what an as-needed medicine is for (e.g. pain, nausea).");
        if (!Enum.IsDefined(r.Kind)) errors.Add("Choose once, regular or as needed.");
        if (errors.Count > 0) return (null, errors);
        var o = new MedOrder
        {
            PatientId = e!.PatientId,
            EpisodeId = e.Id,
            MedicineId = r.MedicineId,
            Medicine = name!,
            Dose = Clip(r.Dose, 60)!,
            Route = Clip(r.Route, 30)!,
            Kind = r.Kind,
            Frequency = Clip(r.Frequency, 100),
            Indication = Clip(r.Indication, 150),
            Notes = Clip(r.Notes, 300),
            PrescribedAt = clock.GetUtcNow(),
            PrescribedBy = user,
        };
        db.Orders.Add(o);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Prescribe", "MedOrder", o.Id.ToString(), o.PatientId.ToString(), reason: $"{o.Medicine} {o.Dose} {o.Route} ({o.Kind})", ct: ct);
        return (o, []);
    }

    public async Task<string?> StopAsync(Guid id, string? reason, string user, CancellationToken ct = default)
    {
        var o = await db.Orders.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return "Order not found.";
        if (o.Status != OrderStatus.Active) return "The order is no longer active.";
        if (string.IsNullOrWhiteSpace(reason)) return "Say why the order is stopped.";
        o.Status = OrderStatus.Stopped; o.StoppedAt = clock.GetUtcNow(); o.StoppedBy = user; o.StopReason = Clip(reason, 300);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Stop", "MedOrder", o.Id.ToString(), o.PatientId.ToString(), reason: o.StopReason, ct: ct);
        return null;
    }

    public async Task<int> MergeAsync(Guid keep, Guid gone, CancellationToken ct = default)
    {
        var n = await db.Orders.Where(x => x.PatientId == gone).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, keep), ct);
        n += await db.Scans.Where(x => x.PatientId == gone).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, keep), ct);
        n += await db.Prescriptions.Where(x => x.PatientId == gone).ExecuteUpdateAsync(s => s.SetProperty(x => x.PatientId, keep), ct);
        return n;
    }
}

public sealed record BcmaNote(string Severity, string Text);
public sealed record BcmaOutcome(bool Allowed, string? Error, Guid? ScanId, IReadOnlyList<BcmaNote> Findings);
public sealed record ScanLookup(bool PatientMatched, string? PatientText, Gs1Pack? Pack, string? Medicine, Guid? MedicineId, bool Expired, string? Message);
public sealed record BcmaReport(DateOnly From, DateOnly To, int Given, int Checked, int PatientScanned, int PackScanned, int Stopped, int Overridden, int Warnings,
    IReadOnlyList<BcmaReportRow> Exceptions);
public sealed record BcmaReportRow(Guid Id, DateTimeOffset At, string User, string Medicine, string Result, string? OverrideReason, string Findings);
public sealed record RxSettingsInput(BcmaMode BcmaMode, bool WarnWithoutOrder, NzepsProvider Provider, string? NzepsUrl, string? NzepsClientId, string? NzepsKey, string? FacilityHpi, string? ExportFolder);

/// <summary>
/// Barcode medicine administration (0.31.0, ADR 0044). Runs inside the clinical record's medicine checks before a medicine is charted:
/// right patient (wristband = NHI or MRN), right medicine (pack GTIN → the medicine chosen, or one with the same ingredients), in date (GS1 expiry),
/// and a current order when one is expected. Every check is kept with what was scanned. Advise: shown only. Enforce: a Stop needs a reason, and a
/// wristband for another patient cannot be overridden.
/// </summary>
public sealed class BcmaService(RxDbContext db, PatientsDbContext patients, MedicinesDbContext meds, EmrDbContext emr, AuditWriter audit, TimeProvider clock)
{
    public async Task<RxSettings> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.Settings.SingleOrDefaultAsync(ct);
        if (s is not null) return s;
        db.Settings.Add(s = new RxSettings());
        await db.SaveChangesAsync(ct);
        return s;
    }

    private static bool IsPatient(Patient p, string scanned)
    {
        var v = scanned.Trim();
        return string.Equals(v, p.Nhi, StringComparison.OrdinalIgnoreCase) || string.Equals(v, p.Mrn, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>For the screen as the nurse scans: whose wristband it is and what the pack is, before pressing Record.</summary>
    public async Task<ScanLookup> LookupAsync(Guid patientId, string? wristband, string? pack, CancellationToken ct = default)
    {
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == patientId, ct);
        var matched = !string.IsNullOrWhiteSpace(wristband) && IsPatient(p, wristband);
        string? who = string.IsNullOrWhiteSpace(wristband) ? null : matched ? $"{p.GivenNames} {p.FamilyName} – right patient" : "NOT this patient – check the wristband";
        var g = Gs1.Parse(pack);
        Medicine? m = null;
        if (g is not null) m = await ByGtinAsync(g.Gtin, ct);
        var expired = g?.Expiry is { } exp && exp < NzTime.Today(clock);
        var msg = pack is null or "" ? null : g is null ? "Not a medicine barcode BrightPAS can read." : m is null ? $"Barcode {g.Gtin} is not in the medicines list." : null;
        return new ScanLookup(matched, who, g, m?.Name, m?.Id, expired, msg);
    }

    private async Task<Medicine?> ByGtinAsync(string gtin, CancellationToken ct)
    {
        var id = await meds.Gtins.AsNoTracking().Where(x => x.Gtin == gtin).Select(x => (Guid?)x.MedicineId).FirstOrDefaultAsync(ct);
        return id is null ? null : await meds.Medicines.AsNoTracking().Include(x => x.Ingredients).SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    private async Task<HashSet<Guid>> IngredientsAsync(Guid? medicineId, CancellationToken ct) => medicineId is null ? []
        : [.. await meds.MedicineIngredients.AsNoTracking().Where(x => x.MedicineId == medicineId).Select(x => x.IngredientId).ToListAsync(ct)];

    /// <summary>Same medicine: same id, the same ingredients, or (without the list) the same name.</summary>
    private async Task<(bool Same, bool SameProduct)> SameAsync(Guid? aId, string aName, Guid? bId, string bName, CancellationToken ct)
    {
        if (aId is not null && aId == bId) return (true, true);
        if (aId is not null && bId is not null)
        {
            var a = await IngredientsAsync(aId, ct); var b = await IngredientsAsync(bId, ct);
            return (a.Count > 0 && a.SetEquals(b), false);
        }
        var same = string.Equals(aName.Trim(), bName.Trim(), StringComparison.OrdinalIgnoreCase)
            || aName.Contains(bName.Trim(), StringComparison.OrdinalIgnoreCase) || bName.Contains(aName.Trim(), StringComparison.OrdinalIgnoreCase);
        return (same, same);
    }

    public async Task<BcmaOutcome> CheckAsync(MedicineGiveCheck c, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        if (s.BcmaMode == BcmaMode.Off && c.OrderId is null) return new(true, null, null, []);
        var enforce = s.BcmaMode == BcmaMode.Enforce;
        var notes = new List<BcmaNote>();
        void Add(string sev, string text) => notes.Add(new(sev, text));
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == c.PatientId, ct);
        var scan = new BcmaScan { PatientId = c.PatientId, EpisodeId = c.EpisodeId, Medicine = c.Medicine, At = clock.GetUtcNow(), User = c.User, Mode = s.BcmaMode };
        var noOverride = false;

        if (s.BcmaMode != BcmaMode.Off)
        {
            // Right patient
            if (string.IsNullOrWhiteSpace(c.PatientBarcode)) Add(enforce ? "Stop" : "Warn", "The wristband was not scanned.");
            else
            {
                scan.PatientScanned = true;
                scan.PatientMatched = IsPatient(p, c.PatientBarcode);
                if (scan.PatientMatched == true) Add("Info", "Wristband scanned – right patient.");
                else { Add("Stop", "The wristband scanned belongs to someone else – check you have the right patient."); noOverride = true; }
            }
            // Right medicine, in date
            var g = Gs1.Parse(c.Barcode);
            if (string.IsNullOrWhiteSpace(c.Barcode)) Add("Warn", "The medicine pack was not scanned.");
            else if (g is null) Add("Warn", "The pack barcode could not be read – check the medicine by eye.");
            else
            {
                scan.PackScanned = true; scan.Gtin = g.Gtin; scan.Batch = g.Batch; scan.Expiry = g.Expiry; scan.Serial = g.Serial;
                var m = await ByGtinAsync(g.Gtin, ct);
                if (m is null) Add("Warn", $"The pack barcode ({g.Gtin}) is not in the medicines list – check the medicine by eye, and add the barcode to the list.");
                else
                {
                    var (same, product) = await SameAsync(m.Id, m.Name, c.MedicineId, c.Medicine, ct);
                    scan.PackMatched = same;
                    if (!same) Add("Stop", $"The pack scanned is {m.Name}, not {c.Medicine}.");
                    else if (!product) Add("Warn", $"The pack scanned is {m.Name} – same medicine, different product or strength; check the dose.");
                    else Add("Info", $"Pack scanned – {m.Name}.");
                }
                if (g.Expiry is { } exp && exp < NzTime.NzDate(c.At)) Add("Stop", $"The pack expired on {exp:d MMM yyyy} – do not use it.");
            }
        }

        // Current order
        MedOrder? order = null;
        if (c.OrderId is { } oid)
        {
            order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == oid && x.EpisodeId == c.EpisodeId, ct);
            if (order is null) Add("Stop", "The order chosen is not on this patient's chart.");
            else if (order.Status != OrderStatus.Active) Add("Stop", $"The order for {order.Medicine} is {order.Status.ToString().ToLowerInvariant()}.");
            else if (!(await SameAsync(order.MedicineId, order.Medicine, c.MedicineId, c.Medicine, ct)).Same) Add("Stop", $"The order is for {order.Medicine}, not {c.Medicine}.");
            else Add("Info", $"Given against the order: {order.Medicine} {order.Dose} {order.Route}.");
        }
        else if (s.BcmaMode != BcmaMode.Off)
        {
            var active = await db.Orders.AsNoTracking().Where(x => x.EpisodeId == c.EpisodeId && x.Status == OrderStatus.Active).ToListAsync(ct);
            foreach (var o in active)
                if ((await SameAsync(o.MedicineId, o.Medicine, c.MedicineId, c.Medicine, ct)).Same) { order = o; break; }
            if (order is not null) Add("Info", $"Matches the order: {order.Medicine} {order.Dose} {order.Route}.");
            else if (s.WarnWithoutOrder) Add("Warn", "There is no current order for this medicine on the chart.");
        }
        scan.OrderId = order?.Id;

        var stops = notes.Where(n => n.Severity == "Stop").ToList();
        scan.Findings = string.Join('\n', notes.Select(n => $"{n.Severity}: {n.Text}"));
        bool allowed; string? error = null;
        if (stops.Count == 0) { allowed = true; scan.Result = notes.Any(n => n.Severity == "Warn") ? BcmaResult.Warnings : BcmaResult.Clear; }
        else if (!enforce && c.OrderId is null) { allowed = true; scan.Result = BcmaResult.Warnings; }
        else if (noOverride) { allowed = false; error = "Scan the wristband of the patient in front of you."; scan.Result = BcmaResult.Stopped; }
        else if (string.IsNullOrWhiteSpace(c.OverrideReason)) { allowed = false; error = "The barcode check stopped this – give a reason to go ahead."; scan.Result = BcmaResult.Stopped; }
        else { allowed = true; scan.Result = BcmaResult.Overridden; scan.OverrideReason = c.OverrideReason.Trim()[..Math.Min(500, c.OverrideReason.Trim().Length)]; }
        db.Scans.Add(scan);
        await db.SaveChangesAsync(ct);
        if (scan.Result is BcmaResult.Overridden or BcmaResult.Stopped)
            await audit.WriteAsync(c.User, scan.Result == BcmaResult.Overridden ? "Override" : "Stopped", "BcmaScan", scan.Id.ToString(), c.PatientId.ToString(), reason: $"{c.Medicine}: {string.Join("; ", stops.Select(x => x.Text))}{(scan.OverrideReason is null ? "" : " – reason: " + scan.OverrideReason)}", ct: ct);
        return new(allowed, error, allowed ? scan.Id : null, notes);
    }

    /// <summary>Links the check to the charted medicine and counts the dose against its order (a once-only order is then given).</summary>
    public async Task LinkAsync(Guid scanId, Guid medicationId, CancellationToken ct = default)
    {
        var scan = await db.Scans.SingleOrDefaultAsync(x => x.Id == scanId, ct);
        if (scan is null) return;
        scan.MedicationId = medicationId;
        if (scan.OrderId is { } oid && await db.Orders.SingleOrDefaultAsync(x => x.Id == oid, ct) is { } o)
        {
            o.TimesGiven++; o.LastGivenAt = scan.At;
            if (o.Kind == OrderKind.Once && o.Status == OrderStatus.Active) o.Status = OrderStatus.Given;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Without BCMA a medicine given against an order still counts against it.</summary>
    public async Task CountOrderAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default)
    {
        if (await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct) is not { } o) return;
        o.TimesGiven++; o.LastGivenAt = at;
        if (o.Kind == OrderKind.Once && o.Status == OrderStatus.Active) o.Status = OrderStatus.Given;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ResultsForAsync(IReadOnlyCollection<Guid> medicationIds, CancellationToken ct = default) =>
        (await db.Scans.AsNoTracking().Where(x => x.MedicationId != null && medicationIds.Contains(x.MedicationId.Value)).Select(x => new { x.MedicationId, x.Result, x.PatientScanned, x.PackScanned }).ToListAsync(ct))
            .GroupBy(x => x.MedicationId!.Value).ToDictionary(g => g.Key, g => { var x = g.First(); return $"{x.Result}{(x.PatientScanned ? "" : " · wristband not scanned")}{(x.PackScanned ? "" : " · pack not scanned")}"; });

    /// <summary>How often wristbands and packs were scanned, and every stop and override, for the medicines charted in the period.</summary>
    public async Task<BcmaReport> ReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var start = NzTime.StartOfDay(from); var end = NzTime.StartOfDay(to.AddDays(1));
        var given = await emr.Medications.AsNoTracking().CountAsync(x => x.GivenAt >= start && x.GivenAt < end && x.Source == "Manual", ct);
        var scans = (await db.Scans.AsNoTracking().Where(x => x.At >= start && x.At < end).ToListAsync(ct)).Where(x => x.Mode != BcmaMode.Off).ToList();
        var charted = scans.Where(x => x.MedicationId != null).ToList();
        var exceptions = scans.Where(x => x.Result is BcmaResult.Stopped or BcmaResult.Overridden).OrderByDescending(x => x.At).Take(200)
            .Select(x => new BcmaReportRow(x.Id, x.At, x.User, x.Medicine, x.Result.ToString(), x.OverrideReason, x.Findings)).ToList();
        return new BcmaReport(from, to, given, charted.Count, charted.Count(x => x.PatientScanned), charted.Count(x => x.PackScanned),
            scans.Count(x => x.Result == BcmaResult.Stopped), charted.Count(x => x.Result == BcmaResult.Overridden), charted.Count(x => x.Result == BcmaResult.Warnings), exceptions);
    }
}
