# Master Data

ข้อมูลหลัก: บริษัท โปรไฟล์บริษัท สาขา ลูกค้า ผู้ขาย สินค้า หน่วยธุรกิจ ผังบัญชี รหัสเอกสาร หมวดค่าใช้จ่าย และประเภทหัก ณ ที่จ่าย.

All shared reference/master data. Most lists accept `?search`/`page`/`pageSize` where applicable; optional query params are nullable.

## Companies (super-admin)

Tax fields (`vatRegistered`, `vatRate`, `pnd30SubmissionMode`) are company master data — settable only here, never in a user-facing settings UI (§4.6). Every tax-field change writes an audit entry.

### `POST /companies`
- **Auth:** `master.company.manage`.
- **Request body (key fields):** `taxId`, `nameTh` (required); `nameEn?`, `legalEntityType`, `registrationDate?`, `vatRegistered` (bool), `vatRegisterDate?`, `fiscalYearStartMonth` (short), address parts (`addressTh?`, `subDistrict?`, `district?`, `province?`, `postalCode?`), `phone?`, `email?`, `paidUpCapital?`, `vatRate` (default `0.07`), `pnd30SubmissionMode` (default `"manual"`), plus founding registered-address parts `regHouseNo?`, `regMoo?`, `regSoi?`, `regStreet?`, `regBuilding?`, `regRoomNo?`, `regFloor?`, `regVillage?`. Schema: `CreateCompanyRequest`.
- **Response:** `201` (Location `/companies/{id}`).

### `PUT /companies/{id}`
- **Auth:** `master.company.manage`. **Path:** `id` (int).
- **Request body:** `nameTh` (required), `nameEn?`, `vatRegistered`, `vatRegisterDate?`, address parts, `phone?`, `email?`, `isActive`, `paidUpCapital?`, `vatRate`, `pnd30SubmissionMode`. Schema: `UpdateCompanyRequest`.
- **Response:** `204`.

### `GET /companies`
- **Auth:** `master.company.manage`. **Response:** `200` list (active + inactive).

### `GET /companies/{id}`
- **Auth:** `master.company.manage`. **Path:** `id` (int). **Response:** `200` company.

## Company Profile

Per-company profile (invoice header, branding, banking). Soft fields are admin-editable; hard (ภ.พ.20-bound) fields are locked.

### `GET /company-profile`
- **Auth:** Authenticated. **Response:** `200` profile, or `404` if none.

### `PUT /company-profile/soft`
- **Auth:** `master.company_profile.manage`.
- **Request body:** `tradeName?`, `logoUrl?`, `phone?`, `email?`, `website?`, `contactName?`, `bankName?`, `bankAccountNo?`, `bankAccountName?`, `ssoEmployerAccountNo?`. Schema: `UpdateCompanyProfileSoftRequest`.
- **Response:** `204`.

### `PUT /company-profile/registered-address`
- **Auth:** `master.company_profile.manage`.
- **Request body:** registered-address parts: `building?`, `roomNo?`, `floor?`, `village?`, `houseNo?`, `moo?`, `soi?`, `street?`, `subdistrict?`, `district?`, `province` (required), `postalCode` (required). Schema: `UpdateRegisteredAddressRequest`.
- **Response:** `204`. **Notes:** Allowed only after the admin confirms the DBD/ภ.พ.09 filing; audited.

### `PUT /company-profile/hard`
- **Auth:** `master.company_profile.manage`. **Response:** `501 Not Implemented` — legal fields are ภ.พ.20-bound (Phase 2 adds a 2-person approval flow).

### `POST /company-profile/logo`
- **Auth:** `master.company_profile.manage`.
- **Request:** `multipart/form-data`, part `file` (png/jpeg/svg/webp, max 1 MB).
- **Response:** `200` `{ logoUrl }`.

## Branches
Gated by `master.branch.manage`.
- `POST /branches` — create. Body: `branchCode`, `nameTh` (required), `nameEn?`, `isHeadOffice` (bool), `addressTh?` (`CreateBranchRequest`). → `201`.
- `PUT /branches/{id}` — update (`UpdateBranchRequest`). Path `id` (int). → `204`.
- `GET /branches` — list. → `200`.

## Customers

### `POST /customers`
- **Auth:** `master.customer.manage`.
- **Request body:** `customerCode`, `customerType`, `nameTh` (required); `nameEn?`, `taxId?`, `branchCode?`, `branchName?`, `vatRegistered` (bool), `billingAddress?`, `contactPerson?`, `phone?`, `email?`, `creditLimit` (decimal), `paymentTermDays` (int), `defaultCurrency`. Schema: `CreateCustomerRequest`.
- **Response:** `201`.

### `PUT /customers/{id}`
- **Auth:** `master.customer.manage`. **Path:** `id` (long). Body: `UpdateCustomerRequest` (adds `isActive`). → `204`.

### `GET /customers`
- **Auth:** `master.customer.read`. **Query:** `search?`, `page?`, `pageSize?`. → `200` paged list.

### `GET /customers/{id}`
- **Auth:** `master.customer.read`. **Path:** `id` (long). → `200` / `404`.

## Vendors
Gated by `master.vendor.manage`.
- `POST /vendors` — create. Body (key fields): `vendorCode`, `vendorType`, `nameTh` (required); `nameEn?`, `taxId?`, `branchCode?`, `vatRegistered` (bool), `address?`, `contactPerson?`, `phone?`, `email?`, `paymentTermDays`, `defaultCurrency`, `defaultWhtTypeCode?`, `isForeign?`, `hasThaiVatDReg?`, `countryCode?`, bank fields `bankName?`/`bankAccountNo?`/`bankAccountName?`/`swiftCode?` (`CreateVendorRequest`). → `201`.
- `PUT /vendors/{id}` — update (`UpdateVendorRequest`). Path `id` (long). → `204`.
- `GET /vendors` — list. Query `search?`, `page?`, `pageSize?`. → `200`.
- `GET /vendors/{id}` — detail. → `200` / `404`.

## Products
Gated by per-resource perms.
- `POST /products` — create. **Auth:** `master.product.manage`. Body: `productCode`, `nameTh` (required); `nameEn?`, `productType`, `defaultUomText?`, `defaultUnitPrice?`, `defaultOutputTaxCodeId?`, `defaultInputTaxCodeId?`, `defaultWhtTypeId?`, `descriptionTh?`, `notes?`, `isSaleable?` (default true), `isPurchasable?` (default false), `businessUnitId?` (`CreateProductRequest`). → `201`.
- `PUT /products/{id}` — update. **Auth:** `master.product.manage`. Path `id` (long). → `204`.
- `POST /products/{id}/deactivate` — soft-deactivate. **Auth:** `master.product.manage`. → `200`/`204`.
- `GET /products` — list. **Auth:** `master.product.read`. → `200`.
- `GET /products/{id}` — detail. **Auth:** `master.product.read`. → `200` / `404`.

## Business Units
Gated by `master.business_unit.manage` (except the company-setting read).
- `POST /business-units` — create. → `201`.
- `PUT /business-units/{id}` — update. Path `id` (int). → `204`.
- `DELETE /business-units/{id}` — delete. → `204`.
- `GET /business-units` — list. → `200`.
- `GET /business-units/{id}` — detail. → `200`.
- `PUT /business-units/company-setting` — set whether the company requires a BU dimension. → `204`.
- `GET /business-units/company-setting` — read that setting. **Auth:** Authenticated.

## Chart of Accounts
Gated by `master.coa.manage`.
- `POST /accounts` — create. Body: `accountCode`, `accountNameTh` (required), `accountNameEn?`, `accountType`, `parentId?`, `isHeader` (bool), `normalBalance` (`CreateAccountRequest`). → `201`.
- `PUT /accounts/{id}` — update (`UpdateAccountRequest`). Path `id` (long). → `204`.
- `GET /accounts` — list. Query: `type?` (AccountType), `activeOnly` (bool). → `200`.

## Document Prefixes
Gated by `sys.doc_prefix.manage`.
- `POST /document-prefixes` — create. Body: `prefixCode`, `documentType`, `descriptionTh` (required), `descriptionEn?`, `requiresEtax` (bool), `isFiscalDoc` (bool), `isExpense` (bool) (`CreateDocumentPrefixRequest`). → `201`.
- `GET /document-prefixes` — list. → `200`.

## Expense Categories
Split auth: write needs `sys.expense_category.manage`, read needs `sys.expense_category.read`.
- `POST /expense-categories` — create. **Auth:** `sys.expense_category.manage`. Body: `categoryCode`, `nameTh` (required), `nameEn?`, `description?`, `defaultExpenseAccountId?`, `defaultTaxCodeId?`, `defaultIsRecoverableVat` (bool), `defaultWhtTypeId?`, `isCapex` (bool), `isCogs` (bool), `parentCategoryId?` (`CreateExpenseCategoryRequest`). → `201`.
- `GET /expense-categories` — list (PV/VI pickers). **Auth:** `sys.expense_category.read`. → `200`.

## WHT Types
Read is open to any authenticated user; writes need `tax.wht_type.manage`.
- `GET /wht-types` — list. **Auth:** Authenticated. → `200`.
- `GET /wht-types/{id}` — detail. **Auth:** Authenticated. Path `id` (int). → `200`.
- `POST /wht-types` — create. **Auth:** `tax.wht_type.manage`. Body: `code`, `nameTh` (required), `nameEn?`, `incomeTypeCode`, `formType`, `rate` (decimal) (`CreateWhtTypeRequest`). → `201`.
- `PUT /wht-types/{id}` — update. **Auth:** `tax.wht_type.manage`. → `204`.
- `DELETE /wht-types/{id}` — deactivate. **Auth:** `tax.wht_type.manage`. → `204`.
- `POST /wht-types/{id}/reactivate` — reactivate. **Auth:** `tax.wht_type.manage`.
- `POST /wht-types/{id}/change-rate` — change the rate (new version). **Auth:** `tax.wht_type.manage`.
