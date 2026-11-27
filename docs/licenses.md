# Licenses

License tracking lives in the API module `Modules/Licensing`. A `License` is a workspace-scoped record of the rights you hold for one or more models.

## Data

`License`: name, `Source` (OwnDesign, MakerWorld, Printables, Thingiverse, Other), `Type`, `AttributionRequired` + `AttributionText`, `UnitsLimit` (only for CommercialWithLimits), `ValidFrom` / `ValidUntil` (dates, both optional), notes, `ProofUrl` and an optional proof file (PDF/PNG/JPEG/WebP, max 10 MB) stored through `IFileStorage` under `{workspaceId}/licenses/{licenseId}/proof-*`. The proof is downloaded through a time-limited signed URL (`GET /api/licenses/{id}/proof-url`, then `/api/license-files/{token}`).

`LicenseType` decides commercial permission: `Personal` = no, `Commercial` and `CommercialWithLimits` = yes.

`PrintModel.LicenseId` is a nullable FK (set null when the license is deleted). Link with `PUT /api/models/{id}/license` body `{ "licenseId": guid|null }`. The ordinary model update is untouched, so the model API stays backward compatible. Model detail returns `licenseId` and `license` (summary with assessment); the list returns `licenseId` and `licenseStatus`.

## Status

`LicenseRules.Assess(license, today, unitsUsed)` is a pure function returning a `LicenseAssessment`. Precedence:

1. `Expired` (ValidUntil before today)
2. `NotYetValid` (ValidFrom after today)
3. `PersonalOnly`
4. `LimitReached` (finished units >= UnitsLimit)
5. `ExpiringSoon` (within `LicenseRules.ExpiringSoonDays` = 30 days)
6. `Valid`

`NoLicense` is used for a model without a license. `LimitNearlyReached` is set at 90% of the units limit. Units used = sum of `FinishedUnits` of `Done` print jobs whose `ModelId` points at a model with that license.

## Reusing it for the commercial-rights check (#24)

`AllowsCommercialSale` is true only for `Valid` and `ExpiringSoon`. Never reimplement it; call:

- `LicenseAssessor.AssessModelAsync(modelId)` or `AssessModelsAsync(modelIds)` (scoped service, registered by the module) for models, which returns `NoLicense` / `AllowsCommercialSale = false` for unlicensed models.
- `GET /api/models/{id}/commercial-use` for the same result over HTTP.
- `AttributionRequired` in the assessment tells the caller that attribution text is needed with the sale; the text is on the license summary of the model.

No schema change is needed for #24.

## Endpoints

`GET/POST /api/licenses`, `GET/PUT/DELETE /api/licenses/{id}`, `POST/DELETE /api/licenses/{id}/proof`, `GET /api/licenses/{id}/proof-url`, `PUT /api/models/{id}/license`, `GET /api/models/{id}/commercial-use`. All require a workspace; other workspaces' data returns 404. Changes are covered by the audit log.

## Commercial rights per product (#24)

`CommercialRightsRules` (Modules/Products) derives a verdict per variant from the models on its BOM print lines, using `LicenseAssessor`. Worst case wins: any model whose assessment does not allow commercial sale makes the variant `NotCleared`; no linked model is `NoModel` (neutral); otherwise `Cleared` (with `expiringSoon` as a hint). Attribution requirements are aggregated per variant and per product.

- Variant responses carry `readyForSale`, `saleRiskAcknowledged` and `commercialRights`; product responses and the list carry `commercialRightsStatus` (worst variant).
- `GET /api/products/{id}/commercial-rights` and `GET /api/products/{id}/variants/{variantId}/commercial-rights`.
- `PUT /api/products/{id}/variants/{variantId}/sale-status` body `{ readyForSale, acknowledgeRisk }`. Marking a `NotCleared` variant ready returns 400 with an error keyed `commercialRights` unless `acknowledgeRisk` is true; the acknowledgement is stored in `SaleRiskAcknowledgedAt` and appears in the audit log. The UI asks for confirmation before sending it.
- Print jobs accept an optional `modelId` (must belong to the workspace) so finished units count toward license limits.
