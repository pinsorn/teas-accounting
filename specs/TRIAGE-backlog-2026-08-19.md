# Spec-backlog triage — 2026-08-19 (C6, Explore sweep + Fable spot-check 4/4)

65 open/partial items across 21 specs → **48 DEAD · 8 OBSOLETE · 9 ALIVE**. Full per-item
table with file:line evidence is in the C6 worker report (this file records verdicts + the queue;
the marking dispatch C9 stamps each spec file with its verdict + evidence line).

## ALIVE — Cluster A: server-migration cutover (blocked on prod; code all shipped)
→ consolidated into `MIGRATION-CUTOVER-CHECKLIST.md` (C8 writes it):
1. Repttown non-VAT AR backfill APPLY run (`fix-breakit-r1` §7 protocol) — money, posts real JVs.
2. Super-admin tenant-scope verify through PUBLIC topology (b406528 never edge-proven) — security.
3. MCP document-chain E2E at Repttown (Q→SO→IV→RC, PO→VI→PV).
4. Delete stray draft CN #2 (฿535) on co5.
(+ from r2: seed-638 pre-probe, class-B survey ×4 tables, §N2.5, co2-style real-volume leg, co5 sanity.)

## ALIVE — Cluster B: Ham decisions
5. **O2b**: should linked TIs drive the Billing Note total? (฿107 billed vs ฿6,955 linked — options
   drafted in fix-army-findings L980-995). Money semantics.
6. **O5**: ภ.พ.36 PDF export (parity with ภ.ง.ด.54 route; filers currently get no printable form). Tax.
(+ footnote: fix-breakit-r2 §10 E1 — CPA sign-off on the ภ.พ.36 ม.83/6 rule; code implements it,
professional confirmation outstanding.)

## ALIVE — Cluster C: small, unblocked
7. troubles-wiki entries ×2 (F14 tax-code-picker trap; no-browser-edit/delete-draft-TI) — C9 does.
8. R10 payroll picker double-click — low UX, never reproduced; stays open in its spec.

## Corrections the sweep made vs prior belief
- O11 (สปส.1-10 ส่วนที่ 2) = OBSOLETE not DEAD (blocked by template, superseded by on-screen alt bf87333).
- payroll-deductions-o10 has ZERO open items — STATUS board's "0 open / 1 partial" was stale.
- 14 of 21 specs fully DEAD/OBSOLETE; only 7 carry live work.

## Prod-scoped OBSOLETE (host retired → concerns fold into migration project)
S13 503s · Cloudflare 5xx pull · NPM certbot renewals · post-swarm co5 sanity ×2 · F9 CDP artifact ·
superadmin optional guard test (invariant covered by D1/D2).

---
# Full per-item verdict table (C6 Explore output, verbatim evidence — C9 stamps from this)

## expense-claims: both open items DEAD (duplicates of already-[x] lines at L481/L488; ExpenseClaimPermissionTests.cs:117)
## fix-army-findings-2026-07-22: O4 DEAD (edit page exists, d877286) · O5 ALIVE (no pp36 PDF route; only pnd54 at TaxFilingEndpoints.cs:111) · O8 DEAD (SalaryProration.DaysEmployed, PayrollRunService.cs:89-92) · O10 DEAD (deductions endpoint + o10 spec 100% [x]) · O11 OBSOLETE (blocked by template 4d71841; superseded by on-screen alt bf87333) · G4 DEAD (PaymentVoucherNonVatCompanyTests.cs:100) · G5 DEAD (d877286) · O2a DEAD (bn-ti-chips, invoices/[id]/page.tsx:162) · O2b ALIVE (Ham decision — BN total vs linked TIs, options at L980-995)
## fix-breakit-r1: apply-run of /admin/nonvat-ar-backfill ALIVE-BLOCKED (code+tests shipped, prod down) → cutover checklist §4
## fix-breakit-r2: all 8 open items DEAD (WP-0 probes executed per §12 L1735/L841/L1786; problems.ts:156/:177/:179; Pnd30VatRegistrantOnlyTests + BillingNoteSettlementDeletionTests swept by full suite) · footnote E1 CPA sign-off ม.83/6 still outstanding (not a checkbox)
## fix-chain-conversion-integrity: both ALIVE (docs) — F14 tax-code-picker wiki entry + no-browser-edit/delete-draft-TI wiki entry → C9 writes both
## fix-cn-list-docno-draft-delete: CN#2 delete on co5 ALIVE-BLOCKED → cutover §4
## fix-payroll-reports-findings-2026-07-16: R1 DEAD (global-error.tsx) · P1 DEAD (api.ts:195-209 throwFileResponseError) · P2+P4 DEAD (settings/employees/page.tsx:64-80) · P3 DEAD (th/en.json common.yes/no+report.total) · R2 DEAD (no dev-note strings left) · P7 DEAD (payroll/page.tsx:134 formatDateBE) · P8/P9 DEAD (aria-label + toast.success) · R10 ALIVE (low UX, never reproduced — stays open)
## fix-review-findings-2026-07-04: both DEAD (stale dispatch marker followed by [x]; M12 closed by 585_audit_log_rls.sql)
## fix-review-n-findings-2026-08-17: both DEAD (047fe95 + 2f8dad8; suite 1255/0/14)
## fix-sales-ux-findings-2026-07-16: all 3 OBSOLETE (origin host retired — 503s/Cloudflare-5xx/certbot concerns fold into migration project)
## fix-swarm-findings-all: both DEAD (628_seed_auditor_read_approver_grant.sql + AuditorReadApproverGrantTests, 5c49234)
## fix-vat-round-findings: DEAD (7fed441; residual failures were documented DB-residue flakes; suite green since)
## general-ledger: R1/R2/R3 DEAD (feature shipped a272d37, on main, CHANGELOG:446-447) · F9 OBSOLETE (CDP automation artifact, no code defect)
## mcp-document-chain: pipeline DEAD (972dddb + 3b082ce) · Repttown E2E ALIVE-BLOCKED → cutover §3
## mcp-error-surfacing: all 5 DEAD (list_tax_codes/wht_types/expense_categories/business_units registered in TeasMcpTools.cs:1203+; uomId doc note at :43-46)
## mcp-expansion: all 3 DEAD (72c8509 on main, CHANGELOG:438-439)
## payroll-deductions-o10: ZERO open items (board was stale)
## superadmin-tenant-scope: optional guard test OBSOLETE (invariant covered by D1 grep + D2 0-policy check) · public-topology verify ALIVE-BLOCKED → cutover §3
## uxswarm-multirole-co5: consolidation DEAD (swarm-findings/round4/ + fix-swarm-crit-numbering-rbac.md) · prod co5 sanity OBSOLETE→cutover · scripts cleanup DEAD (no swarm*.mjs)
## uxswarm-round3-crit-verify: verify DEAD (round3/ committed; CRIT-1/2 → fix spec) · prod seq-drift OBSOLETE→cutover · cleanup DEAD
## uxswarm-round5-finding-verify: both DEAD (round5/ committed; survivors → fix-swarm-round5-lows.md; no swarm5-*.mjs)
