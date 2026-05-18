# Report-Backend17 — Sprint 12 wrap: Internal Purchase Order

**Date:** 2026-05-18
**Spec:** Answer-Sana-Backend17.md
**Status:** ✅ COMPLETE — 18/18 DoD, all gates green, plan.md §23.10 + forward block struck.
**Estimate vs actual:** spec'd ~5-6 days, single phase. Delivered in one
session. The SoD pattern, numbering service, attachment section and BFF proxy
were all already in place from prior sprints — the bulk was the PO aggregate,
the pure auto-close math, and the VI-link wiring.

> **THE LAST Phase-1 backbone sprint.** With this shipped, the Phase-1
> backbone is complete.

---

## 1. What shipped (single phase)

| Area | Delivered |
|---|---|
| Schema | `purchase.purchase_orders` + `purchase_order_lines` — status (Draft/Approved/Closed/Cancelled), vendor snapshot, `business_unit_id`, amounts, `expected_delivery_date`, `sent_to_vendor_at`, `cancellation_reason`, `IAuditable`+`IConcurrencyVersioned`. `ck_po_sod` CHECK (`approved_by IS NULL OR approved_by <> created_by` — byte-mirror of `ck_pv_sod`). Filtered unique `doc_no` index. `vendor_invoices.purchase_order_id` nullable FK (Restrict) + index. Migration `AddInternalPurchaseOrder`. |
| Domain | `PurchaseOrderStatus` enum; `PurchaseOrder.MarkApproved` (Draft guard, SoD `CreatedBy==approver → po.sod_violation`, sets DocNo/ApprovedBy/At/Status), `MarkClosed` (Approved-only → `po.not_approved`), `MarkCancelled` (blocks Closed/Cancelled → `po.terminal`). `PoSettlement.Evaluate(linkedViTotal, poTotal) → (ShouldClose, OverReceipt)` — pure; CloseThreshold 0.95, OverReceiptTolerance 1.05, poTotal≤0 → no-op. |
| Service | `IPurchaseOrderService` — CreateDraft/UpdateDraft/Approve/MarkSent/Close/Cancel/List/GetDetail/BuildPdf (QuestPDF)/Outstanding. `PO-NNNN` via `INumberSequenceService` +BU sub-prefix allocated **on approve only** (gapless, never on draft save). Outstanding report: Approved POs, aging buckets Current / 1-7 / 8-14 / 15-30 / 30+. |
| VI link | `VendorInvoiceService.PostAsync` — after GL post, before tx commit, if `PurchaseOrderId` set: reject Draft/Cancelled PO (`vi.po_link_invalid`), sum Posted linked VIs, `PoSettlement.Evaluate` → auto `MarkClosed` at ≥95%, `PoOverReceiptWarning` chip (**HTTP 200**, not an error) at >105%. `CreateVendorInvoiceRequest` +`PurchaseOrderId`; `VendorInvoiceDetail` +`PurchaseOrderId`/`PurchaseOrderDocNo`. |
| Endpoints | `/purchase-orders` POST/PUT/list/`{id}` + `{id}/approve`·`mark-sent`·`close`·`cancel` + `{id}/pdf` + `/reports/outstanding-po`, all perm-gated; `MapPurchaseOrderEndpoints` wired in `Program.cs`. |
| Perms | `purchase.purchase_order.{create,approve,read,cancel}` + seed `290_seed_purchase_order.sql` (also adds the `PO` document prefix — NOT pre-seeded in 100; role grants mirror PV: read→all working roles, create→ADMIN/ACCOUNTANT/AP_CLERK, approve→ADMIN/CHIEF/APPROVER, cancel→ADMIN/CHIEF). |
| UI | 3 PO pages (list / new w/ VendorSelector / detail w/ status badge + approve/mark-sent/close/cancel + linked-VI list + PDF + `AttachmentsSection PURCHASE_ORDER`) + `/reports/outstanding-po` aging table. VI new-page optional "Link to PO" dropdown (Approved POs of the chosen vendor) + line auto-fill; VI-detail linked-PO badge + over-receipt toast. `purchaseOrder` + `vi.linkPo*`/`vi.linkedPo`/`vi.poOverReceipt` i18n th/en; sidebar + nav i18n th/en. |
| Tests | `PurchaseOrderStateMachineTests` ×5 (approve diff/same user SoD, non-draft approve, close-only-from-approved, cancel blocks terminal); `PoViMatchingTests` Theory ×4 (94/95/105/>105% thresholds); `Sprint12PurchaseOrderTests` ×5 (SoD same/diff, `ck_po_sod` raw-CHECK, cancel, outstanding `8-14` bucket, cross-tenant null); e2e `purchase-order-flow`. |

**Final gate:** build 0/0, no EF drift (`AddInternalPurchaseOrder`), Domain
**79/79**, Api **87/87** (+5, 0 skip/regr), tsc 0, next 0 (+3 PO routes +1
`/reports/outstanding-po`), **Playwright 29/29** (two-pass: 28 @
`Tax__VatMode=true` incl. new `purchase-order-flow`; 1 @ `false`), mirror synced.

---

## 2. Security / compliance highlights

- **SoD enforced twice:** entity `MarkApproved` throws `po.sod_violation` when
  `CreatedBy == approver`, AND the `ck_po_sod` DB CHECK
  (`approved_by IS NULL OR approved_by <> created_by`) — a byte-for-byte mirror
  of the proven `ck_pv_sod`. Raw-CHECK asserted in `Sprint12PurchaseOrderTests`.
- **Gapless numbering:** `PO-NNNN`+BU sub-prefix allocated only on approve via
  the shared `INumberSequenceService` — never on draft save; voided/cancelled
  POs keep their slot (status, not deletion).
- **Tenant isolation:** PO queries + the VI→PO link resolve under the global
  query filter — a tenant cannot link a VI to, or list, another tenant's PO
  (integration-tested: cross-tenant GetDetail → null).
- **Auto-close is not destructive:** ≥95% closes the PO (terminal but
  non-deleting); >105% raises a visible chip on an otherwise-successful
  (HTTP 200) VI post — the accountant is informed, nothing is blocked or lost.
- **Posted-document immutability untouched:** the VI link is read at VI Post
  time and only transitions the PO; no posted document is mutated.

---

## 3. Mechanism notes / premise resolutions (flagged, not improvised)

1. **`PO` document prefix was NOT pre-seeded** in `100`
   (`100_seed_document_prefixes.sql`). QT/SO/DO were inserted as Sprint-1
   forward scaffold; `PO` was not. Added idempotently in seed 290
   (`ON CONFLICT (prefix_code) DO NOTHING`). Escalated as a mechanism note —
   not a silent workaround; same disciplined class as prior premise flags.
2. **`PURCHASING_STAFF` role absent** from the seeded role set. Per the
   Sprint-7½ KI-01 purchase-RBAC convention, `AP_CLERK` is the create-side
   purchasing analog (create → ADMIN/ACCOUNTANT/AP_CLERK). Documented in
   seed 290's header.
3. **`PoSettlement` extracted as a pure Domain type** so the
   auto-close / over-receipt math is unit-testable at the exact 94/95/105/>105%
   boundaries without standing up a full GL fixture. The VI-link end-to-end
   path (real GL post → auto-close → outstanding drop) is proven by the
   `purchase-order-flow` e2e against the real `teas_app` DbInitializer DB.
4. **`ck_po_sod` raw-CHECK test sets `ApprovedBy` = the tenant `userId`** — the
   `IAuditable` SaveChanges interceptor overwrites `CreatedBy` with
   `tenant.UserId`, so to drive `CreatedBy == ApprovedBy` at the DB layer the
   test provisions `Provider(userId:5)` + `ApprovedBy=5`. (The entity-guard
   path is covered separately by the state-machine tests.)
5. **Perm-code strings are literals in `PurchaseOrderService`** — the Api
   `Permissions` class is unreachable from Infrastructure (same constraint as
   the Sprint-11 `AttachmentService` / TaxConfig split). Strings match seed 290.
6. **Scope cuts honored (Answer-Sana-Backend17):** no vendor confirmation
   workflow, no 3-way match, no partial goods receipt, no PO amendments
   (cancel + recreate is the path), no email-PO-to-vendor, no catalog / price
   lists, no multiple approvers — all Phase-2 / explicitly out of scope, none
   improvised in.

---

## 4. Bugs caught & fixed by the gates (honest)

- Long session path → `dotnet test` `Win32Exception (87)` starting the test
  host → `subst U:` short-path (carried-forward env recipe; not a code bug).
- `pnpm` absent from PATH in both Bash and PowerShell → drove the frontend via
  `corepack pnpm` / raw `node .\node_modules\…` (recipe-consistent).
- (Pre-compaction, carried) CS0023 lambda `.Should()` in
  `PurchaseOrderStateMachineTests` → explicit `Action` locals; `ck_po_sod`
  test `ApprovedBy` aligned to the interceptor-set `CreatedBy` (note 3.4).

---

## 5. DoD — 18/18

1 `PurchaseOrderStatus` enum · 2 `PurchaseOrder`+`PurchaseOrderLine` entities ·
3 state machine (MarkApproved/Closed/Cancelled + SoD) · 4 `ck_po_sod` DB CHECK ·
5 `vendor_invoices.purchase_order_id` FK · 6 `PoSettlement` auto-close/
over-receipt math · 7 `IPurchaseOrderService` (CRUD + lifecycle + PDF) ·
8 `PO-NNNN`+BU numbering on approve · 9 VI Post auto-close + >105% chip ·
10 4 perms + seed 290 (incl. `PO` prefix) · 11 VI form PO-link dropdown +
auto-fill + VI-detail badge · 12 Outstanding-PO report w/ aging buckets ·
13 endpoints wired + perm-gated · 14 3 PO pages + report page + `AttachmentsSection` ·
15 i18n th/en + sidebar/nav · 16 tests (Domain state machine + PoSettlement
Theory + Api hardening + 1 e2e) · 17 all gates green + mirror · 18 plan §23.10
struck + this report.

**Sprint 12 closed. Phase-1 backbone complete.** Awaiting the Sprint 13 spec
(`Answer-Sana-Backend18.md`).
