using Microsoft.EntityFrameworkCore;
using Tds.Pas.Core.Audit;

namespace Tds.Pas.Identity;

/// <summary>Notified whenever break-glass access is used (e.g. alert the Privacy Officer).</summary>
public interface IBreakGlassNotifier
{
    Task NotifyAsync(BreakGlassGrant grant, string userName, CancellationToken ct = default);
}

/// <summary>Default notifier until the messaging engine is available: relies on the review queue.</summary>
public sealed class NoOpBreakGlassNotifier : IBreakGlassNotifier
{
    public Task NotifyAsync(BreakGlassGrant grant, string userName, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Emergency access to a patient record: requires a stated reason, lasts a limited time, is audited,
/// alerts the Privacy Officer and must be reviewed.
/// </summary>
public sealed class BreakGlassService(PasIdentityDbContext db, AuditWriter audit, IBreakGlassNotifier notifier, TimeProvider clock)
{
    public static readonly TimeSpan GrantDuration = TimeSpan.FromMinutes(30);
    public const int MinimumReasonLength = 10;

    public async Task<BreakGlassGrant> GrantAsync(PasUser user, string patientId, string reason, string? workstation = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinimumReasonLength)
            throw new ArgumentException($"A reason of at least {MinimumReasonLength} characters is required for emergency access.", nameof(reason));

        var now = clock.GetUtcNow();
        var grant = new BreakGlassGrant
        {
            UserId = user.Id, PatientId = patientId, Reason = reason.Trim(),
            GrantedAt = now, ExpiresAt = now + GrantDuration
        };
        db.BreakGlassGrants.Add(grant);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user.UserName ?? user.Id.ToString(), "BreakGlass", "Patient", patientId, patientId, grant.Reason, workstation, ct);
        await notifier.NotifyAsync(grant, user.UserName ?? user.Id.ToString(), ct);
        return grant;
    }

    public async Task<bool> HasActiveGrantAsync(Guid userId, string patientId, CancellationToken ct = default)
    {
        // Expiry compared in memory (a user has few grants per patient); keeps the query portable across databases.
        var now = clock.GetUtcNow();
        var expiries = await db.BreakGlassGrants.Where(g => g.UserId == userId && g.PatientId == patientId)
            .Select(g => g.ExpiresAt).ToListAsync(ct);
        return expiries.Any(e => e > now);
    }

    public async Task<IReadOnlyList<BreakGlassGrant>> PendingReviewsAsync(CancellationToken ct = default) =>
        await db.BreakGlassGrants.Where(g => g.ReviewStatus == BreakGlassReviewStatus.Pending).OrderBy(g => g.GrantedAt).ToListAsync(ct);

    public async Task ReviewAsync(Guid grantId, string reviewer, BreakGlassReviewStatus outcome, string? notes, CancellationToken ct = default)
    {
        if (outcome == BreakGlassReviewStatus.Pending) throw new ArgumentException("Choose a review outcome.", nameof(outcome));
        var grant = await db.BreakGlassGrants.SingleAsync(g => g.Id == grantId, ct);
        grant.ReviewStatus = outcome;
        grant.ReviewedBy = reviewer;
        grant.ReviewedAt = clock.GetUtcNow();
        grant.ReviewNotes = notes;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(reviewer, "BreakGlassReviewed", "BreakGlassGrant", grantId.ToString(), grant.PatientId, outcome.ToString(), ct: ct);
    }
}
