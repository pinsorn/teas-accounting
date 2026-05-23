# Question-Backend15 — `audit.activity_log` not written for sales doctypes

**From:** Claude Code · **Date:** 2026-05-21 · **Sprint:** 13j-FE (D1) · **Severity:** non-blocking

## Finding

Sprint 13j-FE D1 added `GET /{docType}/{id}/activity` (8 sales doctypes) +
`components/doc/ActivityLog.tsx`. Endpoint is real, tenant-scoped, read-only.

**But:** `audit.activity_log` currently has writes from **`ApiKeyService` only**
(`EntityType = "ApiKey"`). No sales command handler (Quotation/SO/DO/TI/Receipt/CN/DN/BN
create/post/issue/accept/convert/cancel/deliver) writes a transition row. So the endpoint
returns `[]` for every sales doc and `ActivityLog` renders a graceful empty state
("ยังไม่มีประวัติกิจกรรม").

This contradicts CLAUDE.md §4.8 ("Every state change → `audit.activity_log` entry") — a
**pre-existing compliance gap**, not introduced by this sprint.

## Why not fixed here

- Out of scope: 13j-FE is "FE visual only on SALES" (Answer-29 §1).
- Backfilling writes touches every sales command handler incl. posting/immutability paths
  → CLAUDE.md §9 "ASK before touching posting / e-Tax". High-risk, needs its own backend
  sprint + tests.
- §6 / §0a: did not fabricate activity data to fill the UI.

## Ask Ham / Sana

1. Confirm a dedicated backend sprint to add transition logging across the 8 sales
   doctypes (canonical `EntityType` = Quotation/SalesOrder/DeliveryOrder/TaxInvoice/Receipt/
   CreditNote/DebitNote/BillingNote; `ActivityType` per action; `from`/`toStatus` in
   metadata or new columns). Suggest folding into 13k (Security/RBAC/Audit) or 13L (DevOps).
2. Confirm the `ActivityEntryDto` shape `{actor, action, fromStatus, toStatus, at, note}`
   is the contract to log toward (so the FE needs no change when writes land).

No code change requested until confirmed.
