using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BrightPAS.Modules.Messaging;

/// <summary>
/// TLS for MLLP and DICOM links (0.35.0, ADR 0048). TLS 1.2 or 1.3 only. BrightPAS's own certificate is found by thumbprint in the
/// Windows certificate store (Local Machine › Personal, then Current User › Personal). A partner's certificate can be pinned by thumbprint –
/// needed for self-signed device certificates; otherwise the normal Windows trust check applies.
/// </summary>
public static class TlsSupport
{
    public const SslProtocols Protocols = SslProtocols.Tls12 | SslProtocols.Tls13;

    public static string? Normalise(string? thumbprint) =>
        string.IsNullOrWhiteSpace(thumbprint) ? null : new string([.. thumbprint.Where(char.IsAsciiHexDigit)]).ToUpperInvariant();

    /// <summary>Finds a certificate with its private key by thumbprint, or null.</summary>
    public static X509Certificate2? Find(string? thumbprint)
    {
        var t = Normalise(thumbprint);
        if (t is null) return null;
        foreach (var location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
        {
            try
            {
                using var store = new X509Store(StoreName.My, location);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, t, validOnly: false);
                var c = found.FirstOrDefault(x => x.HasPrivateKey);
                if (c is not null) return c;
            }
            catch (CryptographicException) { }
            catch (PlatformNotSupportedException) { }
        }
        return null;
    }

    /// <summary>Why a link's TLS settings can't work, or null when they can.</summary>
    public static string? Check(bool listener, string? ownThumbprint, string? peerThumbprint)
    {
        if (listener && Normalise(ownThumbprint) is null) return "A TLS listener needs BrightPAS's certificate – enter its thumbprint (Windows certificate store, Local Machine › Personal).";
        if (Normalise(ownThumbprint) is { } own && own.Length != 40 && own.Length != 64) return "The certificate thumbprint should be 40 letters and numbers.";
        if (Normalise(peerThumbprint) is { } peer && peer.Length != 40 && peer.Length != 64) return "The partner's certificate thumbprint should be 40 letters and numbers.";
        if (Normalise(ownThumbprint) is { } t && Find(t) is null) return "BrightPAS's certificate was not found in the certificate store (or has no private key).";
        return null;
    }

    private static bool Pinned(X509Certificate? cert, string pin) =>
        cert is not null && string.Equals(new X509Certificate2(cert).Thumbprint, pin, StringComparison.OrdinalIgnoreCase);

    /// <summary>Server side: our certificate; when a partner thumbprint is set, the partner must present that client certificate.</summary>
    public static async Task<SslStream> AcceptAsync(Stream inner, X509Certificate2 own, string? peerThumbprint, CancellationToken ct)
    {
        var pin = Normalise(peerThumbprint);
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false, (_, cert, _, errors) => pin is null || Pinned(cert, pin));
        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
        {
            ServerCertificate = own,
            ClientCertificateRequired = pin is not null,
            EnabledSslProtocols = Protocols,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        }, ct);
        return ssl;
    }

    /// <summary>Client side: checks the receiver's certificate (pinned thumbprint, or normal trust for the host name); sends our certificate if we have one.</summary>
    public static async Task<SslStream> ConnectAsync(Stream inner, string host, X509Certificate2? own, string? peerThumbprint, CancellationToken ct)
    {
        var pin = Normalise(peerThumbprint);
        var ssl = new SslStream(inner, leaveInnerStreamOpen: false, (_, cert, _, errors) => pin is not null ? Pinned(cert, pin) : errors == SslPolicyErrors.None);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            ClientCertificates = own is null ? null : new X509CertificateCollection { own },
            EnabledSslProtocols = Protocols,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        }, ct);
        return ssl;
    }
}
