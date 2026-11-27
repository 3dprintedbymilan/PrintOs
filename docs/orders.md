# Customers and orders

Module: `Sales` (`apps/api/src/PrintOs.Api/Modules/Sales`). This note is the contract for later work on the production queue (#36), finished-stock ledger and reservations (#37), markets/POS (#39) and reporting. Build on these tables; you should not need to change them.

## Customer

`Customers`: name (required), email, phone, address (free text, multi-line), VAT number (optional), notes, `ArchivedAt`. Archived customers stay linked to old orders but cannot be chosen for new ones. A customer with orders cannot be deleted (409); archive it instead. A walk-in or market sale has no customer: `Order.CustomerId` is nullable.

## Order

`Orders` (workspace scoped, `Number` is unique per workspace and counts up from 1).

| Field | Meaning |
| --- | --- |
| `CustomerId` | optional link to a customer (FK restrict) |
| `Status` | workflow state, see below (stored as text) |
| `Channel` | `direct`, `market`, `online`, `other` (POS agents: use `market`) |
| `Currency` | workspace currency at creation (frozen) |
| `OrderedAt`, `DueDate` | when the order was placed, when the customer expects it (date only, optional) |
| `DiscountAmount` | order-level discount in currency units |
| `Subtotal` | sum of `LineTotal` |
| `TotalAmount` | `max(0, Subtotal - DiscountAmount)`, what the customer owes |
| `TotalCost`, `CostComplete` | sum of `Quantity * UnitCost` over lines; `CostComplete` is false when a line has no cost snapshot |
| `AmountPaid` | sum of payments |
| `Notes`, `ConfirmedAt`, `DeliveredAt`, `CancelledAt` | free text and status timestamps |

All totals are stored and recalculated on every change by `OrderRules.Recalculate`, so reports can aggregate columns directly. Profit is `TotalAmount - TotalCost`; margin is `Profit / TotalAmount`.

### Payment status

Derived (not stored) from `AmountPaid` and `TotalAmount`: `Unpaid` (nothing paid, total above zero), `Partial`, `Paid` (`AmountPaid >= TotalAmount`, so a free order is paid). `OrderPayment` rows hold the individual payments (`Amount`, `PaidAt`, `Method` of cash/transfer/card/other, `Note`). Overpayment is rejected. Payments can be added or removed in every status except `Cancelled`. The list endpoint filters with `payment=unpaid|partial|paid|open` using the stored columns.

## OrderLine

`OrderLines`, ordered by `Position`.

- `VariantId` is **nullable**: null means a free-text line (custom job, shipping, repair). The FK is `SET NULL`, so deleting a variant never deletes order history.
- Snapshots taken when the line is created (or when its variant changes): `Description` ("Product - Variant" or the typed text), `Sku`, `ListUnitPrice` (the variant price then), `UnitCost` (the variant production cost then, from the BOM cost calculation, excluding marketplace fee). They never follow later price or cost changes. Re-saving an order with the same line `id` and the same variant keeps the snapshot.
- `UnitPrice` is what the customer pays per piece: the list price unless overridden. `DiscountPercent` is per line. `LineTotal = round(Quantity * UnitPrice * (1 - DiscountPercent / 100), 2)`.
- Free-text lines carry an optional `UnitCost` supplied by the user.
- `RiskAcknowledgedAt`: see commercial rights.

Reservation (#37) and production tasks (#36) should add their own tables keyed by `OrderLine.Id` instead of widening these.

## Status workflow

`Draft -> Confirmed -> InProduction -> Ready -> Delivered`, plus `Cancelled`.

| From | Allowed next |
| --- | --- |
| Draft | Confirmed, Cancelled |
| Confirmed | InProduction, Ready, Cancelled |
| InProduction | Ready, Confirmed, Cancelled |
| Ready | Delivered, InProduction, Cancelled |
| Delivered | none (terminal) |
| Cancelled | Draft (reopen) |

`POST /api/orders/{id}/status` with `{ "status": "...", "acknowledgeRisk": false }` validates against this table (`OrderRules.CanTransition`); anything else is a 400 on `status`. Confirming needs at least one line. The response lists `allowedTransitions`, `linesEditable` (Draft, Confirmed only), `headerEditable` (not Delivered/Cancelled) and `paymentsEditable` (not Cancelled). Other modules that move an order (for example the production queue marking it `InProduction`/`Ready`) should go through `OrderRules.CanTransition` rather than writing `Status` directly. Delivering an unpaid order is allowed (payment is tracked separately).

## Commercial rights (#24)

When a line references a variant whose commercial rights are `NotCleared`, creating or saving the line, and confirming the order, requires `acknowledgeRisk: true`, otherwise the API answers 400 with `lines[i].commercialRights` (or `commercialRights` on the status call). Acknowledging stamps `OrderLine.RiskAcknowledgedAt`, which the audit log records automatically. Responses expose per line `commercialRightsStatus`, `commercialRightsBlockers` (model names) and `riskAcknowledged`, and `hasRightsWarning` per order. Rights are evaluated live; a line that was cleared when added is re-checked when the order is confirmed.

## API

- `GET /api/orders?status=active|Draft,Confirmed&payment=&customerId=&q=&from=&to=&limit=` summaries (`q` matches order number, customer name or line text)
- `POST /api/orders`, `GET|PUT|DELETE /api/orders/{id}` (PUT replaces the header; `lines: null` keeps the lines, a list replaces them; delete only for payment-free drafts)
- `POST /api/orders/{id}/status`, `POST /api/orders/{id}/payments`, `DELETE /api/orders/{id}/payments/{paymentId}`
- `GET|POST /api/customers`, `GET|PUT|DELETE /api/customers/{id}`, `POST /api/customers/{id}/archive|restore`; order history per customer is `GET /api/orders?customerId=`

## Privacy

Customers and orders are covered by the account export (`ExportCatalog`) and removed by account deletion.
