# File storage

The API stores model files and thumbnails behind `IFileStorage` (`Infrastructure/Storage`). The provider is chosen by configuration:

| Setting | Provider |
| --- | --- |
| `Storage:Provider` = `S3`, `Azure` or `Memory` | explicit choice |
| `Storage:Endpoint` set | S3-compatible (SeaweedFS in docker-compose, any S3 API) using `Storage:Bucket`, `Storage:AccessKey`, `Storage:SecretKey`, `Storage:Region` |
| `Storage:AzureAccountUrl` set | Azure Blob, container `Storage:AzureContainer` (default `printos-files`), authenticated with `DefaultAzureCredential` (managed identity) |
| `Storage:AzureConnectionString` set | Azure Blob with a connection string |
| nothing set | uploads return 503; nothing is stored in memory by accident |

`Memory` exists for tests only.

Keys are `{workspaceId}/models/{modelId}/{versionId}.{format}` (thumbnails: `thumb-{guid}.{ext}`), so they are unique per workspace and never user supplied. Storage keys and credentials are never returned by the API.

Downloads go through `GET /api/model-files/{token}`. The token is a time-limited data-protection payload (model versions: 5 minutes, thumbnails: 1 hour) bound to a workspace and file; it is handed out by authorized endpoints (`.../versions/{id}/download-url`, `thumbnailUrl` in list/detail). Nothing is public and nothing points at the storage endpoint, so the storage account can stay private.

Limits (`ModelLibrary:MaxModelFileBytes`, default 100 MB; `ModelLibrary:MaxThumbnailBytes`, default 2 MB) apply per file. File type is checked by extension and by content (STL, 3MF, OBJ; PNG, JPEG, WebP).

Deploying: the Azure container app still needs `Storage__AzureAccountUrl` and a managed identity with the Storage Blob Data Contributor role on `stprintosprod`. That wiring lives with the infrastructure issue (#62).
