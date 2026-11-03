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

## What to add (X++)
**1. Line entities — extensions.** Add three fields mapped to the entity's **InventDim** data source, public names
exactly `InventDimension1`, `InventDimension2`, `InventDimension3`:
- `PurchaseRequisitionLineEntity` (`PurchaseRequisitionLines`)
- `PurchPurchaseOrderLineV2Entity` (`PurchaseOrderLinesV2`)

Add the same fields to each staging table extension.

**2. Assigned values per product master — three new read-only public entities** (the standard
`ProductMasterStyles` pattern; the released-variant entity is not based on InventDim, so it is not used):

| Public collection | Root data source | Joins | Fields |
|---|---|---|---|
| `CTKProductMasterModels` | `CTKEcoResProductMasterModel` | `EcoResProduct` on `ModelProductMaster = RecId`; `CTKEcoResModel` on `Model = RecId` | `ProductMasterNumber` = EcoResProduct.DisplayProductNumber, `Name` = CTKEcoResModel.Name |
| `CTKProductMasterTypes` | `CTKEcoResProductMasterType` | `EcoResProduct` on `TypeProductMaster`; `CTKEcoResType` on `Type` | `ProductMasterNumber`, `Name` |
| `CTKProductMasterYears` | `CTKEcoResProductMasterYear` | `EcoResProduct` on `YearProductMaster`; `CTKEcoResYear` on `Year` | `ProductMasterNumber`, `Name` |

Key: `ProductMasterNumber` + `Name`. `IsPublic = Yes`, OData read is enough.

Then build, sync the database, *Data management → Framework parameters → Entity settings → Refresh entity list*.

## Portal behaviour
**Tenant option, off by default:** *Admin → tenant → D365 connection → "Extended product dimensions (Model / Type / Year)"*
(`d365_connections.UseExtendedProductDimensions`). Turn it on only for the tenant whose environment has the extension
(YELO holding sandbox) after the X++ deployment. While it is off the portal never reads or writes InventDimension1-3.

- With the option on, on first use the portal also probes each entity with `$select=InventDimension1,InventDimension2,InventDimension3`
  (cached 1 hour per environment). If the probe fails (fields not deployed yet), the dimensions are not offered.
- After deployment: for a product master, each of Model / Type / Year that has values assigned in the
  `CTKProductMaster*` entities is shown as a required dropdown with those values; the line is sent with `InventDimension1-3`; Draft edits read
  them back. The server validates the same rule (`422 product_dimension_required`).
