# Step 4 – Purchase requisitions

## Built
- **List** (`GET /api/purchase-requisitions?search=&status=&top=&skip=`): newest first, wildcard search on number or name, status filter, paging with total count.
- **Detail** (`GET /api/purchase-requisitions/{number}`): header, lines (company, item or category, quantity, price, amount, vendor, site/warehouse), totals per currency and an "Open in D365" link.
- **Create** (`POST /api/purchase-requisitions`):
  1. POST `PurchaseRequisitionHeaders`. D365 assigns `RequisitionNumber` from its number sequence.
  2. All lines go to `PurchaseRequisitionLines` in one `$batch` changeset, so they are created all or nothing.
  3. Each line gets `BuyingLegalEntityId` = the current company, plus `RequisitionLineNumber` 1..n and `RequisitionerPersonnelNumber` = the preparer.
  4. If the lines fail, the empty header is deleted and D365's message is shown.
- **UI:** Item or Category line types with live lookups. Picking an item fills its description and purchase unit; picking a vendor fills its currency. The warehouse list is filtered by the chosen site. Estimated totals show while editing. The preparer is remembered per tenant in the browser.
- **Cases** now map to Quality management **CAPACases**, with lookups for CAPACategories, CAPATypes and CAPASources.

## Fields used (standard PurchaseRequisitionHeaderV2Entity / PurchaseRequisitionLineEntity)
- **Header:** RequisitionName, RequisitionPurpose (Consumption/Replenishment), PreparerPersonnelNumber, DefaultRequestedDate, DefaultAccountingDate, DefaultBusinessJustificationDetails.
- **Lines:** RequisitionNumber, RequisitionLineNumber, BuyingLegalEntityId, ItemNumber or ProcurementProductCategoryName (+ ProductName), LineDescription, RequestedPurchaseQuantity, PurchaseUnitSymbol, PurchasePrice, CurrencyCode, VendorAccountNumber, ReceivingSiteId, ReceivingWarehouseId, RequestedDate, RequisitionerPersonnelNumber.

## Not confirmed
- **Deep link:** `?cmp=<company>&mi=PurchReqTableListPage` opens the requisition list in D365, not the specific record.
- **Category lines:** `ProcurementProductCategoryName` alone must identify the category. If the same name exists in two hierarchies, D365 may reject it.
- **Status filter:** uses the enum literal `Microsoft.Dynamics.DataEntities.PurchReqRequisitionStatus'Draft'`.

## Manual configuration
- The preparer must be an existing worker (personnel number) in D365 who is allowed to prepare requisitions for the buying company.
- Submitting for workflow approval is done in D365, because the workflow submit action is not available through standard OData. New requisitions are created as **Draft**.
- The D365 app user needs create rights on purchase requisitions.
