'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { LayoutDashboard, FileText, Receipt, FileMinus, FilePlus, ListChecks, LogOut, Languages, Building2, Wallet, FileSignature, FileInput, Layers, Percent, Coins, Scale, TrendingUp, BarChart3, FileSpreadsheet, Landmark, Package, KeyRound } from 'lucide-react';
import { auth } from '@/lib/auth';
import { toast } from 'sonner';

const SECTIONS = [
  {
    key: 'sales',
    items: [
      { href: '/', key: 'dashboard', Icon: LayoutDashboard },
      { href: '/quotations', key: 'quotations', Icon: FileSignature },
      { href: '/sales-orders', key: 'salesOrders', Icon: ListChecks },
      { href: '/delivery-orders', key: 'deliveryOrders', Icon: FileInput },
      { href: '/tax-invoices', key: 'taxInvoices', Icon: FileText },
      { href: '/receipts', key: 'receipts', Icon: Receipt },
      { href: '/credit-notes', key: 'creditNotes', Icon: FileMinus },
      { href: '/debit-notes', key: 'debitNotes', Icon: FilePlus },
      { href: '/number-gaps', key: 'numberGaps', Icon: ListChecks },
    ],
  },
  {
    key: 'purchase',
    items: [
      { href: '/vendors', key: 'vendors', Icon: Building2 },
      { href: '/vendor-invoices', key: 'vendorInvoices', Icon: FileInput },
      { href: '/purchase-orders', key: 'purchaseOrders', Icon: ListChecks },
      { href: '/payment-vouchers', key: 'paymentVouchers', Icon: Wallet },
      { href: '/wht-certificates', key: 'whtCerts', Icon: FileSignature },
    ],
  },
  {
    key: 'reports',
    items: [
      { href: '/reports/trial-balance', key: 'trialBalance', Icon: Scale },
      { href: '/reports/profit-loss', key: 'profitLoss', Icon: TrendingUp },
      { href: '/reports/sales-summary', key: 'salesSummary', Icon: BarChart3 },
      { href: '/reports/pnd30', key: 'pnd30', Icon: FileSpreadsheet },
      { href: '/reports/outstanding-po', key: 'outstandingPo', Icon: ListChecks },
      { href: '/tax-filings', key: 'taxFilings', Icon: Landmark },
      { href: '/reports/wht-receivable', key: 'whtReceivable', Icon: Coins },
    ],
  },
  {
    key: 'settings',
    items: [
      { href: '/settings/products', key: 'products', Icon: Package },
      { href: '/settings/business-units', key: 'businessUnits', Icon: Layers },
      { href: '/settings/wht-types', key: 'whtTypes', Icon: Percent },
      { href: '/settings/api-keys', key: 'apiKeys', Icon: KeyRound },
    ],
  },
] as const;

export function SidebarNav() {
  const pathname = usePathname();
  const router = useRouter();
  const t = useTranslations('nav');
  const tApp = useTranslations('app');

  async function logout() {
    try {
      await auth.logout();
    } catch {
      /* clearing cookie is best-effort */
    }
    router.push('/login');
    router.refresh();
  }

  function toggleLocale() {
    const current = document.cookie.match(/(?:^|; )locale=([^;]+)/)?.[1] ?? 'th';
    const next = current === 'th' ? 'en' : 'th';
    document.cookie = `locale=${next}; path=/; max-age=31536000; samesite=lax`;
    router.refresh();
    toast.success(next === 'th' ? 'ภาษาไทย' : 'English');
  }

  return (
    <aside className="flex w-64 flex-col border-r border-base-300 bg-base-200">
      <div className="p-5">
        <div className="text-xl font-bold text-primary">{tApp('name')}</div>
        <div className="text-xs text-base-content/60">{tApp('tagline')}</div>
      </div>
      <nav className="flex flex-1 flex-col gap-1 px-3">
        {SECTIONS.map((section) => (
          <div key={section.key} className="flex flex-col gap-1">
            {section.key !== 'sales' && (
              <div className="mt-3 px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-base-content/40">
                {t(`section.${section.key}`)}
              </div>
            )}
            {section.items.map(({ href, key, Icon }) => {
              const active = href === '/' ? pathname === '/' : pathname.startsWith(href);
              return (
                <Link
                  key={href}
                  href={href}
                  className={`flex items-center gap-3 rounded-lg px-3 py-2 text-sm transition ${
                    active ? 'bg-primary text-primary-content' : 'hover:bg-base-300'
                  }`}
                >
                  <Icon className="h-4 w-4" aria-hidden />
                  {t(key)}
                </Link>
              );
            })}
          </div>
        ))}
      </nav>
      <div className="flex flex-col gap-1 border-t border-base-300 p-3">
        <button onClick={toggleLocale} className="btn btn-ghost btn-sm justify-start gap-2">
          <Languages className="h-4 w-4" aria-hidden /> TH / EN
        </button>
        <button onClick={logout} className="btn btn-ghost btn-sm justify-start gap-2 text-error">
          <LogOut className="h-4 w-4" aria-hidden /> {t('logout')}
        </button>
      </div>
    </aside>
  );
}
