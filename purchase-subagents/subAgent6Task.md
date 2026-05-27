# subAgent6 — Phase F: Bug pass + PO form lift (FE)

**Read first:** `_ENV-BRIEFING.md` · `planPurchase.md` → **Phase F** (F0–F5) · `docs/Answer-Sana-Backend30.md` §2 Phase F.

**Skill/plugin/MCP allocation:**
- `next-best-practices`
- `superpowers:systematic-debugging` if a bug surfaces during the pass
- MCP `chrome-devtools`/`playwright` — exercise PO /new submit + verify Thai toasts

**Depends on:** Phase D (shared FE components / messages).

**F0 — read target:** `app/(dashboard)/vendor-invoices/new/page.tsx` (the "VI quality" bar — LineItemsTable usage).

**Scope:**
- **F1 PO /new lift:** `app/(dashboard)/purchase-orders/new/page.tsx` — replace the 1-line form (hardcoded `taxCodeId:1, taxCode:'VAT7', taxRate:0.07` at `:36`) with `<LineItemsTable>` multi-line + `<ProductPicker>` per line (free-text fallback OK per Plan §7) + real VAT-code selector (`tax.tax_codes`) + per-line discount % + specific Thai toast errors (no generic "เกิดข้อผิดพลาด" — BUG #SR9). Keep submit→detail redirect.
- **F2 Expense-category list:** create `app/(dashboard)/settings/expense-categories/page.tsx` (read-only) listing the 19 seeded `sys.expense_categories`; mirror `settings/wht-types`. No CRUD.
- **F3 Toast Thai-only audit:** grep `vendor-invoices/new`, `payment-vouchers/new`, `purchase-orders/new` for English fallback error strings → `useTranslations('common').error` / specific keys.
- **F4 Column-header audit:** replace hardcoded EN in the Purchase list pages with i18n.

**Out of scope:** Settings route relocation (Ham locked), Purchase menu items, BE.

**Verification gate (paste output):**
- `tsc --noEmit` → 0 · `next build` → 0/0 (native path)
- PO /new = VI quality; expense-categories shows 19; no EN bleed in toasts/headers; PO/VI/PV/WHT/AP-Aging reachable with no console errors

**Return:** files touched, before/after of the PO form approach, bug appends to `bugPurchase.md`, tsc/build output, conflicts. **No git commit.**
