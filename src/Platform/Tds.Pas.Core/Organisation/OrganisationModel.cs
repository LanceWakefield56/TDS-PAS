namespace Tds.Pas.Core.Organisation;

/// <summary>Top of the hierarchy: the legal organisation (e.g. Tasman Day Surgery).</summary>
public sealed class Organisation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    /// <summary>HPI organisation identifier, when registered.</summary>
    public string? HpiOrganisationId { get; set; }
    public List<Site> Sites { get; set; } = [];
}

/// <summary>A physical site. Supports multi-site growth from day one.</summary>
public sealed class Site
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganisationId { get; set; }
    public required string Name { get; set; }
    /// <summary>HPI facility identifier, when registered.</summary>
    public string? HpiFacilityId { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Facility> Facilities { get; set; } = [];
}

/// <summary>A functional area within a site (e.g. surgical suite, CSSD, administration).</summary>
public sealed class Facility
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SiteId { get; set; }
    public required string Name { get; set; }
    public FacilityType Type { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Location> Locations { get; set; } = [];
}

/// <summary>A bookable or trackable place: theatre, procedure room, PACU bay, etc.</summary>
public sealed class Location
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FacilityId { get; set; }
    /// <summary>Short unique code within the site, e.g. TH1.</summary>
    public required string Code { get; set; }
    public required string Name { get; set; }
    public LocationType Type { get; set; }
    /// <summary>Inactive locations (e.g. a future Theatre 2) exist in configuration but cannot be booked.</summary>
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public enum FacilityType { SurgicalSuite = 1, Endoscopy = 2, Recovery = 3, Cssd = 4, Clinic = 5, Administration = 6 }

public enum LocationType { Theatre = 1, ProcedureRoom = 2, PacuBay = 3, Stage2Recovery = 4, ConsultRoom = 5, CssdZone = 6, Office = 7 }
