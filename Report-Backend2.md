# Report-Backend2 — Sprint 1 Wrap (Hardening + e-Tax C14N close-out)

**Date:** 2026-05-16
**Sprint:** 1 (Harden Phase 1)
**Prev:** [Report-Backend1.md](./Report-Backend1.md) · [Answer-Backend1.md](./Answer-Backend1.md)
**Author:** Claude Code · **Owner:** Ham (via Sana)

---

## 1. Executive Summary

Sprint 1 done. Answer-Backend1 §7 checklist fully executed + the 5 hardening tests shipped.

| Metric | Result |
|---|---|
| Build | 0 errors / 0 warnings (`NU1902/NU1903` are hard errors — CVE-clean) |
| `Accounting.Domain.Tests` | 32 / 32 pass |
| `Accounting.Api.Tests` | **10 / 10 pass, 0 skip** — and **10/10 again on the same DB** (idempotency proven) |
| e-Tax C14N | **CLOSED** — round-trip self-verify now passes |
| Net-new artifacts | `tax.v_number_gaps` view, 9 backend/test files |

No open blockers from Sprint 1. e-Tax remains inert; production still gated on the ETDA
cert (Answer-Backend1 §2, ~4–6 wk lead) — unchanged, expected.

---

## 2. Answer-Backend1 §7 checklist — status

- [x] Re-read `docs/etax-xades-spec.md` §1 errata + §3.4 (Sana's 3-point correction).
- [x] Added `spRef.AddTransform(new XmlDsigExcC14NTransform())` to `XadesBesSigner`
      before `AddReference(spRef)`.
- [x] Un-skipped the 3 round-trip XAdES tests; added a 4th (string round-trip + BOM-free
      assertion).
- [x] `dotnet test` — all green, **zero skips**.
- [x] `progress.md` — C14N item closed.
- [x] Sprint 1 hardening tests (5) — done (see §4).
- [x] Q4 mirror/ownership rules appended to `plan.md`.
- [x] This report.

---

## 3. e-Tax C14N — closed (root cause: spec, not code)

Confirmed Sana's diagnosis: the ETDA Java reference uses `xades4j`, whose
`XadesBesSigningProfile` default for the **SignedProperties Reference** is **Exclusive
C14N** (`http://www.w3.org/2001/10/xml-exc-c14n#`), per ETSI TS 101 903 §6.3.1 — the
outer `SignedInfo` canonicalization stays Inclusive. Applying that one transform makes the
sign-time (standalone `DataObject` fragment) and verify-time (in-tree node) canonical
forms identical, so .NET `SignedXml.CheckSignature` round-trips.

`XadesBesSignerTests` — **5/5 pass, 0 skip**:
1. `Signed_document_self_verifies` — clean doc verifies true.
2. `Tampered_content_fails_verification` — flip a value post-sign → false (now
   meaningful: clean doc verifies true, so false ⇒ real tamper detection).
3. `Different_certificate_fails_verification` — wrong cert → false.
4. `Survives_string_roundtrip_and_reparse` — UTF-8 BOM-free serialize → reparse →
   re-verify true (catches encoding regressions).
5. `Emits_mandatory_xades_profile_per_spec` — algorithms, 2 signed References,
   decimal `X509SerialNumber`, `SigningTime` +07:00, `SignedProperties` present.

The escalation path (CLAUDE.md §8 — flag, don't improvise) worked end-to-end: the spec
was fixed rather than the signature weakened.

---

## 4. Sprint 1 hardening tests (Answer-Backend1 §3) — 5/5 green

| # | Test | What it pins down |
|---|---|---|
| 1 | `NumberSequence_is_gapless_and_unique_under_concurrency` | 25 parallel `NextAsync` on isolated scopes → results are unique **and** a contiguous `1..25` (regression guard for the `ON CONFLICT` rewrite). |
| 2 | `TenantIsolationTests` (idempotent) | Randomized company ids + `tax_id` + customer code. **Proven** by running the Api suite twice on the same DB with no teardown → 10/10 both times. Fixes Report-Backend1 §2.9. |
| 3 | `Closed_period_blocks_posting_open_period_allows` | `CloseAsync` then `EnsureOpenAsync` throws `period.closed`; an untouched month stays open. |
| 4 | `PaymentVoucher_with_wht_issues_certificate_and_balanced_journal` | vendor → expense category → PV with WHT 3% → exactly one 50 ทวิ row + GL JV balanced 1000=1000 (Dr expense 1000 / Cr WHT 30 / Cr bank 970). |
| 5 | `RolledBack_allocation_does_not_consume_a_number_or_create_a_gap` | r1=1; in-tx r2=2 then **rollback**; r3=2 (released, no burn). New view `tax.v_number_gaps` returns zero rows — compliance §4.3 (no gaps). |

Net-new for #5: **`tax.v_number_gaps`** view, shipped as
`backend/src/Accounting.Infrastructure/Migrations/SqlScripts/050_number_gap_audit_view.sql`
(Claude owns `db/`/SQL per Answer-Backend1 §4). It reports any missing sequence number
within the issued range across `tax_invoices` / `journal_entries` / `payment_vouchers` —
a row here = a §4.3 numbering defect; in practice it must always be empty.

---

## 5. Files Changed

**Backend (src):**
- `ETax/ETaxSigner.cs` — `spRef.AddTransform(XmlDsigExcC14NTransform)` (the C14N fix).
- `Migrations/SqlScripts/050_number_gap_audit_view.sql` — new audit view.

**Tests:**
- `ETax/XadesBesSignerTests.cs` — un-skipped 3, added `Survives_string_roundtrip_and_reparse`.
- `Persistence/TenantIsolationTests.cs` — randomized ids/tax_id/code (idempotent).
- `Hardening/Sprint1HardeningTests.cs` — new: tests #1, #3, #4, #5.

**Docs (Claude-owned logs):**
- `progress.md` — ack line + C14N close-out + Sprint 1 entry.
- `plan.md` — mirror/ownership rules (Answer-Backend1 §4) appended.

> Note: `docs/etax-xades-spec.md` was edited by Sana (the §1 errata + §3.4) per the
> ownership split — Claude only consumed it.

---

## 6. Questions / Flags for Ham

1. **None blocking.** Sprint 1 is clean.
2. **`tax.v_number_gaps`** — I created this view (Claude owns `db/`). If you want it in
   `db/schema.sql` reference or the OpenAPI/reporting surface, say so; right now it's
   DB-only + test-consumed.
3. **e-Tax production** — unchanged: waiting on ETDA Service-Provider registration +
   sandbox cert/endpoint (Answer-Backend1 §2). No action from me until you deliver them.
4. **Ready for Sprint 2** — Tax Invoice end-to-end vertical slice (Answer-Backend1 §3):
   backend TI list/detail/`/xml`/`/pdf`/`resend` endpoints + the frontend screens
   (`Design(UI).md` §5, §7.1, §7.6, §7.7) with DaisyUI `teas` theme + `ui-ux-pro-max`.
   Proceeding unless you redirect.

---

## 7. Next (Sprint 2 — starting)

Per Answer-Backend1 §3 "Sprint 2+". Order:
1. Backend TI read endpoints (list w/ cursor paginate per `openapi.yaml`; detail; `/xml`;
   `/pdf`; `resend` no-op while e-Tax inert).
2. Frontend: login (BFF done) → dashboard (4 hero stats §5.1) → TI list (§7.1) → TI
   create + Post-Confirm dialog (§7.7) → TI detail/print (§7.6).
3. DaisyUI `teas`/`teas-dark` theme; `ui-ux-pro-max` plugin for UI/UX.
4. Wrap → `Report-Backend3.md`.

CLAUDE.md §0.2 honored: will read `frontend/node_modules/next/dist/docs/` before App
Router work.
