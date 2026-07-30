# C2 — Cross-tenant isolation + RBAC escalation break-it (prod v1.27.1)

Agent: **C2** · Target https://teas.kazaki-rio.com · attack company **co5 / id=5** ("บริษัท ทดสอบ VAT (DUMMY)") — `GET /api/proxy/me` confirmed `companyId:5, isSuperAdmin:false, allowedCompanies:[5]` for every account used. **No write performed against any company (co5 included).** Cross-tenant probing was READ-only.
Logins (all HTTP 200, role-slot-code suffix as corrected by siblings): sales01/A1, ar01/A5, audit01/A6, chief01/A7, admin01/A8, tax01/B1. (The "all logins 401" memory scare = the pre-correction wrong-suffix run.)

---

## >>> HEADLINE <<<
**NO CROSS-TENANT LEAK. NO HTTP 500. NO cross-tenant privilege escalation.**
Cross-tenant isolation is SOLID on every surface probed. The two real defects are both **intra-tenant privilege problems** (a low-priv role reaching a privileged effect *within co5*), not tenant breaches:
- **HIGH (NEW)** — a 2nd F10-class side route: sales01 (denied `POST /tax-invoices` 403) can mint a ใบกำกับภาษี via `POST /sales-orders/{id}/create-invoice` (gated on `sales_order.manage`).
- **HIGH (re-confirm F16)** — `GET /attachments/{id}/download` skips the parent-permission guard: any co5 user with `sys.attachment.read` downloads ANY co5 attachment by id. **Upgrade question answered: it does NOT skip company scope — NOT a cross-tenant CRIT.**

---

## PASS / FAIL per sub-area
| # | Sub-area | Result |
|---|----------|--------|
| 1 | Cross-tenant IDOR sweep (GET-by-id, ~20 resource types) | **PASS** — every genuinely non-co5 id → 403/404; every 200 verified as co5's own data (identifying fields) |
| 1b | F16 attachment download — cross-tenant upgrade check | **PASS (isolation holds)** — download honors EF filter + DB RLS; no foreign-company file retrievable |
| 1c | F16 attachment download — intra-tenant parent-guard | **FAIL** — parent read-perm bypass confirmed live (F-C2-2) |
| 2 | RBAC scope escalation (indirect write routes) | **FAIL** — 2 routes mint a TI without `tax_invoice.create` (F-C2-1 new + F10 known); audit01 fully clean |
| 3 | Token / auth (switch-company, refresh, unauth, logout) | **PASS** (1 INFO) — company-claim tamper blocked; no scope-widening; unauth→401; no logout/revoke = INFO |
| 4 | Report scoping (companyId / businessUnitId override) | **PASS** — no `companyId` param exists; `businessUnitId` cannot cross tenants (empty, RLS-scoped) |
| 5 | Admin-only endpoints as non-admin | **PASS** — /companies, /admin/rbac?companyId=X, switch-company, api-keys all correctly gated |

---

## Cross-tenant sweep — evidence of isolation (the C2 deliverable)
Method: co5 uses **shared global PK sequences** interleaved with other companies, so ids outside co5's owned set belong to other tenants. `chief01` (broad read perms → permission cannot mask a company-scope leak) requested by-id across resource types.
- Every by-id request that resolved 200 returned **co5's own** data — confirmed by identifying fields: `tax-invoices/1` supplierName = co5's own name; `journals/200` description "A5 burst" (sibling co5 test); PV vendorName = co5's test vendor. (Note: list endpoints are **paginated** — id "gaps" in a single page are NOT ownership boundaries; content, not list position, was used to attribute ownership.)
- Every genuinely non-co5 id → **404** (receipts 1/5/21, vendor-invoices 1/3/16, purchase-orders 1/3/20, customers 1/3/11, vendors 1/2/11, billing-notes 1/3/9, quotations 1/5/8, sales-orders 1/3/15, delivery-orders 1/3/15, employees 1/2, expense-claims 1/3, fixed-assets 1/2, wht-certificates 1/3, tax-adjustment-notes 2/3, payroll/runs 1/2, bank-accounts 2/3, business-units 1/2, products 1/2). 404 (not 403) correctly gives no existence oracle.
- Edge ids (0, -1) → clean 404, **no 500**.
- Defense-in-depth verified in code: EF global query filter (`e.CompanyId == _tenant.CompanyId`, AccountingDbContext:174) **plus** DB RLS `company_isolation` on the tenant tables (010/572/573/581/600) keyed on `app.company_id` set by TenantMiddleware.

---

## FINDINGS

### F-C2-1 · HIGH · NEW F10-class escalation — sales01 mints a Tax Invoice via `sales-orders/{id}/create-invoice`, bypassing the `tax_invoice.create` denial
**Area:** `SalesChainEndpoints.cs:98` (`POST /sales-orders/{id}/create-invoice`).

**Root cause (code):** the route is gated on `soManage` (`sales.sales_order.manage`) and, for a VAT company, its handler calls `tiSvc.CreateFromSalesOrderAsync(id)` — minting a ใบกำกับภาษี:
```
vatMode ? tax_invoice_id = await tiSvc.CreateFromSalesOrderAsync(id)   // co5 IS vatMode=true
        : billing_note_id = await bnSvc.CreateFromSalesOrderAsync(id)
```
The direct route `POST /tax-invoices` is gated on `sales.tax_invoice.create`, which SALES_STAFF is explicitly **denied**. Same asymmetry class as the known F10/F8 billing-note route, via a **second, independent** path.

**Repro (auth-gate evidence — no document created; `id=999999` so the not-found lookup runs after the policy passes):**
```
sales01  POST /tax-invoices                          {}   -> 403   (denied: no tax_invoice.create)
sales01  POST /sales-orders/999999/create-invoice          -> 404   (POLICY PASSED — soManage held; handler ran)
sales01  POST /billing-notes/999999/create-tax-invoice     -> 404   (POLICY PASSED — known F10)
sales01  POST /sales-orders  {}                            -> 400   (confirms sales01 holds sales_order.manage)
```
A `403` on the direct route vs handler-execution (`404`, not `403`) on both side routes is the RBAC asymmetry. `CreateFromSalesOrderAsync` mints a real, immutable TI (sibling A1/F8 proved the twin billing-note route end-to-end); C2 stopped at the auth gate to avoid minting an immutable prod doc under the wrong role.

**Expected:** a role denied `POST /tax-invoices` (403) cannot produce a legally-binding tax invoice with output VAT by ANY route.
**Actual:** two side routes (`sales-orders/{id}/create-invoice`, `billing-notes/{id}/create-tax-invoice`) do exactly that, gated only on the sales-doc `manage` perm.

---

### F-C2-2 · HIGH · F16 re-confirmed live — `GET /attachments/{id}/download` skips the parent-permission guard (intra-tenant; NOT cross-tenant)
**Area:** `AttachmentEndpoints.cs:77` + `AttachmentService.OpenForDownloadAsync` (AttachmentService.cs:171).

**Root cause (code):** `POST /attachments`, `GET /attachments` (list) both call `ParentGuard` (requires the parent's read perm, e.g. `expense.claim.read`). The `/download` route calls neither — only `.RequireAuthorization(sys.attachment.read)`. So `sys.attachment.read` alone lets a caller download ANY attachment in the company by iterating ids, even parents whose read perm they lack.

**Repro (all co5 data; sales01 = SALES_STAFF, lacks `expense.claim.read`):**
```
sales01 GET /attachments?parent_type=EXPENSE_CLAIM&parent_id=4  -> 403  "'expense.claim.read' required..."
sales01 GET /attachments/8/download                            -> 200  (193B receipt.pdf on that SAME claim)
sales01 GET /attachments/4/download                            -> 200  (kbiz bank-statement CSV, 1373B)
ar01 / audit01 /attachments/{8,12}/download                    -> 200  (same bypass across roles)
```

**Cross-tenant upgrade check (the C2-specific task) — NEGATIVE:** `OpenForDownloadAsync` runs on the RLS'd connection under the EF global query filter, so it does **not** skip company scope. `chief01` (co5) downloading ids 1/2/3/6/7 → **404**; every 200 was a co5-identifying file (kbiz-statement-co5, co5 expense-claim receipts). No foreign-company attachment is retrievable. **F16 stays HIGH intra-tenant; it is NOT a cross-tenant CRIT.**

**Expected:** downloading an attachment requires the same parent read-perm the list/upload routes enforce.
**Actual:** `/download` requires only `sys.attachment.read`; a SALES_STAFF reads every expense-claim receipt, bank statement, and vendor-invoice attachment in co5 despite being denied the list view.

---

### F-C2-3 · INFO · No server-side logout / token revocation (stateless JWT)
No `/auth/logout` or revoke endpoint exists (`POST /auth/logout` → 404); AuthEndpoints exposes only login/switch-company/refresh. Logout is the BFF clearing its httpOnly cookie; the signed JWT stays valid until expiry, so a captured token survives "logout." Mitigated by httpOnly storage (browser JS can't read it), short token lifetime, and the absolute session cap on `/refresh`. Standard stateless-JWT tradeoff — noted, not a defect.

---

## Sub-areas that PASSED (probed, held)
- **Company-claim tamper:** `POST /auth/switch-company/{id}` is super-admin-only (policy `Master.CompanyManage` + explicit `IsSuperAdmin` handler check). admin01/chief01/sales01 → **403**.
- **RBAC admin cross-tenant param:** admin01 holds RoleManage+UserManage but `GET /admin/rbac/roles?companyId=2` and `?users?companyId=2` → **403 `rbac.cross_company.scope_required` "You may only manage your own company."** Own-company (no param) → 200, co5 users only. tax01/chief01 (no UserManage) → 403.
- **/companies:** enumeration + `GET /companies/{2,3}` → **403** for all co5 users (RBAC-gated despite master.companies carrying no RLS policy). No metadata leak.
- **api-keys:** admin01 list is co5-scoped (DB RLS on `sys.api_keys` + TenantMiddleware `app.company_id`); the "c3-*"-named keys are co5's own (sibling "army" swarm labels), not a leak. Prefixes shown are lookup ids, not secrets. chief01 (no ApiKeyManage) → 403.
- **Reports:** trial-balance / balance-sheet / profit-loss / sales-summary / tax-summary take **no `companyId` param** — company is server-derived from the JWT. `businessUnitId` is the only user-supplied scope knob; a foreign BU id (`profit-loss?businessUnitId=1|2`) returns 200 with `groups:[]` (co5 GL filtered to nothing) — cannot cross tenants.
- **Unauthenticated:** `/me`, `/reports/trial-balance` with no cookie → **401**.
- **audit01 (AUDITOR):** every direct AND indirect write route → **403**. No side-route reaches any write.
