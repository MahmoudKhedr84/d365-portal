# Steps 6–8 – CAPA cases, general journals, audit log

## Step 6 – Cases (Quality management CAPA cases)
- YELO environments expose no standard customer-service case entity, so by your decision the Cases module uses **CAPACases** (QMSCAPACaseEntity).
- **List/detail/create:**
  - Endpoint: `/api/cases`. The list is filtered to the current company (`dataAreaId`).
  - Filter by status: Planned, In process, Closed, Cancelled.
  - D365 assigns `CaseId`.
- **Fields sent:** dataAreaId, Description, CAPACaseClassification (None/Corrective/Preventive/CorrectiveAndPreventive), CAPACategoryId, CAPATypeId, CAPASourceCodeId, Priority, CAPADueDate, PlannedEffectiveDate, EmployeeResponsiblePersonnelNumber, Notes.
- **Lookups:** CAPACategories (CategoryId/CategoryDescription), CAPATypes (TypeId/TypeDescription), CAPASources (SourceCode/SourceDescription), Workers.
- **Not confirmed:**
  - Deep link menu item `QMSCAPACaseListPage`.
  - Whether `Priority` must be a value from a priority setup table (sent as free text).
  - Whether `CaseId` auto-numbers when omitted. That depends on the CAPA case number sequence; if it is manual, D365 will say so.

## Step 7 – General journals
- **List/detail/create:**
  - Endpoint: `/api/general-journals`, current company only.
  - Filter by Open/Posted; the list shows totals.
  - D365 assigns `JournalBatchNumber`, and vouchers according to the journal name's "New voucher" setting.
- **Lines:**
  - Each line has Date, Account type (Ledger/Customer/Vendor/Bank), Account and Description.
  - Each line has **either** a Debit or a Credit amount, plus a Currency.
  - An offset account type and account are optional, and so is a Voucher.
  - Ledger account = main account plus optional dimension segments, sent as `AccountDisplayValue`, e.g. `110110-001-022`.
- **Running totals** per currency are shown while editing (sticky bar) and on the detail page:
  - Debit, credit and balance, with "Balanced" in green.
  - A line with an offset account balances itself, so it counts on both sides.
  - Unbalanced journals can be saved but not posted.
- **Lookups:** JournalNames (Name/Description, its Currency pre-fills lines), MainAccounts, CustomersV3, VendorsV3, BankAccounts, Currencies.
- **Not confirmed:**
  - The BankAccounts entity and field names (`BankAccountId`/`Name`).
  - The dimension segment order. It must match the account structure used in D365, which is the same format you would type in the Account field.
- Posting/validating is done in D365 (`mi=LedgerJournalTable5`).

## Step 8 – Audit log
- **Tenant admins:** Administration → Audit log (`/api/admin/audit`).
- **Super-admins:** an Audit log card on each tenant's page.
- Entries cover sign-ins, admin changes, created documents and **every D365 call**: method, URL/body summary, status, duration, and D365's error text.
- Filters: search text, action (D365 calls / created documents / sign-ins / admin changes), result (failures only). Paging is 50 entries per page.
