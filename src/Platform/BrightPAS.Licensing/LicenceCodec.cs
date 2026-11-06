using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrightPAS.Licensing;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LicenceDocument))]
[JsonSerializable(typeof(ActivationToken))]
internal sealed partial class LicenceJson : JsonSerializerContext;

/// <summary>
/// Reads and writes signed licence and activation files:
/// <code>
/// -----BEGIN BRIGHTPAS LICENCE-----
/// base64url(JSON) . base64url(ECDSA P-256 / SHA-256 signature)
/// -----END BRIGHTPAS LICENCE-----
/// </code>
/// The JSON is signed exactly as stored, so formatting can never break or bypass the check.
/// </summary>
public static class LicenceCodec
{
    public const string LicenceLabel = "BRIGHTPAS LICENCE";
    public const string ActivationLabel = "BRIGHTPAS ACTIVATION";

    public static string Sign(LicenceDocument doc, ECDsa privateKey) =>
        Wrap(LicenceLabel, JsonSerializer.SerializeToUtf8Bytes(doc, LicenceJson.Default.LicenceDocument), privateKey, Describe(doc));

    public static string Sign(ActivationToken token, ECDsa privateKey) =>
        Wrap(ActivationLabel, JsonSerializer.SerializeToUtf8Bytes(token, LicenceJson.Default.ActivationToken), privateKey, null);

    public static VerifyResult<LicenceDocument> VerifyLicence(string text, IReadOnlyList<TrustedKey> keys) =>
        Verify(text, LicenceLabel, keys, b => JsonSerializer.Deserialize(b, LicenceJson.Default.LicenceDocument));

    public static VerifyResult<ActivationToken> VerifyActivation(string text, IReadOnlyList<TrustedKey> keys) =>
        Verify(text, ActivationLabel, keys, b => JsonSerializer.Deserialize(b, LicenceJson.Default.ActivationToken));

    /// <summary>Human-readable summary lines placed above the signed block (not trusted, for people only).</summary>
    private static string Describe(LicenceDocument d) =>
        $"# BrightPAS licence {d.LicenceId} – {d.Customer}{(d.Site is null ? "" : " / " + d.Site)}\n" +
        $"# Edition {d.Edition}; valid to {d.ExpiresOn:yyyy-MM-dd}; modules: {string.Join(", ", d.Modules)}\n" +
        $"# Locked to installation: {d.InstallationCode ?? "(any – not for production)"}\n" +
        "# Do not edit: any change makes the licence invalid.\n";

    private static string Wrap(string label, byte[] payload, ECDsa key, string? comment)
    {
        var sig = key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var body = B64(payload) + "." + B64(sig);
        var sb = new StringBuilder();
        if (comment is not null) sb.Append(comment);
        sb.Append("-----BEGIN ").Append(label).Append("-----\n");
        for (var i = 0; i < body.Length; i += 64) sb.Append(body, i, Math.Min(64, body.Length - i)).Append('\n');
        sb.Append("-----END ").Append(label).Append("-----\n");
        return sb.ToString();
    }

    private static VerifyResult<T> Verify<T>(string text, string label, IReadOnlyList<TrustedKey> keys, Func<byte[], T?> read) where T : class
    {
        if (string.IsNullOrWhiteSpace(text)) return VerifyResult<T>.Fail("The licence is empty.");
        var begin = $"-----BEGIN {label}-----";
        var end = $"-----END {label}-----";
        var b = text.IndexOf(begin, StringComparison.Ordinal);
        var e = text.IndexOf(end, StringComparison.Ordinal);
        if (b < 0 || e < b) return VerifyResult<T>.Fail("This is not a BrightPAS licence file (the BEGIN/END lines are missing).");
        var body = new string(text[(b + begin.Length)..e].Where(c => !char.IsWhiteSpace(c)).ToArray());
        var parts = body.Split('.');
        if (parts.Length != 2) return VerifyResult<T>.Fail("The licence file is damaged.");
        byte[] payload, sig;
        try { payload = UnB64(parts[0]); sig = UnB64(parts[1]); }
        catch (FormatException) { return VerifyResult<T>.Fail("The licence file is damaged."); }

        foreach (var k in keys)
        {
            if (!k.Key.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) continue;
            try
            {
                var value = read(payload);
                return value is null ? VerifyResult<T>.Fail("The licence content is empty.") : new VerifyResult<T>(value, null, k);
            }
            catch (JsonException) { return VerifyResult<T>.Fail("The licence content could not be read."); }
        }
        return VerifyResult<T>.Fail("The licence signature is not valid. The file was changed, or it was not issued for BrightPAS.");
    }

    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] UnB64(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}

public sealed record VerifyResult<T>(T? Value, string? Error, TrustedKey? Key) where T : class
{
    public bool Ok => Value is not null;
    public static VerifyResult<T> Fail(string error) => new(null, error, null);
}

/// <summary>A public key that licences may be signed with.</summary>
public sealed record TrustedKey(string Name, ECDsa Key, bool IsDevelopment)
{
    public static TrustedKey FromPem(string name, string pem, bool isDevelopment)
    {
        var k = ECDsa.Create();
        k.ImportFromPem(pem);
        return new TrustedKey(name, k, isDevelopment);
    }
}

/// <summary>The vendor public keys built into this copy of BrightPAS.</summary>
public static class VendorKeys
{
    public static IReadOnlyList<TrustedKey> BuiltIn(bool allowDevelopmentKey = true)
    {
        var list = new List<TrustedKey>();
        var asm = typeof(VendorKeys).Assembly;
        foreach (var (res, name, dev) in new[] { ("keys.production.pem", "BrightPAS production key", false), ("keys.dev.pem", "BrightPAS DEVELOPMENT key", true) })
        {
            if (dev && !allowDevelopmentKey) continue;
            using var s = asm.GetManifestResourceStream(res);
            if (s is null) continue;
            using var r = new StreamReader(s);
            list.Add(TrustedKey.FromPem(name, r.ReadToEnd(), dev));
        }
        return list;
    }

    public static bool HasProductionKey => typeof(VendorKeys).Assembly.GetManifestResourceNames().Contains("keys.production.pem");
}
