'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { ShieldAlert } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useCreateFixedAsset, useUpdateFixedAsset, useVendorInvoices, useGlAccounts, useMePermissions } from '@/lib/queries';
import { problemToast } from '@/lib/api';
import { bangkokToday } from '@/lib/utils';
import type { FixedAssetDetail } from '@/lib/types';

const money = (n: number) => n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Cycle D (specs/fixed-assets.md §9.13) create form, extracted for L3-12
// (specs/fix-r2-u8-fe.md) so the Draft-only edit door can reuse it — same
// `edit?` create/update split as ExpenseClaimForm.tsx. Save draft -> id -> the
// detail page (§9.14) offers a separate "Activate" action either way.
const SCOPE = 'fixedasset.manage';

export function FixedAssetForm({ edit }: { edit?: FixedAssetDetail } = {}) {
  const t = useTranslations('fixedAssets');
  const tc = useTranslations('common');
  const router = useRouter();
  const isEdit = edit != null;
  const create = useCreateFixedAsset();
  const update = useUpdateFixedAsset();
  const vendorInvoices = useVendorInvoices();
  const glAccounts = useGlAccounts();
  const perms = useMePermissions();

  const [name, setName] = useState(edit?.name ?? '');
  const [category, setCategory] = useState(edit?.category ?? '');
  const [acquireDate, setAcquireDate] = useState(edit?.acquireDate ?? bangkokToday());
  const [vendorInvoiceId, setVendorInvoiceId] = useState<number | null>(edit?.vendorInvoiceId ?? null);
  const [cost, setCost] = useState(edit?.cost ?? 0);
  const [salvageValue, setSalvageValue] = useState(edit?.salvageValue ?? 0);
  const [lifeUnit, setLifeUnit] = useState<'months' | 'years'>('months');
  const [lifeValue, setLifeValue] = useState(edit?.usefulLifeMonths ?? 0);
  const [depreciationStartDate, setDepreciationStartDate] = useState(edit?.depreciationStartDate ?? '');
  const [assetCostAccountId, setAssetCostAccountId] = useState<number | null>(edit?.assetCostAccountId ?? null);
  const [accumDepAccountId, setAccumDepAccountId] = useState<number | null>(edit?.accumDepAccountId ?? null);
  const [depExpenseAccountId, setDepExpenseAccountId] = useState<number | null>(edit?.depExpenseAccountId ?? null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!edit) return;
    setName(edit.name);
    setCategory(edit.category ?? '');
    setAcquireDate(edit.acquireDate);
    setVendorInvoiceId(edit.vendorInvoiceId);
    setCost(edit.cost);
    setSalvageValue(edit.salvageValue);
    setLifeUnit('months');
    setLifeValue(edit.usefulLifeMonths);
    setDepreciationStartDate(edit.depreciationStartDate);
    setAssetCostAccountId(edit.assetCostAccountId);
    setAccumDepAccountId(edit.accumDepAccountId);
    setDepExpenseAccountId(edit.depExpenseAccountId);
  }, [edit]);

  const usefulLifeMonths = lifeUnit === 'years' ? lifeValue * 12 : lifeValue;
  const monthlyAmountPreview = useMemo(() => {
    if (usefulLifeMonths <= 0) return 0;
    return Math.round(((cost - salvageValue) / usefulLifeMonths) * 100) / 100;
  }, [cost, salvageValue, usefulLifeMonths]);

  const canSave = name.trim() !== '' && acquireDate !== '' && cost > 0 && usefulLifeMonths > 0
    && salvageValue >= 0 && salvageValue <= cost;

  async function saveDraft() {
    setBusy(true);
    try {
      const payload = {
        name: name.trim(),
        category: category.trim() || null,
        acquireDate,
        vendorInvoiceId,
        cost,
        salvageValue,
        usefulLifeMonths,
        depreciationStartDate: depreciationStartDate || null,
        assetCostAccountId,
        accumDepAccountId,
        depExpenseAccountId,
        notes: edit?.notes ?? null,
        businessUnitId: edit?.businessUnitId ?? null,
      };
      if (isEdit && edit) {
        await update.mutateAsync({ id: edit.fixedAssetId, req: payload });
        toast.success(tc('save'));
        router.push(`/fixed-assets/${edit.fixedAssetId}`);
        return;
      }
      const res = await create.mutateAsync(payload);
      toast.success(tc('save'));
      router.push(`/fixed-assets/${res.fixed_asset_id}`);
    } catch (e) {
      problemToast(e, tc('error'));
    } finally {
      setBusy(false);
    }
  }

  const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
  if (perms.data && !canCreate) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
        <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
        <div className="font-semibold">{tc('noAccessTitle')}</div>
        <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: SCOPE })}</div>
      </div>
    );
  }

  return (
    <>
      <PageHeader title={isEdit ? tc('edit') : t('create')} />
      <div className="max-w-3xl space-y-5">
        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            <label className="form-control">
              <span className="label-text">{t('name')} *</span>
              <input className="input input-bordered" value={name}
                onChange={(e) => setName(e.target.value)} aria-label={t('name')} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('category')}</span>
              <input className="input input-bordered" value={category}
                onChange={(e) => setCategory(e.target.value)} aria-label={t('category')} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('acquireDate')} *</span>
              <input type="date" className="input input-bordered" value={acquireDate}
                onChange={(e) => setAcquireDate(e.target.value)} aria-label={t('acquireDate')} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('vendorInvoice')}</span>
              <select className="select select-bordered" value={vendorInvoiceId ?? ''}
                onChange={(e) => setVendorInvoiceId(e.target.value ? Number(e.target.value) : null)}
                aria-label={t('vendorInvoice')}>
                <option value="">{t('vendorInvoiceNone')}</option>
                {(vendorInvoices.data ?? []).map((v) => (
                  <option key={v.vendorInvoiceId} value={v.vendorInvoiceId}>
                    {v.docNo ?? `#${v.vendorInvoiceId}`} — {v.vendorName} — {money(v.totalAmount)}
                  </option>
                ))}
              </select>
            </label>
            <label className="form-control">
              <span className="label-text">{t('cost')} *</span>
              <input type="number" className="input input-bordered" value={cost}
                onChange={(e) => setCost(Number(e.target.value) || 0)} aria-label={t('cost')} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('salvageValue')}</span>
              <input type="number" className="input input-bordered" value={salvageValue}
                onChange={(e) => setSalvageValue(Number(e.target.value) || 0)} aria-label={t('salvageValue')} />
            </label>
            <label className="form-control">
              <span className="label-text">{t('usefulLife')} *</span>
              <div className="flex gap-2">
                <input type="number" className="input input-bordered flex-1" value={lifeValue}
                  onChange={(e) => setLifeValue(Number(e.target.value) || 0)} aria-label={t('usefulLife')} />
                <select className="select select-bordered" value={lifeUnit}
                  onChange={(e) => setLifeUnit(e.target.value as 'months' | 'years')} aria-label={t('usefulLife')}>
                  <option value="months">{t('usefulLifeMonths')}</option>
                  <option value="years">{t('usefulLifeYears')}</option>
                </select>
              </div>
            </label>
            <label className="form-control">
              <span className="label-text">{t('depreciationStartDate')}</span>
              <input type="date" className="input input-bordered" value={depreciationStartDate}
                placeholder={acquireDate}
                onChange={(e) => setDepreciationStartDate(e.target.value)} aria-label={t('depreciationStartDate')} />
            </label>
          </div>

          <div className="mt-4 rounded-lg border border-base-300 bg-base-200/50 p-3 text-sm">
            <span className="text-base-content/60">{t('monthlyAmountPreview')}:</span>{' '}
            <span className="font-semibold">{money(monthlyAmountPreview)}</span>
          </div>
        </section>

        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <h2 className="mb-3 font-semibold">{t('accountsSection')}</h2>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <label className="form-control">
              <span className="label-text">{t('assetCostAccount')}</span>
              <select className="select select-bordered" value={assetCostAccountId ?? ''}
                onChange={(e) => setAssetCostAccountId(e.target.value ? Number(e.target.value) : null)}
                aria-label={t('assetCostAccount')}>
                <option value="">{t('accountDefault')}</option>
                {(glAccounts.data ?? []).map((a) => (
                  <option key={a.accountId} value={a.accountId}>{a.accountCode} — {a.accountNameTh}</option>
                ))}
              </select>
            </label>
            <label className="form-control">
              <span className="label-text">{t('accumDepAccount')}</span>
              <select className="select select-bordered" value={accumDepAccountId ?? ''}
                onChange={(e) => setAccumDepAccountId(e.target.value ? Number(e.target.value) : null)}
                aria-label={t('accumDepAccount')}>
                <option value="">{t('accountDefault')}</option>
                {(glAccounts.data ?? []).map((a) => (
                  <option key={a.accountId} value={a.accountId}>{a.accountCode} — {a.accountNameTh}</option>
                ))}
              </select>
            </label>
            <label className="form-control">
              <span className="label-text">{t('depExpenseAccount')}</span>
              <select className="select select-bordered" value={depExpenseAccountId ?? ''}
                onChange={(e) => setDepExpenseAccountId(e.target.value ? Number(e.target.value) : null)}
                aria-label={t('depExpenseAccount')}>
                <option value="">{t('accountDefault')}</option>
                {(glAccounts.data ?? []).map((a) => (
                  <option key={a.accountId} value={a.accountId}>{a.accountCode} — {a.accountNameTh}</option>
                ))}
              </select>
            </label>
          </div>
        </section>

        <div className="flex justify-end gap-2">
          <button type="button" className="btn btn-ghost"
            onClick={() => router.push(isEdit && edit ? `/fixed-assets/${edit.fixedAssetId}` : '/fixed-assets')}
            disabled={busy}>
            {tc('cancel')}
          </button>
          <button type="button" className="btn btn-primary" disabled={!canSave || busy || create.isPending || update.isPending} onClick={saveDraft}>
            {tc('save')}
          </button>
        </div>
      </div>
    </>
  );
}
