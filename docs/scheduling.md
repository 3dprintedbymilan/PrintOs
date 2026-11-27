# Job scheduling

Module: `Scheduling` (`apps/api/src/PrintOs.Api/Modules/Scheduling`). Screen: `/schedule` (link in the Prints queue segment and the sidebar). Builds on `production-queue.md`.

PrintOS only reads printers. The scheduler never starts, pauses or stops a print; it proposes which printer a Planned job should use and when it could run.

## What it does

- `SchedulePlanner.Plan` is a pure function (no database, no clock): input printers, running jobs, live printer busy state, planned jobs, spool stock and `now`; output a slot (printer, start, end) per job plus blocked jobs with a reason.
- Order of planned jobs: manual queue position (existing queue move buttons), then priority (low/normal/high/urgent), then order due date, order number, creation time. Manual position stays first because it is an explicit override.
- Each job goes to the printer where it finishes earliest (ties: current printer, then name). First-fit on a per-printer timeline, so short jobs fill gaps before a pinned start.
- Respects: archived printers are never used; printers that are printing are busy until the job's expected end (start plus estimate, minimum now); a linked Bambu printer that reports printing with remaining minutes is busy for that time; build volume versus an optional part size (rotation on the plate allowed; unknown size or unknown volume always fits); filament: grams are claimed per spool in plan order (printing jobs included) and a job whose spool is archived or too low is blocked, not scheduled.
- Jobs without an estimate use 60 minutes and are flagged "estimated". A job that ends after the end of its due day is flagged late. No working hours: printers are assumed to run 24/7.
- Blocked reasons: `NoPrinter`, `TooLarge`, `FilamentShort`, `PrinterUnavailable` (pinned printer archived).

## Pins and overrides

`PrintJob` got: `Priority` (0..3, default 1), `PrinterPinned`, `PinnedStartAt`, `PartX/Y/ZMillimetres` (migration `JobScheduling`).

- A job is pinned when a user picks its printer: creating or editing a planned print job with another printer, the queue edit dialog, `PUT /api/production/schedule/jobs/{id}` with `printerId`, or creating order jobs with an explicit printer. Existing planned jobs were pinned by the migration.
- Unpinned jobs (created with the default printer) can be reassigned: the replan writes the chosen printer into `PrintJob.PrinterId`. Pinned jobs are never moved. A pinned start time is a fixed slot that other jobs plan around; if it collides or lies in the past it shifts to the first free time.
- Times are computed on demand, not stored (they would change every minute and flood the audit log). Only reassignments are persisted.

## Quartz and double execution

Quartz.NET 3.15 with the in-memory store runs two jobs: a periodic sweep (`Scheduling:SweepSeconds`, default 120, first run after 30 s) over every workspace with planned jobs, and an event job fired when planned work changes. An EF `SaveChangesInterceptor` notes workspaces whose `PrintJob`, `PrintJobFilament` or `Printer` rows changed and triggers the event job right after saving, which also covers Bambu imports and every endpoint without touching them.

Production runs 1-2 replicas. Quartz clustering needs the AdoJobStore tables and a second migration surface, and would still not make the replan itself safe. Instead each replan runs in a transaction that first takes `pg_advisory_xact_lock` keyed on the workspace id: the background runner uses `pg_try_advisory_xact_lock` and skips the workspace if another replica (or an HTTP replan) holds it; the explicit "Re-plan now" endpoint waits for the lock. Replanning is idempotent, so a skipped or repeated run is harmless. Each replica runs its own in-memory Quartz scheduler; the event trigger is local, other replicas catch changes at the next sweep (at most `SweepSeconds` later).

`Scheduling:Enabled=false` turns the background work off (tests do this; the HTTP endpoints still work).

## API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/production/schedule?days=14` | Lanes per printer with timed items, blocked jobs, count beyond the horizon. Computed live, no writes. |
| POST | `/api/production/schedule/replan` | Replan now and persist printer reassignments; returns the schedule with `reassigned`. |
| PUT | `/api/production/schedule/jobs/{id}` | `priority`, `printerId` (pins), `startAt` (needs a pinned printer), `unpin`, part size (all three or `clearPartSize`). 404 for other workspaces. |

## Not verified / limits

- Never run against a real Azure Container Apps deployment with two replicas; the lock behaviour is tested against Postgres with a held lock.
- The part size is entered by hand (no model dimensions exist yet). Estimates are manual.
- The agenda is a list per day, not a drag-and-drop timeline.
