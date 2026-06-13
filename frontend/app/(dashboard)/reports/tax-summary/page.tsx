'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { PageHeader } from '@/components/ui/PageHeader';
import { useTaxSummary, useBusinessUnits } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import type { TaxSummaryMonth } from '@/lib/types';

const THAI_MONTHS = ['ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.',
  'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.'];

function kBaht(n: number): string {
  const a = Math.abs(n);
  if (a >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (a >= 1_000) return `${(n / 1_000).toFixed(0)}K`;
  return n.toFixed(0);
}

export default function TaxSummaryPage() {
  const t = useTranslations('taxSummary');
  const tc = useTranslations('common');
  const nowYear = new Date().getFullYear();
  const [year, setYear] = useState(nowYear);
  const [buId, setBuId] = useState<number | undefined>(undefined);
  const bus = useBusinessUnits();
  const q = useTaxSummary(year, buId);
  const data = q.data;
  const totals = data?.totals;
  const months = data?.months ?? [];

  return (
    <>
      <PageHeader title={t('title')} subtitle={t('subtitle')} />

      <div className="mb-5 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('year')}</span>
          <select className="select select-bordered select-sm w-32" data-testid="ts-year"
            value={year} onChange={(e) => setYear(Number(e.target.value))}>
            {Array.from({ length: 6 }, (_, i) => nowYear - i).map((y) => (
              <option key={y} value={y}>{y} ({y + 543})</option>
            ))}
          </select>
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('bu')}</span>
          <select className="select select-bordered select-sm" data-testid="ts-bu"
            value={buId ?? ''} onChange={(e) =>
              setBuId(e.target.value ? Number(e.target.value) : undefined)}>
            <option value="">— {t('allBu')} —</option>
            {bus.data?.map((b) => (
              <option key={b.businessUnitId} value={b.businessUnitId}>{b.code} · {b.nameTh}</option>
            ))}
          </select>
        </label>
        {q.isLoading && <span className="text-sm text-base-content/50">{tc('loading')}</span>}
      </div>

      {/* ── KPI cards (year totals) ─────────────────────────────────────────── */}
      {totals && (
        <div className="mb-6 grid grid-cols-2 gap-3 lg:grid-cols-3" data-testid="ts-kpis">
          <KpiCard label={t('kpi.revenue')} value={formatTHB(totals.revenue)} tone="emerald" />
          <KpiCard label={t('kpi.expense')} value={formatTHB(totals.expense)} tone="rose" />
          <KpiCard label={t('kpi.netProfit')} value={formatTHB(totals.netProfit)}
            tone={totals.netProfit >= 0 ? 'emerald' : 'rose'} />
          <KpiCard label={t('kpi.vatNet')}
            value={totals.vatPayable - totals.vatRefundable >= 0
              ? `${formatTHB(totals.vatPayable - totals.vatRefundable)} ${t('payable')}`
              : `${formatTHB(totals.vatRefundable - totals.vatPayable)} ${t('refundable')}`}
            tone="amber" />
          <KpiCard label={t('kpi.whtPaid')} value={formatTHB(totals.whtPaidTotal)} tone="sky" />
          <KpiCard label={t('kpi.whtReceived')} value={formatTHB(totals.whtReceived)} tone="violet"
            hint={t('kpi.whtReceivedHint')} />
        </div>
      )}

      {/* ── Charts ──────────────────────────────────────────────────────────── */}
      {totals && (
        <div className="mb-6 grid grid-cols-1 gap-4 xl:grid-cols-2">
          <ChartCard title={t('chart.revVsExp')}>
            <GroupedBars months={months}
              series={[
                { key: 'revenue', label: t('revenue'), className: 'fill-emerald-500' },
                { key: 'expense', label: t('expense'), className: 'fill-rose-400' },
              ]} />
          </ChartCard>
          <ChartCard title={t('chart.tax')}>
            <GroupedBars months={months}
              series={[
                { key: 'vatPayable', label: t('vatPayable'), className: 'fill-amber-500' },
                { key: 'whtPaidTotal', label: t('whtPaid'), className: 'fill-sky-500' },
                { key: 'whtReceived', label: t('whtReceived'), className: 'fill-violet-400' },
              ]} />
          </ChartCard>
        </div>
      )}

      {/* ── Monthly table ───────────────────────────────────────────────────── */}
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-sm table-zebra" data-testid="ts-table">
          <thead>
            <tr>
              <th>{t('month')}</th>
              <th className="text-right">{t('revenue')}</th>
              <th className="text-right">{t('expense')}</th>
              <th className="text-right">{t('netProfit')}</th>
              <th className="text-right">{t('outputVat')}</th>
              <th className="text-right">{t('inputVat')}</th>
              <th className="text-right">{t('vatNet')}</th>
              <th className="text-right">ภ.ง.ด.3</th>
              <th className="text-right">ภ.ง.ด.53</th>
              <th className="text-right">ภ.ง.ด.54</th>
              <th className="text-right">ภ.ง.ด.1</th>
              <th className="text-right">{t('whtPaid')}</th>
              <th className="text-right">{t('whtReceived')}</th>
            </tr>
          </thead>
          <tbody>
            {months.map((m) => (
              <tr key={m.month}>
                <td className="font-medium">{THAI_MONTHS[m.month - 1]}</td>
                <td className="text-right tabular-nums">{cell(m.revenue)}</td>
                <td className="text-right tabular-nums">{cell(m.expense)}</td>
                <td className={`text-right tabular-nums ${m.netProfit < 0 ? 'text-error' : ''}`}>{cell(m.netProfit)}</td>
                <td className="text-right tabular-nums">{cell(m.outputVat)}</td>
                <td className="text-right tabular-nums">{cell(m.inputVat)}</td>
                <td className="text-right tabular-nums">
                  <Link className="hover:underline" href="/reports/pnd30">{vatNetCell(m, t)}</Link>
                </td>
                <td className="text-right tabular-nums">{cell(m.whtPaidPnd3)}</td>
                <td className="text-right tabular-nums">{cell(m.whtPaidPnd53)}</td>
                <td className="text-right tabular-nums">{cell(m.whtPaidPnd54)}</td>
                <td className="text-right tabular-nums">{cell(m.whtPaidPnd1)}</td>
                <td className="text-right tabular-nums font-medium">
                  <Link className="hover:underline" href="/tax-filings">{cell(m.whtPaidTotal)}</Link>
                </td>
                <td className="text-right tabular-nums">
                  <Link className="hover:underline" href="/reports/wht-receivable">{cell(m.whtReceived)}</Link>
                </td>
              </tr>
            ))}
          </tbody>
          {totals && (
            <tfoot>
              <tr className="font-bold">
                <td>{t('totalRow')}</td>
                <td className="text-right tabular-nums">{cell(totals.revenue)}</td>
                <td className="text-right tabular-nums">{cell(totals.expense)}</td>
                <td className={`text-right tabular-nums ${totals.netProfit < 0 ? 'text-error' : ''}`}>{cell(totals.netProfit)}</td>
                <td className="text-right tabular-nums">{cell(totals.outputVat)}</td>
                <td className="text-right tabular-nums">{cell(totals.inputVat)}</td>
                <td className="text-right tabular-nums">{vatNetCell(totals, t)}</td>
                <td className="text-right tabular-nums">{cell(totals.whtPaidPnd3)}</td>
                <td className="text-right tabular-nums">{cell(totals.whtPaidPnd53)}</td>
                <td className="text-right tabular-nums">{cell(totals.whtPaidPnd54)}</td>
                <td className="text-right tabular-nums">{cell(totals.whtPaidPnd1)}</td>
                <td className="text-right tabular-nums">{cell(totals.whtPaidTotal)}</td>
                <td className="text-right tabular-nums">{cell(totals.whtReceived)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>

      {buId !== undefined && (
        <p className="mt-3 text-xs text-amber-700">{t('buNote')}</p>
      )}
      <p className="mt-3 text-xs text-base-content/60">{t('footnote')}</p>
    </>
  );
}

function cell(n: number): string {
  return n === 0 ? '—' : formatTHB(n);
}

function vatNetCell(m: TaxSummaryMonth, t: ReturnType<typeof useTranslations>): string {
  const net = m.vatPayable - m.vatRefundable;
  if (net === 0) return '—';
  return net > 0 ? formatTHB(net) : `(${formatTHB(-net)})`;
}

const TONE: Record<string, string> = {
  emerald: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  rose: 'border-rose-200 bg-rose-50 text-rose-700',
  amber: 'border-amber-200 bg-amber-50 text-amber-700',
  sky: 'border-sky-200 bg-sky-50 text-sky-700',
  violet: 'border-violet-200 bg-violet-50 text-violet-700',
};

function KpiCard({ label, value, tone, hint }: {
  label: string; value: string; tone: keyof typeof TONE | string; hint?: string;
}) {
  return (
    <div className={`rounded-xl border p-4 ${TONE[tone] ?? TONE.sky}`}>
      <div className="text-xs font-medium opacity-80">{label}</div>
      <div className="mt-1 text-xl font-bold tabular-nums">{value}</div>
      {hint && <div className="mt-0.5 text-[11px] opacity-70">{hint}</div>}
    </div>
  );
}

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-base-300 bg-base-100 p-4">
      <h3 className="mb-3 text-sm font-semibold text-base-content/80">{title}</h3>
      {children}
    </div>
  );
}

// Inline SVG grouped bars — no chart dependency. 12 month groups, N series each.
function GroupedBars({ months, series }: {
  months: TaxSummaryMonth[];
  series: { key: keyof TaxSummaryMonth; label: string; className: string }[];
}) {
  const W = 560, H = 200, padL = 8, padB = 24, padT = 8;
  const plotH = H - padB - padT;
  const max = Math.max(1, ...months.flatMap((m) => series.map((s) => Math.abs(Number(m[s.key])))));
  const groupW = (W - padL * 2) / 12;
  const barW = Math.max(2, (groupW - 4) / series.length);

  return (
    <div className="w-full overflow-x-auto">
      <svg viewBox={`0 0 ${W} ${H}`} className="h-52 w-full" role="img">
        {/* baseline */}
        <line x1={padL} y1={padT + plotH} x2={W - padL} y2={padT + plotH}
          className="stroke-base-300" strokeWidth={1} />
        {months.map((m, gi) => {
          const gx = padL + gi * groupW + 2;
          return (
            <g key={m.month}>
              {series.map((s, si) => {
                const v = Math.abs(Number(m[s.key]));
                const h = (v / max) * plotH;
                return (
                  <rect key={s.key} className={s.className}
                    x={gx + si * barW} y={padT + plotH - h}
                    width={barW - 1} height={h} rx={1}>
                    <title>{`${THAI_MONTHS[m.month - 1]} · ${s.label}: ${formatTHB(Number(m[s.key]))}`}</title>
                  </rect>
                );
              })}
              <text x={gx + (groupW - 4) / 2} y={H - 8} textAnchor="middle"
                className="fill-base-content/50 text-[8px]">{THAI_MONTHS[m.month - 1]}</text>
            </g>
          );
        })}
        {/* max gridline label */}
        <text x={W - padL} y={padT + 8} textAnchor="end"
          className="fill-base-content/40 text-[9px]">{kBaht(max)}</text>
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
