using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace PrintOs.Api.Infrastructure.Storage;

public sealed class AzureBlobFileStorage(BlobContainerClient container) : IFileStorage
{
    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) =>
        await container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await container.GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
