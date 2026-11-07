using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace BrightPAS.Identity;

/// <summary>Creates the default roles and their permission claims (safe to run repeatedly).</summary>
public sealed class RoleSeeder(RoleManager<PasRole> roles)
{
    public async Task EnsureDefaultRolesAsync()
    {
        foreach (var (name, (description, permissions)) in DefaultRoles.Definitions)
        {
            var role = await roles.FindByNameAsync(name);
            if (role is null)
            {
                role = new PasRole { Name = name, Description = description };
                EnsureSucceeded(await roles.CreateAsync(role), name);
            }
            // A default permission is added only once ("seeded" marker), so permissions an administrator removed are not put back on the next start.
            var claims = await roles.GetClaimsAsync(role);
            var existing = claims.Where(c => c.Type == Permissions.ClaimType).Select(c => c.Value).ToHashSet();
            var seeded = claims.Where(c => c.Type == Permissions.SeededClaimType).Select(c => c.Value).ToHashSet();
            foreach (var p in permissions.Where(p => !seeded.Contains(p)))
            {
                if (!existing.Contains(p)) EnsureSucceeded(await roles.AddClaimAsync(role, new Claim(Permissions.ClaimType, p)), name);
                EnsureSucceeded(await roles.AddClaimAsync(role, new Claim(Permissions.SeededClaimType, p)), name);
            }
        }
    }

    private static void EnsureSucceeded(IdentityResult r, string role)
    {
        if (!r.Succeeded)
            throw new InvalidOperationException($"Could not set up role '{role}': {string.Join("; ", r.Errors.Select(e => e.Description))}");
    }
}
