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