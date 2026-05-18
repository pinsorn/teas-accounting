# Report-Backend6 — Sprint 5 Wrap (Purchase UI slice — partial; structural blockers flagged)

**Date:** 2026-05-16 · **Sprint:** 5 (Vendor / Payment-Voucher / 50 ทวิ UI)
**Author:** Claude Code · **Owner:** Ham (via Sana)
**Prev:** [Report-Backend5.md](./Report-Backend5.md) · [Answer-Sana-Backend5]
**Mid-sprint escalation:** [Question-Backend5.md](./Question-Backend5.md) — **needs `Answer-Backend5` (B1, B2) before Sprint 5 can fully close.**

---

## 1. Executive summary

Sprint-5's stated premise — *"backend exists since Sprint 2, just UI work"* — was
**partly wrong**. Backend verify (Sana's own instruction: *"if any missing → flag in
Question-Backend5"*) found two **structural / compliance** gaps. Per
flag-don't-improvise discipline I did **not** build them; I raised Question-Backend5
and **shipped the unblocked subset in parallel** so the sprint still progressed. The
shipped subset is 6/6 gate-green on the real stack.

| Gate | Result |
|---|---|
| Backend build | 0 / 0 |
| Backend tests | Domain 32/32 · Api 10/10 · **0 regression** (incl. `PaymentVoucher_with_wht…` hardening — directly exercises the PV/WHT path I made `partial`) |
| Frontend `tsc` | exit 0 |
| `next build` | exit 0 — 26 routes (**7 new**) |
| Playwright e2e | **6 / 6 PASS** via system Edge — existing 4 (**0 regression**) + `record-vendor` + 2 screenshot specs |
| Screenshots | 5 captured, `frontend/screenshots/s5-*.png` |

## 2. 🔴 Structural blockers flagged (Question-Backend5) — NOT improvised

- **B1 — VendorInvoice backend entirely absent.** `grep VendorInvoice backend/src`
  → 0 hits: no entity/DTO/service/EF-config/migration/endpoint. PV has no
  `vendor_invoice_id`. Sprint-5 `/vendor-invoices` + e2e `record-vendor-invoice` +
  PV-against-invoice (3-way match, plan §7.3) depend on it. Building it =
  schema-beyond-plan + GL posting (Dr Expense/Dr Input-VAT/Cr AP) + Input-VAT
  claim-period ม.82/4 → CLAUDE.md §9 "ASK before". **Paused.**
- **B2 — PV approval / SoD not implemented.** `IPaymentVoucherService` =
  `CreateDraftAsync` + `PostAsync` only; `PostAsync` calls `MarkPosted(... _tenant.UserId …)`
  with **no `created_by ≠ approved_by` check**; no `PaymentVoucherApprove` permission;
  no `ck_pv_sod`. CLAUDE.md §12.1 is a hard rule — today a creator can post their own
  PV. Introducing an approval state machine + DB CHECK is a compliance-control
  decision, not UX. **Paused.**

Question-Backend5 gives concrete A/B/C options for each (B1-A recommended: build to
plan §7.2/7.3/§17.3 with a model/GL spec sent for sign-off **before** the migration;
B2-A recommended: `Draft→Approved→Posted` + `ck_pv_sod`). The good news:
`PaymentVoucherService.PostAsync` itself is **correct** — allocates
`PV-{CAT}-NNNN`, issues **one 50 ทวิ per income type** (ม.50 ทวิ), GL-posts. The
engine is fine; the approval gate and the upstream invoice are what's missing.

## 3. What shipped (unblocked subset — read-only + master CRUD)

**Backend** (read surface only — my-owned pattern, mirrors Receipt/CN `.Read.cs`)
- `PurchaseReadDtos.cs`; `IPaymentVoucherService` + `PaymentVoucherService.Read.cs`
  → `GET /payment-vouchers` (cursor) `/{id}` `/{id}/pdf` (perm `PaymentVoucherRead`).
- `IWhtCertificateService` + `WhtCertificateService.cs` → `GET /wht-certificates`
  `/{id}` `/{id}/pdf` — **50 ทวิ QuestPDF** to plan §15.10 (payer/payee blocks,
  ภ.ง.ด. type, ม.40 income row, tax withheld). Perm `WhtRead`. `MapWhtCertificate
  Endpoints` + DI registered. **Read-only — never writes; cert issued by PV post.**
- Vendor: `GET /vendors/{id}` (new `VendorDetailDto` + `GetByIdAsync`); **fixed
  `GET /vendors` non-nullable `int page,pageSize` → `int?` + defaults** (proactively
  applied runtime-gotchas §2 — selector calls it param-less).

**Frontend** (DaisyUI `teas`, TanStack Query, next-intl th/en)
- Sidebar restructured into sections; new **"ซื้อ"** group (Vendors / Payment
  Vouchers / WHT Certs).
- `/vendors` list+search, `/vendors/new` (controlled form + `TaxIdInput` mod-11 +
  branch-code mask + locked defaults), `/vendors/[id]` detail.
- `/payment-vouchers` list + `/[id]` detail (lines, VAT/WHT, net, non-recoverable-VAT
  badge, PDF). **Read-only + a defer banner** — create/approve paused (B2).
- `/wht-certificates` list + `/[id]` detail (payer/payee, ม.40 row, PV back-link, 50
  ทวิ PDF).
- `VendorSelector` (async combobox, mirrors `CustomerSelector`, defensive shape).
- `ExpenseCategorySelector` (loads `/expense-categories`, shows `name (CODE)`,
  **⚠ "ภาษีซื้อต้องห้าม" ม.82/5** when non-recoverable, CapEx hint) — built as
  reusable infra per Sana even though its only consumer (PV create) is paused.
- `lib/types.ts` + `lib/queries.ts` Sprint-5 block; `messages/{th,en}.json`
  `nav.section.purchase` + `ven`/`pv`/`wht` namespaces.

## 4. Bugs caught by the gate

1. **`record-vendor` e2e** first failed: `getByRole('cell',{name:code})` matched **2**
   cells because the test vendor *name* embedded the *code* and the list shows both.
   The vendor **was** created & listed — app correct; assertion ambiguous
   (runtime-gotchas **§5** class). Fixed the test (`exact:true` + code-free name),
   re-ran → green. **New §5 sub-case for the doc — see §8.**
2. No app defects this sprint (read-only surface). The proactive gotcha-#2 nullable
   fix on `/vendors` was applied *before* it could 400 a real param-less call —
   verified by `record-vendor` (list renders with no page/pageSize sent).

## 5. Files

**Backend new:** `Application/Purchase/{PurchaseReadDtos,IWhtCertificateService}.cs`,
`Infrastructure/Purchase/{PaymentVoucherService.Read,WhtCertificateService}.cs`,
`Api/Endpoints/WhtCertificateEndpoints.cs`.
**Backend modified:** `IPaymentVoucherService.cs`, `PaymentVoucherService.cs`
(`sealed partial`), `Application/Master/VendorDtos.cs`,
`Infrastructure/Master/MasterDataServices.cs`, `Api/Endpoints/{PaymentVoucher,Master}Endpoints.cs`,
`Api/Program.cs`, `Infrastructure/DependencyInjection.cs`.
**Frontend new:** `components/ui/{VendorSelector,ExpenseCategorySelector}.tsx`,
`app/(dashboard)/{vendors/{page,new/page,[id]/page},payment-vouchers/{page,[id]/page},wht-certificates/{page,[id]/page}}.tsx`,
`e2e/{record-vendor,screenshots-sprint5}.spec.ts`.
**Frontend modified:** `components/app-shell/SidebarNav.tsx`, `lib/{types,queries}.ts`,
`messages/{th,en}.json`.

## 6. Screenshots (Answer-Sana §5.4)

`frontend/screenshots/`: `s5-01-vendors-list.png`, `s5-02-vendor-create.png`,
`s5-03-payment-vouchers.png`, `s5-04-wht-certificates.png`, `s5-05-vendors-mobile.png`.
Eyeballed 01+02: `teas` theme renders cleanly — new **"ซื้อ"** section header
(muted uppercase) + 3 items with icons, active state blue primary, Thai (Sarabun)
readable, 2-col form grid, disabled-until-valid save. **No visual clash to flag**
(per §5.4 I flag, not re-theme).

## 7. Honest gaps / flags

1. **Sprint 5 is partial by design.** `/vendor-invoices`, PV create/approve UI, and
   the 2 e2e Sana specified (`record-vendor-invoice`, `payment-voucher-with-wht`) are
   **paused on B1/B2** — I did not stub or fake them. Shipped subset is fully
   verified; that's the honest 6/6.
2. `ExpenseCategorySelector` ships but is **unexercised** until the PV create form
   (B2) lands — built now as reusable infra per Sana's explicit ask.
3. `BankAccountSelector` **not built** — no `BankAccount` entity (Q3.1). PV detail
   shows cheque/bank as text; a `bank_account` master is future work. Defaulted per
   Question-Backend5 Q3 unless you override.
4. e2e = 5 critical paths (Sprint 3-4 discipline, no gold-plate). PV/WHT lists likely
   render empty on `teas_app` (no posted PV in that DB) — screens/sidebar still
   captured; behavioural PV/WHT coverage arrives with B2.
5. TH copy for `ven`/`pv`/`wht` is first-pass — Sana's pre-merge sweep recommended.
6. Long-path workaround still in force (`subst U:`, `-m:1`); `code/` canonical.

## 8. New runtime-gotcha for Sana to append (doc is Sana-owned)

**Category 15 — Playwright: list view renders the key in 2 cells.**
*Caught:* Sprint 5 (`record-vendor`). *Symptom:* `getByRole('cell',{name:X})`
strict-mode violation, "resolved to 2 elements". *Root cause:* the unique key (vendor
code) was embedded in the human name, and the list shows **code column AND name
column** — both cells satisfy a substring role-name match. *Fix:* assert with
`{ exact: true }` **and** keep the unique key out of any other displayed field.
*Prevention:* when a list shows an id/code in its own column, e2e identity assertions
must be `exact` and target that column only; never reuse the key inside a free-text
field. (Refinement of §5.) ROI table: Sprint 5 = 1 test fix, 0 app defects;
proactive §2 application prevented 1 latent 400.

## 9. Questions for Ham / Sana

1. **`Answer-Backend5` — required to close Sprint 5:** `B1 = A|B|C`
   (A=build VendorInvoice backend to plan, recommended), `B2 = A|B|C`
   (A=`Draft→Approved→Posted` + `ck_pv_sod`, recommended). On B1-A I'll send a
   1-page model/GL spec for sign-off **before** writing the migration.
2. Q3 defaults (Question-Backend5) assumed unless overridden: skip
   `BankAccountSelector`; 50 ทวิ rendered to plan §15.10 layout.
3. Sana parallel (FYI, non-blocking): openapi for `/vendors/{id}`,
   `/payment-vouchers` GET, `/wht-certificates`; the Sprint-4 receipts/CN/DN +
   reasonCode contracts still lagging; append gotcha **§15** above to
   `docs/runtime-gotchas.md`.

## 10. Status

Sprint 5 **partially done — shipped subset is "done done"** (built, verified, 6/6
e2e on the real stack, screenshots, 0 regression). VendorInvoice + PV-approve halves
**correctly paused** behind Question-Backend5 (structural/compliance — escalation
discipline intact, same as the C14N / Question-Backend4 precedents). e-Tax inert
(unchanged). Mirror synced; `code/` canonical. **Blocked on `Answer-Backend5`.**
