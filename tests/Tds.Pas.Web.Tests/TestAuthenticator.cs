using System.Security.Cryptography;

namespace Tds.Pas.Web.Tests;

/// <summary>RFC 6238 TOTP generator, acting as the user's authenticator app in tests.</summary>
public static class TestAuthenticator
{
    public static string Code(string sharedKey, DateTimeOffset? at = null)
    {
        var key = Base32Decode(sharedKey.Replace(" ", "").ToUpperInvariant());
        var counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var hash = HMACSHA1.HashData(key, msg);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string s)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0; var value = 0; var output = new List<byte>();
        foreach (var c in s.TrimEnd('='))
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8) { output.Add((byte)(value >> (bits - 8))); bits -= 8; }
        }
        return output.ToArray();
    }
}
