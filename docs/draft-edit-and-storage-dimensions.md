# Draft editing and item storage dimensions

## Editing Draft documents
- **Purchase requisitions** — `PUT /api/purchase-requisitions/{number}`; allowed only while `RequisitionStatus = Draft` (otherwise `409 not_draft`). UI: **Edit** on the detail page → `/purchase-requisitions/{number}/edit`.
- **Purchase orders** — `PUT /api/purchase-orders/{number}`; allowed only while `DocumentApprovalStatus = Draft`. The vendor can't change (`422 vendor_locked`). UI: **Edit** → `/purchase-orders/{number}/edit`.
- One `$batch` changeset, all or nothing: header `PATCH` → `DELETE` removed lines → `PATCH` kept lines → `POST` new lines (numbered after the highest existing line).
- A line keeps its D365 line number (`lineNumber` in the body). Changing its **item or category** replaces it (delete + add), because `ItemNumber` (and on PO lines `ProcurementProductCategoryName`) is read-only after create.
- Not editable after create: PR purpose and requester; PO vendor and number.
- Audit log: action `Update` with counts of updated / added / removed lines.

## Item storage dimensions
`GET /api/items/{item}/dimensions` returns the item's mandatory storage dimensions in the current company:

1. `ReleasedProductsV2` → `StorageDimensionGroupName`, `ProductType`.
2. The group's setup from `StorageDimensionGroups` (flat flags such as `IsWarehouseMandatory`) or, if that isn't exposed, per-dimension setup rows (`StorageDimensionGroupFieldSetups`). Flags are matched by name, so version differences in field names don't matter.
3. Site is always mandatory (F&O rule). Service items: site only.
4. **Fallback** when the group setup can't be read (`source = "default"`): Site + Warehouse mandatory. Check it in *Admin → Check D365 entities → Storage dimension groups*.

Rules on every item line (create and edit):
- **Mandatory** dimensions are shown and required (UI and server, `422 dimension_required` listing line + dimension).
- **Non-mandatory** dimensions are hidden and cleared; the server drops any value sent for them, and on edit it writes `""` so old values are removed.
- Category lines (no item) keep optional site / warehouse.

| Screen | Dimensions on the line | Entity fields |
|---|---|---|
| Purchase requisition | Site, Warehouse | `ReceivingSiteId`, `ReceivingWarehouseId` |
| Purchase order | Site, Warehouse, Location, Inventory status | + `ReceivingWarehouseLocationId`, `OrderedInventoryStatusId` |

PO header default site / warehouse pre-fill a line's mandatory site / warehouse. New lookups: `warehouseLocations` (filtered by `warehouse`) and `inventoryStatuses`.

## Not covered / to confirm in UAT
- The storage dimension group entity was not exposed in the demo environment used for development; confirm it in YELO UAT with the entity check. Until then the fallback (Site + Warehouse) applies.
- Tracking dimensions (batch / serial) aren't asked for: D365 captures them at receipt, not on the order.
- Cases and general journals have no item lines, so these rules don't apply there.
