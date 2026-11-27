# Reporting

Module: `Reporting` (`apps/api/src/PrintOs.Api/Modules/Reporting`). Read-only aggregation over orders, market sales and the finished-stock ledger. It owns no tables and no migrations. All endpoints need a workspace and only ever see that workspace's data.

## Principles

- **Frozen snapshots only.** Revenue and cost come from `OrderLines` / `MarketSaleLines` (`LineTotal`, `UnitCost`), never from current prices or BOM costs, so changing a price or a bill of materials does not rewrite history.
- The pure maths lives in `ReportPeriods`, `ReportMath` and `ReportAggregator` (unit tested); `ReportLoader` only turns rows into `SaleFact`s.
- Money is in the workspace currency and rounded to 2 decimals after summing; percentages have 1 decimal.

## What counts

| Source | Included | Date used |
| --- | --- | --- |
| Orders | status Confirmed, InProduction, Ready or Delivered (not Draft, not Cancelled) | `OrderedAt` (UTC date) |
| Market POS sales | status Completed (voided sales are excluded completely) | `BusinessDate` |
| Customer returns | `Returned` stock movements linked to a counted order (`OrderId` set): subtract units, revenue (the line's net unit price) and cost (the movement's unit cost) | return date |
| Market stand costs | `Markets.StandCost` | market `StartDate`; counted once, in the period containing the start |

Order-level and sale-level discounts are spread over the lines in proportion to their line totals, so per-product revenue adds up to the real total (`revenue = lineTotal * total / subtotal`). Cost of a line is `quantity * unitCost`; a missing cost counts as 0 and marks the figures `costComplete = false`. Electricity, machine and labour are included to the extent they are part of the frozen unit cost.

## Formulas

- `grossProfit = revenue - cost`
- `marginPercent = grossProfit / revenue * 100` (null without revenue)
- `netProfit = grossProfit - standCosts`, `netMarginPercent = netProfit / revenue * 100`. Stand costs only exist for the POS channel and the "all" view; they are never allocated to products.
- Delta versus the previous period: absolute change, percentage change (null when the previous value is 0) and margin change in percentage points.

## Periods

`period=week|month|quarter|year|custom` with optional `date` (anchor, default today UTC). Presets are whole calendar periods (week Monday to Sunday), so the running period is partial. The previous period is the calendar period before it. `custom` needs `from` and `to` (max 731 days) and is compared with the equally long range directly before it. Trend granularity: day up to 31 days, week up to 190 days, otherwise month (empty buckets are included as zeros).

## API

`GET /api/reports/margin?period=&date=&from=&to=&channel=all|orders|pos&productId=`

Returns `period`, `currency`, `current` / `previous` figures, `delta`, `granularity`, `trend`, and breakdowns `channels`, `markets`, `products`, `variants`. `productId` filters everything to one product (drill-down). 400 with field keys `period`, `channel`, `from`, `to` on invalid input.
## Bestsellers (#44)

`GET /api/reports/bestsellers?period=&date=&from=&to=&channel=&productId=&level=product|variant&limit=` (limit 1-50, default 10). Uses the same facts as the margin report, restricted to the selected period. Returns `totals` (units, revenue, gross profit, item count) and three rankings, `byUnits`, `byRevenue`, `byProfit`, each item with `rank`, units, revenue, profit, margin % and its share of the total (`unitsSharePercent`, `revenueSharePercent`, `profitSharePercent`; null when the total is not positive). Items with net units of 0 or less (fully returned) are left out. Ties are broken by units, then name. Product level groups variants together; free-text lines are grouped by their description (case-insensitive).

`GET /api/reports/slow-movers?days=30` (1-730): variants with finished stock on hand (`FinishedStockLevels.OnHand > 0`) whose last `Sold` movement is at least N days ago. A variant that never sold counts from the first stock increase, so freshly produced stock is not "slow". Sorted never-sold first, then longest since the last sale, then stock value (`OnHand * AverageUnitCost`).

## CSV export (#45)

- `GET /api/reports/margin/export` (same query as the margin report; `part=breakdown` default, or `part=trend`), `GET /api/reports/bestsellers/export` (same query plus `sort=units|revenue|profit`, up to 50 rows), `GET /api/reports/slow-movers/export?days=`.
- Format: UTF-8 with BOM (so Excel opens it correctly), CRLF line endings, comma separated, snake_case header. Numbers are locale-agnostic: invariant culture, dot decimal separator, no thousands separator, money with 2 decimals, percentages with 1. Text cells are quoted when they contain a comma, quote or newline (quotes doubled), and cells starting with `=`, `+`, `-`, `@` or a tab get a leading apostrophe so spreadsheets do not run them as formulas.
- The margin breakdown has one row per scope (`total`, `channel`, `market`, `product`, `variant`) with units, revenue, cost, gross profit, margin %, stand costs, net profit and `cost_complete`.
- File names: `margin-<from>_<to>.csv`, `margin-trend-<from>_<to>.csv`, `bestsellers-<from>_<to>.csv`, `slow-movers-<days>d.csv`. Same validation and authorization as the JSON reports.

## Screens

Business > Reports (`/reports`) is the hub: period and channel selector, KPI tiles with comparison, revenue/profit trend chart (hand-rolled SVG, legend, hover/tap read-out and a table view), top products with drill-down (`/reports/margin?productId=`), links to the detail screens (`/reports/margin`, `/reports/bestsellers`) and the CSV downloads. Filters live in the URL query so a view can be shared and survives a reload.

## Advanced analytics (#55)

Four analytics reports extend the hub: `/reports/production`, `/reports/sales`, `/reports/inventory`, `/reports/profitability`. They share the period selection above (current and previous period, `period`, `date`, `from`, `to`), return JSON from `GET /api/reports/analytics/<name>` and CSV from `GET /api/reports/analytics/<name>/export?part=` (same format rules as the CSV export section). The pure maths is in `ProductionAnalytics`, `SalesAnalytics`, `InventoryAnalytics` and `ProfitabilityAnalytics` (unit tested); `AnalyticsLoader` only turns rows into facts. Sales and profit figures use the frozen order/sale line snapshots, never current prices.

### Production (`/analytics/production?hoursPerDay=24`; parts `printers|materials|trend`)

- Counted jobs: status Done, Failed or Cancelled, dated by `FinishedAt`, else `StartedAt`, else `CreatedAt`. Print hours, filament and cost only count Done and Failed jobs (the machine ran); cancelled jobs are only counted.
- Actual minutes = `FinishedAt - StartedAt`, else the logged `DurationMinutes`. Estimate = the variant's Print BOM minutes x units (`FinishedUnits`, else `PlannedUnits`, else 1); only Done jobs with both values feed "duration vs estimate": `variance% = (sum actual - sum estimate) / sum estimate`.
- `successRate = done / (done + failed)`. `waste% = wasteGrams / grams` (waste is part of the grams). Filament cost = grams x the spool's purchase cost per gram.
- `utilisation = printHours / (printers x days x hoursPerDay)` (per printer: one printer). A job's hours are booked on its date. Printers counted: non-archived printers plus any with jobs.
- `costPerPrintHour = (filamentCost + depreciationPerHour x hours + powerWatts/1000 x hours x electricityPrice) / hours`. Jobs do not freeze rates, so depreciation and the electricity price are the current ones.
- Failure causes are not stored per job; failures are reported per material (a job with several materials counts for each) and per printer.

### Sales (`/analytics/sales?channel=`; parts `channels|customers|products|timing|trend`)

- Documents are counted orders (by `OrderedAt`) and completed POS sales (by `BusinessDate`). Revenue is the document `TotalAmount`; returns are not netted here (the product ranking and margin report do net them).
- `averageOrderValue = revenue / documents`. Discount of a document = list value (`qty x listUnitPrice`, falling back to the unit price) minus its total, never negative; `discount% = discount / (revenue + discount)`.
- A customer is "repeat" when they have 2+ documents in the period or any counted document before it. `repeatRate = repeat / customers`. Documents without a customer are listed as "no customer linked".
- Lead time: orders delivered in the period, `DeliveredAt - ConfirmedAt` in days (average, median, longest, buckets same day, 1-3, 4-7, 8-14, over 14).
- Market timing: POS sales by weekday (Monday = 1) and hour of `SoldAt`, in UTC.
- Products: top 10 by revenue (same ranking as the bestsellers report), customers: top 10 (all in the CSV).

### Inventory (`/analytics/inventory?deadDays=90`; parts `materials|finished|dead`)

- Stock values are as of now: filament = remaining grams x spool cost per gram (non-archived spools); finished goods = on hand x average unit cost.
- Usage per material = `Consumption` movements in the period; `usagePerDay = grams / days`; `daysOfCover = onHand / usagePerDay`; `turnover = cost of grams used in the period / current stock value`. Finished goods: cover = on hand / (units sold per day), turnover = frozen cost of goods sold / stock value.
- Purchase price trend: average landed price per kg of non-cancelled purchases in the period vs the previous period; `impact = (current - previous) per kg x kg used in the period`.
- Dead stock: non-archived spools with grams left and no consumption for `deadDays` (never used counts from their creation), plus finished goods that did not sell for `deadDays` (the slow-movers rule). Dead stock value is their sum.

### Profitability (`/analytics/profitability?channel=&productId=`; parts `breakdown|components|trend`)

- `contribution = revenue - cost of goods - fees`; `netProfit = contribution - stand costs` (stand costs only for the POS/all view without a product filter, never allocated to products). Margin rows exist per channel, product and customer.
- Cost components: the frozen unit cost of each sold line is split over material, electricity, machine, labour, packaging and extras in the proportions of the variant's latest saved cost snapshot at the sale date (the earliest one when the sale predates all). Sold items without a snapshot show as "not itemised"; save a cost snapshot to itemise them. The split always adds up to the frozen cost of goods.
- Fees = revenue x the snapshot's frozen `feePercent` (0 without a snapshot).

### Screens

All four screens have the period selector (plus the channel selector for sales and profitability), KPI tiles with comparison, hand-rolled SVG charts with a legend, keyboard and tap read-out and a "show as table" alternative, loading, empty and error states, drill-downs (printer, customer, product margin/profit, spool, finished-stock variant) and one CSV button per part. Chart colours come from the `--chart-*` tokens (light and dark values).
