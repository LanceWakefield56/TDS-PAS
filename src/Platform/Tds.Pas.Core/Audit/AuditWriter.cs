using Microsoft.EntityFrameworkCore;
using Tds.Pas.Core.Data;

namespace Tds.Pas.Core.Audit;

/// <summary>Appends sealed audit events. Callers never edit or delete audit records.</summary>
public sealed class AuditWriter(PasDbContext db, TimeProvider clock)
{
    public async Task<AuditEvent> WriteAsync(string userId, string action, string entityType, string entityId,
        string? patientId = null, string? reason = null, string? workstation = null, CancellationToken ct = default)
    {
        // Serialisable transaction keeps the sequence and chain consistent under concurrent writers.
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var last = await db.AuditEvents.OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        var e = AuditChain.Seal(new AuditEvent
        {
            OccurredAt = clock.GetUtcNow(),
            UserId = userId, Action = action, EntityType = entityType, EntityId = entityId,
            PatientId = patientId, Reason = reason, Workstation = workstation,
            PreviousHash = string.Empty, Hash = string.Empty
        }, last);
        db.AuditEvents.Add(e);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return e;
    }
}
