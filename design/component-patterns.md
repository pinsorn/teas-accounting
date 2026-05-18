# Component Patterns

> Common UI patterns ที่ทุก screen ใช้ — implement ครั้งเดียวใน `components/ui/` แล้วใช้ทั่วทั้ง app

ดู spec เต็มที่ `docs/Design(UI).md`

---

## Core Components (จะ install ผ่าน shadcn/ui CLI)

```
button, badge, card, dialog, dropdown-menu, form, input,
label, popover, select, separator, sheet, switch, table,
tabs, textarea, toast, tooltip
```

ติดตั้ง:

```bash
pnpm dlx shadcn@latest init
pnpm dlx shadcn@latest add button input table form dialog select toast
```

---

## Patterns ที่ระบบนี้ใช้บ่อย

### 1. DocumentNumberBadge

แสดง `05-2026-TI-0001` ด้วย `font-mono` + เลื่อน hover ให้ copy

### 2. StatusBadge

`<StatusBadge status="POSTED" />` → green pill + lock icon

| Status | Color | Icon |
|---|---|---|
| DRAFT | gray | pencil |
| POSTED | green | lock |
| PAID | blue | check |
| VOIDED | red | x |

### 3. TaxIdInput

Input field + auto-validate Thai 13-digit + format display `0-1055-56123-45-0`  
On blur: ส่ง POST ไป `/customers/lookup-by-tax-id` เพื่อ auto-fill ถ้ามีในระบบแล้ว

### 4. AmountInput

Number input + thousand separator + 2 decimals + tax calculation real-time  
Currency display: `฿1,234,567.89`

### 5. DateInput

ใช้ Bangkok timezone, default = today, locked สำหรับ Tax Invoice (per spec)

### 6. CustomerSelector

Async combobox — search by name/Tax ID, debounce 300ms, show "Add new" if no match

### 7. ProductSelector (SKU search)

Same pattern as Customer — search by SKU/name, auto-fill description + price + tax code

### 8. LineItemsTable

Editable table for document lines  
Columns: Item, Description, Qty, UoM, Price, Discount, Tax, Total  
Auto-recalc on each cell change

### 9. PostConfirmDialog

⚠️ Special dialog for Tax Invoice post — explicit warning + summary preview + "irreversible" emphasis

### 10. DataTable (list views)

- Server-side pagination (cursor-based)
- Filter chips above table
- Bulk action checkbox column
- Sort by column header
- Empty state + loading skeleton
- Mobile: card view fallback

---

## Layout Patterns

### App Shell
```
<AppShell>
  <Header />
  <Sidebar />
  <main>
    <PageHeader title="..." actions={...} />
    <PageContent>{children}</PageContent>
  </main>
</AppShell>
```

### Form Layout
- 2-column on desktop, stacked on mobile
- Section dividers
- Sticky footer with save/cancel
- Validation messages below fields

### Detail View
- 60/40 split — main content left, sidebar with timeline/metadata right
- Sticky action bar at top
- Print button always visible

---

## Form Validation Rules

- All Thai Tax IDs validated client-side via Zod refinement (calls `validateThaiTaxId`)
- Date inputs constrained to today only for Tax Invoice
- Amount fields prohibit negative values (use discount field instead)
- Required fields marked with `*` red
- Inline errors below field on blur

---

## Accessibility Notes

- All interactive elements: `tabindex` set correctly
- Focus ring visible (Tailwind `ring-2 ring-primary-500`)
- ARIA labels for icon-only buttons
- Color contrast 4.5:1 minimum
- Status conveyed via icon + text, never color only
