# Army leg B-mcp — MCP agent surface end-to-end (prod, co5)

Target: https://teas.kazaki-rio.com/mcp (v1.22.10), API key `army-mcp-co5` (MCP/AI-Agent type,
scopes create+read, no post). Playwright legs on https://teas.kazaki-rio.com: appr01 (widget),
sales01 (approve/send), audit01 (activity log).

**Correction note (2026-07-25):** the first pass of this report wrongly called the entire MCP
write surface broken (F1 CRITICAL). Fable root-caused it via prod server log (SSH): every failed
`tools/call` threw `System.ArgumentException: The arguments dictionary is missing a value for
the required parameter 'request'`. Each write tool takes ONE method parameter named `request`
holding the DTO; `tools/list`'s own `inputSchema` correctly nests every DTO field under
`properties.request.properties.*` — my original probes wrongly sent DTO fields flat at the
top level of `arguments`, never inside a `request` wrapper. Independently re-verified against my
own captured schema (`"required":["request"]`, all fields nested) before redoing the mission.
The real, smaller finding from that episode is F1 below (rewritten). Full end-to-end mission
(steps 2-7) redone below with the correct arg shape — all gates now pass.

## Done

1. MCP handshake (`initialize` → `tools/list`) — 86 tools, no post/approve/issue/send/void/
   cancel/reject verb present anywhere (structural deny, confirmed live).
2. `create_quotation_draft` with `arguments:{"request":{...}}` (real co5 IDs: customerId 5,
   productId 6, taxCodeId 25/VAT7) → **success**, quotation id 27 / DocNo QT-27 created as Draft.
3. `list_pending_approvals` (this key's own drafts) → non-empty, shows QT-27 with its
   approval deep-link.
4. Deny-path probe unchanged from the first pass: `post_quotation`/`send_quotation`/
   `approve_quotation`/`post_quotation_draft` all correctly `-32602 Unknown tool`.
5. Playwright, appr01 (`UxSwarm-2026-A3`) on prod co5: dashboard "ต้องทำ/แจ้งเตือน" widget
   **lit up** — "1 ใบเสนอราคารอออนุมัติจาก agent" (1 quotation pending agent approval), with a
   "ตรวจ" (review) link. Screenshot `B-mcp-02-widget-lit-appr01.png`.
6. Discovered mid-mission: appr01 (role APPROVER) holds **zero** `sales.quotation.*`
   permission (confirmed via `/me/permissions` — matches RBAC seed
   `270_seed_quotation_chain_perms.sql`, which grants `sales.quotation.manage` only to
   SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/AR_CLERK/SALES_STAFF, never APPROVER). Clicking
   the widget's own "ตรวจ" link takes appr01 to `/quotations`, which renders an empty
   "ไม่มีข้อมูล" list — appr01 cannot itself view or act on this doc type. Switched to
   **sales01** (SALES_STAFF, holds `sales.quotation.manage`) as "the appropriate role" per the
   dispatch's own wording — see F2.
7. sales01 opened QT-27 (`B-mcp-07-sales01-qt27-draft.png`, status Draft) and clicked "ส่ง"
   (Send) → **`POST /api/proxy/quotations/27/send` → HTTP 204**, status flipped
   Draft → Sent (`B-mcp-08-sales01-qt27-sent.png`).
8. audit01 (AUDITOR, holds `report.audit.read`) read `GET /api/proxy/quotations/27/activity` →
   200, full audit trail (`B-mcp-09-audit01-activity.png`):
   ```json
   [{"actor":"army-mcp-co5","action":"Created","fromStatus":null,"toStatus":"Draft",
     "at":"2026-07-25T05:12:29.696+00:00","note":null},
    {"actor":"sales01","action":"Sent","fromStatus":"Draft","toStatus":"Sent",
     "at":"2026-07-25T05:16:24.848+00:00","note":null}]
   ```
   **Actor identity confirmed**: the Draft-creation row is attributed to `army-mcp-co5` (the
   API key name, not a human user); the Sent row is attributed to the human (`sales01`) who
   approved it.
9. Re-checked as appr01: `GET /api/proxy/reports/pending-agent-approvals` → all-zero, dashboard
   widget back to the green "ไม่มีรายการค้าง — เรียบร้อย" empty state
   (`B-mcp-10-widget-cleared-appr01.png`) — **widget correctly cleared** after the human action.

All temp scripts (`frontend/army-B-mcp*.mjs`, scratchpad `.mjs` probes) deleted after the run.
API key never left the secrets file into any output.

## Evidence (gate-by-gate)

| Gate | Result |
|---|---|
| tools/list captured | 86 tools, JSON captured in scratchpad during the run (not committed) |
| 1 draft created via MCP (2xx) | QT-27, `create_quotation_draft` via nested `request` arg |
| widget lit + screenshot | `B-mcp-02-widget-lit-appr01.png` |
| approve path 2xx | `POST quotations/27/send` → 204; status Draft→Sent |
| actor identity verified | activity log `actor:"army-mcp-co5"` (Created) → `actor:"sales01"` (Sent) |
| deny-path (no-post) recorded | 4/4 fabricated action-tool names → `-32602 Unknown tool` |
| no tenant leak | every screenshot/API call confirmed co5 ("บริษัท ทดสอบ VAT (DUMMY) จำกัด") throughout |

Blast radius: **1 document created** (QT-27), well within the ≤3 cap. No ยืนยัน/ปิดงวด, no
year-end, no payroll/master-data mutation.

## Findings

**F1 — REWRITTEN, was CRITICAL, now LOW-MEDIUM — malformed `tools/call` args surface the SDK's
generic swallowed error instead of a clean parameter error.** Original repro (my own mistake:
flat top-level DTO fields instead of `{"request": {...}}`) produced HTTP 200 /
`"An error occurred invoking '<tool>'."` for every write tool tried. Server log (Fable, via SSH)
showed the actual exception: `System.ArgumentException: The arguments dictionary is missing a
value for the required parameter 'request'`. `McpErrorSurfacingFilter.cs` (`backend/src/
Accounting.Api/Mcp/McpErrorSurfacingFilter.cs`) catches exactly 4 exception types
(`McpE2Exception`, `DomainException`, `FluentValidation.ValidationException`, `JsonException`);
`ArgumentException` isn't one of them, so it falls through to the SDK's own generic catch-all
instead of a diagnosable `[mcp.bad_input]`-style message. **Real gap**: an MCP client (or an
LLM agent) that gets the arg shape wrong sees only "An error occurred invoking 'x'" with zero
clue what's wrong — the schema is correct and available via `tools/list`, but the runtime error
doesn't point back to it. Suggested fix (not applied — army legs are read/verify only): add a
`catch (ArgumentException ex)` arm to the filter (`[mcp.bad_input] {ex.Message}`, same shape as
the existing `JsonException` arm) so a shape mismatch reads as clearly as a validation error.

**F2 — LOW-MEDIUM, likely by-design but worth a product-decision confirmation — the
pending-agent-approvals widget's "ต้องทำ" alert is shown to APPROVER for all 6 tracked doc
types, but APPROVER can only ACT on the purchase-side 3 (PO/VI/PV); for quotation (confirmed
live) — and by the same RBAC pattern, presumably tax invoice/receipt too — APPROVER holds no
`.manage` permission at all, so the widget's own "ตรวจ" link dead-ends on an empty list.**
RBAC seed `270_seed_quotation_chain_perms.sql` grants `sales.quotation.manage` only to
SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/AR_CLERK/SALES_STAFF — never APPROVER — which lines
up with `628_seed_auditor_read_approver_grant.sql`'s own comment that APPROVER's real job is
approving PO/PV (COMPANY_ADMIN/CHIEF_ACCOUNTANT being "the other PO/PV-approving roles"). This
is plausibly intentional SoD (sales docs are sent by the sales/AR roles themselves, not a
generic approver) rather than a bug, but the widget doesn't communicate that distinction — an
APPROVER seeing "1 ใบเสนอราคารอออนุมัติจาก agent" has no way to know they personally can't clear
it and must hand it to sales/AR. Not fixed here (Ponytail — no UI change without a product
decision on whether this is the intended split).

## Unbuilt-vs-untested classification

- **Built + verified working end-to-end (live, this run):** MCP JSON-RPC handshake; MCP read
  tools; `create_quotation_draft` write path (once called correctly); structural post/approve
  exclusion; pending-agent-approvals widget's full lifecycle (lights up on a real agent draft →
  human sends it → widget clears); document activity log correctly stamping the API-key name
  as the actor on the agent-created row, separate from the human's own action row.
- **Not a gap, tester error (see F1):** the original "every MCP write tool is broken" claim —
  false alarm, root-caused and corrected in this revision.
- **Real, smaller finding (F1):** error-surfacing coverage gap for `ArgumentException` on
  malformed MCP tool args.
- **Real, smaller finding, needs a product call (F2):** widget-audience mismatch for
  non-purchase doc types under the APPROVER role.
