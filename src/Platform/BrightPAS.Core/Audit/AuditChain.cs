namespace BrightPAS.Core.Audit;

/// <summary>
/// Hash chain for audit records: each record's hash covers its own content plus the previous
/// record's hash, so any edit, deletion or re-ordering is detectable.
/// </summary>
public static class AuditChain
{
    public const string GenesisHash = BrightPAS.Shared.AuditHash.Genesis;

    public static string ComputeHash(AuditEvent e, string previousHash)
    {
        ArgumentNullException.ThrowIfNull(e);
        return BrightPAS.Shared.AuditHash.Compute(e.Sequence, e.OccurredAt, e.UserId, e.Action, e.EntityType, e.EntityId,
            e.PatientId, e.Reason, e.Workstation, previousHash);
    }

    /// <summary>Fills in sequence, previous hash and hash for a new record appended after <paramref name="last"/>.</summary>
    public static AuditEvent Seal(AuditEvent e, AuditEvent? last)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.Sequence = (last?.Sequence ?? 0) + 1;
        e.PreviousHash = last?.Hash ?? GenesisHash;
        e.Hash = ComputeHash(e, e.PreviousHash);
        return e;
    }

    /// <summary>Returns the sequence number of the first broken record, or null if the chain is intact.</summary>
    public static long? FindFirstBreak(IEnumerable<AuditEvent> eventsInOrder)
    {
        var expectedPrev = GenesisHash;
        long expectedSeq = 1;
        foreach (var e in eventsInOrder)
        {
            if (e.Sequence != expectedSeq || e.PreviousHash != expectedPrev || e.Hash != ComputeHash(e, e.PreviousHash))
                return e.Sequence;
            expectedPrev = e.Hash;
            expectedSeq++;
        }
        return null;
    }
}
