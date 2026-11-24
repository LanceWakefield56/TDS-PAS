using Microsoft.AspNetCore.Identity;

namespace BrightPAS.Identity;

public sealed record TotpEnrolment(string SharedKey, string OtpAuthUri);

/// <summary>
/// Authenticator-app (TOTP) enrolment and verification. Every account must complete enrolment
/// before it can be used. (Passkeys / FIDO2 security keys are added as a further factor.)
/// </summary>
public sealed class MfaService(UserManager<PasUser> users, TotpReplayGuard replay)
{
    public const string Issuer = "BrightPAS";

    public async Task<TotpEnrolment> BeginTotpEnrolmentAsync(PasUser user)
    {
        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user)
                  ?? throw new InvalidOperationException("Authenticator key could not be created.");
        var label = Uri.EscapeDataString($"{Issuer}:{user.UserName}");
        var uri = $"otpauth://totp/{label}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6&period=30";
        return new TotpEnrolment(FormatKey(key), uri);
    }

    /// <summary>Confirms the first code from the user's app, turns MFA on and returns recovery codes (shown once).</summary>
    public async Task<IReadOnlyList<string>?> CompleteTotpEnrolmentAsync(PasUser user, string code)
    {
        if (!await VerifyTotpAsync(user, code)) return null;
        await users.SetTwoFactorEnabledAsync(user, true);
        return (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToList();
    }

    /// <summary>Checks an authenticator code. Each code works once per account (security test 0.26.0: a captured code could be replayed within its window).</summary>
    public async Task<bool> VerifyTotpAsync(PasUser user, string code) =>
        await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Clean(code)) && replay.FirstUse(user.Id.ToString(), Clean(code));

    public async Task<bool> RedeemRecoveryCodeAsync(PasUser user, string code) =>
        (await users.RedeemTwoFactorRecoveryCodeAsync(user, code.Trim())).Succeeded;

    private static string Clean(string code) => new(code.Where(char.IsDigit).ToArray());

    private static string FormatKey(string key) =>
        string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))).ToLowerInvariant();
}


/// <summary>Remembers authenticator codes already used per account for longer than they stay valid (the provider accepts ±1 window).</summary>
public sealed class TotpReplayGuard(TimeProvider clock)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _used = new(StringComparer.Ordinal);

    /// <summary>Off only for the automated web test host, which signs the same test account in many times a second.</summary>
    public bool Enabled { get; set; } = true;

    public bool FirstUse(string userId, string code)
    {
        if (!Enabled) return true;
        var now = clock.GetUtcNow();
        foreach (var kv in _used.Where(kv => kv.Value < now)) _used.TryRemove(kv.Key, out _);
        return _used.TryAdd(userId + ":" + code, now.AddMinutes(3));
    }
}
