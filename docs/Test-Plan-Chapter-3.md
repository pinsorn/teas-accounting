# Test Plan — Chapter 3: การขาย (Sales Cycle)

**Owner:** Sana
**Authored:** 2026-05-19 (cont. 50, pre Sprint 13e merge)
**Refreshed:** 2026-05-21 (cont. 56, post Sprint 13h ☑ COMPLETE — for **deep-mode RE-VALIDATE**)
**Status:** Active checklist — deep-mode RE-VALIDATE per CLAUDE.md §16. No ลักไก่.
**Workflow gate:** CLAUDE.md §16 chapter-sequential — must pass before chapter 3 walkthroughs authored

---

## Deep-mode RE-VALIDATE checklist (Sprint 13h acceptance)

This top-section drives the active Chrome MCP session post-Sprint-13h. The
P-section material below stays as full reference. Sana's commitment after
the first-pass "ลักไก่" miss:

### Process rules (binding)

1. **Test as both roles** — `admin` / `Admin@1234` (control) AND
   `demo-accountant` / `Demo@1234` (RBAC fix subject).
2. **Open every PDF + XML** — verify byte size > 0, open in viewer/text
   editor. P11 fix specifically targets XML download — this MUST be verified.
3. **Click every button** — including "แก้ไข", "ลบ", "ยกเลิก", "ดาวน์โหลด PDF",
   "สร้าง TI จาก Q" — verify each action matches its label.
4. **Audit every form field** — diff form against entity spec; flag any
   free-text where enum/locked-from-product should apply (P7 BE shipped — FE
   polish in 13i, so partial coverage expected).
5. **Test every state transition both directions** — Draft → Issued → ...
   AND Issued → Cancel → ... per Plan §6.4 4-state DO + Q lifecycle.
6. **Test every list filter** — `?status=` URL persistence for SO/DO (P5).

### Sprint 13h smoke gates (priority order)

Verify each below in this order — if a gate breaks, STOP and write bug,
then continue.

| # | Gate | What to confirm |
|---|---|---|
| G1 | **P1 RBAC** | `demo-accountant` login → access all 8 sales surfaces (/quotations, /sales-orders, /delivery-orders, /tax-invoices, /receipts, /credit-notes, /debit-notes, /billing-notes) without 403 |
| G2 | **P2 Picker portal** | ProductPicker dropdown fully visible in Q form line items (no clip from container) + TaxInvoicePicker dropdown fully visible in RC, CN, DN forms |
| G3 | **P12 `<select>` CSS** | Open BU dropdown anywhere — full option height visible (no half-clip) |
| G4 | **P9 DO 4-state + Pattern X** | Create DO from SO → Draft → ออกใบส่งของ (Issue) → status Issued, **no auto-TI yet** → ยืนยันส่งมอบ (Mark Delivered) → status Delivered + **auto-TI fires** (verify TI shows up in detail cross-ref + /tax-invoices list) |
| G5 | **P6.1 TI ← Q FK** | Q Accepted detail has button "สร้าง TI จาก Q" → click → /tax-invoices/new?fromQuotationId=X → form prefilled (customer + line items + BU) → Post → TI detail shows Q chip in cross-ref |
| G6 | **P6.2 Billing Note** | Sidebar "ใบแจ้งหนี้" entry exists → create BN → fill TIs reference → Issue (doc# `MM-2026-BL-*-NNNN`) → Mark Settled OR Cancel from Issued → status transitions correct |
| G7 | **P8 cross-ref chips** | TI detail page shows Q chip + RC chip + CN/DN chips (where applicable). RC detail page shows TI chip. CN/DN detail page shows linked TI chip |
| G8 | **P8 PostConfirmDialog title** | Click Post on TI form → title is "ยืนยันการบันทึกใบกำกับภาษี". Click Post on RC form → title is "ยืนยันการบันทึกใบเสร็จรับเงิน" (not TI). Same for CN/DN/VI/Q/BN |
| G9 | **P10 Logo upload** | /settings/company → upload PNG ≤1 MB → preview renders → `LogoUrl` persists after refresh |
| G10 | **P11 XML 0-byte fix** | Open Posted TI → click "ดาวน์โหลด XML" → file size > 0 → opens as UBL-shaped XML in text editor |
| G11 | **P5 SO/DO filter** | /sales-orders → status filter dropdown → select "Posted" → URL becomes `?status=Posted` → filtered. Refresh → filter persists. Same for /delivery-orders with 4-state options |
| G12 | **P3 Thai date** | List dates render Thai locale (`พ.ศ. 2569`). Form date inputs accept Thai format (or clearly labeled CE alt). Verify `lib/format/date.ts` consumer points |
| G13 | **P4 BE Q lifecycle endpoints** | Direct API: `PUT /quotations/{id}` on Draft → 200; on Sent/Accepted → 409. `DELETE /quotations/{id}` on Draft → 204; on others → 409. FE UI for these still deferred → 13i, expect no buttons yet |
| G14 | **RC post nav** | After Post Receipt → page navigates away from /receipts/new to /receipts list or /receipts/{id} detail |
| G15 | **TaxInvoicePicker docNo display** | Pick TI in RC → input shows `05-2026-TI-ECOM-0001` (docNo), NOT `#1` (db id) |

---

---

## Purpose

One-pager test plan for chapter 3 (sales document cycle). Drives:
1. Sana's Chrome MCP **VALIDATE** pass (per CLAUDE.md §16 phase 1)
2. Bug spec for Backend30/31 if defects surface
3. Walkthrough authoring template for `frontend/manual/walkthroughs/03.01-03.07.ts`

**Compliance bar:** chapter 3 covers `ม.86/4` (Tax Invoice full fields),
`ม.86/9` (Debit Note), `ม.86/10` (Credit Note), `ม.78/78/1` (Tax Point),
posted-doc immutability (compliance §4.2). Test plan must exercise both
the happy path AND the compliance-enforcement branches.

---

## Scope — 7 endpoints × workflows

| # | Endpoint family | Pages | State machine |
|---|---|---|---|
| 03.01 | Quotation (Q) | `/quotations` (list, `/new`, `/[id]`) | Draft → Issued → Accepted → Converted (terminal). Rejected (terminal) |
| 03.02 | Sales Order (SO) | `/sales-orders` (list, `/new`, `/[id]`) | Draft → Confirmed → Fulfilled (terminal). Cancelled (terminal) |
| 03.03 | Delivery Order (DO) | `/delivery-orders` (list, `/new`, `/[id]`) | Draft → Issued → Delivered (terminal). Cancelled (terminal) |
| 03.04 | Tax Invoice (TI) | `/tax-invoices` (list, `/new`, `/[id]`) | Draft → Posted (terminal — e-Tax submitted). CN/DN issued against |
| 03.05 | Receipt (RC) | `/receipts` (list, `/new`, `/[id]`) | Draft → Posted |
| 03.06 | Credit Note (CN) | `/credit-notes` (list, `/new`, `/[id]`) | Draft → Posted → Applied (linked to original TI) |
| 03.07 | Debit Note (DN) | `/debit-notes` (list, `/new`, `/[id]`) | Draft → Posted → Applied |

State machines: per `docs/accounting-system-plan.md §6.4`.
Status-to-action map (button visibility): same §6.4 table.

---

## Priority 1 — Must (compliance-blocking; cannot skip)

### P1.1 — Happy path full sales cycle (B2B)
**Flow:** Q → Issue → Accept → Convert → SO → Confirm → DO → Issue → Delivered, **parallel** SO → TI → Post → Receipt → Post (B2B service order with VAT 7%).
**Pre-state:** logged in as `accountant` (or `AR_clerk`). Customer present
in master data (Tax ID + branch + address). Product/service present.
Business Unit selectable.
**Verify:**
- Each transition response status correct (200/201/204 per OpenAPI)
- Status badge updates (StatusBadge with new icon/color/text per state)
- Posted TI gets sequential doc_no (`MM-YYYY-TI-NNNN`); compare to TI list
  count delta = 1
- Receipt fully applies → TI shows `Paid` chip; partial Receipt → `Partially Paid`
- GL journal entries posted: Dr.AR / Cr.Sales / Cr.Output VAT (TI), then
  Dr.Cash-Bank / Cr.AR (Receipt). Inspect via `/journal-entries`
- Audit log entries: one per state transition (created/issued/accepted/
  converted/confirmed/posted)
- e-Tax (Tier 1 mock — MailHog): one signed XML + one customer email + one
  cc to `csemail@rd.go.th` per Posted TI

### P1.2 — Reissue flow via Credit Note (no same-day void)
**Flow:** Post TI #001 → realize typo in customer name → create CN against
#001 → post CN → optionally create new TI #002 as reissue (link
`is_reissue_of = #001`) → post #002.
**Verify (compliance — `ม.86/10`):**
- CN form pre-fills from #001 (customer, lines, amounts, BU)
- CN posting reduces Output VAT in current period (GL: Dr.Sales Returns /
  Cr.AR + offset Output VAT)
- TI #001 status stays `Posted` (NEVER changes to `Voided` — no same-day void)
- TI #002 (reissue): `is_reissue_of = #001` visible in UI; `tax_point_date` =
  original (`#001.tax_point_date`) per Plan §6.5; `doc_date` = today per
  trigger
- e-Tax sends CN XML + new TI XML, both signed, both emailed

### P1.3 — Immutability after Post
**Flow (TI):** Post TI #003. Try (a) edit the line items via UI form, (b)
DELETE via API direct curl, (c) modify customer info.
**Verify (compliance — §4.2):**
- (a) Edit button hidden / disabled; if force-attempt via dev tools → 409
  `urn:teas:error:document.immutable` (or `409` with appropriate message)
- (b) DELETE returns 405 or 409
- (c) Customer field disabled in detail view
- Same flow for CN, DN, RC after Posted state

### P1.4 — TaxInvoicePicker (Sprint 13e P3) integration
**Flow:** /receipts/new → pick customer → TaxInvoicePicker enables → type
partial doc_no `05-2026-TI` → list narrows to matching unpaid TIs → select
one → row populates `taxInvoiceId` + `appliedAmount = TI.totalAmount`.
**Verify:**
- `search` param hits backend (network log: `GET /api/proxy/tax-invoices?customerId=N&unpaid=true&search=05-2026-TI`)
- `unpaid` filter respected — paid TIs not in dropdown
- CN/DN form uses picker with `status=Posted` filter (Posted-only refs per
  `ม.86/10` / `ม.86/9`)
- Customer not yet chosen → picker disabled with hint text
- Reset customer → picker resets

---

## Priority 2 — Should (functional correctness + RBAC + validation)

### P2.1 — RBAC matrix per role
**Roles to test:** `SUPER_ADMIN` (god mode), `COMPANY_ADMIN`,
`CHIEF_ACCOUNTANT`, `ACCOUNTANT`, `AR_CLERK`, `AP_CLERK`, `SALES_STAFF`,
`AUDITOR` (read-only).
**Verify per role × per endpoint:**
- Can create draft? Can post? Can void? Can read?
- Specifically: `SALES_STAFF` can create Q/SO/DO drafts but NOT post TI;
  `ACCOUNTANT` can post TI; `AUDITOR` read-only on all
- 403 envelope: `urn:teas:error:auth.forbidden` + i18n `title` key
- PermissionGate hides post buttons for unauthorized roles (UI-level —
  defense in depth)

### P2.2 — Validation errors (ErrorEnvelopeV1 `fieldErrors[]`)
**Flow:** submit Q with: missing customer / empty line items / negative qty
/ BU enforced-on but blank / `doc_date` in future (Plan §10 — locked to
today).
**Verify (Plan §20.7):**
- 400 envelope: `urn:teas:error:validation` + `fieldErrors` array (camelCase
  field names + i18n message keys like `validation.required`,
  `validation.qty.positive`)
- FE renders inline errors below each field (via `parseApiError`)
- Top-level errors render as toast (not inline)

### P2.3 — State-machine guards (double-action 409)
**Flow:** Issue Q twice in quick succession; Convert Q→SO twice; Post TI
twice; Accept a Rejected Q.
**Verify:**
- 409 envelope: `urn:teas:error:state.invalid_transition` or similar
- UI buttons disabled/hidden after transition (PermissionGate +
  status-to-action map)
- **Q→SO double-conversion** (TBD pending Plan §6.4 update) — assert 1:1
  lock means second Convert button hidden once `ConvertedAt` set; force-
  attempt → 409 `quotation.already_converted`

### P2.4 — Cross-document state cascades
**Flow:** Cancel SO that has a Confirmed DO linked → expect block (or
cascade cancel with warning); Mark DO Delivered → SO auto-`Fulfilled` if
all linked DOs delivered.
**Verify:** state cascade per Plan §6.4 ("Fulfilled (auto when linked
DO/TI complete)"). Auto-transitions logged to audit table.

---

## Priority 3 — Nice (advanced flows, time-permitting)

### P3.1 — e-Tax mock failure paths (Tier 1)
Use MockServer to inject:
- RD endpoint 500 response → retry queue picks up, eventually dead-letter
  after 6 attempts (Sprint 13c)
- XAdES sign failure (corrupt PFX) → graceful error, TI stays Posted but
  e-Tax submission marked failed in `etax.submissions`
- Email send failure (MailHog down) → same retry semantics

### P3.2 — Idempotency-Key replay
**Flow:** POST `/quotations` with same Idempotency-Key + same body twice
within 24h → second returns cached 201 (or 200 with `Idempotency-Replayed:
true` header per Sprint 14).
**Variants:** same key + different body → 409
`urn:teas:error:idempotency.body_mismatch`.

### P3.3 — Cross-BU receipt (one Receipt applying to TIs from multiple BUs)
Confirm Sprint 8 cross-BU semantics still hold under chapter 3 workflows.
Receipt with applications spanning BU=A and BU=B → header BU=NULL +
per-application BU tag + cross-BU warning chip (Plan §6 / Sprint 8).

### P3.4 — Per-API-key BU lock (Sprint 14)
Use API key with `default_business_unit_id = X` to POST `/api/v1/tax-invoices`:
- Omit BU → auto-fill X (200)
- Specify matching BU → 200
- Specify different BU → 409 `business_unit.locked_mismatch`

---

## Cross-cutting patterns to reuse (proven on chapter 1+2)

1. **Error envelope assertions** — use `parseApiError` from
   `frontend/lib/api/errors.ts` to validate envelope shape (RFC 9110 →
   ErrorEnvelopeV1 unified per Sprint 13d-P5). Assert `type`, `status`,
   `fieldErrors[]` camelCase shape.
2. **AlertDialog assertions** — destructive actions (cancel SO, delete
   draft, void) use `useConfirm` → AlertDialog (Sprint 13d-P1). Test:
   walkthrough opens dialog → cancels → assert no mutation; opens →
   confirms → assert mutation fired.
3. **PermissionGate** — wrap buttons via `<PermissionGate scope="...">`
   (Sprint 13d-P3). Roles without scope see hidden button; roles with
   scope see enabled button.
4. **DOM-assert > waitForResponse** — Sprint 13g lesson. Don't wait for
   GET refetch; assert the new row in DOM after mutation toast. Example
   in `02.04-api-keys.ts`.
5. **TestIds random suffix** — every create flow uses `TestIds.*` (or
   `crypto.randomUUID()` slice) to avoid UNIQUE constraint hits on re-run
   against `teas_app`. CLAUDE.md §15.

---

## Open items (resolve before VALIDATE pass)

1. **Plan §6.4 Q→SO lock explicit text** — pending Sana old session update
   (relay 2026-05-19). Currently spec is "1:1 + lock + single FK"
   (Answer-Sana-Backend26 interpretation). Confirm + cite Plan section
   number in P2.3 above.
2. **Stub Q row count** — Sana new (this session) verify via `SELECT
   COUNT(*) FROM sales.quotations;` on `accounting_dev`. Drives whether
   migration backfill matters. Per Sana old: likely 0 from Sprint 13b
   survey.
3. **#59 tenant isolation audit** — open tracking. NOT in chapter 3 test
   plan scope; flagged separately. If production launch timeline tightens,
   consider running it in parallel with chapter 4/5.
4. **Test plan for Tenant Audit** — possible separate
   `docs/Test-Plan-Tenant-Audit.md` per Sana old guidance (12+ entities to
   audit). Out of chapter 3 scope.

---

## Definition of "chapter 3 validate complete"

- ☐ P1.1–P1.4 all green (Chrome MCP exercised, screenshots/logs captured)
- ☐ P2.1–P2.4 all green or bugs filed to Backend30/31
- ☐ P3.1–P3.4 attempted (time-permitting; failure-to-run is OK if logged)
- ☐ Bugs (if any) → `Answer-Sana-Backend{27,28,...}.md` spec → Claude Code fix → Sana RE-VALIDATE → green
- ☐ Walkthroughs `frontend/manual/walkthroughs/03.01-03.07.ts` authored
  from green-flow logs (only after all bugs cleared)
- ☐ `docs/manual/chapters/03-การขาย.md` authored
- ☐ Chapter-3 in plan.md ticked ☑
