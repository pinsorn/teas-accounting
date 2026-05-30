# UX/UI review — Purchase module (2026-05-30, cont.75)

> Driven via Playwright against the live app (BE :5080 + FE :3000, login admin).
> Screenshots: `.playwright-mcp/ux/*.png` (gitignored). Every page below was visited;
> the WHT-certificate print/download was exercised end-to-end (live PDF rendered + 2 copies).

## Pages walked (all rendered, 0 console errors except the transient /login below)

| # | Page | State |
|---|---|---|
| 00 | Dashboard | OK |
| 01 | ใบสั่งซื้อ list (`/purchase-orders`) | OK — filters, status badges, table |
| 02 | PO detail | OK — paper-doc view + right-rail chain + activity log |
| 07 | PO create (`/purchase-orders/new`) | OK — vendor search, line editor, live totals |
| 03 | 50ทวิ detail (`/wht-certificates/12`) | OK — payer/payee cards, income table, chain |
| 04 | 50ทวิ Print/PDF menu | OK — พิมพ์ / ดาวน์โหลด PDF (untracked, correct for WHT) |
| 05 | 50ทวิ **live PDF** (downloaded) | OK — official RD form filled, Thai intact, **2 copies** |
| 06 | ใบสำคัญจ่าย detail (posted) | OK — no edit/post actions (correct: immutable after post) |
| 09 | บันทึกใบกำกับภาษีซื้อ (VI) list | OK — consistent list pattern |
| 10 | ใบสำคัญจ่าย list | OK |
| 11 | ผู้ขาย (vendors) master | OK — search, type badge, active flag |
| 08 | รายงานเจ้าหนี้ค้างชำระ (AP aging) | OK — aging buckets, total, ส่งออก CSV |

**Verdict: the Purchase module is in good shape** — consistent layout (sidebar groups,
breadcrumb, command-search, filter row, status badges), polished paper-document detail
views with the PO→VI→PV→50ทวิ chain rail + activity log, and correct compliance behaviour
(posted docs expose no edit/post buttons; draft PO numbers show `#id` until issued).

## Findings (prioritized) — 🟠 need Ham's call before FE edits

1. **Duplicate vendor label (cosmetic, several create forms).** `VendorSelector.tsx:99`
   renders its own `ผู้ขาย / Vendor *`, while the create-form wrappers also render a
   `ผู้ขาย *` label above it → two stacked labels (see `07-po-new.png`). Fix: pick ONE
   owner — recommended is to drop the wrapper label in the create forms (the component
   label is canonical per the component's own history) **or** drop line 99 and have every
   caller pass a label. Multi-caller + subjective which wins → left for Ham (not auto-edited).

2. **Required `*` on optional filters.** The same `VendorSelector` hardcodes `*`, so vendor
   appears mandatory in the list/report **filter** rows (`01-po-list.png`, `08-ap-aging.png`)
   where it is optional. Fix: make the label/`*` a prop, off for filters.

3. **Date inputs show US `mm/dd/yyyy`.** Native `<input type="date">` renders in the browser
   locale, inconsistent with the Thai Buddhist display (`29 พ.ค. 2569`) used everywhere else
   (`07-po-new.png`, `08-ap-aging.png`). Cosmetic; a localized date control would align it.

4. **50ทวิ list income column shows a bare code.** `ประเภทเงินได้ (ม.40)` shows `8`/`4`/`7`
   with no label (`wht list`). Showing `40(8)` or the description would read better. Minor.

5. **Vendor master: foreign vendors not visually flagged.** AWS / Netflix show ประเภท
   `นิติบุคคล` with no foreign indicator (`11-vendors.png`). Minor.

## Operational note (not a product bug)

- `/login` returned 500 at first because the FE dev server had been started from the `U:`
  subst drive, which corrupts Next's module resolution (`Can't resolve './C:/…/next-dev.js'`,
  missing `.next/fallback-build-manifest.json`). Fix applied: stop dev, delete `.next`, restart
  `next dev` from the **real** absolute `…/code/frontend` path (never the subst drive). Worth a
  line in `runtime-gotchas.md`.

Nothing here blocks; all five UX items are cosmetic and gated on Ham's preference. No FE code
was changed in this pass (deliberately — subjective, multi-file, done while Ham is away).
