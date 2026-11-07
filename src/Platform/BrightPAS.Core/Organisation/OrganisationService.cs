using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;

namespace BrightPAS.Core.Organisation;

public sealed record OrganisationDetails(string OrganisationName, string? HpiOrganisationId, string SiteName, string? HpiFacilityId, string? AddressLine1,
    string? AddressLine2, string? Suburb, string? City, string? Postcode, string? Phone, string? Email);

public sealed record LocationEdit(Guid? Id, Guid FacilityId, string Code, string Name, LocationType Type, bool IsActive, int SortOrder);

public sealed record ClinicianEdit(Guid? Id, string Name, ClinicianType Type, string? Specialty, string? HpiCpn, string? Phone, string? Email, string? PracticeName,
    string? UserName, bool IsActive, int? Colour = null);

public sealed record ProcedureEdit(Guid? Id, string Code, string Name, string Service, int DefaultMinutes, string DefaultAnaesthetic, bool SideRequired, string? FeeCode, bool IsActive);

/// <summary>Organisation set-up screens: clinic details, theatres and rooms, clinician register and procedure catalogue. Every change is audited.</summary>
public sealed partial class OrganisationService(PasDbContext db, AuditWriter audit)
{
    public static readonly string[] Anaesthetics = ["General", "Sedation", "Regional", "Local", "None"];
    public static readonly string[] Services = ["Surgical", "Endoscopy"];

    [GeneratedRegex("^G[0-9A-Z]{5}-[0-9A-Z]$")] private static partial Regex HpiOrg();
    [GeneratedRegex("^F[0-9A-Z]{5}-[0-9A-Z]$")] private static partial Regex HpiFacility();
    [GeneratedRegex("^[0-9]{2}[A-Z]{4}$")] private static partial Regex HpiCpn();
    [GeneratedRegex("^[A-Z0-9-]{2,20}$")] private static partial Regex Code();
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")] private static partial Regex EmailRx();

    public static bool IsHpiCpn(string? s) => s is not null && HpiCpn().IsMatch(s.Trim().ToUpperInvariant());
    public static bool IsHpiFacility(string? s) => s is not null && HpiFacility().IsMatch(s.Trim().ToUpperInvariant());

    public async Task<Organisation?> GetAsync(CancellationToken ct = default) =>
        await db.Organisations.AsNoTracking().Include(o => o.Sites).ThenInclude(s => s.Facilities).ThenInclude(f => f.Locations).FirstOrDefaultAsync(ct);

    /// <summary>The main site (letters, messages and the patient portal use its details).</summary>
    public async Task<(Organisation Org, Site Site)?> MainSiteAsync(CancellationToken ct = default)
    {
        var org = await GetAsync(ct);
        var site = org?.Sites.Where(s => s.IsActive).OrderBy(s => s.Name).FirstOrDefault();
        return org is null || site is null ? null : (org, site);
    }

    public async Task<IReadOnlyList<string>> UpdateDetailsAsync(OrganisationDetails d, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.OrganisationName)) errors.Add("Enter the organisation name.");
        if (string.IsNullOrWhiteSpace(d.SiteName)) errors.Add("Enter the site name.");
        if (Up(d.HpiOrganisationId) is { } o && !HpiOrg().IsMatch(o)) errors.Add("The HPI organisation number looks wrong (format G12345-A).");
        if (Up(d.HpiFacilityId) is { } f && !HpiFacility().IsMatch(f)) errors.Add("The HPI facility number looks wrong (format F12345-A).");
        if (Blank(d.Email) is { } e && !EmailRx().IsMatch(e)) errors.Add("The email address looks wrong.");
        if (Blank(d.Postcode) is { } pc && !Regex.IsMatch(pc, "^[0-9]{4}$")) errors.Add("NZ postcodes have 4 digits.");
        if (errors.Count > 0) return errors;
        var org = await db.Organisations.Include(x => x.Sites).FirstAsync(ct);
        var site = org.Sites.Where(s => s.IsActive).OrderBy(s => s.Name).First();
        org.Name = d.OrganisationName.Trim(); org.HpiOrganisationId = Up(d.HpiOrganisationId);
        site.Name = d.SiteName.Trim(); site.HpiFacilityId = Up(d.HpiFacilityId); site.AddressLine1 = Blank(d.AddressLine1); site.AddressLine2 = Blank(d.AddressLine2);
        site.Suburb = Blank(d.Suburb); site.City = Blank(d.City); site.Postcode = Blank(d.Postcode); site.Phone = Blank(d.Phone); site.Email = Blank(d.Email);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "Update", "Organisation", org.Id.ToString(), reason: $"{org.Name} / {site.Name}", ct: ct);
        return errors;
    }

    // ------------------------------------------------------------ theatres and rooms

    public async Task<(Location? Location, string? Error)> SaveLocationAsync(LocationEdit l, string user, CancellationToken ct = default)
    {
        var code = Up(l.Code);
        if (code is null || !Code().IsMatch(code)) return (null, "Enter a short code (letters, numbers or -, up to 20), e.g. TH2.");
        if (string.IsNullOrWhiteSpace(l.Name)) return (null, "Enter the name.");
        if (!await db.Facilities.AnyAsync(f => f.Id == l.FacilityId, ct)) return (null, "Choose the area (facility) this room belongs to.");
        if (await db.Locations.AnyAsync(x => x.FacilityId == l.FacilityId && x.Code == code && x.Id != l.Id, ct)) return (null, $"Code {code} is already used in this area.");
        Location loc;
        string change;
        if (l.Id is { } id)
        {
            loc = await db.Locations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new InvalidOperationException();
            change = $"{loc.Code} {loc.Name} ({(loc.IsActive ? "active" : "inactive")}) -> {code} {l.Name.Trim()} ({(l.IsActive ? "active" : "inactive")})";
            loc.FacilityId = l.FacilityId; loc.Code = code; loc.Name = l.Name.Trim(); loc.Type = l.Type; loc.IsActive = l.IsActive; loc.SortOrder = l.SortOrder;
        }
        else
        {
            loc = new Location { FacilityId = l.FacilityId, Code = code, Name = l.Name.Trim(), Type = l.Type, IsActive = l.IsActive, SortOrder = l.SortOrder };
            db.Locations.Add(loc);
            change = $"Added {code} {loc.Name} ({l.Type})";
        }
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, l.Id is null ? "Create" : "Update", "Location", loc.Id.ToString(), reason: change, ct: ct);
        return (loc, null);
    }

    // ------------------------------------------------------------ clinicians

    public async Task<IReadOnlyList<Clinician>> CliniciansAsync(bool activeOnly, CancellationToken ct = default) =>
        await db.Clinicians.AsNoTracking().Where(c => !activeOnly || c.IsActive).OrderBy(c => c.Type).ThenBy(c => c.Name).ToListAsync(ct);

    public async Task<(Clinician? Clinician, IReadOnlyList<string> Errors)> SaveClinicianAsync(ClinicianEdit c, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var name = c.Name?.Trim() ?? "";
        if (name.Length < 3) errors.Add("Enter the clinician's name as it should appear on lists and letters, e.g. Dr Ben Hall.");
        if (Up(c.HpiCpn) is { } cpn && !HpiCpn().IsMatch(cpn)) errors.Add("The HPI CPN looks wrong (2 numbers then 4 letters, e.g. 12ABCD).");
        if (Blank(c.Email) is { } e && !EmailRx().IsMatch(e)) errors.Add("The email address looks wrong.");
        if (await db.Clinicians.AnyAsync(x => x.Name == name && x.Id != c.Id, ct)) errors.Add($"{name} is already in the register.");
        if (Up(c.HpiCpn) is { } cpn2 && await db.Clinicians.AnyAsync(x => x.HpiCpn == cpn2 && x.Id != c.Id, ct)) errors.Add("Another clinician already has this HPI CPN.");
        if (errors.Count > 0) return (null, errors);
        Clinician row;
        if (c.Id is { } id) row = await db.Clinicians.SingleAsync(x => x.Id == id, ct);
        else
        {
            var used = await db.Clinicians.Select(x => x.Colour).ToListAsync(ct);
            row = new Clinician { Name = name, Colour = Enumerable.Range(0, 12).FirstOrDefault(i => !used.Contains(i), used.Count % 12) };
            db.Clinicians.Add(row);
        }
        if (c.Colour is { } colour && colour is >= 0 and < 12) row.Colour = colour;
        row.Name = name; row.Type = c.Type; row.Specialty = Blank(c.Specialty); row.HpiCpn = Up(c.HpiCpn); row.Phone = Blank(c.Phone); row.Email = Blank(c.Email);
        row.PracticeName = Blank(c.PracticeName); row.UserName = Blank(c.UserName); row.IsActive = c.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, c.Id is null ? "Create" : "Update", "Clinician", row.Id.ToString(), reason: $"{row.Name} ({row.Type}{(row.IsActive ? "" : ", inactive")})", ct: ct);
        return (row, errors);
    }

    // ------------------------------------------------------------ procedures

    public async Task<IReadOnlyList<ProcedureItem>> ProceduresAsync(bool activeOnly, CancellationToken ct = default) =>
        await db.Procedures.AsNoTracking().Where(p => !activeOnly || p.IsActive).OrderBy(p => p.Service).ThenBy(p => p.Name).ToListAsync(ct);

    public async Task<(ProcedureItem? Procedure, IReadOnlyList<string> Errors)> SaveProcedureAsync(ProcedureEdit p, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        var code = Up(p.Code);
        if (code is null || !Code().IsMatch(code)) errors.Add("Enter a short code (letters, numbers or -), e.g. LAPCHOLE.");
        if (string.IsNullOrWhiteSpace(p.Name)) errors.Add("Enter the procedure name.");
        if (!Services.Contains(p.Service)) errors.Add("Choose Surgical or Endoscopy.");
        if (!Anaesthetics.Contains(p.DefaultAnaesthetic)) errors.Add("Choose the usual anaesthetic.");
        if (p.DefaultMinutes is < 5 or > 720) errors.Add("Usual duration must be between 5 and 720 minutes.");
        if (code is not null && await db.Procedures.AnyAsync(x => x.Code == code && x.Id != p.Id, ct)) errors.Add($"Code {code} is already used.");
        if (errors.Count > 0) return (null, errors);
        ProcedureItem row;
        if (p.Id is { } id) row = await db.Procedures.SingleAsync(x => x.Id == id, ct);
        else { row = new ProcedureItem { Code = code!, Name = p.Name.Trim() }; db.Procedures.Add(row); }
        row.Code = code!; row.Name = p.Name.Trim(); row.Service = p.Service; row.DefaultMinutes = p.DefaultMinutes; row.DefaultAnaesthetic = p.DefaultAnaesthetic;
        row.SideRequired = p.SideRequired; row.FeeCode = Up(p.FeeCode); row.IsActive = p.IsActive;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, p.Id is null ? "Create" : "Update", "Procedure", row.Id.ToString(), reason: $"{row.Code} {row.Name}", ct: ct);
        return (row, errors);
    }

    /// <summary>Typical day-surgery and endoscopy procedures to start from (sandbox / first set-up). Skips codes that already exist.</summary>
    public async Task<int> AddExampleProceduresAsync(string user, CancellationToken ct = default)
    {
        (string Code, string Name, string Service, int Min, string An, bool Side)[] examples =
        [
            ("LAPCHOLE", "Laparoscopic cholecystectomy", "Surgical", 75, "General", false),
            ("INGHERNIA", "Inguinal hernia repair", "Surgical", 60, "General", true),
            ("UMBHERNIA", "Umbilical hernia repair", "Surgical", 45, "General", false),
            ("CARPAL", "Carpal tunnel release", "Surgical", 20, "Local", true),
            ("SKINLES", "Excision of skin lesion", "Surgical", 20, "Local", false),
            ("HAEMORR", "Haemorrhoidectomy", "Surgical", 30, "General", false),
            ("VARVEINS", "Varicose vein surgery", "Surgical", 60, "General", true),
            ("COLON", "Colonoscopy", "Endoscopy", 30, "Sedation", false),
            ("GASTRO", "Gastroscopy", "Endoscopy", 15, "Sedation", false),
            ("GASTROCOL", "Gastroscopy and colonoscopy", "Endoscopy", 45, "Sedation", false),
            ("FLEXSIG", "Flexible sigmoidoscopy", "Endoscopy", 20, "None", false),
        ];
        var existing = (await db.Procedures.Select(p => p.Code).ToListAsync(ct)).ToHashSet();
        var added = 0;
        foreach (var e in examples.Where(e => !existing.Contains(e.Code)))
        {
            db.Procedures.Add(new ProcedureItem { Code = e.Code, Name = e.Name, Service = e.Service, DefaultMinutes = e.Min, DefaultAnaesthetic = e.An, SideRequired = e.Side });
            added++;
        }
        await db.SaveChangesAsync(ct);
        if (added > 0) await audit.WriteAsync(user, "Create", "Procedure", "examples", reason: $"{added} example procedures added", ct: ct);
        return added;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string? Up(string? s) => Blank(s)?.ToUpperInvariant();
}
