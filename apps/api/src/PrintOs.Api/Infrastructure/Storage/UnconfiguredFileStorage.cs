namespace PrintOs.Api.Infrastructure.Storage;

public sealed class StorageNotConfiguredException()
    : InvalidOperationException("File storage is not configured. Set Storage:Endpoint (S3) or Storage:AzureAccountUrl / Storage:AzureConnectionString (Azure Blob).");

public sealed class UnconfiguredFileStorage : IFileStorage
{
    public Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) => throw new StorageNotConfiguredException();

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => throw new StorageNotConfiguredException();

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new StorageNotConfiguredException();
}
