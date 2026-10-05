# PROGRESS — cancel + reissue (spec: specs/cancel-reissue-sales-docs.md)

Branch `feat/notes-receipt-taxinvoice`. Ham (2026-10-05): "เริ่มเลย เสร็จแล้ว merge ไปเลย" — merge authorised after gates; CPA confirm is a follow-up.

## Done
- 3761e66 notes input RC/TI forms (chunk A)
- cddc30f / 02ce1f7 decisions + spec; §10.1 = Ham's resolutions (O1–O11)
- 1187136 WP-1 schema/triggers/perms — Opus APPROVE-WITH-NITS (nits folded into WP-2)
- WP-2 (services+endpoints+backend tests, 33 files) — worker gates: build 0/0, filtered 226/0/0. **Checkpoint-committed UNREVIEWED** (quota 96%).
- WP-4 FE (12 files) — tsc/lint/vitest 72/i18n parity green. Checkpoint-committed, DTO cross-check vs WP-2 pending.

## Resume order
1. Fable personal diff review of the WP-2 + WP-4 checkpoint commit (focus: TaxInvoiceService.Cancel.cs, ReceiptService.Cancel.cs, DocumentCancellation.cs, GlPostingService diff; FE `lib/types.ts` field names vs BE DTOs: TaxInvoiceCancelResult/ReceiptCancelResult/details).
   Note: WP-2 deviation — replacement UpdateDraft REJECTS differing QuotationId (spec said ignore) — accept. T20 GrantAllAsync leaves grants on pg_database_owner in teas_test (watch full suite).
2. WP-3 reports (§5 WP-3: VatReport/TaxFiling registers voided rows + cross-month memo, SubledgerReport JE-date rule, TaxSummaryService filter; tests T11, T13, T14) → sonnet-implementer.
3. WP-5: e2e `frontend/e2e/cancel-reissue.spec.ts` (T21; testids ti-*/rc-*/cancel-*), RBAC docs.
4. Fable: full backend suite (backgrounded), e2e.
5. Tier-2 Opus review (money+schema lenses) on whole branch diff vs main.
6. Push, PR, CI green, merge (Ham authorised). Watch deploy (login-page asset fingerprint). Bump backend/VERSION per release.
7. Tier-4 live leg on a SANDBOX tenant only (never Repttown real co / co2), resume protocol per spec §7.
