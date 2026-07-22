# Wave B-et — e-Tax pipeline reality-check, co5, prod v1.22.10

Agent: sonnet (browser/Playwright, headless). Target: https://teas.kazaki-rio.com,
company co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด) ONLY. Logins: ar01 (`UxSwarm-2026-A5`,
role AR_CLERK — TI create+post) then tax01 (`UxSwarm-2026-B1`, role TAX_OFFICER —
`tax.filing.read`, needed for the audit-artifact check that ar01 doesn't hold).

## Done
- [x] Read backend e-Tax code FIRST to enumerate every observable artifact of a send
      (see "Artifact checklist from code-read" below) before touching the browser.
- [x] Logged in as ar01 — first try, `mePermissions` confirmed `sales.tax_invoice.create`
      + `sales.tax_invoice.post`, but NOT `tax.filing.read` (role AR_CLERK).
- [x] Posted ONE small Tax Invoice on co5: customer "บริษัท ลูกค้าทดสอบ จำกัด" (C001),
      1 line, qty 1 × 100 THB. **Posted 2xx** → `tax_invoice_id=28`,
      `https://teas.kazaki-rio.com/tax-invoices/28`. Blast cap respected (1 of ≤2 TIs).
- [x] Checked every enumerated artifact via UI/authenticated-session calls only — no DB,
      no SSH. Switched to tax01 (has `tax.filing.read`) for the one artifact ar01
      couldn't see, per RBAC (not a bug — see Findings).
- [x] Tenant-leak check (เรปทาวน์/พงศ์สันต์/repttown strings) on both ar01 and tax01
      dashboards: **clean**.
- [x] Verdict reached with evidence (see below).
- [x] Temp script `frontend/army-B-et.mjs` deleted after the run.

## Artifact checklist from code-read
Read (in order): `frontend/e2e/etax-pipeline-mock.spec.ts`,
`backend/src/Accounting.Api/Endpoints/EtaxEndpoints.cs`,
`backend/src/Accounting.Infrastructure/ETax/ETaxBehaviorOptions.cs`,
`backend/src/Accounting.Domain/Entities/ETax/ETaxSubmission.cs` (+ `Outcome` enum),
`backend/src/Accounting.Infrastructure/ETax/ETaxSubmissionAudit.cs` +
`IETaxSubmissionAudit.cs`, `backend/src/Accounting.Infrastructure/ETax/ETaxSubmissionPipeline.cs`,
`backend/src/Accounting.Infrastructure/Sales/TaxInvoiceService.cs` (PostAsync trigger,
~L544-566), `backend/src/Accounting.Infrastructure/ETax/ETaxSigner.cs`,
`backend/src/Accounting.Infrastructure/DependencyInjection.cs` (L143-165),
`frontend/app/(dashboard)/tax-invoices/[id]/page.tsx` (L66-71 comment).

Enumerated observable artifacts, and how each was checked:

| # | Artifact | How to check (UI/session only) | Result |
|---|---|---|---|
| 1 | **`etax.submissions` audit row** — one append-only row per attempt (`SubmissionId, TaxInvoiceId, AttemptNo, Outcome, AttemptedAt, ToEmailSnapshot, RedirectApplied, DeadLetter, RdAckRef, Notes`; `storage_path` deliberately never projected per code comment) | `GET /api/proxy/etax/submissions?tax_invoice_id=28`, gated on `tax.filing.read` (reused, no dedicated e-Tax permission exists) — polled 6× over ~15s, mirroring the mock spec's `expect.poll` | **0 rows** across all 6 polls (both as ar01-would-be and as tax01, who does hold the permission) |
| 2 | **TI detail page UI** (`/tax-invoices/28`) — e-Tax XML download / resend-email buttons | Screenshot + page-text scan for `/e-?tax/i` | **Not present** — confirmed by code comment (L66-71 of the detail page): these buttons were explicitly REMOVED "while the e-Tax pipeline is inert Phase-1 scaffolding" and were never re-added; the plan is to gate a future re-add on `sys.etaxEnabled` (not yet wired) |
| 3 | **`/system/info` `etaxEnabled` field** | `GET /api/proxy/system/info` while logged in as ar01 | Field **does not exist** in the live response (`{version, vat_mode, vat_rate, pnd30_submission_mode, document_number_format, timezone}`) — the FE code comment referencing it is aspirational/TODO, not implemented; confirms zero e-Tax config surface anywhere in the FE today |
| 4 | **Outbound email** (customer inbox / any mail log) | N/A — MailHog is a Tier-1 dev-only dependency (docker-compose), not present/reachable from prod; no in-app "sent mail" log exists | Not checkable via UI by design; the audit row's `ToEmailSnapshot`/`Outcome` is the only proxy the app exposes, and artifact #1 shows the pipeline never ran, so no email was attempted |
| 5 | **Signed XML / PDF attachment on the TI** | `AttachmentsSection` on the TI detail page (screenshot) | No e-Tax XML attachment shown; consistent with `SignedXmlPath`/`PdfPath` being explicitly non-projected even if a row existed |

## Evidence
- ar01 dashboard (pre-TI): `B-et-00-dashboard-ar01.png`
- Customer picked in TI form: `B-et-01-ti-form-customer-picked.png`
- TI form filled (qty 1 × 100 THB): `B-et-02-ti-form-filled.png`
- Post-confirm dialog: `B-et-03-post-confirm-dialog.png`
- TI #28 posted, detail page (no e-Tax UI visible): `B-et-04-ti-detail-posted.png`
- tax01 dashboard (audit-check session): `B-et-05-dashboard-tax01.png`
- Full console log (permissions, system/info JSON, all 6 poll results, verdict):
  captured inline below (script `army-B-et.mjs`, deleted after run).

```
ar01 perms: isSuperAdmin=false roles=["AR_CLERK"] has-ti-create=true has-ti-post=true has-filing-read=false
system/info (ar01): {"version":"1.22.10","vat_mode":true,"vat_rate":0.07,"pnd30_submission_mode":"manual","document_number_format":"MM-YYYY-PREFIX-NNNN","timezone":"Asia/Bangkok"} -- etaxEnabled field present: false
TENANT LEAK CHECK (ar01 dashboard): clean
customers found: 10
chosen customer: {"customerId":5,"customerCode":"C001","customerType":"Corporate","nameTh":"บริษัท ลูกค้าทดสอบ จำกัด", ... "email":null}
TI POSTED OK: id=28 url=https://teas.kazaki-rio.com/tax-invoices/28
TI detail page text mentions e-Tax: false (expected false per code read — buttons were removed)
ar01 lacks tax.filing.read -> switching to tax01 for the audit check
tax01 perms: isSuperAdmin=false roles=["TAX_OFFICER"] has-filing-read=true
[poll 0..5] as tax01: GET /etax/submissions?tax_invoice_id=28 -> 200 (0 rows every time, ~15s span)
FINAL AUDIT RESULT: {"status":200,"rows":[]}
```

## Findings
- **No defect.** RBAC-as-designed: ar01 (AR_CLERK) can create/post Tax Invoices but
  cannot read the e-Tax audit trail (`tax.filing.read` is TAX_OFFICER/ACCOUNTANT/
  CHIEF_ACCOUNTANT/COMPANY_ADMIN/SUPER_ADMIN only, per `241_seed_tax_filing_perms.sql`
  + `627_seed_tax_officer_filing_grant.sql`). Requiring a role switch to tax01 to view
  it is correct SoD, not a gap — noted only so the next army leg doesn't re-flag it.
- **No 500s/crashes/blank pages/raw-i18n-keys** anywhere in the run (TI create form,
  post-confirm dialog, TI detail, both dashboards).
- The customer used (C001, "บริษัท ลูกค้าทดสอบ จำกัด") has **no email on file** —
  checked all 10 co5 customers via `/customers/{id}` before choosing; none had an
  email set. This is orthogonal to the verdict below (see Unbuilt-vs-untested): even
  with no customer email, the pipeline — if enabled — still writes a `NotApplicable`
  audit row (`TaxInvoiceService.cs`/`ETaxSubmissionPipeline.cs` L77-83). Zero rows
  written means the pipeline was never invoked at all, not that it ran and no-opped.

## Verdict: **DISABLED-by-config**
`ETax:Enabled` and/or `ETax:AutoSendOnTaxInvoicePost` are **false** in prod. Evidence:
`TaxInvoiceService.PostAsync` only calls `TryAutoSendETaxAsync` (→
`_etaxPipeline.EnqueueAsync` → `ETaxSubmissionPipeline.RunAsync`) when
`_etaxOpts.Enabled && _etaxOpts.AutoSendOnTaxInvoicePost` are both true
(`TaxInvoiceService.cs:546`). `RunAsync` writes an append-only `etax.submissions` row
on **every** code path — success, XSD failure, SMTP failure, missing-cert
`DomainException`, even the "customer has no email" no-op. A posted TI (#28) producing
**zero** rows across 6 polls (~15s) after post therefore means the pipeline was never
entered — config gate, not a runtime failure. This is a **classification, not a bug**.

What would flip it to ENABLED (for whoever owns prod config next):
- `ETax:Enabled=true` and `ETax:AutoSendOnTaxInvoicePost=true` in appsettings/env.
- Even then, two more prerequisites the code enforces would likely surface as
  `SendFailed` rows rather than `SendOk`, based on the code:
  - `ETax:Signing:PfxPath` must point to a real PKCS#12 file — `ETaxSigner.SignAsync`
    throws `etax.pfx_missing` ("e-Tax is inert until a cert is provisioned") if
    missing/not found. No evidence either way was gathered here (config not
    inspectable via UI), but the doc-comment on `ETaxSigner` itself calls the
    signer "inert by default", and the FE code comment calls the whole pipeline
    "inert Phase-1 scaffolding" as of this sprint — strongly suggesting no cert is
    provisioned in prod today.
  - `RdApi:Provider` defaults to `Mock` (`MockRdEfilingClient`) unless explicitly set
    to something else — so even a fully-enabled, fully-signed submission on prod
    today would NOT be a real RD e-filing submission; it would hit the same mock RD
    client the Tier-1 e2e spec exercises. This is a **design/scope fact, not a bug**:
    Phase 2 (per `EtaxEndpoints.cs` doc comment, "audit-viewer UI is Phase 2 §11")
    is where real RD ack handling (`RejectedByRd`, `RdAckRef`) plugs in.

## Unbuilt-vs-untested classification
- **Unbuilt (by design, Phase 1 scaffolding)**: e-Tax audit-viewer UI (no page
  surfaces `etax.submissions` rows — API-only, confirmed by `EtaxEndpoints.cs`'s own
  doc comment "audit-viewer UI is Phase 2"); `/system/info` `etaxEnabled` surfacing
  (FE code comment names it as pending, gates a future XML-download/resend-email
  button re-add on it); real RD e-filing HTTP client (`RdHttpEfilingClient` exists in
  code but is only wired when `RdApi:Provider != Mock` — untested whether that path
  even works, out of scope for this leg since prod appears disabled at the outer gate).
- **Untested-because-disabled, not broken**: the actual sign→validate→email happy
  path (`ETaxSigner`, `LocalXsdValidator`, `ETaxEmailSender`, `ETaxRecipientResolver`'s
  redirect/whitelist safety logic) — none of it ran on prod during this leg because
  the outer `ETax:Enabled`/`AutoSendOnTaxInvoicePost` gate is off. The mock e2e spec
  (`etax-pipeline-mock.spec.ts`) DOES cover this happy path end-to-end (MailHog +
  dev cert + both flags on) — that is Tier-1-only coverage; prod has never run it.
- No real-external-email risk materialized: with the pipeline gated off, no SMTP
  send was attempted, so there was no risk of a real email firing to a real address
  in this run — consistent with the dispatch's caution.

## Blast-radius / safety
- 1 Tax Invoice posted (#28, ≤2 cap respected, not retried further since the
  DISABLED-by-config verdict was unambiguous after 6 clean polls).
- No config changed. No ยืนยัน/ปิดงวด, no year-end close, no payroll, no master
  edits. No cross-tenant data observed.
