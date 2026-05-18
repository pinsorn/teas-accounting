# Answer-Sana-Backend8 — Sprint 7-half: Purchase RBAC seed pass

**Date:** 2026-05-16 (post Sprint 6 wrap)
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** [Report-Backend8.md](./Report-Backend8.md) — KI-01 flagged in §8 "Flagged" block
**Gate:** **Sprint 7-half kicked off — small, surgical. Build it, no spec iteration.**

> Sprint 6 ✅ all gates green, 17 gotchas logged, mirror synced. Good work.
> Per §8: "Purchase RBAC seed gap (pre-existing): 110 ไม่เคย insert
> purchase.payment_voucher.{create,post,read} perms/grants" — confirmed by inspection.
> This sprint resolves KI-01 (now registered in `plan.md` §23.1). Single SQL script
> + 1 e2e. Estimated 1-2 days. **Do not improvise scope beyond this.**

---

## 1. Problem (confirmed by file inspection — no spec ambiguity)

| File | Status |
|---|---|
| `Accounting.Api/Authorization/Permissions.cs:47-50` | `PaymentVoucherCreate/Approve/Post/Read` constants exist (`purchase.payment_voucher.*`). |
| `Accounting.Api/Endpoints/PaymentVoucherEndpoints.cs:26,34,39,43,47` | Endpoints reference these via `[RequireAuthorization(...)]`. |
| `Migrations/SqlScripts/110_seed_roles_and_permissions.sql` | Inserts **14 perms**. None are purchase.*. |
| `Migrations/SqlScripts/140_seed_vendor_invoice_prefix_and_pv_approve.sql:12-17` | Adds `purchase.vendor_invoice.{create,post,read}` + `purchase.payment_voucher.approve`. **Does NOT add `payment_voucher.{create,post,read}`.** |

**Effect:** any non-super user calling `POST /payment-vouchers`, `POST /payment-vouchers/{id}/post`, `GET /payment-vouchers` gets 403 because the perm row literally doesn't exist in `sys.permissions`. Even SUPER_ADMIN: 110 cross-joined existing perms at apply time → these three perms were never in the cross-join because they didn't exist yet, and no later script re-grants them to super-admin.

In tests this is masked because the test infra likely bypasses `[Authorize]` or runs with a synthetic super-admin claim that short-circuits before the perm check.

---

## 2. Fix — ONE new idempotent SQL script

**File:** `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/180_seed_pv_purchase_perms.sql`

```sql
-- Sprint 7-half seed: missing purchase.payment_voucher.{create,post,read} perms.
-- KI-01 fix (plan.md §23.1). Idempotent (ON CONFLICT). Mirrors 140 pattern.
-- Keep in sync with Accounting.Api.Authorization.Permissions.Purchase.

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('purchase.payment_voucher.create', 'purchase', 'payment_voucher', 'create', 'Create Payment Voucher'),
    ('purchase.payment_voucher.post',   'purchase', 'payment_voucher', 'post',   'Post Payment Voucher'),
    ('purchase.payment_voucher.read',   'purchase', 'payment_voucher', 'read',   'View Payment Voucher')
ON CONFLICT (permission_code) DO NOTHING;

-- Grants. Match the VI-create/post/read role set from 140 for symmetry:
-- create/post/read → SUPER_ADMIN + COMPANY_ADMIN + CHIEF_ACCOUNTANT + ACCOUNTANT + AP_CLERK
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'purchase.payment_voucher.create',
    'purchase.payment_voucher.post',
    'purchase.payment_voucher.read')
WHERE r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AP_CLERK')
ON CONFLICT DO NOTHING;
```

**No** new C# code. The perms/constants/endpoints all already exist. This is a pure data-seed gap.

---

## 3. One e2e test — proves the unlock

**File:** `frontend/e2e/payment-voucher-non-super-rbac.spec.ts` (new)

**Setup:** existing `approver` seed user (from 160) has APPROVER role. Add or reuse an `ap_clerk` seed user with AP_CLERK role. (If 160 doesn't seed an AP clerk, add a single INSERT in the same 180 script — small, idempotent, document it inline.)

**Flow:**
1. Log in as **ap_clerk** (NOT super-admin).
2. Create a PV draft via `POST /payment-vouchers` → expect **201** (previously 403).
3. Log in as **approver** (different user, SoD).
4. Approve the PV via `POST /payment-vouchers/{id}/approve` → expect **200**.
5. Log back in as **ap_clerk**.
6. Post the PV via `POST /payment-vouchers/{id}/post` → expect **200** (previously 403).
7. GET `/payment-vouchers/{id}` → expect **200** with `status: "Posted"`.

**Negative control (same spec, second test):**
1. Log in as **sales_staff** (no purchase perms).
2. Attempt `GET /payment-vouchers/1` → expect **403**.

That's it. ~80 lines of Playwright.

---

## 4. Verification gates (non-negotiable per CLAUDE.md §8)

| Gate | Expectation |
|---|---|
| Backend build | 0/0 warnings/errors |
| Tests | All existing pass (Api 27/27 + Domain 32/32), no regression |
| tsc | 0 |
| next build | 0 (route count unchanged — no new UI in this sprint) |
| Playwright | 11 existing + 2 new = **13/13** via system Edge |
| DbInitializer fresh DB | Apply 180 script idempotently; verify perm count in `sys.permissions` increased by 3; no errors on second run |
| Re-apply 180 | Second run is a no-op (ON CONFLICT NOTHING) — verify via `SELECT COUNT(*) FROM sys.permissions WHERE permission_code LIKE 'purchase.payment_voucher.%'` = 4 (the original `approve` from 140 + 3 new) |

---

## 5. Scope cuts — explicitly NOT in this sprint

- ❌ **UI changes** — no new screens, no role-management UI. Pure seed.
- ❌ **Refactoring** existing seed scripts — 110 stays 14 perms, 140 stays as-is, 180 is additive.
- ❌ **Adding new permissions** beyond the three flagged (create/post/read).
- ❌ **Broader RBAC pass** for Sales/GL/Receipt — those work today; only PV is broken.
- ❌ **Permission management endpoints** (`PUT /roles/{id}/permissions` etc.) — future sprint.

If any of these emerge as blockers during build, **STOP and flag** per CLAUDE.md §8. Do not improvise.

---

## 6. Definition of done

1. `180_seed_pv_purchase_perms.sql` created, applied idempotently by DbInitializer.
2. Optional `ap_clerk` user seed in 180 (or reuse if already present in 160 — check first).
3. New Playwright spec `payment-voucher-non-super-rbac.spec.ts` (2 tests: happy path + negative).
4. All gates green (table in §4).
5. Mirror synced to `Y:\AccountApp\backend` (robocopy).
6. **Update `plan.md` §23.1** — strike through KI-01 entry with "✅ resolved Sprint 7-half".
7. Write `Report-Backend9.md` per the existing template (gates table, bugs caught, time taken).

---

## 7. After this sprint

Next in queue (decided 2026-05-16):
- **Sprint 8 Business Units** — design approved, Sana writing spec next.
- Sprint 7 File Attachment — decision captured, spec deferred until BU lands (BU may want to tag attachments too).

---

**Build it. ~1-2 days. Report back via Report-Backend9.**
