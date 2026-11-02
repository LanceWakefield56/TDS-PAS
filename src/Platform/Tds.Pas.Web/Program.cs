// TDS PAS – web host.
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Tds.Pas.Core.Audit;
using Tds.Pas.Core.Data;
using Tds.Pas.Core.Modules;
using Tds.Pas.Identity;
using Tds.Pas.Modules.Patients;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.AddServerHeader = false);

var connectionString = builder.Configuration.GetConnectionString("Pas")
    ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Pas' is not configured. Run Setup or set it in appsettings.Production.json.");

void UseSql(DbContextOptionsBuilder o, string schema) =>
    o.UseSqlServer(connectionString, s => s.MigrationsHistoryTable("__EFMigrationsHistory", schema).EnableRetryOnFailure());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new PasDatabaseOptions(UseSql));
builder.Services.AddDbContext<PasDbContext>(o => UseSql(o, PasDbContext.Schema));
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddPasIdentity(o => UseSql(o, PasIdentityDbContext.Schema));
builder.Services.AddPasModules([new PatientsModule()]);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("setup", w => { w.PermitLimit = 10; w.Window = TimeSpan.FromMinutes(1); w.QueueLimit = 0; });
});
builder.Services.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; });

var app = builder.Build();

// Idempotent start-up seeding: default roles and, on a brand-new installation, the TDS organisation structure.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<RoleSeeder>().EnsureDefaultRolesAsync();
    var core = scope.ServiceProvider.GetRequiredService<PasDbContext>();
    if (!await core.Organisations.AnyAsync())
    {
        core.Organisations.Add(Tds.Pas.Core.Organisation.TdsDefaultSetup.Create());
        await core.SaveChangesAsync();
    }
}

if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    h["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    h["Cache-Control"] = "no-store";
    await next();
});
app.UseRateLimiter();

// Liveness check used by the installer and monitoring.
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "TDS PAS", version = typeof(Program).Assembly.GetName().Version?.ToString() }));

// First administrator set-up (one-time code from the installer; MFA required before the code is used up).
var setup = app.MapGroup("/api/setup/first-admin").RequireRateLimiting("setup");
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

app.Run();

internal sealed record StartFirstAdmin(string Code, string UserName, string DisplayName, string Password);
internal sealed record CompleteFirstAdmin(string Code, string UserName, string TotpCode);
