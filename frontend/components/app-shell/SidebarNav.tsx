'use client';

import Link from 'next/link';
import Image from 'next/image';
import { usePathname, useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { LayoutDashboard, FileText, Receipt, ReceiptText, FileMinus, FilePlus, ListChecks, LogOut, Languages, Building2, Wallet, FileSignature, FileInput, Layers, Percent, Coins, Scale, TrendingUp, BarChart3, FileSpreadsheet, Landmark, Package, KeyRound, PanelLeftClose, PanelLeft, Users, FolderTree, Files, PieChart, ShieldCheck, UsersRound } from 'lucide-react';
import { auth } from '@/lib/auth';
import { resolveLogoUrl } from '@/lib/company-logo';
import { useCompanyProfile, useMePermissions, useSystemInfo } from '@/lib/queries';
import { toast } from 'sonner';

// vatOnly: shown only for VAT-registered companies (ม.86). Non-VAT companies do
// not file ภ.พ.30, so that item is hidden. WHT filings (ภ.ง.ด.3/53) stay — a
// non-VAT company is still a withholding agent.
// superAdminOnly: shown only for super admins (hidden, not disabled — same rule
// as PermissionGate; BE enforces master.company.manage regardless).
// perm: shown when the user holds this permission OR is a super admin (RBAC admin
// pages are dual-audience: super-admins + company-admins with the grant).
type NavItem = { href: string; key: string; Icon: typeof LayoutDashboard; badge?: number; vatOnly?: boolean; superAdminOnly?: boolean; perm?: string };

// Sprint 13k Plan 2 (Phase E) — each item carries the READ permission of its primary endpoint
// (from docs/rbac/endpoint-permission-map.generated.md) so the nav mirrors the BE gate: a role
// that would 403 on the section's main endpoint no longer sees the item. Super-admins bypass.
// Items left ungated are reachable by any authenticated user (dashboard, AuthnOnly endpoints like
// /documents and the own-company /settings/company profile page).
const SECTIONS: { key: string; items: NavItem[] }[] = [
  {
    key: 'overview',
    items: [
      { href: '/', key: 'dashboard', Icon: LayoutDashboard },
    ],
  },
  {
    // Master Data section (Design(UI).md §3 nav spec): Customers, Vendors, Products (SKU).
    // Customers moved here from Sales; Vendors moved here from Purchase;
    // Products moved here from Settings — all are reference data, not transactions.
    key: 'masterData',
    items: [
      { href: '/customers', key: 'customers', Icon: Users, perm: 'master.customer.read' },
      { href: '/vendors', key: 'vendors', Icon: Building2, perm: 'master.vendor.manage' },
      { href: '/settings/products', key: 'products', Icon: Package, perm: 'master.product.manage' },
    ],
  },
  {
    key: 'sales',
    items: [
      { href: '/quotations', key: 'quotations', Icon: FileSignature, perm: 'sales.quotation.manage' },
      { href: '/sales-orders', key: 'salesOrders', Icon: ListChecks, perm: 'sales.sales_order.manage' },
      { href: '/delivery-orders', key: 'deliveryOrders', Icon: FileInput, perm: 'sales.delivery_order.manage' },
      // Document chain order: Invoice (ใบแจ้งหนี้) precedes the Tax Invoice (ใบกำกับภาษี).
      { href: '/invoices', key: 'billingNotes', Icon: ReceiptText, perm: 'sales.billing_note.read' },
      { href: '/tax-invoices', key: 'taxInvoices', Icon: FileText, vatOnly: true, perm: 'sales.tax_invoice.read' },
      { href: '/receipts', key: 'receipts', Icon: Receipt, perm: 'sales.receipt.read' },
      // CN/DN adjust a Tax Invoice's VAT (ม.86/10) — a non-VAT company issues none.
      { href: '/credit-notes', key: 'creditNotes', Icon: FileMinus, vatOnly: true, perm: 'sales.credit_note.read' },
      { href: '/debit-notes', key: 'debitNotes', Icon: FilePlus, vatOnly: true, perm: 'sales.debit_note.read' },
    ],
  },
  {
    key: 'purchase',
    items: [
      // Document flow order (Ham 2026-05-30): ใบสั่งซื้อ → ใบสำคัญจ่าย →
      // ใบกำกับภาษีซื้อ → หนังสือรับรองหัก ณ ที่จ่าย.
      { href: '/purchase-orders', key: 'purchaseOrders', Icon: ListChecks, perm: 'purchase.purchase_order.read' },
      { href: '/payment-vouchers', key: 'paymentVouchers', Icon: Wallet, perm: 'purchase.payment_voucher.read' },
      { href: '/vendor-invoices', key: 'vendorInvoices', Icon: FileInput, perm: 'purchase.vendor_invoice.read' },
      { href: '/wht-certificates', key: 'whtCerts', Icon: FileSignature, perm: 'purchase.wht.read' },
    ],
  },
  {
    key: 'payroll',
    items: [
      { href: '/payroll', key: 'payroll', Icon: Coins, perm: 'payroll.run.manage' },
    ],
  },
  {
    key: 'reports',
    items: [
      { href: '/reports/tax-summary', key: 'taxSummary', Icon: PieChart, perm: 'report.profit_loss.read' },
      { href: '/reports/trial-balance', key: 'trialBalance', Icon: Scale, perm: 'report.trial_balance.read' },
      { href: '/reports/profit-loss', key: 'profitLoss', Icon: TrendingUp, perm: 'report.profit_loss.read' },
      { href: '/reports/sales-summary', key: 'salesSummary', Icon: BarChart3, perm: 'report.profit_loss.read' },
      { href: '/reports/pnd30', key: 'pnd30', Icon: FileSpreadsheet, vatOnly: true, perm: 'tax.pnd30.read' },
      { href: '/reports/outstanding-po', key: 'outstandingPo', Icon: ListChecks, perm: 'purchase.purchase_order.read' },
      { href: '/reports/ap-aging', key: 'apAging', Icon: Coins, perm: 'purchase.purchase_order.read' },
      { href: '/tax-filings', key: 'taxFilings', Icon: Landmark, perm: 'tax.filing.read' },
      // /documents (chain views) is AuthnOnly on the BE — any authenticated user.
      { href: '/documents', key: 'documents', Icon: Files },
      { href: '/tax-filings/missing-wht-cert', key: 'missingWhtCert', Icon: FileSpreadsheet, perm: 'tax.pnd53.read' },
      { href: '/reports/wht-receivable', key: 'whtReceivable', Icon: Coins, perm: 'tax.pnd53.read' },
      // number-gaps moved from Sales → Reports (it's an audit/admin tool, not a transactional doc).
      { href: '/number-gaps', key: 'numberGaps', Icon: ListChecks, perm: 'report.audit.read' },
    ],
  },
  {
    key: 'settings',
    items: [
      // Own-company profile (soft fields) — AuthnOnly read; edits are BE-gated.
      { href: '/settings/company', key: 'company', Icon: Building2 },
      // Per-company VAT mode — super-admin companies CRUD (tax config per tenant).
      { href: '/settings/companies', key: 'companies', Icon: Landmark, superAdminOnly: true },
      // Per-company RBAC admin — super-admin OR a company-admin holding the grant.
      { href: '/settings/roles', key: 'roles', Icon: ShieldCheck, perm: 'sys.role.manage' },
      { href: '/settings/users', key: 'users', Icon: UsersRound, perm: 'sys.user.manage' },
      { href: '/settings/business-units', key: 'businessUnits', Icon: Layers, perm: 'master.business_unit.manage' },
      { href: '/settings/employees', key: 'employees', Icon: Users, perm: 'master.employee.manage' },
      { href: '/settings/wht-types', key: 'whtTypes', Icon: Percent, perm: 'tax.wht_type.manage' },
      { href: '/settings/expense-categories', key: 'expenseCategories', Icon: FolderTree, perm: 'sys.expense_category.manage' },
      { href: '/settings/api-keys', key: 'apiKeys', Icon: KeyRound, perm: 'sys.api_key.manage' },
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
  const sysInfo = useSystemInfo();
  const vatMode = sysInfo.data?.vatMode ?? true;
  // Default false: super-admin-only items appear only once /me/permissions confirms.
  const mePerms = useMePermissions();
  const me = mePerms.data;
  const isSuperAdmin = me?.isSuperAdmin ?? false;
  const myPerms = me?.permissions ?? [];
  // e2e sentinel — both gate queries settled (success OR error), so the nav has
  // applied its final permission/VAT filter. The RBAC UI-gating spec waits on
  // this (attached) after every navigation instead of guessing a timeout; it
  // also closes the vatMode-default-true flash on non-VAT companies. Hidden;
  // zero UX impact.
  const gatesReady = !mePerms.isPending && !sysInfo.isPending;

  // Sidebar active state: the MOST-SPECIFIC matching nav href wins, so a child route
  // (e.g. /tax-filings/missing-wht-cert) highlights ONLY itself, not its sibling prefix
  // (/tax-filings) — while a detail route with no nav item of its own (e.g. /quotations/1)
  // still highlights its parent (/quotations). startsWith(h + '/') keeps the match on a
  // segment boundary (/tax-filings never matches /tax-filings-foo).
  const activeHref = (() => {
    if (pathname === '/') return '/';
    const matches = SECTIONS.flatMap((s) => s.items.map((i) => i.href))
      .filter((h) => h !== '/' && (pathname === h || pathname.startsWith(h + '/')));
    return matches.sort((a, b) => b.length - a.length)[0] ?? null;
  })();

  useEffect(() => {
    try { setCollapsed(localStorage.getItem(COLLAPSE_KEY) === '1'); } catch { /* incognito/restricted storage — ignore */ }
  }, []);

  function toggleCollapsed() {
    setCollapsed((c) => {
      const next = !c;
      try { localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0'); } catch { /* incognito/restricted storage — ignore */ }
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
    const secure = window.location.protocol === 'https:' ? '; secure' : '';
    document.cookie = `locale=${next}; path=/; max-age=31536000; samesite=lax${secure}`;
    router.refresh();
    toast.success(next === 'th' ? 'ภาษาไทย' : 'English');
  }

  function closeDrawer() {
    const el = document.getElementById('app-drawer') as HTMLInputElement | null;
    if (el) el.checked = false;
  }

  return (
    <aside
      data-testid="app-sidebar"
      className={`flex h-full shrink-0 flex-col border-r border-ink-100 bg-base-100 transition-[width] duration-200 ${
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
      {gatesReady && <span data-testid="nav-gates-ready" hidden aria-hidden />}
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
              .filter((it) =>
                (!it.vatOnly || vatMode)
                && (!it.superAdminOnly || isSuperAdmin)
                && (!it.perm || isSuperAdmin || myPerms.includes(it.perm)))
              .map(({ href, key, Icon, badge }) => {
              const active = href === activeHref;
              return (
                <Link
                  key={href}
                  href={href}
                  title={t(key)}
                  onClick={closeDrawer}
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
