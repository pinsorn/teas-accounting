# B1 — Approval / SoD / State-machine attack (co5, VAT dummy, prod v1.27.1)

Agent: **B1** · Company: **co5 id=5** (บริษัท ทดสอบ VAT (DUMMY)) — `GET /api/proxy/me` → `companyId:5` re-confirmed before EVERY write. No co2/co3 touched.
Roles used (separate cookie jars): **chief01**/A7 (uid17, full poster), **admin01**/A8 (uid18, full), **tax01**/B1 (uid20, TAX_OFFICER, NO post/approve/create scopes), **sales01**/A1 (uid11, SALES_STAFF, sales read-only).
Credential note confirmed: password suffix is the ROLE-SLOT CODE (`UxSwarm-2026-<code>`), not the role name. All 4 logins → HTTP 200. The "all logins 401" scare in memory was the pre-correction wrong-suffix run; correct suffixes work.

---

## ⚠️ CRIT (by the standing "any HTTP 500 = CRIT" rule) — F1 class EXTENDS to the Sales post path

**Concurrent double-post (double-click / retry) returns raw HTTP 500 `internal_error` on the `/post` endpoints of Tax Invoice (TI), Receipt (RC), and Journal (JV).** A5 confirmed only JV and *inferred* TI/RC/PV; **B1 confirms TI and RC live, and clears PV** (PV is immune — it was hardened). Impact is **error-surfacing ONLY — zero data corruption** in every trial (posted exactly once, unique + contiguous doc numbers, no gaps, no double-settlement). Same class as troubles-wiki CRIT-1 / A5-F1; **new surfaces = TI + RC** (both compliance/cash documents). See **F1** below.

---

## Sub-area verdicts (one line each)

| # | Sub-area | Verdict |
|---|----------|---------|
| 1 | **Self-approval** (creator == approver == poster) | **PASS (by design)** — no doctype has a hard creator≠approver block; approval is permission-only (Ham ruling, SoD removed). chief01 created+approved+posted one PV alone → all 2xx. Documented, not a defect. Stale code comments claiming SoD *is* enforced = **F2 (LOW)**. |
| 2 | **Missing-scope approve/post** (direct API, bypass UI) | **PASS** — all 9 attempts → **403**. tax01 (no post/approve/create) blocked on PV approve/post/create, journals manual/create/post; sales01 blocked on TI post, RC post, PV approve. No bypass. |
| 3 | **Double-approve race** (N concurrent /approve) | **PASS** — PV: 1×200 + rest clean `422 pv.not_draft`, no 500. (ApproveAsync is defensively un-wrapped — latent seam, **F3 LOW**, not reproducible live.) |
| 4 | **Double-post race** (N concurrent /post) | **FAIL** — TI, RC, JV surface raw **HTTP 500** to losing racers (**F1**). PV is clean (409 `pv.locked_mismatch` / 422 `pv.not_approved`). |
| 5 | **Out-of-order transitions** | **PASS** — every illegal transition → clean 422; posted docs immutable. See table in F-details. |
| 6 | **Approve/post across a CLOSED period** | **PASS** — JV into closed 2026-06/2026-01/2025-06 → `422 period.closed`; future → `422 je.future_date`. PV/TI/RC re-pin DocDate to Bangkok-today at post, so they *cannot* target a closed period at all. |
| 7 | **Pending-approvals widget accuracy** | **PASS** — `/reports/pending-agent-approvals` tenant-scoped, count accurate (0 — my docs were JWT-created, correctly excluded; only API-key drafts count), correct RBAC gate (tax01 → 403, lacks `sales.tax_invoice.read`). No cross-company leak observed (full cross-tenant leak test is C2's scope). |
| 8 | **Edit-while-approving** (lost update / stale approval) | **PASS / N/A by design** — no editable-while-approving window exists: the only approval-carrying doc in scope (PV) has NO update/delete REST endpoint; TI/RC/JV have no approve step. |

---

## F1 — Concurrent double-post of the same draft returns raw HTTP 500 on TI, RC, JV (PV immune)

- **Severity:** CRIT by the "any 500 → CRIT" rule; **true impact = MEDIUM robustness** (raw 500 on a normal double-click/retry). **No data corruption.**
- **Endpoints affected (confirmed live):** `POST /api/proxy/tax-invoices/{id}/post`, `POST /api/proxy/receipts/{id}/post`, `POST /api/proxy/journals/{id}/post`.
- **Endpoint NOT affected (confirmed clean):** `POST /api/proxy/payment-vouchers/{id}/post` → maps the conflict to `409 pv.locked_mismatch`.
- **Root cause (code-confirmed):** all four documents are `IConcurrencyVersioned` (Version is `.IsConcurrencyToken()`), so a racer that reads the row as still-Draft, proceeds, then collides at `SaveChanges`, throws `DbUpdateConcurrencyException`. **Only `PaymentVoucherService.PostAsync` wraps that exception** (WP-B / Opus Tier-2 F1, 2026-07-25 — `catch (DbUpdateConcurrencyException) → pv.locked_mismatch`). `TaxInvoiceService` / `ReceiptService` / `JournalService` post paths have **no such catch**, so the exception escapes unmapped → the ApiError middleware's generic `500 internal_error`. Racers that read the row *after* the winner commits get the clean in-memory guard instead (`ti.not_draft` / `rc.not_draft` / `je.not_draft` = 422). Which losers 500 vs 422 is pure timing; ≥1 recurs reliably at N≥3 with tight (HTTP/2-multiplexed) concurrency.

### Exact repro (TI shown; RC/JV identical shape)
```
# 1) create a draft TI on co5
POST /api/proxy/tax-invoices
{"docDate":"2026-07-31","customerId":5,"isTaxInclusive":false,"currencyCode":"THB","exchangeRate":1,
 "lines":[{"productId":6,"productCode":"P001","descriptionTh":"A","quantity":1,"uomId":1,"uomText":"ชิ้น",
           "unitPrice":100,"discountPercent":0,"taxCodeId":1,"taxCode":"VAT7","taxRate":0.07,"productType":"GOOD"}],
 "businessUnitId":4}
 → 201 {"tax_invoice_id":43}

# 2) fire 12 simultaneous posts of the SAME id over ONE HTTP/2 connection (tightest overlap):
curl --parallel --parallel-immediate --parallel-max 12 -b jar \
     ( -X POST https://teas.kazaki-rio.com/api/proxy/tax-invoices/43/post ) x12
```
Note: separate curl *processes* (each doing its own TLS handshake) do NOT reproduce it — the handshake jitter smears arrival and the server serializes cleanly. A single `curl --parallel` multiplexed connection is required to overlap the critical section. (This is why A5 could not independently repro the sales/PV variants.)

### Observed (5 rounds, N=12, TI)
| round | winner 200 | clean `ti.not_draft` 422 | **RAW 500 `internal_error`** |
|---|---|---|---|
| 1 | 1 | 11 | **0** |
| 2 | 1 | 8 | **3** |
| 3 | 1 | 7 | **4** |
| 4 | 1 | 8 | **3** |
| 5 | 1 | 7 | **4** |

RC (4 rounds, N=12): 500-count **4,3,3,4**. JV (verification, N=12): 500 reproduced rounds 2–3.

- **500 body (defect):** `{"type":"urn:teas:error:internal_error","title":"internal_error","status":500,"detail":"An unexpected error occurred."}` — generic, **no stack-trace leak** (login/proxy hardening holds).
- **Clean loser (correct):** `{"type":"urn:teas:error:ti.not_draft","status":422,"detail":"Cannot post ... in status Posted."}`
- **PV contrast (correct handling):** losers split between `409 pv.locked_mismatch` (2–4×) and `422 pv.not_approved` (7–9×); **zero 500** across all rounds.

### Expected vs actual
- **Expected:** exactly one 200; every other concurrent racer gets a clean idempotent refusal (409 or the same 422), as PV already does.
- **Actual:** TI/RC/JV give one 200, some clean 422, and the remaining in-flight racers get raw **HTTP 500**.

### Data-integrity evidence (HOLDS — this is error-surfacing only)
- TIs 42–46 after the races: each `status=Posted`, **exactly one** contiguous docNo `07-2026-TI-BU01-0001…0005`, total 107 each. RCs 30–33: each Posted once, `07-2026-RC-BU01-0001…0004`, TIs settled once (no double-application).
- **Number-gap audit** `GET /reports/number-gaps?year=2026&month=7&doc_type=TI|RC` → `{"gaps":[],"hasGaps":false}` for both — the losing 500 threads waste **no** doc number (allocation rolls back cleanly, CRIT-1 retry-guard works).

### Suggested fix (for the fix-arc; NOT applied)
Mirror `PaymentVoucherService.PostAsync`'s `try { … } catch (DbUpdateConcurrencyException) { throw new DomainException("<doc>.locked_mismatch", …) }` wrapper into `TaxInvoiceService`, `ReceiptService`, and `JournalService` post paths (and the shared `CreateAndPostManualAsync`). One-line-per-service, same pattern already proven on PV.

---

## F2 — Stale/false SoD comments claim an enforcement that does not exist (LOW, doc/audit-integrity)

- **Where:**
  - `PaymentVoucherService.cs:408` — `// SoD enforced in the entity (and belt-and-braces by DB CHECK ck_pv_sod).` — **false**: `MarkApproved` has no creator≠approver check, and `ck_pv_sod` was dropped.
  - `IPaymentVoucherService.cs:11-13` — `/// B2 SoD gate (Draft → Approved). Approver must differ from creator (CLAUDE.md §12.1).` — **contradicts** the entity's own authoritative comment (`PaymentVoucher.cs:104-108`: "approval is now permission-based only … the previous creator≠approver SoD rule (app check + DB CHECK ck_pv_sod) is removed").
- **Live proof it is NOT enforced:** chief01 (uid17) created PV 41, then `POST /payment-vouchers/41/approve` → `200 {"approvedBy":17}`, then `/post` → `200`, final `07-2026-PV-BU01-IT-0011` with `approvedBy:17, postedBy:17`. One user, whole chain.
- **Severity LOW** because self-approval is a **deliberate product decision** (single-operator SME). The risk is a maintainer/auditor reading the stale comments and believing SoD is enforced when it is not. **Fix = delete the two stale comments**, don't add enforcement.

---

## F3 — PV `ApproveAsync` lacks the concurrency wrapper its siblings have (LOW, latent)

- `PaymentVoucherService.PostAsync` and `CancelAsync` both `catch (DbUpdateConcurrencyException)` → clean `pv.locked_mismatch`. **`ApproveAsync` (L390-416) does not** — a genuine version conflict on the approve `SaveChanges` would escape as a raw 500, same class as F1.
- **Not reproducible live:** approve is a single fast statement, so the winner commits before losers finish their read → all losers hit the in-memory `pv.not_draft` guard (clean 422) instead of the SaveChanges collision. Confirmed clean across 3 rounds × N=12.
- **Severity LOW / latent** — recommend mirroring the same one-line wrapper defensively when F1 is fixed.

---

## F4 — PaymentVoucher `CreatedBy` never stamped (LOW, audit)

- `PaymentVoucherService.CreateDraftAsync` sets `CreatedViaApiKeyName` but never sets the entity's `CreatedBy` column; `GET /payment-vouchers/41` returns `createdBy:null` while `approvedBy`/`postedBy` are populated. A dedicated "who created this payment voucher" field is blank.
- **Severity LOW** — a redundant audit trail exists (`_activity.Record("PaymentVoucher", … "Created")` logs the actor), so the information isn't lost, just not on the entity/DTO.

---

## Out-of-order transition matrix (all PASS — clean 422, immutability absolute)

| Attempt | Result |
|---|---|
| POST-before-approve (Draft `/post`) | `422 pv.not_approved` |
| approve an already-POSTED PV (`/approve`) | `422 pv.not_draft` |
| re-POST a posted PV (`/post`) | `422 pv.not_approved` |
| CANCEL a posted PV (`/cancel`) | `422 pv.cannot_cancel` (immutable after Post) |
| CANCEL an approved PV (legit escape hatch) | `204` |
| approve a VOIDED PV | `422 pv.not_draft` |
| post a VOIDED PV | `422 pv.not_approved` |

---

## Coverage / scope notes
- All writes on **co5 only** (companyId re-confirmed = 5 before every write batch). Litter left on co5: PVs 41–54, TIs 41–46 (posted) + drafts, RCs 30–33, ~10 JV drafts + posts, all in OPEN period 2026-07.
- Sales-chain state machine beyond post (QT/SO/DO send/accept/convert, PUT-edit) = A1's scope, not re-tested here.
- Cross-tenant leak on the pending-approvals widget needs a co7 user = C2's scope.
- Expense-claim and payroll approve chains = B2/B4 (not duplicated).
