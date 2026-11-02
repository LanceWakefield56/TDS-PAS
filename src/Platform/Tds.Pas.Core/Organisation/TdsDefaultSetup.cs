namespace Tds.Pas.Core.Organisation;

/// <summary>
/// Default organisation structure offered by the installer for TDS. Staff can rename, add or
/// deactivate anything afterwards through administration screens (no code changes).
/// </summary>
public static class TdsDefaultSetup
{
    public static Organisation Create()
    {
        var org = new Organisation { Name = "Tasman Day Surgery" };
        var site = new Site { OrganisationId = org.Id, Name = "Tasman Day Surgery – main site" };
        org.Sites.Add(site);

        var surgical = AddFacility(site, "Surgical suite", FacilityType.SurgicalSuite);
        AddLocation(surgical, "TH1", "Theatre 1", LocationType.Theatre, active: true, 10);
        // Second theatre is planned (2–5 years): configured now, switched on later without development.
        AddLocation(surgical, "TH2", "Theatre 2 (future)", LocationType.Theatre, active: false, 20);

        var recovery = AddFacility(site, "Recovery", FacilityType.Recovery);
        AddLocation(recovery, "PACU", "Post-anaesthesia care unit", LocationType.PacuBay, active: true, 30);
        AddLocation(recovery, "ST2", "Stage 2 recovery", LocationType.Stage2Recovery, active: true, 40);

        var cssd = AddFacility(site, "Sterile services (CSSD)", FacilityType.Cssd);
        AddLocation(cssd, "CSSD", "CSSD", LocationType.CssdZone, active: true, 50);

        return org;
    }

    private static Facility AddFacility(Site site, string name, FacilityType type)
    {
        var f = new Facility { SiteId = site.Id, Name = name, Type = type };
        site.Facilities.Add(f);
        return f;
    }

    private static void AddLocation(Facility f, string code, string name, LocationType type, bool active, int sort) =>
        f.Locations.Add(new Location { FacilityId = f.Id, Code = code, Name = name, Type = type, IsActive = active, SortOrder = sort });
}
