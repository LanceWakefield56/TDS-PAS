using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;

namespace BrightPAS.Core.Tests;

public class AuditChainTests
{
    private static List<AuditEvent> BuildChain(int n)
    {
        var list = new List<AuditEvent>();
        AuditEvent? last = null;
        for (var i = 0; i < n; i++)
        {
            last = AuditChain.Seal(new AuditEvent
            {
                OccurredAt = new DateTimeOffset(2026, 11, 2, 9, 0, i, TimeSpan.Zero),
                UserId = "nurse.a", Action = "View", EntityType = "Patient", EntityId = $"P{i}",
                PatientId = $"P{i}", PreviousHash = "", Hash = ""
            }, last);
            list.Add(last);
        }
        return list;
    }

    [Fact]
    public void Intact_chain_verifies()
    {
        Assert.Null(AuditChain.FindFirstBreak(BuildChain(5)));
    }

    [Fact]
    public void Edited_record_is_detected()
    {
        var chain = BuildChain(5);
        chain[2].UserId = "someone.else";
        Assert.Equal(3, AuditChain.FindFirstBreak(chain));
    }

    [Fact]
    public void Deleted_record_is_detected()
    {
        var chain = BuildChain(5);
        chain.RemoveAt(1);
        Assert.Equal(3, AuditChain.FindFirstBreak(chain));
    }

    [Fact]
    public async Task Writer_appends_sealed_events_to_the_database()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<PasDbContext>().UseSqlite(conn).Options;
        await using var db = new PasDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var writer = new AuditWriter(db, TimeProvider.System);
        await writer.WriteAsync("admin", "Login", "User", "admin");
        await writer.WriteAsync("nurse.a", "View", "Patient", "P1", patientId: "P1", workstation: "PACU-01");

        var stored = await db.AuditEvents.OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Null(AuditChain.FindFirstBreak(stored));
    }
}

public class AuditQueryTests
{
    [Fact]
    public async Task Verification_reports_intact_chain_and_detects_a_tampered_record()
    {
        await using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<PasDbContext>().UseSqlite(conn).Options;
        await using var db = new PasDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var writer = new AuditWriter(db, TimeProvider.System);
        for (var i = 0; i < 12; i++) await writer.WriteAsync("u" + i, "View", "Patient", "P" + i, "P" + i);

        var q = new AuditQueryService(db, TimeProvider.System);
        var ok = await q.VerifyAsync();
        Assert.True(ok.Intact);
        Assert.Equal(12, ok.Records);
        Assert.Equal(3, (await q.SearchAsync(null, null, "View", take: 3)).Count);

        await db.Database.ExecuteSqlRawAsync("UPDATE AuditEvents SET UserId = 'intruder' WHERE Sequence = 7");
        var bad = await q.VerifyAsync();
        Assert.False(bad.Intact);
        Assert.Equal(7, bad.FirstBrokenSequence);
    }
}
