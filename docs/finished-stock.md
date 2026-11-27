# Finished stock

Module: `FinishedStock` (`apps/api/src/PrintOs.Api/Modules/FinishedStock`). Stock of sellable goods per **product variant**, an append-only movement ledger, and reservations against order lines. This is the contract for market/POS checkout (#39-#41), reporting and advanced inventory (#47): book through `FinishedStockLedger` (or the endpoints below) and never write the tables directly.

It mirrors the filament ledger (`Inventory/StockLedger.cs`, `MaterialMovement`) but in whole units.

## Tables

| Table | Meaning |
| --- | --- |
| `FinishedStockLevels` | One row per workspace + variant (created on the first movement). `OnHand`, `Reserved`, `AverageUnitCost` (moving average, 4 decimals), `LowStockThreshold`, `Version` (optimistic concurrency token). `Available = OnHand - Reserved`. |
| `FinishedStockMovements` | Append-only ledger, never edited or deleted (except with the workspace). |
| `StockReservations` | One row per order line (unique on `OrderLineId`): `Quantity` currently reserved, `FulfilledQuantity`, `Status` Active / Released / Fulfilled. |

Variants with stock history cannot be deleted (409 from the product API); archive the product instead. Products that are archived but still hold stock stay in the stock list.

### Movement

`Kind`, `Delta` (change of on hand), `ReservedDelta` (change of reserved), `BalanceAfter`, `ReservedAfter`, `Reason` (required, max 300), `UnitCost`, and optional links `PrintJobId`, `OrderId`, `OrderLineId`, `MarketSaleId` (plain ids, no foreign keys, so history survives deletes; `MarketSaleId` is reserved for POS).

| Kind | Delta | ReservedDelta | When |
| --- | --- | --- | --- |
| `Produced` | + (or - when a print job is corrected) | 0 | print job Done with finished units and variant |
| `Sold` | - | 0 (direct sale) or - (delivery of a reservation) | order delivered, POS/market sale |
| `Reserved` | 0 | + | reserved for an order line |
| `ReservationReleased` | 0 | - | order cancelled/edited, reservation lowered or released, shortage at delivery |
| `Adjustment` | +/- | 0 | manual count correction (reason required) |
| `Returned` | + | 0 | customer return |

### Unit cost (profit reports)

Every movement stores `UnitCost`. For stock-increasing movements it is the cost of the receipt: for `Produced` and positive `Adjustment` the variant BOM cost at that moment (`ProductCostService`, same figure as the order line `UnitCost` snapshot), for `Returned` the supplied `unitCost` or else the BOM cost. For outflows (`Sold`, reservations) it is the level's moving average cost at that moment. `FinishedStockLevels.AverageUnitCost` is blended on every receipt (`FinishedStockMath.NextAverageCost`); outflows keep it. Stock value = `OnHand * AverageUnitCost`. Orders still carry their own price/cost snapshot per line; use the ledger for stock valuation and cost of goods actually taken from stock.

## Rules (`FinishedStockMath.Apply`)

- On hand never goes below zero (`InsufficientOnHand`); reserved never below zero.
- Reserving and direct sales need available stock (`InsufficientAvailable`) unless over-reserving is explicitly allowed (`allowOverReserve`). Fulfilling a reservation and adjustments are checked against on hand only; an adjustment that would drop on hand below the reserved quantity needs `allowBelowReserved` (available then goes negative and the UI shows the shortage).
- Concurrency: every change loads the level, bumps `Version` and saves; a concurrent writer causes `DbUpdateConcurrencyException` (or a unique violation when two requests create the level) and `FinishedStockRetry.RunAsync` re-runs the whole request on fresh data (up to 25 times, then 409). Result: concurrent reservations/sales can never exceed available stock. Wrap any new endpoint that books stock in `FinishedStockRetry.RunAsync`.
- Everything in one request is saved in one `SaveChanges`: either the order change and its stock movements are written, or nothing.

## Production (print jobs)

`PrintJob.ProductVariantId` (nullable, set via the print-job API `productVariantId`, also accepted on finish). When a job is `Done`, has `finishedUnits > 0` and a variant, `ReconcileProducedAsync` compares the units that should be booked with the sum of the job's `Produced` movements and writes only the difference (`Produced`, positive or negative). Saving or finishing again with the same values writes nothing; editing the units writes the difference; changing status away from Done, changing the variant or deleting the job takes the units back out. If the units were already sold or reserved away (on hand too low) the request fails with 400 on `finishedUnits`. Jobs without variant or units never touch finished stock.

## Reservations and the order flow

Reservations exist for product lines (`OrderLine.VariantId` set) of orders that are Confirmed, InProduction or Ready. Reserving sets the reserved quantity of a line to an exact number (so repeating a request never double-reserves) and is limited by the line quantity.

Hooks in the order module (the transition table in `OrderRules` is unchanged):

- `Delivered`: `FulfilOrderAsync` turns every active reservation into a `Sold` movement (`Delta` and `ReservedDelta` both negative). If on hand is lower than the reservation (stock lost after reserving) it sells what is there and releases the rest with a reason; delivery is never blocked. Lines without a reservation do not touch stock.
- `Cancelled`: `ReleaseOrderAsync` releases all active reservations.
- Order edit (`PUT /api/orders/{id}`): `ReconcileOrderAsync` lowers reservations to the new line quantity and releases those of removed lines or lines whose variant changed.

## API (workspace scoped, 404 for foreign data)

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/finished-stock?q=&productId=&low=&inStock=` | `{ summary, items }` for every variant (zero stock included). Summary: `skuCount`, `skusInStock`, `totalUnits`, `totalReserved`, `totalAvailable`, `lowStockCount`, `stockValue`. Item: `onHand`, `reserved`, `available`, `lowStockThreshold`, `isLow` (available <= threshold), `averageUnitCost`, `stockValue`. |
| GET | `/api/finished-stock/{variantId}` | Item plus active reservations (order number, line, quantity). |
| GET | `/api/finished-stock/{variantId}/movements?kind=&limit=` | Newest first (default 100, max 500) with print job name and order number resolved. |
| POST | `/api/finished-stock/{variantId}/adjustments` | `{ delta, reason, allowBelowReserved? }`. |
| POST | `/api/finished-stock/{variantId}/returns` | `{ quantity, reason, orderId?, unitCost? }`. |
| POST | `/api/finished-stock/{variantId}/sales` | `{ quantity, reason, marketSaleId?, orderId? }` direct sale (market), needs available stock. |
| PUT | `/api/finished-stock/{variantId}/low-stock` | `{ threshold }` (null clears). |
| GET / POST / DELETE | `/api/orders/{orderId}/reservations` | List per product line (`reserved`, `fulfilledQuantity`, `status`, `onHand`, `available`); POST reserves all lines in full (`{ allowOverReserve? }`, all-or-nothing, 400 with `lines[position]` errors); DELETE releases all. |
| POST | `/api/finished-stock/reservations` | `{ orderLineId, quantity, allowOverReserve? }` sets one line (0 releases). |
| POST | `/api/finished-stock/reservations/{orderLineId}/release` | Release one line. |

## Privacy and audit

All three tables are workspace scoped, covered by the account export (`ExportCatalog`) and removed with the workspace. Level and movement changes appear in the audit log (`Version` is excluded).
