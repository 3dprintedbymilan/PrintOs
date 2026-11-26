# Azure production hosting

PrintOS production runs on Azure Container Apps. Development and testing stay local with Docker Compose. Tracked in issue #62.

## Why Container Apps

The subscription has zero App Service Basic quota and support tickets are refused for it, and West Europe is closed to new customers. Container Apps has its own quota and runs the Docker images the repo already builds.

## Resources (resource group `rg-printos-prod`)

| Resource | Name | Region | Notes |
|---|---|---|---|
| PostgreSQL flexible server | `psql-printos-ne` | North Europe | PostgreSQL 18, Burstable B1ms, 32 GiB with autogrow, 14-day backups, database `printos` |
| Container Registry | `crprintosprod` | Sweden Central | Basic; holds `printos-api` and `printos-web` images |
| Container Apps environment | `managedEnvironment-rgprintosprod-9928` | North Europe | Consumption |
| Container App (API) | `ca-printos-api` | North Europe | port 8080 |
| Container App (web) | `ca-printos-web` | North Europe | port 3000 |
| Storage account | `stprintosprod` | Sweden Central | private container `printos-files` |
| Application Insights | `appi-printos-prod` | Sweden Central | |

Regions: PostgreSQL provisioning failed twice in Sweden Central (the same subscription succeeded in North Europe), so the database and apps are in North Europe. The registry, storage and Application Insights stay in Sweden Central.

## API settings (Container App environment variables)

| Name | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ConnectionStrings__Default` | secret reference; `Host=psql-printos-ne.postgres.database.azure.com;Port=5432;Database=printos;Username=printosadmin;Password=<secret>;Ssl Mode=Require` |
| `Database__MigrateOnStartup` | `true` (single replica; move migrations into the pipeline before scaling out) |
| `Cors__AllowedOrigins__0` | the web app URL |

The web app gets `NUXT_PUBLIC_API_BASE` from the deploy workflow.

## Deploying

`.github/workflows/deploy.yml` builds both images, pushes them to the registry, updates both container apps and runs a smoke check on `/health/ready`. It runs on pushes to `dev` that touch `apps/**`, or manually.

One-time setup it depends on:

1. A Microsoft Entra app registration with a federated credential for this repository and the `production` environment.
2. That identity needs the Contributor role on `rg-printos-prod` and AcrPull plus AcrPush on the registry.
3. GitHub repository variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` (identifiers, not passwords). The workflow skips itself until `AZURE_CLIENT_ID` is set.
4. A GitHub environment named `production` with a required reviewer.
5. AcrPull for the system-assigned identities of both container apps on the registry.

## Costs (estimates, check Cost Management)

- PostgreSQL B1ms + 32 GiB: about 17 USD per month (a free offer may reduce it)
- Container Registry Basic: about 5 USD per month
- Container Apps: free monthly allowance, then per use
- Storage and Application Insights: cents, first 5 GB of logs free

## Runbook

- **Deploy:** merge to `dev` and approve the `production` deployment, or run the workflow manually.
- **Roll back:** in the container app, Revisions, activate the previous revision.
- **Restore the database:** PostgreSQL server, Backup and restore (point in time, creates a new server), then update the connection string secret.
- **Rotate the database password:** change it on the server, update the secret, restart the API revision.
