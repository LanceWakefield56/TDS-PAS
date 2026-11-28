using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Modules.Medicines;
using BrightPAS.Modules.Patients;
using BrightPAS.Modules.Reminders;

namespace BrightPAS.Modules.Prescribing;

public sealed record PrescriberInput(string? UserName, string? Name, string? HpiCpn, string? Registration, string? Scope, bool IsActive);
public sealed record ItemInput(Guid? MedicineId, string? Medicine, string? Directions, decimal Quantity, string? Unit, int Repeats, bool Controlled);
public sealed record PrescriptionInput(Guid PatientId, Guid? EpisodeId, string? Note, IReadOnlyList<ItemInput> Items);
public sealed record ItemRow(Guid Id, Guid? MedicineId, string Medicine, string? NzmtId, string Directions, decimal Quantity, string Unit, int Repeats, bool Controlled);
public sealed record PrescriptionRow(Guid Id, string Number, string NzepsId, Guid PatientId, string Patient, string? Nhi, Guid? EpisodeId, string Prescriber, string Status, string? Note,
    DateTimeOffset CreatedAt, DateTimeOffset? SignedAt, DateTimeOffset? SentAt, string? SendReference, string? SendError, DateTimeOffset? CancelledAt, string? CancelReason,
    int TimesPrinted, bool NeedsSignature, IReadOnlyList<ItemRow> Items);
public sealed record PrescriptionPdfModel(string ClinicName, IReadOnlyList<string> ClinicAddress, string? ClinicPhone, string? FacilityHpi, string Number, string NzepsId,
    DateOnly Date, string Patient, DateOnly DateOfBirth, string? Nhi, IReadOnlyList<string> PatientAddress, string Prescriber, string HpiCpn, string? Registration, string Scope,
    IReadOnlyList<ItemRow> Items, bool NeedsSignature, bool Sent, bool Reprint, string? Note);

public interface IPrescriptionPdf
{
    byte[] Render(PrescriptionPdfModel m);
}

/// <summary>The NZ ePrescription Service. Faked in tests; the HTTP client lives in the web project. Layout to be confirmed with Health NZ.</summary>
public interface INzepsApi
{
    Task<(bool Ok, string? Reference, string? Error)> SendAsync(string url, string? clientId, string? key, string fhirJson, CancellationToken ct);
    Task<(bool Ok, string? Error)> CancelAsync(string url, string? clientId, string? key, string nzepsId, string reason, CancellationToken ct);
}

/// <summary>
/// Discharge prescriptions (0.31.0, ADR 0044). The prescriber writes the items (medicine, directions, quantity, repeats), signs, and BrightPAS
/// prints the prescription with its NZePS barcode; when NZePS is connected it is sent there too (so any pharmacy can dispense from the barcode),
/// and a FHIR copy can be written to a folder. Controlled drugs are always printed for the prescriber's own signature.
/// </summary>
public sealed class PrescriptionService(RxDbContext db, BcmaService bcma, PatientsDbContext patients, MedicinesDbContext meds, PasDbContext core, ISecretProtector protector,
    INzepsApi nzeps, IPrescriptionPdf pdf, AuditWriter audit, TimeProvider clock)
{
    private static string? Clip(string? s, int n) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(n, s.Trim().Length)];
    private static readonly string[] Scopes = ["Medical practitioner", "Nurse practitioner", "Dentist", "Designated prescriber"];

    // ------------------------------------------------------------------ settings
    public async Task<IReadOnlyList<string>> SaveSettingsAsync(RxSettingsInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(r.BcmaMode)) errors.Add("Choose how the barcode check works.");
        if (!Enum.IsDefined(r.Provider)) errors.Add("Choose how prescriptions are sent.");
        if (r.Provider == NzepsProvider.Nzeps && (string.IsNullOrWhiteSpace(r.NzepsUrl) || !Uri.TryCreate(r.NzepsUrl.Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps))
            errors.Add("Enter the NZePS web address (https) given by Health NZ.");
        if (!string.IsNullOrWhiteSpace(r.FacilityHpi) && !System.Text.RegularExpressions.Regex.IsMatch(r.FacilityHpi.Trim(), "^F[A-Z0-9]{5}-[A-Z]$")) errors.Add("The HPI facility id looks like F12345-A.");
        if (errors.Count > 0) return errors;
        var s = await bcma.SettingsAsync(ct);
        s.BcmaMode = r.BcmaMode; s.WarnWithoutOrder = r.WarnWithoutOrder; s.Provider = r.Provider; s.NzepsUrl = Clip(r.NzepsUrl, 200); s.NzepsClientId = Clip(r.NzepsClientId, 100);
        if (!string.IsNullOrWhiteSpace(r.NzepsKey)) s.NzepsKeyProtected = protector.Protect(r.NzepsKey.Trim());
        s.FacilityHpi = Clip(r.FacilityHpi?.ToUpperInvariant(), 20); s.ExportFolder = Clip(r.ExportFolder, 260);
        s.UpdatedAt = clock.GetUtcNow(); s.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Settings", "RxSettings", "1", reason: $"Barcode check {r.BcmaMode}; prescriptions {r.Provider}", ct: ct);
        return [];
    }

    // ------------------------------------------------------------------ prescribers
    public async Task<IReadOnlyList<Prescriber>> PrescribersAsync(CancellationToken ct = default) => await db.Prescribers.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    public async Task<Prescriber?> PrescriberForAsync(string user, CancellationToken ct = default) => await db.Prescribers.AsNoTracking().SingleOrDefaultAsync(x => x.UserName == user && x.IsActive, ct);

    public async Task<(Prescriber? Row, IReadOnlyList<string> Errors)> SavePrescriberAsync(Guid? id, PrescriberInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var userName = Clip(r.UserName, 100); var name = Clip(r.Name, 150); var cpn = Clip(r.HpiCpn?.ToUpperInvariant(), 10);
        if (userName is null) errors.Add("Enter the prescriber's BrightPAS user name.");
        if (name is null) errors.Add("Enter the prescriber's name as it is printed.");
        if (cpn is null || !System.Text.RegularExpressions.Regex.IsMatch(cpn, "^[0-9]{2}[A-Z]{4}$")) errors.Add("The HPI person number (CPN) looks like 12ABCD.");
        var scope = Scopes.FirstOrDefault(x => string.Equals(x, r.Scope, StringComparison.OrdinalIgnoreCase));
        if (scope is null) errors.Add("Choose the prescriber's scope.");
        if (userName is not null && await db.Prescribers.AnyAsync(x => x.UserName == userName && x.Id != id, ct)) errors.Add("That user is already a prescriber.");
        if (errors.Count > 0) return (null, errors);
        Prescriber p;
        if (id is { } pid) { p = await db.Prescribers.SingleOrDefaultAsync(x => x.Id == pid, ct) ?? throw new KeyNotFoundException("Prescriber not found."); }
        else db.Prescribers.Add(p = new Prescriber());
        p.UserName = userName!; p.Name = name!; p.HpiCpn = cpn!; p.Registration = Clip(r.Registration, 20); p.Scope = scope!; p.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, id is null ? "Create" : "Update", "Prescriber", p.Id.ToString(), reason: $"{p.Name} ({p.HpiCpn}){(p.IsActive ? "" : " – inactive")}", ct: ct);
        return (p, []);
    }

    // ------------------------------------------------------------------ prescriptions
    public async Task<IReadOnlyList<PrescriptionRow>> ListAsync(Guid? patientId, string? view, CancellationToken ct = default)
    {
        var q = db.Prescriptions.AsNoTracking().Include(x => x.Items).AsQueryable();
        if (patientId is { } pid) q = q.Where(x => x.PatientId == pid);
        if (view == "attention") q = q.Where(x => x.Status == RxStatus.Failed || x.Status == RxStatus.Draft);
        var list = await q.OrderByDescending(x => x.CreatedAt).Take(200).ToListAsync(ct);
        return await RowsAsync(list, ct);
    }

    public async Task<PrescriptionRow?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Prescriptions.AsNoTracking().Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
        return p is null ? null : (await RowsAsync([p], ct))[0];
    }

    private async Task<IReadOnlyList<PrescriptionRow>> RowsAsync(List<Prescription> list, CancellationToken ct)
    {
        var pids = list.Select(x => x.PatientId).Distinct().ToList();
        var people = await patients.Patients.AsNoTracking().Where(x => pids.Contains(x.Id)).Select(x => new { x.Id, x.GivenNames, x.FamilyName, x.Nhi }).ToDictionaryAsync(x => x.Id, ct);
        var prescribers = await db.Prescribers.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return [.. list.Select(p =>
        {
            var who = people.GetValueOrDefault(p.PatientId);
            return new PrescriptionRow(p.Id, p.Number, p.NzepsId, p.PatientId, who is null ? "" : $"{who.FamilyName.ToUpperInvariant()}, {who.GivenNames}", who?.Nhi, p.EpisodeId,
                prescribers.GetValueOrDefault(p.PrescriberId, ""), p.Status.ToString(), p.Note, p.CreatedAt, p.SignedAt, p.SentAt, p.SendReference, p.SendError, p.CancelledAt, p.CancelReason,
                p.TimesPrinted, p.Items.Any(i => i.Controlled), [.. p.Items.Select(ItemView)]);
        })];
    }

    private static ItemRow ItemView(PrescriptionItem i) => new(i.Id, i.MedicineId, i.Medicine, i.NzmtId, i.Directions, i.Quantity, i.Unit, i.Repeats, i.Controlled);

    public async Task<(Prescription? Row, IReadOnlyList<string> Errors)> CreateAsync(PrescriptionInput r, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var prescriber = await PrescriberForAsync(user, ct);
        if (prescriber is null) errors.Add("Only a registered prescriber can write a prescription – ask the administrator to add you under Prescribing › Prescribers.");
        if (!await patients.Patients.AnyAsync(x => x.Id == r.PatientId && x.MergedIntoId == null, ct)) errors.Add("Patient not found.");
        if (r.Items is null || r.Items.Count == 0) errors.Add("Add at least one medicine.");
        if (r.Items is { Count: > 10 }) errors.Add("Up to 10 medicines on one prescription.");
        var items = new List<PrescriptionItem>();
        foreach (var (it, n) in (r.Items ?? []).Select((x, i) => (x, i + 1)))
        {
            var name = Clip(it.Medicine, 150); string? nzmt = null;
            if (it.MedicineId is { } mid)
            {
                var m = await meds.Medicines.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mid && x.IsActive, ct);
                if (m is null) { errors.Add($"Medicine {n}: choose it from the list."); continue; }
                name = m.Name; nzmt = m.NzmtId;
            }
            if (name is null) errors.Add($"Medicine {n}: choose or type the medicine.");
            if (string.IsNullOrWhiteSpace(it.Directions)) errors.Add($"Medicine {n}: write the directions for the patient.");
            if (it.Quantity <= 0 || it.Quantity > 10000) errors.Add($"Medicine {n}: enter the quantity to supply.");
            if (it.Repeats is < 0 or > 11) errors.Add($"Medicine {n}: repeats must be 0 to 11.");
            if (it.Controlled && it.Repeats > 0) errors.Add($"Medicine {n}: controlled drugs cannot have repeats on this prescription.");
            items.Add(new PrescriptionItem
            {
                MedicineId = it.MedicineId,
                Medicine = name ?? "",
                NzmtId = nzmt,
                Directions = Clip(it.Directions, 500) ?? "",
                Quantity = it.Quantity,
                Unit = Clip(it.Unit, 30) ?? "tablet",
                Repeats = it.Repeats,
                Controlled = it.Controlled
            });
        }
        if (errors.Count > 0) return (null, errors);
        var strategy = db.Database.CreateExecutionStrategy();
        Prescription? rx = null;
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var s = await bcma.SettingsAsync(ct);
            rx = new Prescription
            {
                Number = $"RX{s.NextNumber++:D6}",
                NzepsId = NzepsId.New(),
                PatientId = r.PatientId,
                EpisodeId = r.EpisodeId,
                PrescriberId = prescriber!.Id,
                Note = Clip(r.Note, 300),
                CreatedAt = clock.GetUtcNow(),
                CreatedBy = user,
            };
            foreach (var i in items) { i.PrescriptionId = rx.Id; rx.Items.Add(i); }
            db.Prescriptions.Add(rx);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        await audit.WriteAsync(user, "Create", "Prescription", rx!.Id.ToString(), rx.PatientId.ToString(), reason: $"{rx.Number}: {string.Join(", ", items.Select(i => i.Medicine))}", ct: ct);
        return (rx, []);
    }

    /// <summary>The prescriber signs; then it is sent to NZePS (when connected) and a FHIR copy written to the folder (when set).</summary>
    public async Task<(string? Status, string? Error)> SignAsync(Guid id, string user, CancellationToken ct = default)
    {
        var rx = await db.Prescriptions.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rx is null) return (null, "Prescription not found.");
        if (rx.Status != RxStatus.Draft) return (null, "This prescription is already signed.");
        var prescriber = await PrescriberForAsync(user, ct);
        if (prescriber is null || prescriber.Id != rx.PrescriberId) return (null, "Only the prescriber who wrote it can sign it.");
        rx.Status = RxStatus.Signed; rx.SignedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Sign", "Prescription", rx.Id.ToString(), rx.PatientId.ToString(), reason: rx.Number, ct: ct);
        var error = await SendAsync(rx, user, ct);
        return (rx.Status.ToString(), error);
    }

    public async Task<(string? Status, string? Error)> ResendAsync(Guid id, string user, CancellationToken ct = default)
    {
        var rx = await db.Prescriptions.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rx is null) return (null, "Prescription not found.");
        if (rx.Status is not (RxStatus.Failed or RxStatus.Signed)) return (null, "Only a signed prescription that was not sent can be sent again.");
        var error = await SendAsync(rx, user, ct);
        return (rx.Status.ToString(), error);
    }

    private async Task<string?> SendAsync(Prescription rx, string user, CancellationToken ct)
    {
        var s = await bcma.SettingsAsync(ct);
        string? error = null;
        var json = await FhirAsync(rx, s, ct);
        if (s.ExportFolder is { } folder)
        {
            try { Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, $"{rx.Number}-{rx.NzepsId}.json"), json, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { error = $"Could not write the copy to {folder}: {ex.Message}"; }
        }
        switch (s.Provider)
        {
            case NzepsProvider.Simulator:
                rx.Status = RxStatus.Sent; rx.SentAt = clock.GetUtcNow(); rx.SendReference = "SIM-" + rx.NzepsId; rx.SendError = null;
                break;
            case NzepsProvider.Nzeps when s.NzepsUrl is { } url:
                var (ok, reference, err) = await nzeps.SendAsync(url, s.NzepsClientId, protector.Unprotect(s.NzepsKeyProtected), json, ct);
                if (ok) { rx.Status = RxStatus.Sent; rx.SentAt = clock.GetUtcNow(); rx.SendReference = Clip(reference, 100); rx.SendError = null; }
                else { rx.Status = RxStatus.Failed; rx.SendError = Clip(err ?? "NZePS did not accept the prescription.", 500); error = "Not sent to NZePS – print it for the patient, or send again. " + rx.SendError; }
                break;
        }
        await db.SaveChangesAsync(ct);
        if (rx.Status is RxStatus.Sent or RxStatus.Failed)
            await audit.WriteAsync(user, rx.Status == RxStatus.Sent ? "Send" : "SendFailed", "Prescription", rx.Id.ToString(), rx.PatientId.ToString(), reason: $"{rx.Number} {s.Provider}{(rx.SendError is null ? "" : ": " + rx.SendError)}", ct: ct);
        return error;
    }

    public async Task<string?> CancelAsync(Guid id, string? reason, string user, CancellationToken ct = default)
    {
        var rx = await db.Prescriptions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rx is null) return "Prescription not found.";
        if (rx.Status == RxStatus.Cancelled) return "Already cancelled.";
        if (string.IsNullOrWhiteSpace(reason)) return "Say why the prescription is cancelled.";
        var prescriber = await PrescriberForAsync(user, ct);
        if (prescriber is null) return "Only a prescriber can cancel a prescription.";
        if (rx.Status == RxStatus.Sent)
        {
            var s = await bcma.SettingsAsync(ct);
            if (s.Provider == NzepsProvider.Nzeps && s.NzepsUrl is { } url)
            {
                var (ok, err) = await nzeps.CancelAsync(url, s.NzepsClientId, protector.Unprotect(s.NzepsKeyProtected), rx.NzepsId, reason.Trim(), ct);
                if (!ok) return "NZePS did not cancel it (it may already be dispensed): " + err;
            }
        }
        rx.Status = RxStatus.Cancelled; rx.CancelledAt = clock.GetUtcNow(); rx.CancelledBy = user; rx.CancelReason = Clip(reason, 300);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Cancel", "Prescription", rx.Id.ToString(), rx.PatientId.ToString(), reason: $"{rx.Number}: {rx.CancelReason}", ct: ct);
        return null;
    }

    public async Task<(byte[]? Pdf, string? Number, string? Error)> PdfAsync(Guid id, string user, CancellationToken ct = default)
    {
        var rx = await db.Prescriptions.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (rx is null) return (null, null, "Prescription not found.");
        if (rx.Status is RxStatus.Draft or RxStatus.Cancelled) return (null, null, rx.Status == RxStatus.Draft ? "Sign the prescription before printing it." : "A cancelled prescription cannot be printed.");
        var s = await bcma.SettingsAsync(ct);
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == rx.PatientId, ct);
        var pr = await db.Prescribers.AsNoTracking().SingleAsync(x => x.Id == rx.PrescriberId, ct);
        var org = await core.Organisations.AsNoTracking().Select(o => o.Name).FirstOrDefaultAsync(ct) ?? "Tasman Day Surgery";
        var site = await core.Sites.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).FirstOrDefaultAsync(ct);
        static IReadOnlyList<string> Lines(params string?[] v) => [.. v.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())];
        var cityLine = string.Join(" ", new[] { p.City, p.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var siteCity = site is null ? null : string.Join(" ", new[] { site.City, site.Postcode }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var model = new PrescriptionPdfModel(org, site is null ? [] : Lines(site.AddressLine1, site.AddressLine2, site.Suburb, siteCity), site?.Phone, s.FacilityHpi, rx.Number, rx.NzepsId,
            NzTime.NzDate(rx.SignedAt ?? rx.CreatedAt), $"{p.GivenNames} {p.FamilyName}", p.DateOfBirth, p.Nhi, Lines(p.AddressLine1, p.AddressLine2, p.Suburb, cityLine),
            pr.Name, pr.HpiCpn, pr.Registration, pr.Scope, [.. rx.Items.Select(ItemView)], rx.Items.Any(i => i.Controlled) || rx.Status != RxStatus.Sent, rx.Status == RxStatus.Sent,
            rx.TimesPrinted > 0, rx.Note);
        rx.TimesPrinted++;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Print", "Prescription", rx.Id.ToString(), rx.PatientId.ToString(), reason: $"{rx.Number}{(model.Reprint ? " (reprint)" : "")}", ct: ct);
        return (pdf.Render(model), rx.Number, null);
    }

    /// <summary>The prescription as a FHIR R4 bundle (Patient, Practitioner, MedicationRequest per item sharing the NZePS id as group identifier).</summary>
    public async Task<string> FhirAsync(Prescription rx, RxSettings s, CancellationToken ct)
    {
        var p = await patients.Patients.AsNoTracking().SingleAsync(x => x.Id == rx.PatientId, ct);
        var pr = await db.Prescribers.AsNoTracking().SingleAsync(x => x.Id == rx.PrescriberId, ct);
        var entries = new JsonArray
        {
            new JsonObject { ["fullUrl"] = "urn:uuid:" + p.Id, ["resource"] = new JsonObject
            {
                ["resourceType"] = "Patient",
                ["identifier"] = new JsonArray(new JsonObject { ["system"] = "https://standards.digital.health.nz/ns/nhi-id", ["value"] = p.Nhi ?? "" }),
                ["name"] = new JsonArray(new JsonObject { ["family"] = p.FamilyName, ["given"] = new JsonArray(p.GivenNames) }),
                ["birthDate"] = p.DateOfBirth.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            } },
            new JsonObject { ["fullUrl"] = "urn:uuid:" + pr.Id, ["resource"] = new JsonObject
            {
                ["resourceType"] = "Practitioner",
                ["identifier"] = new JsonArray(new JsonObject { ["system"] = "https://standards.digital.health.nz/ns/hpi-person-id", ["value"] = pr.HpiCpn }),
                ["name"] = new JsonArray(new JsonObject { ["text"] = pr.Name }),
            } },
        };
        foreach (var i in rx.Items)
        {
            var med = new JsonObject { ["text"] = i.Medicine };
            if (i.NzmtId is not null) med["coding"] = new JsonArray(new JsonObject { ["system"] = "http://nzmt.org.nz", ["code"] = i.NzmtId, ["display"] = i.Medicine });
            var mr = new JsonObject
            {
                ["resourceType"] = "MedicationRequest",
                ["status"] = "active",
                ["intent"] = "order",
                ["groupIdentifier"] = new JsonObject { ["system"] = "https://standards.digital.health.nz/ns/nzeps-prescription-id", ["value"] = rx.NzepsId },
                ["medicationCodeableConcept"] = med,
                ["subject"] = new JsonObject { ["reference"] = "urn:uuid:" + p.Id },
                ["requester"] = new JsonObject { ["reference"] = "urn:uuid:" + pr.Id },
                ["authoredOn"] = (rx.SignedAt ?? rx.CreatedAt).ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                ["dosageInstruction"] = new JsonArray(new JsonObject { ["text"] = i.Directions }),
                ["dispenseRequest"] = new JsonObject
                {
                    ["numberOfRepeatsAllowed"] = i.Repeats,
                    ["quantity"] = new JsonObject { ["value"] = i.Quantity, ["unit"] = i.Unit },
                },
            };
            if (s.FacilityHpi is not null) mr["performer"] = new JsonObject { ["identifier"] = new JsonObject { ["system"] = "https://standards.digital.health.nz/ns/hpi-facility-id", ["value"] = s.FacilityHpi } };
            entries.Add(new JsonObject { ["fullUrl"] = "urn:uuid:" + i.Id, ["resource"] = mr });
        }
        var bundle = new JsonObject { ["resourceType"] = "Bundle", ["type"] = "collection", ["identifier"] = new JsonObject { ["value"] = rx.NzepsId }, ["entry"] = entries };
        return bundle.ToJsonString(new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true });
    }
}
