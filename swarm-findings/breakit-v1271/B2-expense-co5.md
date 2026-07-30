# B2 — Expense Claims break-it QA (co5, VAT dummy) — prod v1.27.1

Target: https://teas.kazaki-rio.com · company **co5** (id=5, "บริษัท ทดสอบ VAT (DUMMY)") · all writes confirmed `companyId:5` before each op.
Tested via direct API (`/api/proxy/expense-claims`, `/attachments`, `/journals`) — the FE pages for expense-claims are still unbuilt per `specs/expense-claims.md` §5 (backend endpoints all live).
Date: 2026-07-31. Users: acct01 (create), chief01/admin01 (approve/pay). Note: **appr01 (APPROVER role) has NO expense.claim.* perms** — approve/pay must use chief01/admin01.

---

## ⛔ CRIT — 2× HTTP 500 (per B2 stop-rule: any 500 = mark prominently)

Neither 500 corrupts data (no Dr≠Cr, both roll back / reject), but both are unhandled server errors on unvalidated input.

### CRIT-1 — Attachment upload > ~5MB → HTTP 500 (breaks receipt upload; advertised 25MB limit unreachable)
- **Repro:** `POST /api/proxy/attachments` multipart (`parent_type=EXPENSE_CLAIM, parent_id=4, category=EXPENSE_CLAIM_FORM`, `file` declared `application/pdf`):
  - 1MB → 201 · 5MB → 201 · **10MB → 500** · 24MB → 500 · 26MB → 500
- **Expected:** files up to the configured `FileStorage:MaxFileSizeMb=25` succeed; an over-limit file returns a graceful **413** (`attachment.too_large`, the code path in `AttachmentService.UploadAsync:112`).
- **Actual:** anything above ~5–8MB returns `{"type":"urn:teas:error:internal_error","status":500}`. The app's own size check / 413 path is never reached — a lower body-size limit in the topology (Next `/api/proxy` handler or reverse proxy) throws first. No `MaxRequestBodySize`/`MultipartBodyLengthLimit` is configured in backend code (Kestrel defaults 30MB/128MB are both above the failing sizes), so the ~5–8MB break is upstream of Kestrel.
- **Impact:** larger-but-valid scanned receipts (a 24MB PDF is well under the promised 25MB) cannot be uploaded and fail with an opaque 500. Shared attachment infra → also affects vendor-invoice / PV / tax-invoice / bank-statement uploads, not just expense claims.

### CRIT-2 — Pay TRANSFER with invalid/nonexistent bankAccountId → HTTP 500 (missing money-path validation)
- **Repro:** claim 8 in status Approved, as chief01:
  - `POST /expense-claims/8/pay` `{"paymentMethod":"TRANSFER","bankAccountId":999999}` → **500** `internal_error`
  - same with `"bankAccountId":0` → **500**
- **Expected:** a clean domain error (e.g. `expense_claim.bank_account_not_found` / 422) — mirroring the existing account-override guard which correctly 422s.
- **Actual:** 500. The pay validator (`PayExpenseClaimValidator`) only checks `bankAccountId != null` for TRANSFER; existence/tenant-ownership is never validated. `GlPostingService.PostExpenseClaimAsync` resolves the bank's `GlCashAccountId` and blows up on the missing row.
- **Mitigating:** transactional rollback held — after both 500s claim 8 is still `Approved`, `docNo=None`, `journalEntryId=None`. **No orphan JE, no consumed doc number.** A foreign company's bank id is hidden by the tenant filter → behaves as nonexistent → same 500 (no cross-company posting).

---

## PASS / FAIL per sub-area

| Sub-area | Result |
|---|---|
| Round 1 happy path create→submit→approve→pay + JE tie-out | **PASS** |
| VAT split to 1170 (7% / 0% / non-recoverable), Dr=Cr | **PASS** |
| CASH vs TRANSFER credit branch (1110 vs 1120) | **PASS** |
| Amount validation (zero, negative) | **PASS** (400) |
| VAT-rate bounds (>1, negative, the 7-vs-0.07 mistake) | **PASS** (400) |
| Expense-account override guard (header / foreign / nonexistent) | **PASS** (422) |
| Employee / category existence validation | **PASS** (422) |
| Immutability of PAID claim (edit/cancel/submit/approve/reject) | **PASS** (422) |
| Illegal transitions (approve/reject Draft, pay unapproved) | **PASS** (422) |
| Sequential double-pay (pay an already-Paid claim) | **PASS** (422 not_approved) |
| Concurrent double-pay race → exactly one JE | **PASS** |
| Reject → edit → resubmit (no dup, reject-reason cleared) | **PASS** |
| RBAC endpoint gating (acct01/sales01/appr01 negatives) | **PASS** (403) |
| Cross-tenant attachment read by id | **PASS** (no leak found) |
| Attachment validation (bad MIME, empty, bad/absent parent) | **PASS** (422/400) |
| Attachment download parent-perm inheritance | **FAIL** (HIGH-1) |
| Attachment content sniffing | **FAIL** (LOW-1, MIME spoof) |
| Attachment immutability after PAID | **FAIL** (LOW-2) |
| Amount cap / approval threshold | **N/A — none exists** (LOW-3) |
| Separation of duties on money path | **none — permission-only** (LOW-4, documented) |
| Oversized attachment / large upload | **FAIL** (CRIT-1) |
| Bank-account validation on pay | **FAIL** (CRIT-2) |

---

## HIGH

### HIGH-1 — Attachment download-by-id bypasses the parent-read-permission check (broken access control)
- **Repro:** sales01 and appr01 both hold `sys.attachment.read` but NOT `expense.claim.read`.
  - `GET /attachments?parent_type=EXPENSE_CLAIM&parent_id=4` (LIST) → **403** ("'expense.claim.read' required…") — correct, ParentGuard enforced.
  - `GET /attachments/8/download` (the SAME expense-claim receipt) → **200, full 193-byte PDF returned.**
- **Expected:** the download-by-id route should apply the same parent-read-permission inheritance the LIST route enforces.
- **Actual:** `GET /attachments/{id}/download` (`AttachmentEndpoints.cs:77`) requires only `sys.attachment.read` — no `ParentGuard`. `OpenForDownloadAsync` fetches by attachment id with no parent-perm check. So any user with the generic `sys.attachment.read` can read **any** document's attachments (vendor-invoice scans, PV docs, tax invoices, expense receipts) by walking small sequential attachment ids, regardless of holding that document type's specific read permission. Intra-company (tenant isolation still holds); horizontal privilege escalation defeating `ParentReadPermission`.

---

## LOW / INFO

### LOW-1 — No content sniffing; MIME is the client-declared Content-Type only (spoofable)
- **Repro:** upload `evil.html` (`<script>alert(1)</script>`) with `type=application/pdf` → **201**, stored as `mimeType=application/pdf`, `fileName=evil.html` (attachmentId 9). `text/html` declared honestly → correctly 422 `bad_mime`.
- Arbitrary bytes (HTML/script/executable) can be stored under a whitelisted MIME by spoofing the header. Mitigated: download uses `content-disposition: attachment` (forces save, not inline render), so stored-XSS-on-view is blunted. Recommend magic-byte validation.

### LOW-2 — Attachments mutable on a terminal PAID claim (no audit lock)
- **Repro:** `POST /attachments` to claim 4 (status Paid, JE posted) → **201** (attachmentId 10). Uploader can also soft-delete own receipts (`SoftDeleteAsync` allows uploader OR delete-perm). Receipts can be added/removed after payment with no state guard — weakens audit integrity of a posted disbursement.

### LOW-3 — No amount cap / approval threshold (silent accept)
- Create accepted `amount:999999999999.99` (claim 6) → submitted → **approved** (chief01) with zero block or escalation. `numeric(19,4)`; no limit/threshold feature exists in spec or code. Not paid (would post a 1.07T-baht JE). 3+-decimal amounts (`100.12345`, claim 5) silently rounded to 4dp net.

### LOW-4 — No separation of duties on the cash-disbursement path (documented, permission-only)
- chief01 single-handedly created → submitted → **self-approved** (approvedBy=17) → **self-paid** claim 9 (JE 260), all succeeded. Matches `specs/expense-claims.md` open-question #3 ruling (permission-only; creator may self-approve). Flagged as a money-control weakness: one CHIEF_ACCOUNTANT / COMPANY_ADMIN can originate and disburse company cash unassisted.

---

## Round 1 evidence — happy-path JE tie-out (hand-calc match)

Claim 4 (acct01 create, chief01 approve+pay TRANSFER bank 1), docNo `07-2026-EX-0002`, JE 243:

| Line | net | vatRate | vat | recoverable | lineTotal |
|---|---|---|---|---|---|
| Taxi (cat 28) | 500.00 | 0 | 0.00 | n/a | 500.00 |
| Hotel (cat 26) | 1000.00 | 0.07 | 70.00 | yes | 1000.00 |
| Meal (cat 30) | 200.00 | 0.07 | 14.00 | **no** | 214.00 |

Header subtotal 1700.00 / VAT 84.00 / total 1784.00 — matched exactly by the server.

JE 243 posted lines:
```
Dr 5200 ค่าใช้จ่ายค่าบริการ   500.00
Dr 5200 ค่าใช้จ่ายค่าบริการ 1000.00
Dr 5200 ค่าใช้จ่ายค่าบริการ  214.00   (200 net + 14 non-recoverable VAT expensed)
Dr 1170 ภาษีซื้อ (Input VAT)  70.00   (only the recoverable line)
   Cr 1120 เงินฝากธนาคาร            1784.00
totalDebit 1784.00 == totalCredit 1784.00   ✔ balanced, no WHT line
```
CASH branch (JE 260): Dr 5200 100 / Dr 1170 7 / **Cr 1110 เงินสด 107** — credits cash account, balanced. Input VAT → **1170** in both branches. ✔

Trial balance stayed balanced throughout (baseline 515,575.64=515,575.64 → final 518,870.64=518,870.64).

---

## Artifacts left in co5 (for cleanup awareness — JEs immutable)
- Claims: 4 Paid (JE 243), 5 Draft, 6 Approved (1.07T huge test — unpaid), 7 Paid (JE 252, race), 8 Approved (reject-cycle + CRIT-2 target, rolled back), 9 Paid (JE 260, SoD).
- Attachments on claim 4: ids 8 (receipt.pdf), 9 (evil.html-as-pdf spoof), 10 (post-pay add), 11 (1MB), 12 (5MB).
- Posted JEs 243/252/260 (real GL in co5 dummy). CRIT-1/CRIT-2 500s created nothing (rolled back / rejected).
