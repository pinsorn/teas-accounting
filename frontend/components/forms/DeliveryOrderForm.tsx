'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Plus, Trash2 } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { AmountInput } from '@/components/ui/AmountInput';
import {
  useCreateDeliveryOrderDraft,
  useDeliveryOrderAction,
  useCompanyBuSetting,
  useCompanyProfile,
} from '@/lib/queries';
import { bangkokToday } from '@/lib/utils';
import { scrollToFirstError } from '@/lib/forms';
import { PaperDocument } from '@/components/paper/PaperDocument';
import { PAPER_DOC, companyToSeller } from '@/lib/paper-doc-config';

interface DoLine {
  descriptionTh: string;
  quantity: number;
  uomText: string;
}
const EMPTY: DoLine = { descriptionTh: '', quantity: 1, uomText: 'หน่วย' };

// Sprint 13e P4 — Delivery Order create form (replaces the P1 routing stub).
// Non-fiscal: no price / VAT (spec). Ship-to + recipient are folded into Notes
// (the DTO/entity carry no dedicated fields — adding them would need a breaking
// migration; deferred per the no-unverifiable-migration policy).
export function DeliveryOrderForm() {
  const router = useRouter();
  const t = useTranslations('deliveryOrder');
  const tc = useTranslations('common');
  const tt = useTranslations('toast');
  const create = useCreateDeliveryOrderDraft();
  const action = useDeliveryOrderAction();
  const company = useCompanyProfile();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;

  const [customerId, setCustomerId] = useState<number | null>(null);
  const [docDate, setDocDate] = useState(bangkokToday());
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);
  const [shipTo, setShipTo] = useState('');
  const [recipient, setRecipient] = useState('');
  const [notes, setNotes] = useState('');
  const [lines, setLines] = useState<DoLine[]>([{ ...EMPTY }]);
  // Sprint 13i B4 — explicit field-error flags (this form is not RHF-driven).
  const [custError, setCustError] = useState(false);
  const [buError, setBuError] = useState(false);
  const [lineError, setLineError] = useState(false);

  const setLine = (i: number, patch: Partial<DoLine>) =>
    setLines(lines.map((l, idx) => (idx === i ? { ...l, ...patch } : l)));

  function composedNotes(): string | null {
    const parts: string[] = [];
    if (shipTo.trim()) parts.push(`[${t('shipTo')}] ${shipTo.trim()}`);
    if (recipient.trim()) parts.push(`[${t('recipient')}] ${recipient.trim()}`);
    if (notes.trim()) parts.push(notes.trim());
    return parts.length ? parts.join('\n') : null;
  }

  async function createDo(): Promise<number | null> {
    const noCust = !customerId;
    const noBu = buRequired && businessUnitId === null;
    const badLines = lines.some((l) => !l.descriptionTh.trim() || l.quantity <= 0);
    setCustError(noCust);
    setBuError(noBu);
    setLineError(badLines);
    if (noCust || noBu || badLines) {
      toast.error(tt('validationFailed'));
      requestAnimationFrame(scrollToFirstError);
      return null;
    }
    try {
      const res = (await create.mutateAsync({
        docDate,
        customerId,
        businessUnitId,
        isCombinedWithTi: false,
        notes: composedNotes(),
        fromSalesOrderId: null,
        lines: lines.map((l) => ({
          salesOrderLineId: null,
          productId: null,
          descriptionTh: l.descriptionTh,
          quantity: l.quantity,
          uomText: l.uomText.trim() || 'หน่วย',
          unitPrice: 0,
          discountPercent: 0,
          taxCodeId: 1,
          taxCode: 'VAT0',
          taxRate: 0,
        })),
      })) as { delivery_order_id: number };
      return res.delivery_order_id;
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
      return null;
    }
  }

  const cfg = PAPER_DOC['delivery-order'];

  return (
    <>
      <PageHeader title={t('create')} subtitle={docDate} />
      <div className="create-grid">
      <div className="space-y-6">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <div>
            <CustomerSelector
              value={customerId}
              onChange={(id) => { setCustomerId(id); if (id) setCustError(false); }}
            />
            {custError && (
              <span className="text-error text-sm" data-field-error="true">
                {t('pickCustomer')}
              </span>
            )}
          </div>
          <BusinessUnitSelector
            value={businessUnitId}
            onChange={(id) => { setBusinessUnitId(id); if (id) setBuError(false); }}
            required={buRequired}
            error={buError}
          />
          <label className="form-control">
            <span className="label-text">{t('deliveryDate')} *</span>
            <input
              type="date"
              className="input input-bordered"
              value={docDate}
              onChange={(e) => setDocDate(e.target.value)}
              aria-label={t('deliveryDate')}
            />
          </label>
        </div>

        <div>
          <div className="mb-2 flex items-center justify-between">
            <h2 className="font-semibold">{t('lines')}</h2>
            <button
              type="button"
              className="btn btn-ghost btn-sm gap-1"
              onClick={() => setLines([...lines, { ...EMPTY }])}
            >
              <Plus className="h-4 w-4" aria-hidden /> {t('addLine')}
            </button>
          </div>
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table">
              <thead>
                <tr>
                  <th>{t('desc')}</th>
                  <th className="w-28 text-right">{t('qty')}</th>
                  <th className="w-28">{t('uom')}</th>
                  <th className="w-10" />
                </tr>
              </thead>
              <tbody>
                {lines.map((l, i) => (
                  <tr key={i}>
                    <td>
                      <input
                        className={`input input-sm input-bordered w-full${
                          lineError && !l.descriptionTh.trim() ? ' input-error' : ''
                        }`}
                        value={l.descriptionTh}
                        onChange={(e) => { setLine(i, { descriptionTh: e.target.value }); setLineError(false); }}
                        aria-label={`${t('desc')} ${i + 1}`}
                        aria-invalid={(lineError && !l.descriptionTh.trim()) || undefined}
                      />
                    </td>
                    <td>
                      <AmountInput
                        value={l.quantity}
                        step={1}
                        onValueChange={(n) => setLine(i, { quantity: n })}
                        aria-label={`${t('qty')} ${i + 1}`}
                      />
                    </td>
                    <td>
                      <input
                        className="input input-sm input-bordered w-full"
                        value={l.uomText}
                        onChange={(e) => setLine(i, { uomText: e.target.value })}
                        aria-label={`${t('uom')} ${i + 1}`}
                      />
                    </td>
                    <td>
                      <button
                        type="button"
                        className="btn btn-ghost btn-xs text-error"
                        disabled={lines.length === 1}
                        onClick={() => setLines(lines.filter((_, idx) => idx !== i))}
                        aria-label={`remove line ${i + 1}`}
                      >
                        <Trash2 className="h-4 w-4" aria-hidden />
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {lineError && (
            <span className="text-error text-sm">{tt('lineRequired')}</span>
          )}
        </div>

        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <label className="form-control">
            <span className="label-text">{t('shipTo')} *</span>
            <textarea
              className="textarea textarea-bordered"
              rows={2}
              value={shipTo}
              onChange={(e) => setShipTo(e.target.value)}
              aria-label={t('shipTo')}
            />
          </label>
          <label className="form-control">
            <span className="label-text">{t('recipient')}</span>
            <textarea
              className="textarea textarea-bordered"
              rows={2}
              value={recipient}
              onChange={(e) => setRecipient(e.target.value)}
              aria-label={t('recipient')}
            />
          </label>
        </div>

        <label className="form-control">
          <span className="label-text">{t('notes')}</span>
          <textarea
            className="textarea textarea-bordered"
            rows={2}
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            aria-label={t('notes')}
          />
        </label>

        <div className="flex justify-end gap-2">
          <button
            type="button"
            className="btn btn-ghost"
            disabled={create.isPending}
            onClick={async () => {
              const id = await createDo();
              if (id) {
                toast.success(tc('save'));
                router.push('/delivery-orders');
              }
            }}
          >
            {t('saveDraft')}
          </button>
          <button
            type="button"
            className="btn btn-primary"
            disabled={create.isPending}
            onClick={async () => {
              const id = await createDo();
              if (!id) return;
              try {
                // Sprint 13h P9 — Draft → Issued (doc_no allocated; TI fires later on MarkDelivered).
                await action.mutateAsync({ id, action: 'issue' });
                toast.success(t('issued'));
                router.push('/delivery-orders');
              } catch (e) {
                toast.error((e as { detail?: string })?.detail ?? tc('error'));
              }
            }}
          >
            {t('issue')}
          </button>
        </div>
      </div>

      <div className="preview-side">
        <PaperDocument
          docType={cfg.docType}
          docTypeEn={cfg.docTypeEn}
          docNo="(ฉบับร่าง)"
          issueDate={docDate}
          seller={companyToSeller(company.data)}
          customer={{ name: '—' }}
          items={lines.map((l) => ({
            description: l.descriptionTh,
            quantity: l.quantity,
            unit: l.uomText,
            amount: 0,
          }))}
          summary={{ subtotal: 0, vat: 0, total: 0 }}
          notes={composedNotes()}
          signRoles={cfg.signRoles}
        />
      </div>
      </div>
    </>
  );
}
