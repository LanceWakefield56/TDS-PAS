// BrightPAS Licence Server – optional online activation service run by the vendor (ADR 0009).
// Configuration (appsettings.json or environment):
//   LicenceServer:DataFolder      folder holding issued/*.lic, revoked.txt and activations.json
//   LicenceServer:PrivateKeyPath  vendor private key (PEM); password from BRIGHTPAS_KEY_PASSWORD
//   LicenceServer:TokenDays       how long each activation reply is valid (default 14)
// POST /api/activate {licenceId, installationCode} -> signed activation token (text/plain) or 4xx {error}
// A licence binds to the first installation that activates it; to move it, delete its entry in activations.json.
using System.Security.Cryptography;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using BrightPAS.Licensing;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("activate", w => { w.PermitLimit = 30; w.Window = TimeSpan.FromMinutes(1); });
});
var app = builder.Build();
app.UseRateLimiter();

var cfg = app.Configuration.GetSection("LicenceServer");
var folder = cfg["DataFolder"] ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(Path.Combine(folder, "issued"));
var tokenDays = cfg.GetValue("TokenDays", 14);
using var key = ECDsa.Create();

// Pin the process culture to New Zealand English so the server's regional settings never change number/date formats (HL7, CSV, JSON).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-NZ");
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-NZ");
var pem = File.ReadAllText(cfg["PrivateKeyPath"] ?? throw new InvalidOperationException("LicenceServer:PrivateKeyPath is not set."));
if (pem.Contains("ENCRYPTED", StringComparison.Ordinal)) key.ImportFromEncryptedPem(pem, Environment.GetEnvironmentVariable("BRIGHTPAS_KEY_PASSWORD") ?? "");
else key.ImportFromPem(pem);
var trusted = new[] { new TrustedKey("server", key, false) };
var gate = new SemaphoreSlim(1, 1);

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "BrightPAS licence server" }));

app.MapPost("/api/activate", async (ActivateRequest req, ILogger<Program> log) =>
{
    if (string.IsNullOrWhiteSpace(req.LicenceId) || string.IsNullOrWhiteSpace(req.InstallationCode))
        return Results.BadRequest(new { error = "licenceId and installationCode are required." });
    var file = Directory.EnumerateFiles(Path.Combine(folder, "issued"), "*.lic")
        .Select(f => LicenceCodec.VerifyLicence(File.ReadAllText(f), trusted).Value)
        .FirstOrDefault(d => d?.LicenceId == req.LicenceId);
    if (file is null) return Results.NotFound(new { error = "Unknown licence." });
    var revokedPath = Path.Combine(folder, "revoked.txt");
    if (File.Exists(revokedPath) && File.ReadAllLines(revokedPath).Any(l => l.Trim() == req.LicenceId))
        return Results.Json(new { error = "This licence has been revoked. Contact the vendor." }, statusCode: 403);
    var code = LicenceEvaluator.Normalise(req.InstallationCode);
    if (file.InstallationCode is not null && LicenceEvaluator.Normalise(file.InstallationCode) != code)
        return Results.Conflict(new { error = "This licence is for a different installation." });

    await gate.WaitAsync();
    try
    {
        var path = Path.Combine(folder, "activations.json");
        var map = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [] : [];
        if (map.TryGetValue(req.LicenceId, out var bound) && bound != code)
            return Results.Conflict(new { error = "This licence is already active on another installation. Contact the vendor to move it." });
        map[req.LicenceId] = code;
        File.WriteAllText(path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
    }
    finally { gate.Release(); }

    var now = DateTimeOffset.UtcNow;
    log.LogInformation("Activated {Licence} on {Code}", req.LicenceId, code);
    return Results.Text(LicenceCodec.Sign(new ActivationToken
    {
        LicenceId = req.LicenceId,
        InstallationCode = req.InstallationCode.Trim().ToUpperInvariant(),
        IssuedAt = now,
        ValidUntil = now.AddDays(tokenDays)
    }, key), "text/plain");
}).RequireRateLimiting("activate");

app.Run();

internal sealed record ActivateRequest(string? LicenceId, string? InstallationCode, string? Product, string? Version);
