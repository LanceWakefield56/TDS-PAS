using Microsoft.AspNetCore.Identity;
using Galenica.Core.Audit;

namespace Galenica.Identity;

public enum SignInStep { Failed, LockedOut, Inactive, NeedsMfaEnrolment, NeedsMfaCode, Success }

/// <summary>
/// Two-step sign-in policy: password, then MFA. Accounts without MFA can only enrol, never use the
/// system. Failed passwords and failed MFA codes both count towards lockout. All outcomes are audited.
/// </summary>
public sealed class SignInService(UserManager<PasUser> users, MfaService mfa, AuditWriter audit, TimeProvider clock)
{
    public async Task<SignInStep> CheckPasswordAsync(string userName, string password, string? workstation = null)
    {
        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            await audit.WriteAsync(userName, "LoginFailed", "User", userName, reason: "Unknown user", workstation: workstation);
            return SignInStep.Failed;
        }
        if (!user.IsActive)
        {
            await audit.WriteAsync(userName, "LoginFailed", "User", user.Id.ToString(), reason: "Inactive account", workstation: workstation);
            return SignInStep.Inactive;
        }
        if (await users.IsLockedOutAsync(user))
        {
            await audit.WriteAsync(userName, "LoginFailed", "User", user.Id.ToString(), reason: "Locked out", workstation: workstation);
            return SignInStep.LockedOut;
        }
        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            var locked = await users.IsLockedOutAsync(user);
            await audit.WriteAsync(userName, "LoginFailed", "User", user.Id.ToString(), reason: locked ? "Wrong password – now locked out" : "Wrong password", workstation: workstation);
            return locked ? SignInStep.LockedOut : SignInStep.Failed;
        }
        return user.TwoFactorEnabled ? SignInStep.NeedsMfaCode : SignInStep.NeedsMfaEnrolment;
    }

    /// <summary>Second step. Call only after <see cref="CheckPasswordAsync"/> returned NeedsMfaCode.</summary>
    public async Task<SignInStep> CheckMfaAsync(string userName, string codeOrRecoveryCode, string? workstation = null)
    {
        var user = await users.FindByNameAsync(userName);
        if (user is null || !user.IsActive || !user.TwoFactorEnabled) return SignInStep.Failed;
        if (await users.IsLockedOutAsync(user)) return SignInStep.LockedOut;

        var ok = await mfa.VerifyTotpAsync(user, codeOrRecoveryCode)
                 || (codeOrRecoveryCode.Trim().Length > 6 && await mfa.RedeemRecoveryCodeAsync(user, codeOrRecoveryCode));
        if (!ok)
        {
            await users.AccessFailedAsync(user);
            var locked = await users.IsLockedOutAsync(user);
            await audit.WriteAsync(userName, "LoginFailed", "User", user.Id.ToString(), reason: "Wrong MFA code", workstation: workstation);
            return locked ? SignInStep.LockedOut : SignInStep.Failed;
        }

        await users.ResetAccessFailedCountAsync(user);
        user.LastSignInAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        await audit.WriteAsync(userName, "Login", "User", user.Id.ToString(), workstation: workstation);
        return SignInStep.Success;
    }
}
