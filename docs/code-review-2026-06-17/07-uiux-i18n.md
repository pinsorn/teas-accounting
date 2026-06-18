# UI/UX & i18n Review — TEAS — 2026-06-17

## Summary

**Overall posture:** Good structural foundation. i18n key parity is perfect (1 401 keys in both
`th.json` and `en.json`, zero diff). The shadcn/ui + DaisyUI component layer is used consistently
across list screens. The main risk areas are: (1) a compliance-UI rule violation where `vatRate` is
surfaced as an editable field in the super-admin Companies screen; (2) a cluster of hardcoded
user-facing strings that bypass `t()`; (3) per-screen accessibility gaps (missing `aria-label` on
SVG charts, English-only `aria-label="close"` on every modal backdrop, inline English aria-labels on
table row inputs); and (4) minor locale-consistency issues.

| Severity | Count |
|---|---|
| Critical | 1 |
| High | 3 |
| Medium | 5 |
| Low | 3 |
| **Total** | **12** |

i18n key-set diff: **0 keys** in TH not EN, **0 keys** in EN not TH. Full parity confirmed.

---

## Findings

---

### [CRITICAL-01] VAT rate/mode exposed as editable UI in Companies settings

**Severity:** Critical
**File:** `frontend/app/(dashboard)/settings/companies/page.tsx` lines 79, 96, 157, 167, 273-274, 420-421
**Confidence:** [Confirmed]

**Why it matters:** CLAUDE.md §4.6 and §10 state `vat_rate/vatMode` must NEVER be exposed in a
user-facing settings UI — it is master data, settable only via `POST/PUT /companies` with super-admin
permission. The Companies settings screen is accessible to any user with `Master.CompanyManage`, and it
(a) renders a `vatRate` column in the list table, (b) has a `vatRatePct` text input field in both the
Create and Edit modals.

**Quoted code — list column:**
```tsx
// line 79
<th className="text-right">{t('vatRate')}</th>
// line 96
<td className="text-right tabular-nums">{toPercent(c.vatRate)}%</td>
```

**Quoted code — create/edit form input:**
```tsx
// line 273-274
<Field label={t('vatRatePercent')} value={f.vatRatePct} type="number"
  testId="co-new-vatrate" onChange={(v) => set({ vatRatePct: v })} />
// line 420-421
<Field label={t('vatRatePercent')} value={f.vatRatePct} type="number"
  testId="co-edit-vatrate" onChange={(v) => set({ vatRatePct: v })} />
```

**Note:** The comment at line 24 ("vatRate travels as a FRACTION (0.07); the UI edits a PERCENT (7)")
confirms this is a fully wired, editable control, not a display-only read.

**Fix:** Per CLAUDE.md §4.6 the vatRate/vatRegistered fields may only be changed via the `POST/PUT
/companies` API by a super-admin. The list-table `vatRate` column and the modal `vatRatePct` field
should either be removed from this screen or restricted to read-only display (no `<Field … onChange>`).
The permission gate for this page is `Master.CompanyManage` which is NOT the same as the intended
restriction to only a super-admin context.

---

### [HIGH-01] Hardcoded Thai strings in shared components bypass next-intl

**Severity:** High
**Files:**
- `frontend/components/doc/ActivityLog.tsx` lines 43, 47, 49
- `frontend/components/doc/ChainRowPrint.tsx` lines 26, 30, 44, 45, 53
- `frontend/components/doc/RelatedDocs.tsx` line 41
- `frontend/components/doc/ReceiptWhtCertSection.tsx` lines 31, 38, 48, 55, 59
- `frontend/components/forms/AdjustmentNoteForm.tsx` line 30
- `frontend/components/forms/DeliveryOrderPicker.tsx` lines 54-55
**Confidence:** [Confirmed]

**Why it matters:** These are shared components used across multiple screens. Hardcoded Thai strings
mean EN locale renders Thai text — breaking the EN UX — and they can never be updated via the
translation file.

**Quoted examples:**
```tsx
// ActivityLog.tsx:43
<h3 className="text-[15px] font-bold text-ink-900">ประวัติกิจกรรม</h3>
// ActivityLog.tsx:47
<p className="text-[13px] text-ink-500">กำลังโหลด…</p>
// ActivityLog.tsx:49
<p className="text-[13px] text-ink-500">ยังไม่มีประวัติกิจกรรม</p>

// ChainRowPrint.tsx:26
toast.warning('ต้นฉบับเคยถูกพิมพ์แล้ว — พิมพ์เป็นสำเนาแทน');
// ChainRowPrint.tsx:30
toast.error('บันทึกการพิมพ์ไม่สำเร็จ — ออกเป็นสำเนา');

// AdjustmentNoteForm.tsx:30
reason: z.string().min(1, 'เหตุผลบังคับตามกฎหมาย'),

// DeliveryOrderPicker.tsx:54-55
disabledHint = 'เลือกลูกค้าก่อน',
ariaLabel = 'อ้างอิงใบส่งของ',
```

**Fix:** Add translation keys for each hardcoded string and call `useTranslations(…)` inside each
component. For the Zod validation message in `AdjustmentNoteForm`, pass the translated string from
the calling component via a prop, or use a refine callback that reads from the hook.

---

### [HIGH-02] Hardcoded English `aria-label="close"` on every modal backdrop (accessibility)

**Severity:** High
**Files:** 13 screens — `payroll/page.tsx:139`, `settings/api-keys/page.tsx:191`,
`settings/business-units/page.tsx:209`, `settings/companies/page.tsx:298,447`,
`settings/company/page.tsx:218`, `settings/employees/page.tsx:312`, `settings/roles/page.tsx:246,324,458`,
`settings/users/page.tsx:231`, `settings/wht-types/page.tsx:239,269`
**Confidence:** [Confirmed]

**Why it matters:** Every DaisyUI modal backdrop button carries `aria-label="close"` in English.
Screen readers on TH locale announce "close" instead of "ปิด". This affects every modal in the
settings section.

**Quoted pattern (repeated 13+ times):**
```tsx
<button className="modal-backdrop" aria-label="close" onClick={…} />
```

**Fix:** Replace `aria-label="close"` with `aria-label={tc('close')}` (add `close` key to
`common` namespace in both message files if not already present).

---

### [HIGH-03] Inline English `aria-label` on table row inputs in receipts/new

**Severity:** High
**File:** `frontend/app/(dashboard)/receipts/new/page.tsx` lines 452, 456, 460, 516, 600
**Confidence:** [Confirmed]

**Why it matters:** Dynamic row aria-labels like `` `qty ${i + 1}` `` are English-only. Screen
readers announce "qty 1", "unitPrice 1", etc., breaking accessibility for Thai-primary users.

**Quoted code:**
```tsx
// line 452
<AmountInput value={l.quantity} step={1} aria-label={`qty ${i + 1}`}
// line 456
<AmountInput value={l.unitPrice} aria-label={`unitPrice ${i + 1}`}
// line 460
<AmountInput value={l.amount} aria-label={`lineAmount ${i + 1}`}
// line 516
<AmountInput value={a.appliedAmount} aria-label={`appliedAmount ${i + 1}`}
```

**Fix:** Translate the field name segment: e.g., `` aria-label={`${t('qty')} ${i + 1}`} ``.

---

### [MEDIUM-01] Buddhist-era year displayed alongside CE in year selector

**Severity:** Medium
**File:** `frontend/app/(dashboard)/reports/tax-summary/page.tsx` line 43
**Confidence:** [Confirmed]

**Why it matters:** CLAUDE.md §5 states "CE calendar only (never Buddhist internally)" and "convert
to Asia/Bangkok only at display". Displaying `{y} ({y + 543})` shows a Buddhist-era annotation that
may confuse EN locale users and potentially creates inconsistency with the CE-only date display rule
for other date fields.

**Quoted code:**
```tsx
// line 43
<option key={y} value={y}>{y} ({y + 543})</option>
```

**Note:** Thai government filings commonly reference BE years, so this may be intentional UX. Flag
for owner decision — not removing, just calling it out.

**Fix:** Consider driving this from locale: show `{y}` only for EN locale, `{y} (พ.ศ. {y + 543})`
for TH locale. Or add the `พ.ศ.` prefix (currently it just appends a bare number) for clarity in TH.

---

### [MEDIUM-02] SVG bar chart in tax-summary missing `aria-label` on `role="img"`

**Severity:** Medium
**File:** `frontend/app/(dashboard)/reports/tax-summary/page.tsx` line 227
**Confidence:** [Confirmed]

**Why it matters:** The dashboard SVG chart at `frontend/app/(dashboard)/page.tsx:219` correctly
uses `aria-label={t('trend.title', …)}`. The tax-summary page uses the same `role="img"` pattern
but omits `aria-label`, making it an unlabelled image for screen readers.

**Quoted code:**
```tsx
// tax-summary/page.tsx:227
<svg viewBox={`0 0 ${W} ${H}`} className="h-52 w-full" role="img">
```

**vs. dashboard/page.tsx:219 (correct):**
```tsx
<svg … role="img" aria-label={t('trend.title', { year: … })}>
```

**Fix:** Add `aria-label={t('chart.title')}` (or equivalent key) to the tax-summary SVG.

---

### [MEDIUM-03] Dashboard KPI compact number formatter (`kBaht`) produces untranslated "M"/"K"

**Severity:** Medium
**File:** `frontend/app/(dashboard)/page.tsx` lines 24-26
**Confidence:** [Confirmed]

**Why it matters:** The `kBaht` function formats large numbers as "1.5M" or "120K" using English
abbreviations. Thai users conventionally use ล้าน / พัน. The formatter also uses `toFixed(0)` without
a locale argument, so number formatting (decimal separators) is browser-dependent.

**Quoted code:**
```tsx
function kBaht(n: number): string {
  const a = Math.abs(n);
  if (a >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (a >= 1_000) return `${(n / 1_000).toFixed(0)}K`;
  return n.toFixed(0);
}
```

**Fix:** Locale-branch the suffix: use `ล้าน`/`พัน` for TH locale, "M"/"K" for EN; or use
`Intl.NumberFormat('th-TH', { notation: 'compact' })` which handles this automatically.

---

### [MEDIUM-04] `toLocaleString()` called without locale argument on product unit prices

**Severity:** Medium
**File:** `frontend/app/(dashboard)/settings/products/page.tsx` line 106
**Confidence:** [Confirmed]

**Why it matters:** `toLocaleString()` with no arguments uses the browser's OS locale, which may
be `en-US` on non-Thai machines. Thai locale formats large numbers with different digit grouping.

**Quoted code:**
```tsx
// line 106
return <span className="tabular-nums">{v == null ? '—' : v.toLocaleString()}</span>;
```

**Fix:** `v.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })` or
use the shared `formatTHB` helper already used elsewhere in the codebase.

---

### [MEDIUM-05] Hardcoded English `placeholder="YYYYMM"` in payroll period input

**Severity:** Medium
**File:** `frontend/app/(dashboard)/payroll/page.tsx` line 118
**Confidence:** [Confirmed]

**Why it matters:** The format hint for the payroll period field is English-only. Thai users may
not recognise "YYYYMM". Should be `ปีเดือน เช่น 202601` or driven by a translation key.

**Quoted code:**
```tsx
// line 118
<input className="input input-bordered font-mono" value={period} maxLength={6}
  placeholder="YYYYMM"
  onChange={(e) => setPeriod(e.target.value.replace(/\D/g, ''))} />
```

**Fix:** Replace with `placeholder={t('periodPlaceholder')}` and add the key to both message files.

---

### [LOW-01] Hardcoded Thai strings in paper print components (intentional but undocumented)

**Severity:** Low
**Files:** `frontend/components/paper/PaperItems.tsx` lines 12-16; `PaperHead.tsx:36`;
`PaperMeta.tsx:39`
**Confidence:** [Confirmed]

**Why it matters:** The paper/print components use hardcoded Thai column headers (จำนวน, หน่วย,
ราคา/หน่วย, ส่วนลด, จำนวนเงิน). These are legal document print templates — Thai is correct and
required by ม.86/4 — but the column in `PaperItems.tsx:11` bilingual label
`รายการ / Description` is inconsistent (other columns are TH-only).

**Quoted code:**
```tsx
// PaperItems.tsx:12-16
<th className="num" style={{ width: 70 }}>จำนวน</th>
<th style={{ width: 60 }}>หน่วย</th>
<th className="num" style={{ width: 100 }}>ราคา/หน่วย</th>
<th className="num" style={{ width: 70 }}>ส่วนลด</th>
<th className="num" style={{ width: 110 }}>จำนวนเงิน</th>
```

**Fix:** No change required for legal compliance (TH is correct on tax invoices). Consider
standardising the bilingual label pattern on the รายการ column or removing the English half.

---

### [LOW-02] `en-CA` locale used in AP-aging `bangkokToday()` function

**Severity:** Low
**File:** `frontend/app/(dashboard)/reports/ap-aging/page.tsx` lines 15-22
**Confidence:** [Confirmed]

**Why it matters:** `en-CA` is used to produce an ISO-like `YYYY-MM-DD` string for the API
call (not for display), which is a reasonable trick. However, it's a locale-coupling smell —
`en-CA` behavior is not guaranteed to produce ISO format across all environments. The comment
explains the intent (Bangkok timezone) but not the locale choice.

**Quoted code:**
```tsx
return new Intl.DateTimeFormat('en-CA', {
  timeZone: 'Asia/Bangkok',
  year: 'numeric', month: '2-digit', day: '2-digit',
}).format(new Date());
```

**Fix:** Use a more explicit approach: `new Date().toLocaleDateString('sv-SE', { timeZone: 'Asia/Bangkok' })`
(`sv-SE` reliably produces YYYY-MM-DD) or build the string directly from Bangkok-offset date parts.

---

### [LOW-03] `aria-label="loading"` on onboarding spinner is English

**Severity:** Low
**File:** `frontend/app/onboarding/page.tsx` line 275
**Confidence:** [Confirmed]

**Quoted code:**
```tsx
<span className="loading loading-spinner loading-lg text-peach-600" aria-label="loading" />
```

**Fix:** `aria-label={t('loading')}` using the common namespace.

---

## Verified GOOD

1. **i18n key parity:** th.json and en.json both have exactly 1 401 keys with zero divergence. [Confirmed]
2. **Loading / error / empty states:** All list screens pass `isLoading`/`isError` to the shared
   DataTable component. Detail pages follow the `if (q.isLoading) return …` guard pattern consistently.
3. **Destructive-action confirmation:** `useConfirm()` is called before all delete, cancel, and void
   actions across quotations, invoices, payroll, PND30 auto-submit — no bare unguarded mutations found.
4. **Table overflow handling:** All data tables are wrapped in `overflow-x-auto`. `min-w-*` column
   hints are used on wide columns to guide horizontal scroll.
5. **Compliance-critical VAT fields in non-settings screens:** `vatMode` in purchase-orders, receipts,
   quotations, and pnd30 is used to conditionally show/hide workflow UI (e.g., "Create Tax Invoice"
   button), not to display the numeric rate — this is correct behaviour per §4.6. Only the Companies
   settings screen (CRITICAL-01) violates this.
6. **Date locale (Bangkok):** The AP-aging screen anchors today to `Asia/Bangkok` timezone. The
   document creation screens use server-side date assignment (CLAUDE.md §10). No client-side
   `new Date()` used raw for a posted `doc_date`.
7. **DaisyUI `role="dialog" aria-modal="true"`:** All modal `div.modal.modal-open` wrappers carry
   `role="dialog"` and `aria-modal="true"` consistently.
8. **`aria-hidden` on decorative icons:** Lucide icons in interactive containers consistently carry
   `aria-hidden` (e.g., `<Printer … aria-hidden />`, `<Link2 … aria-hidden />`).
9. **shadcn/ui / DaisyUI token consistency:** The design-token layer (`text-ink-*`, `bg-base-*`,
   `rounded-card`, `shadow-warm-*`) is used uniformly. No raw hex colors found in Tailwind class
   attributes. Mixed use of DaisyUI semantic tokens (`text-error`, `text-base-content`) alongside
   `ink-*` tokens is contained to a handful of screens and is minor.
10. **Number formatting on financial screens:** `payment-vouchers/new` uses
    `toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })` correctly.
    Most monetary display uses the shared `formatTHB` helper.
