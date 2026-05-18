'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import {
  useProducts, useCreateProduct, useUpdateProduct, useDeactivateProduct,
} from '@/lib/queries';
import type { ProductTypeStr } from '@/lib/types';

interface Editing {
  productId: number | null;
  productCode: string; nameTh: string; nameEn: string;
  productType: ProductTypeStr; defaultUomText: string;
  defaultUnitPrice: string; isActive: boolean;
}
const EMPTY: Editing = {
  productId: null, productCode: '', nameTh: '', nameEn: '',
  productType: 'GOOD', defaultUomText: '', defaultUnitPrice: '', isActive: true,
};
const TYPES: ProductTypeStr[] = ['GOOD', 'SERVICE', 'EXEMPT_GOOD', 'EXEMPT_SERVICE'];

export default function ProductsSettingsPage() {
  const t = useTranslations('product');
  const tc = useTranslations('common');
  const q = useProducts(true);
  const create = useCreateProduct();
  const update = useUpdateProduct();
  const deactivate = useDeactivateProduct();
  const [edit, setEdit] = useState<Editing | null>(null);
  const rows = q.data ?? [];

  async function save() {
    if (!edit) return;
    const price = edit.defaultUnitPrice ? Number(edit.defaultUnitPrice) : null;
    try {
      if (edit.productId === null) {
        await create.mutateAsync({
          productCode: edit.productCode, nameTh: edit.nameTh,
          nameEn: edit.nameEn || null, productType: edit.productType,
          defaultUomText: edit.defaultUomText || null, defaultUnitPrice: price,
          defaultOutputTaxCodeId: null, defaultInputTaxCodeId: null,
          defaultWhtTypeId: null, descriptionTh: null, notes: null,
        });
      } else {
        await update.mutateAsync({
          id: edit.productId,
          req: {
            nameTh: edit.nameTh, nameEn: edit.nameEn || null,
            productType: edit.productType,
            defaultUomText: edit.defaultUomText || null, defaultUnitPrice: price,
            defaultOutputTaxCodeId: null, defaultInputTaxCodeId: null,
            defaultWhtTypeId: null, descriptionTh: null, notes: null,
            isActive: edit.isActive,
          },
        });
      }
      toast.success(tc('save'));
      setEdit(null);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  return (
    <>
      <PageHeader
        title={t('settingsTitle')}
        actions={
          <button className="btn btn-primary btn-sm gap-1"
            onClick={() => setEdit({ ...EMPTY })}>
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </button>
        }
      />

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('code')}</th><th>{t('nameTh')}</th><th>{t('type')}</th>
              <th className="text-right">{t('unitPrice')}</th>
              <th>{tc('status')}</th><th />
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {rows.length === 0 && !q.isLoading && (
              <tr><td colSpan={6} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {rows.map((p) => (
              <tr key={p.productId} className={p.isActive ? '' : 'opacity-50'}>
                <td className="font-mono">{p.productCode}</td>
                <td>{p.nameTh}</td>
                <td><span className="badge badge-ghost">{p.productType}</span></td>
                <td className="text-right tabular-nums">
                  {p.defaultUnitPrice == null ? '—' : p.defaultUnitPrice.toLocaleString()}
                </td>
                <td>{p.isActive ? tc('active') : tc('inactive')}</td>
                <td className="text-right">
                  <button className="btn btn-ghost btn-xs gap-1" onClick={() => setEdit({
                    productId: p.productId, productCode: p.productCode,
                    nameTh: p.nameTh, nameEn: p.nameEn ?? '',
                    productType: p.productType,
                    defaultUomText: '', defaultUnitPrice: String(p.defaultUnitPrice ?? ''),
                    isActive: p.isActive,
                  })}>
                    <Pencil className="h-3 w-3" aria-hidden /> {tc('edit')}
                  </button>
                  {p.isActive && (
                    <button className="btn btn-ghost btn-xs text-error"
                      onClick={async () => {
                        if (!window.confirm(t('deactivateConfirm'))) return;
                        try { await deactivate.mutateAsync(p.productId); toast.success(tc('save')); }
                        catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
                      }}>
                      {tc('deactivate')}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {edit && (
        <div className="modal modal-open">
          <div className="modal-box">
            <h3 className="mb-3 text-lg font-bold">
              {edit.productId === null ? t('create') : t('edit')}
            </h3>
            <div className="flex flex-col gap-3">
              {edit.productId === null && (
                <label className="form-control">
                  <span className="label-text text-xs">{t('code')}</span>
                  <input className="input input-bordered input-sm" value={edit.productCode}
                    onChange={(e) => setEdit({ ...edit, productCode: e.target.value })} />
                </label>
              )}
              <label className="form-control">
                <span className="label-text text-xs">{t('nameTh')}</span>
                <input className="input input-bordered input-sm" value={edit.nameTh}
                  onChange={(e) => setEdit({ ...edit, nameTh: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text text-xs">{t('nameEn')}</span>
                <input className="input input-bordered input-sm" value={edit.nameEn}
                  onChange={(e) => setEdit({ ...edit, nameEn: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text text-xs">{t('type')}</span>
                <select className="select select-bordered select-sm" value={edit.productType}
                  onChange={(e) => setEdit({ ...edit, productType: e.target.value as ProductTypeStr })}>
                  {TYPES.map((ty) => <option key={ty} value={ty}>{ty}</option>)}
                </select>
              </label>
              <label className="form-control">
                <span className="label-text text-xs">{t('uom')}</span>
                <input className="input input-bordered input-sm" value={edit.defaultUomText}
                  onChange={(e) => setEdit({ ...edit, defaultUomText: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text text-xs">{t('unitPrice')}</span>
                <input type="number" className="input input-bordered input-sm"
                  value={edit.defaultUnitPrice}
                  onChange={(e) => setEdit({ ...edit, defaultUnitPrice: e.target.value })} />
              </label>
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setEdit(null)}>
                {tc('cancel')}
              </button>
              <button className="btn btn-primary btn-sm"
                disabled={create.isPending || update.isPending} onClick={save}>
                {tc('save')}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
