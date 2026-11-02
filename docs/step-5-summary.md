# Step 5 – Purchase orders

## Built
- **List** `GET /api/purchase-orders?search=&status=&approval=&top=&skip=`: shows only the current company (`dataAreaId`). Search covers the number, vendor account and vendor name. You can filter by order status (Open order/Received/Invoiced/Canceled) and approval status. Paging includes the total count.
- **Detail** `GET /api/purchase-orders/{number}`: header facts, lines (with variant), total and an "Open in D365" link (`mi=PurchTableListPage`).
- **Create** `POST /api/purchase-orders`:
  1. POST `PurchaseOrderHeadersV2` with `dataAreaId` = current company. D365 assigns the number and fills currency, terms and addresses from the vendor.
  2. All lines go to `PurchaseOrderLinesV2` in one `$batch` changeset, with LineNumber 1..n and `CalculateLineAmount = Yes`.
  3. Lines inherit the header's default site and warehouse unless overridden.
  4. If the lines fail, the empty order is deleted and D365's message is shown.
- **Shared with requisitions:** the line editor (Item/Category, on-hold items blocked, variant picker for product masters, unit, price, site/warehouse) and the on-hold check on the server.

## Fields used
- **Header:** dataAreaId, OrderVendorAccountNumber, CurrencyCode, RequestedDeliveryDate, AccountingDate, DefaultReceivingSiteId, DefaultReceivingWarehouseId, VendorOrderReference, RequesterPersonnelNumber.
- **Lines:** dataAreaId, PurchaseOrderNumber, LineNumber, ItemNumber or ProcurementProductCategoryName, LineDescription, OrderedPurchaseQuantity, PurchaseUnitSymbol, PurchasePrice, CalculateLineAmount, ReceivingSiteId, ReceivingWarehouseId, RequestedDeliveryDate, ProductConfigurationId/SizeId/ColorId/StyleId/VersionId.

## Not confirmed
- The deep link opens the PO list in the company, not the specific order.
- Confirming the PO and submitting it to workflow are not available through standard OData; both are done in D365.

## Manual configuration
- The integration user needs rights to create purchase orders in each company the portal users work in.
- If change management (approval workflow) is on, new POs start as **Draft**.

## Fixes after testing in WFQ
- **PO number.** WFQ's purchase order number sequence is manual, and D365 said "Field 'Purchase order' must be filled in". The PO form now has an optional **Purchase order number** field. When D365 needs a number, the portal says so (`po_number_required`) and highlights that field.
- **Tax.** PO header **Sales tax group** (`SalesTaxGroupCode`) is defaulted from the vendor and inherited by lines. Each line (PO and requisition) has a **Sales tax group** and an **Item sales tax group** (`SalesTaxItemGroupCode`, defaulted from the item's purchase item tax group). Lookups: `TaxGroups` (TaxGroupCode/Description) and `TaxItemGroups` (TaxItemGroupCode/Description).
