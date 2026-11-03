# Model / Type / Year (InventDimension1-3) — D365 entity extension

YELO's CTK model binds three product dimensions to the generic InventDim fields:

| Dimension | InventDim field | Value table / public entity |
|---|---|---|
| Model | `InventDimension1` (EDT `CTKEcoResItemModelName`) | `CTKEcoResModel` / `CTKEcoResModels` |
| Type | `InventDimension2` (EDT `CTKEcoResItemTypeName`) | `CTKEcoResType` / `CTKEcoResTypes` |
| Year | `InventDimension3` (EDT `CTKEcoResItemYearName`) | `CTKEcoResYear` / `CTKEcoResYears` |

Per-product-master assignment: `CTKEcoResProductMasterModel/Type/Year` (no public entity).

Standard entities do **not** expose `InventDimension1-12`, so OData can't write them and D365 fails with
"Inventory dimension Model is a product dimension and must consequently be specified".

## What to add (X++ entity extensions)
Add three fields to each entity below, mapped to the entity's **InventDim** data source, with the public field
names exactly `InventDimension1`, `InventDimension2`, `InventDimension3` (the portal uses these names):

1. `PurchaseRequisitionLineEntity` (`PurchaseRequisitionLines`)
2. `PurchPurchaseOrderLineV2Entity` (`PurchaseOrderLinesV2`)
3. `EcoResReleasedProductVariantV2Entity` (`ReleasedProductVariantsV2`) — the portal reads each item's valid
   Model / Type / Year combinations from here

For each: add the same fields to the entity's staging table extension, build, sync the database, then
*Data management → Framework parameters → Entity settings → Refresh entity list*.

## Portal behaviour
- On first use the portal probes each entity with `$select=InventDimension1,InventDimension2,InventDimension3`
  (cached 1 hour per environment). Until the extension is deployed nothing changes.
- After deployment: for a product master, every dimension that has values in the item's released variants is
  shown as a required dropdown with those values; the line is sent with `InventDimension1-3`; Draft edits read
  them back. The server validates the same rule (`422 product_dimension_required`).
