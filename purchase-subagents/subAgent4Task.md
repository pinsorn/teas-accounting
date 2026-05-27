# subAgent4 — Phase D: PaperDocument + DocumentChain + PrintMenu adoption (FE)

**Read first:** `_ENV-BRIEFING.md` (esp. `next build` native-path rule) · `planPurchase.md` → **Phase D** (D0–D6) · `docs/Answer-Sana-Backend30.md` §2 Phase D.

**Skill/plugin/MCP allocation:**
- `next-best-practices` (RSC boundaries, App Router conventions)
- `superpowers:verification-before-completion` (gate before claiming done)
- MCP `chrome-devtools` or `playwright` — load each detail page to confirm PaperDocument + chain + PrintMenu render

**Depends on:** Phase C merged (PO/PV `/pdf?copy=` endpoints exist).

**D0 — read before editing (do NOT pre-assume the diff):** `components/doc/DocumentChain.tsx` (its doctype union — currently likely Sales-only), `components/ui/PrintMenu.tsx` + `components/doc/ChainRowPrint.tsx` (doctype prop), `components/ui/StatusBadge.tsx` (status→Thai map), and `app/(dashboard)/tax-invoices/[id]/page.tsx` as the adoption template.

**Scope:**
- **D1:** extend the doctype enum/union in `DocumentChain.tsx` + `PrintMenu.tsx` + `ChainRowPrint.tsx` to add `PO | VI | PV | WHT` (Thai labels) **without breaking existing Sales doctypes**.
- **D2:** verify `Api/Endpoints/DocumentCrossRefEndpoints.cs` (+ service) returns Purchase doctypes for `GET /documents/chain?docType=PO&id=…`. If Sales-only, extend the resolver to PO→VI→PV→WHT (mirror Sales). **If non-trivial, STOP and flag to the main agent** — may need a BE sub-task.
- **D3:** PO/VI/PV/WHT `[id]/page.tsx` — wrap in `<PaperDocument>`, add `<DocumentChain>` panel, wire `<PrintMenu>` → `/{doc}/{id}/pdf?copy=true|false` (send `?copy=true`, never `1`; never `window.print()` — BUG #SR8). WHT keeps its bespoke 50ทวิ endpoint.
- **D4:** Posted detail read-only (no enabled edit fields when `status==="Posted"` — §4.2).
- **D5:** the four list `page.tsx` — `<MascotGreeting>` empty state, `<FilterBar>` (status chips + vendor + date range), `<StatusBadge>` Thai, headers via `useTranslations()` (no hardcoded EN). Edit `messages/th.json` + `en.json` for any new keys.

**Out of scope:** AP Aging page (Phase E), PO /new form (Phase F), Purchase menu items + Settings route (Ham locked — do not touch).

**Verification gate (paste output):**
- `tsc --noEmit` → **0**
- `next build` → **0/0** from NATIVE `<ROOT>\frontend` (NOT `U:`), `next dev` stopped first
- manual (chrome/playwright): each detail page shows PaperDocument + DocumentChain + PrintMenu; Posted = read-only

**Return:** doctype-enum diff, BE chain coverage finding (D2), files touched, tsc/build output, manual-render evidence, conflicts. **No git commit.**
