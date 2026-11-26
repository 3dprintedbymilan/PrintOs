using Amazon.Runtime;
using Amazon.S3;
using Azure.Identity;
using Azure.Storage.Blobs;

namespace PrintOs.Api.Infrastructure.Storage;

public static class FileStorageRegistration
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryFileStorage>();
        services.AddSingleton(CreateFileStorage);
        return services;
    }

    private static IFileStorage CreateFileStorage(IServiceProvider services)
    {
        var options = services.GetRequiredService<IConfiguration>().GetSection("Storage").Get<FileStorageOptions>() ?? new FileStorageOptions();

        var provider = options.Provider?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(provider))
        {
            provider = !string.IsNullOrWhiteSpace(options.Endpoint) ? "s3"
                : !string.IsNullOrWhiteSpace(options.AzureAccountUrl) || !string.IsNullOrWhiteSpace(options.AzureConnectionString) ? "azure"
                : "none";
        }

        return provider switch
        {
            "memory" => services.GetRequiredService<InMemoryFileStorage>(),
            "s3" => CreateS3(options),
            "azure" => CreateAzure(options),
            _ => new UnconfiguredFileStorage(),
        };
    }

    private static S3FileStorage CreateS3(FileStorageOptions options)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = options.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = options.Region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };
        return new S3FileStorage(new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config), options.Bucket);
    }

    private static AzureBlobFileStorage CreateAzure(FileStorageOptions options)
    {
        var container = !string.IsNullOrWhiteSpace(options.AzureConnectionString)
            ? new BlobContainerClient(options.AzureConnectionString, options.AzureContainer)
            : new BlobContainerClient(new Uri($"{options.AzureAccountUrl!.TrimEnd('/')}/{options.AzureContainer}"), new DefaultAzureCredential());
        return new AzureBlobFileStorage(container);
    }
}
