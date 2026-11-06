using Microsoft.Extensions.DependencyInjection;

namespace BrightPAS.Core.Modules;

/// <summary>Contract implemented by every PAS module.</summary>
public interface IPasModule
{
    ModuleManifest Manifest { get; }

    /// <summary>Registers the module's services with the application.</summary>
    void ConfigureServices(IServiceCollection services);
}
