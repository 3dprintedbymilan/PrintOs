using System.Reflection;

namespace PrintOs.Api.Modules;

public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> DiscoverModules() =>
        Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IModule).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .Select(type => (IModule)Activator.CreateInstance(type)!)
            .ToList();

    public static void RegisterModules(this IServiceCollection services, IConfiguration configuration, IEnumerable<IModule> modules)
    {
        foreach (var module in modules)
        {
            module.RegisterServices(services, configuration);
        }
    }

    public static void MapModules(this IEndpointRouteBuilder endpoints, IEnumerable<IModule> modules)
    {
        foreach (var module in modules)
        {
            module.MapEndpoints(endpoints);
        }
    }
}
