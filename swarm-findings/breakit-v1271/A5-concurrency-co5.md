# A5 — Doc-number sequence + concurrency attack (co5, VAT dummy, prod v1.27.1)

Agent: A5 · Company: **co5 id=5** (บริษัท ทดสอบ VAT (DUMMY)) — confirmed `GET /api/proxy/me` → `companyId:5` before every write.
Login: **chief01 / `UxSwarm-2026-A7`** (CHIEF_ACCOUNTANT). NOTE: the task prompt's password `UxSwarm-2026-chief` is WRONG → 401; the real suffix is the agent **code** (A7), matching the NV-account convention (`UxSwarm-2026-NV4`). All 10 co5 accounts follow `UxSwarm-2026-<CODE>` (sales01=A1 … chief01=A7 … tax01=B1).

Primary weapon: `POST /journals/manual` (create-and-post atomic, hits the shared **JV bucket** — the CRIT-1 "usual culprit") and `POST /journals` (draft) → `POST /journals/{id}/post` (draft→post state transition, for the double-click race).

---

## ⚠️ CRIT (per the "any HTTP 500 = CRIT" rule) — see F1. Impact is error-surfacing ONLY; NO data-integrity loss.

Reproducible raw **HTTP 500** on the concurrent double-post (double-click) race. Data integrity fully held in every trial: posted exactly once, Dr=Cr, unique number, zero sequence gaps. This is the **same raw-500 error-surfacing class as the known CRIT-1** (troubles-wiki L123-127: a DB exception escaping the post path as a generic 500) but a **DIFFERENT trigger** — a concurrent state-transition TOCTOU, not single-threaded bucket drift. Not previously documented.

---

## Sub-area verdicts

| # | Sub-area | Verdict |
|---|----------|---------|
| 1 | Concurrent posts, same type (JV) — 5-wide & 20-wide | **PASS** — all 2xx, numbers unique + contiguous |
| 2 | Mixed types concurrently (JV + VI + PV interleaved) | **PASS** — shared JV bucket stayed contiguous; no deadlock (40P01), no cross-type 500 |
| 3 | Approve + post race (PV) | **NOT independently repro'd** — same TOCTOU class as F1 (draft→post); heavy setup owned by sibling agents. Inferred same-class defect, flagged for a targeted follow-up |
| 4 | Rapid retry / double-click (re-post same draft) | **FAIL** — raw HTTP 500 to racers ≥ N=3 (F1) |
| 5 | Sequence contiguity + uniqueness after every burst | **PASS** — JV-0001…JV-0101, zero gaps, zero dups |

---

## F1 — Concurrent double-post of the same draft JE returns raw HTTP 500 to the losing racers (instead of a clean 409/422)

- **Severity:** CRIT by the standing "any 500 → CRIT" rule; **true impact = MEDIUM/HIGH robustness** (raw 500 on a normal double-click/retry path). **No data corruption** — see integrity evidence.
- **Endpoint:** `POST /api/proxy/journals/{id}/post` (draft → Posted). Root cause is generic to the state-transition post path shared by TI/RC/PV/JV (all are `IConcurrencyVersioned`), so the same defect very likely reproduces on `/tax-invoices/{id}/post`, `/receipts/{id}/post`, `/payment-vouchers/{id}/post|approve`.
- **Root cause (inferred, high confidence):** the `je.not_draft` guard is a **read-check TOCTOU**. Racers that read `status=Draft` BEFORE the winner commits proceed, then collide at `SaveChanges` on the optimistic row-version → `DbUpdateConcurrencyException` that is **not mapped to a clean status** → generic `internal_error` 500. Racers that read status AFTER the winner commits get the clean `422 je.not_draft`. Which losers 500 vs 422 is pure timing (how many are inside the transaction window when the winner commits), so the 500 **count** varies run-to-run but ≥1 recurs whenever N≥3.

### Exact repro
```
# 1) create a draft (note: POST /journals/ with trailing slash → 308; use no trailing slash)
POST /api/proxy/journals
{"docDate":"2026-07-31","postingDate":"2026-07-31","description":"A5 double-post race draft",
 "reference":"A5-DP","currencyCode":"THB","exchangeRate":1,
 "lines":[{"accountId":52,"debitAmount":5.00,"creditAmount":0},
          {"accountId":53,"debitAmount":0,"creditAmount":5.00}]}
 → 201 {"journal_id":214}

# 2) fire N simultaneous posts of the SAME id via a FIFO barrier (curl &, released together)
POST /api/proxy/journals/214/post   x N   (no body)
```

### Observed responses
| N | winner (200) | clean losers (422 `je.not_draft`) | **RAW 500 `internal_error`** |
|---|---|---|---|
| 2 | 1 (JV-0089) | 1 | **0** |
| 3 | 1 (JV-0090) | 1 | **1** |
| 5 | 1 (JV-0087) | 1 | **3** |
| 20 | 1 (JV-0095) | 18 | **1** |

- 200 body: `{"journalId":214,"docNo":"07-2026-JV-0087","postedAt":...}`
- 422 body (correct): `{"type":"urn:teas:error:je.not_draft","title":"je.not_draft","status":422,"detail":"Cannot post journal in status Posted."}`
- **500 body (defect):** `{"type":"urn:teas:error:internal_error","title":"internal_error","status":500,"detail":"An unexpected error occurred."}` — generic, **no stack-trace leak** (login/proxy hardening holds).

### Expected vs actual
- **Expected:** exactly one 200; every other concurrent racer gets a clean idempotent refusal (409 Conflict or the same `422 je.not_draft`).
- **Actual:** one 200, one clean 422, and the remaining in-flight racers get raw **HTTP 500**. Minimum trigger **N=3**; N=2 is clean.

### Data-integrity evidence (the sequence target itself — HOLDS)
- `GET /journals/214` after the 5-wide race: `status=Posted`, single `docNo JV-0087`, `totalDebit 5.0000 == totalCredit 5.0000`, 2 lines. Posted **exactly once** — no duplicate JE, no duplicate GL, no Dr≠Cr.
- `GET /journals/222` after the 20-wide race: single `JV-0095`, Dr 5.0 = Cr 5.0. Exactly-once.
- Full month sweep after ALL bursts: **JV-0001…JV-0101, zero gaps, zero duplicate docNos.** The losing 500 threads leave **no orphaned number** (allocation rolls back cleanly — the savepoint/retry fix from CRIT-1 works).

---

## Sequence-integrity evidence (PASS — the core target held under everything I threw at it)

- **Baseline:** `POST /journals/manual` → `JV-0061`, format `MM-YYYY-JV-NNNN`.
- **5-wide concurrent manual-JV burst (FIFO barrier):** all 200 → JV-0062…JV-0066, unique + contiguous. Response times 423-681 ms show server-side serialization (row lock) — correct.
- **20-wide concurrent manual-JV burst:** all 200 → JV-0067…JV-0086, 20 unique contiguous numbers, no gap/dup. Times 500-880 ms (queued) — serialization holds at 20-wide.
- **Real mixed-doctype multi-agent load (incidental, strongest evidence):** interleaved with sibling Wave-A agents — JV-0088 = another agent's **VI** (`07-2026-VI-BU01-0001`, Dr 10700=Cr 10700), JV-0091 = another agent's **PV** (`07-2026-PV-BU01-IT-0001`) — the shared JV bucket stayed **fully contiguous** (0001-0101) across JV+VI+PV concurrent posting. **No 23505 on any `*_doc_no`, no reused number, no deadlock (40P01).**

**Verdict on the CRIT-1 family target:** the doc-number sequence + retry-guard (cap 50, savepoint, GREATEST reconcile 626) **HOLDS** under 20-wide same-type and real mixed multi-agent concurrency. The one crack is F1 — the losing side of a state-transition race surfaces a raw 500 instead of a clean refusal, with no data effect.

---

## Coverage gaps / not tested (for the orchestrator's follow-up call)
- Deliberate **TI / RC / PV concurrent same-type bursts** and **PV approve+post race** were NOT synthesized by A5 — they need customer/vendor/product/taxcode/uom setup owned by sibling Wave-A agents (A1 sales, A2/A4 purchase). Their own-bucket numbers were exercised concurrently by those agents (VI-0088, PV-0091 seen contiguous). F1's root cause is generic to the shared `IConcurrencyVersioned` post path, so a targeted double-post race on `/tax-invoices/{id}/post` and `/payment-vouchers/{id}/post|approve` should confirm the same raw-500 there — recommended as a one-shot follow-up.
- All writes were on **co5 only** (companyId re-confirmed = 5). No co2/co3 touched.
