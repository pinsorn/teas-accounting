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
const SEG_KEY: Record<string, string> = {
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
  'payment-vouchers': 'paymentVouchers',
  'wht-certificates': 'whtCerts',
  'tax-filings': 'taxFilings',
  settings: 'section.settings',
  reports: 'section.reports',
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

      <nav aria-label="breadcrumb" className="flex items-center gap-1.5 text-[13px] text-ink-600">
        {crumbs.map((c, i) => (
          <span key={i} className="flex items-center gap-1.5">
            {i > 0 && <ChevronRight className="h-3 w-3 text-ink-300" aria-hidden />}
            <span className={i === crumbs.length - 1 ? 'font-semibold text-ink-900' : ''}>{c}</span>
          </span>
        ))}
      </nav>

      <div className="ml-auto" />
      <CompanySwitcher />

      <button
        className="relative grid h-[34px] w-[34px] place-items-center rounded-lg border border-ink-100 bg-base-100 text-ink-600 hover:bg-base-300"
        title="การแจ้งเตือน"
        aria-label="การแจ้งเตือน"
      >
        <Bell className="h-[18px] w-[18px]" aria-hidden />
        <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full border-2 border-base-100 bg-peach-500" aria-hidden />
      </button>
      <Link
        href="/settings/company"
        className="grid h-[34px] w-[34px] place-items-center rounded-lg border border-ink-100 bg-base-100 text-ink-600 hover:bg-base-300"
        title="ตั้งค่า"
        aria-label="ตั้งค่า"
      >
        <Settings className="h-[18px] w-[18px]" aria-hidden />
      </Link>
    </header>
  );
}
