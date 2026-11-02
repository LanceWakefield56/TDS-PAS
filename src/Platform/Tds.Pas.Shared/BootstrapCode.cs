using System.Security.Cryptography;
using System.Text;

namespace Tds.Pas.Shared;

/// <summary>
/// One-time setup code shown by the installer and typed in by the person creating the first
/// administrator account. Only a salted PBKDF2 hash is stored in the database.
/// </summary>
public static class BootstrapCode
{
    // Unambiguous characters (no 0/O, 1/I/L).
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int Iterations = 210_000;

    /// <summary>Generates a code such as K7QM-3XRP-WD9T-H2BN.</summary>
    public static string Generate()
    {
        var chars = new char[16];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var s = new string(chars);
        return $"{s[..4]}-{s[4..8]}-{s[8..12]}-{s[12..]}";
    }

    public static string Normalise(string code) =>
        new(code.ToUpperInvariant().Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Returns "pbkdf2$iterations$salt$hash" (base64 parts).</summary>
    public static string Hash(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Normalise(code)), salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string code, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2" || !int.TryParse(parts[1], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Normalise(code)), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
