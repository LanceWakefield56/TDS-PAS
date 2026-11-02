namespace Tds.Pas.Core.Modules;

/// <summary>
/// Holds the known module manifests and works out install order and safe enable/disable,
/// so the Setup Centre can guide users without breaking dependencies.
/// </summary>
public sealed class ModuleCatalogue
{
    private readonly Dictionary<string, ModuleManifest> _modules = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ModuleManifest> All => _modules.Values;

    public void Register(ModuleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.Id))
            throw new ArgumentException("Module id is required.", nameof(manifest));
        if (!_modules.TryAdd(manifest.Id, manifest))
            throw new ModuleDependencyException($"Module '{manifest.Id}' is already registered.");
    }

    public ModuleManifest Get(string id) =>
        _modules.TryGetValue(id, out var m) ? m : throw new ModuleDependencyException($"Unknown module '{id}'.");

    /// <summary>All dependencies of a module, including the implicit core dependency.</summary>
    public IEnumerable<string> DependenciesOf(string id)
    {
        var m = Get(id);
        var deps = new HashSet<string>(m.DependsOn, StringComparer.OrdinalIgnoreCase);
        if (!m.Id.Equals(ModuleManifest.CoreId, StringComparison.OrdinalIgnoreCase))
            deps.Add(ModuleManifest.CoreId);
        return deps;
    }

    /// <summary>
    /// Returns the requested modules plus everything they need, ordered so that each module
    /// comes after its dependencies. Throws for unknown or circular dependencies.
    /// </summary>
    public IReadOnlyList<ModuleManifest> ResolveInstallOrder(IEnumerable<string> requestedIds)
    {
        var ordered = new List<ModuleManifest>();
        var state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // false = visiting, true = done

        void Visit(string id, Stack<string> path)
        {
            if (!_modules.ContainsKey(id))
            {
                var by = path.Count > 0 ? $" (required by '{path.Peek()}')" : string.Empty;
                throw new ModuleDependencyException($"Missing module '{id}'{by}.");
            }
            if (state.TryGetValue(id, out var done))
            {
                if (done) return;
                var cycle = string.Join(" -> ", path.Reverse().Append(id));
                throw new ModuleDependencyException($"Circular module dependency: {cycle}.");
            }

            state[id] = false;
            path.Push(id);
            foreach (var dep in DependenciesOf(id).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                Visit(dep, path);
            path.Pop();
            state[id] = true;
            ordered.Add(_modules[id]);
        }

        foreach (var id in requestedIds)
            Visit(id, new Stack<string>());
        return ordered;
    }

    /// <summary>Enabled modules that depend on the given module and would break if it were disabled.</summary>
    public IReadOnlyList<string> BlockingDependents(string id, IEnumerable<string> enabledIds) =>
        enabledIds
            .Where(e => !e.Equals(id, StringComparison.OrdinalIgnoreCase))
            .Where(e => _modules.ContainsKey(e) && DependenciesOf(e).Contains(id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
