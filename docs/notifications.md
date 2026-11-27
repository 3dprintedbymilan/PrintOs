# Notifications

Module: `Notifications` (`apps/api/src/PrintOs.Api/Modules/Notifications`). Screens: bell with unread badge in the shell (header on mobile, sidebar on desktop), `/notifications` (centre), `/notification-settings` (Settings, Notifications). Printer maintenance lives on the printer page.

## Data

| Entity | Purpose |
| --- | --- |
| `Notification` | One row per workspace user: type, severity, parameters (JSON, rendered by the client per language), link, `ReadAt`, email status. `[NotAudited]` (high volume). |
| `NotificationPreference` | Per user and type: in-app and email. Default: in-app on, email off. |
| `NotificationSettings` | Per user quiet hours (start, end, UTC offset sent by the browser). |
| `NotificationState` | Internal "what did we last notify about" memory per workspace and key, so only transitions notify. `[NotAudited]`. |

All four are `IWorkspaceScoped`, listed in `ExportCatalog` (exported with the account data) and have only a cascade FK to the workspace, so account deletion works. `Printer` got `MaintenanceEveryHours`, `MaintenanceEveryDays` and `LastMaintenanceAt` (migration `Notifications`).

## Triggers

A background worker (`NotificationWorker`, every `Notifications:SweepSeconds`, default 60) evaluates every workspace. Each trigger compares the current state with the stored state and notifies on a transition into an alert state, never on every evaluation.

| Type | Condition |
| --- | --- |
| `ReorderSoon`, `LowStock` | Existing alert logic (`InventoryEndpoints.EvaluateSeverity`). Notifies when a material enters reorder, or escalates from reorder to low. Leaving and re-entering within 6 hours does not notify again. |
| `PrintFailed`, `PrintFinished` | A Done/Failed print job finished within the last `RecentPrintHours` (6), once per job. Based on print job status, see below. |
| `PrinterOffline` | A linked Bambu printer has been offline for `PrinterOfflineMinutes` (10). Once per offline episode, and at most once per `OfflineCooldownMinutes` (60) so a flapping printer cannot spam. |
| `MaintenanceDue` | Print hours (Done and Failed jobs) since the last maintenance reach the printer's interval, or the days do. Once per maintenance cycle; "Maintenance done" on the printer page starts a new cycle. |
| `LicenseExpiring` | A license expires within `LicenseExpiryDays` (30), and again when it has expired. |
| `OrderDueSoon`, `OrderOverdue` | Confirmed, in production or ready orders due within `DueSoonDays` (2), and again once overdue. |

Not built: the optional daily-closing reminder.

Print finished/failed use the print job data (status and finish time), because the `IPrintEventPublisher` abstraction of the Bambu monitoring work had not merged when this was written. Once it exists, publishing the same notification from its events is a small follow-up; the per-job dedupe key (`job:<id>`) already makes a double trigger harmless.

## Deduplication and rate limiting

- Transition state (above) means a steady alert produces one notification.
- Every candidate has a dedupe key and an optional cooldown; an existing notification with the same type and key inside the cooldown suppresses a new one for that user.
- Per user at most `RateLimitPerHour` (30) notifications per hour. A rate-limited trigger does not record its state, so it is retried by the next sweep once there is room instead of being lost.

## Channels

- **In-app**: list, unread badge, mark read, mark all read, delete. Always on unless disabled per type.
- **Real time**: a SignalR hub at `/hubs/notifications` (JWT via `access_token` query for the socket, bearer scheme only). It only sends a "changed" signal to the owner's group; the client then refetches the count. Production may run 1-2 replicas on Azure Container Apps and there is no backplane (no Redis): a signal only reaches clients connected to the replica that created the notification. Correctness never depends on it: the client also polls the unread count every 30 seconds while the page is visible, and refreshes when the tab becomes visible again, so every replica's notifications show up within 30 seconds. The settings screen shows whether the live connection is up.
- **Email**: SMTP through environment variables, disabled by default. `Notifications__Smtp__Enabled=true`, `Notifications__Smtp__Host`, `__Port` (587), `__UseSsl` (true), `__UserName`, `__Password`, `__From`. Credentials are never logged (failures log only the exception type and attempt). A notification is mailed only when the server has SMTP enabled and the user opted in for that type. Mail goes out from the sweep, outside the user's quiet hours (in-app delivery is never delayed); a failed send is retried up to `MaximumEmailAttempts` (3). Email text is Dutch or English following the workspace language.
- **Web push (VAPID)**: not built. It needs a service-worker push handler and a subscription flow inside the `@vite-pwa` generated service worker plus a Web Push library on the API; this was left as a follow-up rather than risking the PWA. The settings screen says push is not available yet.

## Multi replica safety

Every sweep of a workspace runs in a transaction that takes `pg_try_advisory_xact_lock` on the workspace; a replica that does not get the lock skips that workspace this round. Together with the transition state this makes double execution harmless.

## API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/notifications?unreadOnly&limit&before` | Own notifications (newest first, cursor `before`) with `unreadCount`, `hasMore`. |
| GET | `/api/notifications/unread-count` | `{ count }`. |
| POST | `/api/notifications/{id}/read`, `/api/notifications/read-all` | Mark read. |
| DELETE | `/api/notifications/{id}` | Delete one of your own. |
| GET/PUT | `/api/notifications/preferences` | Per type in-app/email, quiet hours, and which channels the server offers. 400 with `types[i].type`, `quietHours.start` keys. |
| GET/PUT | `/api/printers/{id}/maintenance` | Interval (print hours, days), last maintenance, due flag. |
| POST | `/api/printers/{id}/maintenance/done` | Record maintenance now. |

Other workspaces' data returns 404; users only see their own notifications.

## Not verified

- Real SMTP delivery (tests use a fake sender), including TLS to a real provider.
- A real two-replica deployment, and SignalR through the Azure Container Apps ingress (tests use long polling against the in-process server; the websocket transport and the polling fallback were not tried against Azure).
- Web push does not exist.
- Quiet hours use the UTC offset of the browser at save time and do not follow daylight saving changes until saved again.
- A user who turns off in-app but keeps email gets a row that is stored as already read, so it can appear in the list as read.
