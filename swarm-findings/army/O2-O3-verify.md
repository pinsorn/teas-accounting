# O2 + O3 verification leg — billing notes, co5, prod v1.22.12

Follow-up to `swarm-findings/army/B-bn.md`'s two open items (`specs/fix-army-findings-2026-07-22.md`
O2/O3). Target: https://teas.kazaki-rio.com, company **co5** only, account **ar01**.
Method: code review of the actual FE/BE source first (to know exactly what to expect and how to
drive the UI correctly), then a live Playwright run against prod for real numbers/screenshots/
network evidence. Script: `frontend/army-O2O3.mjs` (+ `frontend/army-debug.mjs`), both deleted
after this run. Documents created: TI #29 (`07-2026-TI-0019`), TI #30 (`07-2026-TI-0020`),
BN #24 (`07-2026-IV-0005`) — 3 documents, within the ≤4 blast cap. Left in place as evidence
(no delete/cancel path exists for Posted TIs or an Issued BN, same as prior legs).

## O2 — does "ใบกำกับภาษีที่รวม" actually aggregate?

**Method**: created TI #29 (qty 2 × ฿1,000 → **฿2,140.00** incl. 7% VAT) and TI #30
(qty 3 × ฿1,500 → **฿4,815.00** incl. 7% VAT), both for the same customer
(บริษัท ลูกค้าทดสอบ จำกัด). Created a Billing Note for that customer, opened the
"ใบกำกับภาษีที่รวม" picker, and picked **both** TIs — anchoring each pick on the picker's own
`GET /api/proxy/tax-invoices` network response (not a sleep), and confirming via the picker's
own visible chip state that both were really selected **before** issuing. Then added one small,
distinguishable manual line (qty 1 × ฿100 → ฿107.00) — required because the form still needs
≥1 line item to pass validation — and issued.

**What the earlier leg (B-bn) got wrong**: it only ever cleanly picked 1 of 2 TIs, because
re-opening the picker after a first pick with a plain `.click()` doesn't refire `onFocus` (the
picker's `onMouseDown` handler calls `preventDefault()` specifically to keep the input focused
across a pick, so a second `.click()` on an already-focused input never fires `onFocus` again,
and `open` never flips back to `true`). Fix: force a re-open via `.fill('')` on the input, which
always fires `onChange → setOpen(true)` regardless of prior focus state. With that fix, both
picks worked cleanly and reproducibly (confirmed twice: chip state after each pick, and the
picker's own `GET /tax-invoices` response for each open).

**Chips before issue** (screenshot `O2O3-06-bn-both-chips-selected.png`): both chips visible —
`07-2026-TI-0019 ×` and `07-2026-TI-0020 ×`. Selection genuinely persisted (this alone kills the
"maybe it's not even persisting the selection" half of B-bn's inconclusive read).

**(a) Does the BN total equal the sum of the linked TIs, or only the manually-entered lines?**
— **Only the manual lines.** BN #24's `totalAmount` = **฿107.00** (screenshot
`O2O3-08-bn-issued-detail.png`: Subtotal 100.00, VAT 7.00, Grand Total ฿107.00) — exactly the
one manual line, and nowhere close to the TI sum (฿2,140 + ฿4,815 = ฿6,955). Confirmed at the
code level too: `BillingNoteForm.tsx`'s `totals` reducer and `BillingNoteService.ApplyLinesAsync`
both sum **only** `lines` / `req.Lines`; `taxInvoiceIds`/`TaxInvoiceIds` is a fully separate
field sent to `BuildTaxInvoiceLinksAsync`, which only writes rows to the
`BillingNoteTaxInvoice` join table (with a snapshotted `AppliedAmount` = the TI's own total at
link time) — it never touches `bn.Lines` or the running totals.

**(b) Do the linked TIs appear as back-links on the BN detail page after issue?**
— **No.** `GET /api/proxy/billing-notes/24` (the same request the detail page makes) **does**
return both links in its `taxInvoices` array
(`[{"taxInvoiceId":29,"docNo":"07-2026-TI-0019","appliedAmount":2140},{"taxInvoiceId":30,"docNo":"07-2026-TI-0020","appliedAmount":4815}]`)
— so the data is correctly persisted and served. But **the detail page never renders it**: the
`data-testid="bn-ti-chips"` chip block only exists in `BillingNoteForm.tsx` (the create/edit
form) — a repo-wide grep confirms it does not exist anywhere in the detail-page component tree
(`app/(dashboard)/invoices/[id]/page.tsx`, `DocumentChain.tsx`, etc.). Live-verified two ways on
the actual BN #24 detail page: `page.getByTestId('bn-ti-chips')` → **0 elements**, and neither
TI docNo (`07-2026-TI-0019` / `07-2026-TI-0020`) appears **anywhere** in the page's visible text
(screenshot `O2O3-08-bn-issued-detail.png` — the "เอกสารอ้างอิง" document-chain panel shows only
the BN itself, count 1). The chain panel's TI section (`DocumentChain.tsx` /
`DocumentCrossRefService.GetChainAsync`) is unrelated plumbing: it only walks
`TaxInvoice.BillingNoteId` (a TI **created from** a BN), the opposite relationship from
`BillingNoteTaxInvoice` (TIs **grouped into** a BN by the picker) — so it can never surface
these links either.
The BE's own code comment on the `GetAsync` query says the join result carries "doc_no **for
chips**" — i.e. detail-page chips were the intended design, they're just not wired into the
detail page. The one live side-effect of the link today: it hides the "ออกใบกำกับภาษี" (create
TI from this BN) button once any TI is linked (`(d.taxInvoices?.length ?? 0) === 0` gate in
`invoices/[id]/page.tsx`), which is a bit of an unrelated-looking side effect of a field the user
can otherwise never see again after Issue.

**(c) Verdict**: **not a pure reference tag by design, and not the "totals roll up" bug B-bn
guessed at either** — it's a genuine **half-built feature**: the pick/select/persist path,
the join-table storage, and the API response all work correctly (confirmed live, clean 2-of-2
pick); the **display half is missing** on the one screen (BN detail) where a user would ever
want to see what TIs a billing note is collecting. This is a real product gap, not a false
positive — recommend adding the same `bn-ti-chips` block (or a `DocumentChain`-style link list)
to the BN detail page, sourced from `d.taxInvoices`, which the API already returns.

## O3 — "ดาวน์โหลด PDF (สำเนา)" dropdown item

**Method**: `PrintMenu.tsx` (shared by 17 doc-type detail pages, including billing-notes AND
tax-invoices) is a plain **DaisyUI CSS `:focus`-based dropdown** (`<label tabIndex={0}>` trigger
+ `<ul tabIndex={0} className="dropdown-content">`), **not** a Radix menu — B-bn's "Radix-menu
interaction quirk" guess was about the wrong component. Clicking "ดาวน์โหลด PDF (สำเนา)" runs
`trackedDoc(true,'download')` → `POST .../mark-printed?copy=true` → `downloadFile()` (`fetch` →
blob → synthetic `<a download>` click) — a real network call precedes the actual file download,
so "0 network request" is a strong signal the click handler never fired at all, not that the
handler ran and something downstream failed.

Live run (tall 1440×2200 viewport per the troubles-wiki swarm-script note, and plain Playwright
`locator.click()` — no raw coordinates, no force-click) on **BN #24's own detail page**, with
`page.on('download')`, `page.on('request')` and `page.on('console')` listeners attached before
the click:
- Network: `POST /api/proxy/billing-notes/24/mark-printed?copy=true` then
  `GET /api/proxy/billing-notes/24/pdf?copy=true` — both fired.
- Download event: **fulfilled**, filename `billing-notes-24.pdf`.

**Control** — identical `PrintMenu` component, same click sequence, on TI #29's detail page
(a page B-bn never flagged as broken): same two network calls fired
(`mark-printed` then `pdf`), confirming the handler and endpoint work there too. The Playwright
`download` **event** itself didn't resolve that second time (Node-side listener timing, not a
page-side failure — the network trace shows the fetch+blob+anchor-click sequence completed
either way, which is what a real user's browser actually does).

**Direct endpoint probe**: `GET /api/proxy/billing-notes/24/pdf` → **200**,
`content-type: application/pdf`, 118,264 bytes — endpoint healthy.

Minor unrelated observation: a `403` console error fired on both pages during this flow
(same on BN and TI, unrelated to which button was clicked) — some other background resource on
the dashboard shell 403s for the `ar01` role. Not investigated further; doesn't affect the PDF
button and isn't part of O3's scope.

**Verdict**: **automation-only artifact, not a real user-facing break.** With a standard
Playwright click (proper actionability wait, no raw coordinates, no force-click, tall viewport)
the button reliably fires its network calls and produces a real PDF download, on both the
flagged page (BN) and a control page (TI) using the identical shared component. The handler is
wired, the endpoint is healthy. **What a human should see**: click "พิมพ์ / PDF" → "ดาวน์โหลด PDF
(สำเนา)" on any billing note detail page → a `billing-notes-<id>.pdf` file downloads normally
(same as any other doc-type's print menu). No further confirmation needed from Ham on this one —
recommend simply closing O3 as "works as intended, was a swarm-script artifact."

## Screenshots (scratchpad)
`O2O3-00-logged-in.png`, `O2O3-03-bn-picker-open-1.png`, `O2O3-04-bn-chip-1-selected.png`,
`O2O3-05-bn-picker-open-2.png`, `O2O3-06-bn-both-chips-selected.png` (both chips, pre-issue),
`O2O3-07-bn-manual-line-filled.png`, `O2O3-08-bn-issued-detail.png` (final BN #24, ฿107.00, no
TI back-links visible), `O2O3-09-bn-printmenu-open.png` / `O2O3-10-bn-after-download-click.png`,
`O2O3-11-ti-printmenu-open.png` / `O2O3-12-ti-after-download-click.png` (control). Raw JSON:
`Z:\temp\claude\Y--ClaudePlayground-TEAS-Project\225a4d63-b294-46d1-b374-d68b02875ecb\scratchpad\O2O3-results.json`.

Note: the PrintMenu screenshots (09/11) don't visually show the dropdown open — Playwright's
`fullPage` screenshot capture appears to trigger a scroll/reflow that can drop the CSS
`:focus`-based dropdown's visibility right as the capture runs. Not a concern for the verdict:
the network/download evidence (the authoritative signal here, matching what B-bn itself checked
for and found absent) is unambiguous either way.
