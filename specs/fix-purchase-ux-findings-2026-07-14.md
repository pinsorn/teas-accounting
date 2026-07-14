# Spec: fix purchase-side UX/spec findings F1–F29 (prod UX test 2026-07-14)

Status: DRAFT for Ham review. Source: PROGRESS-purchase-uxtest.md (full findings log +
repro evidence). Test artifacts live in BU TEST @ Repttown (PO-TEST-0001/0002,
VI-TEST-0001, PV-OFFI-0001, PV-PROF-0001, WT-0001) — usable as live repro fixtures.

Verified-good (no work): chain PO→VI→PV→50ทวิ posts correctly; AP settlement flips VI
to PAID; vendor ledger reconciles to GL 2110; 50ทวิ picks ภ.ง.ด.3/53 by vendor type;
WHT rates auto-fill by income type; non-VAT vendor messaging on PV; ม.86/4 attach-file
completeness tracking; live A4 preview.

## Ham decisions needed BEFORE dispatch (blocking the marked items)
- [ ] D1 (→WP1.2): non-VAT company posting recoverable input VAT (F27) — block VAT
      entry entirely on non-VAT co, or allow but force non-recoverable? (RD position:
      non-VAT registrant cannot claim; VAT paid = cost.)
- [ ] D2 (→WP3.3): PO draft editing (F6) — add edit, or is create-only intentional?
- [ ] D3 (→WP3.4): "ปิด" PO semantics (F29) — what should closing do? (Today: no-op.)
- [ ] D4 (→WP4): SoD text on PV (F25) — enforce for non-admin only? Then text should
      say so; or drop the text.
- [ ] D5 (→WP1.1): VAT-rate input UX — switch UI to percent (display 7, store 0.07),
      or keep fraction + hard validation? (Recommend percent UI; MCP/API stays fraction.)

## WP1 — MONEY/COMPLIANCE (footgun zone → Opus DESIGN → Sonnet implement, Tier-2 Opus review)
- [ ] 1.1 F15+addendum: VAT-rate + WHT-rate fields are fractions behind %-labeled inputs,
      no bounds. Fix per D5: percent-presentation layer on VI "อัตรา VAT" and PV
      "หัก ณ ที่จ่าย %" + validation (VAT ∈ {0, 7} typical, hard-cap 0..1 fraction /
      0..100 percent; out-of-range = inline error, not silent accept).
      Accept: typing 7 yields ฿210 VAT on ฿3,000 base; typing 700 rejected.
- [ ] 1.2 F27 (per D1): non-VAT company VI VAT handling. Server-side rule + FE mirror.
      Accept: on vatMode=false co, posting VI with recoverable VAT is impossible.
- [ ] 1.3 F14: VI line pulled from PO defaults vatRate=0 even when company+vendor are
      both VAT-registered (on VAT co, per co2 verification pull is correct — restrict fix
      to deriving from vendor/company when PO line has no tax, not blanket 0.07).
      Accept: on co2, PO with no VAT data → linked VI line defaults to vendor-derived rate.
- [ ] 1.4 F13: vendor "จดทะเบียน VAT" requires 13-digit เลขผู้เสียภาษี (create+edit,
      server-side validation; existing rows grandfathered with warning on VI create).
- [ ] 1.5 F20: expense categories without default GL account (COGS on Repttown).
      Two parts: (a) seed/backfill mapping for auto-seeded categories (relates to co2/co3
      CreateAsync-bypass gap in memory), (b) FE: disable/badge categories with no account
      in the dropdown instead of 422 at save. Accept: COGS selectable+savable OR visibly
      marked unusable before save.

## WP2 — AUTH/SESSION UX (F16 family; Opus design for token strategy, Sonnet FE)
- [ ] 2.1 Token refresh: silent refresh or sliding session (current ~25-30 min hard
      expiry). Design owns choice (refresh token vs extended TTL + idle logout).
- [ ] 2.2 Global 401 handler: expired session mid-form → modal "session หมดอายุ —
      login ใหม่" + preserve form state (at minimum: don't leave buttons dead; F1 stale
      shell redirect included). Accept: expire token manually → any save shows the modal,
      re-login → same form still filled.
- [ ] 2.3 F21: hanging duplicate POST after failed save (trailing-slash /api/proxy double
      request) — find root cause in proxy route handlers; a failed save must leave the
      form usable (no reload needed).
- [ ] 2.4 F19: error toasts — Thai translations for domain errors, longer/sticky
      duration for errors, keep EN detail collapsible.

## WP3 — FLOW/DISCOVERABILITY (Sonnet direct, spec-airtight)
- [ ] 3.1 F18: add "+ บันทึกใบกำกับภาษีซื้อ" button on /vendor-invoices list; fix stale
      subtitle ("สร้างจากใบสำคัญจ่าย (PV → บันทึก)" no longer true).
- [ ] 3.2 F8: approved-PO action bar gets "บันทึกใบกำกับภาษีซื้อ" CTA (→ /vendor-invoices/new
      ?fromPurchaseOrderId=; primary CTA before สร้างใบสำคัญจ่าย to match chain order).
- [ ] 3.3 F6 (per D2): PO draft edit (reuse create form, PUT exists per API manual).
- [ ] 3.4 F29 (per D3): PO close — implement semantics or remove button.
- [ ] 3.5 F24: PV from ชำระด้วยใบสำคัญจ่าย prefills vendor + line (desc "ชำระ <VI docNo>",
      amount = VI outstanding) — user adjusts, not re-keys. Accept: one click from posted
      VI → PV form complete except payment method review.
- [ ] 3.6 F7/F28: confirmation dialog on PO approve and PV approve/post (mirror VI post
      modal: totals + immutable warning). One shared confirm component.
- [ ] 3.7 F9: "ส่ง PO ให้ vendor" → relabel "บันทึกว่าส่งแล้ว" (or add real email later —
      out of scope now) + confirm/undo of the stamp.
- [ ] 3.8 F4: vendor picker modal "+ เพิ่มผู้ขายใหม่" quick-create (name/type/VAT only).

## WP4 — POLISH/i18n/a11y (Haiku-able mechanical batch where zero-judgment, else Sonnet)
- [ ] 4.1 F2: Thai BE date display for all date inputs (or dual hint) — pick one pattern
      app-wide; native input stays, add BE hint text under field.
- [ ] 4.2 F3: PO/VI list "หน่วยธุรกิจ" column → BU code/name, not #id.
- [ ] 4.3 F11: activity log event labels → Thai ("Created → Draft" etc.).
- [ ] 4.4 F17: restore last-used company after re-login (localStorage).
- [ ] 4.5 F10: refresh เอกสารอ้างอิง/ประวัติกิจกรรม panels after approve/post/mark-sent.
- [ ] 4.6 F12: form inputs get proper label association (a11y) — vendor form first.
- [ ] 4.7 F22: VI post-confirm title "ใบรับวางบิล (ผู้ขาย)" → "ใบกำกับภาษีซื้อ".
- [ ] 4.8 F23: user-facing refs use docNo not internal #id once issued (PV subtitle,
      50ทวิ "อ้างอิงใบสำคัญจ่าย: PV #3").
- [ ] 4.9 F25 (per D4): align SoD text with actual enforcement.
- [ ] 4.10 /wht-certificates list: แบบยื่น "Pnd3"→"ภ.ง.ด.3", ม.40 column "8"→"40(8) ค่าบริการ".

## No action
- F5: PO VAT display gated by vatMode && vendor.vatRegistered = by design (wiki entry
  exists; manual already documents the conditional).
- F26: duplicate of F16 (resolved — PV post works on live session).

## Ordering & notes
- WP1 + WP2.1/2.2 = one release (money + auth); WP3/WP4 can trail.
- Every WP1 item: server-side rule is the source of truth, FE mirrors. JE/GL code paths
  untouched (settlement verified good) — changes stop at validation/derivation layer.
- Manual ch.5: the F15 warning admonition added 2026-07-14 gets REMOVED when 1.1 ships
  (walkthrough 05.02 step-03b then re-captured to show the percent field).
- Test fixtures: reuse BU TEST docs for regression; add integration tests per WP1 item
  (fraction/percent boundary, non-VAT co VI, category-without-account).
