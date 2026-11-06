using BrightPAS.Core.Modules;

namespace BrightPAS.Core.Tests;

public class ModuleCatalogueTests
{
    private static ModuleManifest M(string id, params string[] deps) => new(id, id, new Version(1, 0), id, deps);

    private static ModuleCatalogue Catalogue()
    {
        var c = new ModuleCatalogue();
        c.Register(M("core"));
        c.Register(M("patients"));
        c.Register(M("scheduling", "patients"));
        c.Register(M("billing", "patients", "scheduling"));
        c.Register(M("workforce"));
        return c;
    }

    [Fact]
    public void Install_order_puts_dependencies_first_and_includes_core()
    {
        var order = Catalogue().ResolveInstallOrder(["billing"]).Select(m => m.Id).ToList();
        Assert.Equal(["core", "patients", "scheduling", "billing"], order);
    }

    [Fact]
    public void Install_order_has_no_duplicates_for_shared_dependencies()
    {
        var order = Catalogue().ResolveInstallOrder(["billing", "scheduling", "workforce"]).Select(m => m.Id).ToList();
        Assert.Equal(order.Count, order.Distinct().Count());
        Assert.True(order.IndexOf("patients") < order.IndexOf("scheduling"));
        Assert.Contains("workforce", order);
    }

    [Fact]
    public void Missing_dependency_is_reported_with_the_requiring_module()
    {
        var c = Catalogue();
        c.Register(M("endoscopy", "imaging"));
        var ex = Assert.Throws<ModuleDependencyException>(() => c.ResolveInstallOrder(["endoscopy"]));
        Assert.Contains("imaging", ex.Message);
        Assert.Contains("endoscopy", ex.Message);
    }

    [Fact]
    public void Circular_dependency_is_rejected()
    {
        var c = new ModuleCatalogue();
        c.Register(M("core"));
        c.Register(M("a", "b"));
        c.Register(M("b", "a"));
        var ex = Assert.Throws<ModuleDependencyException>(() => c.ResolveInstallOrder(["a"]));
        Assert.Contains("Circular", ex.Message);
    }

    [Fact]
    public void Duplicate_registration_is_rejected()
    {
        var c = Catalogue();
        Assert.Throws<ModuleDependencyException>(() => c.Register(M("billing")));
    }

    [Fact]
    public void Cannot_disable_a_module_that_enabled_modules_depend_on()
    {
        var blocking = Catalogue().BlockingDependents("patients", ["core", "patients", "scheduling", "billing"]);
        Assert.Equal(["billing", "scheduling"], blocking);
    }

    [Fact]
    public void Can_disable_a_module_nothing_depends_on()
    {
        Assert.Empty(Catalogue().BlockingDependents("workforce", ["core", "patients", "workforce"]));
    }
}
