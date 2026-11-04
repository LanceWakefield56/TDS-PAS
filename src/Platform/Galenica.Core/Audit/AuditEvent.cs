namespace Galenica.Core.Audit;

/// <summary>
/// One audit record: who did what to which record, when, from where and why.
/// Records are append-only and hash-chained; on SQL Server 2022 the table is also a ledger table.
/// </summary>
public sealed class AuditEvent
{
    public long Sequence { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public required string UserId { get; set; }
    /// <summary>e.g. View, Create, Update, Print, Export, BreakGlass, Login.</summary>
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? PatientId { get; set; }
    public string? Reason { get; set; }
    public string? Workstation { get; set; }
    public required string PreviousHash { get; set; }
    public required string Hash { get; set; }
}
