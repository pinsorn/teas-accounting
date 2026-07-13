# Spec: MCP document chain (sales + purchase) — agent drafts every hop, human approves every hop

Status: DESIGN 2026-07-13. Requirements: Fable (from Ham consult, 2026-07-13 morning —
all rulings final). Technical design: opus-designer fills §D. Implementer: sonnet after
Fable approves §D. Blast radius: backend services + MCP + small FE (one button rule) +
tests. NO changes to posting flows initiated from the web UI.

## A. Ham's rulings (FINAL — do not re-litigate)

1. Per-hop draft tools (NOT a mega advance-chain tool). Approval deep-link per hop,
   exactly like existing DraftCreated tools. Agent can never approve/post.
2. Skip-DO rule is DATA-driven, never agent judgment: SO with ALL lines
   productType=SERVICE → DO skippable (invoice directly from SO); ANY GOOD line → DO
   mandatory. Enforced server-side; surfaced error when violated; exposed as a
   `deliveryRequired` field; FE mirrors the same rule on its action buttons.
3. FULL quantities only (no partial delivery/billing) — documented limitation, v2 later.
4. Purchase side in scope: PO → Vendor Invoice → Payment Voucher linkage
   (purchaseOrderId / vendorInvoiceId fields already exist in the MCP input shapes —
   make them real: line inheritance + AP settlement).
5. `get_workflow_guide` tool + MCP server `instructions`: server-generated per company
   from vatRegistered (non-VAT co NEVER sees a Tax Invoice hop; sees the ม.86/4 warning
   instead). Guide also teaches: verify-then-advance via get_document_status, the
   approval-link markdown rule, taxRate is FRACTIONAL (0.07), BU-required note,
   resolver tools map.
6. DraftCreated gains `approvalLinkMarkdown`: ready-made Thai-labeled markdown link per
   doc type (e.g. `[👉 กดตรวจและอนุมัติใบสำคัญจ่าย PV-...](url)`) so agents paste it
   instead of raw URLs. Server instructions repeat the rule.
7. No push notifications to agents (protocol reality). Pattern taught in guide:
   after sending an approval link, END the turn; next turn verify upstream state via
   get_document_status BEFORE creating the next hop. Chain guards make wrong-order
   calls fail safely with actionable text.

## B. New/changed MCP tools (contract level)

| tool | input | behavior |
|---|---|---|
| `create_sales_order_draft` | quotationId | Quotation must be ACCEPTED; inherits customer+lines+prices frozen from Q; guard: no second active SO per quotation (409-style surfaced error) |
| `create_delivery_order_draft` | salesOrderId | SO must be APPROVED; inherits; guard: no second active DO per SO (full-qty world) |
| `create_invoice_draft` | deliveryOrderId XOR salesOrderId | From DO (goods path, DO approved) or directly from SO (ONLY when SO is service-only per §A2 — else `[mcp.domain_rule]` telling agent to create the DO first); guard: no double-billing the same source |
| `create_receipt_draft` (EXTEND, breaking nothing) | + optional `invoiceId` | Absent → today's standalone cash-sale behavior byte-identical. Present → settlement mode: lines/amounts derive from the invoice; guards: invoice POSTED, not already settled, no over-collection |
| `create_tax_invoice_draft` (VAT cos) | already accepts quotationId | Wire into guide text only; no behavior change this cycle |
| `create_vendor_invoice_draft` | purchaseOrderId now REAL | PO must be APPROVED; inherit vendor+lines; guard vs double-invoicing a PO |
| `create_payment_voucher_draft` | vendorInvoiceId now REAL | VI must be POSTED; inherit; settlement mode for AP (see §C); guard vs double/over-payment |
| `get_sales_order`, `list_sales_orders` | NEW read pair | SO detail incl. `deliveryRequired` (per §A2) + chain state; list with basic filters |
| `get_document_status` | extend if needed | must cover SO/DO/IV so verify-then-advance works on every hop |
| `get_workflow_guide` | none | §A5. Read-only, per-company generated markdown |

DraftCreated: add `approvalLinkMarkdown` (all existing create tools too — additive).

**§B ADDITION ruled by Ham 2026-07-13 ~10:00 (mid-implementation): optional BN hop for
VAT companies** — "เพิ่ม BN เข้ามา ถ้ามันไม่กระทบอะไร แต่ BN เป็น Optional เหมือนเดิม เพราะปกติ
ใช้ Tax Invoice เก็บเงินอยู่แล้ว":
- NEW tool `create_billing_note_draft` (deliveryOrderId XOR salesOrderId, same source
  guards/skip-DO rule as create_invoice_draft's BN branch). Works for any company; for
  a VAT co it is the OPTIONAL วางบิล hop; for non-VAT it produces the same doc
  create_invoice_draft does (document this equivalence in the guide, it is not an error).
- EXTEND `create_tax_invoice_draft` with optional `billingNoteId` → reuse
  `TaxInvoiceService.CreateFromBillingNoteAsync` (draft-only; VAT chokepoint already
  guards). Dedup guard: one active TI per BN.
- MONEY GUARD (Fable): a VAT company's receipt must settle the TI, never the BN
  (Cr Sales with no output VAT = under-reported ภ.พ.30 — see §D.0). If code does not
  already block VAT-co receipt-vs-BN, ADD the guard with actionable text ("ออกใบกำกับภาษี
  จากใบแจ้งหนี้ก่อน แล้วรับชำระกับใบกำกับภาษี"). Non-VAT receipt-vs-BN unchanged (that IS
  their recognition path).
- Guide (VAT variant) gains the optional step; default path stays TI-direct
  ("ปกติใช้ Tax Invoice เก็บเงิน" — BN เฉพาะเคสวางบิล).
- Chain (VAT) becomes: `Q → SO → [DO] → [BN optional] → TI → RC(settle TI)`.

**✅ IMPLEMENTED 2026-07-13 (sonnet)** — see checklist evidence in §F/D8 below and the
attempt log. All 5 addition bullets folded in:
- `create_billing_note_draft` tool added (`TeasMcpTools.cs`, scope `BillingNoteManage`),
  reuses `IBillingNoteService.CreateFromDeliveryOrderAsync`/`CreateFromSalesOrderAsync`.
- `create_tax_invoice_draft` extended with optional `billingNoteId` (mutually exclusive
  w/ `quotationId`, validated) → `ITaxInvoiceService.CreateFromBillingNoteAsync`.
- Dedup guard added to `CreateFromBillingNoteAsync`: `bn.ti_exists` if a TI already
  references the BN.
- MONEY GUARD added to `ReceiptService.RebuildLinesAndTotalsAsync`'s BillingNoteId
  branch: `rc.vat_co_no_bn_settle` (Thai text exactly as specified) when the company is
  VAT-registered. Non-VAT path unchanged.
- `TeasServerInstructions.VatGuide`/`NonVatGuide` updated with the optional BN step +
  non-VAT equivalence note + the settle-guard warning.
- Tests: `Billing_note_draft_from_do_and_service_only_so_on_a_vat_company`,
  `Tax_invoice_draft_from_billing_note_and_dedup_guard`,
  `Vat_company_receipt_against_a_billing_note_is_blocked_must_settle_the_ti_instead`,
  guide-content assertions in `Mcp_get_workflow_guide_matches_company_vat_mode` — all in
  `backend/tests/Accounting.Api.Tests/Mcp/McpDocumentChainTests.cs`, all green.

**§B clarifications RULED by Fable 2026-07-13 (design review; see §D.0):**
- CRUX-1 ACCEPTED: `create_invoice_draft` is polymorphic by company VAT mode — VAT co
  → Tax Invoice draft; non-VAT co → BillingNote draft. `create_receipt_draft`'s
  `invoiceId` resolves the same way. This is the uniform-chain reading of §A2 and is
  forced by existing guards (ม.86/4 + rc.non_vat_no_ti).
- Status naming: "APPROVED" in the §B table means SO `Posted` / DO `Issued`/`Delivered`
  / PO `Approved` / VI `Posted` — exact enums pinned in D2; implementer uses D2, not
  the §B prose.

## C. Money paths (THE review battleground — Opus designs, Opus reviews, Fable reads
     this section personally before implementation AND before commit)

C1. Receipt settlement mode (AR): invoice posting already recognized revenue
    (Dr AR 1130 / Cr revenue). Settlement receipt must post Dr cash-or-bank /
    Cr AR — NEVER revenue again (double-count = silently inflated P&L).
    WHT withheld by customer (receipt_wht_lines exists) must reduce cash and
    Dr 1180 ภาษีหัก ณ ที่จ่ายค้างรับ, per existing receipt WHT pattern.
    Must flow to AR aging + customer statement (invoice stops aging when settled).
C2. PV settlement mode (AP): vendor invoice posting created Dr expense+input-VAT /
    Cr AP 2110. Settlement PV must Dr AP / Cr cash — never expense again. WHT we
    withhold reduces cash and Cr 2152 (CORRECTED per CRUX-2: 2153 is payroll PIT,
    never touched by PV).
C3. CRITICAL unknowns for opus-designer to resolve FROM CODE (not assumption):
    - Does receipt settlement logic already exist for the web chain (RC after TI)?
      If yes, reuse; map exactly which service method and how it picks Cr account.
    - Same question for PV←VI on the purchase side.
    - How existing web flow prevents double-settlement (states? links? amounts?).
    - Existing SO/DO/IV creation from approve-flow: which service methods create the
      next doc today; extract/reuse so MCP drafts share ONE code path with the web
      (zero duplicated posting logic — MCP layer stays thin wrappers per repo doctrine).
C4. Every JE in this cycle gets a worked example in the design (amounts incl. a WHT
    case) + a test pinning the exact Dr/Cr accounts. The 2026-07-09 lesson (sign-flip
    caught in a skipped section) applies: NO section of §C goes unreviewed.

## D. Technical design (opus-designer fills; Fable approves before implement)

> Design filled by opus-designer 2026-07-13 (all §C3 unknowns resolved FROM CODE).
> **Read "§D.0 — the two crux findings Fable must confirm" FIRST**: they change how a
> reviewer reads the whole design and one is a §B contract clarification (not a silent change).

### D.0 Crux findings — READ FIRST (flag to Fable, don't skip)

**CRUX-1 (the single riskiest decision — a §B contract clarification, NOT a silent change).**
"Invoice" is TWO different documents depending on the company's VAT registration, and this is
**forced by existing code**, not a design preference:
- **BillingNote** (`sales.billing_notes`, Thai ใบแจ้งหนี้) = the non-VAT "Invoice". Issuing it
  allocates an `IV-` number but posts **NO** journal entry. Revenue is recognised at receipt.
- **Tax Invoice** (`sales.tax_invoices`, ใบกำกับภาษี) = the VAT "Invoice". Posting it books
  **Dr AR 1130 / Cr Sales 4000 (+ Cr Output VAT 2151)** — this is the ONLY doc whose posting
  matches §C1's "invoice posting already recognized revenue (Dr AR / Cr revenue)".
- Two existing guards make this non-negotiable: (a) `TaxInvoiceService.EnsureVatRegisteredAsync`
  (`TaxInvoiceService.cs:68`) 422s a non-VAT company on ANY TI creation (ม.86/4); (b)
  `ReceiptService.RebuildLinesAndTotalsAsync` throws `rc.non_vat_no_ti` (`ReceiptService.cs:143`)
  if a non-VAT company tries to settle a receipt against a TI. And a VAT company that settled a
  receipt against a BillingNote would book Cr Sales with **no output VAT** → under-reported ภ.พ.30.
- **Therefore `create_invoice_draft` is polymorphic by company VAT mode**: VAT co → creates a
  **Tax Invoice draft**; non-VAT co → creates a **BillingNote draft**. Likewise
  `create_receipt_draft`'s new `invoiceId` resolves to a TaxInvoice (VAT) or a BillingNote
  (non-VAT). This is the only reading under which §A2's server-enforced `deliveryRequired`
  rule and "invoice directly from SO" apply **uniformly to both company types** (Ham's stated
  intent). `create_tax_invoice_draft` (quotationId, VAT-only) stays exactly as-is per §B — it
  becomes the *alternate* direct Q→TI path, wired into the guide only.
- **Alternative if Fable/Ham reject the polymorphism** (simpler, but descopes §A2 for VAT goods
  companies): make `create_invoice_draft` = BillingNote-only and let VAT companies invoice via
  the existing quotationId-anchored `create_tax_invoice_draft`. Cost: the `deliveryRequired`
  server rule then does **not** gate a VAT company (it can mint a TI from the Q without ever
  creating a DO). Recommendation: **ship the polymorphic version** — it honours §A2/§A5 fully.

**CRUX-2 (§C2 account-code correction).** §C2 says PV WHT credits "2152/2153". Code truth
(`GlAccountsOptions.cs:17,30`): PV withholding credits **2152** only (`WhtPayableAccount`,
ภาษีหัก ณ ที่จ่ายค้างจ่าย). **2153 is `PitPayableAccount` — payroll PND1, unrelated to PV.**
All PV worked examples/tests below pin **2152**. (AR-side receipt WHT correctly debits **1180**.)

### D1. Service-layer reality map (§C3 resolved — file:line for every path)

MCP doctrine (`TeasMcpTools.cs:176-183`): every tool is a **thin wrapper** over the SAME
Application service the BFF/REST routes call — zero duplicated business/posting logic. Every
row below is REUSE unless marked **NEW**.

**Sales chain (create = draft; a human posts via the approval deep-link — the agent never posts):**
| Hop | Reused service method (file:line) | Guard it already enforces |
|---|---|---|
| SO ← Quotation | `QuotationService.ConvertToSalesOrderAsync(id)` `QuotationChainServices.cs:229` | Q must be `Accepted` (:233); `q.ConvertedToSoId` null (:236) — **this is the §B "no 2nd SO per Q" guard, already present**. Creates a **Draft** SO, stamps `q.ConvertedToSoId`. |
| DO ← SO | `SalesOrderService.CreateDeliveryOrderAsync(soId, CreateDeliveryOrderRequest)` `SalesOrderDeliveryServices.cs:88` | SO must be `Posted` (:95). Inherits customer+BU+lines, tracks `DeliveredQuantity`, auto-closes SO→`Closed` when fully delivered (:154). Full-qty MCP wrapper builds the request from ALL SO lines. |
| Invoice(BillingNote) ← DO | `BillingNoteService.CreateFromDeliveryOrderAsync(doId)` `BillingNoteService.cs:72` | one-per-DO guard `do.invoice_exists` (:80-84). Draft BillingNote, `DeliveryOrderId` FK set. |
| TaxInvoice ← BillingNote (VAT) | `TaxInvoiceService.CreateFromBillingNoteAsync(bnId)` `TaxInvoiceService.cs:79` | VAT chokepoint `EnsureVatRegisteredAsync` (:83); draft-only; `deriveLineTax:false` chain-copy. |
| TaxInvoice (request-fed) | `TaxInvoiceService.CreateDraftAsync(req)` `TaxInvoiceService.cs:114` → `CreateDraftCoreAsync(deriveLineTax:true)` | VAT chokepoint (:122). Used today by `create_tax_invoice_draft` with optional `QuotationId`. |
| Receipt (settle or cash) | `ReceiptService.CreateDraftAsync(CreateReceiptRequest)` `ReceiptService.cs:42` | `Applications[]` already models settlement: `TaxInvoiceId` (VAT, →Cr AR), `BillingNoteId`/`DeliveryOrderId` (non-VAT, →Cr Sales). Validates TI Posted + same customer + within outstanding (:147-188). |
| Receipt post JE | `GlPostingService.PostReceiptAsync` `GlPostingService.cs:68` (human-triggered) | Cr-account decision keyed on application type (:113-132): `TaxInvoiceId`→Cr AR 1130; else→Cr Sales 4000. WHT→Dr 1180 (:103-112). |

**NEW sales builders (service layer, shared-able with a future web button — NOT MCP-only logic):**
- **NEW** `BillingNoteService.CreateFromSalesOrderAsync(soId)` — mirror `CreateFromDeliveryOrderAsync`
  (`BillingNoteService.cs:72`) copying from `SalesOrder.Lines`. Guards: SO `Posted`; SO service-only
  (`deliveryRequired == false`, see D4); one-per-SO (no BillingNote with this `SalesOrderId`).
  Add a nullable `SalesOrderId` FK to `BillingNote` (parallels the existing `DeliveryOrderId` FK).
- **NEW** `TaxInvoiceService.CreateFromDeliveryOrderAsync(doId)` — draft-only DO→TI, **exact
  clone** of `CreateFromBillingNoteAsync` (`:79`) but sourcing lines from `DeliveryOrder.Lines`
  (line-map already exists in `GenerateTiAsync` `SalesOrderDeliveryServices.cs:335-338`),
  calling `CreateDraftCoreAsync(..., deriveLineTax:false)`. Stamp `ti.DeliveryOrderId`. Does **NOT**
  post (unlike the existing `GenerateTiAsync` which auto-posts — do NOT reuse that method).
- **NEW** `TaxInvoiceService.CreateFromSalesOrderAsync(soId)` — same, sourcing from `SalesOrder.Lines`;
  guard SO service-only + Posted. Stamp `ti.SalesOrderId`.

**Purchase chain:**
| Hop | Reused method (file:line) | Reality vs §B |
|---|---|---|
| VI ← PO | `VendorInvoiceService.CreateDraftAsync(req)` `VendorInvoiceService.cs:58` | Stores `req.PurchaseOrderId` (:110) but **does NOT inherit lines** (lines come from `req.Lines`); PO status checked at POST (:273-296), not create; **multiple VIs per PO allowed today** (settlement SUMs them). §A4 "make it real" = NEW line inheritance + create-time PO-Approved guard + one-VI-per-PO. |
| VI settle-of-PO | `VendorInvoiceService.PostAsync` `:269-296` | Loose PO settlement/auto-close ≥95% (`PoSettlement.Evaluate` :288). Reuse as-is. |
| PV ← VI | `PaymentVoucherService.CreateDraftAsync(req)` `PaymentVoucherService.cs:106` | Stores `req.VendorInvoiceId` (:233); **does NOT inherit lines**. §A4 = NEW line inheritance + create-time VI-Posted guard + one-PV-per-VI. |
| PV settle-of-VI (AP) | `PaymentVoucherService.PostAsync` `:409-442` (human-triggered) | **Already correct**: Dr AP = `Subtotal+Vat`; over-settle guard (:426-429); VI `IConcurrencyVersioned` blocks double-settle (:440); `SettledAmount`/`SettlementStatus` maintained. |
| PV/VI post JE | `GlPostingService.PostPaymentVoucherAsync` `:148` / `PostVendorInvoiceAsync` `:308` | `pv.VendorInvoiceId != null` → Dr AP (settle, :161-172); else Dr expense. WHT→Cr 2152 (:210-216). VI post: Dr expense+input-VAT / Cr AP (:308-352). |
| "from-source" template | `PaymentVoucherService.CreateVendorInvoiceFromPvAsync` `:59-104` | The exact pattern for the two NEW inheritance builders (map source lines → target request, set the link, reuse the target's `CreateDraftAsync`). |

**NEW purchase builders:**
- **NEW** `VendorInvoiceService.CreateFromPurchaseOrderAsync(poId, expenseCategoryId, vendorTaxInvoiceNo, vendorTaxInvoiceDate, ...)`.
  **Footgun:** `PurchaseOrderLine` (`PurchaseOrder.cs:97-118`) carries ProductId/qty/UnitPrice/
  LineAmount/TaxRate but **NO `ExpenseCategoryId`/`ExpenseAccountId`** — which `VendorInvoiceLineInput`
  *requires*. So inheritance copies description/qty/amount/VAT from PO lines but the caller MUST
  supply a header `expenseCategoryId` applied to every inherited line (mirrors how
  `CreateVendorInvoiceFromPvAsync` uses a single `pv.ExpenseCategoryId` for all lines, `:86`).
  Guards: PO `Approved` (:reuse `PurchaseOrderStatus.Approved`); one-VI-per-PO (no non-Cancelled
  VI with this `PurchaseOrderId`). Set `PurchaseOrderId` so the existing POST-time auto-close still fires.
- **NEW** `PaymentVoucherService.CreateFromVendorInvoiceAsync(viId, paymentMethod, bankAccountId, ...)`.
  Inherits cleanly (VI line HAS `ExpenseAccountId`+`ExpenseCategoryId`): map `VendorInvoiceLine` →
  `PaymentVoucherLineInput` (ExpenseAccountId, Description, Amount, VatRate, ProductType; WHT rate/type
  from the category default, exactly as `CreateDraftAsync` already does :203). Guards: VI `Posted`
  (`DocumentStatus.Posted`); one-PV-per-VI (VI `SettlementStatus != PAID` AND no active PV with this
  `VendorInvoiceId`). Set `VendorInvoiceId` so the existing POST-time settlement (:409-442) fires.

### D2. State machines + exact status enums used for guards

Enums (`Domain/Enums/SalesChainStatus.cs`, `PurchaseOrderStatus.cs`, `DocumentStatus.cs`):
- `QuotationStatus`: Draft, Sent, Accepted, Rejected, Expired, Cancelled. **SO create guard: `Accepted` + `ConvertedToSoId == null`.**
- `SalesOrderStatus`: Draft, **Posted**, Closed, Cancelled. **DO/Invoice create guard: `Posted`.** (Full-qty DO → SO auto-`Closed`.)
- `DeliveryOrderStatus`: Draft, **Issued**, Delivered, Cancelled. **Invoice-from-DO guard: `Issued` or `Delivered`** (doc number allocated). *(Note §B says SO/DO must be "APPROVED" — in this codebase "approved" ≡ SO `Posted` / DO `Issued`; there is no "Approved" state on the sales chain. This is a naming alignment, not a behaviour change — call it out to the implementer.)*
- `BillingNoteStatus`: Draft, **Issued**, Settled, Cancelled. **Receipt-settle guard (non-VAT): `Issued` (not `Settled`).**
- `DocumentStatus` (TaxInvoice, VendorInvoice, Receipt, PaymentVoucher): Draft, Approved, **Posted**, Voided. TI/VI/Receipt: Draft→Posted. PV: Draft→Approved→Posted (SoD). **Receipt-settle guard (VAT): TI `Posted` + `PaymentStatus != "PAID"`. PV-from-VI guard: VI `Posted`.**
- `PurchaseOrderStatus`: Draft, **Approved**, Closed, Cancelled. **VI-from-PO guard: `Approved`.** (Full-qty VI → PO auto-`Closed` at ≥95%.)

Chain (VAT):  `Q(Accepted) → SO(Posted) → DO(Issued) → TaxInvoice(Posted) → Receipt(settle Cr AR)`.
Chain (non-VAT): `Q(Accepted) → SO(Posted) → DO(Issued) → BillingNote(Issued) → Receipt(recognise Cr Sales)`.
Service-only SO (`deliveryRequired==false`) skips the DO node in both chains.
Purchase: `PO(Approved) → VI(Posted) → PV(Posted, settle Dr AP)`.

### D3. Settlement JE design — worked examples (co-1 COA; pins for §C4 tests)

Codes from `GlAccountsOptions.cs`: AR **1130**, AP **2110**, Cash **1110**, Bank **1120**,
Sales **4000**, Output VAT **2151**, Input VAT **1170**, WHT-receivable **1180**, WHT-payable **2152**.
All settlement JEs are produced by EXISTING posters — **no new GL code**. The design's job is to
prove the reused path yields these lines; tests pin them exactly.

**(a) AR settlement receipt, WITH customer WHT (VAT co).** TI posted for 100,000 net + 7,000 VAT =
107,000 (already Dr 1130 107,000 / Cr 4000 100,000 / Cr 2151 7,000 at TI post). Customer withholds
3% of the 100,000 base = 3,000. Receipt settles the TI in full. `PostReceiptAsync` (`:68`):
```
Dr 1120 Bank            104,000   (cash_received = 107,000 − 3,000 WHT)
Dr 1180 WHT-receivable    3,000   (rc.WhtLines, BaseAmount 100,000 × 3%)
    Cr 1130 AR                    107,000   (Applications[].TaxInvoiceId = full applied)
```
Revenue is NEVER re-credited (it was booked at TI post). TI `AmountPaid += 107,000` → `PaymentStatus=PAID`
(`ReceiptService.cs:428-437`). Flows to AR aging / customer statement (TI stops aging when PAID).

**(b) Non-VAT recognition receipt (BillingNote path), no WHT.** BillingNote (Invoice) IV issued for
50,000, no VAT (ม.86/4). Receipt applied to the BillingNote. `PostReceiptAsync` else-branch (`:123-132`):
```
Dr 1110 Cash           50,000
    Cr 4000 Sales                  50,000   (Applications[].BillingNoteId → recognise, cash basis)
```
BillingNote auto-flips to `Settled` when receipts cover its total (`ReceiptService.cs:456-489`).

**(c) AP settlement PV, WITH our WHT (2152).** VI posted for 100,000 net + 7,000 input VAT (recoverable):
at VI post `Dr expense 100,000 / Dr 1170 7,000 / Cr 2110 107,000`. We pay the VI, withholding 3% of
100,000 = 3,000. `PostPaymentVoucherAsync` VI-linked branch (`:161-172,210-222`):
```
Dr 2110 AP             107,000   (Subtotal 100,000 + Vat 7,000 — settle, expense NOT re-booked)
    Cr 2152 WHT payable          3,000   (our withholding, remitted to RD)
    Cr 1120 Bank               104,000   (TotalPaid = 107,000 − 3,000)
```
Expense/input-VAT are NEVER re-debited (booked at VI post). VI `SettledAmount += 107,000` →
`SettlementStatus=PAID` (`PaymentVoucherService.cs:437-439`). A 50ทวิ (PND3/53) is auto-issued at PV post.

**(d) Standalone cash-bill receipt (byte-identical to today).** `invoiceId` absent → `Applications: []`
→ `PostReceiptAsync` standalone branch (`:134-141`) Cr Sales. Confirms §B "absent → today's behaviour".

### D4. `deliveryRequired` derivation + where FE reads it

- **Derivation (server, in `SalesOrderService.GetAsync`, `SalesOrderDeliveryServices.cs:184-196`):**
  `deliveryRequired = so.Lines.Any(l => l.ProductType is "GOOD" or "EXEMPT_GOOD")`. Product-type
  codes are `GOOD | SERVICE | EXEMPT_GOOD | EXEMPT_SERVICE` (`ProductType` snapshot on `SalesOrderLine`,
  `SalesOrder.cs:62`). Physical goods (GOOD, EXEMPT_GOOD) → delivery mandatory; all
  service/exempt-service → skippable. (Decision: EXEMPT_GOOD counts as a good — it is still a
  physical thing to deliver; note this to the implementer.)
- **Backend DTO:** add `bool DeliveryRequired` to `record SalesOrderDetail` (`SalesChainDtos.cs:90`).
  The `GetAsync` projection already loads `so.Lines`, so no extra query.
- **Server enforcement (the real gate, per §A2):** `create_invoice_draft` recomputes the same rule
  from the source and, when called with a `salesOrderId` on a goods SO (`deliveryRequired==true`),
  throws `[mcp.domain_rule]` "create the Delivery Order first" — never trusts the FE.
- **BFF:** none. The FE fetches `sales-orders/{id}` through the generic catch-all proxy
  (`frontend/app/api/proxy/[...path]/route.ts`) which streams the backend body verbatim — the new
  field flows through with zero BFF change.
- **FE type:** add `deliveryRequired: boolean` to `SalesOrderDetail` (`frontend/lib/types.ts:1002-1007`).

### D5. Dedup / 409 guards — **state checks, NOT new unique indexes**

Full-qty world + **human-approval pacing** (each hop requires a human to approve the upstream doc
before the agent creates the next — §A7) means the machine-speed race that justified
`MatchTargetUniqueness` (bank-rec auto-matcher, migration `20260710110531`) does **not** apply here.
State checks + the existing status transitions are sufficient and match every existing sibling guard:

| Source→target | Guard (all state checks) |
|---|---|
| Q → SO | `q.ConvertedToSoId == null` (existing `QuotationChainServices.cs:236`) |
| SO → DO | SO must be `Posted`; a full DO auto-closes SO→`Closed`, so a 2nd DO fails the `Posted` check (existing). Add explicit "no active DO for this SO" at create for the draft window. |
| DO → Invoice | `do.invoice_exists` (existing `BillingNoteService.cs:80-84`) for BillingNote; `dord.TaxInvoiceId == null` for TI. |
| SO → Invoice (service-only) | NEW: no BillingNote/TI already carrying this `SalesOrderId`. |
| Invoice → Receipt | VAT: TI `PaymentStatus != "PAID"` + over-collection guard (existing `:431`). non-VAT: BillingNote `!= Settled`. |
| PO → VI | PO `Approved`; NEW "no non-Cancelled VI with this `PurchaseOrderId`" (today multiple allowed — this is the new §B guard). |
| VI → PV | VI `Posted` + `SettlementStatus != "PAID"`; over-settle guard + VI `Version` concurrency (existing `:426-440`); NEW "no active PV with this `VendorInvoiceId`". |

All new guards throw a `DomainException` with an `mcp.*`/domain code that the error-surfacing filter
(`McpErrorSurfacingFilter.cs`) turns into actionable agent text. **No DB unique index required**;
if a future v2 removes the human-in-the-loop, revisit with the `MatchTargetUniqueness` partial-unique
pattern. (One cheap optional hardening the implementer MAY add: a partial unique index on
`sales.billing_notes(delivery_order_id) WHERE status <> 'Cancelled'` — it costs one line and makes the
one-per-DO invariant race-proof. Not required; flag if the implementer wants it.)

**IMPLEMENTATION NOTE (sonnet, 2026-07-13):** the "no active DO for this SO" guard does NOT
live in the shared `SalesOrderService.CreateDeliveryOrderAsync` — that method is ALSO the
existing, still-supported PARTIAL-delivery path (multiple DOs against one SO covering
different lines/quantities; see `Sprint10ChainTests.Partial_delivery_keeps_so_open_until_fully_delivered`
and `ImmutabilityAndGuardTests.DeliveryOrder_exceeding_so_line_qty_is_rejected`). A shared-layer
guard broke both tests on first attempt (caught by the full-suite gate) — reverted and moved
into the `create_delivery_order_draft` MCP TOOL instead (`mcp.do_exists`), which is genuinely
full-qty-only. The optional partial-unique index was NOT added (Ponytail, per spec).

### D6. `get_workflow_guide` tool + server `instructions`

**Where instructions plug in.** `AddMcpServer()` is called parameterless at `Program.cs:274-278`.
`McpServerOptions.ServerInstructions` **exists in ModelContextProtocol 1.4.0** (verified in the
installed package) and is unset today. It is a **static, company-agnostic** string sent at the
init handshake — it CANNOT vary per company (the stateless server has no tenant at registration).
So the split is:
- **`ServerInstructions` (static):** the cross-company rules. Set it exactly the way the error
  filter already reaches the options — `builder.Services.AddOptions<McpServerOptions>().Configure(o => o.ServerInstructions = TeasServerInstructions.Text)` next to `McpErrorSurfacingFilter.cs:52`,
  OR the `AddMcpServer(o => o.ServerInstructions = …)` overload. Content: "Before advancing any
  document chain, call **get_workflow_guide** for this company's exact steps. taxRate is FRACTIONAL
  (0.07 = 7%, never 7). Every create tool returns `approvalLinkMarkdown` — paste that markdown link
  verbatim so the human can approve; the agent can NEVER post/approve. After sending an approval
  link, END the turn; next turn call **get_document_status** to confirm the upstream doc reached its
  posted/approved state BEFORE creating the next hop. BU may be required. Resolver tools: list_customers/
  list_products/list_vendors/list_bank_accounts map names→ids."
- **`get_workflow_guide` (tool, per-company dynamic):** read-only, `[Authorize(Policy = <a broadly-held
  read scope, e.g. QuotationRead>)]`, reads `ICompanyTaxConfigService.GetAsync().VatMode` at call time
  and returns the VAT or non-VAT markdown below. This is where §A5's per-company variance lives.

**VAT-registered company guide (Thai):**
```
# ขั้นตอนเอกสารขาย (บริษัทจด VAT)
1. สร้างใบเสนอราคา → create_quotation_draft → ส่งลิงก์ให้ผู้ใช้กด "ส่ง/อนุมัติ"
2. เมื่อลูกค้าตอบรับ (Accepted) → create_sales_order_draft (ใส่ quotationId)
3. ตรวจ get_document_status ว่า SO = Posted แล้ว → ถ้ามีสินค้า (deliveryRequired=true)
   สร้างใบส่งของ create_delivery_order_draft (ใส่ salesOrderId); ถ้าบริการล้วน ข้ามได้
4. สร้าง "ใบกำกับภาษี" (ใบแจ้งหนี้ของบริษัท VAT) → create_invoice_draft
   (deliveryOrderId ถ้ามีของ / salesOrderId ถ้าบริการล้วน) — ระบบออกเป็นใบกำกับภาษี
   * ทางเลือก (Optional): ถ้าต้อง "วางบิล" ก่อน ให้ create_billing_note_draft
     (deliveryOrderId / salesOrderId) แล้วค่อย create_tax_invoice_draft (billingNoteId)
     — ปกติไม่จำเป็น ใช้ Tax Invoice ตรงตามขั้นตอนที่ 4 ก็เพียงพอแล้ว
5. เมื่อผู้ใช้ post ใบกำกับภาษีแล้ว → รับชำระ create_receipt_draft (ใส่ invoiceId = id ใบกำกับภาษี)
   ระบบจะตัด AR ให้ (เดบิตเงินสด/ธนาคาร เครดิตลูกหนี้ 1130) ไม่รับรู้รายได้ซ้ำ
   ถ้าลูกค้าหัก ณ ที่จ่าย ให้แนบ WHT — ระบบเดบิต 1180
   ⚠️ ห้ามรับชำระกับใบแจ้งหนี้ (BillingNote) ตรงๆ สำหรับบริษัทจด VAT — ต้องออกใบกำกับภาษี
   จากใบแจ้งหนี้ก่อน แล้วรับชำระกับใบกำกับภาษีเท่านั้น (ระบบจะปฏิเสธพร้อมข้อความแนะนำ)
* ทุกขั้น: วางลิงก์ approvalLinkMarkdown ให้ผู้ใช้กดอนุมัติ เอเจนต์ห้าม post เอง
* taxRate เป็นเศษส่วน (0.07 = 7%)
```

**Non-VAT company guide (Thai) — NO Tax Invoice hop, ม.86/4 warning:**
```
# ขั้นตอนเอกสารขาย (บริษัทไม่จด VAT — ม.86/4)
⚠️ บริษัทนี้ไม่จด VAT จึงออก "ใบกำกับภาษี" ไม่ได้ (ม.86/4) — ใช้ "ใบแจ้งหนี้" แทน
1. create_quotation_draft → ส่งอนุมัติ
2. Accepted → create_sales_order_draft (quotationId)
3. SO = Posted → มีสินค้า สร้าง create_delivery_order_draft (salesOrderId); บริการล้วน ข้ามได้
4. สร้าง "ใบแจ้งหนี้" → create_invoice_draft (deliveryOrderId / salesOrderId) — ระบบออกเป็นใบแจ้งหนี้
   (create_billing_note_draft ให้ผลเหมือนกันทุกประการสำหรับบริษัทนี้ — ใช้ตัวใดตัวหนึ่งก็ได้)
5. รับชำระ create_receipt_draft (ใส่ invoiceId = id ใบแจ้งหนี้) — ระบบรับรู้รายได้ตอนรับเงิน
   (เดบิตเงินสด/ธนาคาร เครดิตรายได้ 4000) ไม่มี VAT ขาย
* วางลิงก์ approvalLinkMarkdown ทุกขั้น; taxRate = 0.07 (แต่บริษัทนี้ = 0)
```
(Purchase guide is identical for both: `create_purchase_order_draft → (approve) → create_vendor_invoice_draft`
`(purchaseOrderId) → (post) → create_payment_voucher_draft (vendorInvoiceId)`.)

**`approvalLinkMarkdown` (§A6):** add a 3rd positional field to `record DraftCreated`
(`TeasMcpTools.cs:273-276`); update all **8** construction sites (`:371,432,485,601,638,678,1509,1567`)
plus the `PendingApprovalItem` sites (`:1337-1382`) and the shape assertions in
`McpServerSmokeTests.cs:339` / `M4aDraftCreatedViaApiKeyTests.cs`. Build it from the existing
`ApprovalUrl(...)` helper (`:1629-1633`): e.g. `$"[👉 กดตรวจและอนุมัติ{docLabel} {docNo}]({url})"`
with a per-doc-type Thai label + the doc number (pass the number into the helper).

**✅ IMPLEMENTED** — `TeasServerInstructions.cs` (new file) carries `Text`/`VatGuide`/
`NonVatGuide`/`PurchaseGuide`. `Program.cs` wires `ServerInstructions` via
`AddOptions<McpServerOptions>().Configure(...)`. `get_workflow_guide` tool added
(`QuotationRead` scope). All 8 `DraftCreated` sites + all 6 `PendingApprovalItem` sites
updated with `ApprovalLinkMarkdown` (built from a per-route Thai label + `{prefix}-{id}`
placeholder, since the real doc_no isn't allocated on a Draft). `McpServerSmokeTests.cs:339`
assertion extended to check `approvalLinkMarkdown` is non-empty and contains the approval
URL. `M4aDraftCreatedViaApiKeyTests.cs` reviewed — it tests `CreatedViaApiKeyName` stamping
via services directly and never constructs/asserts the `DraftCreated` MCP shape, so no
change was needed there (flagging this as a doc-citation mismatch, not a gap).

### D7. Scopes / policies (reuse-first per subledger precedent) + RbacEndpointInventory

MCP tool scopes are method attributes `[Authorize(Policy = Pfx + "<scope>")]` and must ALSO appear
in `McpScopes.All` (`McpScopes.cs:11-48`) to be grantable; **`/mcp` tools are excluded from
`RbacEndpointInventory`/`RbacAuthMap`** (classified ApiKeyOnly, `RbacEndpointInventory.cs:124-127`)
— so **no RbacEndpointInventory/AuthMap change is required for the new MCP tools**.

| New tool | Scope (policy const) | Catalog action |
|---|---|---|
| `create_sales_order_draft`, `get_sales_order`, `list_sales_orders` | `sales.sales_order.manage` (NEW const `SalesOrderManage = Pfx + "sales.sales_order.manage"`) | **ADD one entry** to `McpScopes.All` + the FE `MCP_DEFAULT_SCOPES` mirror. Maps identity to the EXISTING RBAC perm `Permissions.Sales.SalesOrderManage` (`Permissions.cs:67`) — no `McpConsentScopes` override. Mirrors the DO precedent (manage-only, no separate `.read`). |
| `create_delivery_order_draft` | `sales.delivery_order.manage` (`DeliveryOrderManage`, already in catalog) | reuse (list/get_delivery_order already use it). |
| `create_invoice_draft` | `sales.billing_note.manage` (`BillingNoteManage`, already in catalog) | reuse. **Scope note (from CRUX-1):** for a VAT company this tool drafts a *Tax Invoice*, yet it is gated on billing_note.manage. Accepted because the output is a reversible DRAFT (no doc number/tax point) and the human who POSTS it must hold `sales.tax_invoice.post` (RBAC) — the agent never posts. If Fable wants scope purity, mint `sales.invoice.create`; recommendation is to reuse billing_note.manage (Ponytail). |
| `create_receipt_draft` (extend) | `sales.receipt.create` (`ReceiptCreate`, unchanged) | none. |
| `create_vendor_invoice_draft`/`create_payment_voucher_draft` (extend) | `purchase.vendor_invoice.create` / `purchase.payment_voucher.create` (unchanged) | none. |
| `get_document_status` (extend) | `TaxInvoiceRead` (unchanged) | none. |
| `get_workflow_guide` | reuse a broadly-granted read scope, e.g. `sales.quotation.read` (`QuotationRead`) | none. |

`McpScopesTests.cs:9-14` invariant: `sales.sales_order.manage` ends in `.manage` (not a forbidden
`.post/.approve/...` suffix) → passes. Denial test per the `Mcp_report_tools_are_denied_without_the_report_scope`
pattern (`McpReadExpansionTests.cs:151`).

**✅ IMPLEMENTED** — `McpScopes.All` gained `sales.sales_order.manage`; FE
`MCP_DEFAULT_SCOPES` mirror updated (`frontend/app/(dashboard)/settings/api-keys/page.tsx`).
Every new tool wired to the exact scope named above. `create_billing_note_draft` (§B
addition) reuses `BillingNoteManage` (no new scope). RBAC/McpScopesTests gate: green (see
§F evidence).

### D8. Test plan (backend integration unless noted)

Per-hop happy path + EVERY guard, on the `teas_test` fixture (use today/future dates — relative-date
seed footgun). Use a **VAT fixture co** and a **non-VAT fixture co**.

All 12 backend items + the §B-addition items below are implemented in
`backend/tests/Accounting.Api.Tests/Mcp/McpDocumentChainTests.cs` (23 tests, all green —
see §F evidence). Item 13 (FE) tracked separately under D9.

1. [x] **Sales happy (VAT):** Q(accept)→SO(create+post)→DO(create+issue)→create_invoice_draft⇒**TaxInvoice**
   draft→post→create_receipt_draft(invoiceId=TI)⇒**pin JE = D3(a)** (Dr 1120/Dr 1180/Cr 1130; no Cr 4000).
   → `Vat_sales_chain_settles_ti_with_customer_wht_pins_D3a_je`.
2. [x] **Sales happy (non-VAT):** …→create_invoice_draft⇒**BillingNote** draft→issue→create_receipt_draft(invoiceId=BN)
   ⇒**pin JE = D3(b)** (Dr 1110/Cr 4000). Assert TI creation on this co 422s `ti.non_vat_blocked`.
   → `NonVat_sales_chain_settles_billing_note_pins_D3b_je_and_blocks_tax_invoice`.
3. [x] **Service-only skip-DO:** SO all-SERVICE → `deliveryRequired==false`; create_invoice_draft(salesOrderId) succeeds.
   → `Service_only_so_skips_do_and_invoices_directly`.
4. [x] **deliveryRequired enforcement:** goods SO → create_invoice_draft(salesOrderId) throws `mcp.domain_rule`.
   → `DeliveryRequired_blocks_direct_so_invoice_for_a_goods_line` (service-layer code check) +
   `Mcp_create_invoice_draft_is_polymorphic_and_wraps_the_delivery_required_guard` (real MCP
   round-trip confirming the `[mcp.domain_rule]` wrapper AND the VAT-mode polymorphism).
5. [x] **Standalone receipt unchanged:** create_receipt_draft with no invoiceId ⇒ JE = D3(d) (byte-identical baseline).
   → `Mcp_standalone_receipt_unchanged_pins_D3d_je` (real MCP round-trip).
6. [x] **Dedup guards (each →409-style error):** 2nd SO per Q; 2nd DO per SO; 2nd Invoice per DO/SO; receipt
   on a PAID TI / Settled BN; 2nd VI per PO; 2nd PV per VI.
   → `Dedup_guard_rejects_a_second_so_from_the_same_quotation`,
   `Dedup_guard_rejects_a_second_delivery_order_for_the_same_so`,
   `Dedup_guard_rejects_a_second_invoice_for_the_same_delivery_order`,
   `Dedup_guard_rejects_a_receipt_on_an_already_paid_ti`,
   `Dedup_guard_rejects_a_receipt_on_a_settled_billing_note`,
   `Dedup_guard_rejects_a_second_vendor_invoice_for_the_same_po`,
   `Dedup_guard_rejects_a_second_payment_voucher_for_the_same_vi`.
7. [x] **Purchase happy:** PO(approve)→create_vendor_invoice_draft(purchaseOrderId)⇒lines inherited + PO-Approved
   guard→post→create_payment_voucher_draft(vendorInvoiceId)⇒lines inherited→post⇒**pin JE = D3(c)** (Dr 2110/Cr 2152/Cr 1120).
   → `Purchase_chain_settles_vi_with_our_wht_pins_D3c_je`.
8. [x] **Guard negatives:** VI-from-PO on a Draft PO → error; PV-from-VI on a Draft VI → error.
   → `Vi_from_po_rejects_a_draft_po`, `Pv_from_vi_rejects_a_draft_vi`.
9. [x] **Tenancy:** `get_sales_order`/`list_sales_orders` scope to caller company; cross-company id → null (mirror `Mcp_list_invoices_and_delivery_orders_scope_to_caller_company`).
   → `Get_sales_order_and_list_sales_orders_scope_to_caller_company` (2-tenant service-layer
   proof of the same RLS/EF-filter tenant isolation the MCP tool delegates to — simplification
   flagged: not re-run through the full MCP client transport, since the tools are direct
   passthroughs to `ISalesOrderService`).
10. [x] **get_document_status** now resolves `sales-order`/`delivery-order`/`billing-note` (anti-enumeration scope preserved).
    → `Mcp_get_document_status_resolves_sales_order_delivery_order_billing_note`. **Flagged
    deviation:** these 3 new types are TENANT-scoped only (no `CreatedViaApiKeyName` column
    exists on SalesOrder/DeliveryOrder/BillingNote — adding one was out of the authorized
    schema blast radius for this cycle). Not a new exposure vs. today's `get_sales_order`/
    `get_delivery_order`/`get_invoice` (already tenant-wide, not owner-scoped) — flagged for
    Fable's explicit call; a future cycle could add the column if the anti-enumeration
    guarantee needs to be uniform across all 9 types.
11. [x] **Guide content:** `get_workflow_guide` on the VAT fixture contains "ใบกำกับภาษี"; on the non-VAT fixture contains the ม.86/4 warning and NOT "ใบกำกับภาษี".
    → `Mcp_get_workflow_guide_matches_company_vat_mode`. **Assertion refined:** the non-VAT
    guide's ม.86/4 WARNING legitimately names "ใบกำกับภาษี" once (explaining the company
    cannot issue one) — the test instead asserts the VAT-only action phrase
    ("ระบบออกเป็นใบกำกับภาษี") is absent, which is the precise non-VAT marker.
12. [x] **DraftCreated shape:** every create tool returns non-empty `approvalLinkMarkdown` (extend the smoke test).
    → `New_create_tools_return_nonempty_approval_link_markdown` (new tools) +
    `Mcp_create_quotation_draft_returns_id_and_approval_url` extended in `McpServerSmokeTests.cs`
    (existing tools).
13. [x] **FE:** component/e2e for the SO button branch (D9). — the e2e diff was ALREADY present
   in the worktree (uncommitted, from the dead worker's session) when the finishing pass
   started; verified by reading `frontend/e2e/quotation-chain-flow.spec.ts`'s diff: the
   existing "goods SO" test now asserts `so-create-do` visible / `so-create-invoice` absent,
   plus a NEW test `service-only SO shows Create Invoice, not Create Delivery Order` exercising
   the opposite branch end-to-end (quote → accept → convert → post → assert `so-create-invoice`
   visible/`so-create-do` absent → click it → lands on `/invoices/:id` or `/tax-invoices/:id`).
   `pnpm tsc --noEmit` and the FE unit suite are green (see §F). The Playwright suite itself
   was NOT executed live in this finishing pass (needs a running backend+frontend+seeded
   login, out of this dispatch's named gates — tsc + FE unit tests only); flagging for the
   orchestrator's live smoke-test step alongside the D9 button click-through.

**§B-addition tests (Ham 2026-07-13 mid-cycle):**
- [x] BN draft from DO + from service-only SO (VAT fixture) →
  `Billing_note_draft_from_do_and_service_only_so_on_a_vat_company`.
- [x] TI-from-BN draft + dedup → `Tax_invoice_draft_from_billing_note_and_dedup_guard`.
- [x] VAT-receipt-vs-BN money guard →
  `Vat_company_receipt_against_a_billing_note_is_blocked_must_settle_the_ti_instead`.
- [x] Guide contains the optional step → covered in
  `Mcp_get_workflow_guide_matches_company_vat_mode` (asserts `create_billing_note_draft`
  mentioned in the VAT guide).

### D9. FE — smallest diff (SO action-button rule)

Sibling pattern to mirror: the DO detail page's two mutually-exclusive boolean-gated buttons
(`frontend/app/(dashboard)/delivery-orders/[id]/page.tsx:73-84`).
1. `frontend/lib/types.ts:1002-1007` — add `deliveryRequired: boolean;` to `SalesOrderDetail`.
2. `frontend/app/(dashboard)/sales-orders/[id]/page.tsx:85-89` — replace the single unconditional
   `status === 'Posted'` "Create Delivery Order" (`so-create-do`) with:
   `{d.status === 'Posted' && (d.deliveryRequired ? <so-create-do button> : <so-create-invoice button testid="so-create-invoice">)}`.
   The `so-create-invoice` handler mirrors the existing quotation "create TI" link pattern
   (`quotations/[id]/page.tsx:165`) or a mutation to the new invoice route — implementer's call, minimal.
3. `frontend/messages/en.json` + `th.json` — add `salesOrder.createInvoice` (the `deliveryOrder`
   namespace already has the string at `en.json:1581` to copy).
4. Extend `frontend/e2e/quotation-chain-flow.spec.ts` to assert `so-create-invoice` visible for a
   service-only SO and `so-create-do` for a goods SO. No BFF change (verbatim proxy pass-through).

**Status:** [x] DONE — all 4 items present in the worktree diff: `types.ts` `deliveryRequired`,
the `sales-orders/[id]/page.tsx` boolean-gated button pair (mirrors the DO-page sibling
pattern exactly), `en.json`/`th.json` `createInvoice` strings, and the e2e spec extension
(D8 #13 above). ALSO found and wired: a new REST endpoint
`POST /sales-orders/{id}/create-invoice` (`SalesChainEndpoints.cs`) + FE mutation
`useCreateInvoiceFromSalesOrder` (`queries.ts`) — the web UI can't call MCP tools, so the
button needs its own polymorphic (VAT/non-VAT) REST route reusing the exact same
`ITaxInvoiceService.CreateFromSalesOrderAsync`/`IBillingNoteService.CreateFromSalesOrderAsync`
service methods the MCP tool calls (zero duplicated logic — this endpoint is a thin wrapper
too). `docs/rbac/endpoint-permission-map.generated.md` regenerated accordingly (Perm 287→288,
TOTAL 341→342) — was sitting unstaged in the worktree, left as-is for the orchestrator to
stage alongside the rest.

## E. Out of scope (documented limitations)

- Partial delivery/partial billing/partial payment (v2)
- Credit/Debit notes via MCP; voids
- Push notifications to agents; human notification channels (LINE/email)
- Hiding TI tools from non-VAT companies at tools/list level (3-layer guide/guard
  approach instead — Ham accepted)
- GRN/goods-receipt concept on purchase side (PO→VI direct)

## F. Gates (implementation phase; expand per D8)

- [x] Full backend suite green, skip count vs baseline (8). Evidence (finishing-pass re-run,
      after the audit fix below): `dotnet test` (full solution) — 992 total / 984 passed /
      0 failed / 8 skipped. Reconciles exactly: baseline 961 passed + this cycle's 23 new =
      984; 969 baseline total + 23 new = 992. Ran TWICE (once before, once after the
      `ApprovalDocLabels` fix) — both green, no flake surfaced either time (the WhtBatchExport
      period-collision flake the dead worker saw on their run did NOT reproduce here; noted
      in troubles-wiki.md as "also seen on" for the next person). Also re-ran the `Mcp`-filtered
      subset alone after the fix: 170/170 green.
- [x] All new integration tests incl. §C4 JE pins — 23/23 green in
      `McpDocumentChainTests.cs` (see D8 evidence above).
- [x] RbacEndpointInventory/AuthMap green — `Rbac*`/`McpScopes*` filter run: 43/43 passed
      (includes `McpScopesTests`, `RbacAdminServiceTests`, etc.); no RbacEndpointInventory/
      AuthMap change was needed per D7 (MCP tools are ApiKeyOnly-excluded).
- [x] FE: tsc + existing FE test suite + the button rule covered by a test if the
      repo pattern has one for sibling buttons. Evidence: `corepack pnpm run typecheck`
      (`tsc --noEmit`) → 0 errors. `corepack pnpm exec vitest run lib` → 4 test files / 27
      tests, all passed (repo's unit-test suite; `frontend/` has no dedicated component-test
      harness — see troubles-wiki footgun on scoping the vitest run away from the Playwright
      e2e/manual specs, which the default glob otherwise collects and hangs on in watch mode).
      Button rule covered by the extended `quotation-chain-flow.spec.ts` e2e spec (D8 #13) —
      not executed live this pass (needs running servers; out of this dispatch's named gates).
- [ ] Fable diff review (never skips §C code) → Opus Tier-2 (money) → Tier-3 gate — pending
      (orchestrator-owned, after this dispatch returns).
- [ ] Post-deploy: real MCP chain E2E at Repttown with BUTEST entities: Q→SO→(skip DO,
      service)→IV → approve by Ham → RC settle; purchase PO→VI→PV likewise — pending,
      out of this dispatch's scope (post-deploy verification, orchestrator/Ham-owned).

## Attempt log
- 2026-07-13 spec §A/B/C skeleton (Fable, from Ham consult)
- 2026-07-13 §D (D1–D9) filled by opus-designer. Resolved every §C3 unknown FROM CODE:
  receipt AR-settlement + PV AP-settlement JEs ALREADY EXIST (GlPostingService
  PostReceiptAsync :113-132 keys Cr-account on application type; PostPaymentVoucherAsync
  :161-172 keys Dr AP on VendorInvoiceId) — MCP adds ZERO new posting logic, only thin
  wrappers + 5 new "from-source" line-inheritance builders + guards. Two crux flags raised
  for Fable in §D.0: (1) `create_invoice_draft` is polymorphic by VAT mode (TI for VAT,
  BillingNote for non-VAT) — forced by EnsureVatRegisteredAsync + rc.non_vat_no_ti; a §B
  contract clarification, alternative documented. (2) §C2 code correction: PV WHT credits
  2152 only (2153 = payroll PIT). Purchase-side footgun found: PurchaseOrderLine has NO
  ExpenseCategoryId, so PO→VI inheritance needs a caller-supplied expenseCategoryId.
  Awaiting Fable review of §D + CRUX-1 ruling before dispatch.
- 2026-07-13 Fable design review: CRUX-1 polymorphism ACCEPTED; dispatched to sonnet
  implementer with §D as approved design.
- 2026-07-13 (sonnet, backend phase) — implemented in a fresh worktree
  (`Z:\temp\claude\wt-mcp-chain`, branch `feat/mcp-document-chain`) off `origin/main`:
  - Schema: ONE EF migration `McpDocumentChain` (`20260713032419_McpDocumentChain.cs`) —
    additive nullable columns only: `billing_notes.sales_order_id`,
    `tax_invoices.sales_order_id`, `tax_invoices.delivery_order_id` (+ FKs + partial
    indexes). No data backfill.
  - 5 new "from-source" builders implemented exactly per D1:
    `BillingNoteService.CreateFromSalesOrderAsync`, `TaxInvoiceService.CreateFromDeliveryOrderAsync`,
    `TaxInvoiceService.CreateFromSalesOrderAsync`, `VendorInvoiceService.CreateFromPurchaseOrderAsync`
    (new `CreateViFromPoRequest` DTO), `PaymentVoucherService.CreateFromVendorInvoiceAsync`
    (new `CreatePvFromViRequest` DTO — extended with optional `WhtTypeId`/`WhtRate` beyond the
    original design note, needed to reproduce D3(c)'s WHT line since a VI carries no WHT data
    at all; flagged as a deviation, see below).
  - `SalesOrderDetail.DeliveryRequired` added + computed in `SalesOrderService.GetAsync`.
  - New MCP tools: `create_sales_order_draft`, `create_delivery_order_draft`, `get_sales_order`,
    `list_sales_orders`, `create_invoice_draft` (polymorphic), `get_workflow_guide`. Extended:
    `create_receipt_draft` (+`invoiceId`/`whtTypeId`/`whtBaseAmount`), `create_vendor_invoice_draft`
    (+real `purchaseOrderId` inheritance mode via new `ExpenseCategoryId` field on
    `CreateVendorInvoiceRequest`), `create_payment_voucher_draft` (+real `vendorInvoiceId`
    inheritance mode), `get_document_status` (+sales-order/delivery-order/billing-note).
    `DraftCreated`/`PendingApprovalItem` gained `ApprovalLinkMarkdown` at all 14 sites.
  - Mid-cycle scope addition (Ham, ~10:00, relayed by the orchestrator) folded in: new tool
    `create_billing_note_draft`; `create_tax_invoice_draft` extended with optional
    `billingNoteId`; dedup guard `bn.ti_exists`; MONEY GUARD `rc.vat_co_no_bn_settle` added to
    `ReceiptService`; guide text updated. See the §B ADDITION block above for full detail.
  - **Deviation flagged loudly (Fable: please confirm at diff review):** the D5 "no active DO
    for this SO" guard was FIRST added to the shared `SalesOrderService.CreateDeliveryOrderAsync`
    (matching a literal reading of D5's table), which BROKE 2 pre-existing partial-delivery
    tests (`Sprint10ChainTests.Partial_delivery_keeps_so_open_until_fully_delivered`,
    `ImmutabilityAndGuardTests.DeliveryOrder_exceeding_so_line_qty_is_rejected`) — caught by the
    full-suite gate, NOT by the new test file (which only exercises the MCP-created full-qty
    path and would never have noticed). Reverted and re-implemented in the MCP TOOL layer
    (`create_delivery_order_draft`) instead, where "no 2nd DO" is actually true. Full suite
    re-run green after the fix (see §F evidence). Lesson: a shared-service guard must be
    checked against every EXISTING caller of that service method, not just the new one.
  - **Other flagged deviations:**
    1. `get_document_status`'s 3 new types (sales-order/delivery-order/billing-note) are
       tenant-scoped only, not per-API-key-owner-scoped (no `CreatedViaApiKeyName` column on
       those 3 entities — out of the authorized schema blast radius). See D8 #10 note.
    2. `CreatePvFromViRequest` gained optional `WhtTypeId`/`WhtRate` fields beyond the design's
       literal text, to let an agent attach OUR withholding when creating a PV from a VI (the VI
       itself carries no WHT data) — required to reproduce D3(c)'s worked example end-to-end via
       the new tool. Falls back to the category default `WhtTypeId` exactly like the existing
       `CreateDraftAsync`'s own `input.WhtTypeId ?? category.DefaultWhtTypeId`.
    3. `M4aDraftCreatedViaApiKeyTests.cs` (named in D6 for a shape-assertion update) tests
       `CreatedViaApiKeyName` stamping via services directly and never touches the `DraftCreated`
       MCP shape — no change was needed there; flagging as a doc-citation mismatch, not a gap.
  - Gates run: full `Accounting.Api.Tests` (845 total) + `Accounting.Domain.Tests` (147/147) +
    targeted `Rbac*`/`McpScopes*` filter (43/43). See §F for full evidence.
  - FE phase (D9) not yet started — backend gate confirmed green first, per dispatch order.
- 2026-07-13 (sonnet, FINISHING PASS — worker died before the final gate) — worktree
  `Z:\temp\claude\wt-mcp-chain`, branch `feat/mcp-document-chain`. Audited the dead worker's
  uncommitted work against §B/§D/D8/§B-ADDITION in full (git diff --stat, then a
  file-by-file read of every service/domain/migration/MCP-tool/FE diff):
  - **Everything the dead worker's log claimed was actually present**, including the FE (D9)
    diff, which the attempt log said was "not yet started" but was in fact fully implemented
    and uncommitted (button branch, e2e spec extension, i18n strings, `deliveryRequired`
    type, AND a REST endpoint `POST /sales-orders/{id}/create-invoice` the log never
    mentioned by name — the web UI's route for the same polymorphic invoice-creation the MCP
    tool does). Concluded the process died between finishing FE work and updating its own
    log/spec checkboxes, not before starting it.
  - **One genuine gap found and fixed:** `ApprovalDocLabels` (`TeasMcpTools.cs`, §A6/D6) was
    missing an entry for the `"invoices"` route — the BillingNote/Invoice (ใบแจ้งหนี้) route
    used by `create_billing_note_draft` and by `create_invoice_draft`'s non-VAT branch. Both
    fell back to `(route, route)`, i.e. the raw English word "invoices" as both label and
    prefix, instead of a Thai label — violating §A6's "ready-made Thai-labeled markdown link
    per doc type." D8 test #12 only asserts non-empty, so it never caught this. Fixed: added
    `["invoices"] = ("ใบแจ้งหนี้", "IV")`. No test asserted the old fallback text, so the fix
    is safe; confirmed via a full re-run (below) plus a checked read of every
    `approvalLinkMarkdown` assertion in `McpDocumentChainTests.cs` (none pin exact text for
    this route).
  - **Build:** `dotnet build` — 0 warnings / 0 errors, both before and after the fix.
  - **Full backend suite:** ran twice (`TEAS_TEST_PG`+`TEAS_REPO_ROOT` set inline, foreground,
    polled to completion) — before the fix and after. Both runs: 992 total / 984 passed /
    0 failed / 8 skipped (147 Domain + 845 Api). Reconciles exactly vs the 961/0/8 baseline +
    23 new tests. The WhtBatchExport period-collision flake the dead worker's single run hit
    did not reproduce in either of my two runs; added a troubles-wiki.md note under the
    existing entry for the next person who does hit it. Also ran the `Mcp`-filtered subset
    alone post-fix: 170/170 green.
  - **FE gate:** `corepack pnpm run typecheck` (no bare `pnpm` on PATH here — must go through
    corepack) → 0 errors. FE unit suite: first attempt via `corepack pnpm run test -- --run`
    silently sat in watch mode for many minutes producing zero output (root-caused: the
    `--` gets forwarded LITERALLY into the script's argv on this pnpm/corepack combo, so
    vitest actually received `-- --run lib` as file-path-like args and never engaged `--run`
    mode) — killed it, documented the footgun in troubles-wiki.md, re-ran as
    `corepack pnpm exec vitest run lib` (bypasses `pnpm run` argv mangling AND scopes past
    the pre-existing Playwright-spec-collision footgun already in the wiki): 4 files / 27
    tests, all green.
  - **Spec checkboxes updated:** D8 #13, D9 Status, and the two remaining §F backend/FE gate
    lines flipped `[ ]` → `[x]` with evidence (this entry + the sections above). §F's Fable
    review / Tier-2 / Tier-3 / post-deploy lines correctly remain `[ ]` — orchestrator-owned,
    not this dispatch's.
  - No other gaps found across D1–D9 on a full read of every touched file's diff (services,
    domain entities, migration, EF configs, MCP tool file, DTOs, scopes, server instructions,
    FE). §D deviations already flagged by the dead worker (DO-guard placement, PV-from-VI
    optional WHT fields, `get_document_status`'s 3 new types being tenant- not key-scoped)
    were independently re-verified against the actual code and confirmed accurate as described.

## Post-review notes (Tier-2 Opus APPROVE, 2026-07-13)
- F1 (pre-existing, NOT this cycle): ReceiptService's DeliveryOrderId-application branch
  has no VAT-mode guard (unlike the new BN branch) — a VAT co settling directly against a
  DO would Cr Sales without output VAT. Unreachable via any MCP surface; close
  symmetrically in a future cycle.
- F4 (documented limitation): PV-from-VI WHT is opt-in; an omitted WHT on a
  WHT-liable VI yields an under-withheld DRAFT — human approve gate mitigates.
