using Microsoft.AspNetCore.Identity;

namespace Tds.Pas.Identity;

/// <summary>A PAS user account. MFA is mandatory for every account.</summary>
public sealed class PasUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
    /// <summary>HPI Common Person Number for registered health practitioners, when applicable.</summary>
    public string? HpiCpn { get; set; }
}

public sealed class PasRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}

/// <summary>Time-limited emergency access to a patient record outside the user's normal permissions.</summary>
public sealed class BreakGlassGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string PatientId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public BreakGlassReviewStatus ReviewStatus { get; set; } = BreakGlassReviewStatus.Pending;
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
}

public enum BreakGlassReviewStatus { Pending = 0, Appropriate = 1, Inappropriate = 2 }
