'use client';

import Link from 'next/link';
import { useTranslations, useLocale } from 'next-intl';
import {
  TrendingUp, TrendingDown, Wallet, Receipt, Coins, ListChecks, FileInput,
  AlertTriangle, CheckCircle2, ArrowRight, FileText, Plus,
} from 'lucide-react';
import { PermissionGate } from '@/components/PermissionGate';
import {
  useTaxSummary, useNumberGaps, useVatThresholdStatus, useVendorInvoices,
  useSystemInfo, useCompanyProfile,
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
  const vatMode = useSystemInfo().data?.vatMode ?? true;

  const summary = useTaxSummary(year);
  const months = summary.data?.months ?? [];
  const cur = months.find((m) => m.month === monthNo);

  const gaps = useNumberGaps();
  const gapCount = gaps.data?.gaps.length ?? 0;
  const threshold = useVatThresholdStatus().data?.status;
  const incompleteVi = useVendorInvoices(true).data?.length ?? 0;

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
  if (incompleteVi > 0)
    alerts.push({ key: 'vi', tone: 'warning', icon: FileInput, text: t('alerts.incompletePurchase', { n: incompleteVi }), href: '/vendor-invoices', cta: t('alerts.complete') });
  if (pnd30Due)
    alerts.push({ key: 'pnd30', tone: 'info', icon: Receipt, text: t('alerts.pnd30Due', { day: 15, month: monthFull(locale, (monthNo % 12) + 1) }), href: '/reports/pnd30', cta: t('alerts.prepare') });

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

      {/* KPI tiles — current month */}
      <section aria-label={t('kpi.section')}
        className="grid grid-cols-2 gap-3 md:grid-cols-3 xl:grid-cols-5">
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
            <div className="grid h-52 place-items-center text-sm text-base-content/40">{t('loading')}</div>
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

      {/* Quick actions */}
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
    </div>
  );
}

const KPI_TONE: Record<string, string> = {
  emerald: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  rose: 'border-rose-200 bg-rose-50 text-rose-700',
  amber: 'border-amber-200 bg-amber-50 text-amber-700',
  sky: 'border-sky-200 bg-sky-50 text-sky-700',
};
const ALERT_TONE: Record<string, string> = {
  error: 'bg-rose-50 text-rose-800 hover:bg-rose-100',
  warning: 'bg-amber-50 text-amber-800 hover:bg-amber-100',
  info: 'bg-sky-50 text-sky-800 hover:bg-sky-100',
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
