'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import {
  useProducts, useCreateProduct, useUpdateProduct, useDeactivateProduct,
  useBusinessUnits,
} from '@/lib/queries';
import { apiGet } from '@/lib/api';
import type { ProductTypeStr, ProductDetail } from '@/lib/types';
import { useConfirm } from '@/hooks/useConfirm';
import { PermissionGate } from '@/components/PermissionGate';
import { WhtTypeSelect } from '@/components/ui/WhtTypeSelect';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';

const SCOPE = 'master.product.manage';

// WHT is a service-only concept (BE validator rejects a default WHT type on
// GOOD / EXEMPT_GOOD). The product master is the single source of a line's
// goods/service typing + service WHT category (sprint plan: product-master driven).
const isService = (t: ProductTypeStr) => t === 'SERVICE' || t === 'EXEMPT_SERVICE';

interface Editing {
  productId: number | null;
  productCode: string; nameTh: string; nameEn: string;
  productType: ProductTypeStr; defaultUomText: string;
  defaultUnitPrice: string; defaultWhtTypeId: number | null; isActive: boolean;
  // cont.81 — purchase/sale split + BU scope.
  isSaleable: boolean; isPurchasable: boolean; businessUnitId: number | null;
}
const EMPTY: Editing = {
  productId: null, productCode: '', nameTh: '', nameEn: '',
  productType: 'GOOD', defaultUomText: '', defaultUnitPrice: '',
  defaultWhtTypeId: null, isActive: true,
  isSaleable: true, isPurchasable: false, businessUnitId: null,
};
const TYPES: ProductTypeStr[] = ['GOOD', 'SERVICE', 'EXEMPT_GOOD', 'EXEMPT_SERVICE'];

export default function ProductsSettingsPage() {
  const t = useTranslations('product');
  const tc = useTranslations('common');
  // cont.81 follow-up — full filter set: search (sku/name), type, usage, BU, status.
  const [search, setSearch] = useState('');
  const [productType, setProductType] = useState<'' | ProductTypeStr>('');
  const [purpose, setPurpose] = useState<'' | 'sale' | 'purchase'>('');
  const [buFilter, setBuFilter] = useState<number | null>(null);
  const [statusFilter, setStatusFilter] = useState<'' | 'active' | 'inactive'>('');
  const isActiveFilter =
    statusFilter === 'active' ? true : statusFilter === 'inactive' ? false : undefined;
  const q = useProducts(
    true, search || undefined, purpose || undefined,
    productType || undefined, isActiveFilter, buFilter);
  const create = useCreateProduct();
  const update = useUpdateProduct();
  const deactivate = useDeactivateProduct();
  const confirm = useConfirm();
  const [edit, setEdit] = useState<Editing | null>(null);
  const rows = q.data ?? [];
  // BU id → "CODE — name" for the list badge (list row carries only the id).
  const buList = useBusinessUnits().data ?? [];
  const buName = (id: number | null) =>
    id == null ? null : (() => {
      const b = buList.find((u) => u.businessUnitId === id);
      return b ? `${b.code} — ${b.nameTh}` : `#${id}`;
    })();

  // Open the edit modal with the FULL product (the list row omits uom / nameEn /
  // WHT — building from it would silently wipe those on save).
  async function openEdit(productId: number) {
    try {
      const d = await apiGet<ProductDetail>(`products/${productId}`);
      setEdit({
        productId: d.productId, productCode: d.productCode,
        nameTh: d.nameTh, nameEn: d.nameEn ?? '',
        productType: d.productType, defaultUomText: d.defaultUomText ?? '',
        defaultUnitPrice: String(d.defaultUnitPrice ?? ''),
        defaultWhtTypeId: d.defaultWhtTypeId ?? null, isActive: d.isActive,
        isSaleable: d.isSaleable, isPurchasable: d.isPurchasable,
        businessUnitId: d.businessUnitId ?? null,
      });
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function save() {
    if (!edit) return;
    const price = edit.defaultUnitPrice ? Number(edit.defaultUnitPrice) : null;
    const whtId = isService(edit.productType) ? edit.defaultWhtTypeId : null;
    try {
      if (edit.productId === null) {
        await create.mutateAsync({
          productCode: edit.productCode, nameTh: edit.nameTh,
          nameEn: edit.nameEn || null, productType: edit.productType,
          defaultUomText: edit.defaultUomText || null, defaultUnitPrice: price,
          defaultOutputTaxCodeId: null, defaultInputTaxCodeId: null,
          defaultWhtTypeId: whtId, descriptionTh: null, notes: null,
          isSaleable: edit.isSaleable, isPurchasable: edit.isPurchasable,
          businessUnitId: edit.businessUnitId,
        });
      } else {
        await update.mutateAsync({
          id: edit.productId,
          req: {
            nameTh: edit.nameTh, nameEn: edit.nameEn || null,
            productType: edit.productType,
            defaultUomText: edit.defaultUomText || null, defaultUnitPrice: price,
            defaultOutputTaxCodeId: null, defaultInputTaxCodeId: null,
            defaultWhtTypeId: whtId, descriptionTh: null, notes: null,
            isActive: edit.isActive,
            isSaleable: edit.isSaleable, isPurchasable: edit.isPurchasable,
            businessUnitId: edit.businessUnitId,
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
          <PermissionGate scope={SCOPE}>
            <button className="btn btn-primary btn-sm gap-1"
              onClick={() => setEdit({ ...EMPTY })}>
              <Plus className="h-4 w-4" aria-hidden /> {t('create')}
            </button>
          </PermissionGate>
        }
      />

      {/* cont.81 follow-up — multi-filter bar: ชื่อ/SKU · ประเภท · การใช้งาน · BU · สถานะ. */}
      <div className="mb-4 grid grid-cols-1 items-end gap-3 rounded-card border border-ink-100 bg-base-100 p-4 shadow-warm-sm md:grid-cols-2 lg:grid-cols-5">
        <label className="form-control">
          <span className="label-text text-ink-600">{t('searchLabel')}</span>
          <input
            className="input input-bordered"
            value={search}
            placeholder={t('searchPlaceholder')}
            aria-label={t('searchLabel')}
            data-testid="product-search"
            onChange={(e) => setSearch(e.target.value)}
          />
        </label>
        <label className="form-control">
          <span className="label-text text-ink-600">{t('type')}</span>
          <select
            className="select select-bordered"
            value={productType}
            aria-label={t('type')}
            data-testid="product-type-filter"
            onChange={(e) => setProductType(e.target.value as '' | ProductTypeStr)}
          >
            <option value="">{tc('all')}</option>
            {TYPES.map((ty) => <option key={ty} value={ty}>{t(`typeLabel.${ty}`)}</option>)}
          </select>
        </label>
        <label className="form-control">
          <span className="label-text text-ink-600">{t('usage')}</span>
          <select
            className="select select-bordered"
            value={purpose}
            aria-label={t('usage')}
            data-testid="product-usage-filter"
            onChange={(e) => setPurpose(e.target.value as '' | 'sale' | 'purchase')}
          >
            <option value="">{tc('all')}</option>
            <option value="sale">{t('saleable')}</option>
            <option value="purchase">{t('purchasable')}</option>
          </select>
        </label>
        <div data-testid="product-bu-filter">
          <BusinessUnitSelector value={buFilter} onChange={setBuFilter} />
        </div>
        <label className="form-control">
          <span className="label-text text-ink-600">{tc('status')}</span>
          <select
            className="select select-bordered"
            value={statusFilter}
            aria-label={tc('status')}
            data-testid="product-status-filter"
            onChange={(e) => setStatusFilter(e.target.value as '' | 'active' | 'inactive')}
          >
            <option value="">{tc('all')}</option>
            <option value="active">{tc('active')}</option>
            <option value="inactive">{tc('inactive')}</option>
          </select>
        </label>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{t('code')}</th><th>{t('nameTh')}</th><th>{t('type')}</th>
              <th>{t('usage')}</th><th>{t('businessUnit')}</th>
              <th className="text-right">{t('unitPrice')}</th>
              <th>{tc('status')}</th><th />
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={8} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {rows.length === 0 && !q.isLoading && (
              <tr><td colSpan={8} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {rows.map((p) => (
              <tr key={p.productId} className={p.isActive ? '' : 'opacity-50'}>
                <td className="font-mono">{p.productCode}</td>
                <td>{p.nameTh}</td>
                <td><span className="badge badge-ghost">{p.productType}</span></td>
                <td>
                  <div className="flex flex-wrap gap-1">
                    {p.isSaleable && <span className="badge badge-sm bg-sky-100 text-sky-700">{t('saleable')}</span>}
                    {p.isPurchasable && <span className="badge badge-sm bg-emerald-100 text-emerald-700">{t('purchasable')}</span>}
                  </div>
                </td>
                <td className="text-sm text-base-content/70">{buName(p.businessUnitId) ?? '—'}</td>
                <td className="text-right tabular-nums">
                  {p.defaultUnitPrice == null ? '—' : p.defaultUnitPrice.toLocaleString()}
                </td>
                <td>{p.isActive ? tc('active') : tc('inactive')}</td>
                <td className="text-right">
                  <PermissionGate scope={SCOPE}>
                    <button className="btn btn-ghost btn-xs gap-1" onClick={() => openEdit(p.productId)}>
                      <Pencil className="h-3 w-3" aria-hidden /> {tc('edit')}
                    </button>
                    {p.isActive ? (
                      <button className="btn btn-ghost btn-xs text-error"
                        onClick={async () => {
                          if (!(await confirm({ description: t('deactivateConfirm'), variant: 'destructive' }))) return;
                          try { await deactivate.mutateAsync(p.productId); toast.success(tc('save')); }
                          catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
                        }}>
                        {tc('deactivate')}
                      </button>
                    ) : (
                      <button className="btn btn-ghost btn-xs text-success"
                        data-testid="row-restore"
                        onClick={async () => {
                          try {
                            // Fetch full detail so reactivation preserves uom / WHT
                            // category instead of nulling them out.
                            const d = await apiGet<ProductDetail>(`products/${p.productId}`);
                            await update.mutateAsync({
                              id: p.productId,
                              req: {
                                nameTh: d.nameTh, nameEn: d.nameEn ?? null,
                                productType: d.productType,
                                defaultUomText: d.defaultUomText ?? null,
                                defaultUnitPrice: d.defaultUnitPrice ?? null,
                                defaultOutputTaxCodeId: d.defaultOutputTaxCodeId ?? null,
                                defaultInputTaxCodeId: d.defaultInputTaxCodeId ?? null,
                                defaultWhtTypeId: d.defaultWhtTypeId ?? null,
                                descriptionTh: d.descriptionTh ?? null, notes: d.notes ?? null,
                                isActive: true,
                                isSaleable: d.isSaleable, isPurchasable: d.isPurchasable,
                                businessUnitId: d.businessUnitId ?? null,
                              },
                            });
                            toast.success(tc('restore'));
                          } catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
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
                  onChange={(e) => {
                    const next = e.target.value as ProductTypeStr;
                    setEdit({
                      ...edit, productType: next,
                      defaultWhtTypeId: isService(next) ? edit.defaultWhtTypeId : null,
                    });
                  }}>
                  {TYPES.map((ty) => <option key={ty} value={ty}>{t(`typeLabel.${ty}`)}</option>)}
                </select>
              </label>
              {isService(edit.productType) && (
                <label className="form-control">
                  <span className="label-text text-xs">{t('wht')}</span>
                  <WhtTypeSelect
                    value={edit.defaultWhtTypeId}
                    onChange={(id) => setEdit({ ...edit, defaultWhtTypeId: id })}
                    ariaLabel={t('wht')}
                    placeholder={t('whtNone')}
                  />
                  <span className="mt-1 text-xs text-base-content/50">{t('whtHint')}</span>
                </label>
              )}
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

              {/* cont.81 — purchase/sale split: a product may be sold, purchased, or
                  both. At least one must be on (Save disabled otherwise). */}
              <div className="form-control">
                <span className="label-text text-xs">{t('usage')}</span>
                <div className="mt-1 flex gap-4">
                  <label className="flex cursor-pointer items-center gap-2 text-sm">
                    <input type="checkbox" className="checkbox checkbox-sm"
                      checked={edit.isSaleable}
                      onChange={(e) => setEdit({ ...edit, isSaleable: e.target.checked })} />
                    {t('saleable')}
                  </label>
                  <label className="flex cursor-pointer items-center gap-2 text-sm">
                    <input type="checkbox" className="checkbox checkbox-sm"
                      checked={edit.isPurchasable}
                      onChange={(e) => setEdit({ ...edit, isPurchasable: e.target.checked })} />
                    {t('purchasable')}
                  </label>
                </div>
                {!edit.isSaleable && !edit.isPurchasable && (
                  <span className="mt-1 text-xs text-error">{t('usageRequired')}</span>
                )}
              </div>

              {/* cont.81 — optional owning Business Unit (ว่าง = ใช้ได้ทุกหน่วย). */}
              <BusinessUnitSelector
                value={edit.businessUnitId}
                onChange={(id) => setEdit({ ...edit, businessUnitId: id })}
              />
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setEdit(null)}>
                {tc('cancel')}
              </button>
              <button className="btn btn-primary btn-sm"
                disabled={create.isPending || update.isPending
                  || (!edit.isSaleable && !edit.isPurchasable)} onClick={save}>
                {tc('save')}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
