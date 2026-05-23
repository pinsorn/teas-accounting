# Non-VAT mode — complete design (บริษัทไม่จด VAT)

> Spec date: 2026-05-23. Status: **decisions locked with Ham (async); Ham to review spec when back from AFK.**
> Supersedes the partial Sprint 8.5 work (plan.md §23.4) and accounts for the 13j-PDF
> QuestPDF rewrite that regressed parts of it.

---

## 1. Legal basis (Thai Revenue Code)

A company **not registered for VAT** (ผู้ไม่จด VAT):

- **Cannot issue a Tax Invoice (ใบกำกับภาษี)** — ม.86/4 reserves it for VAT registrants. Issuing one illegally = penalty.
- Has **no output VAT**, does **not file ภ.พ.30** (VAT return), does **not do e-Tax invoice**.
- May issue: **ใบส่งของ (delivery note), บิลเงินสด (cash bill), ใบเสร็จรับเงิน (receipt)**.
- **Is still a withholding agent** when it pays for services/rent/etc. → **still files ภ.ง.ด.3 / ภ.ง.ด.53 (WHT)** and issues 50ทวิ. WHT is independent of VAT registration.

Mode is **config-only** (`Tax:VatMode` bool in appsettings/.env, never a UI setting — CLAUDE.md §4.6). It flows to the FE via `GET /system/info` (`vatMode`) and to the BE via `IConfiguration` / `VatModeOptions`. **The canonical flag is `vatMode` (bool), never `vatRate === 0`** (a registrant may legitimately have 0%-rated lines).

---

## 2. Decisions (locked with Ham)

| # | Question | Decision |
|---|----------|----------|
| D1 | TI creation under non-VAT | **Block** — hide menus/buttons (UI) **and** reject create/post at the API (BE). |
| D2 | Filing menu under non-VAT | **Hide ภ.พ.30** (VAT return); **keep ภ.ง.ด.3 / ภ.ง.ด.53** (WHT — still an agent). |
| D3 | Enforcement layer | **UI + BE both.** FE hides/strips VAT; BE rejects `taxRate > 0` (and TI issuance) when `VatMode=false`. |
| D4 | Billing path with TI blocked | **Both:** (a) Receipt may **apply to a DeliveryOrder** (credit sale: Q→SO→DO→RC); (b) **standalone Receipt** (cash bill, no application). |

---

## 3. Current state audit (2026-05-22/23)

**Already shipped & still correct (Sprint 8.5):**
- BE PDF doctype-label swap: `Domain/ValueObjects/DocumentLabels.cs` resolver; `TaxInvoiceService.Read.BuildPdfAsync` passes the resolved `hdrTh`/`hdrEn` into the QuestPDF model → **TI PDF header label is honored** even after the 13j-PDF rewrite. CN/DN legal-ref branch likewise.
- TI **detail page** hides the e-Tax CTA (`tax-invoices/[id]/page.tsx` reads `useSystemInfo().vatMode`).
- `IVatThresholdService` + ม.85/1 threshold banner.
- Config keys exist: `VatModeOptions` (`VatMode`, `NonVatDocLabelTh/En`), `TaxConfig`, `/system/info.vatMode`.

**Broken / not done (this sprint):**
1. **VAT total rows always render.** Both `Infrastructure/Pdf/PaperDocumentPdf.cs` (server PDF) and `frontend/components/paper/PaperFoot.tsx` (on-screen preview) always print "มูลค่าก่อนภาษี / Before VAT" + "ภาษีมูลค่าเพิ่ม X% / VAT". `PaperDocModel`/`PaperSummary` have **no `showVat` flag**.
2. **FE on-screen doctype label not swapped.** `vatMode` is consumed in only 3 FE files (`lib/queries.ts`, `tax-invoices/[id]/page.tsx`, the e2e). `components/paper/PaperHead.tsx` / `PaperDocument.tsx` render the static `docType` prop → preview shows "ใบกำกับภาษี" even under non-VAT (BE PDF is fine; on-screen is not).
3. **`LineItemsTable` VAT column** always shows a VAT-rate column (defaults 0% but column present).
4. **Filing menu** (`SidebarNav` + `/tax-filings/*`) shows ภ.พ.30 regardless of mode.
5. **TI creation** menu/buttons not gated; no BE block.
6. **e-Tax surfaces** beyond TI detail (TI list, `PrintMenu`, any resend) not audited under non-VAT.
7. **Receipt is hard-coupled to TI** — `CreateReceiptRequest.Applications` is `NotEmpty` and each item is a `TaxInvoiceId`. With TI blocked, **a non-VAT company cannot issue any receipt** → revenue cycle dead. (Drives D4.)

---

## 4. Design

### Phase 1 — VAT-artifact hiding (low risk, well-understood)

**1a. `showVat` flag through the paper model.**
- BE: add `bool ShowVat` to `PaperSummary` (or `PaperDocModel`). When `false`, `PaperDocumentPdf` renders a **single "ยอดรวม / Total"** row — no Subtotal-before-discount/Before-VAT/VAT rows. Source the flag from `VatModeOptions.VatMode` in every `*Service.Read.BuildPdfAsync` mapper (TI/RC/CN/DN/Q/SO/DO/BN).
- FE: mirror in `components/paper/types.ts` `PaperSummary.showVat?: boolean` (default `true`). `PaperFoot.tsx` hides the Before-VAT + VAT rows when `showVat === false`. Callers pass `useSystemInfo().data?.vatMode` → `showVat`.

**1b. FE on-screen doctype label.** Resolve the on-screen label from `vatMode`: when `false`, TI preview header → `nonVat.docLabel` (the same neutral "ใบส่งของ"/configured label the BE PDF uses). Add a small `useDocLabel(docKind)` helper (reads `useSystemInfo`) used by `PaperHead`/the detail pages. Keep BE as the source of truth for the PDF; FE only needs parity for the preview.

**1c. `LineItemsTable` VAT column.** Hide the VAT-rate column entirely when `useSystemInfo().data?.vatMode === false` (not merely default 0%). No VAT math shown; line totals only.

**1d. Filing menu (D2).** In `SidebarNav`, gate the **ภ.พ.30** item behind `vatMode === true`. **Keep** ภ.ง.ด.3 / ภ.ง.ด.53 and the missing-50ทวิ report visible (WHT path is independent). Guard the ภ.พ.30 route page too (redirect/empty-state if reached directly under non-VAT).

**1e. e-Tax surfaces.** Audit TI list + `PrintMenu` + any resend/XML action; gate every e-Tax affordance behind `vatMode === true` (mirrors the TI-detail gate already shipped).

### Phase 2 — Block TI + BE enforcement (D1, D3)

**2a. UI block.** Hide "สร้างใบกำกับภาษี" entry points (sales menu, TI list "new", DO→TI conversion buttons) when `vatMode === false`.

**2b. BE block (authoritative).** When `VatMode=false`:
- `TaxInvoiceService` create + post → reject with a domain error (`ti.non_vat_blocked`, ม.86/4). Covers Pattern X (DO auto-create TI) and Pattern Y (manual).
- Any sales-line create/update with `taxRate > 0` → reject (`vat.not_registered`). Apply in the validators / services for Quotation, SalesOrder, DeliveryOrder, BillingNote lines. (Force-to-zero is an alternative; **reject** is chosen so bad input is surfaced, per D3.)

### Phase 3 — Non-VAT billing path (D4) — **largest, GL/compliance-sensitive**

> ⚠️ **Open compliance item — flag for Ham before finalizing GL:** a standalone non-VAT receipt
> recognizes revenue at cash receipt (Dr Cash/Bank, Cr Revenue), unlike the VAT path where the TI
> recognizes revenue and the receipt only settles AR. The exact revenue/AR GL treatment for
> (a) DO-applied and (b) standalone receipts must be confirmed against `accounting-system-plan.md`
> §9 and Ham. **Implement the document/flow first; gate the GL posting behind explicit confirmation.**

**3a. Receipt applies to DeliveryOrder (credit sale).**
- Generalize the application reference: `ReceiptApplicationInput` gains an optional `DeliveryOrderId` (or a `kind` discriminator) while keeping `TaxInvoiceId` for the VAT path. Exactly one of {TI, DO} per application line.
- DB: `receipt_applications` gains a nullable `delivery_order_id` FK (mirrors the existing `tax_invoice_id` shape; child table, tenant-scoped via parent — no own `company_id`/RLS, matching the existing pattern). Migration.
- Line derivation (Approach-B style): when applied to a DO, derive receipt line items + WHT-suggest base from the **DO** lines instead of TI lines (DO is non-VAT → no VAT split; service lines still drive WHT).
- Validator: under `VatMode=false`, applications carry `DeliveryOrderId`; `TaxInvoiceId` applications are rejected (no TI exists).

**3b. Standalone Receipt (cash bill).**
- Allow `Applications` **empty** when `VatMode=false`. The receipt then carries **its own line items** (free entry or seeded from an SO). Requires persisting receipt lines (new `sales.receipt_lines` or reuse the derived-line shape made persistent for standalone). Migration.
- Doc label = ใบเสร็จรับเงิน / บิลเงินสด (configured neutral label).

**3c. GL.** Per the open item above — confirm + wire revenue recognition for the non-VAT receipt. No output-VAT line (vat=0 already yields none). Assert JV balanced.

### Phase 4 — Verification & docs
- Two-mode test discipline: every gate runs at `VatMode=true` (unchanged behavior preserved) **and** `VatMode=false`.
- Update `progress.md` (prepend cont. NN) + tick `plan.md`. Run `/graphify` if files added/moved materially.

---

## 5. Components & boundaries

- **`DocumentLabels` (Domain, pure)** — already the single compliance authority for doc-type labels. Extend if a new doc kind needs a non-VAT label. Unit-tested.
- **`PaperSummary.ShowVat` (BE) / `showVat` (FE)** — one boolean, one purpose: suppress VAT rows. No mode logic inside the renderer.
- **`useSystemInfo().vatMode` (FE)** — the single FE source for mode. New `useDocLabel`/gating reads from it; no component re-derives mode from `vatRate`.
- **`VatModeOptions.VatMode` (BE)** — the single BE source. Services read it; the renderer takes a plain bool.
- **Receipt application reference** — generalized to {TI | DO} via one optional field; exactly-one invariant enforced in the validator. Keeps the receipt service's line-derivation pluggable by source.

## 6. Testing strategy

- **Pure unit:** `DocumentLabels` (existing) extended; a `ReceiptWhtAllocator`-style test if DO-line derivation adds logic.
- **Validator unit:** TI blocked under non-VAT; `taxRate>0` rejected; receipt application exactly-one-of {TI,DO}; standalone allowed only under non-VAT.
- **Integration (PG):** receipt applied to DO posts + GL balanced; standalone receipt posts + GL balanced (after GL confirmation).
- **e2e (Playwright) two-pass:** `VatMode=true` stack unchanged; one `VatMode=false` pass asserting: no VAT rows on paper, neutral doc label, no VAT column, no ภ.พ.30 menu, ภ.ง.ด.53 present, no "สร้างใบกำกับภาษี", receipt creatable from DO / standalone.
- Test-data discipline per CLAUDE.md §15 (`TestIds.*` for any unique-constrained insert).

## 7. Verify gate

`dotnet build` 0/0 · Domain tests ≥89 (run from `W:`) · FE `tsc --noEmit` 0 · `next build` 0/0 (native path, stop dev first) · both modes exercised · `progress.md` cont. prepended · `plan.md` ticked.

## 8. Out of scope

- VatMode UI toggle (forbidden — §4.6).
- Retroactive PDF regen / re-issue of historical TIs.
- VAT-registration wizard / threshold-crossing auto-registration.
- e-Tax for non-VAT (not applicable).
- Inventory.

## 9. Phasing & risk

| Phase | Risk | Autonomy |
|-------|------|----------|
| 1 (VAT-artifact hiding) | Low | Implement now. |
| 2 (block TI + enforce) | Low–med | Implement now. |
| 3a (receipt apply-to-DO) | Med (schema + derivation) | Implement; tests. |
| 3b/3c (standalone + GL) | **High (revenue-recognition compliance)** | Build doc/flow; **GL gated on Ham confirmation** (§4 open item). |

---

Brainstormed with Ham 2026-05-23 (4 decisions locked). Ham AFK → proceeding per explicit
"proceed" authorization; this spec is the async review artifact.

---

## 10. Implementation status (cont. 67, 2026-05-23)

- **Phase 1 — DONE.** `ShowVat` flag BE+FE, `PaperFoot` single-Total, `LineItemsTable` column hidden,
  ภ.พ.30 menu+route gated. **§4 1b (FE on-screen TI label swap) DROPPED as moot** — TI blocked (D1), all
  other doc labels mode-neutral.
- **Phase 2 — DONE.** `TaxInvoiceService.EnsureVatRegistered()` (Create + Post chokepoint), FE create
  buttons gated. **Live-verified:** `POST /tax-invoices` → 422 `ti.non_vat_blocked`. **§4.2b taxRate>0 on
  Q/SO/DO/BN — NOT BE-enforced (scope decision):** VAT is realized only via TI (blocked); pre-sale docs are
  estimates; FE hides the column. Belt-and-suspenders API rejection deferred.
- **Phase 3a — DONE (BE, live-smoked).** Ham clarified (interactive): `Receipt.taxInvoiceId` nullable is
  **schema correctness, not a design choice** (ม.86/13 — a non-VAT entity issuing a TI = 2× penalty). Shipped:
  `ReceiptApplication.TaxInvoiceId` nullable + `DeliveryOrderId` (exactly-one check); standalone `ReceiptLine`
  table; `MarkPosted` source = apps OR own lines; GL `PostReceiptAsync` → **Cr Sales 4000 (cash basis)** for
  DO/standalone (Ham-confirmed; uses `GlAccountsOptions.SalesAccount`, no guessing) vs Cr AR for TI. Migration
  `AddReceiptWhtAndNonVatBilling` applied. Smoke: standalone receipt create+post → 200 RC-0002, balanced.
- **Phase 3b — DONE.** ภ.พ.36 (ม.83/6): every receiver remits, but a non-VAT receiver **cannot reclaim →
  sunk cost**. `GlAccountsOptions.IrrecoverableVatExpenseAccount` (5350, seeded) + `WhtFilingService` branch
  Dr 5350 / Cr 2151 (non-VAT) vs Dr 1170 / Cr 2151 (VAT). ภ.พ.36 menu kept visible (NOT hidden).
- **§4.2b correction (Ham):** "reject taxRate>0" was wrong — zero-rated (ม.80/1 export, rate 0) is legit,
  exempt (ม.81) has no VAT field, non-VAT entity has no taxRate concept. These are 4 states, already modeled
  on `TaxCode` (`IsZeroRated`/`IsExempt`). Enforcement = the TI block + FE column hide; no `taxRate>0` rule.
- **REMAINING:** FE non-VAT receipt form (standalone lines + DO picker); PG integration tests; `next build` +
  Playwright two-pass.
- **Gates:** build 0/0, Domain 89/89, FE tsc 0, migration applied, standalone receipt + TI-block live-smoked.
  VatMode=true preserved by construction.
- ⚠️ **Process lesson:** never `dotnet ef … --no-build` after entity edits (stale Api/bin → empty/wrong diffs
  + `migrations remove` reverted an untracked migration's Down on the dev DB; recovered via a consolidated
  regen). Commit Migrations/ with code.
