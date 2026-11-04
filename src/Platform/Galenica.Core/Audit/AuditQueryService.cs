using Microsoft.EntityFrameworkCore;
using Galenica.Core.Data;

namespace Galenica.Core.Audit;

public sealed record AuditIntegrityResult(bool Intact, long Records, long? FirstBrokenSequence, DateTimeOffset CheckedAt);

/// <summary>Read access to the audit log for the Privacy Officer, plus integrity verification of the hash chain.</summary>
public sealed class AuditQueryService(PasDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<AuditEvent>> SearchAsync(string? userId, string? patientId, string? action, int take = 200, CancellationToken ct = default)
    {
        IQueryable<AuditEvent> q = db.AuditEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(userId)) q = q.Where(e => e.UserId == userId.Trim());
        if (!string.IsNullOrWhiteSpace(patientId)) q = q.Where(e => e.PatientId == patientId.Trim());
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(e => e.Action == action.Trim());
        return await q.OrderByDescending(e => e.Sequence).Take(Math.Clamp(take, 1, 1000)).ToListAsync(ct);
    }

    /// <summary>Re-computes every record's hash in order (streamed in pages so it scales to millions of rows).</summary>
    public async Task<AuditIntegrityResult> VerifyAsync(CancellationToken ct = default)
    {
        const int page = 5000;
        long after = 0, count = 0;
        var prevHash = AuditChain.GenesisHash;
        long expectedSeq = 1;
        while (true)
        {
            var batch = await db.AuditEvents.AsNoTracking().Where(e => e.Sequence > after).OrderBy(e => e.Sequence).Take(page).ToListAsync(ct);
            if (batch.Count == 0) break;
            foreach (var e in batch)
            {
                if (e.Sequence != expectedSeq || e.PreviousHash != prevHash || e.Hash != AuditChain.ComputeHash(e, e.PreviousHash))
                    return new(false, count, e.Sequence, clock.GetUtcNow());
                prevHash = e.Hash; expectedSeq++; count++;
            }
            after = batch[^1].Sequence;
        }
        return new(true, count, null, clock.GetUtcNow());
    }
}
