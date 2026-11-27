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
