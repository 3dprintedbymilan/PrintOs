# PrintOS API

ASP.NET Core (.NET 10) modular monolith with EF Core and PostgreSQL.

## Structure

```
src/PrintOs.Api
  Modules/            one folder per module (Identity, Inventory, ModelLibrary, Products, Production)
  Infrastructure/     persistence, error handling
```

Each module implements `IModule` (service registration and endpoint mapping). Modules are discovered at startup, so adding a module is adding one class. Entity configurations in any module are picked up by `PrintOsDbContext`.

## Run

Requires the .NET 10 SDK and a PostgreSQL database (see issue #3 for Docker Compose).

```powershell
dotnet run --project src/PrintOs.Api
```

- `GET /health/live`: process is up
- `GET /health/ready`: database reachable
- `GET /openapi/v1.json`: OpenAPI document (Development only)

Configuration: `ConnectionStrings:Default` (override with the `ConnectionStrings__Default` environment variable). Each environment uses its own database.

## Authentication

ASP.NET Core Identity with opaque bearer tokens (access 30 min, refresh 14 days).

| Endpoint | Purpose |
|---|---|
| `POST /api/auth/sign-up` | creates a workspace and its owner (`email`, `password`, `workspaceName`) |
| `POST /api/auth/sign-in` | returns `accessToken` and `refreshToken`; 5 failed attempts lock the account for 15 minutes (423) |
| `POST /api/auth/refresh` | exchanges a refresh token for new tokens |
| `POST /api/auth/sign-out` | needs a token; rotates the security stamp so existing refresh tokens stop working |
| `GET /api/workspace` | the caller's own workspace (policy `RequireWorkspace`) |

Passwords need 10+ characters with upper case, lower case and a digit. Tokens carry a `workspace_id` claim; entities that implement `IWorkspaceScoped` get an automatic query filter and are stamped with the caller's workspace on save. Token protection keys are stored in the database (data protection), so tokens survive restarts and work across replicas.

## Tests

xUnit v3 (`tests/PrintOs.Api.Tests`). Integration tests start a real PostgreSQL container with Testcontainers, so Docker must be running.

```powershell
dotnet test
```

`global.json` opts into the Microsoft.Testing.Platform runner required by xUnit v3 on .NET 10.

## Migrations

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/PrintOs.Api --output-dir Infrastructure/Persistence/Migrations
dotnet ef database update --project src/PrintOs.Api
```

## NuGet

`nuget.config` limits restore to nuget.org so builds are reproducible and ignore private feeds on a developer machine.
