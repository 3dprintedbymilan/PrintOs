namespace PrintOs.Api.Infrastructure.Storage;

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public sealed class FileStorageOptions
{
    public string? Provider { get; set; }

    public string? Endpoint { get; set; }

    public string Bucket { get; set; } = "printos";

    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string? AzureConnectionString { get; set; }

    public string? AzureAccountUrl { get; set; }

    public string AzureContainer { get; set; } = "printos-files";
}
