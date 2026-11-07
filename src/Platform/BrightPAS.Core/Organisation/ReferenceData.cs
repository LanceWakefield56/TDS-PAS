namespace BrightPAS.Core.Organisation;

public enum ClinicianType { Surgeon = 1, Anaesthetist = 2, Endoscopist = 3, Physician = 4, Other = 5 }

/// <summary>
/// A credentialed clinician who works at TDS (surgeon, anaesthetist, endoscopist). Used on theatre lists, referrals,
/// the waitlist and letters instead of free text. HPI CPN = the practitioner's Common Person Number.
/// </summary>
public sealed class Clinician
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Display name as used on lists and letters, e.g. "Dr Ben Hall".</summary>
    public required string Name { get; set; }
    public ClinicianType Type { get; set; }
    public string? Specialty { get; set; }
    public string? HpiCpn { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>The clinician's own practice (where letters and reports for their patients are sent).</summary>
    public string? PracticeName { get; set; }
    /// <summary>Optional link to the clinician's BrightPAS user account.</summary>
    public string? UserName { get; set; }
    /// <summary>Calendar colour (index into the 12-colour palette).</summary>
    public int Colour { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>An entry in the procedure catalogue: picked when booking or waitlisting instead of typing.</summary>
public sealed class ProcedureItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>Surgical or Endoscopy.</summary>
    public string Service { get; set; } = "Surgical";
    public int DefaultMinutes { get; set; } = 30;
    /// <summary>General, Sedation, Regional, Local or None.</summary>
    public string DefaultAnaesthetic { get; set; } = "General";
    /// <summary>When true, a side (left/right/bilateral) must be chosen at booking.</summary>
    public bool SideRequired { get; set; }
    /// <summary>Optional fee schedule code (Estimates &amp; invoices).</summary>
    public string? FeeCode { get; set; }
    public bool IsActive { get; set; } = true;
}
