using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Data;

namespace BrightPAS.Core.Audit;

/// <summary>Appends sealed audit events. Callers never edit or delete audit records.</summary>
public sealed class AuditWriter(PasDbContext db, TimeProvider clock)
{
    public async Task<AuditEvent> WriteAsync(string userId, string action, string entityType, string entityId,
        string? patientId = null, string? reason = null, string? workstation = null, CancellationToken ct = default)
    {
        // Runs inside the configured execution strategy (e.g. SQL Server retry on transient faults) so the
        // serialisable transaction is retried as a unit. Serialisable isolation keeps the sequence and chain consistent.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async token =>
        {
            AuditEvent? e = null;
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
                var last = await db.AuditEvents.AsNoTracking().OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(token);
                e = AuditChain.Seal(new AuditEvent
                {
                    // Millisecond precision: stored exactly by every supported database, so hashes re-verify after reload.
                    OccurredAt = TruncateToMilliseconds(clock.GetUtcNow()),
                    UserId = userId, Action = action, EntityType = entityType, EntityId = entityId,
                    PatientId = patientId, Reason = reason, Workstation = workstation,
                    PreviousHash = string.Empty, Hash = string.Empty
                }, last);
                db.AuditEvents.Add(e);
                await db.SaveChangesAsync(token);
                await tx.CommitAsync(token);
                return e;
            }
            catch
            {
                if (e is not null) db.Entry(e).State = EntityState.Detached; // so a retry does not insert it twice
                throw;
            }
        }, ct);
    }

    private static DateTimeOffset TruncateToMilliseconds(DateTimeOffset t) =>
        new(t.UtcTicks - t.UtcTicks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
}
