# PrintOS

From filament to profit. An internal production and business management system for 3D Printed by Milan: materials, models, pricing, orders, stock and market sales.

## Repository layout

| Path | Purpose |
|---|---|
| `apps/web` | Nuxt 3 / Vue 3 / TypeScript PWA (mobile-first, iPhone look) |
| `apps/api` | ASP.NET Core (.NET 10) modular monolith, EF Core, PostgreSQL |
| `docs` | Requirements, design notes and decisions |

## Tech stack

Nuxt / Vue 3 / TypeScript PWA, ASP.NET Core .NET 10, EF Core + PostgreSQL, ASP.NET Core Identity, SignalR, Quartz.NET, S3-compatible storage, Docker Compose, GitHub Actions, xUnit and Playwright.

## Running the full stack

Requires Docker.

```powershell
docker compose up -d --build
```

| Service | URL |
|---|---|
| Web | http://localhost:3000 |
| API | http://localhost:5000 (`/health/ready`, `/openapi/v1.json`) |
| PostgreSQL | localhost:5432 (user, password and database `printos`) |
| S3-compatible storage (SeaweedFS) | http://localhost:8333, bucket `printos` |

Data persists in Docker volumes; `docker compose down -v` wipes it. Copy `.env.example` to `.env` to change ports or credentials. The API applies migrations on startup in this setup.

Each app documents its own commands: `apps/web/README.md`, `apps/api/README.md`.

## Working agreement

Every change goes through an issue, a branch and a pull request. See `CONTRIBUTING.md` once issue #9 is merged.

## Milestones

M0 Foundation, M1 MVP, M2 Business Ops (V2), M3 Automation (V3). See the GitHub issues and milestones.
