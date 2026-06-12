'use client';

import { useCallback, useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { apiDelete, apiGet, apiPost, apiPut, openPdf } from '@/lib/api';
import { formatTHB } from '@/lib/utils';

// Phase C-C — CIT year data (ภ.ง.ด.50 foundations): SME profile, per-FY net-profit
// summary (computed/override/effective) and the ม.65ทวิ/65ตรี adjustment store.
// Mirrors the pnd51 page idiom: useState + lib/api fetch helpers, no RHF/Zod.

interface CitYearSummary {
  fiscalYear: number;
  computedNetProfit: number | null;
  overrideNetProfit: number | null;
  effectiveNetProfit: number | null;
  pnd51EstimatedProfit: number | null;
  pnd51Prepaid: number | null;
  note: string | null;
}

interface CitAdjustment {
  citAdjustmentId: number;
  fiscalYear: number;
  legalRefCode: string;
  label: string;
  amount: number;
}

interface CitProfile {
  fiscalYear: number;
  paidUpCapital: number | null;
  revenueFullYear: number;
  isSme: boolean;
  adjustmentsTotal: number;
  lossCarryIn: number;
  accountingNetProfit: number;
}

// ภ.ง.ด.50 v2 dashboard preview — single-source dry-run (same figures the filler prints).
interface Pnd50Ladder {
  directRevenue: number;
  costOfSales: number;
  grossProfit: number;
  otherIncome: number;
  total5: number;
  otherExpenses: number;
  total7: number;
  sellingAdminExpenses: number;
  accountingNetProfit: number;
  incomeAdditions: number;
  disallowedExpenses: number;
  total12: number;
  exemptDeductions: number;
  total14: number;
  lossCarryForward: number;
  total16: number;
  total20: number;
  taxableNetProfit: number;
}

interface Pnd50BalanceSheet {
  cashAndEquivalents: number;
  tradeReceivables: number;
  inventory: number;
  otherCurrentAssets: number;
  otherNonCurrentAssets: number;
  totalAssets: number;
  tradePayables: number;
  otherCurrentLiabilities: number;
  otherNonCurrentLiabilities: number;
  totalLiabilities: number;
  paidUpShareCapital: number;
  otherEquity: number;
  retainedEarnings: number;
  totalEquity: number;
  totalLiabilitiesAndEquity: number;
  balanced: boolean;
}

interface Pnd50WhtCert {
  docNo: string;
  docDate: string;
  customerName: string;
  customerTaxId: string | null;
  whtAmount: number;
  customerWhtCertNo: string | null;
}

// C-D — p5 รายการที่ 7 partition (column ③); total == ladder row 8.
interface Pnd50ExpenseSchedule {
  employee: number;
  directorComp: number;
  utilities: number;
  travel: number;
  freight: number;
  rent: number;
  repairs: number;
  entertainment: number;
  marketing: number;
  sbtTax: number;
  otherTaxes: number;
  financeCost: number;
  bookkeeping: number;
  auditFee: number;
  politicalDonation: number;
  charityDonation: number;
  educationSport: number;
  consulting: number;
  otherFees: number;
  badDebt: number;
  depreciation: number;
  other: number;
  doubleDeduct: number;
  total: number;
}

// C-D — p5 รายการที่ 8 (positive adjustments classified); total == ladder row 11.
interface Pnd50Disallowed {
  incomeTax: number;
  entertainment: number;
  badDebt: number;
  provisions: number;
  fromItem7Line23: number;
  other: number;
  total: number;
}

interface Pnd50Preview {
  year: number;
  periodStart: string;
  periodEnd: string;
  isSme: boolean;
  paidUpCapital: number | null;
  revenue: number;
  expenses: number;
  pnd51EstimatedProfit: number | null;
  pnd51Prepaid: number;
  whtCreditTotal: number;
  whtCertificates: Pnd50WhtCert[];
  ladder: Pnd50Ladder | null;
  adjustments: CitAdjustment[];
  expenseSchedule: Pnd50ExpenseSchedule | null;
  disallowed: Pnd50Disallowed | null;
  taxBeforeCredits: number;
  creditsTotal: number;
  netPayable: number;
  payMore: boolean;
  surcharge: number;
  totalDue: number;
  balanceSheet: Pnd50BalanceSheet;
  refusals: string[];
}

const money = (v: number | null | undefined) => (v == null ? '—' : formatTHB(v));

// Informational refusals warn on the dashboard but never block the PDF (the disclosure form is
// a separate manual filing — see Pnd50FilingService.InformationalRefusals).
const INFORMATIONAL_REFUSALS = ['pnd50.disclosure_required'];

// [i18n key, value] pairs for the รายการที่ 7 lines (1-23, form margin order).
const expenseLines = (s: Pnd50ExpenseSchedule): Array<[string, number]> => [
  ['es1', s.employee], ['es2', s.directorComp], ['es3', s.utilities], ['es4', s.travel],
  ['es5', s.freight], ['es6', s.rent], ['es7', s.repairs], ['es8', s.entertainment],
  ['es9', s.marketing], ['es10', s.sbtTax], ['es11', s.otherTaxes], ['es12', s.financeCost],
  ['es13', s.bookkeeping], ['es14', s.auditFee], ['es15', s.politicalDonation],
  ['es16', s.charityDonation], ['es17', s.educationSport], ['es18', s.consulting],
  ['es19', s.otherFees], ['es20', s.badDebt], ['es21', s.depreciation], ['es22', s.other],
  ['es23', s.doubleDeduct],
];

const disallowedLines = (d: Pnd50Disallowed): Array<[string, number]> => [
  ['ds1', d.incomeTax], ['ds2', d.entertainment], ['ds3', d.badDebt],
  ['ds4', d.provisions], ['ds5', d.fromItem7Line23], ['ds6', d.other],
];

export default function CitYearDataPage() {
  const t = useTranslations('cit');
  const tc = useTranslations('common');
  const [year, setYear] = useState(new Date().getFullYear());
  const [profile, setProfile] = useState<CitProfile | null>(null);
  const [summary, setSummary] = useState<CitYearSummary | null>(null);
  const [adjustments, setAdjustments] = useState<CitAdjustment[]>([]);
  const [preview, setPreview] = useState<Pnd50Preview | null>(null);
  const [loading, setLoading] = useState(false);

  // year-summary edit fields
  const [override, setOverride] = useState('');
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [computing, setComputing] = useState(false);

  // ภ.ง.ด.50 v1 download (p1+p2; the service 422s pnd50.not_attestable unless clean)
  const [hasRelatedParty, setHasRelatedParty] = useState(false);
  const [attestFirstFiling, setAttestFirstFiling] = useState(false);
  const [attestBlankSchedules, setAttestBlankSchedules] = useState(false);
  const [pnd50Busy, setPnd50Busy] = useState(false);

  // adjustment add/edit form (editId = null → add mode)
  const [editId, setEditId] = useState<number | null>(null);
  const [refCode, setRefCode] = useState('');
  const [label, setLabel] = useState('');
  const [amount, setAmount] = useState('');
  const [adjBusy, setAdjBusy] = useState(false);

  function applySummary(s: CitYearSummary | null) {
    setSummary(s);
    setOverride(s?.overrideNetProfit != null ? String(s.overrideNetProfit) : '');
    setNote(s?.note ?? '');
  }

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [p, years, adj] = await Promise.all([
        apiGet<CitProfile>(`tax-filings/cit/profile?year=${year}`),
        apiGet<CitYearSummary[]>('tax-filings/cit/years'),
        apiGet<CitAdjustment[]>(`tax-filings/cit/adjustments?year=${year}`),
      ]);
      setProfile(p);
      applySummary(years.find((y) => y.fiscalYear === year) ?? null);
      setAdjustments(adj);
      // ภ.ง.ด.50 dashboard dry-run (never throws — refusals are reported, not fatal).
      setPreview(await apiGet<Pnd50Preview>(`tax-filings/pnd50/preview?year=${year}`));
    } catch {
      toast.error(t('loadError'));
    } finally {
      setLoading(false);
    }
  }, [year, t]);

  useEffect(() => {
    void load();
  }, [load]);

  async function compute() {
    setComputing(true);
    try {
      applySummary(await apiPost<CitYearSummary>(`tax-filings/cit/years/${year}/compute`));
      // adjustments feed the computed figure — refresh the profile + dashboard preview too
      setProfile(await apiGet<CitProfile>(`tax-filings/cit/profile?year=${year}`));
      setPreview(await apiGet<Pnd50Preview>(`tax-filings/pnd50/preview?year=${year}`));
      toast.success(t('saved'));
    } catch {
      toast.error(t('saveError'));
    } finally {
      setComputing(false);
    }
  }

  async function saveOverride() {
    setSaving(true);
    try {
      applySummary(await apiPut<CitYearSummary>(`tax-filings/cit/years/${year}`, {
        overrideNetProfit: override.trim() === '' ? null : Number(override),
        note: note.trim() === '' ? null : note.trim(),
      }));
      setPreview(await apiGet<Pnd50Preview>(`tax-filings/pnd50/preview?year=${year}`));
      toast.success(t('saved'));
    } catch {
      toast.error(t('saveError'));
    } finally {
      setSaving(false);
    }
  }

  async function downloadPnd50() {
    setPnd50Busy(true);
    try {
      const params = new URLSearchParams({ year: String(year) });
      if (hasRelatedParty) params.set('hasRelatedParty', 'true');
      params.set('attestFirstFiling', String(attestFirstFiling));
      params.set('attestBlankSchedules', String(attestBlankSchedules));
      await openPdf(`tax-filings/pnd50/pdf?${params}`);
    } catch {
      toast.error(t('pnd50Error'));
    } finally {
      setPnd50Busy(false);
    }
  }

  function startEdit(a: CitAdjustment) {
    setEditId(a.citAdjustmentId);
    setRefCode(a.legalRefCode);
    setLabel(a.label);
    setAmount(String(a.amount));
  }

  function resetAdjForm() {
    setEditId(null);
    setRefCode('');
    setLabel('');
    setAmount('');
  }

  const adjValid =
    refCode.trim() !== '' && label.trim() !== '' && Number.isFinite(Number(amount)) && amount.trim() !== '';

  async function saveAdjustment() {
    if (!adjValid) return;
    setAdjBusy(true);
    try {
      const body = { legalRefCode: refCode.trim(), label: label.trim(), amount: Number(amount) };
      if (editId == null) {
        await apiPost<CitAdjustment>(`tax-filings/cit/adjustments?year=${year}`, body);
      } else {
        await apiPut<CitAdjustment>(`tax-filings/cit/adjustments/${editId}`, body);
      }
      resetAdjForm();
      await load();
      toast.success(t('saved'));
    } catch {
      toast.error(t('saveError'));
    } finally {
      setAdjBusy(false);
    }
  }

  async function deleteAdjustment(id: number) {
    setAdjBusy(true);
    try {
      await apiDelete<void>(`tax-filings/cit/adjustments/${id}`);
      if (editId === id) resetAdjForm();
      await load();
      toast.success(t('saved'));
    } catch {
      toast.error(t('saveError'));
    } finally {
      setAdjBusy(false);
    }
  }

  return (
    <>
      <PageHeader title={t('title')} />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('year')}</span>
          <input
            type="number"
            className="input input-bordered input-sm w-28"
            value={year}
            min={2000}
            max={2200}
            onChange={(e) => setYear(Number(e.target.value))}
          />
        </label>
        {loading && <span className="loading loading-spinner loading-sm self-end" />}
      </div>

      <div className="mb-4 grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* ── SME profile card ── */}
        <div className="rounded-lg border border-base-300 p-4">
          <div className="mb-2 flex items-center gap-2">
            <h2 className="font-semibold">{t('profileTitle')}</h2>
            {profile && (
              <span className={`badge badge-sm ${profile.isSme ? 'badge-success' : 'badge-ghost'}`}>
                {profile.isSme ? t('smeBadge') : t('generalBadge')}
              </span>
            )}
          </div>
          {profile ? (
            <dl className="grid grid-cols-2 gap-y-1.5 text-sm">
              <dt className="text-base-content/60">{t('paidUpCapital')}</dt>
              <dd className="text-right tabular-nums">{money(profile.paidUpCapital)}</dd>
              <dt className="text-base-content/60">{t('revenue')}</dt>
              <dd className="text-right tabular-nums">{money(profile.revenueFullYear)}</dd>
              <dt className="text-base-content/60">{t('lossCarryIn')}</dt>
              <dd className="text-right tabular-nums">{money(profile.lossCarryIn)}</dd>
              <dt className="text-base-content/60">{t('adjustmentsTotal')}</dt>
              <dd className="text-right tabular-nums">{money(profile.adjustmentsTotal)}</dd>
              <dt className="text-base-content/60">{t('accountingNetProfit')}</dt>
              <dd className="text-right tabular-nums">{money(profile.accountingNetProfit)}</dd>
            </dl>
          ) : (
            <p className="text-sm text-base-content/50">{tc('loading')}</p>
          )}
        </div>

        {/* ── year-summary card ── */}
        <div className="rounded-lg border border-base-300 p-4">
          <div className="mb-2 flex items-center justify-between gap-2">
            <h2 className="font-semibold">{t('summaryTitle')}</h2>
            <button className="btn btn-sm btn-outline" disabled={computing || loading} onClick={compute}>
              {computing && <span className="loading loading-spinner loading-xs" />}
              {t('compute')}
            </button>
          </div>
          <dl className="grid grid-cols-2 gap-y-1.5 text-sm">
            <dt className="text-base-content/60">{t('computedNetProfit')}</dt>
            <dd className="text-right tabular-nums">{money(summary?.computedNetProfit)}</dd>
            <dt className="text-base-content/60">{t('effectiveNetProfit')}</dt>
            <dd className="text-right font-semibold tabular-nums">{money(summary?.effectiveNetProfit)}</dd>
            <dt className="text-base-content/60">{t('pnd51Estimate')}</dt>
            <dd className="text-right tabular-nums">{money(summary?.pnd51EstimatedProfit)}</dd>
            <dt className="text-base-content/60">{t('pnd51Prepaid')}</dt>
            <dd className="text-right tabular-nums">{money(summary?.pnd51Prepaid)}</dd>
          </dl>
          <div className="mt-3 flex flex-wrap items-end gap-3">
            <label className="form-control">
              <span className="label-text text-xs">{t('overrideNetProfit')}</span>
              <input
                type="number"
                className="input input-bordered input-sm w-44"
                value={override}
                onChange={(e) => setOverride(e.target.value)}
              />
            </label>
            <label className="form-control grow">
              <span className="label-text text-xs">{t('note')}</span>
              <input
                className="input input-bordered input-sm w-full"
                value={note}
                onChange={(e) => setNote(e.target.value)}
              />
            </label>
            <button className="btn btn-sm btn-primary" disabled={saving || loading} onClick={saveOverride}>
              {saving && <span className="loading loading-spinner loading-xs" />}
              {tc('save')}
            </button>
          </div>
        </div>
      </div>

      {/* ── ภ.ง.ด.50 dashboard: every figure that will be filled, before generating ── */}
      <div className="mb-4 grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* ① ภ.ง.ด.51 ที่ยื่นไว้ + ② บันไดคำนวณ ภ.ง.ด.50 */}
        <div className="rounded-lg border border-base-300 p-4">
          <div className="mb-2 flex items-center gap-2">
            <h2 className="font-semibold">{t('ladderTitle')}</h2>
            {preview && (
              <span className={`badge badge-sm ${preview.isSme ? 'badge-success' : 'badge-ghost'}`}>
                {preview.isSme ? t('smeBadge') : t('generalBadge')}
              </span>
            )}
          </div>
          {preview?.ladder ? (
            <dl className="grid grid-cols-[1fr_auto] gap-y-1 text-sm">
              <dt className="text-base-content/60">{t('ladderDirectRevenue')}</dt>
              <dd className="text-right tabular-nums">{money(preview.ladder.directRevenue)}</dd>
              <dt className="text-base-content/60">{t('ladderSellingAdmin')}</dt>
              <dd className="text-right tabular-nums">{money(preview.ladder.sellingAdminExpenses)}</dd>
              <dt className="text-base-content/60">{t('ladderAccountingNetProfit')}</dt>
              <dd className="text-right tabular-nums">{money(preview.ladder.accountingNetProfit)}</dd>
              <dt className="text-base-content/60">{t('ladderDisallowed')}</dt>
              <dd className="text-right tabular-nums">{money(preview.ladder.disallowedExpenses)}</dd>
              <dt className="text-base-content/60">{t('ladderExempt')}</dt>
              <dd className="text-right tabular-nums">{money(preview.ladder.exemptDeductions)}</dd>
              <dt className="text-base-content/60">{t('ladderLossCf')}</dt>
              <dd className="text-right tabular-nums">{money(preview.ladder.lossCarryForward)}</dd>
              <dt className="font-semibold">{t('ladderTaxable')}</dt>
              <dd className="text-right font-semibold tabular-nums">{money(preview.ladder.taxableNetProfit)}</dd>
              <dt className="text-base-content/60">{t('taxBeforeCredits')}</dt>
              <dd className="text-right tabular-nums">{money(preview.taxBeforeCredits)}</dd>
              <dt className="text-base-content/60">{t('creditsTotal')}</dt>
              <dd className="text-right tabular-nums">{money(preview.creditsTotal)}</dd>
              <dt className="text-base-content/60">{t('pnd51Prepaid')}</dt>
              <dd className="text-right tabular-nums">{money(preview.pnd51Prepaid)}</dd>
              {preview.surcharge > 0 && (
                <>
                  <dt className="text-warning">{t('surcharge')}</dt>
                  <dd className="text-right tabular-nums text-warning">{money(preview.surcharge)}</dd>
                </>
              )}
              <dt className="font-semibold">{preview.payMore ? t('totalPayable') : t('totalOverpaid')}</dt>
              <dd className="text-right font-semibold tabular-nums">{money(preview.totalDue)}</dd>
            </dl>
          ) : (
            <p className="text-sm text-base-content/50">{preview ? t('ladderUnavailable') : tc('loading')}</p>
          )}
        </div>

        {/* ⑤ งบแสดงฐานะการเงินย่อ */}
        <div className="rounded-lg border border-base-300 p-4">
          <div className="mb-2 flex items-center gap-2">
            <h2 className="font-semibold">{t('balanceSheetTitle')}</h2>
            {preview && (
              <span className={`badge badge-sm ${preview.balanceSheet.balanced ? 'badge-success' : 'badge-error'}`}>
                {preview.balanceSheet.balanced ? t('balanced') : t('unbalanced')}
              </span>
            )}
          </div>
          {preview ? (
            <dl className="grid grid-cols-[1fr_auto] gap-y-1 text-sm">
              <dt className="text-base-content/60">{t('bsTotalAssets')}</dt>
              <dd className="text-right tabular-nums">{money(preview.balanceSheet.totalAssets)}</dd>
              <dt className="text-base-content/60">{t('bsTotalLiabilities')}</dt>
              <dd className="text-right tabular-nums">{money(preview.balanceSheet.totalLiabilities)}</dd>
              <dt className="text-base-content/60">{t('bsRetainedEarnings')}</dt>
              <dd className="text-right tabular-nums">{money(preview.balanceSheet.retainedEarnings)}</dd>
              <dt className="text-base-content/60">{t('bsTotalEquity')}</dt>
              <dd className="text-right tabular-nums">{money(preview.balanceSheet.totalEquity)}</dd>
              <dt className="font-semibold">{t('bsTotalLiabAndEquity')}</dt>
              <dd className="text-right font-semibold tabular-nums">
                {money(preview.balanceSheet.totalLiabilitiesAndEquity)}
              </dd>
            </dl>
          ) : (
            <p className="text-sm text-base-content/50">{tc('loading')}</p>
          )}
        </div>
      </div>

      {/* ③ เครดิตภาษีถูกหัก ณ ที่จ่าย (ขาเข้า) — รายใบ 50ทวิ */}
      {preview && preview.whtCertificates.length > 0 && (
        <div className="mb-4 rounded-lg border border-base-300 p-4">
          <h2 className="mb-2 font-semibold">{t('whtCreditTitle')}</h2>
          <div className="overflow-x-auto">
            <table className="table table-sm table-zebra">
              <thead>
                <tr>
                  <th>{t('whtDocNo')}</th>
                  <th>{t('whtDocDate')}</th>
                  <th>{t('whtCustomer')}</th>
                  <th>{t('whtCertNo')}</th>
                  <th className="text-right">{t('amount')}</th>
                </tr>
              </thead>
              <tbody>
                {preview.whtCertificates.map((w) => (
                  <tr key={w.docNo} className={w.customerWhtCertNo ? '' : 'text-warning'}>
                    <td className="font-mono text-xs">{w.docNo}</td>
                    <td className="tabular-nums">{w.docDate}</td>
                    <td>{w.customerName}</td>
                    <td className="font-mono text-xs">{w.customerWhtCertNo ?? t('whtCertMissing')}</td>
                    <td className="text-right tabular-nums">{formatTHB(w.whtAmount)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <th colSpan={4} className="text-right">{tc('total')}</th>
                  <th className="text-right tabular-nums">{formatTHB(preview.whtCreditTotal)}</th>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      )}

      {/* ── C-D: รายการที่ 7 รายจ่ายในการขายและบริหาร (p5) — nonzero lines + รวม ── */}
      {preview?.expenseSchedule && (
        <div className="mb-4 rounded-lg border border-base-300 p-4">
          <h2 className="mb-2 font-semibold">{t('expenseScheduleTitle')}</h2>
          <dl className="grid grid-cols-[1fr_auto] gap-y-1 text-sm">
            {expenseLines(preview.expenseSchedule)
              .filter(([, v]) => v !== 0)
              .map(([k, v]) => (
                <div key={k} className="contents">
                  <dt className="text-base-content/60">{t(k as 'es1')}</dt>
                  <dd className="text-right tabular-nums">{money(v)}</dd>
                </div>
              ))}
            <dt className="font-semibold">{t('esTotal')}</dt>
            <dd className="text-right font-semibold tabular-nums">
              {money(preview.expenseSchedule.total)}
            </dd>
          </dl>
          {preview.disallowed && preview.disallowed.total > 0 && (
            <>
              <h3 className="mb-1 mt-3 text-sm font-semibold">{t('disallowedScheduleTitle')}</h3>
              <dl className="grid grid-cols-[1fr_auto] gap-y-1 text-sm">
                {disallowedLines(preview.disallowed)
                  .filter(([, v]) => v !== 0)
                  .map(([k, v]) => (
                    <div key={k} className="contents">
                      <dt className="text-base-content/60">{t(k as 'ds1')}</dt>
                      <dd className="text-right tabular-nums">{money(v)}</dd>
                    </div>
                  ))}
                <dt className="font-semibold">{t('dsTotal')}</dt>
                <dd className="text-right font-semibold tabular-nums">
                  {money(preview.disallowed.total)}
                </dd>
              </dl>
            </>
          )}
        </div>
      )}

      {/* ── ภ.ง.ด.50 C-D PDF (p1-p6 + p7 header) ── */}
      <div className="mb-4 rounded-lg border border-base-300 p-4">
        <h2 className="font-semibold">{t('pnd50Title')}</h2>
        <p className="mb-2 text-xs text-base-content/60">{t('pnd50Hint')}</p>
        {preview && preview.refusals.some((c) => !INFORMATIONAL_REFUSALS.includes(c)) && (
          <div className="mb-3 rounded-md bg-error/10 p-3 text-sm text-error">
            <p className="font-semibold">{t('refusalsTitle')}</p>
            <ul className="ml-4 list-disc">
              {preview.refusals
                .filter((c) => !INFORMATIONAL_REFUSALS.includes(c))
                .map((code) => {
                  // codes are "pnd50.<key>"; the i18n bundle nests them under refusal.<key>.
                  const key = code.replace(/^pnd50\./, '');
                  return <li key={code}>{t(`refusal.${key}` as 'refusal.not_renderable')}</li>;
                })}
            </ul>
          </div>
        )}
        {preview && preview.refusals.some((c) => INFORMATIONAL_REFUSALS.includes(c)) && (
          <div className="mb-3 rounded-md bg-warning/10 p-3 text-sm text-warning">
            <ul className="ml-4 list-disc">
              {preview.refusals
                .filter((c) => INFORMATIONAL_REFUSALS.includes(c))
                .map((code) => {
                  const key = code.replace(/^pnd50\./, '');
                  return <li key={code}>{t(`refusal.${key}` as 'refusal.not_renderable')}</li>;
                })}
            </ul>
          </div>
        )}
        <div className="flex flex-wrap items-center gap-4">
          <label className="label cursor-pointer gap-2">
            <input
              type="checkbox"
              className="checkbox checkbox-sm"
              checked={attestFirstFiling}
              onChange={(e) => setAttestFirstFiling(e.target.checked)}
            />
            <span className="label-text text-sm">{t('pnd50AttestFirstFiling')}</span>
          </label>
          <label className="label cursor-pointer gap-2">
            <input
              type="checkbox"
              className="checkbox checkbox-sm"
              checked={attestBlankSchedules}
              onChange={(e) => setAttestBlankSchedules(e.target.checked)}
            />
            <span className="label-text text-sm">{t('pnd50AttestBlankSchedules')}</span>
          </label>
          <label className="label cursor-pointer gap-2">
            <input
              type="checkbox"
              className="checkbox checkbox-sm"
              checked={hasRelatedParty}
              onChange={(e) => setHasRelatedParty(e.target.checked)}
            />
            <span className="label-text text-sm">{t('pnd50HasRelatedParty')}</span>
          </label>
          <button
            className="btn btn-sm btn-primary"
            disabled={
              pnd50Busy ||
              loading ||
              !attestFirstFiling ||
              !attestBlankSchedules ||
              (preview != null &&
                preview.refusals.some((c) => !INFORMATIONAL_REFUSALS.includes(c)))
            }
            onClick={() => void downloadPnd50()}
          >
            {pnd50Busy && <span className="loading loading-spinner loading-xs" />}
            {t('pnd50Download')}
          </button>
        </div>
      </div>

      {/* ── adjustments (ม.65ทวิ/65ตรี) ── */}
      <div className="rounded-lg border border-base-300 p-4">
        <h2 className="font-semibold">{t('adjustmentsTitle')}</h2>
        <p className="mb-2 text-xs text-base-content/60">{t('adjustmentsHint')}</p>
        <div className="overflow-x-auto">
          <table className="table table-sm table-zebra">
            <thead>
              <tr>
                <th>{t('legalRefCode')}</th>
                <th>{t('label')}</th>
                <th className="text-right">{t('amount')}</th>
                <th className="w-32" />
              </tr>
            </thead>
            <tbody>
              {adjustments.length === 0 && (
                <tr>
                  <td colSpan={4} className="py-4 text-center text-base-content/50">{tc('empty')}</td>
                </tr>
              )}
              {adjustments.map((a) => (
                <tr key={a.citAdjustmentId}>
                  <td className="font-mono text-xs">{a.legalRefCode}</td>
                  <td>{a.label}</td>
                  <td className={`text-right tabular-nums ${a.amount < 0 ? 'text-error' : ''}`}>
                    {formatTHB(a.amount)}
                  </td>
                  <td className="text-right">
                    <button className="btn btn-ghost btn-xs" disabled={adjBusy} onClick={() => startEdit(a)}>
                      {tc('edit')}
                    </button>
                    <button
                      className="btn btn-ghost btn-xs text-error"
                      disabled={adjBusy}
                      onClick={() => void deleteAdjustment(a.citAdjustmentId)}
                    >
                      {tc('delete')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* add / edit row */}
        <div className="mt-3 flex flex-wrap items-end gap-3">
          <label className="form-control">
            <span className="label-text text-xs">{t('legalRefCode')}</span>
            <input
              className="input input-bordered input-sm w-36"
              value={refCode}
              placeholder="ม.65ตรี(3)"
              onChange={(e) => setRefCode(e.target.value)}
            />
          </label>
          <label className="form-control grow">
            <span className="label-text text-xs">{t('label')}</span>
            <input
              className="input input-bordered input-sm w-full"
              value={label}
              onChange={(e) => setLabel(e.target.value)}
            />
          </label>
          <label className="form-control">
            <span className="label-text text-xs">{t('amount')}</span>
            <input
              type="number"
              className="input input-bordered input-sm w-40"
              value={amount}
              onChange={(e) => setAmount(e.target.value)}
            />
          </label>
          <button
            className="btn btn-sm btn-primary"
            disabled={adjBusy || !adjValid}
            onClick={() => void saveAdjustment()}
          >
            {adjBusy && <span className="loading loading-spinner loading-xs" />}
            {editId == null ? t('add') : tc('save')}
          </button>
          {editId != null && (
            <button className="btn btn-sm btn-ghost" disabled={adjBusy} onClick={resetAdjForm}>
              {tc('cancel')}
            </button>
          )}
        </div>
      </div>
    </>
  );
}
