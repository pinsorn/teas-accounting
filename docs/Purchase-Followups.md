# Purchase — Follow-ups backlog (post Sprint 13j-PURCH)

> Captured 2026-05-27 so they don't get lost. Sprint 13j-PURCH shipped in `01136c5`; these are
> the deliberately-deferred items. None block Purchase Phase 1. Source: `bugPurchase.md`, `docs/Report-Backend35.md`.

---

## ITEM 3 — Sales / test-infra track (small, ~15 min, separate PR)

These surfaced while running the Purchase E2E as a regression check. **Neither is a Purchase bug**; both
are pre-existing on the Sales/RBAC test track (out of Sprint 13j-PURCH scope per Requirements §6). Doing
them makes the "Sales E2E no-regression" gate actually runnable.

### BP-10 — Sales detail pages miss status test-ids (the Sales E2E can't run)
- **Cause:** `quotations/[id]`, `sales-orders/[id]`, `invoices/[id]` have NO `data-testid` on their
  `<StatusBadge>`. The E2E specs (`quotation-chain-flow.spec.ts`, `billing-note-flow.spec.ts`) assert
  `getByTestId('q-status' | 'so-status' | 'bn-status')` which never existed. Only `po-status` exists
  (added this sprint, `purchase-orders/[id]/page.tsx:51`).
- **Fix (mirror `po-status`):**
  - `quotations/[id]/page.tsx` → wrap the status badge: `<span data-testid="q-status"><StatusBadge … /></span>`
  - `sales-orders/[id]/page.tsx` → `data-testid="so-status"`
  - `invoices/[id]/page.tsx` → `data-testid="bn-status"`
  - (check the specs for any other `getByTestId('*-status')` on detail pages and add to match)
- **Then:** run `quotation-chain-flow.spec.ts` + `billing-note-flow.spec.ts`; chase any further reds
  (may be additional seed/data drift on the dev stack — re-seed `accounting_dev` if a fixed customer is missing).
- **Verify:** the two Sales specs green.

### BP-08 — `payment-voucher-non-super-rbac.spec.ts` picks a cross-company expense category
- **NOT a code bug.** `ExpenseCategory` is `ITenantOwned` / per-company by design; the global query
  filter (`AccountingDbContext:136`) correctly rejects a category from another company for a non-super
  role. **DO NOT weaken the filter — that would violate §4.7 / §10 (multi-tenant isolation).**
- **Fix (test-side):** in `payment-voucher-non-super-rbac.spec.ts`, resolve/seed the expense category
  **within the acting role's own company** (don't take an `admin`/super-listed cross-company id). Mirror
  how `purchase-chain.spec.ts` creates the PV with a company-scoped role.
- **Verify:** the rbac spec green; the tenant filter untouched.

---

## ITEM 4 — Question-Backend36 (optional BE, defer until needed)

**Server-resolved unified Purchase document chain.** Phase 1 ships a FE `PurchaseDocumentChain` that
resolves PO→VI→PV→WHT from cross-refs on the detail DTOs (upward + the new downward `settlingPvs` /
`whtCertificates`). That covers the linear chain. A *server-resolved* unified chain (like Sales
`DocumentCrossRefService.GetChainAsync`) is only needed if/when:
- the chain must show branches (e.g. multiple PVs settling one VI, or split VIs from one PO) richly, or
- a single `GET /documents/chain?docType=PO&id=…` response is wanted for parity with Sales tooling.

**Scope if taken:**
- Extend `DocumentCrossRefService.GetChainAsync` `switch(anchorType)` to handle PO/VI/PV/WHT (currently
  `default: return null`), OR add a `PurchaseChainService` mirroring it.
- `DocumentChainDto` is a fixed 7-slot Sales shape → needs a Purchase variant DTO (PO, VIs[], PVs[], WHTs[]).
- FE: swap `PurchaseDocumentChain`'s client-side hydration for the single endpoint (the component's
  node-rendering can stay).
- No migration (read-only).

**Recommendation:** leave deferred. Revisit only if the FE-side chain proves insufficient in real use.

---

## AFK-batch deferred (2026-05-28) — SHIPPED 2026-05-28 (cont.72)

These three were the leftover AFK-batch items from the morning of 2026-05-28. Each shipped with
its own commit on `main` + verification gates (see `progress.md` cont.72 for the full table). The
section is kept for trace-back; do not re-open.

- **C — Vendor Invoice mandatory vendor-file attachment** — DONE in commit `19516e2`. Post throws
  `vi.attachment_required` when no attachment, status stays Draft; FE detail page disables the Post
  button + shows the Thai/English warning banner. All 5 BE VI-post test sites + 2 e2e specs updated
  to seed/attach first; a new positive guard test pins the behavior.
- **F — server-resolved Purchase chain (Question-Backend36)** — DONE in commits `59ae661` (BE) +
  `378e4a4` (FE). New `IPurchaseChainService` + `GET /documents/purchase-chain` (own DTO, Sales
  resolver and DTO untouched). FE `PurchaseDocumentChain` now uses one `usePurchaseChain` hook
  instead of 4–N detail-DTO hydrations; testids + UX preserved.
- **WAGE WHT default** — DONE in commit `3f7c981`. New SqlScript `460_seed_wage_wht_type.sql`
  inserts the missing WAGE row (ม.40(2), ภ.ง.ด.3, 3%) and wires the WAGE expense-category default.
  **SAL** stays NULL — payroll ภ.ง.ด.1 progressive withholding is a separate subsystem,
  intentionally deferred to a future Payroll sprint ("ทำให้ Support ลองดูหน่อย").

## RESOLVED 2026-05-29 — `wht_types.income_type_code` = ม.40 sub-section (fixed in `954ff89`)

**Outcome:** Ham supplied the official RD ภ.ง.ด.3/53 booklet + the ภ.ง.ด.3 form. Both label the
income box verbatim by **ม.40 sub-section** (box 1=40(1), 2=40(2), 3=40(3), 4(ก)=40(4)(ก),
4(ข)=40(4)(ข); catch-all box = ทำของ/โฆษณา/เช่า/ขนส่ง/บริการ = 40(5)–(8)). So the Domain comment
was right and the data was wrong. Fixed 9 rows at source (220/250/460 + MasterDataServices
DefaultWhtTypes), added idempotent UPDATE seed `470` for existing DBs, and changed the 50ทวิ PDF
to print `ตามมาตรา 40(X) — desc`. Issued certs are snapshotted (PaymentVoucherService :235) so
unaffected. CPA-review judgment calls recorded in the `954ff89` commit body (WAGE/SVC-IND→40(8),
CONTRACT→40(7)). Verified: build 0/0, suite 178/178 ×2, dev DB income codes confirmed.

The original analysis is kept below for trace-back.

---

Surfaced while cross-checking the WHT seeds against `docs/Tax-Reference-TH.md` §2.1–2.2 (Sana's
reference) after shipping the WAGE row (commit `3f7c981`). **This was an open question, not a defect
— nothing was broken and no commit was blocked.** Needed the doc author / a CPA to settle the
canonical meaning before "correcting" the data — which Ham then did with the RD source files.

**The repo-verifiable fact (internal contradiction):** `WhtType.IncomeTypeCode` is documented in the
Domain entity as the ม.40 sub-section number — its XML comment says *"RD income type code (1 = เงินเดือน,
2 = ค่าธรรมเนียม, 5 = ค่าเช่า, …). See ม.40."* But the seeded data doesn't follow that scheme
consistently. Examples from `220_seed_wht_types_full.sql` / `250_seed_foreign_wht_types.sql`:

| code | concept | §2.1 ม.40 sub-section | seeded `income_type_code` |
|---|---|---|---|
| `PROF`  | ค่าวิชาชีพอิสระ | **40(6)** | `2` |
| `ADS`   | ค่าโฆษณา | **40(8)** (special 2%) | `4` |
| `COMM`  | ค่านายหน้า | **40(2)** | `3` |
| `AGRI`  | ค่าซื้อพืชผลเกษตร | **40(8)** | `6` |
| `RENT`  | ค่าเช่า | 40(5) | `5` ✓ matches |
| `CONTRACT` | ค่าจ้างทำของ | 40(7) | `7` ✓ matches |
| `WAGE` (new, this session) | ค่าจ้างแรงงาน | 40(2) | `2` |

So under the "it's the ม.40 sub-section" reading, several rows look off. **But that reading may not be
the intended one** — the value is printed verbatim as the income line on the 50ทวิ PDF
(`WhtCertificatePdf` renders `d.IncomeTypeCode`), and the ภ.ง.ด.3 / ภ.ง.ด.53 *ใบแนบ* use their own
income line-numbering that is distinct from the ม.40 sub-section numbers. So `income_type_code` could
legitimately be: (a) the ม.40 sub-section, (b) the ภ.ง.ด. attachment line number, or (c) an internal
code. The three give different "correct" values per row. **We cannot pick a fix until that is settled.**

**The discriminating question for Ham/Sana:** *What does `wht_types.income_type_code` represent — ม.40
sub-section, ภ.ง.ด. ใบแนบ line number, or an internal code?* Resolve via Sana (wrote both the reference
doc and the seed), the actual ภ.ง.ด.3/53 ใบแนบ, or the Smart WHT system (`whtsvs.rd.go.th`) — which
Tax-Reference §14.3 #4 explicitly names as the production lookup of record. Then either fix the Domain
comment (if the data scheme is right) or the data (if the comment is right).

**Blast radius is small + safe to defer:**
- **Rates + form types are NOT in question** — they match Tax-Reference §2.2 (RENT 5%/PND3, PROF 3%/PND53,
  ADS 2%/PND53, INT 1%, CONTRACT 3%, foreign 15%/PND54, and the new WAGE 3%/PND3). Only the
  `income_type_code` *label* is ambiguous.
- **Issued 50ทวิ are immune.** `PaymentVoucherService.cs:235` snapshots `IncomeTypeCode = whtType.IncomeTypeCode`
  onto the certificate row at PV-post. Editing a seed would only change *future* certs, never ones
  already issued — so this is not a "we corrupted past filings" situation, which is exactly why it's
  safe to leave for a daytime decision.
- **WAGE (this session) shares the same open scheme** for its `income_type_code='2'`; its rate (3%) and
  form (ภ.ง.ด.3) are independently correct per §2.2. If the scheme resolves against `2`, WAGE moves with
  the rest — it is not a special case.

## Lower-priority watch-items (from bugPurchase.md)
- **BP-01** 🟡 — one-off `DbUpdateException` on `PurchaseAuditTests.Pv_post_with_wht_…` (~1/many runs);
  not reproduced since. If it recurs, capture `ex.InnerException.Message`.
- **BP-02** 🔵 — pre-existing strict-YAML scanner trip in `docs/api/openapi.yaml` (`Idempotency-Replayed: true`
  unquoted in a `description:`). Harmless to Swagger/Redoc; quote it only if a strict YAML lint hits CI.
