# Step 3 – D365 connectivity, company switching, lookups, tenant delete

## Built
- **Generic OData client** (`D365ODataClient`): GET with $select/$filter/$orderby/$expand/$top/$skip/$count and `cross-company=true`; POST, PATCH and DELETE by key; `$batch` with one atomic changeset.
  - Retries 429/503 up to 3 times, honouring Retry-After (capped at 20 s).
  - On a 401 it gets a fresh token once and retries.
  - D365 errors become readable messages: 400 → 422 `d365_validation`; a missing connection → 409; D365 failures → 502.
  - Every call is audited as `D365.<METHOD>` with company, duration and status. Secrets are never logged.
- **Company switching inside a tenant.**
  - Every tenant-scoped request carries `X-Company`. It must be one of the user's allowed companies, otherwise the call returns 403 `company_access_denied`.
  - `PUT /api/session/company` saves the choice as the user's current company.
  - The top bar has a company menu; switching refetches data for the new company.
- **Lookups.** `GET /api/lookups/{key}?search=&top=&site=&dimension=` serves live data with wildcard search (`eq '*text*'`).
  - Company-specific entities are filtered on `dataAreaId`.
  - Entity and field names are probed from a list of candidates, and the result is cached for each environment.
  - The home page has a "D365 reference data" card for trying it out.
- **Check D365 entities.** A button on the connection screen (tenant admin and super-admin) shows which entities and fields exist in that environment.
- **Delete tenant** (super-admin only). `DELETE /api/platform/tenants/{id}?confirm=<slug>` removes the connection, users and company access. Audit history is kept.
  - The UI is a "Danger zone" with a typed-slug confirmation.

## Entity/field names not yet confirmed (use "Check D365 entities")
| Lookup | Candidates tried in order |
|---|---|
| vendors | VendorsV3, VendorsV2, Vendors · VendorAccountNumber / VendorOrganizationName |
| products | ReleasedProductsV2, ReleasedProducts · ItemNumber / ProductName |
| procurementCategories | ProcurementCategories, ProcurementProductCategories, ProductCategories |
| sites | OperationalSites, Sites · SiteId / SiteName |
| warehouses | Warehouses · WarehouseId / WarehouseName (filter by OperationalSiteId) |
| mainAccounts | MainAccounts · MainAccountId / Name |
| financialDimensions, dimensionValues | FinancialDimensions / FinancialDimensionValues (DimensionAttributes / DimensionAttributeValues) |
| journalNames | LedgerJournalNames, JournalNames |
| currencies, workers, customers | Currencies; Workers (PersonnelNumber); CustomersV3, CustomersV2, Customers |
| caseCategories | CaseCategories, CaseCategoryHierarchyDetails, CaseCategory |
| units | UnitsOfMeasure, Units · UnitSymbol |
| Transactions | PurchaseRequisitionHeaders/Lines, PurchaseOrderHeadersV2/LinesV2, CaseDetails, LedgerJournalHeaders/Lines |

Confirmed in YELO environments: the companies entity is **LegalEntities** (`LegalEntityId`, `Name`).

## Manual configuration
- The D365 user mapped to each app registration needs read access to the entities above. It also needs create rights for the modules built in steps 4–8.
- Users only see the companies assigned to them under Users → company access.
