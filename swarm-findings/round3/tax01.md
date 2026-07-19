# UX Swarm ROUND 3 findings — tax01 (Tax Officer, co5, prod v1.22.6)

Target: https://teas.kazaki-rio.com (v1.22.6, confirmed in page footer) · company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด, companyId=5)
Run: 2026-07-19 ~23:44–23:49 ICT · user tax01 / role TAX_OFFICER (REUSE, not recreated)
Tool: Playwright (chromium/msedge headless) via temp `frontend/swarm3-tax01.mjs` — deleted after run.

## Done
- Login สำเร็จ (attempt 1/1), password `UxSwarm-2026-B1`.
- `GET /api/proxy/me`: `companyId=5`, `companyName="บริษัท ทดสอบ VAT (DUMMY) จำกัด"` — co5 confirmed,
  **no tenant leak**.
- `GET /api/proxy/me/permissions`: role `TAX_OFFICER`, `isSuperAdmin=false`, **14 grants** (up from
  12 in round 2) — the two new grants are exactly the ones round-2 tax01 flagged as missing:
  `tax.filing.preview`, `tax.filing.read`. Full list: `gl.journal.read, master.product.read,
  purchase.wht.read, report.audit.read, report.general_ledger.read, report.profit_loss.read,
  report.trial_balance.read, sys.attachment.read, tax.filing.preview, tax.filing.read,
  tax.pnd3.read, tax.pnd30.read, tax.pnd53.read, tax.vat_register.read`. **No `tax.filing.finalize`.**
- `/reports/pnd30`, period set to July 2026 (`202607`):
  - Clicked "แสดงตัวอย่าง" (preview) → `POST /api/proxy/tax-filings/pnd30?period=202607&mode=preview`
    → **200 OK**. Table rendered on screen with full line items.
  - Clicked "ดาวน์โหลด PDF (ภ.พ.30)" → `GET /api/proxy/tax-filings/pnd30/pdf?period=202607` →
    **200 OK**.
  - Clicked "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" → `GET /api/proxy/tax-filings/pnd30/batch-file?period=202607`
    → **422** (not 403 — see CRIT-verify + Findings below; different root cause).
- Read the rendered July numbers directly off the preview response body (not just the DOM):
  `salesTaxable = 13,000.00 / VAT 910.00`, `purchaseTaxable = 15,000.00 / VAT 1,050.00`,
  `outputVatTotal = 910.00`, `inputVatTotal = 1,050.00`, `netVatPayable = 0.00`, credit carry
  (`max(0, input-output)`) = **140.00**. Cross-checked against `/api/proxy/reports/tax-summary?year=2026`
  → month 7: revenue 13,000 / outputVat 910 / inputVat 1,050 / vatRefundable 140 — **exact match**,
  zero drift observed at the moment of this request (other agents' concurrent writes hadn't touched
  co5's July VAT-relevant docs in the window I sampled).
- Finalize/close-period ("ยืนยัน/ปิดงวด") button: **never clicked** (hard rule 2 — forbidden action,
  no exceptions). Observed via DOM only: `visible=true`, `disabled=false` (it activates once a
  preview exists — round 2 never got this far since preview itself was 403'd, so this is the first
  time the button's real enabled state has been observed). Cross-checked against backend source
  (`TaxFilingEndpoints.cs` lines 12–30): `mode=finalize` is gated **in-handler** on
  `Permissions.Tax.FilingFinalize`, independent of the `tax.filing.preview` policy on the route —
  TAX_OFFICER's grant list (above) does **not** include `tax.filing.finalize`, so a click would 403.
  High-confidence, source-confirmed; **not empirically clicked** per hard rule.
- Confirmed dashboard-widget 403 noise from round 2 (known MED, unrelated to this round's fix) still
  present: `GET /api/proxy/reports/pending-agent-approvals` → 403, `GET /api/proxy/vendor-invoices?
  incompleteOnly=true&limit=100` → 403 — both fire globally regardless of route, same as round 2.
- Console: one 404 (login-page initial paint, same LOW as round 2), the two 403s above, one 422
  (the batch-file finding). Zero `pageerror` events, zero crashes/blank screens.

## CRIT-verify (this round's reason to exist)
**CRIT-2 — RBAC gate on ภ.พ.30 preview/PDF/.txt: CLOSED for preview + PDF, PARTIALLY closed for .txt.**
- ภ.พ.30 **preview**: 2xx — **YES**, opens successfully (200). ✅ CLOSED.
- ภ.พ.30 **PDF export**: 2xx — **YES**, opens successfully (200). ✅ CLOSED.
- ภ.พ.30 **.txt export**: **NOT a 403** (the RBAC/permission gate that defined CRIT-2 is gone —
  `tax.filing.preview` now correctly authorizes the route). However it returns **422**
  (`pp30_batch.missing_address`: *"Company registered address is incomplete; ภ.พ.30 requires:
  เลขที่ (registered house no.). Complete the company profile first."*) — a **data-completeness
  validation**, not a permission denial. This validation was previously **unreachable** in round 2
  because the request never got past the 403. So: the specific CRIT-2 regression (a Tax Officer
  403'd by RBAC) is fixed, but the Tax Officer **still cannot successfully produce the .txt file**
  end-to-end today, for a different, newly-exposed reason. See Findings table — flagged as new HIGH,
  NOT re-flagged as CRIT since the spec's own closure bar ("403 now = NOT closed") is satisfied and
  the blocker is a company-data issue, not an RBAC/security issue.
- July numbers vs baseline (sales 13,000/910): **confirmed exact match**, see Done section above.
- Finalize/close still denied (SoD): **holds, source-confirmed** (`tax.filing.finalize` absent from
  grants, in-handler 403 guard present) — **not empirically clicked**, per hard rule. Button *state*
  changed from round 2 (now visibly enabled once a preview exists, vs previously always-disabled
  because preview never succeeded) — flagged as a minor UX finding below, not a security gap.

## Findings
| severity | area | symptom | repro | screenshot |
|---|---|---|---|---|
| HIGH | ภ.พ.30 → ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt) | RBAC 403 from round 2 is gone, but the endpoint now 422s with `pp30_batch.missing_address` — co5's company profile is missing the registered house-number field required by the RD batch-file format. Tax Officer still cannot produce a usable .txt filing package for July 2026 end-to-end (PDF works as a fallback, .txt does not). This gap was invisible in round 2 because the RBAC 403 fired first and masked it. | login tax01 → `/reports/pnd30` → period 2026-07 → "แสดงตัวอย่าง" (succeeds) → "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" → 422; confirmed via direct `GET /api/proxy/tax-filings/pnd30/batch-file?period=202607` | swarm-findings/shots/round3/tax01-05-pnd30-after-batch-click.png |
| MED (UX, not security) | ภ.พ.30 → "ยืนยัน/ปิดงวด" button | Button is now genuinely clickable (`disabled=false`) for a role with no `tax.filing.finalize` grant, once a preview exists — the backend correctly blocks it in-handler (source-confirmed), but the frontend doesn't hide/disable it based on the user's actual finalize permission, unlike other permission-gated buttons in the app. Cosmetic/UX only (no data at risk since backend enforces), but round 2's assumption "finalize button — never tested if it'd 403" is now testable and the UI affordance is misleading for a role that can never finalize. | login tax01 → `/reports/pnd30` → preview succeeds → observe "ยืนยัน/ปิดงวด" is enabled (NOT clicked, per hard rule) | swarm-findings/shots/round3/tax01-06-pnd30-finalize-button-state.png |
| MED (confirmed still present, unchanged from round 2) | Global dashboard-widget fetches | `GET /api/proxy/reports/pending-agent-approvals` and `GET /api/proxy/vendor-invoices?incompleteOnly=true&limit=100` still fire 403 on every route for tax01 (global/layout-level fetch not gated by permission before firing) — same known MED as round 2, not in this round's fix scope, just re-confirmed still standing. | any page after login as tax01 | swarm-findings/shots/round3/tax01-01-dashboard.png |

## Denied-as-expected
- Finalize/close period: **not attempted** (forbidden by hard rule 2) — SoD conclusion above is
  source-code-corroborated, not click-tested.
- No ยืนยัน/ปิดงวด, no year-end close, no payroll mutation, no master-data edit/delete attempted —
  all read-only + new-preview-only per hard rules.
- Only co5 data touched/observed throughout (verified via `/me` before and after the pnd30 flow).

## Notes for consolidation (Fable)
- **CRIT-2 verdict: RBAC portion CLOSED** (preview 200, PDF 200, both previously 403). The .txt
  export is a **separate, newly-visible HIGH** (company-profile data completeness), not a
  reopened CRIT-2 — recommend a follow-up task to either (a) complete co5's registered address in
  company profile (fastest, playground data fix) and/or (b) give `TaxFilingsIndexPage`/pnd30 page a
  clearer inline error surface for 422s specifically (currently just a generic toast per
  `throwFileResponseError`, easy to miss — screenshot 05 shows no visible toast at capture time).
- Finalize-button UX gap (MED) is cheap to fix if wanted: gate the button's `disabled` state on
  `tax.filing.finalize` presence in `useMePermissions()`, matching the pattern likely used elsewhere
  for permission-gated actions — not urgent since backend already enforces it.
- Script `frontend/swarm3-tax01.mjs` and its raw JSON dump `swarm-findings/round3/tax01-raw.json`
  deleted after this write-up per hard rule 4 (temp-script) / output-only rule 5.
