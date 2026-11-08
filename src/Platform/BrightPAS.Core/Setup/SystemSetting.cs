using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;

namespace BrightPAS.Core.Setup;

/// <summary>Administrator-controlled setting (key/value), changed in the app – not at install – and audited.</summary>
public sealed class SystemSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}

/// <summary>How exports (CSV, packs, reports, letters downloaded as files) protect patient information.</summary>
public enum ExportProtection
{
    /// <summary>Every export is an AES-256 encrypted, password-protected file (recommended).</summary>
    Required = 1,
    /// <summary>Staff choose; unprotected exports are recorded as such in the audit log.</summary>
    Optional = 2,
}

public sealed record SecuritySettings(ExportProtection ExportProtection, int MinExportPasswordLength, int MaxUploadMegabytes, int PortalLinkDays);

public sealed class SettingsService(PasDbContext db, AuditWriter audit, TimeProvider clock)
{
    public const string ExportProtectionKey = "export.protection";
    public const string ExportMinPasswordKey = "export.minPasswordLength";
    public const string MaxUploadKey = "documents.maxUploadMegabytes";
    public const string PortalLinkDaysKey = "portal.linkDays";

    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key, ct))?.Value;

    public async Task SetAsync(string key, string value, string user, CancellationToken ct = default)
    {
        var row = await db.Settings.SingleOrDefaultAsync(s => s.Key == key, ct);
        var old = row?.Value;
        if (row is null) { row = new SystemSetting { Key = key }; db.Settings.Add(row); }
        if (old == value) return;
        row.Value = value; row.UpdatedAt = clock.GetUtcNow(); row.UpdatedBy = user;
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(user, "SettingChanged", "SystemSetting", key, reason: $"{old ?? "(default)"} -> {value}", ct: ct);
    }

    public async Task<SecuritySettings> SecurityAsync(CancellationToken ct = default)
    {
        var all = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        int I(string k, int d, int min, int max) => all.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? Math.Clamp(n, min, max) : d;
        var mode = all.TryGetValue(ExportProtectionKey, out var m) && Enum.TryParse<ExportProtection>(m, out var e) ? e : ExportProtection.Required;
        return new SecuritySettings(mode, I(ExportMinPasswordKey, 10, 8, 64), I(MaxUploadKey, 20, 1, 25), I(PortalLinkDaysKey, 30, 1, 90));
    }

    public async Task<IReadOnlyList<string>> UpdateSecurityAsync(SecuritySettings s, string user, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(s.ExportProtection)) errors.Add("Choose how exports are protected.");
        if (s.MinExportPasswordLength is < 8 or > 64) errors.Add("Export passwords must be at least 8 characters (up to 64).");
        if (s.MaxUploadMegabytes is < 1 or > 25) errors.Add("Upload limit must be 1–25 MB.");
        if (s.PortalLinkDays is < 1 or > 90) errors.Add("Patient portal links last 1–90 days.");
        if (errors.Count > 0) return errors;
        await SetAsync(ExportProtectionKey, s.ExportProtection.ToString(), user, ct);
        await SetAsync(ExportMinPasswordKey, s.MinExportPasswordLength.ToString(), user, ct);
        await SetAsync(MaxUploadKey, s.MaxUploadMegabytes.ToString(), user, ct);
        await SetAsync(PortalLinkDaysKey, s.PortalLinkDays.ToString(), user, ct);
        return errors;
    }
}
