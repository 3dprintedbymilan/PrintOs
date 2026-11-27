using PrintOs.Api.Modules;

namespace PrintOs.Api.Tests;

public class ModuleCatalogTests
{
    [Fact]
    public void DiscoverModules_FindsAllRegisteredModules()
    {
        var moduleNames = ModuleCatalog.DiscoverModules().Select(module => module.Name).ToList();

        Assert.Equal(["Audit", "Bambu", "Costing", "Identity", "Inventory", "Licensing", "ModelLibrary", "Production", "Products"], moduleNames.Order().ToList());
    }
}
