# MCP Agentic Expansion — Spec — 2026-06-18

Expands the MCP surface (shipped v1.3.0: sales read + create-draft, customer/product read). Builds on the existing design: in-process MCP, mcp-kind keys (read+create, no post), agent drafts → human approves+posts via deep-link, audit actor = api_key_name.

## Decisions (Ham, 2026-06-18)
- **Require list-only** (productId + customerId from master list; no ad-hoc): **AGENT/MCP path only** — humans/UI keep ad-hoc lines + custom.
- **Approve → agent notification:** **poll (pull)** — agent checks status tools; no push/webhook.
- **PDF delivery:** **download URL/link** (reuse existing pdf endpoint; not base64 inline).
- **Purchase agentic:** **FULL (read + create-draft)**.

## Phase E1 — Master-data write tools (agent)
- New tools: `create_customer` (scope `master.customer.manage`), `create_product` (scope `master.product.manage`).
- Auto (no human-approve — master data, not fiscal/immutable). **[ASSUMED — confirm]**
- Rationale: enables E2 (agent must be able to add a customer/product, then use it in a draft).

## Phase E2 — Require list-only on agent create-draft
- MCP create-draft tools (TI, quotation, receipt + purchase from E3): every line **must** carry a `productId` resolving to an existing company product; header `customerId` (or vendorId) must resolve to an existing record. **Reject** free-text/ad-hoc lines.
- **Price stays custom** (UnitPrice editable) — only the product/customer *identity* is enforced.
- Humans/UI unchanged (ad-hoc still allowed there).
- Impl: enforce in the MCP tool path (required productId in tool input + service-side existence check, company-scoped). Do NOT change the shared service validators (those serve the UI too).

## Phase E3 — Purchase agentic (read + create-draft)
- **Read** tools: list/get `purchase_orders`, `vendor_invoices`, `payment_vouchers`, `vendors` (scopes `purchase.*.read` / `master.vendor.read`).
- **Create-draft** tools: PO draft, Vendor-Invoice draft, Payment-Voucher draft, + `create_vendor` → return approval deep-link (scopes `purchase.*.create`).
- **No post** (human posts PV/VI). **[ASSUMED post=human — confirm]**
- Require-list (E2): vendor + product/expense-category from list. WHT on PV: agent drafts, human reviews.

## Phase E4 — PDF download tools
- `get_<doc>_pdf_url` (read scope) → returns a URL to the existing pdf endpoint for the doc.
- Agent/human fetches with `X-Api-Key` (add a pdf-read scope to the mcp default set), or a short-lived signed link.
- Gating: **posted/approved docs only?** or allow **draft preview** too? **[DECISION needed]**
- Docs: quotation, invoice, delivery order, tax invoice, receipt (+ purchase docs).

## Phase E5 — Approval-status poll (so agent learns of approval)
- Tools: `list_pending_approvals` (the agent's own drafts not yet posted) + `get_document_status(id)` → `{status, posted, docNo}`.
- Reuses M4 (`created_via_api_key_name` + `/reports/pending-agent-approvals`). Agent polls these to learn a draft was approved/posted (and can then fetch the PDF via E4).

## Phase E6 — mcp key scopes + frontend
- mcp default scope set += `master.customer.manage`, `master.product.manage`, purchase read+create, vendor read+manage, pdf-read.
- FE: purchase draft pages get the `?action=approve` CTA (like sales); api-keys create scope list + setup panel reflect the new tools; M4 badge/dashboard count extends to purchase drafts.

## RESOLVED DECISIONS (Ham, 2026-06-18)
1. **add-customer / add-product** via agent → **AUTO** (no human-approve — master data).
2. **purchase post** → **human-approve only** (agent drafts, human posts; same as sales).
3. **PDF gating** → **posted/approved docs only** (drafts get no PDF).
4. **require-list fallback** → agent must **`create_product` first (E1)** then use the productId (no inline-create-in-draft).
5. **Build** → **all of E1–E6**.

## Effort & batches
Large. Suggested subagent batches: E1 (master writes) → E2 (require-list, agent path) → E3 (purchase BE + MCP, biggest) → E4 (pdf url tools) → E5 (poll/status tools) → E6 (FE: purchase approve CTA + scopes + setup panel + badge). Backend batches sequential (shared teas_test + possible migrations); FE last.

## Compliance notes
- Purchase create-draft must respect existing PV per-line VAT guards (ม.82/5, ม.81), WHT, immutability (no agent post).
- Tenant isolation via key→company + RLS (automatic).
- Audit actor = api_key_name (M1) extends to new write tools.
