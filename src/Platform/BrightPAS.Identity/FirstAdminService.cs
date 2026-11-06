using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Setup;
using BrightPAS.Shared;

namespace BrightPAS.Identity;

public enum FirstAdminError { None, AlreadyCompleted, InvalidCode, CodeExpired, PasswordRejected, InvalidMfaCode, NotStarted }

public sealed record FirstAdminResult(FirstAdminError Error, TotpEnrolment? Enrolment = null, IReadOnlyList<string>? RecoveryCodes = null, IReadOnlyList<string>? Messages = null)
{
    public bool Succeeded => Error == FirstAdminError.None;
}

/// <summary>
/// Creates the first System Administrator using the one-time code shown by the installer, then
/// requires MFA enrolment before the code is marked as used.
/// </summary>
public sealed class FirstAdminService(UserManager<PasUser> users, MfaService mfa, PasDbContext core, AuditWriter audit, TimeProvider clock)
{
    public async Task<FirstAdminResult> StartAsync(string bootstrapCode, string userName, string displayName, string password)
    {
        var state = await core.SetupState.SingleOrDefaultAsync(s => s.Id == 1);
        var check = CheckCode(state, bootstrapCode);
        if (check != FirstAdminError.None) return new(check);
        if ((await users.GetUsersInRoleAsync(DefaultRoles.SystemAdministrator)).Any(u => u.TwoFactorEnabled))
            return new(FirstAdminError.AlreadyCompleted);

        var user = await users.FindByNameAsync(userName);
        if (user is null)
        {
            user = new PasUser { UserName = userName, DisplayName = displayName, CreatedAt = clock.GetUtcNow() };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
                return new(FirstAdminError.PasswordRejected, Messages: created.Errors.Select(e => e.Description).ToList());
            await users.AddToRoleAsync(user, DefaultRoles.SystemAdministrator);
            await audit.WriteAsync("setup", "Create", "User", user.Id.ToString(), reason: "First administrator");
        }
        return new(FirstAdminError.None, await mfa.BeginTotpEnrolmentAsync(user));
    }

    public async Task<FirstAdminResult> CompleteAsync(string bootstrapCode, string userName, string totpCode)
    {
        var state = await core.SetupState.SingleOrDefaultAsync(s => s.Id == 1);
        var check = CheckCode(state, bootstrapCode);
        if (check != FirstAdminError.None) return new(check);

        var user = await users.FindByNameAsync(userName);
        if (user is null || !await users.IsInRoleAsync(user, DefaultRoles.SystemAdministrator)) return new(FirstAdminError.NotStarted);

        var recovery = await mfa.CompleteTotpEnrolmentAsync(user, totpCode);
        if (recovery is null) return new(FirstAdminError.InvalidMfaCode);

        state!.BootstrapCodeUsedAt = clock.GetUtcNow();
        await core.SaveChangesAsync();
        await audit.WriteAsync(userName, "MfaEnrolled", "User", user.Id.ToString(), reason: "First administrator set-up completed");
        return new(FirstAdminError.None, RecoveryCodes: recovery);
    }

    private FirstAdminError CheckCode(SetupState? state, string code)
    {
        if (state?.BootstrapCodeHash is null) return FirstAdminError.InvalidCode;
        if (state.BootstrapCodeUsedAt is not null) return FirstAdminError.AlreadyCompleted;
        if (state.BootstrapCodeCreatedAt is null || clock.GetUtcNow() - state.BootstrapCodeCreatedAt > SetupState.BootstrapLifetime)
            return FirstAdminError.CodeExpired;
        return BootstrapCode.Verify(code, state.BootstrapCodeHash) ? FirstAdminError.None : FirstAdminError.InvalidCode;
    }
}
