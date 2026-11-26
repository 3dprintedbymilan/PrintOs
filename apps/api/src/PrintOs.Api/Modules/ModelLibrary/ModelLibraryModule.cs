using Microsoft.AspNetCore.Http.Features;
using PrintOs.Api.Infrastructure.Storage;

namespace PrintOs.Api.Modules.ModelLibrary;

public sealed class ModelLibraryModule : IModule
{
    public string Name => "ModelLibrary";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddFileStorage();
        services.Configure<ModelLibraryOptions>(configuration.GetSection("ModelLibrary"));
        services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = ModelFileRules.RequestSizeCeilingBytes);
        services.AddSingleton<ModelFileSigner>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapModelLibraryEndpoints();
    }
}
