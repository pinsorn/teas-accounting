# Wave B-rc — ภ.พ.36 + ภ.ง.ด.54 reverse charge (ม.83/6), co5, prod v1.22.10

Agent: sonnet (browser/Playwright, headless). Target: https://teas.kazaki-rio.com, company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด) ONLY. Vendor: `ARMYAWS859829` (Amazon Web Services, Inc., US,
foreign, no Thai VAT-D — created in Wave A1). Logins used: ap01 (create/post VI, create PV draft),
admin01 (approve+post PV — ap01/AP_CLERK has no `purchase.payment_voucher.approve`, B2 SoD by
design; mirrors A1's admin01-fallback pattern), tax01 (tax-filing preview + PDF).

## Done
- [x] Created + posted a SERVICE Vendor Invoice (VI #14, `07-2026-VI-0004`) on the foreign vendor:
      line "AWS cloud hosting services", ฿20,000.00, VAT 0% (foreign vendor charges no Thai VAT —
      self-assessed separately), category "ค่า IT / Cloud / Software (IT)" — co5's real seed has no
      dedicated "SVC" category (that's a different fixture set than
      `frontend/e2e/foreign-vendor-aws.spec.ts` uses); IT is the closest real match for AWS hosting.
      `RequiresPnd36ReverseCharge` correctly auto-derived server-side (foreign + no Thai VAT-D).
- [x] Created a Payment Voucher (PV #17, `07-2026-PV-IT-0001`) settling that VI via the
      "ชำระด้วยใบสำคัญจ่าย" CTA, with WHT type "ค่าบริการ ต่างประเทศ (FOR-SVC, 15%, ภ.ง.ด.54)"
      selected on the line. Approved by admin01.
- [~] **Posting the PV FAILED — 422 `gl.unbalanced`** (see Findings). PV is stuck at status
      "อนุมัติแล้ว" (Approved), never reached Posted. This blocked the WHT-certificate/ภ.ง.ด.54 half
      of the mission — documented as a finding below rather than worked around (retrying would only
      reproduce the same deterministic bug and burn more of the ≤6-document cap).
- [x] ภ.พ.36 (`/tax-filings/pnd36`) previewed as tax01 for period 2026-07: VI-only row present,
      total ฿20,000.00 / ฿1,400.00 VAT — matches hand-calc exactly. JV-disclosure note shown
      ("ปิดงวดจะตั้งบัญชี Dr 1170 ภาษีซื้อ / Cr 2151 ภาษีขายค้างจ่าย (สุทธิ = 0)"). Did **not**
      Finalize (HARD RULE 2 forbids ยืนยัน/ปิดงวด any filing) — the actual JE only posts at
      Finalize time, so it was never created this run (see Unbuilt-vs-untested).
- [x] ภ.ง.ด.54 (`/tax-filings/pnd54`) previewed as tax01 for period 2026-07: **0 rows / ฿0.00**
      — direct, expected consequence of the PV post failure (no WhtCertificate was ever created).
- [x] PND54 PDF downloaded via the authenticated `/api/proxy/tax-filings/pnd54/pdf` endpoint (the
      FE's `openPdf()` opens a blob URL in a new tab, which is awkward to capture headless — fetched
      the same cookie-authed request directly instead): `swarm-findings/army/pdfs/B-rc-pnd54.pdf`
      (283,294 bytes; header-only/zero-row PDF, consistent with the empty preview).
- [x] **No PND36 PDF exists to save** — confirmed by code search (`backend/src/Accounting.Api/
      Endpoints/TaxFilingEndpoints.cs` has a `pnd54/pdf` route only; no `pnd36/pdf` anywhere in
      backend or frontend). Classified Unbuilt, not a bug. Closest available artifact is the
      preview screenshot/text dump, saved instead.
- [x] Tenant-leak check (co2/co3/เรปทาวน์/พงศ์สันต์/repttown strings) on every screen visited
      (VI detail ×2 logins, PV draft, PV posted, pnd36 preview, pnd54 preview): **clean**, no hits.
- [x] Blast-radius cap respected: **2 documents created** (VI #14, PV #17 — the PV never reached
      Posted), well under the ≤6 cap.
- [x] Temp script `frontend/army-B-rc.mjs` deleted after the run.

## Evidence
- VI form filled (pre-post): `B-rc-01-vi-form-filled.png`
- VI posted detail (ap01): `B-rc-02-vi-posted.png`
- PV form pre-filled from VI, WHT type picked (pre-save): `B-rc-03-pv-form-prefilled.png`
- PV draft created: `B-rc-04-pv-draft.png`
- **PV post FAILED — raw `gl.unbalanced` toast visible**: `B-rc-05-pv-posted.png`
- ภ.พ.36 preview (correct): `B-rc-06-pnd36-preview.png`
- ภ.ง.ด.54 preview (empty — consequence of the PV failure): `B-rc-07-pnd54-preview.png`
- Full console/action log: `B-rc-run-log.txt`
- Network log (mutating calls only, with status codes): `B-rc-network-log.txt` — note line 5:
  `POST .../payment-vouchers/17/post -> 422`
- PV pre-submit preview page text (base/VAT/WHT breakdown): `B-rc-pv-preview-text.txt`
- ภ.พ.36 / ภ.ง.ด.54 preview page text dumps: `B-rc-pnd36-preview-text.txt`, `B-rc-pnd54-preview-text.txt`
- PND54 PDF: `pdfs/B-rc-pnd54.pdf`

## Hand-calc table

**ภ.พ.36 — VAT self-assessed on the reverse-charge service, 7%**

| | Expected (hand-calc) | Shown on form | Shown on GL |
|---|---|---|---|
| Base (VI subtotal) | ฿20,000.00 | ฿20,000.00 (VI + pnd36 preview) | matches |
| VAT 7% | ฿20,000.00 × 7% = **฿1,400.00** | **฿1,400.00** (pnd36 preview, single row, VI only) | **Not posted** — Finalize is forbidden by HARD RULE 2; the Dr 1170/Cr 2151 JV only fires at Finalize time (`WhtFilingService.PostReverseChargeJvAsync`), so no JE exists to check this run. Preview correctly *discloses* the JV it would post. |

**Result: ภ.พ.36 MATCHES hand-calc exactly.** No double-counting from the PV side either (the PV
never reached Posted, so `GeneratePnd36Async`'s PV-row query correctly excludes it — the VI-only
total of ฿1,400.00 is correct either way).

**ภ.ง.ด.54 — WHT self-withheld (gross-up-forever) on ม.70 foreign service, 15%**

| | Expected (hand-calc, correct base) | Shown on PV preview (as computed) | Shown on ภ.ง.ด.54 / GL |
|---|---|---|---|
| Base | ฿20,000.00 (= VI total, since VI VAT is 0%) | **฿18,691.59** (wrong — see Finding 2) | n/a — PV never posted |
| Gross-up income (net/(1−0.15)) | 20,000/0.85 = **฿23,529.41** | 18,691.59/0.85 = ฿21,990.11 (not directly shown; derived) | n/a |
| WHT 15% | 23,529.41 × 15% = **฿3,529.41** | **฿3,298.52** ("ภาษีออกให้เอง" line, computed off the wrong base) | n/a — no WhtCertificate created, ภ.ง.ด.54 total = **฿0.00** |

**Result: ภ.ง.ด.54 does NOT match hand-calc — MISMATCH, HIGH.** Expected ฿3,529.41 WHT to remit;
actual filing shows ฿0.00 because the settling PV crashed on POST (Finding 1) before a
WhtCertificate could ever be created. Even the pre-crash preview figure (฿3,298.52) is itself
wrong versus the correct-base hand-calc (Finding 2) — two independent bugs compound here.

## Findings (severity + repro)

### Finding 1 — CRITICAL: settling a foreign self-withhold vendor's VI via PV always crashes GL posting (422 `gl.unbalanced`, raw diagnostic shown to user)
**Repro**: co5, any foreign vendor with no Thai VAT-D (auto self-withhold). Post a Vendor Invoice
for it. From the posted VI, click "ชำระด้วยใบสำคัญจ่าย" (settle with PV). Pick any WHT type with
rate > 0 on the line (required to get a real WHT figure — the whole point of this flow). Save
draft → Approve → Post. Post returns **HTTP 422**, and the UI surfaces a raw, un-localized toast:
`GL post unbalanced: D=20000.0000 C=23298.5200 for PV 07-2026-PV-IT-0001.` The PV is left stuck at
"Approved" — Activity Log shows only Created/Approved, no Post entry.

**Root cause** (`backend/src/Accounting.Infrastructure/Ledger/GlPostingService.cs`, method
`PostPaymentVoucherAsync`, lines 162–217): the JE-building logic has two branches on
`pv.VendorInvoiceId`:
- `else` (standalone PV, no linked VI, lines 174–210): if `pv.SelfWithholdMode && pv.WhtAmount > 0`,
  an extra **Debit** line "Self-withhold gross-up" for `pv.WhtAmount` is added (booking the
  absorbed tax as extra expense) — this is what keeps the JE balanced under self-withhold.
- `if (pv.VendorInvoiceId is not null)` (VI-settlement path, lines 162–173): only books
  `Dr AP = pv.SubtotalAmount + pv.VatAmount`. **The self-withhold gross-up debit line is never
  added on this branch** — but the WHT-payable credit (line 211–217, unconditional) and the
  Cash/Bank credit (= `pv.TotalPaid`, which under self-withhold equals subtotal+VAT, i.e. does
  NOT net off the WHT) both still fire. Credits therefore exceed Debits by exactly `pv.WhtAmount`
  every single time. Confirmed arithmetically against the actual error: D=20,000.00
  (=18,691.59+1,308.41, our AP-clear amount) vs C=23,298.52 (=3,298.52 WHT payable + 20,000.00
  cash); difference = 3,298.52 = exactly the WHT amount.

**Impact**: since a foreign-no-Thai-VAT-D vendor's self-withhold is **auto-locked ON** (cannot be
turned off), this is not an edge case — it is the **only** way `purchase.payment_voucher.*` roles
can settle such a VI through the standard UI flow, and it **always** 422s whenever the PV carries
any WHT (which is required to get a real ภ.ง.ด.54 filing at all). This makes the entire
"pay a foreign reverse-charge vendor + file ภ.ง.ด.54" pipeline unusable via VI→PV settlement on
prod today. (A standalone PV, per `foreign-vendor-aws.spec.ts`, hits the `else` branch and is
unaffected — that's a workaround, but it skips the VI/AP trail entirely.)

### Finding 2 — HIGH: "settle with PV" mis-derives VAT rate for a foreign no-VAT-D vendor, corrupting the base/VAT split
**Repro**: same setup as Finding 1. On `frontend/app/(dashboard)/payment-vouchers/new/page.tsx`
line 159, `const vendorVat = vendor?.vatRegistered ?? true;` drives the line's derived VAT rate —
using only the vendor's general `vatRegistered` flag. Elsewhere in the same codebase (VI's own
create form, `frontend/app/(dashboard)/vendor-invoices/new/page.tsx` lines 85–87, and the backend
ม.82/5 guard) the correct check is the **dual-flag** one: VAT can't apply when
`!vendor.vatRegistered || (vendor.isForeign && !vendor.hasThaiVatDReg)`. Our AWS vendor has
`vatRegistered=true` (apparently a DB default from vendor creation — nothing in
`foreign-vendor-aws.spec.ts`'s `createForeignVendor()` helper ever sets it) but
`hasThaiVatDReg=false` (foreign, no Thai tax id — the flag that actually governs ม.82/5). Result:
the PV pre-fill wrongly applied 7% VAT and back-split the VI's clean ฿20,000.00/0%-VAT total into
a fabricated base ฿18,691.59 + "VAT" ฿1,308.41 that the vendor never charged and the VI itself
never carried (VI correctly shows 0% VAT, `hasInputVat=false`). Total net-paid still landed on
฿20,000.00 by construction, so this is easy to miss without checking the base/VAT line split —
but it silently corrupts the AP-clearing subtotal and would flow a wrong base into any
downstream reporting that reads the PV's own `SubtotalAmount` (e.g. AP aging, or a future
ภ.พ.36 pickup from the PV side, which today happens to be moot only because Finding 1 stops the
PV from ever posting).

### Finding 3 — LOW/UX: self-withhold explanation + gross-up mode choice is hidden on the "settle from VI" PV form, even though it's still silently applied
`payment-vouchers/new/page.tsx` line 445 wraps the entire self-withhold toggle/explanation/
gross-up-mode-radio block in `{!fromVi && (...)}` — so when arriving via "ชำระด้วยใบสำคัญจ่าย",
the accountant never sees the "ภาษีออกให้เอง" toggle, the warning that it's auto-locked for this
vendor, or the choice between "ออกให้ตลอดไป" (forever) vs "ออกให้ครั้งเดียว" (once). The backend
still silently applies `GROSS_UP_FOREVER` by default (via `req.SelfWithholdMode/WhtPayerMode`
both being sent `null` and falling back to the same auto-detect + default as the standalone path)
— visible only indirectly, as the "ภาษีออกให้เอง" remit-line total in the totals box. Not a data
bug, but a real transparency gap: the user can't choose or even see which condition box (2 vs 3
on the 50ทวิ) is being applied on this path.

### Not a finding — permission design confirmed working as intended
ap01 (AP_CLERK) can create+post a VI directly (`purchase.vendor_invoice.post` granted) but cannot
approve a PV (`purchase.payment_voucher.approve` is COMPANY_ADMIN/CHIEF_ACCOUNTANT/APPROVER only —
B2 segregation-of-duties, `140_seed_vendor_invoice_prefix_and_pv_approve.sql`). Used admin01 for
that one step, same pattern Wave A1 used. No badge/UX issue — clean permission gate.

## Unbuilt-vs-untested classification
- **Unbuilt**: ภ.พ.36 has no PDF export anywhere (no backend route, no FE button) — confirmed by
  code search across `backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs` and the whole
  backend tree. Only ภ.ง.ด.3/53/54 have `/pdf` routes. Not filed as a bug.
- **Untested (by design, not by failure)**: the actual ภ.พ.36 reverse-charge JE (Dr 1170/Cr 2151)
  only posts at Finalize time (`WhtFilingService.PostReverseChargeJvAsync`), and HARD RULE 2
  forbids finalizing any filing in this leg. The Preview mode correctly *discloses* what it would
  post (verified: the JV note text matches the code comment exactly), but the actual GL entry was
  never exercised this run. A future leg with finalize authority should verify it lands as
  Dr 1170 ฿1,400.00 / Cr 2151 ฿1,400.00.
- **Untested — blocked by Finding 1**: the ภ.ง.ด.54 WHT-certificate → filing → PDF path with a
  real non-zero WHT figure could not be exercised at all, because the only realistic way to
  generate one (settle a foreign self-withhold VI via PV) is the exact path that 422s. This is
  the mission's core untested surface, and it's untested precisely because it's broken — see
  Finding 1. Re-run once Finding 1 is fixed to get real ภ.ง.ด.54 numbers end-to-end.

## Attempt log
- 2026-07-22: leg executed in one pass (~55 min incl. code research to understand the
  VI/PV/ภ.พ.36/ภ.ง.ด.54 wiring before touching the browser — exceeded the ~30 min timebox because
  the reverse-charge/self-withhold interaction across VI, PV, and two tax-filing services needed
  reading before any click could be trusted to mean what it looked like). Script
  `frontend/army-B-rc.mjs` written once, ran clean on the second attempt (first attempt failed
  fast on a wrong expense-category-code assumption ("SVC" doesn't exist on co5's real seed; used
  "IT" instead — zero mutations before the fix, so no wasted documents). Both real documents (VI
  #14, PV #17) created in a single successful run; PV's 422 was the finding itself, not a script
  bug — confirmed root cause by reading `GlPostingService.PostPaymentVoucherAsync` rather than
  guessing, and did not attempt a workaround/retry against the same deterministic bug.
