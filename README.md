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

## Running the apps

Each app documents its own commands once it exists:

- Web: see `apps/web/README.md`
- API: see `apps/api/README.md`
- Full local stack: see issue #3 (Docker Compose)

## Working agreement

Every change goes through an issue, a branch and a pull request. See `CONTRIBUTING.md` once issue #9 is merged.

## Milestones

M0 Foundation, M1 MVP, M2 Business Ops (V2), M3 Automation (V3). See the GitHub issues and milestones.
