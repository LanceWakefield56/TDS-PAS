using BrightPAS.Modules.Patients;

namespace BrightPAS.Modules.Scribe;

/// <summary>
/// Consultation audio, encrypted at rest (Data Protection, same as patient documents) in numbered parts under the scribe folder
/// (config Scribe:AudioPath, default &lt;data&gt;\scribe written by Setup). Deleted when the note is signed or discarded, or after the retention period.
/// </summary>
public sealed class AudioStore(IDocumentCipher cipher, Microsoft.Extensions.Configuration.IConfiguration? config = null)
{
    public string Root => config?["Scribe:AudioPath"] is { Length: > 0 } p ? p
        : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BrightPAS", "Scribe")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".brightpas", "scribe");

    private string Folder(Guid session) => Path.Combine(Root, session.ToString("N"));

    public async Task AppendAsync(Guid session, int seq, byte[] data, CancellationToken ct = default)
    {
        var dir = Folder(session);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(Path.Combine(dir, $"{seq:D5}.part"), cipher.Protect(data), ct);
    }

    public async Task<byte[]> ReadAllAsync(Guid session, CancellationToken ct = default)
    {
        var dir = Folder(session);
        if (!Directory.Exists(dir)) return [];
        using var ms = new MemoryStream();
        foreach (var f in Directory.GetFiles(dir, "*.part").Order(StringComparer.Ordinal))
            ms.Write(cipher.Unprotect(await File.ReadAllBytesAsync(f, ct)));
        return ms.ToArray();
    }

    public void Delete(Guid session)
    {
        var dir = Folder(session);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    public (bool Ok, string Detail) Check()
    {
        try
        {
            Directory.CreateDirectory(Root);
            var probe = Path.Combine(Root, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok"); File.Delete(probe);
            return (true, Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (false, $"{Root}: {ex.Message}"); }
    }
}
