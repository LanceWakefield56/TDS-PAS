using System.Security.Cryptography;
using System.Text;

namespace BrightPAS.Licensing;

/// <summary>
/// Works out what a licence allows right now.
/// Clinical-safety rule (ADR 0009): a licence problem never hides existing clinical data. Modules that were licensed
/// become read-only (view, print, export) instead of disappearing; only new work is blocked.
/// </summary>
public static class LicenceEvaluator
{
    public sealed record Input(
        string? LicenceText,
        string InstallationCode,
        DateTimeOffset Now,
        DateTimeOffset? LastSeenAt = null,
        string? ActivationText = null,
        DateTimeOffset? LicenceInstalledAt = null,
        IReadOnlyList<string>? LastKnownModules = null);

    public const int ExpiryWarningDays = 30;
    private static readonly TimeZoneInfo Nz = FindNz();

    public static LicenceStatus Evaluate(Input input, IReadOnlyList<TrustedKey> keys)
    {
        var msgs = new List<string>();
        var lastKnown = input.LastKnownModules ?? [];
        if (string.IsNullOrWhiteSpace(input.LicenceText))
            return Build(LicenceHealth.Missing, null, lastKnown, ModuleAccess.ReadOnly, ["No licence is installed. Only the base system is available."], false);

        var v = LicenceCodec.VerifyLicence(input.LicenceText, keys);
        if (!v.Ok)
            return Build(LicenceHealth.Invalid, null, lastKnown, ModuleAccess.ReadOnly, [v.Error!, "Existing records stay viewable; new work in paid modules is paused until a valid licence is installed."], false);

        var doc = v.Value!;
        var dev = v.Key!.IsDevelopment;
        if (dev) msgs.Add("This licence is signed with the DEVELOPMENT key – for sandbox and testing only.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(input.Now, Nz).DateTime);

        if (input.LastSeenAt is { } seen && input.Now < seen.AddDays(-1))
            return Build(LicenceHealth.ClockProblem, doc, doc.Modules, ModuleAccess.ReadOnly,
                [.. msgs, $"The server clock ({input.Now:yyyy-MM-dd HH:mm}) is earlier than when BrightPAS last ran ({seen:yyyy-MM-dd HH:mm}). Correct the clock; paid modules are read-only until then."], dev);

        if (doc.InstallationCode is not null && !Same(doc.InstallationCode, input.InstallationCode))
            return Build(LicenceHealth.WrongInstallation, doc, doc.Modules, ModuleAccess.ReadOnly,
                [.. msgs, $"This licence is for installation {doc.InstallationCode}, but this server is {input.InstallationCode}. Ask the vendor to reissue it for this server."], dev);

        var health = LicenceHealth.Valid;
        var access = ModuleAccess.Full;
        var daysLeft = doc.ExpiresOn.DayNumber - today.DayNumber;
        if (daysLeft < 0)
        {
            var graceLeft = doc.GraceDays + daysLeft;
            if (graceLeft >= 0) { health = LicenceHealth.InGrace; msgs.Add($"The licence expired on {doc.ExpiresOn:d MMMM yyyy}. Paid modules keep working for {graceLeft} more day(s), then become read-only."); }
            else { health = LicenceHealth.Expired; access = ModuleAccess.ReadOnly; msgs.Add($"The licence expired on {doc.ExpiresOn:d MMMM yyyy}. Paid modules are read-only until it is renewed."); }
        }
        else if (daysLeft <= ExpiryWarningDays)
        {
            health = LicenceHealth.ExpiringSoon;
            msgs.Add($"The licence expires on {doc.ExpiresOn:d MMMM yyyy} ({daysLeft} day(s)). Arrange renewal.");
        }

        if (doc.Activation is { } act && access == ModuleAccess.Full)
        {
            var token = input.ActivationText is null ? null : LicenceCodec.VerifyActivation(input.ActivationText, keys).Value;
            var tokenOk = token is not null && token.LicenceId == doc.LicenceId && Same(token.InstallationCode, input.InstallationCode);
            if (!tokenOk || token!.ValidUntil < input.Now)
            {
                var since = tokenOk ? token!.IssuedAt : input.LicenceInstalledAt ?? input.Now;
                var offlineLeft = (since.AddDays(act.OfflineGraceDays) - input.Now).TotalDays;
                if (offlineLeft >= 0)
                    msgs.Add($"Online licence check needed: BrightPAS could not confirm the licence with {act.Url}. Paid modules keep working for {Math.Ceiling(offlineLeft)} more day(s).");
                else
                {
                    health = LicenceHealth.ActivationOverdue; access = ModuleAccess.ReadOnly;
                    msgs.Add($"The licence has not been confirmed online for more than {act.OfflineGraceDays} days. Paid modules are read-only until BrightPAS can reach {act.Url}.");
                }
            }
        }

        if (health == LicenceHealth.Valid) msgs.Add($"Licensed to {doc.Customer} until {doc.ExpiresOn:d MMMM yyyy}.");
        return Build(health, doc, doc.Modules, access, msgs, dev);
    }

    private static LicenceStatus Build(LicenceHealth h, LicenceDocument? doc, IReadOnlyList<string> licensed, ModuleAccess access, List<string> msgs, bool dev)
    {
        var map = new Dictionary<string, ModuleAccess>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in ProductModules.All) map[m.Id] = ModuleAccess.None;
        var all = licensed.Contains("*");
        foreach (var id in all ? ProductModules.All.Select(m => m.Id) : licensed) map[id] = access;
        return new LicenceStatus(h, doc, map, msgs, dev);
    }

    private static bool Same(string a, string b) => Normalise(a) == Normalise(b);
    public static string Normalise(string code) => new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static TimeZoneInfo FindNz()
    {
        foreach (var id in new[] { "Pacific/Auckland", "New Zealand Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.Utc;
    }
}

/// <summary>
/// The installation code identifies one BrightPAS installation on one server: a hash of the installation's random id
/// (stored in its database) and the server's machine id, shown as XXXXX-XXXXX-XXXXX-XXXXX.
/// Copying the database to another server, or the program to another database, gives a different code.
/// </summary>
public static class InstallationIdentity
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Code(Guid installationId, string machineId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"brightpas-install|{installationId:N}|{machineId.Trim().ToLowerInvariant()}"));
        var sb = new StringBuilder();
        for (var i = 0; i < 20; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append('-');
            sb.Append(Alphabet[hash[i] % Alphabet.Length]);
        }
        return sb.ToString();
    }

    public static string MachineId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (k?.GetValue("MachineGuid") is string g && g.Length > 0) return g;
            }
            foreach (var f in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                if (File.Exists(f)) return File.ReadAllText(f).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return Environment.MachineName;
    }
}
