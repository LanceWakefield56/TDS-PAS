using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tds.Pas.Core.Data;
using Tds.Pas.Core.Organisation;

namespace Tds.Pas.Core.Tests;

public class OrganisationTests
{
    [Fact]
    public void Default_setup_has_theatre_1_active_and_theatre_2_configured_but_inactive()
    {
        var locations = TdsDefaultSetup.Create().Sites.Single().Facilities.SelectMany(f => f.Locations).ToList();
        Assert.True(locations.Single(l => l.Code == "TH1").IsActive);
        Assert.False(locations.Single(l => l.Code == "TH2").IsActive);
        Assert.Equal(2, locations.Count(l => l.Type == LocationType.Theatre));
    }

    [Fact]
    public async Task Default_setup_saves_and_reloads()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<PasDbContext>().UseSqlite(conn).Options;

        await using (var db = new PasDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.Organisations.Add(TdsDefaultSetup.Create());
            await db.SaveChangesAsync();
        }

        await using (var db = new PasDbContext(options))
        {
            var org = await db.Organisations
                .Include(o => o.Sites).ThenInclude(s => s.Facilities).ThenInclude(f => f.Locations)
                .SingleAsync();
            Assert.Equal("Tasman Day Surgery", org.Name);
            Assert.Equal(5, org.Sites.Single().Facilities.Sum(f => f.Locations.Count));
        }
    }

    [Fact]
    public async Task Location_codes_must_be_unique_within_a_facility()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<PasDbContext>().UseSqlite(conn).Options;
        await using var db = new PasDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var org = TdsDefaultSetup.Create();
        var surgical = org.Sites[0].Facilities[0];
        surgical.Locations.Add(new Location { FacilityId = surgical.Id, Code = "TH1", Name = "Duplicate", Type = LocationType.Theatre });
        db.Organisations.Add(org);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
