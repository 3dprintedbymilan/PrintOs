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

## Migrations

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/PrintOs.Api --output-dir Infrastructure/Persistence/Migrations
dotnet ef database update --project src/PrintOs.Api
```

## NuGet

`nuget.config` limits restore to nuget.org so builds are reproducible and ignore private feeds on a developer machine.
