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

// Child process for text recognition (see OutOfProcessOcrEngine): keeps native OCR crashes away from the website.
if (args.Length > 0 && args[0] == "--ocr-worker") { Environment.Exit(BrightPAS.Web.Integration.OutOfProcessOcrEngine.RunWorker(args)); }

// Pin the process culture to New Zealand English so the server's regional settings never change number/date formats (HL7, CSV, JSON).
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-NZ");
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en-NZ");

var builder = WebApplication.CreateBuilder(args);
BrightPAS.Web.Security.PasLogging.Configure(builder); // log files, error references, crash capture (0.12.2)
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

builder.Services.AddSingleton<TimeProvider>(new BrightPAS.Web.Demo.DemoClock(TimeProvider.System)); // real time; the demo-data loader can wind it back for its own work
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
    new BrightPAS.Modules.Referrals.ReferralsModule(), new BrightPAS.Modules.Letters.LettersModule(),
    new BrightPAS.Modules.Rostering.RosteringModule(), new BrightPAS.Modules.Credentialing.CredentialingModule(), new BrightPAS.Modules.Portal.PortalModule(),
    new BrightPAS.Modules.Messaging.MessagingModule(), new BrightPAS.Modules.Assets.AssetsModule(), new BrightPAS.Modules.Emr.EmrModule(), new BrightPAS.Modules.Endoscopy.EndoscopyModule(), new BrightPAS.Modules.Scribe.ScribeModule(), new BrightPAS.Modules.Devices.DevicesModule(), new BrightPAS.Modules.Quality.QualityModule(), new BrightPAS.Modules.Coding.CodingModule(), new BrightPAS.Modules.Estimates.EstimatesModule(), new BrightPAS.Modules.Triage.TriageModule()]);
builder.Services.AddScoped<BrightPAS.Core.Organisation.OrganisationService>();
builder.Services.AddScoped<RoleAdminService>();
builder.Services.AddScoped<BrightPAS.Core.Setup.SettingsService>();
builder.Services.AddScoped<BrightPAS.Web.Security.SecureExportService>();
builder.Services.AddSingleton<BrightPAS.Modules.Patients.IDocumentCipher, BrightPAS.Web.Security.DataProtectionDocumentCipher>();
builder.Services.AddScoped<BrightPAS.Web.Integration.OffboardingService>();
builder.Services.AddSingleton<BrightPAS.Web.Integration.OutOfProcessOcrEngine>();
builder.Services.AddSingleton<BrightPAS.Web.Integration.IOcrEngine>(sp => sp.GetRequiredService<BrightPAS.Web.Integration.OutOfProcessOcrEngine>());
builder.Services.AddScoped<BrightPAS.Modules.Patients.IDocumentTextExtractor, BrightPAS.Web.Integration.DocumentTextExtractor>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.DocumentTextWorker>();
foreach (var t in new[] { typeof(BrightPAS.Web.Integration.TheatreMerge), typeof(BrightPAS.Web.Integration.FormsMerge), typeof(BrightPAS.Web.Integration.BillingMerge),
    typeof(BrightPAS.Web.Integration.LabsMerge), typeof(BrightPAS.Web.Integration.ReferralsMerge), typeof(BrightPAS.Web.Integration.LettersMerge),
    typeof(BrightPAS.Web.Integration.RemindersMerge), typeof(BrightPAS.Web.Integration.PortalMerge), typeof(BrightPAS.Web.Integration.MessagingMerge), typeof(BrightPAS.Web.Integration.AssetsMerge), typeof(BrightPAS.Web.Integration.EmrMerge), typeof(BrightPAS.Web.Integration.EndoscopyMerge), typeof(BrightPAS.Web.Integration.ScribeMerge), typeof(BrightPAS.Web.Integration.DevicesMerge), typeof(BrightPAS.Web.Integration.QualityMerge), typeof(BrightPAS.Web.Integration.CodingMerge), typeof(BrightPAS.Web.Integration.EstimatesMerge), typeof(BrightPAS.Web.Integration.TriageMerge) })
    builder.Services.AddScoped(typeof(BrightPAS.Modules.Patients.IPatientMergeParticipant), t);
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.HealthLinkWorker>();
builder.Services.AddScoped<BrightPAS.Web.Licensing.ModuleSetupService>();
// Demonstration data (0.12.3): loaded once when Setup asks for it on a new database.
builder.Services.AddScoped<BrightPAS.Web.Demo.DemoDataSeeder>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Demo.DemoDataWorker>();
builder.Services.AddSingleton<BrightPAS.Modules.Reminders.ISecretProtector, BrightPAS.Web.Integration.DataProtectionSecretProtector>();
// Installation defaults chosen in Setup (Reminders:SmsProvider / Reminders:EmailProvider); Modica + SMTP when not set.
builder.Services.AddSingleton(new BrightPAS.Modules.Reminders.ReminderDefaults(
    builder.Configuration.GetValue("Reminders:SmsProvider", BrightPAS.Modules.Reminders.SmsProvider.Modica),
    builder.Configuration.GetValue("Reminders:EmailProvider", BrightPAS.Modules.Reminders.EmailProvider.Smtp)));
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.ReminderWorker>();
builder.Services.AddHostedService<BrightPAS.Web.Integration.MllpListener>();
// Messaging engine (ADR 0021): fo-dicom networking, live listener state, inbound pipeline and the engine host.
FellowOakDicom.IServiceCollectionExtension.AddFellowOakDicom(builder.Services);
builder.Services.AddSingleton<BrightPAS.Web.Integration.MessagingRuntime>();
builder.Services.AddScoped<BrightPAS.Web.Integration.MessagingPipeline>();
builder.Services.AddScoped<BrightPAS.Web.Integration.FileImportService>();
builder.Services.AddSingleton<BrightPAS.Modules.Quality.IIndicatorCalculators, BrightPAS.Web.Integration.QualityIndicatorCalculators>();
builder.Services.AddScoped<BrightPAS.Web.Integration.VideoArchive>();
// Procedure estimates (0.21.0, ADR 0034): printed estimate, mail through the Reminders mail server, held-email outbox worker.
builder.Services.AddSingleton<BrightPAS.Modules.Estimates.IEstimatePdf, BrightPAS.Web.Pdf.EstimatePdf>();
builder.Services.AddScoped<BrightPAS.Modules.Estimates.IEstimateMailer, BrightPAS.Web.Integration.EstimateMailer>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.EstimatesWorker>();
// Patient triage (0.22.0, ADR 0035): booking hold until sign-off, re-triage scan.
builder.Services.AddScoped<BrightPAS.Core.Modules.IBookingConfirmGate, BrightPAS.Web.Integration.TriageBookingGate>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.TriageWorker>();
builder.Services.AddHttpClient("messaging", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("ai", c => c.Timeout = TimeSpan.FromSeconds(60));
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.MessagingHost>();
builder.Services.AddSingleton<BrightPAS.Web.Integration.EndoscopyDicomHost>();
builder.Services.AddSingleton<BrightPAS.Web.Integration.ScribeEngines>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService(sp => sp.GetRequiredService<BrightPAS.Web.Integration.EndoscopyDicomHost>());
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.DevicesWorker>();
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<BrightPAS.Web.Integration.CssdLogWatcher>();
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
    // Patient portal links: per client IP, separate from questionnaire links.
    o.AddPolicy("patient-portal", ctx => RateLimitPartition.GetFixedWindowLimiter("portal:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // SMS vendor callbacks (replies and delivery receipts): per client IP, sized for vendor bursts.
    o.AddPolicy("vendor-callbacks", ctx => RateLimitPartition.GetFixedWindowLimiter("hooks:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Inbound REST interfaces (other systems posting messages): per client IP, sized for interface volumes.
    // MCP server for AI tools: per client IP.
    o.AddPolicy("mcp", ctx => RateLimitPartition.GetFixedWindowLimiter("mcp:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("integration-in", ctx => RateLimitPartition.GetFixedWindowLimiter("in:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
FellowOakDicom.DicomSetupBuilder.UseServiceProvider(app.Services);

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

app.UsePasErrorHandling();
app.UseHttpsOnly(builder.Configuration.GetValue("Https:Port", 443));
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=()"; // camera: endoscopy capture card; microphone: BrightPAS Scribe
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

// Website log for administrators (no patient data is written to the log).
app.MapGet("/api/admin/logs", (string? level, int? take, IConfiguration config) =>
    Results.Ok(BrightPAS.Web.Security.PasLogging.Recent(config, level ?? "WRN", Math.Clamp(take ?? 200, 1, 1000)))).RequireAuthorization(Permissions.UsersManage);

// Liveness check used by the installer and monitoring.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "BrightPAS", version = typeof(Program).Assembly.GetName().Version?.ToString() })).AllowAnonymous();

// First administrator set-up (one-time code from the installer; MFA required before the code is used up).
app.MapGet("/api/setup/status", async (PasDbContext core, Microsoft.AspNetCore.Identity.UserManager<PasUser> users) =>
{
    var state = await core.SetupState.SingleOrDefaultAsync(s => s.Id == 1);
    var adminReady = (await users.GetUsersInRoleAsync(DefaultRoles.SystemAdministrator)).Any(u => u.TwoFactorEnabled);
    // Demonstration systems only (never set on a real installation): show the shared demo sign-in details on the sign-in page.
    var demoSettings = await core.Settings.AsNoTracking().Where(x => x.Key == BrightPAS.Web.Demo.DemoDataSeeder.StateKey || x.Key == "demo.banner").ToDictionaryAsync(x => x.Key, x => x.Value);
    var demoState = demoSettings.GetValueOrDefault(BrightPAS.Web.Demo.DemoDataSeeder.StateKey) ?? (demoSettings.GetValueOrDefault("demo.banner") == "true" ? "done" : null);
    object? demo = demoState is null ? null : new
    {
        state = demoState,
        password = demoState == "done" ? BrightPAS.Web.Demo.DemoAccounts.Password : null,
        authenticatorSecret = demoState == "done" ? BrightPAS.Web.Demo.DemoAccounts.TotpSecret : null,
        accounts = demoState == "done" ? BrightPAS.Web.Demo.DemoAccounts.All.Select(a => new { a.UserName, a.DisplayName, a.Purpose }) : null
    };
    return Results.Ok(new { firstAdminNeeded = demoState is null && !adminReady && state?.BootstrapCodeHash is not null && state.BootstrapCodeUsedAt is null, demo });
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
app.MapRosterEndpoints();
app.MapPatientRecordsEndpoints();
app.MapPortalEndpoints();
app.MapMessagingEndpoints();
app.MapAssetsEndpoints();
app.MapEmrEndpoints();
app.MapEndoscopyEndpoints();
app.MapScribeEndpoints();
app.MapDevicesEndpoints();
app.MapImportEndpoints();
app.MapQualityEndpoints();
app.MapCodingEndpoints();
app.MapEstimatesEndpoints();
app.MapTriageEndpoints();
app.MapMcpAndAiEndpoints();

// Unknown /api routes return 404; everything else serves the single-page app.
app.MapFallback("/api/{**rest}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous(); // the app shell itself holds no data; every API call is checked

try { app.Run(); }
catch (Exception ex) when (ex is not OperationCanceledException) { BrightPAS.Web.Security.PasLogging.StartupFailed(ex, builder.Configuration); throw; }

internal sealed record StartFirstAdmin(string Code, string UserName, string DisplayName, string Password);
internal sealed record CompleteFirstAdmin(string Code, string UserName, string TotpCode);

public partial class Program;
