using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Setup;
using BrightPAS.Modules.Patients;

namespace BrightPAS.Web.Security;

/// <summary>
/// How a staff member asked for an export to be protected. Password is sent in the POST body (never in a URL) and is never stored or logged.
/// </summary>
public sealed record ExportOptions(bool Protect, string? Password, string? Format = null)
{
    /// <summary>"pdf" = password-protected PDF (opens in any PDF reader); anything else = encrypted ZIP.</summary>
    public bool WantsPdf => string.Equals(Format, "pdf", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Every file that leaves BrightPAS (CSV, transfer pack, reports, letters as files) goes through here: by default it is wrapped in an
/// AES-256 encrypted, password-protected ZIP. Administrators decide whether protection is required (default) or optional, and the
/// minimum password length. Every export is audited with whether it was protected.
/// </summary>
public sealed class SecureExportService(SettingsService settings, AuditWriter audit, TimeProvider clock)
{
    public sealed record ExportFile(string Name, byte[] Content);

    public async Task<IResult> ExportAsync(ExportOptions? options, string baseName, IReadOnlyList<ExportFile> files, string user, string what, string? patientId = null,
        CancellationToken ct = default)
    {
        var policy = await settings.SecurityAsync(ct);
        options ??= new ExportOptions(true, null);
        var protect = options.Protect || policy.ExportProtection == ExportProtection.Required;
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmm");
        if (!protect)
        {
            if (files.Count == 1)
            {
                await audit.WriteAsync(user, "Export", what, files[0].Name, patientId, reason: "NOT password-protected (allowed by policy)", ct: ct);
                return Results.File(files[0].Content, ContentType(files[0].Name), $"{baseName}-{stamp}{Path.GetExtension(files[0].Name)}");
            }
            await audit.WriteAsync(user, "Export", what, baseName, patientId, reason: $"{files.Count} files, NOT password-protected (allowed by policy)", ct: ct);
            return Results.File(Zip(files, null), "application/zip", $"{baseName}-{stamp}.zip");
        }
        var problem = PasswordProblem(options.Password, policy.MinExportPasswordLength);
        if (problem is not null) return Results.BadRequest(new { errors = new[] { problem }, passwordRequired = true, minLength = policy.MinExportPasswordLength });
        await audit.WriteAsync(user, "Export", what, baseName, patientId, reason: $"{files.Count} file(s), AES-256 encrypted ZIP", ct: ct);
        return Results.File(Zip(files, options.Password), "application/zip", $"{baseName}-{stamp}-protected.zip");
    }

    /// <summary>
    /// PDF export: with protection the PDF itself is AES-256 encrypted and opens with the password in any PDF reader (no 7-Zip needed).
    /// <paramref name="build"/> receives the password (null = unprotected, only when the policy allows).
    /// </summary>
    public async Task<IResult> ExportPdfAsync(ExportOptions? options, string baseName, Func<string?, byte[]> build, string user, string what, string? patientId = null, CancellationToken ct = default)
    {
        var policy = await settings.SecurityAsync(ct);
        options ??= new ExportOptions(true, null, "pdf");
        var protect = options.Protect || policy.ExportProtection == ExportProtection.Required;
        var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmm");
        if (!protect)
        {
            var plain = build(null);
            await audit.WriteAsync(user, "Export", what, baseName + ".pdf", patientId, reason: "PDF, NOT password-protected (allowed by policy)", ct: ct);
            return Results.File(plain, "application/pdf", $"{baseName}-{stamp}.pdf");
        }
        var problem = PasswordProblem(options.Password, policy.MinExportPasswordLength);
        if (problem is not null) return Results.BadRequest(new { errors = new[] { problem }, passwordRequired = true, minLength = policy.MinExportPasswordLength });
        var bytes = build(options.Password);
        await audit.WriteAsync(user, "Export", what, baseName + ".pdf", patientId, reason: "PDF, AES-256 password-protected", ct: ct);
        return Results.File(bytes, "application/pdf", $"{baseName}-{stamp}-protected.pdf");
    }

    public static string? PasswordProblem(string? password, int minLength)
    {
        if (string.IsNullOrEmpty(password)) return $"Enter a password for the protected file (at least {minLength} characters).";
        if (password.Length < minLength) return $"The password must be at least {minLength} characters.";
        if (password.Length > 128) return "The password is too long.";
        if (password.Distinct().Count() < 4) return "Choose a less predictable password.";
        return null;
    }

    public static byte[] Zip(IReadOnlyList<ExportFile> files, string? password)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipOutputStream(ms) { IsStreamOwner = false })
        {
            zip.SetLevel(6);
            if (password is not null) zip.Password = password;
            foreach (var f in files)
            {
                var entry = new ZipEntry(ZipEntry.CleanName(f.Name)) { DateTime = DateTime.Now, Size = f.Content.Length };
                if (password is not null) entry.AESKeySize = 256;
                zip.PutNextEntry(entry);
                zip.Write(f.Content, 0, f.Content.Length);
                zip.CloseEntry();
            }
            zip.Finish();
        }
        return ms.ToArray();
    }

    public static byte[] Utf8Bom(string text) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

    private static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".csv" => "text/csv", ".html" => "text/html", ".pdf" => "application/pdf", ".txt" => "text/plain", ".zpl" => "text/plain", _ => "application/octet-stream",
    };
}

/// <summary>Document encryption at rest with ASP.NET Data Protection (keys DPAPI-protected on the server).</summary>
public sealed class DataProtectionDocumentCipher(IDataProtectionProvider provider) : IDocumentCipher
{
    private readonly IDataProtector _p = provider.CreateProtector("BrightPAS.PatientDocuments.v1");
    public byte[] Protect(byte[] plain) => _p.Protect(plain);
    public byte[] Unprotect(byte[] cipher) => _p.Unprotect(cipher);
}
