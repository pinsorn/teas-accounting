# Question-Backend12 — Sprint 8.6 spec-first gate (BEFORE migration)

**Date:** 2026-05-17
**From:** Claude Code
**To:** Ham / Sana
**Re:** Answer-Sana-Backend11 (Sprint 8.6 AR-side WHT) — 1 blocking premise gap +
4 confirmations needed before writing the `AddARWhtSupport` migration.

> Raising this as a spec-first gate (same discipline as Question-Backend5, which
> you asked for and approved) because this is a ~6-7 day sprint and one premise
> the spec leans on does not exist in the codebase. I have NOT written any
> migration/code yet. Survey done; nothing built. Recommendations included so
> this is a fast yes/adjust, not an open question.

---

## 🔴 BLOCKER — B1: `wht-base-suggest` service/goods split has no data model

**Spec §3.2 / §6.1 / DoD #7,#12 / e2e `receipt-customer-withholds`** require
splitting applied-TI line subtotal into `service_subtotal` vs `goods_subtotal`
by `Product.ProductType = SERVICE | GOOD`.

**Reality (verified across all `.cs` + `.sql`):**
- There is **no Product master entity, no `products` table, no `ProductType`
  enum, no `is_service` flag** anywhere in the codebase.
- `TaxInvoiceLine` has only `ProductId long?` + `ProductCode string?` — both
  free-form, not linked to any product table, no goods/service classification.
- TI line creation (`CreateTaxInvoiceRequest`) takes `ProductId?`/`ProductCode?`
  as optional free text; nothing populates or constrains them.

So the core auto-suggest ("WHT base = service-line subtotal only") **cannot be
computed** — the data to distinguish service vs goods does not exist. The spec's
own e2e §8.3 is internally contradictory about this (step 3: "base should be
10,000"; step 4: "wait — assume mixed 6k goods + 4k service").

Building a Product master with type classification is a **large unrequested
feature** (new entity + CRUD + UI + TI-line FK + migration of free-form
ProductCode) — that would be improvising well beyond scope, which §8/CLAUDE.md
forbids.

**Recommended resolution (pick one — R-B1a recommended):**

- **R-B1a (recommended, ~0 added scope):** Ship AR-WHT with **manual WHT base
  entry** (the UI already says every field is override-able). `wht-base-suggest`
  still ships but, with no product typing, suggests `suggested_wht_base =
  total applied subtotal (ex-VAT)` and `explanation = "ระบบยังไม่มี Product
  master แยกสินค้า/บริการ — ฐานเริ่มต้น = ยอดก่อน VAT ทั้งหมด กรุณาปรับเฉพาะส่วน
  บริการเอง"`. Rate/type still auto-suggested from `customer.default_wht_type_id`
  / CORPORATE heuristic. Everything else in the sprint is unaffected. e2e
  asserts the manual-override path (enter base 4,000 by hand → WHT 120). Defer
  the true service/goods split to a future "Product master" sprint.
- **R-B1b:** Add a **minimal `is_service` boolean on `TaxInvoiceLine`** (entered
  at TI creation, default false) — no full Product master. `wht-base-suggest`
  sums `LineAmount where IsService`. Adds ~1 column + 1 TI-form checkbox +
  migration; modest, but it does touch the (immutable, posted) TI write path and
  every existing TI test. Medium scope creep.
- **R-B1c:** Full Product master with `ProductType`. Large; effectively its own
  sprint. Not recommended now.

I recommend **R-B1a** — zero scope creep, ships the legally-important part
(GL Dr 1180 + WhtCertificate Direction='R' + ภ.ง.ด.50 register), and the
service-only base is an accountant convenience they can enter manually until a
Product master exists.

---

## 🟡 Confirmations (recommended answers — say "all R-default" to proceed fast)

**B2 — `SVC` rename strategy (spec §2.4 "confirm migration strategy").**
Existing seed `120` has `SVC` 3% PND53, `RENT` 5% PND3, `ADS` 2% PND53; seed
`170` links the SVC **expense category → SVC WhtType** (AP-side, Sprint 6); PV
tests reference `SVC`. Renaming `SVC`→`SVC-CORP` breaks 170 + Sprint 5/6 AP-side
+ existing green PV tests.
**R-B2 (recommended):** Do **NOT** rename. Keep `SVC`/`RENT`/`ADS` as-is; add
the new AR-relevant types alongside (e.g. `SVC-CORP` as a new code, or treat
existing `SVC` as the corporate-service type and just add the missing ones).
Zero regression. Confirm.

**B3 — 13 vs 14 WhtTypes (spec §2.4 recount confusion).**
Scope cut §9 explicitly excludes Payroll/`SALARY` (PND1). So the seed is **13
types, no SALARY**. Spec self-acknowledges ("could drop SALARY"). Confirm 13.

**B4 — e2e `receipt-customer-withholds` base figure (spec §8.3 contradicts
itself).** With R-B1a there is no auto service/goods split, so the e2e will:
post a mixed TI, toggle WHT, accept the suggested base = full ex-VAT subtotal,
then **manually override base to the service portion** and assert WHT = base ×
rate, cash = sum(apps) − WHT, GL Dr Bank + Dr 1180 = Cr AR. Confirm this
manual-override e2e shape is acceptable (it tests the real legal path).

**B5 — `CompanyService.CreateAsync` default-set copy scope (spec §3.4).**
`ICompanyService.CreateAsync(CreateCompanyRequest)` exists and currently inserts
only the Company row (no branches/CoA/wht_types — multi-company onboarding is
otherwise unexercised in Phase 1). Spec wants it to also copy 13 wht_types +
`1180` (+ "verify which other CoA missing").
**R-B5 (recommended):** Implement the copy as specced for **wht_types + 1180
only** (the two this sprint introduces). Do **not** expand it to a full
CoA/branch onboarding bootstrap (that's a separate concern, unscoped, and the
demo company is seeded via SQL scripts anyway). Confirm narrow scope.

---

## What I will build the moment this is answered (unblocked, no further questions)

Everything except the service/goods split, per the recommended answers:
- Schema/migration `AddARWhtSupport`: Receipt WHT fields + `CashReceived`;
  `WhtCertificate.Direction`/`ReceiptId` + `PaymentVoucherId` → nullable;
  `WhtType.EffectiveFrom/To` + unique-index swap; `Customer.DefaultWhtTypeId`.
- SQL `220` (13 wht_types, idempotent) + `230` (`1180` CoA + `tax.wht_type.manage`).
- `GlAccountsOptions.WhtReceivableAccount="1180"`.
- Receipt service: WHT capture + validators + GL branch (Dr Bank cash_received +
  Dr 1180 wht_amount = Cr AR; cross-BU per Sprint 8) + `WhtCertificate`
  Direction='R' on POST.
- `IWhtTypeService` CRUD + effective-date `ResolveAtDateAsync` +
  `POST /wht-types/{id}/change-rate` + perm.
- `CompanyService.CreateAsync` narrow default-set copy (R-B5).
- `wht-base-suggest` endpoint in the R-B1a (no-split) form.
- `/settings/wht-types` UI, Receipt form WHT toggle, detail section, list column,
  Receipt PDF WHT section (reuses 8.5 branching), 2 reports, i18n.
- Tests (unit + integration + 2 e2e), all gates → Playwright 18/18, mirror,
  plan.md §23.5 strike, Report-Backend12.

Phased + gated like Sprint 8 (P1 schema/migration → P2 service/GL/endpoints →
P3 WhtType master/effective-date → P4 reports → P5 UI → P6 tests/gates/wrap).

**Fastest path: reply "R-B1a + all R-defaults" (or adjust) and I start P1 immediately.**
