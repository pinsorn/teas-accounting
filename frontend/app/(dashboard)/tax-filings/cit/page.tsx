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

const money = (v: number | null | undefined) => (v == null ? '—' : formatTHB(v));

export default function CitYearDataPage() {
  const t = useTranslations('cit');
  const tc = useTranslations('common');
  const [year, setYear] = useState(new Date().getFullYear());
  const [profile, setProfile] = useState<CitProfile | null>(null);
  const [summary, setSummary] = useState<CitYearSummary | null>(null);
  const [adjustments, setAdjustments] = useState<CitAdjustment[]>([]);
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
      // adjustments feed the computed figure — refresh the profile totals too
      setProfile(await apiGet<CitProfile>(`tax-filings/cit/profile?year=${year}`));
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

      {/* ── ภ.ง.ด.50 v1 PDF (p1 header + p2 รายการที่ 1; รายการ 2–9 ว่าง) ── */}
      <div className="mb-4 rounded-lg border border-base-300 p-4">
        <h2 className="font-semibold">{t('pnd50Title')}</h2>
        <p className="mb-2 text-xs text-base-content/60">{t('pnd50Hint')}</p>
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
            disabled={pnd50Busy || loading || !attestFirstFiling || !attestBlankSchedules}
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
