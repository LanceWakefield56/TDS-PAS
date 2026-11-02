// TDS PAS – web host (skeleton). Feature code starts in Phase 0 once the specification is approved.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

// Liveness check used by the installer and monitoring.
app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "TDS PAS",
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}));

app.Run();
