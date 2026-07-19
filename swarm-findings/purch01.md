# UX Swarm findings — purch01 (Purchasing Staff)

Target: https://teas.kazaki-rio.com (prod), company co5. Generated 2026-07-19T10:55:37.024Z.

## Done
- Login as purch01 succeeded (attempt 1).
- /me/permissions: isSuperAdmin=false, roles=PURCHASING_STAFF, permCount=5.
  Exact grant set: master.product.read, master.vendor.manage,
  purchase.purchase_order.create, purchase.purchase_order.read, sys.attachment.read.
  (No vendor-invoice/payment-voucher/report/user perms at all.)
- PO #1 created via UI, id=7, url=https://teas.kazaki-rio.com/purchase-orders/7
- PO #1 docNo=(none yet — likely pre-approval draft)
- PO #1 status text on detail page: ร่าง
- mark-sent button not visible/enabled on PO #1 (expected if not yet Approved).
- close button not visible/enabled on PO #1.
- PO #2 created via UI, id=8, url=https://teas.kazaki-rio.com/purchase-orders/8
- No company-switcher button found for purch01 (expected — single-company scope).
- PO #1 id: 7
- PO #2 id: 8
- Limitation (script, not app): the "รายการ" line field is a combined free-text
  description input with a product-picker icon (magnifying glass) next to it;
  the script typed "P001 swarm PO test qty7" as plain text rather than driving
  the picker to link the actual P001 SKU. Both POs carry qty 7 as requested but
  are NOT product-linked lines — a real user would click the picker icon to
  attach P001 properly. Noted for consolidation; not itself a product defect.

## Findings

Note: the first-pass script flagged /vendor-invoices/new and /payment-vouchers/new
as CRIT "5xx/crash" via a crude regex (`/5\d\d/`) that false-positived on ordinary
3-digit amounts/dates in the rendered form (e.g. "50ทวิ", VAT "7%..."). A follow-up
API-level probe (swarm-purch01-probe2.mjs, same session, same rules) disproved the
crash and surfaced the REAL finding below — corrected table:

| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| MED | RBAC UI gating — silent-403 pattern (VI/PV/reports) | purch01 (PURCHASING_STAFF; grants: master.product.read, master.vendor.manage, purchase.purchase_order.{create,read}, sys.attachment.read — no VI/PV/report/user perms) can navigate straight to `/vendor-invoices/new`, `/payment-vouchers/new`, `/vendor-invoices`, `/payment-vouchers`, `/reports/trial-balance`, `/reports/profit-loss` via typed URL. Every page renders its FULL chrome + interactive form/filters (not blocked, not redirected) while the underlying data call 403s silently — confirmed via network capture: `GET /api/proxy/expense-categories`→403, `GET /api/proxy/vendor-invoices?limit=100`→403, `GET /api/proxy/payment-vouchers?limit=100`→403, `GET /api/proxy/reports/trial-balance`→403, `GET /api/proxy/reports/profit-loss`→403, and direct `POST /api/proxy/vendor-invoices/`→403 / `POST /api/proxy/payment-vouchers/`→403. Net effect: create-forms look fully fillable-out (vendor picker, line items, live PDF preview all interactive) and list/report pages render as if legitimately empty ("ไม่มีข้อมูล" / blank table) — a user has no way to tell "no data" from "no permission" until they try to submit and hit an unstyled failure. Contrast with `/settings/users`, which does this correctly (see Denied-as-expected). | Login purch01 → paste any of the 6 URLs above directly into the address bar | swarm-findings/shots/purch01-08-probe-vi-new.png, purch01-09-probe-pv-new.png, purch01-10-probe-reports-tb.png, purch01-11-probe-reports-pl.png, purch01-13-probe-vi-list.png, purch01-14-probe-pv-list.png |

## Denied-as-expected
- purch01 self-approve PO #7 correctly denied: HTTP 403 (SoD holds — PURCHASING_STAFF
  cannot approve its own PO despite having purchase.purchase_order.create).
- Probe `/settings/users`: **best-practice clean deny** — full-page notice, shield icon,
  Thai copy "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้ (sys.user.manage) —
  กรุณาติดต่อผู้ดูแลระบบ", zero API calls fired (client-side route guard blocks before
  any fetch). This is the pattern the VI/PV/reports pages above should copy —
  screenshot swarm-findings/shots/purch01-12-probe-users.png.
- Direct API probe `GET /api/proxy/users` → 404 (route doesn't exist under that path;
  not a real finding, just confirms the FE never calls it for this role either).

## Console errors captured
- [console] https://teas.kazaki-rio.com/ :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/purchase-orders/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/purchase-orders/7 :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/vendor-invoices/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/payment-vouchers/new :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/reports/trial-balance :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/reports/profit-loss :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/settings/users :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/vendor-invoices :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/payment-vouchers :: Failed to load resource: the server responded with a status of 403 ()
- [console] https://teas.kazaki-rio.com/dashboard :: Failed to load resource: the server responded with a status of 404 ()
