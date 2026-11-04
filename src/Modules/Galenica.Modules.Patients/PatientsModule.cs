using Microsoft.Extensions.DependencyInjection;
using Galenica.Core.Modules;

namespace Galenica.Modules.Patients;

public sealed class PatientsModule : IPasModule
{
    public ModuleManifest Manifest { get; } = new(
        "patients", "Patient administration", typeof(PatientsModule).Assembly.GetName().Version ?? new Version(0, 0),
        PatientsDbContext.Schema, [], "Patient register, NHI, alerts and duplicate checks.");

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContext<PatientsDbContext>((sp, o) => sp.GetRequiredService<PasDatabaseOptions>().Configure(o, PatientsDbContext.Schema));
        services.AddScoped<PatientService>();
    }
}
