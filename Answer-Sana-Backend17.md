# Answer-Sana-Backend17 — Sprint 12: Internal Purchase Order

**Date:** 2026-05-18
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Internal-only PO (approval + traceability, no external workflow), final Phase-1 backbone sprint
**Gate:** **Focused sprint ~5-6 days. Single phase OK (small surface).**

> Sprint 12 = **last Phase-1 backbone sprint**. After this ships → only Sprint 13b
> (User Manual generator) + e-Tax wiring + go-live checklist remain. Phase 1
> production-ready in sight.

---

## 0. Pre-spec audit (Sana — emergent discipline)

| Check | Result | Sprint 12 impact |
|---|---|---|
| `PurchaseOrder` entity in Domain | ❌ doesn't exist (verified by grep) | Build from scratch |
| `purchase_order_id` column on `vendor_invoices` | ❌ doesn't exist (verified — no scaffold) | Add column + FK in this sprint's migration |
| `PURCHASE_ORDER` enum value in `sys.attachments.parent_type` | ✅ exists (Sprint 11 forward-compat) | Use it for PO attachments |
| `PURCHASE_ORDER` category enum in attachments | ✅ exists | Use for VI cross-ref attachments |
| Numbering — `PO` doc_type prefix | Check if pre-seeded; if not, add via seed | Verify P1 |
| `purchase.payment_voucher.*` perms pattern (mirror) | ✅ exists from Sprint 7-half | Mirror pattern for PO perms |
| `ck_pv_sod` CHECK pattern (mirror SoD) | ✅ exists from Sprint 5.5 B2 | Mirror for `ck_po_sod` |
| SaaS approval workflow pattern (Approved/Posted) | ✅ exists (PV B2) | Mirror exact pattern |

**Outcome:** Sprint 12 has **strong scaffold reuse** — SoD approval pattern, attachment system, perm pattern all proven. Greenfield for entity + minor surgical addition to VI (FK). No drift hazard surfaced.

---

## 1. Concept (Internal PO — not external workflow)

Per Ham consultation (2026-05-17):
- PO is **internal approval + traceability**, NOT external commitment
- Many vendors don't require PO → we use it for **spending control** anyway
- "ส่ง/ไม่ส่ง vendor" = optional action, not workflow gate
- Status flow simpler than external PO: **Draft → Approved → Closed** (no Sent/Confirmed/Received steps)
- VI links back to PO **retroactively** when invoice arrives (loose matching, ≤105% tolerance)
- Auto-close PO when linked VI total ≥ PO total
- "Outstanding PO" report = approved PO without VI link

---

## 2. Schema

```
purchase.purchase_orders
  purchase_order_id    BIGINT IDENTITY PK
  company_id, branch_id (RLS via app.company_id)

  doc_no               VARCHAR NULL          -- PO-NNNN, allocated on POST
  status               ENUM     Draft | Approved | Closed | Cancelled
  doc_date             DATE NN               -- Asia/Bangkok today
  expected_delivery_date  DATE NULL          -- informational; surfaces in Outstanding report aging

  -- Vendor snapshot (frozen pattern from Sprint 5.5 VI)
  vendor_id            BIGINT NN
  vendor_name          VARCHAR NN
  vendor_address       VARCHAR NULL
  vendor_tax_id        VARCHAR NULL
  vendor_type          INT NN                -- enum from existing CustomerType-like

  -- Optional BU tagging (Sprint 8 cascade)
  business_unit_id     INT NULL FK master.business_units

  -- Amounts (informational; VI may differ within tolerance)
  currency_code        VARCHAR(3) DEFAULT 'THB'
  exchange_rate        NUMERIC(19,6) DEFAULT 1
  subtotal_amount      NUMERIC(19,4) NN
  vat_amount           NUMERIC(19,4) NN
  total_amount         NUMERIC(19,4) NN
  total_amount_thb     NUMERIC(19,4) NN

  notes                VARCHAR NULL
  internal_notes       VARCHAR NULL          -- not shown on PDF, internal context

  -- SoD approval (mirror PV B2)
  created_at, created_by                    -- IAuditable
  approved_at          TIMESTAMPTZ NULL
  approved_by          BIGINT NULL
  -- DB CHECK ck_po_sod: (approved_by IS NULL) OR (approved_by <> created_by)

  -- Optional external action timestamps
  sent_to_vendor_at    TIMESTAMPTZ NULL      -- when user clicked "ส่ง PO ให้ vendor" (info only)
  closed_at            TIMESTAMPTZ NULL
  cancelled_at         TIMESTAMPTZ NULL
  cancellation_reason  VARCHAR NULL

  -- Audit
  updated_at, updated_by, version

  ITenantOwned, IAuditable, IConcurrencyVersioned

purchase.purchase_order_lines
  line_id              BIGINT IDENTITY PK
  purchase_order_id    BIGINT NN FK
  line_no              INT NN

  product_id           BIGINT NULL FK master.products      -- Sprint 10 product master
  product_code         VARCHAR NULL                          -- snapshot fallback
  description_th       VARCHAR NN

  quantity             NUMERIC(19,4) NN
  uom_text             VARCHAR(50) NULL
  unit_price           NUMERIC(19,4) NN
  line_amount          NUMERIC(19,4) NN

  tax_code_id          INT NULL
  tax_code             VARCHAR NULL
  tax_rate             NUMERIC NN DEFAULT 0
  tax_amount           NUMERIC(19,4) NN DEFAULT 0
  total_amount         NUMERIC(19,4) NN

  notes                VARCHAR NULL
```

### Add to `purchase.vendor_invoices`:

```
ALTER purchase.vendor_invoices ADD:
  purchase_order_id  BIGINT NULL FK purchase.purchase_orders
                                       -- retroactive link when VI received
```

(Nullable — PO link is optional, many VIs come without PO reference.)

### Numbering

`PO-NNNN` per `(company_id, doc_type='PO', sub_prefix=BU_code|NULL, year_month)`. Existing `sys.number_sequences` infrastructure. Pre-seed verify in P1 (check if PO prefix exists in `sys.document_prefixes` — if not, add via seed).

---

## 3. Service layer

### IPurchaseOrderService

```csharp
Task<long> CreateDraftAsync(CreatePurchaseOrderRequest req, CancellationToken ct);
Task UpdateDraftAsync(long id, CreatePurchaseOrderRequest req, CancellationToken ct);
Task<PurchaseOrderApprovedResult> ApproveAsync(long id, CancellationToken ct);     // SoD check
Task<PurchaseOrderPostedResult>   PostAsync(long id, CancellationToken ct);         // allocates doc_no + freezes
Task MarkSentAsync(long id, CancellationToken ct);                                  // optional, sets sent_to_vendor_at
Task CancelAsync(long id, string reason, CancellationToken ct);

Task<CursorPage<PurchaseOrderListItem>> ListAsync(long? cursor, int limit, ..., CancellationToken ct);
Task<PurchaseOrderDetail?> GetDetailAsync(long id, CancellationToken ct);
Task<byte[]> BuildPdfAsync(long id, CancellationToken ct);
```

**ApproveAsync logic:**
1. Verify PO status = Draft
2. Verify caller `!= po.CreatedBy` (SoD service-layer check)
3. Set `approved_by = caller`, `approved_at = now`
4. (No status transition yet — Approval is a state of Draft + approved_at set; POST is what allocates doc_no and locks)

Actually let me simplify — make Approval = state machine: Draft → Approved → Closed.

**Refined state machine:**
- `Draft`: editable, no doc_no
- `Approved`: doc_no allocated, fields frozen except `sent_to_vendor_at` + `closed_at` + `cancellation_reason`
- `Closed`: terminal (auto-close when VI total ≥ PO total, OR manual close by user)
- `Cancelled`: terminal (with reason)

Transitions:
- `Draft → Approved`: ApproveAsync (SoD check: caller ≠ creator; DB CHECK ck_po_sod)
- `Approved → Closed`: auto when VI total ≥ PO total in linked VI's POST, OR manual via CloseAsync
- `Draft → Cancelled` or `Approved → Cancelled`: via CancelAsync with reason
- POSTED VI cannot break: closed PO stays closed; cancelled PO cannot be linked from new VI

### Auto-close logic in VendorInvoiceService.PostAsync

When posting a VI with `purchase_order_id` set:
1. Look up linked PO
2. Verify PO status in (Approved, Closed) — reject if Draft/Cancelled
3. Compute "PO settlement": sum of all VIs linked to this PO
4. If `sum_vi_total >= po.total_amount * 0.95` (95% threshold) → auto-close PO (set status=Closed, closed_at)
5. If `sum_vi_total > po.total_amount * 1.05` (105% tolerance) → **warn** (HTTP 200 with warning chip in response, not error) — over-receipt
6. Out-of-band manual close via PO detail "Close PO" button

### Outstanding PO report

```
GET /reports/outstanding-po?as_of=YYYY-MM-DD&vendor_id=...&overdue_only=false
```

Returns Approved PO with no/partial VI link, aging by `expected_delivery_date`:

```json
{
  "as_of": "2026-05-18",
  "rows": [
    {
      "po_id": 1, "doc_no": "PO-2026-0001",
      "vendor_name": "Acme Vendor",
      "expected_delivery_date": "2026-05-10",
      "days_overdue": 8,
      "po_total": 50000,
      "linked_vi_count": 1, "linked_vi_total": 35000,
      "remaining": 15000
    },
    ...
  ]
}
```

Aging buckets: Current / 1-7 / 8-14 / 15-30 / 30+ days overdue.

---

## 4. Permissions

```
purchase.purchase_order.create   — create + edit Draft + cancel own
purchase.purchase_order.approve  — Draft → Approved (SoD; cannot approve own)
purchase.purchase_order.read     — view + download PDF + list
purchase.purchase_order.cancel   — Approved → Cancelled (different from create scope — bigger consequence)
```

Default grants:
- SUPER_ADMIN: all 4
- COMPANY_ADMIN: all 4
- CHIEF_ACCOUNTANT: approve + read + cancel
- ACCOUNTANT + PURCHASING_STAFF + AP_CLERK: create + read
- APPROVER role: approve + read

Add to seed script (next available number — likely 290 or 300 per existing convention).

---

## 5. Endpoints

```
POST   /purchase-orders                         create Draft
PUT    /purchase-orders/{id}                    update Draft (rejects if Approved+)
POST   /purchase-orders/{id}/approve            Draft → Approved (SoD)
POST   /purchase-orders/{id}/mark-sent          optional, sets sent_to_vendor_at
POST   /purchase-orders/{id}/close              manual close (Approved → Closed)
POST   /purchase-orders/{id}/cancel             with reason
GET    /purchase-orders                         list (cursor + filter status/vendor/BU/date)
GET    /purchase-orders/{id}                    detail
GET    /purchase-orders/{id}/pdf                PDF stream
GET    /reports/outstanding-po                  Outstanding PO report
```

`POST /vendor-invoices` (existing) extended to accept `purchase_order_id` (nullable):
- if set → validate PO status, snapshot for traceability, auto-close on POST per §3 logic
- if null → existing flow unchanged

---

## 6. UI

### New pages
- `/purchase-orders` — list (status filter, vendor filter, BU filter, date range, "Outstanding only" toggle)
- `/purchase-orders/new` — form: vendor + BU + lines (product-aware auto-pickup from Sprint 10 Product master) + expected_delivery_date
- `/purchase-orders/{id}` — detail:
  - Status chip + workflow buttons (Approve/Cancel/Close based on status + perm)
  - "ส่ง PO ให้ vendor" button (sets sent_to_vendor_at, optional)
  - Linked VIs section (if any) with totals + remaining
  - Attachment section (PO copy, contract, vendor confirmation reply — Sprint 11 polymorphic)
  - PDF download button

### Modified pages
- `/vendor-invoices/new` — add "Link to PO?" dropdown (optional, shows Approved PO of selected vendor) → auto-fill VI lines from PO with user-editable override
- `/vendor-invoices/{id}` — show linked PO badge with link

### New report page
- `/reports/outstanding-po` — table with aging buckets, vendor filter, overdue chip styling

### Sidebar nav
- Add "Purchase Orders" under "การซื้อ" section (between PaymentVoucher and Reports)

### i18n keys

```
purchaseOrder.title              "ใบสั่งซื้อ"
purchaseOrder.docNo              "เลขที่"
purchaseOrder.status             "สถานะ"
purchaseOrder.expectedDelivery   "วันที่คาดว่าจะส่ง"
purchaseOrder.sentToVendor       "ส่ง PO ให้ vendor"
purchaseOrder.approve            "อนุมัติ"
purchaseOrder.close              "ปิด"
purchaseOrder.cancel             "ยกเลิก"
purchaseOrder.linkedTo           "VI ที่เชื่อม"
purchaseOrder.remaining          "ค้างเหลือ"
purchaseOrder.outstandingReport  "PO ค้าง"
purchaseOrder.overdueDays        "วันที่เลย"
```

---

## 7. Tests

### Unit
- `PurchaseOrderStateMachineTests` — Draft → Approved → Closed transitions; reject invalid
- `PurchaseOrderSoDTests` — approver ≠ creator (service-layer + DB CHECK)
- `PoVi MatchingTests` — auto-close at 95%, warn at 105%, error semantics

### Integration
- POST PO + approve (different user) → status Approved + doc_no allocated
- POST PO + approve (same user) → 403 SoD violation
- DB direct INSERT violating ck_po_sod → CHECK constraint blocks
- Cancel PO with reason → status Cancelled + reason stored
- VI POST with PO link → auto-close at threshold
- VI POST with PO link exceeding tolerance → warning in response, NOT error
- VI POST with link to Cancelled PO → reject 409
- Outstanding PO report — only Approved without full VI link, aging correct
- Cross-tenant: cannot link to other tenant's PO

### e2e Playwright (×1 new)
- `purchase-order-flow.spec.ts`:
  1. Login as accountant → create PO Draft
  2. Login as approver → approve PO → doc_no allocated
  3. Login as accountant → "ส่ง PO" → sent_to_vendor_at set
  4. Login as ap_clerk → create VI linked to PO → POST → PO auto-closes
  5. Check /reports/outstanding-po → PO no longer appears

Total: 28 prior + 1 new = **29/29**.

---

## 8. Scope cuts — explicitly OUT

- ❌ **Vendor confirmation workflow** (PO Sent → Confirmed by vendor) — internal-only design
- ❌ **3-way match (PR → PO → GR)** — Phase 2 enterprise expansion
- ❌ **Partial Goods Receipt (GR)** — not in scope; loose VI matching with ≤105% tolerance suffices
- ❌ **PO amendments (PO-001-v2)** — Cancel + recreate this sprint; versioning Phase 2
- ❌ **Email PO directly to vendor from system** — user downloads PDF + emails manually; integration Phase 2
- ❌ **Catalog / vendor price lists** — Phase 2
- ❌ **Auto-numbering reservation before POST** — same as TI: allocated on POST only (gapless)
- ❌ **Approval workflows with multiple approvers** — single approver (SoD) only

If any block → escalate per §8.

---

## 9. Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (state machine, SoD, matching math) |
| Api tests | +N (CRUD + approve + close + cancel + VI linking + auto-close + outstanding report) |
| EF migration | `AddInternalPurchaseOrder` clean (1 migration: 2 new tables + VI.purchase_order_id FK + ck_po_sod + perms seed) |
| tsc / next build | 0 / 0 (+3 routes: list, new, detail; +1 report route) |
| Playwright | 28 + 1 = **29/29** |
| SoD verification | Direct DB INSERT violating ck_po_sod → CHECK constraint blocks |
| Outstanding aging | Integration test: PO past expected_delivery + no VI → appears in correct aging bucket |

---

## 10. DoD

1. PurchaseOrder + Line entity + EF config + migration
2. ck_po_sod DB CHECK
3. VI.purchase_order_id FK
4. IPurchaseOrderService + state machine + SoD enforcement
5. VendorInvoiceService.PostAsync auto-close logic (95%) + tolerance warning (105%)
6. Numbering PO-NNNN with optional BU sub-prefix
7. 4 permissions + grants seed + ApproverRole grant
8. Endpoints (CRUD + approve/close/cancel/mark-sent + outstanding report)
9. PO PDF template (with optional WHT note like Quotation Sprint 10, if applicable)
10. UI: list + new + detail + outstanding report page
11. VI form enhancement: PO link dropdown + auto-fill on selection
12. AttachmentsSection on PO detail (reuse Sprint 11 component, parent_type=PURCHASE_ORDER)
13. i18n th + en
14. Tests (unit + integration + 1 e2e)
15. All gates green
16. Mirror sync `Y:\AccountApp`
17. plan.md §23.3 — strike Sprint 12
18. `Report-Backend17.md`

**Total: 18 DoD items.**

---

## 11. After this sprint

**Phase 1 backbone COMPLETE.** Remaining for Phase 1 production-ready:

| Item | Owner | Estimated |
|---|---|---|
| Sprint 13b — User Manual generator | Claude Code | ~8-12 days |
| e-Tax wiring (sign+send real-time + RD ack) | Claude Code | Phase 1 ปลาย task per plan §22 Phase 4 |
| External pen-test | External vendor | 5-10 days |
| Go-live checklist (ch.09) walkthrough | Ham + Sana | 1-2 days |
| First customer onboarding + migration test | Sana + customer | per ch.08 |

Phase 1 production-ready ETA: **end of this week** at current Claude Code burn rate.

---

**Build it. Single phase, ~5-6 days. Report back via Report-Backend17.**
