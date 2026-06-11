'use client';

import Link from 'next/link';
import Image from 'next/image';
import { usePathname, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { LayoutDashboard, FileText, Receipt, ReceiptText, FileMinus, FilePlus, ListChecks, LogOut, Languages, Building2, Wallet, FileSignature, FileInput, Layers, Percent, Coins, Scale, TrendingUp, BarChart3, FileSpreadsheet, Landmark, Package, KeyRound, PanelLeftClose, PanelLeft, Users, FolderTree, Files } from 'lucide-react';
import { auth } from '@/lib/auth';
import { resolveLogoUrl } from '@/lib/company-logo';
import { useCompanyProfile, useMePermissions, useSystemInfo } from '@/lib/queries';
import { toast } from 'sonner';

// vatOnly: shown only for VAT-registered companies (ม.86). Non-VAT companies do
// not file ภ.พ.30, so that item is hidden. WHT filings (ภ.ง.ด.3/53) stay — a
// non-VAT company is still a withholding agent.
// superAdminOnly: shown only for super admins (hidden, not disabled — same rule
// as PermissionGate; BE enforces master.company.manage regardless).
type NavItem = { href: string; key: string; Icon: typeof LayoutDashboard; badge?: number; vatOnly?: boolean; superAdminOnly?: boolean };

const SECTIONS: { key: string; items: NavItem[] }[] = [
  {
    key: 'overview',
    items: [
      { href: '/', key: 'dashboard', Icon: LayoutDashboard },
    ],
  },
  {
    key: 'sales',
    items: [
      { href: '/customers', key: 'customers', Icon: Users },
      { href: '/quotations', key: 'quotations', Icon: FileSignature },
      { href: '/sales-orders', key: 'salesOrders', Icon: ListChecks },
      { href: '/delivery-orders', key: 'deliveryOrders', Icon: FileInput },
      // Document chain order: Invoice (ใบแจ้งหนี้) precedes the Tax Invoice (ใบกำกับภาษี).
      { href: '/invoices', key: 'billingNotes', Icon: ReceiptText },
      { href: '/tax-invoices', key: 'taxInvoices', Icon: FileText, vatOnly: true },
      { href: '/receipts', key: 'receipts', Icon: Receipt },
      // CN/DN adjust a Tax Invoice's VAT (ม.86/10) — a non-VAT company issues none.
      { href: '/credit-notes', key: 'creditNotes', Icon: FileMinus, vatOnly: true },
      { href: '/debit-notes', key: 'debitNotes', Icon: FilePlus, vatOnly: true },
      { href: '/number-gaps', key: 'numberGaps', Icon: ListChecks },
    ],
  },
  {
    key: 'purchase',
    items: [
      // Document flow order (Ham 2026-05-30): ผู้ขาย → ใบสั่งซื้อ → ใบสำคัญจ่าย →
      // ใบกำกับภาษีซื้อ → หนังสือรับรองหัก ณ ที่จ่าย.
      { href: '/vendors', key: 'vendors', Icon: Building2 },
      { href: '/purchase-orders', key: 'purchaseOrders', Icon: ListChecks },
      { href: '/payment-vouchers', key: 'paymentVouchers', Icon: Wallet },
      { href: '/vendor-invoices', key: 'vendorInvoices', Icon: FileInput },
      { href: '/wht-certificates', key: 'whtCerts', Icon: FileSignature },
    ],
  },
  {
    key: 'payroll',
    items: [
      { href: '/payroll', key: 'payroll', Icon: Coins },
    ],
  },
  {
    key: 'reports',
    items: [
      { href: '/reports/trial-balance', key: 'trialBalance', Icon: Scale },
      { href: '/reports/profit-loss', key: 'profitLoss', Icon: TrendingUp },
      { href: '/reports/sales-summary', key: 'salesSummary', Icon: BarChart3 },
      { href: '/reports/pnd30', key: 'pnd30', Icon: FileSpreadsheet, vatOnly: true },
      { href: '/reports/outstanding-po', key: 'outstandingPo', Icon: ListChecks },
      { href: '/reports/ap-aging', key: 'apAging', Icon: Coins },
      { href: '/tax-filings', key: 'taxFilings', Icon: Landmark },
      { href: '/documents', key: 'documents', Icon: Files },
      { href: '/tax-filings/missing-wht-cert', key: 'missingWhtCert', Icon: FileSpreadsheet },
      { href: '/reports/wht-receivable', key: 'whtReceivable', Icon: Coins },
    ],
  },
  {
    key: 'settings',
    items: [
      { href: '/settings/company', key: 'company', Icon: Building2 },
      // Per-company VAT mode — super-admin companies CRUD (tax config per tenant).
      { href: '/settings/companies', key: 'companies', Icon: Landmark, superAdminOnly: true },
      { href: '/settings/products', key: 'products', Icon: Package },
      { href: '/settings/business-units', key: 'businessUnits', Icon: Layers },
      { href: '/settings/employees', key: 'employees', Icon: Users },
      { href: '/settings/wht-types', key: 'whtTypes', Icon: Percent },
      { href: '/settings/expense-categories', key: 'expenseCategories', Icon: FolderTree },
      { href: '/settings/api-keys', key: 'apiKeys', Icon: KeyRound },
    ],
  },
];

const COLLAPSE_KEY = 'teas-sidebar-collapsed';

export function SidebarNav() {
  const pathname = usePathname();
  const router = useRouter();
  const t = useTranslations('nav');
  const tApp = useTranslations('app');
  const [collapsed, setCollapsed] = useState(false);
  // Sprint 13j-tail (4) — Ham directive 2026-05-22: prefer the company-uploaded
  // logo from the active tenant's profile; fall back to the TEAS mascot when no
  // logo has been uploaded yet. Hits same /company-profile endpoint as the
  // Settings page → react-query dedupes (default 5-min staleTime).
  const profile = useCompanyProfile();
  const logoSrc = resolveLogoUrl(profile.data?.logoUrl);
  // ม.86 — hide VAT-only items (ภ.พ.30) for non-VAT companies. Default true so the
  // menu is unchanged before /system/info resolves and for VAT registrants.
  const vatMode = useSystemInfo().data?.vatMode ?? true;
  // Default false: super-admin-only items appear only once /me/permissions confirms.
  const isSuperAdmin = useMePermissions().data?.isSuperAdmin ?? false;

  useEffect(() => {
    setCollapsed(localStorage.getItem(COLLAPSE_KEY) === '1');
  }, []);

  function toggleCollapsed() {
    setCollapsed((c) => {
      const next = !c;
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0');
      return next;
    });
  }

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
    <aside
      className={`flex shrink-0 flex-col border-r border-ink-100 bg-base-100 transition-[width] duration-200 ${
        collapsed ? 'w-[72px]' : 'w-64'
      }`}
    >
      {/* Header: logo mark + brand + collapse toggle */}
      <div className="flex min-h-topbar items-center gap-2.5 border-b border-ink-100 px-4 py-3.5">
        <span className="grid h-[38px] w-[38px] shrink-0 place-items-center overflow-hidden rounded-field bg-gradient-to-br from-peach-100 to-peach-300 shadow-[inset_0_0_0_1px_rgba(26,24,22,0.04)]">
          <Image
            src={logoSrc}
            alt={tApp('name')}
            width={38}
            height={38}
            className="h-full w-full object-cover"
            unoptimized={logoSrc.startsWith('/api/proxy/')}
          />
        </span>
        {!collapsed && (
          <div className="flex min-w-0 flex-col">
            <span className="text-base font-extrabold leading-tight tracking-wide text-ink-900">{tApp('name')}</span>
            <span className="text-[11px] tracking-wide text-ink-500">ENTERPRISE ACCOUNTING</span>
          </div>
        )}
        <button
          onClick={toggleCollapsed}
          title={collapsed ? 'ขยาย' : 'ย่อ'}
          aria-label={collapsed ? 'ขยายเมนู' : 'ย่อเมนู'}
          className={`grid h-7 w-7 place-items-center rounded-lg border border-ink-100 bg-base-100 text-ink-600 hover:bg-base-300 ${
            collapsed ? 'mx-auto' : 'ml-auto'
          }`}
        >
          {collapsed ? <PanelLeft className="h-4 w-4" /> : <PanelLeftClose className="h-4 w-4" />}
        </button>
      </div>

      {/* Nav */}
      <nav className="flex-1 overflow-y-auto px-2.5 py-3">
        {SECTIONS.map((section) => (
          <div key={section.key} className="mb-1.5">
            {section.key !== 'overview' &&
              (collapsed ? (
                <div className="mx-2 mb-1 mt-1.5 border-t border-ink-100" />
              ) : (
                <div className="px-3 pb-1.5 pt-3 text-[10.5px] font-bold uppercase tracking-[1.2px] text-ink-500">
                  {t(`section.${section.key}`)}
                </div>
              ))}
            {section.items
              .filter((it) => (!it.vatOnly || vatMode) && (!it.superAdminOnly || isSuperAdmin))
              .map(({ href, key, Icon, badge }) => {
              const active = href === '/' ? pathname === '/' : pathname.startsWith(href);
              return (
                <Link
                  key={href}
                  href={href}
                  title={t(key)}
                  className={`relative flex items-center gap-3 rounded-field px-3 py-2 text-[13.5px] transition-colors ${
                    collapsed ? 'justify-center px-2.5' : ''
                  } ${
                    active
                      ? 'bg-peach-50 font-semibold text-peach-700'
                      : 'font-medium text-ink-600 hover:bg-base-300 hover:text-ink-900'
                  }`}
                >
                  {active && (
                    <span className="absolute left-0 top-2 bottom-2 w-[3px] rounded-r bg-peach-500" aria-hidden />
                  )}
                  <Icon className="h-[18px] w-[18px] shrink-0" aria-hidden />
                  {!collapsed && <span className="truncate">{t(key)}</span>}
                  {!collapsed && badge != null && badge > 0 && (
                    <span className="ml-auto rounded-full bg-peach-100 px-1.5 py-px text-[11px] font-bold text-peach-700">
                      {badge}
                    </span>
                  )}
                </Link>
              );
            })}
          </div>
        ))}
      </nav>

      {/* Footer */}
      <div className="border-t border-ink-100 p-3">
        {!collapsed && (
          <div className="mb-2 flex items-center gap-2.5">
            <span className="grid h-9 w-9 shrink-0 place-items-center rounded-full bg-gradient-to-br from-peach-200 to-peach-400 text-[13px] font-bold text-ink-900">
              T
            </span>
            <div className="flex min-w-0 flex-col">
              <span className="truncate text-[13px] font-semibold text-ink-900">{tApp('name')}</span>
              <span className="text-[11px] text-ink-500">Enterprise</span>
            </div>
          </div>
        )}
        <div className="flex flex-col gap-1">
          <button
            onClick={toggleLocale}
            className={`flex items-center gap-2 rounded-field px-3 py-2 text-[13px] font-medium text-ink-600 hover:bg-base-300 ${
              collapsed ? 'justify-center px-2.5' : ''
            }`}
            title="TH / EN"
          >
            <Languages className="h-4 w-4 shrink-0" aria-hidden />
            {!collapsed && 'TH / EN'}
          </button>
          <button
            onClick={logout}
            className={`flex items-center gap-2 rounded-field px-3 py-2 text-[13px] font-medium text-status-danger hover:bg-status-danger-bg ${
              collapsed ? 'justify-center px-2.5' : ''
            }`}
            title={t('logout')}
          >
            <LogOut className="h-4 w-4 shrink-0" aria-hidden />
            {!collapsed && t('logout')}
          </button>
        </div>
      </div>
    </aside>
  );
}
