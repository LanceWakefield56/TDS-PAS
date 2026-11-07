// BrightPAS – web host.
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using BrightPAS.Core.Audit;
using BrightPAS.Core.Data;
using BrightPAS.Core.Modules;
using BrightPAS.Identity;
using BrightPAS.Modules.Patients;
using BrightPAS.Web.Api;
using BrightPAS.Web.Security;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

var connectionString = builder.Configuration.GetConnectionString("Pas")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Pas' is not configured. Run Setup or set it in appsettings.Production.json.");

void UseSql(DbContextOptionsBuilder o, string schema) =>
    o.UseSqlServer(connectionString, s => s.MigrationsHistoryTable("__EFMigrationsHistory", schema).EnableRetryOnFailure());

// Data Protection keys (cookie encryption) persisted outside the web folder; DPAPI-protected on Windows.
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    var dp = builder.Services.AddDataProtection().SetApplicationName("BrightPAS").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
    if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(protectToLocalMachine: true);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new PasDatabaseOptions(UseSql));
builder.Services.AddDbContext<PasDbContext>(o => UseSql(o, PasDbContext.Schema));
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<AuditQueryService>();
builder.Services.AddSingleton<BrightPAS.Web.Licensing.LicenceCache>();
builder.Services.AddScoped<BrightPAS.Web.Licensing.LicensingService>();
builder.Services.AddHttpClient("licensing", c => c.Timeout = TimeSpan.FromSeconds(20));
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Licensing.LicenceHeartbeat>();
builder.Services.AddPasIdentity(o => UseSql(o, PasIdentityDbContext.Schema));
builder.Services.AddPasModules([new PatientsModule(), new BrightPAS.Modules.Labs.LabsModule(), new BrightPAS.Modules.Theatre.TheatreModule(),
    new BrightPAS.Modules.Forms.FormsModule(), new BrightPAS.Modules.Billing.BillingModule(), new BrightPAS.Modules.Reminders.RemindersModule(),
    new BrightPAS.Modules.Referrals.ReferralsModule(), new BrightPAS.Modules.Letters.LettersModule()]);
builder.Services.AddScoped<BrightPAS.Core.Organisation.OrganisationService>();
builder.Services.AddScoped<RoleAdminService>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.HealthLinkWorker>();
builder.Services.AddScoped<BrightPAS.Web.Licensing.ModuleSetupService>();
builder.Services.AddSingleton<BrightPAS.Modules.Reminders.ISecretProtector, BrightPAS.Web.Integration.DataProtectionSecretProtector>();
// Installation defaults chosen in Setup (Reminders:SmsProvider / Reminders:EmailProvider); Modica + SMTP when not set.
builder.Services.AddSingleton(new BrightPAS.Modules.Reminders.ReminderDefaults(
    builder.Configuration.GetValue("Reminders:SmsProvider", BrightPAS.Modules.Reminders.SmsProvider.Modica),
    builder.Configuration.GetValue("Reminders:EmailProvider", BrightPAS.Modules.Reminders.EmailProvider.Smtp)));
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.ReminderWorker>();
builder.Services.AddHostedService<BrightPAS.Web.Integration.MllpListener>();
builder.Services.AddPasAuthentication(builder.Environment);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("setup", w => { w.PermitLimit = 10; w.Window = TimeSpan.FromMinutes(1); w.QueueLimit = 0; });
    o.AddFixedWindowLimiter("auth", w => { w.PermitLimit = 30; w.Window = TimeSpan.FromMinutes(1); w.QueueLimit = 0; });
    // Patient-facing questionnaire links: per client IP. Kept separate from vendor callbacks so a burst of
    // SMS delivery receipts can never lock patients out of their forms (and vice versa).
    o.AddPolicy("patient-links", ctx => RateLimitPartition.GetFixedWindowLimiter("links:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 40, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // SMS vendor callbacks (replies and delivery receipts): per client IP, sized for vendor bursts.
    o.AddPolicy("vendor-callbacks", ctx => RateLimitPartition.GetFixedWindowLimiter("hooks:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Idempotent start-up seeding: default roles and, on a brand-new installation, the TDS organisation structure.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<RoleSeeder>().EnsureDefaultRolesAsync();
    var core = scope.ServiceProvider.GetRequiredService<PasDbContext>();
    if (!await core.Organisations.AnyAsync())
    {
        core.Organisations.Add(BrightPAS.Core.Organisation.TdsDefaultSetup.Create());
        await core.SaveChangesAsync();
    }
    await scope.ServiceProvider.GetRequiredService<BrightPAS.Web.Licensing.LicensingService>().RefreshAsync(heartbeat: true);
}

app.UseHttpsRedirection();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
    // HSTS: browsers must only ever use HTTPS for this site (set directly so it also applies to https://localhost).
    if (ctx.Request.IsHttps && !app.Environment.IsDevelopment()) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UsePasCsrfProtection(); // before sign-in checks, so forged requests are refused the same way whether or not a cookie is present
app.UseAuthentication();
app.UseAuthorization();

// Liveness check used by the installer and monitoring.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "BrightPAS", version = typeof(Program).Assembly.GetName().Version?.ToString() })).AllowAnonymous();

// First administrator set-up (one-time code from the installer; MFA required before the code is used up).
app.MapGet("/api/setup/status", async (PasDbContext core, Microsoft.AspNetCore.Identity.UserManager<PasUser> users) =>
{
    var state = await core.SetupState.SingleOrDefaultAsync(s => s.Id == 1);
    var adminReady = (await users.GetUsersInRoleAsync(DefaultRoles.SystemAdministrator)).Any(u => u.TwoFactorEnabled);
    return Results.Ok(new { firstAdminNeeded = !adminReady && state?.BootstrapCodeHash is not null && state.BootstrapCodeUsedAt is null });
}).AllowAnonymous();
var setup = app.MapGroup("/api/setup/first-admin").RequireRateLimiting("setup").AllowAnonymous();
setup.MapPost("/start", async (StartFirstAdmin req, FirstAdminService svc) =>
{
    var r = await svc.StartAsync(req.Code, req.UserName, req.DisplayName, req.Password);
    return r.Succeeded
        ? Results.Ok(new { sharedKey = r.Enrolment!.SharedKey, otpAuthUri = r.Enrolment.OtpAuthUri })
        : Results.BadRequest(new { error = r.Error.ToString(), messages = r.Messages });
});
setup.MapPost("/complete", async (CompleteFirstAdmin req, FirstAdminService svc) =>
{
    var r = await svc.CompleteAsync(req.Code, req.UserName, req.TotpCode);
    return r.Succeeded
        ? Results.Ok(new { recoveryCodes = r.RecoveryCodes })
        : Results.BadRequest(new { error = r.Error.ToString() });
});

app.MapAuthEndpoints();
app.MapPatientEndpoints();
app.MapOrganisationEndpoints();
app.MapAdminEndpoints();
app.MapModuleEndpoints();
app.MapRemindersEndpoints();
app.MapReferralsEndpoints();
app.MapLettersEndpoints();

// Unknown /api routes return 404; everything else serves the single-page app.
app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous(); // the app shell itself holds no data; every API call is checked

app.Run();

internal sealed record StartFirstAdmin(string Code, string UserName, string DisplayName, string Password);
internal sealed record CompleteFirstAdmin(string Code, string UserName, string TotpCode);

public partial class Program;
