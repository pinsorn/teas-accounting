'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { useCreateProduct } from '@/lib/queries';
import type { ProductTypeStr, ProductPurpose } from '@/lib/types';
import { WhtTypeSelect } from '@/components/ui/WhtTypeSelect';
import type { ProductPick } from '@/components/forms/ProductPicker';

// Sprint (line product/service typing) — inline "create new product/service"
// from a line table. Product-master driven: pick type → goods/service; a SERVICE
// type may carry a default WHT category (the withholding rate differs per service
// category). Price is NOT collected here — price/discount stay per-line on the
// document (sprint plan). On save it POSTs to the product master and hands the
// new product back to the line via onCreated.
const TYPES: ProductTypeStr[] = ['GOOD', 'SERVICE', 'EXEMPT_GOOD', 'EXEMPT_SERVICE'];
const isService = (t: ProductTypeStr) => t === 'SERVICE' || t === 'EXEMPT_SERVICE';

export function ProductQuickCreateModal({
  open,
  initialNameTh,
  onClose,
  onCreated,
  purpose,
  businessUnitId,
}: {
  open: boolean;
  initialNameTh: string;
  onClose: () => void;
  onCreated: (p: ProductPick) => void;
  // cont.81 — a product created from a purchase picker must be born purchasable
  // (+ scoped to the doc's BU), or it won't appear in the picker that made it.
  purpose?: ProductPurpose;
  businessUnitId?: number | null;
}) {
  const t = useTranslations('product');
  const tc = useTranslations('common');
  const create = useCreateProduct();
  const [code, setCode] = useState('');
  const [nameTh, setNameTh] = useState(initialNameTh);
  const [type, setType] = useState<ProductTypeStr>('SERVICE');
  const [whtTypeId, setWhtTypeId] = useState<number | null>(null);

  // Reset local state every time the modal (re)opens with a fresh description.
  const [seededFor, setSeededFor] = useState<string | null>(null);
  if (open && seededFor !== initialNameTh) {
    setSeededFor(initialNameTh);
    setNameTh(initialNameTh);
    setCode('');
    setType('SERVICE');
    setWhtTypeId(null);
  }
  if (!open) {
    if (seededFor !== null) setSeededFor(null);
    return null;
  }

  const whtAllowed = isService(type);

  async function save() {
    if (!code.trim() || !nameTh.trim()) {
      toast.error(t('quickCreateMissing'));
      return;
    }
    try {
      const res = await create.mutateAsync({
        productCode: code.trim(),
        nameTh: nameTh.trim(),
        nameEn: null,
        productType: type,
        defaultUomText: null,
        defaultUnitPrice: null,
        defaultOutputTaxCodeId: null,
        defaultInputTaxCodeId: null,
        // WHT only valid on service types (BE validator rejects otherwise).
        defaultWhtTypeId: whtAllowed ? whtTypeId : null,
        descriptionTh: null,
        notes: null,
        // cont.81 — born usable on the side that created it (+ scoped to the BU).
        // A purchase-picker product is purchasable; everything else is saleable.
        isSaleable: purpose !== 'purchase',
        isPurchasable: purpose === 'purchase',
        businessUnitId: businessUnitId ?? null,
      });
      onCreated({
        productId: res.product_id,
        productCode: code.trim(),
        nameTh: nameTh.trim(),
        productType: type,
        defaultUnitPrice: null,
      });
      toast.success(tc('save'));
      onClose();
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  return (
    <div className="modal modal-open">
      <div className="modal-box">
        <h3 className="mb-3 text-lg font-bold">{t('quickCreateTitle')}</h3>
        <div className="flex flex-col gap-3">
          <label className="form-control">
            <span className="label-text text-xs">{t('code')}</span>
            <input
              className="input input-bordered input-sm"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="SKU"
              autoFocus
            />
          </label>
          <label className="form-control">
            <span className="label-text text-xs">{t('nameTh')}</span>
            <input
              className="input input-bordered input-sm"
              value={nameTh}
              onChange={(e) => setNameTh(e.target.value)}
            />
          </label>
          <label className="form-control">
            <span className="label-text text-xs">{t('type')}</span>
            <select
              className="select select-bordered select-sm"
              value={type}
              onChange={(e) => {
                const next = e.target.value as ProductTypeStr;
                setType(next);
                if (!isService(next)) setWhtTypeId(null);
              }}
            >
              {TYPES.map((ty) => (
                <option key={ty} value={ty}>
                  {t(`typeLabel.${ty}`)}
                </option>
              ))}
            </select>
          </label>
          {whtAllowed && (
            <label className="form-control">
              <span className="label-text text-xs">{t('wht')}</span>
              <WhtTypeSelect
                value={whtTypeId}
                onChange={(id) => setWhtTypeId(id)}
                ariaLabel={t('wht')}
                placeholder={t('whtNone')}
              />
              <span className="mt-1 text-xs text-base-content/50">{t('whtHint')}</span>
            </label>
          )}
        </div>
        <div className="modal-action">
          <button className="btn btn-ghost btn-sm" onClick={onClose}>
            {tc('cancel')}
          </button>
          <button
            className="btn btn-primary btn-sm"
            disabled={create.isPending}
            onClick={save}
          >
            {t('createAndSelect')}
          </button>
        </div>
      </div>
    </div>
  );
}
