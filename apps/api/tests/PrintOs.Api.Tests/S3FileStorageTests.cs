using Amazon.Runtime;
using Amazon.S3;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using PrintOs.Api.Infrastructure.Storage;

namespace PrintOs.Api.Tests;

public sealed class S3FileStorageTests : IAsyncLifetime
{
    private const string Bucket = "printos-test";

    private readonly IContainer seaweed = new ContainerBuilder("chrislusf/seaweedfs:latest")
        .WithCommand("server", "-dir=/data", "-s3", "-s3.port=8333")
        .WithPortBinding(8333, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8333).ForPath("/status").ForStatusCode(System.Net.HttpStatusCode.OK).ForStatusCode(System.Net.HttpStatusCode.Forbidden).ForStatusCode(System.Net.HttpStatusCode.NotFound)))
        .Build();

    private AmazonS3Client client = null!;

    public async ValueTask InitializeAsync()
    {
        await seaweed.StartAsync();
        client = new AmazonS3Client(
            new BasicAWSCredentials("any", "any"),
            new AmazonS3Config
            {
                ServiceURL = $"http://{seaweed.Hostname}:{seaweed.GetMappedPublicPort(8333)}",
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
        await client.PutBucketAsync(Bucket, TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await seaweed.DisposeAsync();
    }

    [Fact]
    public async Task SaveReadAndDelete_RoundTripThroughAnS3CompatibleServer()
    {
        var storage = new S3FileStorage(client, Bucket);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = $"ws/models/{Guid.NewGuid():N}/file.stl";

        await storage.SaveAsync(key, new MemoryStream([1, 2, 3, 4]), "model/stl", cancellationToken);
        await using (var stream = await storage.OpenReadAsync(key, cancellationToken))
        {
            Assert.NotNull(stream);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            Assert.Equal([1, 2, 3, 4], buffer.ToArray());
        }

        await storage.DeleteAsync(key, cancellationToken);
        Assert.Null(await storage.OpenReadAsync(key, cancellationToken));
    }
}
