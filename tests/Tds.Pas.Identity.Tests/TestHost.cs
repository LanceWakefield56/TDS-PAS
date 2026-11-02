using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tds.Pas.Core.Audit;
using Tds.Pas.Core.Data;

namespace Tds.Pas.Identity.Tests;

/// <summary>In-memory test host: identity and core databases on SQLite, controllable clock.</summary>
public sealed class TestHost : IAsyncDisposable
{
    private readonly SqliteConnection _identityConn = new("DataSource=:memory:");
    private readonly SqliteConnection _coreConn = new("DataSource=:memory:");
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public ServiceProvider Services { get; }

    public TestHost()
    {
        _identityConn.Open();
        _coreConn.Open();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<TimeProvider>(Clock);
        s.AddDbContext<PasDbContext>(o => o.UseSqlite(_coreConn));
        s.AddScoped<AuditWriter>();
        s.AddPasIdentity(o => o.UseSqlite(_identityConn));
        Services = s.BuildServiceProvider();
        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PasIdentityDbContext>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<PasDbContext>().Database.EnsureCreated();
        scope.ServiceProvider.GetRequiredService<RoleSeeder>().EnsureDefaultRolesAsync().GetAwaiter().GetResult();
    }

    public AsyncServiceScope Scope() => Services.CreateAsyncScope();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _identityConn.DisposeAsync();
        await _coreConn.DisposeAsync();
    }
}

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
