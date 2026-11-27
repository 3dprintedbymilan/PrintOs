# Production queue

Module: `Production` (`ProductionQueueEndpoints.cs`, `ProductionQueueRules.cs`). Screen: Prints, Queue segment (`/prints?section=queue`, also in the sidebar). Order detail has a Production card. Builds on `orders.md` and `print-jobs.md`; finished-goods stock is not part of this (see the stock ledger work, #37/#38, which consumes `PrintJob.FinishedUnits`).

## Data

`PrintJob` got four nullable columns (migration `ProductionQueue`):

| Column | Meaning |
| --- | --- |
| `OrderLineId` | FK to `OrderLines` (set null when the line is deleted). A job belongs to at most one order line. |
| `PlannedUnits` | Pieces the job should yield. Counts as "in the queue" while the job is Planned or Printing. |
| `QueuePosition` | Manual order, null = automatic. |
| `RequeuedFromId` | The failed or cancelled job this job reprints (plain id, no FK). |

`PrintJob.FinishedUnits` (Done jobs only) stays the number of pieces produced. `PUT /api/print-jobs/{id}` only changes `orderLineId` or `plannedUnits` when the body contains them; omitting them keeps the link. Finishing a job as Done without `finishedUnits` defaults it to `plannedUnits`.

## Progress rules

Per order line: `produced` = sum of `FinishedUnits` of Done jobs, `queued` = sum of `PlannedUnits` of Planned/Printing jobs, `remaining` = `max(0, quantity - produced - queued)`. Failed and Cancelled jobs count for nothing and offer a requeue. Order progress sums the lines (produced capped per line at the ordered quantity).

## Order status

After every job create/update/start/finish/delete/link/unlink the server calls `ProductionQueueRules.SyncOrderStatusAsync`, which moves the linked order through `OrderRules.CanTransition`:

- Confirmed -> InProduction when a linked job is Printing or Done.
- Confirmed/InProduction -> Ready when every line has `produced >= quantity` (and the order has lines).

Progression is forward only: it never reverts Ready, and draft, delivered and cancelled orders are untouched.

## Queue order

Printing jobs come first. Planned jobs are sorted by manual position (if any), then due date (no date last), then order number, then creation time. Moving a job (`up`, `down`, `top`) numbers all planned jobs 1..n, so the order becomes manual; jobs added later go after the manual ones. `reset` clears the manual order. Planned jobs of cancelled orders are hidden. There is no order priority field; the due date and order number are the priority. Reordering uses buttons (single pointer), not drag and drop.

## Filament check

For each queued job the queue adds up the grams per spool in queue order (Printing jobs included, since stock is booked when a job finishes) and flags a line as `enough = false` when the spool is archived or the running total exceeds `RemainingWeightGrams`. Jobs without filament lines are flagged as "no filament chosen". It is a warning only.

## API

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/production/queue` | Queue items with order info, filament checks, `estimatedMinutes`, `withoutEstimate`, `manualOrder`. |
| POST | `/api/production/queue/{id}/move` | `{ direction: up\|down\|top }` on a planned job. Returns the queue. |
| POST | `/api/production/queue/reset` | Back to automatic order. |
| PATCH | `/api/production/queue/{id}` | Planned job: `printerId`, `durationMinutes` (estimate), `plannedUnits`. |
| GET | `/api/production/suggestions` | Confirmed/in-production orders with lines that still have pieces to plan. |
| POST | `/api/production/orders/{orderId}/jobs` | `{ lineId?, printerId?, unitsPerJob? }` creates Planned jobs for what is left (one per line, or several when `unitsPerJob` splits the quantity). Default printer: the oldest active one; 400 `printerId` without printers. |
| GET | `/api/production/orders/{orderId}` | Progress per order and line with its jobs. |
| POST | `/api/print-jobs/{id}/order-line` | `{ orderLineId, units }` links any job (for example a Bambu import) to a line of a confirmed order; `units` sets `finishedUnits` on a Done job, else `plannedUnits`. `orderLineId: null` unlinks. |
| POST | `/api/print-jobs/{id}/requeue` | Failed/Cancelled job -> new Planned copy (same printer, order line, units, filament grams), once per job. |

Job responses now include `orderLineId`, `orderId`, `orderNumber`, `orderLineDescription`, `plannedUnits`, `queuePosition`, `requeuedFromId`, `requeued`.

## Limits

- Created jobs have no filament; add spools on the print before starting. There is no automatic link from a product's bill of materials to grams.
- Time estimates are entered by hand (no slicer data).
- Reopening a Ready order does not reset anything; new jobs can still be planned while it is InProduction.
