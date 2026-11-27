# MVP release gate

Issue #32. This document defines when the MVP is "done", how the automated gate runs, what it cannot cover (manual QA) and which gaps are known and accepted.

## Definition of done for the MVP

The MVP (milestone M1) is releasable when all of these hold on `dev`:

1. **CI is green**: web (lint, typecheck, unit tests including the i18n catalogue check, build), mocked end-to-end suite (mobile and desktop), API (build with `-warnaserror`, all tests against Testcontainers Postgres), Docker images build, and the **MVP journey** job described below.
2. **The journey passes on mobile and desktop** against the real API and a real Postgres.
3. **Authorization and validation are covered**: every API module has tests for 401 without a token, 404 across workspaces, and 400 validation with camel-case field keys; the journey adds an end-to-end check (`journey/security.journey.spec.ts`).
4. **Backup and restore were rehearsed**: `scripts/backup/roundtrip-test.sh` (see "Restore from backup" below).
5. **Dutch and English are complete**: `tests/i18n.test.ts` passes (same keys, no empty values, same placeholders and plural arity, no missing or unused keys).
6. **The manual QA checklist below was walked through** on a real iPhone and against real Bambu Lab and Azure resources, and the result noted in the release PR.
7. **Known gaps (below) are accepted by the owner** or ticketed.

## Automated gate

### The journey (`apps/web/journey`, `npm run test:journey`)

`mvp.journey.spec.ts` is a single long walk through the product, run once per Playwright project (iPhone 14 viewport and desktop Chrome), each with its own freshly signed-up account. It runs against a **real API and a real Postgres**, not mocks (`playwright.journey.config.ts`, API base from `JOURNEY_API_BASE`).

| Step | What is asserted |
|---|---|
| Sign up | account and workspace are created, lands on the dashboard, `<html lang>` is `nl-BE` |
| Settings | rates and waste are saved and come back after a reload |
| Add printer | A1 defaults are prefilled, printer appears in the list |
| Add material and spool | spool detail shows full stock |
| Adjust stock | 800 g removed with a reason, 200 g remain, movement visible |
| Low-stock alert | thresholds saved, alert with the exact grams appears on the dashboard |
| Cost calculator | breakdown includes the material line with waste (1,05), snapshot saved and listed |
| Create model | tiny STL fixture (`journey/fixtures/tetrahedron.stl`) is uploaded and the model opens |
| License and rights | a personal-use licence is created and linked to the model |
| Product, variant, BOM, cost | variant with price, filament line (cost 2,10), print line with the model, commercial-rights warning and "mark anyway" action shown, cost snapshot saved |
| Manual print job | a Done job consumes 20 g, the spool shows 180 g and the ledger has a "used in print" movement with the new balance |
| Dashboard | finished-print figures and the recent print appear |
| Bambu Lab screen | connect flow works through the fake client and shows the fake printer |
| Export | the ZIP download starts with the expected file name |
| Delete account | confirmation with password and workspace name, redirect to sign-in with the notice, signing in again fails |

`security.journey.spec.ts` adds anonymous 401s on the data endpoints, cross-workspace 404s (read and update) and field-level 400 validation, using two accounts.

The existing mocked suite (`apps/web/e2e`, `npm run test:e2e`) is unchanged and still runs in the `e2e` job; it covers error states, validation details and layouts that are impractical to provoke against a real API.

### How the journey runs

CI job `journey` in `.github/workflows/ci.yml`:

1. a `postgres:18-alpine` service container,
2. `dotnet build` then `dotnet run --no-build` of `apps/api/src/PrintOs.Api` in the background, configured only through environment variables (see below), health-checked on `/health/ready`,
3. `npm run build` of the web app, then the journey with the built `.output` server pointing at the API (`NUXT_PUBLIC_API_BASE`).

Test-only API configuration (never set in production):

| Setting | Value | Why |
|---|---|---|
| `Storage__Provider` | `Memory` | no S3 or Azure needed for model files, licence proofs and the export |
| `Bambu__UseJourneyDoubles` | `true` | replaces the Bambu Lab cloud client with a fixed fake (one A1, no tasks) and the MQTT source with an idle one. The fake throws when the environment is `Production`. No real credentials, no network call to Bambu Lab |
| `Bambu__WorkerEnabled` | `false` | no background telemetry worker |
| `Database__MigrateOnStartup` | `true` | the real migrations create the schema |

To run it locally (PowerShell shown, any shell works):

```
docker run -d --name printos-journey-pg -e POSTGRES_DB=printos -e POSTGRES_USER=printos -e POSTGRES_PASSWORD=printos -p 55432:5432 postgres:18-alpine
# API, from apps/api
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5100'
$env:ConnectionStrings__Default='Host=localhost;Port=55432;Database=printos;Username=printos;Password=printos'
$env:Database__MigrateOnStartup='true'; $env:Cors__AllowedOrigins__0='http://localhost:3100'
$env:Storage__Provider='Memory'; $env:Bambu__UseJourneyDoubles='true'; $env:Bambu__WorkerEnabled='false'
dotnet run --project src/PrintOs.Api
# web, from apps/web
npm run build
$env:E2E_PORT='3100'; $env:JOURNEY_API_BASE='http://localhost:5100'; npm run test:journey
```

The CORS origin must match the web port, because the browser calls the API directly.

### Restore from backup

`scripts/backup/roundtrip-test.sh` starts a throwaway Postgres, applies the real EF schema, seeds data, makes an encrypted backup, checks tamper and wrong-passphrase rejection, drops the source database and restores into a scratch database, comparing every table's row count. It runs in `.github/workflows/backup.yml` (weekly, on demand and on pull requests touching the backup scripts) and was run again for this gate: 29 tables, 14 migrations, "row counts match the source exactly". The production backup job and the Azure point-in-time restore are not exercised by automation; see the checklist.

## Manual QA checklist (what automation cannot cover)

Walk through this on a real device before calling the release done. Tick each item in the release PR.

**iPhone Safari and PWA**
- [ ] Open the production URL in Safari on an iPhone, sign up or sign in, and confirm the layout has no horizontal scroll and the bottom navigation clears the home indicator.
- [ ] Share, Add to Home Screen: the icon is the PrintOS icon, the app opens full screen without Safari chrome, the status bar is readable on the dark theme.
- [ ] Close and reopen the installed app: the session is still active; after a deploy the app updates itself (service worker `autoUpdate`) without a stale shell.
- [ ] Switch the system between light and dark and confirm the theme switch follows or overrides it.
- [ ] Upload a real STL, 3MF and OBJ from the Files app and confirm the 3D preview renders and can be rotated by touch.
- [ ] Switch language in Settings and confirm the whole app, including dates and currency, changes and stays after reopening.

**Real Bambu Lab account and printer** (nothing has ever run against the real service)
- [ ] Paste a real access token on the Bambu Lab screen; confirm connection, device list and region handling (global; china only if relevant).
- [ ] Link the A1, start a print and confirm live status, progress, layer and temperatures on Home and the printers list.
- [ ] Finish a print and confirm the history sync books filament on the matching spool, and that an unmatched or insufficient spool is flagged instead of silently ignored.
- [ ] Let the token expire or revoke it and confirm the "token expired" state and reconnect flow.
- [ ] Disconnect and confirm the token is gone and printers plus imported prints remain.

**Azure Blob storage and infrastructure**
- [ ] On the deployed environment, upload a model and a licence proof and download them again (needs `Storage__AzureAccountUrl` and the managed identity role; see `file-storage.md`).
- [ ] Run the account data export against the deployed environment and open the ZIP: files from Blob storage are included, no secrets are present.
- [ ] Delete a throwaway account and confirm its blobs are gone from the container.
- [ ] Confirm `/health/ready` is healthy after a deploy and that the migrations ran.
- [ ] Azure backups: check the retention (14 days) and, once, do a point-in-time restore into a new server following `backups.md`; run the production encrypted backup job once and restore its output with `restore-test.sh`.

**Content and copy**
- [ ] Read the Dutch screens once end to end as a native speaker (the i18n pass was a consistency review, not a native review).

## Known gaps collected from merged pull requests

Summarised from the "Not verified" and "Known gaps" notes of the merged PRs. None blocks the release gate by itself; each needs a conscious yes.

**Not verified against real services**
- Bambu Lab (#88): endpoints, hosts, MQTT behaviour, cookie-based token retrieval and the China region come from community documentation only. Spool matching is by material type plus exact colour code; task weights are slicer estimates.
- Azure Blob storage (#81, #93): never run against a real account; the S3 path is tested against SeaweedFS. The export and delete flows were not checked against real Azure or S3 storage.
- Backups (#86): the production backup job and the `az` commands in `backups.md` were never run; `openssl enc` is not authenticated (the SHA-256 file and `pg_restore` compensate); firewall setup for the runner is left to the owner.
- Deploy (#67): the deploy workflow cannot run end to end until the Azure identity and repository variables exist.
- 3MF and OBJ previews (#85) were not verified against real files in a browser.

**Functional gaps**
- No revenue, sales or profit on the dashboard (#91): there is no orders module. Periods use UTC days; "online printers" only counts Bambu-linked devices.
- Calculator and products (#79, #87, #89): fees are a percent of the selling price only; a variant has at most one print line, several filament lines are costed with one weighted cost per gram; removing a BOM line has no confirmation; the model page has no "used in products" link.
- Print jobs (#80, #83): no photos; finished units do not feed product stock; spools have no concurrency token (last write wins); filters apply to the latest 200 jobs on the client; `ModelId` has no foreign key (deleted models count as unlinked, #92).
- Licences (#90, #92): units used only counts finished units of Done jobs with a model; the licence chip is not shown on the model list; the finish form cannot change the model.
- Audit log (#84): append-only is enforced in the context only (no database trigger); any workspace member can read the log (there is no owner role yet). Entity and field labels are now localised (#30); unknown future fields fall back to a readable name.
- Account (#93): the export rate limit is per API instance; blobs not referenced by a row cannot be found for deletion (the storage interface has no listing); a mid-stream failure yields a truncated ZIP; the multi-user rule is documented but only one owner exists today.
- Settings (#72): numbers such as `0,3` are shown without fixed decimals.
- Printers (#73): the A1 price, power and lifetime are editable placeholders.
- Model library (#85): no upload progress bar (spinner and hint only).
- Dark theme was not screenshotted for some screens (#82, #89).

**Localisation (#30)**
- No native-speaker review of the Dutch copy; the dev-only `/design` showcase stays English; server sentences without a mapping fall back to a generic localised message.

**Release gate (#32)**
- The journey uses the in-memory storage and a fake Bambu Lab client by design; it proves the UI-to-API-to-database path, not the cloud integrations.
- The journey is a single long scenario: a failure in an early step hides the later ones. The trace and screenshot in the CI artifact show where it stopped.
