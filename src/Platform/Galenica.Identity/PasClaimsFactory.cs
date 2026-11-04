using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Galenica.Identity;

/// <summary>Builds the signed-in user's claims: identity, roles and the permissions granted by those roles.</summary>
public sealed class PasClaimsFactory(UserManager<PasUser> users, RoleManager<PasRole> roles)
{
    public const string DisplayNameClaim = "pas:name";
    public const string SecurityStampClaim = "pas:stamp";

    public async Task<ClaimsPrincipal> CreateAsync(PasUser user, string authenticationScheme)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(DisplayNameClaim, user.DisplayName),
            new(SecurityStampClaim, await users.GetSecurityStampAsync(user) ?? string.Empty),
            new("amr", "mfa")
        };
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleName in await users.GetRolesAsync(user))
        {
            claims.Add(new Claim(ClaimTypes.Role, roleName));
            var role = await roles.FindByNameAsync(roleName);
            if (role is null) continue;
            foreach (var c in await roles.GetClaimsAsync(role))
                if (c.Type == Permissions.ClaimType) permissions.Add(c.Value);
        }
        claims.AddRange(permissions.Order(StringComparer.Ordinal).Select(p => new Claim(Permissions.ClaimType, p)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
    }
}
