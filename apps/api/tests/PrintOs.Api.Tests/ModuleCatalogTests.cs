using PrintOs.Api.Modules;

namespace PrintOs.Api.Tests;

public class ModuleCatalogTests
{
    [Fact]
    public void DiscoverModules_FindsAllRegisteredModules()
    {
        var moduleNames = ModuleCatalog.DiscoverModules().Select(module => module.Name).ToList();

        Assert.Equal(["Costing", "Identity", "Inventory", "ModelLibrary", "Production", "Products"], moduleNames.Order().ToList());
    }
}
