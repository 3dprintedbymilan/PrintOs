# Print jobs

A print job records one print on one printer: status, times, notes and the filament used per spool. Module: `Production` (`PrintJobEndpoints.cs`). All endpoints require a signed-in workspace and only see that workspace's data (other workspaces get 404 / validation errors).

## Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/print-jobs` | List, newest first. Filters: `status` (comma list), `printerId`, `spoolId`, `from`, `to` (on start time, else creation time), `q` (name), `externalSource`, `externalId`, `limit` (default 200, max 500). |
| GET | `/api/print-jobs/{id}` | One job with filament lines. |
| POST | `/api/print-jobs` | Create. 201 for a new job, 200 with the existing job when the external pair already exists. |
| PUT | `/api/print-jobs/{id}` | Replace the job (full body, same shape as create). |
| POST | `/api/print-jobs/{id}/start` | Planned -> Printing, sets `startedAt` when empty. |
| POST | `/api/print-jobs/{id}/finish` | Planned/Printing -> Done, Failed or Cancelled. Body: `status`, `finishedAt?`, `durationMinutes?`, `notes?`, `finishedUnits?`, `filaments?` (defaults to the lines already on the job). |
| DELETE | `/api/print-jobs/{id}` | Delete and give the filament back. |

Request body: `printerId`, `name`, `status` (`Planned|Printing|Done|Failed|Cancelled`, case-insensitive), `startedAt`, `finishedAt`, `durationMinutes`, `notes`, `finishedUnits`, `externalSource`, `externalId`, `filaments: [{ spoolId, grams, wasteGrams }]`.

- `grams` is everything taken from the spool for the job; `wasteGrams` is the part of it that was waste (0 to `grams`).
- `durationMinutes` is computed from the two times when omitted.
- `finishedAt` is only allowed on Done/Failed/Cancelled and defaults to now there; Printing defaults `startedAt` to now.
- `finishedUnits` is only allowed on Done jobs.
- `productVariantId` (optional, also on finish) links the job to a product variant: a Done job with `finishedUnits` books those units into finished stock, see [finished-stock.md](finished-stock.md).
- `ModelId` exists on the entity as a nullable column for the later model/product link; it is not exposed for writing yet.
- Validation errors are 400 ValidationProblem with camel-case keys, for example `filaments[0].grams`.

## Stock ledger rule

Stock is booked only while the job is Done, Failed or Cancelled (failed prints still use filament). Planned and Printing jobs store their lines but touch no stock.

On every save the server compares the grams that should be booked per spool with what the job's `MaterialMovement` rows (`PrintJobId`) already add up to, and writes only the difference as a new `Consumption` movement (negative to consume, positive to give back) while reducing `FilamentSpool.RemainingWeightGrams` in the same `SaveChanges`. Old movements are never edited or deleted. If a spool would go below zero (or a newly used spool is archived) the request fails with 400 and nothing is written. Deleting a job reverses its net consumption the same way. The shared logic lives in `Inventory/StockLedger.cs`, which the manual stock adjustment endpoint also uses.

## Idempotent import (`externalSource` + `externalId`)

For syncing from outside systems such as the Bambu Lab connection (#78):

- Give both fields or neither (for example `bambu` + the printer's task id).
- The pair is unique per workspace (partial unique index).
- `POST /api/print-jobs` with a pair that already exists returns 200 with the stored job and changes nothing, so re-running an import never duplicates a job or double-books filament.
- To update an imported job, find it with `GET /api/print-jobs?externalSource=...&externalId=...` and `PUT` it; the ledger adjusts by the difference.
- Changing a job to a pair that another job holds is a 400 on `externalId`.
