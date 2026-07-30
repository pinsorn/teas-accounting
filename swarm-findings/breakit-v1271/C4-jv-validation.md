# C4 — Manual Journal Voucher (JV) validation break-it (v1.27.1, prod)

Target: https://teas.kazaki-rio.com · company **co5** (id=5, "บริษัท ทดสอบ VAT (DUMMY)") · all writes confirmed co5 via `/api/proxy/me`.
Users: acct01 (uid12, gl.journal.create) · chief01 (uid17, gl.journal.post + CoaManage) · sales01 (uid11, no gl scope). All logins 200 (the ~00:10 "all UxSwarm 401" was transient — resolved by 01:08).

## CRIT banner
**No strict CRIT triggered** — no JV reached Posted unbalanced, and no header/inactive/closed-period violation reached Posted. BUT one **HIGH** money-invariant breach did reach the immutable GL (F1): the draft / human-post / MCP path posts sub-satang (>2-decimal) amounts, and co5's **live trial-balance total is now `822801.785`** — a value that cannot exist in a 2-decimal currency.

## Per-sub-area verdict
| Sub-area | Verdict |
|---|---|
| 1. Balance edge — `/journals/manual` (create+post) | **PASS** — all 6 attacks refused (400) |
| 1. Balance edge — `/journals` draft → `/journals/{id}/post` | **FAIL (F1)** — sub-satang precision accepted → Posted |
| 2. Account 3-check gate (header/inactive/foreign/nonexistent/mixed) | **PASS** — 422 on both manual and human-post paths |
| 3. Period / fiscal gates (closed / future / boundary) | **PASS** — 422 (future, closed mid/last-day, old months) |
| 4. Size/shape (100-line, dup-account, injection) | **PASS** (post holds; SQLi none) |
| 4. Size/shape (long strings; 200-line cap) — draft path | **FAIL (F2 raw 500, F3 no cap)** |
| 5. Human-post flow — permission-less refusal (sales01) | **PASS** — 403 server-side on every route |
| 5. Human-post flow — does post re-run manual checks? | **FAIL (F1)** — account/period gates run, precision/length do NOT |
| 6. Immutability (edit / delete / re-post posted JV) | **PASS** — je.not_draft 422; PUT/DELETE 405 |

---

## F1 — HIGH — Draft/human-post/MCP path posts sub-satang (>2-decimal) amounts the manual path rejects; breaks the THB 2-decimal invariant in the immutable GL
**Root cause (code):** the two JV write paths use different validators.
- `/journals/manual` (`CreateAndPostManualAsync`) → `CreateManualJournalValidator` — **has** the rule `decimal.Round(amt,2)==amt` ("Amounts must have at most 2 decimal places", JournalDtos.cs:75-77).
- `/journals` draft (`CreateDraftAsync`) → `CreateJournalValidator` — **no** such rule (JournalDtos.cs:85-108).
- `POST /journals/{id}/post` (`PostAsync`, JournalService.cs:82-115) runs the account gate + period gate + `MarkPosted`, but `MarkPosted` (JournalEntry.cs:58-71) only checks the **header totals** `TotalDebit==TotalCredit` — never per-line precision. So a draft carrying 4-decimal lines that sum equal sails through to Posted.
- **Reachable in prod via the v1.27.0 "agents draft / humans post" flow:** MCP tool `create_manual_journal_draft` (TeasMcpTools.cs:1147) validates with the same `IValidator<CreateJournalRequest>` (= `CreateJournalValidator`) — so an AI agent drafting a proportional / 1-of-3 / FX-converted split passes, and the human who clicks Post cannot catch it.

**Repro (direct API, but MCP-path identical):**
1. acct01 `POST /api/proxy/journals` (draft):
   - `{"docDate":"2026-07-31","postingDate":"2026-07-31","description":"...","currencyCode":"THB","exchangeRate":1,"lines":[{"accountId":52,"debitAmount":100.005,"creditAmount":0},{"accountId":64,"debitAmount":0,"creditAmount":100.005}]}` → **201** `journal_id:275`
   - Identical payload to `/journals/manual` → **400** `"Amounts must have at most 2 decimal places"`.
2. chief01 `POST /api/proxy/journals/275/post` → **200 Posted** `07-2026-JV-0142`, read-back lines `dr 100.005 / cr 100.005`, `totalDebit 100.005`.
3. Same for a 1/3 split draft 274 (33.3333 / 33.3333 / 33.3334 vs Cr 100.00) → Posted `07-2026-JV-0141`; its three debit lines each display **33.33** (sum 99.99) against the **100.00** credit — a visible per-line tie-out break.

**Expected:** the human-post/MCP path enforces the same 2-decimal guard as `/journals/manual`; sub-satang refused with 400.
**Actual:** accepted and posted to the immutable ledger. **Live impact:** co5 `/reports/trial-balance?asOfDate=2026-07-31` now reports `totals.debit = totals.credit = 822801.785`; rows 1110=`6949.3383`, 1120=`25194.7833`, 1130=`50358.6434`, 4000cr=`47706.005` — the fractional parts are exactly JVs 274/275; these were clean 2-dp before the test. Immutable — cannot be corrected except by a reversing JV (which would also need sub-satang to net out).

## F2 — MEDIUM — Draft path returns raw HTTP 500 (Postgres 22001) for over-length Reference / line Description instead of 400
**Root cause:** `CreateJournalValidator` has no `Reference` MaxLength and no per-line `Description` MaxLength; the columns are `Reference` nvarchar(255) and line `Description` nvarchar(500). `CreateManualJournalValidator` caps both (its own comment at JournalDtos.cs:67-70 says this exact class "came back as a raw Postgres 22001 instead of a 400" and was fixed there — the fix was never applied to the draft validator). `CreateDraftAsync` persists the raw value → DB 22001 → unhandled → 500.
**Repro (acct01 `POST /api/proxy/journals`):**
- `reference` = 300 chars → **500** `{"type":"urn:teas:error:internal_error",...}`
- one line `description` = 600 chars → **500** (same)
Both reachable by an MCP agent too (the line `Memo` maps to line Description with no cap).
**Expected:** 400 validation error. **Actual:** raw 500 (backend-thrown; request rolls back — no data written).

## F3 — LOW — Draft path has no 200-line cap (manual path caps at 200)
`CreateManualJournalValidator` rejects >200 lines; `CreateJournalValidator` does not.
**Repro:** acct01 `POST /api/proxy/journals` with 250 lines (249×Dr 1.00 + 1×Cr 249.00) → **201** draft 286 → chief01 post → **200 Posted** `07-2026-JV-0146` (250 lines). Mild unbounded-array abuse surface into the immutable GL.

## F4 — INFO/LOW — Injection/XSS payloads stored raw (no SQLi; FE-escaping dependent)
Manual-path JV 285 with `description` = `Robert'); DROP TABLE gl.journal_entries;-- <script>alert(1)</script>`, `reference` = `<img src=x onerror=alert(1)>`, line desc = `'; DELETE FROM gl.journal_lines;--` → **200 Posted**; read-back stores all strings **literally** (EF parameterized — tables intact, subsequent calls fine). Thai combining marks (ม + ่ + ้) stored fine. No SQLi. Flagged only for a FE render audit (stored `<script>`/`onerror` rely on React auto-escaping in the JV detail/PDF views).

---

## Things that correctly held (evidence)
- **Manual balance battery** (chief01 `/journals/manual`), all **400**: unbalanced by 0.01; by 0.005 (precision+balance); 33.3333 satang split; a 0.00 line (XOR); negatives (`>=0` rule); both-dr-and-cr line (XOR).
- **Account 3-check** (422): nonexistent (999999999)→`je.account_not_found`; foreign co (acct 1, company 1)→`je.account_not_found` (same answer, no info leak); header (acct 157/9990)→`je.account_is_header`; inactive (acct 158/9991)→`je.account_inactive`; mixed valid+header→`je.account_is_header`. **Gate also fires on the human-post path:** draft 282 with a header line was created (draft-time has no account check) but `POST /journals/282/post` → **422 je.account_is_header**.
- **Date gates** (422): future 2027-01-01→`je.future_date`; closed period 2026-06-15 & 2026-06-30 (last day) & 2026-01-15 & 2020-01-01→`period.closed`. Note: "not-yet-open fiscal year" is unreachable on manual path (future-date check fires first) and on draft path (docDate pinned to today) — by design; `je.year_closed` is masked by `period.closed` firing first, both reject.
- **Permission-less (sales01, no gl scope)** — server-side **403** on: `POST /journals/manual`, `POST /journals/273/post`, `POST /journals` (draft), `GET /journals/273`. Refusal is server-enforced, not just hidden UI.
- **Immutability** on posted JV 273: re-`POST /post` → **422 je.not_draft**; `PUT /journals/273` → **405**; `DELETE /journals/273` → **405** (no update/delete route exists).
- Happy path: acct01 draft 273 → chief01 post → `07-2026-JV-0140` Posted; docDate server-pinned to 2026-07-31 (Bangkok), request date ignored.

## Test residue left in co5 (immutable / master data)
- Posted JVs (immutable): 273 (`JV-0140` clean), **274 (`JV-0141` satang-split — skews TB)**, **275 (`JV-0142` 100.005 — skews TB)**, 283 (`JV-0143` 100-line), 284 (`JV-0144` dup-account), 285 (`JV-0145` injection strings), 286 (`JV-0146` 250-line).
- Unposted draft 282 (header-account line, post-rejected) — no GL/doc-no impact.
- CoA fixtures created via chief01: acct 157 (code 9990, header) + acct 158 (code 9991, deactivated) — master-data residue.
- co5 trial balance carries a permanent sub-satang skew (`822801.785`) from 274/275 (F1).
