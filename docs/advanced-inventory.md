# Advanced inventory

Module: `Inventory` (`Modules/Inventory/AdvancedInventory.cs`). All stock changes still go through the existing ledgers (`StockLedger` for spools, `FinishedStockLedger` for finished goods), so the movement history stays the audit trail.

| Method | Path | Purpose |
| --- | --- | --- |
| POST | `/api/spools/{id}/weigh` | `{ grossWeightGrams, emptySpoolGrams?, reason? }`. Remaining = gross - empty spool; the difference to the current balance is booked as an `Adjustment`. The empty-spool weight (`FilamentSpool.EmptySpoolGrams`) is remembered for next time. No difference, no movement. |
| POST | `/api/spools/bulk-adjustments` | `{ reason, lines: [{ spoolId, remainingGrams | deltaGrams }] }`, max 200 lines, all-or-nothing, errors keyed `lines[i].field`. Unchanged lines write nothing. |
| POST | `/api/finished-stock/bulk-adjustments` | `{ reason, allowBelowReserved?, lines: [{ variantId, counted | delta }] }`, same rules, uses `FinishedStockLedger.TryApply` (reservation rules and concurrency retry apply). |
| GET | `/api/finished-stock/reservations?q=&shortOnly=` | Reservation overview: every active reservation with order, customer, due date, variant on hand/available and a shortage flag, plus a summary. |

Web: `/inventory/count` (stock take, filament and finished goods), `/inventory/reservations`, and a weigh card on the spool page.
