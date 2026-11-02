using Microsoft.AspNetCore.Identity;

namespace Tds.Pas.Identity;

/// <summary>
/// Password rules following NIST SP 800-63B: long passwords (12+ characters, enforced in options),
/// no forced character mixes, but common, organisation-related and username-based passwords are refused.
/// </summary>
public sealed class PasPasswordValidator : IPasswordValidator<PasUser>
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "password1234", "passw0rd", "qwerty", "qwertyuiop",
        "123456789012", "1234567890", "111111111111", "letmein", "welcome", "welcome1", "welcome123", "iloveyou",
        "admin", "administrator", "changeme", "default", "summer2026", "winter2026", "spring2026", "autumn2026",
        "monkey", "dragon", "football", "rugby", "allblacks", "newzealand", "aotearoa", "kiwi", "nelson",
        "abcdefghijkl", "trustno1", "superman", "baseball", "sunshine", "princess", "starwars", "whatever"
    };

    private static readonly string[] OrganisationWords = ["tasman", "daysurgery", "tdspas", "tds"];

    public Task<IdentityResult> ValidateAsync(UserManager<PasUser> manager, PasUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return Task.FromResult(Failed("PasswordRequired", "A password is required."));

        var simplified = new string(password.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        // Strip trailing digits so "password123456" is treated like "password".
        var withoutTrailingDigits = simplified.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        if (Common.Contains(password) || Common.Contains(simplified) || Common.Contains(withoutTrailingDigits))
            return Task.FromResult(Failed("PasswordTooCommon", "This password is too common. Choose a longer, unique passphrase."));

        if (OrganisationWords.Any(w => simplified.Contains(w, StringComparison.Ordinal)) && simplified.Length < 20)
            return Task.FromResult(Failed("PasswordOrganisation", "Do not base your password on the organisation name."));

        if (!string.IsNullOrEmpty(user.UserName) && user.UserName.Length >= 3 &&
            password.Contains(user.UserName, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Failed("PasswordContainsUserName", "Your password must not contain your username."));

        if (password.Distinct().Count() < 5)
            return Task.FromResult(Failed("PasswordRepetitive", "Your password is too repetitive."));

        return Task.FromResult(IdentityResult.Success);
    }

    private static IdentityResult Failed(string code, string description) =>
        IdentityResult.Failed(new IdentityError { Code = code, Description = description });
}
