'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil, Percent } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import {
  useWhtTypes, useCreateWhtType, useUpdateWhtType,
  useDeactivateWhtType, useReactivateWhtType, useChangeWhtRate,
} from '@/lib/queries';
import type { WhtTypeListItem } from '@/lib/types';
import { QueryState } from '@/components/states/QueryState';
import { PermissionGate } from '@/components/PermissionGate';

const SCOPE = 'tax.wht_type.manage';

const FORMS = ['PND3', 'PND53', 'PND1'] as const;

interface Editing {
  whtTypeId: number | null;
  code: string; nameTh: string; nameEn: string;
  incomeTypeCode: string; formType: string; rate: string; // rate as % string
}
const EMPTY: Editing = {
  whtTypeId: null, code: '', nameTh: '', nameEn: '',
  incomeTypeCode: '', formType: 'PND53', rate: '3',
};

export default function WhtTypesSettingsPage() {
  const t = useTranslations('whtType');
  const tc = useTranslations('common');
  const q = useWhtTypes(true);
  const create = useCreateWhtType();
  const update = useUpdateWhtType();
  const deactivate = useDeactivateWhtType();
  const reactivate = useReactivateWhtType();
  const changeRate = useChangeWhtRate();
  const [edit, setEdit] = useState<Editing | null>(null);
  const [rateChange, setRateChange] = useState<{ id: number; code: string } | null>(null);
  const [newRate, setNewRate] = useState('');
  const [effFrom, setEffFrom] = useState('');

  const rows = q.data ?? [];

  async function save() {
    if (!edit) return;
    try {
      if (edit.whtTypeId === null) {
        await create.mutateAsync({
          code: edit.code, nameTh: edit.nameTh, nameEn: edit.nameEn || null,
          incomeTypeCode: edit.incomeTypeCode, formType: edit.formType,
          rate: Number(edit.rate) / 100,
        });
      } else {
        await update.mutateAsync({
          id: edit.whtTypeId,
          req: {
            nameTh: edit.nameTh, nameEn: edit.nameEn || null,
            incomeTypeCode: edit.incomeTypeCode, formType: edit.formType,
          },
        });
      }
      toast.success(t('save'));
      setEdit(null);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function applyRateChange() {
    if (!rateChange) return;
    try {
      await changeRate.mutateAsync({
        id: rateChange.id, newRate: Number(newRate) / 100, effectiveFrom: effFrom,
      });
      toast.success(t('changeRate'));
      setRateChange(null); setNewRate(''); setEffFrom('');
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  return (
    <>
      <PageHeader
        title={t('title')}
        actions={
          <PermissionGate scope={SCOPE}>
            <button className="btn btn-primary btn-sm gap-1" onClick={() => setEdit({ ...EMPTY })}>
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </button>
          </PermissionGate>
        }
      />

      <QueryState query={q} isEmpty={!q.isLoading && rows.length === 0}>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('code')}</th><th>{t('nameTh')}</th><th>{t('rate')}</th>
              <th>{t('formType')}</th><th>{t('incomeTypeCode')}</th>
              <th>{t('effectiveFrom')}</th><th>{t('effectiveTo')}</th>
              <th>{t('isActive')}</th><th className="w-32" />
            </tr>
          </thead>
          <tbody>
            {rows.map((w: WhtTypeListItem) => (
              <tr key={w.whtTypeId} className="hover">
                <td className="font-mono">{w.code}</td>
                <td>{w.nameTh}</td>
                <td className="tabular-nums">{(w.rate * 100).toFixed(2)}%</td>
                <td>{w.formType}</td>
                <td>{w.incomeTypeCode}</td>
                <td className="tabular-nums">{w.effectiveFrom}</td>
                <td className="tabular-nums">{w.effectiveTo ?? <span className="badge badge-ghost badge-sm">{t('current')}</span>}</td>
                <td>{w.isActive ? '✓' : '—'}</td>
                <td className="flex gap-1">
                  <PermissionGate scope={SCOPE}>
                    <button className="btn btn-ghost btn-xs" aria-label={t('edit')}
                      onClick={() => setEdit({
                        whtTypeId: w.whtTypeId, code: w.code, nameTh: w.nameTh,
                        nameEn: w.nameEn ?? '', incomeTypeCode: w.incomeTypeCode,
                        formType: w.formType, rate: String(w.rate * 100),
                      })}>
                      <Pencil className="h-3 w-3" aria-hidden />
                    </button>
                    <button className="btn btn-ghost btn-xs" aria-label={t('changeRate')}
                      onClick={() => { setRateChange({ id: w.whtTypeId, code: w.code }); setNewRate(String(w.rate * 100)); }}>
                      <Percent className="h-3 w-3" aria-hidden />
                    </button>
                    {w.isActive ? (
                      <button className="btn btn-ghost btn-xs text-error"
                        onClick={async () => {
                          try { await deactivate.mutateAsync(w.whtTypeId); toast.success(t('deactivate')); }
                          catch { toast.error(tc('error')); }
                        }}>
                        {t('deactivate')}
                      </button>
                    ) : (
                      <button className="btn btn-ghost btn-xs text-success"
                        data-testid="row-restore"
                        onClick={async () => {
                          try { await reactivate.mutateAsync(w.whtTypeId); toast.success(tc('restore')); }
                          catch { toast.error(tc('error')); }
                        }}>
                        ↺ {tc('restore')}
                      </button>
                    )}
                  </PermissionGate>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      </QueryState>

      {edit && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="text-lg font-bold">
              {edit.whtTypeId === null ? t('create') : t('edit')}
            </h3>
            <div className="mt-4 space-y-3">
              <label className="form-control">
                <span className="label-text">{t('code')} *</span>
                <input className="input input-bordered" value={edit.code}
                  disabled={edit.whtTypeId !== null}
                  onChange={(e) => setEdit({ ...edit, code: e.target.value.toUpperCase() })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('nameTh')} *</span>
                <input className="input input-bordered" value={edit.nameTh}
                  onChange={(e) => setEdit({ ...edit, nameTh: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('nameEn')}</span>
                <input className="input input-bordered" value={edit.nameEn}
                  onChange={(e) => setEdit({ ...edit, nameEn: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('incomeTypeCode')} *</span>
                <input className="input input-bordered" value={edit.incomeTypeCode}
                  onChange={(e) => setEdit({ ...edit, incomeTypeCode: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('formType')} *</span>
                <select className="select select-bordered" value={edit.formType}
                  onChange={(e) => setEdit({ ...edit, formType: e.target.value })}>
                  {FORMS.map((f) => <option key={f} value={f}>{f}</option>)}
                </select>
              </label>
              {edit.whtTypeId === null && (
                <label className="form-control">
                  <span className="label-text">{t('rate')} (%) *</span>
                  <input type="number" step="0.01" className="input input-bordered" value={edit.rate}
                    onChange={(e) => setEdit({ ...edit, rate: e.target.value })} />
                </label>
              )}
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => setEdit(null)}>{t('cancel')}</button>
              <button className="btn btn-primary"
                disabled={edit.code.trim() === '' || edit.nameTh.trim() === '' ||
                          edit.incomeTypeCode.trim() === '' ||
                          create.isPending || update.isPending}
                onClick={save}>
                {t('save')}
              </button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label="close" onClick={() => setEdit(null)} />
        </div>
      )}

      {rateChange && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="text-lg font-bold">{t('changeRate')} — {rateChange.code}</h3>
            <p className="mt-1 text-xs text-base-content/60">{t('changeRateConfirm')}</p>
            <div className="mt-4 space-y-3">
              <label className="form-control">
                <span className="label-text">{t('newRate')} *</span>
                <input type="number" step="0.01" className="input input-bordered" value={newRate}
                  onChange={(e) => setNewRate(e.target.value)} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('effectiveFrom')} *</span>
                <input type="date" className="input input-bordered" value={effFrom}
                  onChange={(e) => setEffFrom(e.target.value)} />
              </label>
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => setRateChange(null)}>{t('cancel')}</button>
              <button className="btn btn-primary"
                disabled={newRate.trim() === '' || effFrom.trim() === '' || changeRate.isPending}
                onClick={applyRateChange}>
                {t('save')}
              </button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label="close" onClick={() => setRateChange(null)} />
        </div>
      )}
    </>
  );
}
