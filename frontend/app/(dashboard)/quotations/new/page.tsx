'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { useCreateQuotation } from '@/lib/queries';

function today() { return new Date().toISOString().slice(0, 10); }
function plus30() {
  const d = new Date(); d.setDate(d.getDate() + 30);
  return d.toISOString().slice(0, 10);
}

export default function NewQuotationPage() {
  const t = useTranslations('quotation');
  const tc = useTranslations('common');
  const router = useRouter();
  const create = useCreateQuotation();

  const [customerId, setCustomerId] = useState<number | null>(null);
  const [docDate, setDocDate] = useState(today());
  const [validUntil, setValidUntil] = useState(plus30());
  const [desc, setDesc] = useState('สินค้า/บริการ');
  const [qty, setQty] = useState('1');
  const [price, setPrice] = useState('1000');

  async function submit() {
    if (!customerId) { toast.error(t('pickCustomer')); return; }
    try {
      const r = await create.mutateAsync({
        docDate, validUntilDate: validUntil, customerId,
        businessUnitId: null, currencyCode: 'THB', exchangeRate: 1,
        notes: null, internalNotes: null,
        lines: [{
          productId: null, descriptionTh: desc, quantity: Number(qty),
          uomText: 'ชิ้น', unitPrice: Number(price), discountPercent: 0,
          taxCodeId: 1, taxCode: 'VAT7', taxRate: 0.07,
        }],
      }) as { quotation_id: number };
      toast.success(tc('save'));
      router.push(`/quotations/${r.quotation_id}`);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  return (
    <>
      <PageHeader title={t('create')} />
      <div className="flex max-w-xl flex-col gap-3">
        <div>
          <span className="label-text text-xs">{t('customer')}</span>
          <CustomerSelector value={customerId}
            onChange={(id) => setCustomerId(id)} />
        </div>
        <div className="flex gap-3">
          <label className="form-control flex-1">
            <span className="label-text text-xs">{t('docDate')}</span>
            <input type="date" className="input input-bordered input-sm"
              value={docDate} onChange={(e) => setDocDate(e.target.value)} />
          </label>
          <label className="form-control flex-1">
            <span className="label-text text-xs">{t('validUntil')}</span>
            <input type="date" className="input input-bordered input-sm"
              value={validUntil} onChange={(e) => setValidUntil(e.target.value)} />
          </label>
        </div>
        <label className="form-control">
          <span className="label-text text-xs">{t('lineDesc')}</span>
          <input className="input input-bordered input-sm" value={desc}
            onChange={(e) => setDesc(e.target.value)} />
        </label>
        <div className="flex gap-3">
          <label className="form-control flex-1">
            <span className="label-text text-xs">{t('qty')}</span>
            <input type="number" className="input input-bordered input-sm"
              value={qty} onChange={(e) => setQty(e.target.value)} />
          </label>
          <label className="form-control flex-1">
            <span className="label-text text-xs">{t('unitPrice')}</span>
            <input type="number" className="input input-bordered input-sm"
              value={price} onChange={(e) => setPrice(e.target.value)} />
          </label>
        </div>
        <button className="btn btn-primary btn-sm self-start"
          disabled={create.isPending} onClick={submit}>
          {tc('save')}
        </button>
      </div>
    </>
  );
}
