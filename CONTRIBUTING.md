# Contributing to PrintOS

## Flow

1. Pick an issue (every change starts with one). Check its dependencies first.
2. Branch from `dev`: `feat/<n>-short-name`, `chore/<n>-short-name` or `fix/<n>-short-name`.
3. Commit in small steps. Use conventional prefixes: `feat:`, `fix:`, `chore:`, `docs:`, `test:`.
4. Open a pull request into `dev`. Put `Closes #<n>` in the description and fill in the template.
5. CI must be green. Review the diff and leave review comments. Fix what is found.
6. Squash-merge and delete the branch.

## Continuous integration

`.github/workflows/ci.yml` runs on every pull request and on pushes to `dev`:

- Web: lint, typecheck, unit tests, build
- API: restore, build (warnings are errors), xUnit tests
- Docker images for api and web build

Recommended branch protection for `dev` (GitHub, Settings, Branches): require a pull request, require the status checks `Web (lint, typecheck, test, build)`, `API (build, test)` and `Docker images build` to pass, and block force pushes.

## Rules

- One issue per pull request; keep PRs small and focused.
- Server-side authorization for every endpoint; validate all input.
- Add or update tests with each change (xUnit for the API, Vitest/Playwright for the web).
- UI changes include a screenshot at phone width (375px) and in dark and light themes.
- All user-facing text goes through i18n (Dutch and English).
- Never commit secrets. Use `.env` locally and keep `.env.example` current.

## Labels and milestones

- Type: `type:feature`, `type:chore`, `type:spike`
- Area: `area:web`, `area:api`, `area:infra`, `area:design`
- Priority: `priority:P0` (MVP), `priority:P1` (V2), `priority:P2` (V3)
- Milestones: M0 Foundation, M1 MVP, M2 Business Ops (V2), M3 Automation (V3)
