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
