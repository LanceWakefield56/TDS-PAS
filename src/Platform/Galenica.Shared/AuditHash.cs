using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Galenica.Shared;

/// <summary>
/// The audit hash-chain formula, shared by the application and the installer's security self-test so both
/// verify the audit log identically.
/// </summary>
public static class AuditHash
{
    public const string Genesis = "GENESIS";

    public static string Compute(long sequence, DateTimeOffset occurredAt, string userId, string action, string entityType, string entityId,
        string? patientId, string? reason, string? workstation, string previousHash)
    {
        // Unit separator (U+001F) between fields prevents ambiguity between adjacent values.
        var canonical = string.Join('\u001F',
            sequence.ToString(CultureInfo.InvariantCulture),
            occurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            userId, action, entityType, entityId, patientId ?? string.Empty, reason ?? string.Empty, workstation ?? string.Empty,
            previousHash);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
