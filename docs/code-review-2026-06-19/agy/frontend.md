# AGY (Gemini) review — Frontend (2026-06-19)

> Independent second-model review of frontend/. File refs point at the throwaway copy; map to repo root.
> (Thai string literals were captured as mojibake in the bridge; the finding meaning is in the English text.)

### D1 Compliance
- **Major — settings/companies/page.tsx:44** — gate `if (perms.data && !perms.data.isSuperAdmin)`: while `useMePermissions` is loading, `perms.data` is undefined → condition falsy → super-admin company create/edit page renders & is interactive for regular users until the query resolves. Fix: gate on `perms.isLoading` first, then `!perms.data?.isSuperAdmin`.
- **Critical — tax-invoices/[id]/page.tsx:90, vendor-invoices/[id]/page.tsx:123** — human-approval gate relies solely on `?action=approve` query param. Normal detail links omit it → user bypasses the warning banner and can directly Post. Fix: don't use query param as security state; have backend return `createdViaApiKey` on the detail, auto-show the banner + disable/hide default Post for any agent-created draft.
- **Major — receipts/[id]/page.tsx:98** — `DocActionBar` for receipts renders no Post button in Draft state; only the `action=approve` banner can post → standard users can't post a normally-created draft receipt. Fix: add Post action (PermissionGate `sales.receipt.post`) to DocActionBar when Draft.

### D2 Correctness
- **Major — lib/queries.ts:186,218** — `usePostReceipt`/`usePostAdjustmentNote` invalidate only list keys, not `['receipt',id]`/`['adjustment-note',id]` → detail page stays "Draft" after posting.
- **Minor — lib/queries.ts:266** — `useMarkPrinted` does no invalidation → print watermark/audit rail stale until manual refresh.
- **Major — lib/queries.ts:140,386** — `usePostTaxInvoice`/`usePostVendorInvoice` don't invalidate report queries (`input-vat-register`, `output-vat-register`, `tax-summary`, `profit-loss`) → stale dashboard/report values.
- **Major — lib/utils.ts:26** — `formatDate` sets `calendar:'buddhist'`. *(ADJUDICATE: display-layer Buddhist era is normal/expected for Thai docs; CLAUDE.md bans Buddhist INTERNALLY only — likely false positive.)*
- **Minor — lib/utils.ts:11-12** — `formatTHB` fixed 2dp; unit prices need up to 4dp. Fix: maximumFractionDigits 4.

### D3 Security
- **Critical — app/api/auth/login/route.ts:67** — returns full `e.stack` to client on 500 (info disclosure). Fix: log server-side, return `e.message`/generic code. *(Claude FE flagged same, severity Minor — reconcile.)*
- **Major — app/api/proxy/[...path]/route.ts:12** — `forward` fetch not wrapped in try/catch; upstream offline → unhandled throw. Fix: try/catch → 502.

### D4 i18n parity
- **Minor ×4** — hardcoded Thai strings in receipts/[id], receipts/new, tax-invoices/new, app/layout.tsx (meta title/keywords). Fix: move to messages bundles / generateMetadata.
- **i18n flat key mismatches th↔en: 0** (matches Claude FE: exact parity).

### Summary
| Dimension | Critical | Major | Minor |
|---|---|---|---|
| D1 | 1 | 2 | 0 |
| D2 | 0 | 3 | 2 |
| D3 | 1 | 1 | 0 |
| D4 | 0 | 0 | 4 |
