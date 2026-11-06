using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;

namespace BrightPAS.Identity;

public sealed record UserSummary(Guid Id, string UserName, string DisplayName, bool IsActive, bool MfaEnabled, bool LockedOut,
    DateTimeOffset CreatedAt, DateTimeOffset? LastSignInAt, IReadOnlyList<string> Roles);

public sealed record AdminResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static readonly AdminResult Ok = new(true, []);
    public static AdminResult Fail(params string[] errors) => new(false, errors);
}

/// <summary>User account administration for System Administrators. Every change is audited.</summary>
public sealed class UserAdminService(UserManager<PasUser> users, RoleManager<PasRole> roles, AuditWriter audit, TimeProvider clock)
{
    public async Task<IReadOnlyList<UserSummary>> ListAsync()
    {
        var list = new List<UserSummary>();
        foreach (var u in await users.Users.OrderBy(u => u.DisplayName).ToListAsync())
            list.Add(await SummaryAsync(u));
        return list;
    }

    public async Task<IReadOnlyList<(string Name, string? Description)>> RolesAsync() =>
        (await roles.Roles.OrderBy(r => r.Name).ToListAsync()).Select(r => (r.Name!, r.Description)).ToList();

    public async Task<(AdminResult Result, UserSummary? User)> CreateAsync(string userName, string displayName, IReadOnlyList<string> roleNames, string password, string actor)
    {
        userName = userName.Trim();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(displayName)) return (AdminResult.Fail("Name and username are required."), null);
        if (roleNames.Count == 0) return (AdminResult.Fail("Choose at least one role."), null);
        var unknown = await UnknownRolesAsync(roleNames);
        if (unknown.Count > 0) return (AdminResult.Fail($"Unknown role(s): {string.Join(", ", unknown)}"), null);
        if (await users.FindByNameAsync(userName) is not null) return (AdminResult.Fail($"The username '{userName}' is already in use."), null);

        var user = new PasUser { UserName = userName, DisplayName = displayName.Trim(), CreatedAt = clock.GetUtcNow() };
        var r = await users.CreateAsync(user, password);
        if (!r.Succeeded) return (AdminResult.Fail(r.Errors.Select(e => e.Description).ToArray()), null);
        await users.AddToRolesAsync(user, roleNames);
        await audit.WriteAsync(actor, "UserCreated", "User", user.Id.ToString(), reason: $"{userName}; roles: {string.Join(", ", roleNames)}");
        return (AdminResult.Ok, await SummaryAsync(user));
    }

    public async Task<AdminResult> SetRolesAsync(Guid id, IReadOnlyList<string> roleNames, string actor)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return AdminResult.Fail("User not found.");
        if (roleNames.Count == 0) return AdminResult.Fail("Choose at least one role.");
        var unknown = await UnknownRolesAsync(roleNames);
        if (unknown.Count > 0) return AdminResult.Fail($"Unknown role(s): {string.Join(", ", unknown)}");
        var current = await users.GetRolesAsync(user);
        if (current.Contains(DefaultRoles.SystemAdministrator) && !roleNames.Contains(DefaultRoles.SystemAdministrator) && await IsLastActiveAdminAsync(user))
            return AdminResult.Fail("This is the last active System Administrator. Give another account that role first.");
        await users.RemoveFromRolesAsync(user, current.Except(roleNames));
        await users.AddToRolesAsync(user, roleNames.Except(current));
        await users.UpdateSecurityStampAsync(user); // new permissions take effect at next sign-in check
        await audit.WriteAsync(actor, "UserRolesChanged", "User", user.Id.ToString(), reason: $"{string.Join(", ", current)} -> {string.Join(", ", roleNames)}");
        return AdminResult.Ok;
    }

    public async Task<AdminResult> SetActiveAsync(Guid id, bool active, string actor)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return AdminResult.Fail("User not found.");
        if (!active && string.Equals(user.UserName, actor, StringComparison.OrdinalIgnoreCase)) return AdminResult.Fail("You cannot deactivate your own account.");
        if (!active && await users.IsInRoleAsync(user, DefaultRoles.SystemAdministrator) && await IsLastActiveAdminAsync(user))
            return AdminResult.Fail("This is the last active System Administrator and cannot be deactivated.");
        user.IsActive = active;
        await users.UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user); // signs the user out everywhere within 2 minutes
        await audit.WriteAsync(actor, active ? "UserReactivated" : "UserDeactivated", "User", user.Id.ToString(), reason: user.UserName);
        return AdminResult.Ok;
    }

    public async Task<AdminResult> UnlockAsync(Guid id, string actor)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return AdminResult.Fail("User not found.");
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await audit.WriteAsync(actor, "UserUnlocked", "User", user.Id.ToString(), reason: user.UserName);
        return AdminResult.Ok;
    }

    /// <summary>For a lost phone: removes the authenticator so the user must set up MFA again at next sign-in.</summary>
    public async Task<AdminResult> ResetMfaAsync(Guid id, string actor)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return AdminResult.Fail("User not found.");
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        await users.UpdateSecurityStampAsync(user);
        await audit.WriteAsync(actor, "UserMfaReset", "User", user.Id.ToString(), reason: user.UserName);
        return AdminResult.Ok;
    }

    public async Task<AdminResult> ResetPasswordAsync(Guid id, string newPassword, string actor)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return AdminResult.Fail("User not found.");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var r = await users.ResetPasswordAsync(user, token, newPassword);
        if (!r.Succeeded) return AdminResult.Fail(r.Errors.Select(e => e.Description).ToArray());
        await users.UpdateSecurityStampAsync(user);
        await audit.WriteAsync(actor, "UserPasswordReset", "User", user.Id.ToString(), reason: user.UserName);
        return AdminResult.Ok;
    }

    private async Task<List<string>> UnknownRolesAsync(IEnumerable<string> names)
    {
        var known = (await roles.Roles.Select(r => r.Name).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        return names.Where(n => !known.Contains(n)).ToList();
    }

    private async Task<bool> IsLastActiveAdminAsync(PasUser user) =>
        !(await users.GetUsersInRoleAsync(DefaultRoles.SystemAdministrator)).Any(u => u.Id != user.Id && u.IsActive);

    private async Task<UserSummary> SummaryAsync(PasUser u) =>
        new(u.Id, u.UserName!, u.DisplayName, u.IsActive, u.TwoFactorEnabled, await users.IsLockedOutAsync(u),
            u.CreatedAt, u.LastSignInAt, (await users.GetRolesAsync(u)).Order().ToList());
}
