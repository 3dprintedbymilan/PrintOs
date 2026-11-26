using System.Collections.Concurrent;

namespace PrintOs.Api.Infrastructure.Storage;

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> files = new();

    public IReadOnlyCollection<string> Keys => files.Keys.ToList();

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        files[key] = buffer.ToArray();
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult<Stream?>(files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes, writable: false) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        files.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
