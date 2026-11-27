# Markets and point of sale

Module: `Markets` (`apps/api/src/PrintOs.Api/Modules/Markets`). Backend for the market register (#39), the POS screens (#40) and the daily closing (#41). Reporting (#43-#45) should aggregate `MarketSales` / `MarketSaleLines` directly; sales are append-only facts (see Immutability).

## Tables

| Table | Meaning |
| --- | --- |
| `Markets` | An event or stall: `Name`, `Location`, `StartDate`, optional `EndDate`, `StandCost` (fee for the stand), `Notes`, `Status` Open / Closed, `ClosedAt`, `Currency` (workspace currency, frozen at creation). |
| `MarketSales` | One checkout (one receipt). |
| `MarketSaleLines` | Lines of a sale, price and cost frozen. |

### MarketSale

`ReceiptNumber` counts up from 1 **per market** (unique per workspace + market; a concurrent duplicate loses the unique index and the request is retried). `IdempotencyKey` is unique per workspace. `SoldAt` (when the customer paid; the client sends the original time for queued sales, never more than 5 minutes in the future or 14 days in the past) and `BusinessDate` (the day the sale belongs to in the seller's local time; defaults to the UTC date of `SoldAt`, must be within one day of it) are separate: day totals and the daily closing group on `BusinessDate`. `PaymentMethod` is `cash`, `card` or `other`. `Subtotal` = sum of line totals, `DiscountAmount` = sale-level discount in currency, `TotalAmount = max(0, Subtotal - DiscountAmount)`. `TotalCost` / `CostComplete` mirror orders. For cash `AmountTendered` (defaults to the total) and `ChangeGiven` are stored; card/other have none. Optional `CustomerId` (walk-ins have none), `Notes`.

### MarketSaleLine

`VariantId` (SET NULL when a variant is deleted, which stock history normally prevents), `Description`/`Sku`/`ListUnitPrice` snapshots, `UnitPrice` (list price unless overridden), `DiscountPercent`, `LineTotal = round(qty * unitPrice * (1 - pct/100), 2)`, `UnitCost` (4 decimals): the moving-average cost of the stock taken, i.e. the `UnitCost` of the `Sold` movement, falling back to the BOM cost when the variant has no cost history. `RiskAcknowledgedAt` records the commercial-rights acknowledgement.

## Checkout (`POST /api/markets/{marketId}/sales`)

Body: `idempotencyKey` (8-64 chars, required), `lines[] { variantId, quantity, unitPrice?, discountPercent? }`, `discountAmount?`, `paymentMethod`, `amountTendered?`, `customerId?`, `notes?`, `acknowledgeRisk?`, `soldAt?`, `businessDate?`.

- Books every line through `FinishedStockLedger.TryApply` as `Sold` with `MarketSaleId` set, requiring **available** stock (on hand minus reserved for orders). All lines and the sale are saved in one `SaveChanges`: all or nothing. Not enough stock gives 400 with `lines[i].quantity`; no stock moves and no sale is created.
- The endpoint is wrapped in `FinishedStockRetry.RunAsync`, so parallel checkouts can never oversell.
- Idempotent: the same key returns the existing sale with `replayed: true` and status 200 (a new sale is 201). Sending the same key for another market is 400. Clients must keep one key per cart/attempt and reuse it on every retry.
- A variant whose commercial rights are `NotCleared` needs `acknowledgeRisk: true`, otherwise 400 keyed `commercialRights` (same contract as orders, see licenses.md). The acknowledgement time is stored on each line.
- A closed market answers 409. A variant may appear once per sale. Archived products cannot be sold.

## Void (`POST /api/market-sales/{id}/void`, body `{ reason }`)

Marks the sale `Voided` (`VoidedAt`, `VoidReason`) and books a `Returned` movement per line (cost = the line's cost snapshot, `MarketSaleId` set) so the units are back in stock. Repeating the call returns the voided sale and books nothing twice. A sale is never edited or deleted; a closed market must be reopened first (409). Voided sales stay listed, are excluded from revenue and counted separately.

## Other endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| GET / POST | `/api/markets` | list (`?status=open|closed`, with `saleCount`, `voidedCount`, `revenue`, `cost`) / create |
| GET / PUT / DELETE | `/api/markets/{id}` | read, edit, delete (409 once a sale exists) |
| POST | `/api/markets/{id}/close`, `/reopen` | status |
| GET | `/api/markets/{id}/sales?date=&status=&limit=` | sales with lines, newest receipt first |
| GET | `/api/market-sales/{id}` | one sale |
| GET | `/api/markets/{id}/summary?date=` | totals (completed sales only): counts, items, subtotal, discounts, revenue, cost, gross profit, stand cost, totals by payment method, items sold (quantity, revenue, cost per variant), and per business date |
| GET | `/api/pos/catalogue` | variants with available stock, not archived: id, names, sku, price, `available`, `commercialRightsStatus` |

All require a workspace; other workspaces' data is 404.

## Immutability and reporting notes

- Sales and lines are never updated after checkout except `Status`, `VoidedAt`, `VoidReason`. The audit log records everything automatically.
- Profit of a sale = `TotalAmount - TotalCost` (stand cost is a market-level cost, not allocated to sales: report it from `Markets.StandCost`).
- Stock accounting stays in `FinishedStockMovements` (`Sold` and `Returned` with `MarketSaleId`).
- The "brought vs sold" view is the finished-stock ledger plus the market summary; there is no separate packing list.

## Daily closing (#41)

`MarketDayClosings`: one immutable record per market and business date (unique). It is a snapshot taken at closing time from the day's completed sales: `SaleCount`, `VoidedCount`, `ItemCount`, `Subtotal`, `Discounts`, `Revenue`, `Cost`, `CashTotal` / `CardTotal` / `OtherTotal` (by payment method), `OpeningFloat` (cash in the till at the start, entered by the seller), `ExpectedCash = OpeningFloat + CashTotal`, `CountedCash` (entered), `Difference = CountedCash - ExpectedCash` (negative = short), `StandCost` (market stand cost at that time, informational; not split per day), `Notes`, `ClosedAt`. There is no update or delete endpoint.

- `POST /api/markets/{id}/closings` `{ date, openingFloat?, countedCash, notes? }` creates it (201). 409 when the day is already closed, 400 for a future date or invalid amounts.
- `GET /api/markets/{id}/closings` lists closings; `GET /api/markets/{id}/closings/{yyyy-MM-dd}` returns one with the items sold that day (the closing report).
- Locking: once a day is closed, checkout with that `BusinessDate` and voiding a sale of that day answer 409. Other days of the market are unaffected. A queued offline sale that reaches the server after the closing is refused (the register shows it as refused).
- Reports should use the closing for cash reconciliation and the sales tables for everything else; both agree at the moment of closing.
