# Bambu Lab connection

PrintOS can show what your Bambu Lab printer is doing and import finished prints from the Bambu cloud. Module: `Bambu` (`apps/api/src/PrintOs.Api/Modules/Bambu`). Screen: Business, Printers, Bambu Lab (`/printers/bambu`).

The API runs on Azure and cannot reach a printer on your home network, so everything goes through the Bambu cloud. There is no LAN mode.

## Setup

1. Sign in at bambulab.com in your browser.
2. Open the developer tools (F12), go to Application (or Storage), Cookies, `https://bambulab.com`.
3. Copy the value of the cookie named `token`. This is the access token.
4. In PrintOS open Bambu Lab, paste the token (the eye button shows or hides it), pick your account region and press Connect.
5. Link each printer found on your account to a PrintOS printer, or let PrintOS create one with the A1 defaults.

Linked printers get a live status chip in the printers list, and the Home screen shows a "What am I printing" card. The web app polls `GET /api/bambu/status` every 10 seconds, only while the page is visible.

## What is stored

| Data | Where | Notes |
| --- | --- | --- |
| Access token | `BambuConnections.EncryptedAccessToken` | Encrypted with ASP.NET Data Protection (keys live in the database). Never returned by any endpoint, never logged, never written to the audit log. Your Bambu password is never asked for or stored. |
| Account id and name, region | `BambuConnections` | The id is needed for the MQTT user name. |
| Devices (serial, name, model) and latest status | `BambuDevices` | One row per printer: state, job name, progress, remaining minutes, layer, nozzle and bed temperature, current filament type and colour, last seen. Only the latest state is kept, not a history. |
| Imported tasks | `BambuTaskImports` | Which cloud task became which print job, and whether it needs attention. |

Imported prints are normal print jobs with `externalSource = "bambu-cloud"` and `externalId = <task id>` (see `print-jobs.md`), so syncing twice never creates duplicates.

Connection, devices and imports are marked `[NotAudited]`: they hold secrets and high-frequency telemetry.

## How it works

- `IBambuCloudClient` (REST, `api.bambulab.com`) validates the token, lists bound devices and lists tasks.
- `IBambuTelemetrySource` (MQTT over TLS, port 8883) subscribes to `device/<serial>/report`. Broker user name is `u_<account id>`, password is the access token. The only message ever published is the read-only `pushall` request on `device/<serial>/request`.
- `BambuTelemetryWorker` (a `BackgroundService`) keeps one subscription per linked printer, reconnects with exponential backoff (5 s up to 5 min) and marks the printer offline while disconnected. It also refreshes devices and syncs history every few minutes (`Bambu:SyncMinutes`).
- History sync turns finished cloud tasks (status 2 done, 3 aborted or failed) into print jobs for linked printers. Filament is consumed through the normal print job logic only when the task lists the grams per filament and exactly one spool of the same material type and colour has enough stock (the spool with the least filament left that still suffices is used). Otherwise the job is imported without filament consumption and flagged (the connection screen shows how many). Aborted tasks are never consumed because the cloud weight is the slicer estimate for the whole job.
- All Bambu specifics sit behind the two interfaces, so they can be swapped or faked in tests.

## Limits

- Unofficial API. Endpoints, hosts and payloads come from community documentation (OpenBambuAPI) and can change or be blocked by Bambu Lab at any time.
- The access token expires. When Bambu rejects it, the connection shows "token expired", printers show offline, and you paste a new token (Reconnect). Nothing else is lost.
- Cookie-based token retrieval is a manual, unofficial step.
- Read only: PrintOS never sends print, pause, stop or any other control command.
- No LAN mode, no camera, no AMS or filament control.
- Task weights are slicer estimates, not measured usage. Check flagged imports in the print log.
- "Offline" comes from the broker connection and the cloud's online flag, refreshed every few minutes. A printer that is switched off can briefly still look idle.
- Regions: `global` (api.bambulab.com, us.mqtt.bambulab.com) and `china` (api.bambulab.cn, cn.mqtt.bambulab.com). The China hosts are unverified. Hosts can be overridden in the `Bambu` configuration section.
- Not verified against a real account or printer: all behaviour was tested with fakes and sample payloads.

## API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/bambu/connection` | Connection state, devices and links (never the token). |
| PUT | `/api/bambu/connection` | Connect or reconnect: `{ accessToken, region }`. 400 `accessToken` when Bambu rejects it, 502 when the cloud is unreachable. |
| DELETE | `/api/bambu/connection` | Disconnect: deletes the token and devices, stops subscriptions. Printers and imported jobs stay. |
| POST | `/api/bambu/connection/refresh` | Refresh devices and sync history now. |
| POST | `/api/bambu/devices/{serial}/link` | `{ printerId }` or `{ createPrinter: true }`. |
| DELETE | `/api/bambu/devices/{serial}/link` | Unlink. |
| GET | `/api/bambu/status` | Live status of linked printers. |

Configuration (`Bambu` section): `WorkerEnabled`, `ReconcileSeconds`, `SyncMinutes`, `FlushSeconds`, `InitialBackoffSeconds`, `MaximumBackoffSeconds`, hosts and `MqttPort`.

## Printer monitor (#49)

Screen: Printers, Printer monitor (`/printers/monitor`, also linked from the Prints tab). `GET /api/bambu/monitor` returns, per linked printer: the last known status (kept while offline, with `online`, `stale` and `lastKnownState`), health (`printErrorCode`, `hmsCodes`, nozzle and bed temperature, filament trays, last seen), utilization for 24 hours, 7 days and 30 days, and the last 12 state changes. The page polls every 10 seconds while visible and keeps showing the last data when a refresh fails.

- **State history**: `BambuStateChanges` (`[NotAudited]`) gets one row whenever a device's state changes (printing, paused, finished, failed, idle, offline). The telemetry worker, device refresh and unlinking write it. Rows older than `Bambu:StateHistoryRetentionDays` (45, minimum 31) are pruned hourly, keeping the newest older row per device as the baseline for the 30-day window.
- **Utilization** = time in `printing` divided by observed time in the window (offline counts as observed, not printing; time before the first record is not counted and `coveragePercent` says how much of the window was recorded). An open printing or paused interval is only trusted for 10 minutes after the last report, so a stopped worker cannot inflate the figure.
- **Stale**: no report for 5 minutes, or offline.
- **Error codes**: `print_error` is shown as `AAAA-BBBB`, each HMS entry as `AAAA-BBBB-CCCC-DDDD` (attr and code as hexadecimal 16-bit halves). The grouping follows the community convention and is not verified against real A1 messages; PrintOS shows the raw code and does not translate it.
- **Camera**: always the state "Camera not available via cloud" with the reason and a link to [bambu-spike.md](bambu-spike.md). A future bridge can replace that section.

## Print completion events and automation (#50)

The telemetry worker watches the printer state of each linked printer and turns a finished or failed print into a normal print job, exactly once.

### How a print is recognised

- When a report shows `printing` or `paused` and no print is being tracked, the worker stores a small context on the device row (start time, the cloud `task_id` when known, job name, a snapshot of the AMS trays with `remain` and `tray_weight`, and whether the start was observed). The context is persisted, so it survives reconnects, restarts and offline periods.
- A transition to `finished` or `failed` while a context exists completes it. A report that shows `idle` drops the context without a job (the history sync still catches such prints). A `finished`/`failed` report without a context is ignored, so linking a printer that still shows an old FINISH never creates a job.
- `LastCompletedTaskId` stops a stale `RUNNING` replay of an already completed task from starting a new context.
- Offline then finish: the context is kept while offline, and the first report after reconnecting completes it. The finish time is the time the worker observed it.

### Idempotency

The print job uses `ExternalSource = "bambu-cloud"` and `ExternalId = <task_id>`; without a usable `task_id` (missing or `0`) it uses `live-<serial>-<start unix seconds>`. A `BambuTaskImport` row (unique per task) is written with `Source = "event"`. A duplicate or replayed completion finds that row or job and does nothing, and publishes no second event. The later history sync skips known task ids, and when it meets a cloud task whose finish time is within 20 minutes of an event-created job on the same printer it adopts that job (sets its external id to the cloud task id, marks the import `event-matched`) instead of creating a second job. Stock is therefore booked once. Several API replicas completing the same print at the same moment are not coordinated beyond the unique indexes.

### Filament

Stock is adjusted only through the normal print job logic (consumption movements, finished-stock hook). The amount comes from the AMS: for each tray, `(remain at start - remain at end) / 100 * tray_weight`. It is used only when the start was observed (progress at most 3 percent when tracking began), at least one tray used measurable filament, and the tray in use at the end reports a known `remain`. The spool is matched by material type and colour exactly as in the history sync (the matching spool with the least filament left that still suffices). Otherwise the job is recorded without consumption and flagged in `BambuTaskImports.Flag`: `noUsageData` (below the 1 percent AMS resolution, external spool, or no RFID data), `partialObservation` (connected mid-print), `noMatchingSpool`, `insufficientStock`. A failed print books the measured usage, not the slicer estimate. The 1 percent resolution means about 10 g steps on a 1 kg spool, so small prints are usually flagged rather than guessed.

### Queue link

If exactly one job is `Printing` on that printer (started from the production queue or by hand), that job is finished instead of creating a new one: it gets the external id, finish time and, when it has no filament lines yet, the measured lines; a Done job takes `finishedUnits = plannedUnits`, which books finished stock and moves the order forward (see [production-queue.md](production-queue.md)). With zero or several `Printing` jobs a new job is created.

### Domain events

`IPrintEventPublisher` (scoped, in-process) publishes `PrintFinishedEvent` and `PrintFailedEvent` (both derive from `PrintEvent`: workspace, printer, print job, job name, start and finish, `ConsumedStock`, `Flag`, `OrderLineId`, `Source`, `IsCatchUp`; the failed event adds `ErrorCode`). They are published once, after the job is saved, by the telemetry worker (`Source = "telemetry"`) and by the history sync for tasks it imports itself (`Source = "history-sync"`, `IsCatchUp` true when the print ended more than 30 minutes earlier, so a consumer can skip old prints).

Subscribe by registering a handler:

```csharp
services.AddScoped<IPrintEventHandler<PrintFinishedEvent>, MyHandler>();
```

Handlers run inline and in registration order. A handler exception is logged and swallowed, so a failing subscriber never blocks recording a print. Handlers must be quick; do slow work through their own queue. Events are not persisted: if the process stops between saving the job and publishing, the event is lost (the job is not).

### Not verified against hardware

Whether the A1's MQTT `task_id` equals the cloud task id (if not, the history sync adopts the job by time match), the exact `gcode_state` values and ordering around a cancel or power loss, and how reliably an A1 with AMS lite reports `remain` and `tray_weight`.
