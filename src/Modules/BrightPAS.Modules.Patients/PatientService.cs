using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Patients;

namespace BrightPAS.Modules.Patients;

public sealed record PatientRegistration(
    string FamilyName, string GivenNames, DateOnly DateOfBirth, BirthSex Sex,
    string? Nhi = null, string? PreferredName = null, string? Gender = null,
    string? MobilePhone = null, string? HomePhone = null, string? Email = null,
    string? AddressLine1 = null, string? AddressLine2 = null, string? Suburb = null, string? City = null, string? Postcode = null,
    IReadOnlyList<string>? EthnicityCodes = null, string? GpPracticeHpiFacilityId = null,
    /// <summary>Set when staff have checked the possible duplicates and confirmed this is a different person.</summary>
    bool ConfirmedNotDuplicate = false);

/// <summary>Contact and GP details that reception and nurses keep up to date. Identity (name, date of birth, NHI) changes go through a separate, stricter process.</summary>
public sealed record ContactUpdate(string? PreferredName, string? MobilePhone, string? HomePhone, string? Email, string? AddressLine1, string? AddressLine2,
    string? Suburb, string? City, string? Postcode, string? GpPracticeHpiFacilityId);

public sealed record PossibleDuplicate(Guid Id, string Mrn, string? Nhi, string FamilyName, string GivenNames, DateOnly DateOfBirth);

public sealed record RegistrationResult(Patient? Patient, IReadOnlyList<string> Errors, IReadOnlyList<PossibleDuplicate> PossibleDuplicates)
{
    public bool Succeeded => Patient is not null;
}

/// <summary>Patient register: validated registration, duplicate checks, search and alerts, all audited.</summary>
public sealed class PatientService(PatientsDbContext db, AuditWriter audit, TimeProvider clock)
{
    public const int MaxEthnicities = 6;

    public async Task<RegistrationResult> RegisterAsync(PatientRegistration r, string userId, CancellationToken ct = default)
    {
        var errors = Validate(r, out var nhi);
        if (errors.Count > 0) return new(null, errors, []);

        if (nhi is not null && await db.Patients.AnyAsync(p => p.Nhi == nhi, ct))
            return new(null, [$"A patient with NHI {nhi} is already registered."], await DuplicatesByNhiAsync(nhi, ct));

        var family = SearchKey(r.FamilyName);
        var given = SearchKey(r.GivenNames);
        var candidates = await db.Patients.Where(p => p.MergedIntoId == null)
            .Where(p => p.DateOfBirth == r.DateOfBirth && (p.FamilyNameSearch == family || p.GivenNamesSearch == given))
            .Select(p => new PossibleDuplicate(p.Id, p.Mrn, p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth))
            .ToListAsync(ct);
        if (candidates.Count > 0 && !r.ConfirmedNotDuplicate)
            return new(null, ["Possible duplicate patient(s) found. Check them, then confirm this is a different person."], candidates);

        var patient = new Patient
        {
            Mrn = await NextMrnAsync(ct),
            Nhi = nhi,
            FamilyName = r.FamilyName.Trim(), GivenNames = r.GivenNames.Trim(), PreferredName = Clean(r.PreferredName),
            FamilyNameSearch = family, GivenNamesSearch = given,
            DateOfBirth = r.DateOfBirth, Sex = r.Sex, Gender = Clean(r.Gender),
            MobilePhone = Clean(r.MobilePhone), HomePhone = Clean(r.HomePhone), Email = Clean(r.Email),
            AddressLine1 = Clean(r.AddressLine1), AddressLine2 = Clean(r.AddressLine2), Suburb = Clean(r.Suburb),
            City = Clean(r.City), Postcode = Clean(r.Postcode), GpPracticeHpiFacilityId = Clean(r.GpPracticeHpiFacilityId),
            IsTestPatient = nhi?.StartsWith('Z') == true,
            CreatedAt = clock.GetUtcNow(), CreatedBy = userId
        };
        patient.Ethnicities.AddRange((r.EthnicityCodes ?? []).Select((c, i) => new PatientEthnicity { PatientId = patient.Id, Order = i + 1, Code = c.Trim() }));

        db.Patients.Add(patient);
        for (var attempt = 1; ; attempt++)
        {
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateConcurrencyException) when (attempt < 5)
            {
                // Another registration took the same MRN number at the same moment: take the next one.
                foreach (var entry in db.ChangeTracker.Entries<MrnCounter>()) await entry.ReloadAsync(ct);
                patient.Mrn = await NextMrnAsync(ct);
            }
        }
        await audit.WriteAsync(userId, "Create", "Patient", patient.Id.ToString(), patient.Id.ToString(),
            r.ConfirmedNotDuplicate && candidates.Count > 0 ? "Registered after possible-duplicate check" : null, ct: ct);
        return new(patient, [], candidates);
    }

    public async Task<IReadOnlyList<string>> UpdateContactAsync(Guid id, ContactUpdate u, string userId, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (Clean(u.Email) is { } e && !System.Text.RegularExpressions.Regex.IsMatch(e, @"^[^@\s]+@[^@\s]+\.[^@\s]+$")) errors.Add("The email address looks wrong.");
        if (Clean(u.Postcode) is { } pc && !System.Text.RegularExpressions.Regex.IsMatch(pc, "^[0-9]{4}$")) errors.Add("NZ postcodes have 4 digits.");
        if (Clean(u.GpPracticeHpiFacilityId)?.ToUpperInvariant() is { } gp && !System.Text.RegularExpressions.Regex.IsMatch(gp, "^F[0-9A-Z]{5}-[0-9A-Z]$"))
            errors.Add("The GP practice HPI facility number looks wrong (format F12345-A).");
        if (errors.Count > 0) return errors;
        var p = await db.Patients.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return ["Patient not found."];
        var changed = new List<string>();
        void Set(string name, string? oldV, string? newV, Action<string?> apply) { if (oldV != newV) { changed.Add(name); apply(newV); } }
        Set("preferred name", p.PreferredName, Clean(u.PreferredName), v => p.PreferredName = v);
        Set("mobile", p.MobilePhone, Clean(u.MobilePhone), v => p.MobilePhone = v);
        Set("home phone", p.HomePhone, Clean(u.HomePhone), v => p.HomePhone = v);
        Set("email", p.Email, Clean(u.Email), v => p.Email = v);
        Set("address", $"{p.AddressLine1}|{p.AddressLine2}|{p.Suburb}|{p.City}|{p.Postcode}", $"{Clean(u.AddressLine1)}|{Clean(u.AddressLine2)}|{Clean(u.Suburb)}|{Clean(u.City)}|{Clean(u.Postcode)}", _ =>
        { p.AddressLine1 = Clean(u.AddressLine1); p.AddressLine2 = Clean(u.AddressLine2); p.Suburb = Clean(u.Suburb); p.City = Clean(u.City); p.Postcode = Clean(u.Postcode); });
        Set("GP practice", p.GpPracticeHpiFacilityId, Clean(u.GpPracticeHpiFacilityId)?.ToUpperInvariant(), v => p.GpPracticeHpiFacilityId = v);
        if (changed.Count == 0) return errors;
        p.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, "Update", "Patient", p.Id.ToString(), p.Id.ToString(), reason: "Changed: " + string.Join(", ", changed), ct: ct);
        return errors;
    }

    /// <summary>Opens a patient record (audited as a view).</summary>
    public async Task<Patient?> GetAsync(Guid id, string userId, string? workstation = null, CancellationToken ct = default)
    {
        var p = await db.Patients.Include(x => x.Ethnicities).Include(x => x.Alerts).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is not null)
            await audit.WriteAsync(userId, "View", "Patient", id.ToString(), id.ToString(), workstation: workstation, ct: ct);
        return p;
    }

    /// <summary>
    /// Search by NHI, MRN, or name ("Family", "Family, Given") with optional date of birth.
    /// Name matching ignores case and macrons. Returns at most 50 results.
    /// </summary>
    public async Task<IReadOnlyList<Patient>> SearchAsync(string term, DateOnly? dateOfBirth = null, CancellationToken ct = default)
    {
        term = term.Trim();
        if (term.Length == 0 && dateOfBirth is null) return [];
        IQueryable<Patient> q = db.Patients.Where(p => p.MergedIntoId == null);

        var upper = term.ToUpperInvariant();
        if (NhiValidator.Validate(upper).IsValid)
            q = q.Where(p => p.Nhi == upper);
        else if (upper.StartsWith("TDS", StringComparison.Ordinal) && upper.Length > 3 && upper[3..].All(char.IsAsciiDigit))
            q = q.Where(p => p.Mrn == upper);
        else if (term.Length > 0)
        {
            var parts = term.Split(',', 2, StringSplitOptions.TrimEntries);
            var family = SearchKey(parts[0]);
            q = q.Where(p => p.FamilyNameSearch.StartsWith(family));
            if (parts.Length == 2 && parts[1].Length > 0)
            {
                var given = SearchKey(parts[1]);
                q = q.Where(p => p.GivenNamesSearch.StartsWith(given));
            }
        }
        if (dateOfBirth is not null) q = q.Where(p => p.DateOfBirth == dateOfBirth);

        return await q.OrderBy(p => p.FamilyNameSearch).ThenBy(p => p.GivenNamesSearch).Take(50).ToListAsync(ct);
    }

    public async Task<PatientAlert> AddAlertAsync(Guid patientId, AlertType type, AlertSeverity severity, string description, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Alert description is required.", nameof(description));
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, ct)) throw new KeyNotFoundException("Patient not found.");
        var alert = new PatientAlert
        {
            PatientId = patientId, Type = type, Severity = severity, Description = description.Trim(),
            RecordedBy = userId, RecordedAt = clock.GetUtcNow()
        };
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(userId, "Create", "PatientAlert", alert.Id.ToString(), patientId.ToString(), $"{type}: {severity}", ct: ct);
        return alert;
    }

    /// <summary>Upper-case and remove diacritics (macrons), collapse spaces.</summary>
    public static string SearchKey(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return string.Join(' ', sb.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private List<string> Validate(PatientRegistration r, out string? nhi)
    {
        var errors = new List<string>();
        nhi = null;
        if (string.IsNullOrWhiteSpace(r.FamilyName)) errors.Add("Family name is required.");
        if (string.IsNullOrWhiteSpace(r.GivenNames)) errors.Add("Given name(s) are required.");
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (r.DateOfBirth > today) errors.Add("Date of birth cannot be in the future.");
        if (r.DateOfBirth < new DateOnly(1900, 1, 1)) errors.Add("Date of birth looks incorrect.");
        if (!string.IsNullOrWhiteSpace(r.Nhi))
        {
            var v = NhiValidator.Validate(r.Nhi);
            if (v.IsValid) nhi = r.Nhi.Trim().ToUpperInvariant();
            else errors.Add($"NHI: {v.Error}");
        }
        var eth = r.EthnicityCodes ?? [];
        if (eth.Count > MaxEthnicities) errors.Add($"Up to {MaxEthnicities} ethnicities can be recorded.");
        if (eth.Any(c => c.Trim().Length != 5 || !c.Trim().All(char.IsAsciiDigit))) errors.Add("Ethnicity codes must be 5-digit HISO 10001 codes.");
        if (!string.IsNullOrWhiteSpace(r.Email) && !r.Email.Contains('@')) errors.Add("Email address looks incorrect.");
        return errors;
    }

    private async Task<IReadOnlyList<PossibleDuplicate>> DuplicatesByNhiAsync(string nhi, CancellationToken ct) =>
        await db.Patients.Where(p => p.Nhi == nhi)
            .Select(p => new PossibleDuplicate(p.Id, p.Mrn, p.Nhi, p.FamilyName, p.GivenNames, p.DateOfBirth)).ToListAsync(ct);

    private async Task<string> NextMrnAsync(CancellationToken ct)
    {
        // Allocated inside the same unit of work; the counter row update and patient insert commit together.
        var counter = await db.MrnCounter.SingleAsync(c => c.Id == 1, ct);
        var value = counter.NextValue++;
        return $"TDS{value:D7}";
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
