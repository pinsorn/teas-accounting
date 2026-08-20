'use client';

import Link from 'next/link';
import { useTranslations, useLocale } from 'next-intl';
import {
  TrendingUp, TrendingDown, Wallet, Receipt, Coins, ListChecks, FileInput,
  AlertTriangle, CheckCircle2, ArrowRight, FileText, Plus, Bot,
} from 'lucide-react';
import { PermissionGate, useHasScope } from '@/components/PermissionGate';
import {
  useTaxSummary, useNumberGaps, useVatThresholdStatus, useVendorInvoices,
  useSystemInfo, useCompanyProfile, usePendingAgentApprovals,
} from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import type { TaxSummaryMonth } from '@/lib/types';

/** Format a 1-based month number as a short label (e.g. ม.ค. / Jan) using the active locale. */
function monthShort(locale: string, month1: number): string {
  return new Intl.DateTimeFormat(locale, { month: 'short' }).format(new Date(2000, month1 - 1, 1));
}
/** Format a 1-based month number as a long label (e.g. มกราคม / January) using the active locale. */
function monthFull(locale: string, month1: number): string {
  return new Intl.DateTimeFormat(locale, { month: 'long' }).format(new Date(2000, month1 - 1, 1));
}

function kBaht(n: number, locale: string): string {
  return new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(n);
}

export default function DashboardPage() {
  const t = useTranslations('dashboard');
  const locale = useLocale();
  const now = new Date();
  const year = now.getFullYear();
  const monthNo = now.getMonth() + 1;

  const profile = useCompanyProfile().data;
  // S1 fix (2026-07-16) — undefined vatMode must NEVER be treated as true: a
  // non-VAT company would otherwise flash the VAT card/quick-action for the
  // ~1s before /system/info resolves. Default false (hidden) until known.
  const vatMode = useSystemInfo().data?.vatMode ?? false;

  const summary = useTaxSummary(year);
  const months = summary.data?.months ?? [];
  const cur = months.find((m) => m.month === monthNo);

  const gaps = useNumberGaps();
  const gapCount = gaps.data?.gaps.length ?? 0;
  // H1 (specs/fix-duplicate-tax-doc-numbers.md) §3.5 — a duplicate is a DIFFERENT compliance
  // failure from a gap (tax.v_number_gaps cannot see it at all); a SEPARATE alert so one never
  // hides behind the other.
  const dupCount = gaps.data?.duplicates?.length ?? 0;
  const threshold = useVatThresholdStatus().data?.status;
  const incompleteVi = useVendorInvoices(true).data?.length ?? 0;
  const agentApprovals = usePendingAgentApprovals().data;
  const hasScope = useHasScope();
  // R5 — mirrors the Quick-actions section's own per-button PermissionGate scopes below;
  // keep this list in sync if a quick action is added/removed.
  const anyQuickAction =
    (vatMode && hasScope('sales.tax_invoice.create'))
    || hasScope('sales.receipt.create')
    || hasScope('purchase.payment_voucher.create')
    || hasScope('master.customer.manage')
    || hasScope('master.vendor.manage');

  const companyName = profile?.tradeName || profile?.legalName || t('title');
  const netVat = (cur?.vatPayable ?? 0) - (cur?.vatRefundable ?? 0);

  // ภ.พ.30 is due the 15th of the following month (VAT companies only).
  const pnd30Due = vatMode && now.getDate() <= 15;

  type Alert = { key: string; tone: 'error' | 'warning' | 'info'; icon: typeof AlertTriangle; text: string; href: string; cta: string };
  const alerts: Alert[] = [];
  if (threshold === 'Exceeded')
    alerts.push({ key: 'thx', tone: 'error', icon: AlertTriangle, text: t('vatThreshold.exceeded'), href: '/settings/company', cta: t('alerts.view') });
  else if (threshold === 'Approaching')
    alerts.push({ key: 'tha', tone: 'warning', icon: AlertTriangle, text: t('vatThreshold.approaching'), href: '/settings/company', cta: t('alerts.view') });
  if (gapCount > 0)
    alerts.push({ key: 'gap', tone: 'error', icon: ListChecks, text: t('alerts.numberGaps', { n: gapCount }), href: '/number-gaps', cta: t('alerts.review') });
  if (dupCount > 0)
    alerts.push({ key: 'dup', tone: 'error', icon: ListChecks, text: t('alerts.numberDuplicates', { n: dupCount }), href: '/number-gaps', cta: t('alerts.review') });
  if (incompleteVi > 0)
    alerts.push({ key: 'vi', tone: 'warning', icon: FileInput, text: t('alerts.incompletePurchase', { n: incompleteVi }), href: '/vendor-invoices', cta: t('alerts.complete') });
  if (pnd30Due)
    alerts.push({ key: 'pnd30', tone: 'info', icon: Receipt, text: t('alerts.pnd30Due', { day: 15, month: monthFull(locale, (monthNo % 12) + 1) }), href: '/reports/pnd30', cta: t('alerts.prepare') });
  // Agent-created drafts pending human approval — one alert per doc type, each
  // linking to its OWN list (the DTO breaks the count out by type; a single
  // /tax-invoices link sent receipt/quotation drafts to the wrong list).
  // I1 (specs/fix-army-findings-2026-07-22.md O7 / army B-mcp F2) — the widget used to show a
  // row (and its "ตรวจ" deep-link) to every viewer regardless of whether they can actually open
  // that doc type — e.g. APPROVER holds zero sales.quotation.* perms, so the link 404s into an
  // empty list. Standing WP1/WP2 rule: never show a link that 403s. Gate each row on the SAME
  // per-doc-type READ permission the sidebar nav uses for that doc type's list page
  // (SidebarNav.tsx SECTIONS), reusing useHasScope (no new permission, no widened grant). A
  // filtered-out row is dropped entirely, so the count shown always matches the rows rendered.
  if (agentApprovals) {
    const agentTypes: { key: string; n: number; href: string; type: string; perm: string }[] = [
      { key: 'agentTi', n: agentApprovals.taxInvoices, href: '/tax-invoices', type: t('alerts.agentType.taxInvoice'), perm: 'sales.tax_invoice.read' },
      { key: 'agentQt', n: agentApprovals.quotations, href: '/quotations', type: t('alerts.agentType.quotation'), perm: 'sales.quotation.read' },
      { key: 'agentRc', n: agentApprovals.receipts, href: '/receipts', type: t('alerts.agentType.receipt'), perm: 'sales.receipt.read' },
      { key: 'agentPo', n: agentApprovals.purchaseOrders, href: '/purchase-orders', type: t('alerts.agentType.purchaseOrder'), perm: 'purchase.purchase_order.read' },
      { key: 'agentVi', n: agentApprovals.vendorInvoices, href: '/vendor-invoices', type: t('alerts.agentType.vendorInvoice'), perm: 'purchase.vendor_invoice.read' },
      { key: 'agentPv', n: agentApprovals.paymentVouchers, href: '/payment-vouchers', type: t('alerts.agentType.paymentVoucher'), perm: 'purchase.payment_voucher.read' },
    ];
    for (const a of agentTypes) {
      if (a.n > 0 && hasScope(a.perm))
        alerts.push({ key: a.key, tone: 'info', icon: Bot, text: t('alerts.agentApprovalsTyped', { n: a.n, type: a.type }), href: a.href, cta: t('alerts.review') });
    }
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <header className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <p className="text-sm text-base-content/60">{t('hello')}</p>
          <h1 className="text-2xl font-bold text-base-content">{companyName}</h1>
        </div>
        <p className="text-sm font-medium text-base-content/70">
          {t('overviewFor', { month: monthFull(locale, monthNo), year })}
        </p>
      </header>

      {/* KPI tiles — current month. S1 fix: skeleton while the tax summary is still
          loading, instead of flashing ฿0.00 (and, for the VAT tile, flashing it on a
          non-VAT company before vatMode is known). */}
      <section aria-label={t('kpi.section')}
        className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
        {summary.isLoading ? (
          Array.from({ length: 4 }, (_, i) => <KpiSkeleton key={i} />)
        ) : (
          <>
            <Kpi label={t('kpi.revenue')} value={formatTHB(cur?.revenue ?? 0)} icon={TrendingUp} tone="emerald" />
            <Kpi label={t('kpi.expense')} value={formatTHB(cur?.expense ?? 0)} icon={TrendingDown} tone="rose" />
            <Kpi label={t('kpi.netProfit')} value={formatTHB(cur?.netProfit ?? 0)} icon={Wallet}
              tone={(cur?.netProfit ?? 0) >= 0 ? 'emerald' : 'rose'} />
            {vatMode && (
              <Kpi label={t('kpi.vatNet')} icon={Receipt} tone="amber"
                value={netVat >= 0 ? formatTHB(netVat) : formatTHB(-netVat)}
                hint={netVat === 0 ? undefined : netVat > 0 ? t('payable') : t('refundable')} />
            )}
            <Kpi label={t('kpi.whtPaid')} value={formatTHB(cur?.whtPaidTotal ?? 0)} icon={Coins} tone="sky" />
          </>
        )}
      </section>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Trend chart — 2/3 width */}
        <section className="lg:col-span-2 rounded-xl border border-base-300 bg-base-100 p-5">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-sm font-semibold text-base-content/80">{t('trend.title', { year })}</h2>
            <Link href="/reports/tax-summary" className="flex items-center gap-1 text-xs font-medium text-primary hover:underline">
              {t('trend.detail')} <ArrowRight className="h-3.5 w-3.5" aria-hidden />
            </Link>
          </div>
          {summary.isLoading ? (
            <div className="flex h-52 items-end gap-1.5 px-2" aria-busy="true" aria-label={t('loading')}>
              {Array.from({ length: 12 }, (_, i) => (
                <div key={i} className="skeleton-shimmer flex-1 rounded-t" style={{ height: `${30 + ((i * 37) % 60)}%` }} />
              ))}
            </div>
          ) : months.length === 0 ? (
            <Empty text={t('trend.empty')} />
          ) : (
            <TrendBars months={months} t={t} locale={locale} />
          )}
        </section>

        {/* Action items — 1/3 width */}
        <section className="rounded-xl border border-base-300 bg-base-100 p-5">
          <h2 className="mb-4 text-sm font-semibold text-base-content/80">{t('alerts.section')}</h2>
          {alerts.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-8 text-center">
              <CheckCircle2 className="h-8 w-8 text-success" aria-hidden />
              <p className="text-sm text-base-content/60">{t('alerts.allClear')}</p>
            </div>
          ) : (
            <ul className="space-y-2.5">
              {alerts.map((a) => (
                <li key={a.key}>
                  <Link href={a.href}
                    className={`flex items-start gap-3 rounded-lg p-3 transition-colors ${ALERT_TONE[a.tone]}`}>
                    <a.icon className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
                    <span className="flex-1 text-sm leading-snug">{a.text}</span>
                    <span className="shrink-0 self-center text-xs font-semibold opacity-80">{a.cta}</span>
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      {/* Quick actions — R5 (ui-codebase-review-2026-08-20 #5): each button is gated
          individually below via <PermissionGate>, but the heading+wrapper used to render
          unconditionally, leaving an empty "ทางลัด" section for roles with zero of these
          5 scopes (e.g. sales_staff, rbac_auditor). Mirror the SAME scope checks here
          (same source, useHasScope) so the section only renders when ≥1 button will. */}
      {anyQuickAction && (
        <section>
          <h2 className="mb-3 text-sm font-semibold text-base-content/80">{t('quick.title')}</h2>
          <div className="flex flex-wrap gap-2">
            {vatMode && (
              <PermissionGate scope="sales.tax_invoice.create">
                <QuickAction href="/tax-invoices/new" icon={FileText} label={t('quick.taxInvoice')} />
              </PermissionGate>
            )}
            <PermissionGate scope="sales.receipt.create">
              <QuickAction href="/receipts/new" icon={Receipt} label={t('quick.receipt')} />
            </PermissionGate>
            <PermissionGate scope="purchase.payment_voucher.create">
              <QuickAction href="/payment-vouchers/new" icon={Wallet} label={t('quick.paymentVoucher')} />
            </PermissionGate>
            <PermissionGate scope="master.customer.manage">
              <QuickAction href="/customers/new" icon={Plus} label={t('quick.customer')} />
            </PermissionGate>
            <PermissionGate scope="master.vendor.manage">
              <QuickAction href="/vendors/new" icon={Plus} label={t('quick.vendor')} />
            </PermissionGate>
          </div>
        </section>
      )}
    </div>
  );
}

const KPI_TONE: Record<string, string> = {
  emerald: 'border-status-success/30 bg-status-success-bg text-status-success',
  rose:    'border-status-danger/30 bg-status-danger-bg text-status-danger',
  amber:   'border-status-warning/30 bg-status-warning-bg text-status-warning',
  sky:     'border-status-info/30 bg-status-info-bg text-status-info',
};
const ALERT_TONE: Record<string, string> = {
  error:   'bg-status-danger-bg text-status-danger hover:opacity-90',
  warning: 'bg-status-warning-bg text-status-warning hover:opacity-90',
  info:    'bg-status-info-bg text-status-info hover:opacity-90',
};

function Kpi({ label, value, icon: Icon, tone, hint }: {
  label: string; value: string; icon: typeof Wallet; tone: keyof typeof KPI_TONE; hint?: string;
}) {
  return (
    <div className={`rounded-xl border p-4 ${KPI_TONE[tone]}`}>
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium opacity-80">{label}</span>
        <Icon className="h-4 w-4 opacity-70" aria-hidden />
      </div>
      <div className="mt-1.5 text-xl font-bold tabular-nums">{value}</div>
      {hint && <div className="text-[11px] opacity-70">{hint}</div>}
    </div>
  );
}

function KpiSkeleton() {
  return (
    <div className="rounded-xl border border-base-300 bg-base-100 p-4">
      <div className="flex items-center justify-between">
        <span className="skeleton-shimmer h-3 w-16 rounded" />
        <span className="skeleton-shimmer h-4 w-4 rounded" />
      </div>
      <div className="mt-1.5 skeleton-shimmer h-6 w-24 rounded" />
    </div>
  );
}

function QuickAction({ href, icon: Icon, label }: { href: string; icon: typeof Wallet; label: string }) {
  return (
    <Link href={href}
      className="flex items-center gap-2 rounded-lg border border-base-300 bg-base-100 px-3.5 py-2 text-sm font-medium text-base-content/80 transition-colors hover:border-primary/40 hover:bg-base-200 hover:text-base-content">
      <Icon className="h-4 w-4 text-primary" aria-hidden /> {label}
    </Link>
  );
}

function Empty({ text }: { text: string }) {
  return <div className="grid h-52 place-items-center text-sm text-base-content/40">{text}</div>;
}

// Inline SVG dual-series bars (revenue vs expense) — no chart dependency, mirrors
// the reports/tax-summary GroupedBars so the dashboard reads consistently.
function TrendBars({ months, t, locale }: { months: TaxSummaryMonth[]; t: ReturnType<typeof useTranslations>; locale: string }) {
  const series = [
    { key: 'revenue' as const, label: t('kpi.revenue'), className: 'fill-emerald-500' },
    { key: 'expense' as const, label: t('kpi.expense'), className: 'fill-rose-400' },
  ];
  const W = 560, H = 200, padL = 8, padB = 24, padT = 8;
  const plotH = H - padB - padT;
  const max = Math.max(1, ...months.flatMap((m) => series.map((s) => Math.abs(Number(m[s.key])))));
  const groupW = (W - padL * 2) / 12;
  const barW = Math.max(2, (groupW - 4) / series.length);

  return (
    <div className="w-full overflow-x-auto">
      <svg viewBox={`0 0 ${W} ${H}`} className="h-52 w-full" role="img" aria-label={t('trend.title', { year: months[0]?.month ? new Date().getFullYear() : '' })}>
        <line x1={padL} y1={padT + plotH} x2={W - padL} y2={padT + plotH} className="stroke-base-300" strokeWidth={1} />
        {months.map((m, gi) => {
          const gx = padL + gi * groupW + 2;
          const label = monthShort(locale, m.month);
          return (
            <g key={m.month}>
              {series.map((s, si) => {
                const v = Math.abs(Number(m[s.key]));
                const h = (v / max) * plotH;
                return (
                  <rect key={s.key} className={s.className} x={gx + si * barW} y={padT + plotH - h}
                    width={barW - 1} height={h} rx={1}>
                    <title>{`${label} · ${s.label}: ${formatTHB(Number(m[s.key]))}`}</title>
                  </rect>
                );
              })}
              <text x={gx + (groupW - 4) / 2} y={H - 8} textAnchor="middle"
                className="fill-base-content/50 text-[8px]">{label}</text>
            </g>
          );
        })}
        <text x={W - padL} y={padT + 8} textAnchor="end" className="fill-base-content/40 text-[9px]">{kBaht(max, locale)}</text>
      </svg>
      <div className="mt-2 flex flex-wrap gap-3">
        {series.map((s) => (
          <span key={s.key} className="flex items-center gap-1.5 text-xs text-base-content/70">
            <svg width="10" height="10"><rect width="10" height="10" rx="2" className={s.className} /></svg>
            {s.label}
          </span>
        ))}
      </div>
    </div>
  );
}
