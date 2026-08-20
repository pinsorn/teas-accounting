'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { ChevronRight, Bell, Settings, Menu } from 'lucide-react';
import { CompanySwitcher } from '@/components/layout/CompanySwitcher';

// Sprint 13j-FE — top bar: breadcrumbs + search pill (⌘K) + icon buttons.
// Breadcrumb labels come from the `nav` i18n namespace; unknown segments
// (e.g. document ids, "new") fall back to a sensible translation or the raw
// segment. Search pill is presentational for now (no global search backend).

// Path segment → nav translation key (mirrors SidebarNav routes).
// S2 fix (2026-07-16) — this map was missing most non-sales routes (e.g.
// `customers`), so /customers rendered "แดชบอร์ด > customers" (raw EN slug)
// while sales routes (already listed) showed Thai. Swept every top-level
// route folder under app/(dashboard) — including nested reports/* and
// settings/* segments — so no dashboard route falls back to its raw slug.
const SEG_KEY: Record<string, string> = {
  customers: 'customers',
  quotations: 'quotations',
  'sales-orders': 'salesOrders',
  'delivery-orders': 'deliveryOrders',
  'tax-invoices': 'taxInvoices',
  invoices: 'billingNotes',
  receipts: 'receipts',
  'credit-notes': 'creditNotes',
  'debit-notes': 'debitNotes',
  'number-gaps': 'numberGaps',
  vendors: 'vendors',
  'vendor-invoices': 'vendorInvoices',
  'purchase-orders': 'purchaseOrders',
  'outstanding-po': 'outstandingPo',
  'ap-aging': 'apAging',
  'payment-vouchers': 'paymentVouchers',
  'expense-claims': 'expenseClaims',
  'fixed-assets': 'fixedAssets',
  depreciation: 'depreciation',
  'wht-certificates': 'whtCerts',
  'wht-receivable': 'whtReceivable',
  payroll: 'payroll',
  'period-close': 'periodClose',
  documents: 'documents',
  'tax-filings': 'taxFilings',
  'missing-wht-cert': 'missingWhtCert',
  'bank-accounts': 'bankAccounts',
  settings: 'section.settings',
  reports: 'section.reports',
  // settings/*
  company: 'company',
  companies: 'companies',
  roles: 'roles',
  users: 'users',
  products: 'products',
  'business-units': 'businessUnits',
  employees: 'employees',
  'wht-types': 'whtTypes',
  'expense-categories': 'expenseCategories',
  'api-keys': 'apiKeys',
  // reports/*
  'tax-summary': 'taxSummary',
  'trial-balance': 'trialBalance',
  'balance-sheet': 'balanceSheet',
  'profit-loss': 'profitLoss',
  'general-ledger': 'generalLedger',
  'bank-reconciliation': 'bankReconciliation',
  'ar-aging': 'arAging',
  'customer-statement': 'customerStatement',
  'vendor-ledger': 'vendorLedger',
  'sales-summary': 'salesSummary',
  pnd30: 'pnd30',
};

export function Topbar() {
  const pathname = usePathname();
  const t = useTranslations('nav');
  const tc = useTranslations('common');

  const segments = pathname.split('/').filter(Boolean);

  const crumbs: string[] = [t('dashboard')];
  if (segments.length > 0) {
    for (const seg of segments) {
      if (seg === 'new') {
        crumbs.push('สร้างใหม่');
      } else if (seg === 'edit') {
        crumbs.push(tc('edit'));
      } else if (/^\d+$/.test(seg)) {
        crumbs.push(`#${seg}`);
      } else if (SEG_KEY[seg]) {
        try {
          crumbs.push(t(SEG_KEY[seg]));
        } catch {
          crumbs.push(seg);
        }
      } else {
        crumbs.push(seg);
      }
    }
  }

  return (
    <header className="flex h-topbar shrink-0 items-center gap-4 border-b border-ink-100 bg-base-100 px-6">
      {/* Hamburger — visible only on mobile (<lg); opens the DaisyUI drawer */}
      <label
        htmlFor="app-drawer"
        className="btn btn-ghost btn-sm lg:hidden"
        aria-label={t('openMenu')}
      >
        <Menu className="h-5 w-5" aria-hidden />
      </label>

      {/* R2 (ui-codebase-review-2026-08-20 #2) — at 390px the full trail + CompanySwitcher
          + icons never fit; overflow-hidden on the ancestor drawer-content div then clips
          CompanySwitcher instead of reflowing. min-w-0 lets the nav actually shrink below
          its intrinsic min-content size (was inert without it, same footgun on any flex
          item with nowrap text inside). flex-auto (NOT flex-1 — flex-1's 0% basis made the
          nav render at literal zero width whenever the row is in deficit, i.e. hid the
          breadcrumb on almost every mobile page) keeps its content-sized basis, still grows
          to push CompanySwitcher/icons to the right on desktop, still shrinks under
          pressure. Non-last crumbs collapse to just the current page below sm so mobile
          spends its width on what users need (company switcher, icons). */}
      <nav aria-label="breadcrumb" className="flex min-w-0 flex-auto items-center gap-1.5 text-[13px] text-ink-600">
        {crumbs.map((c, i) => (
          <span key={i} className={`flex min-w-0 items-center gap-1.5 ${i < crumbs.length - 1 ? 'hidden sm:flex' : ''}`}>
            {i > 0 && <ChevronRight className="hidden h-3 w-3 shrink-0 text-ink-300 sm:block" aria-hidden />}
            <span className={`truncate ${i === crumbs.length - 1 ? 'font-semibold text-ink-900' : ''}`}>{c}</span>
          </span>
        ))}
      </nav>

      <CompanySwitcher />

      <button
        className="relative grid h-[34px] w-[34px] shrink-0 place-items-center rounded-lg border border-ink-100 bg-base-100 text-ink-600 hover:bg-base-300"
        title="การแจ้งเตือน"
        aria-label="การแจ้งเตือน"
      >
        <Bell className="h-[18px] w-[18px]" aria-hidden />
        <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full border-2 border-base-100 bg-peach-500" aria-hidden />
      </button>
      <Link
        href="/settings/company"
        className="grid h-[34px] w-[34px] shrink-0 place-items-center rounded-lg border border-ink-100 bg-base-100 text-ink-600 hover:bg-base-300"
        title="ตั้งค่า"
        aria-label="ตั้งค่า"
      >
        <Settings className="h-[18px] w-[18px]" aria-hidden />
      </Link>
    </header>
  );
}
