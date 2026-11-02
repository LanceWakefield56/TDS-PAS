using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tds.Pas.Core.Modules;

/// <summary>How every PAS database context connects. Each module passes its own schema for its migration history.</summary>
public sealed class PasDatabaseOptions(Action<DbContextOptionsBuilder, string> configure)
{
    public void Configure(DbContextOptionsBuilder builder, string migrationsSchema) => configure(builder, migrationsSchema);
}

public static class ModuleServiceCollectionExtensions
{
    /// <summary>Registers the given modules in dependency order, plus the module catalogue.</summary>
    public static IServiceCollection AddPasModules(this IServiceCollection services, IEnumerable<IPasModule> modules)
    {
        var list = modules.ToList();
        var catalogue = new ModuleCatalogue();
        if (!list.Any(m => m.Manifest.Id.Equals(ModuleManifest.CoreId, StringComparison.OrdinalIgnoreCase)))
            catalogue.Register(new ModuleManifest(ModuleManifest.CoreId, "Platform core", typeof(ModuleCatalogue).Assembly.GetName().Version ?? new Version(0, 0), "core", []));
        foreach (var m in list) catalogue.Register(m.Manifest);

        var byId = list.ToDictionary(m => m.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in catalogue.ResolveInstallOrder(list.Select(m => m.Manifest.Id)))
            if (byId.TryGetValue(manifest.Id, out var module))
                module.ConfigureServices(services);

        services.AddSingleton(catalogue);
        return services;
    }
}
