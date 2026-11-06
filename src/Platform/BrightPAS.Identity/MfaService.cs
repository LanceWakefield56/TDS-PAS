using Microsoft.AspNetCore.Identity;

namespace BrightPAS.Identity;

public sealed record TotpEnrolment(string SharedKey, string OtpAuthUri);

/// <summary>
/// Authenticator-app (TOTP) enrolment and verification. Every account must complete enrolment
/// before it can be used. (Passkeys / FIDO2 security keys are added as a further factor.)
/// </summary>
public sealed class MfaService(UserManager<PasUser> users)
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

    public Task<bool> VerifyTotpAsync(PasUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, Clean(code));

    public async Task<bool> RedeemRecoveryCodeAsync(PasUser user, string code) =>
        (await users.RedeemTwoFactorRecoveryCodeAsync(user, code.Trim())).Succeeded;

    private static string Clean(string code) => new(code.Where(char.IsDigit).ToArray());

    private static string FormatKey(string key) =>
        string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))).ToLowerInvariant();
}
