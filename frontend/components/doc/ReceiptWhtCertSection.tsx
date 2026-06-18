'use client';

import { useState } from 'react';
import { AlertTriangle, Check } from 'lucide-react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { useSetReceiptWhtCert } from '@/lib/queries';
import { formatDate } from '@/lib/utils';

// Sprint 13j-FE — 50ทวิ status + late entry on the receipt detail page.
// A receipt with WHT but no cert number shows "ขาดใบทวิ 50" + a form to add
// the number/date later. The scanned cert is attached via AttachmentsSection.
export function ReceiptWhtCertSection({
  receiptId,
  whtAmount,
  certNo,
  certDate,
}: {
  receiptId: number;
  whtAmount: number;
  certNo: string | null;
  certDate: string | null;
}) {
  const tw = useTranslations('rc.wht');
  const save = useSetReceiptWhtCert(receiptId);
  const [no, setNo] = useState('');
  const [date, setDate] = useState('');

  if (whtAmount <= 0) return null;

  async function submit() {
    if (!no.trim()) {
      toast.error(tw('certNoRequired'));
      return;
    }
    try {
      await save.mutateAsync({ certNo: no.trim(), certDate: date || null });
      toast.success(tw('certSaved'));
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tw('certError'));
    }
  }

  return (
    <section className="mt-5 rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
      <h3 className="mb-3 text-[15px] font-bold text-ink-900">{tw('certTitle')}</h3>
      {certNo ? (
        <p className="inline-flex items-center gap-2 rounded-full bg-status-success-bg px-3 py-1 text-sm font-medium text-status-success">
          <Check className="h-4 w-4" aria-hidden />
          {tw('certNo')} {certNo}
          {certDate && <> · {tw('certDate')} {formatDate(certDate)}</>}
        </p>
      ) : (
        <div className="space-y-3">
          <p className="inline-flex items-center gap-2 rounded-full bg-status-warning-bg px-3 py-1 text-sm font-semibold text-status-warning">
            <AlertTriangle className="h-4 w-4" aria-hidden />
            {tw('certMissing')}
          </p>
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <label className="form-control">
              <span className="label-text text-ink-600">{tw('certNoLabel')}</span>
              <input className="input input-bordered" value={no} onChange={(e) => setNo(e.target.value)} aria-label={tw('certNoLabel')} />
            </label>
            <label className="form-control">
              <span className="label-text text-ink-600">{tw('certDateLabel')}</span>
              <input type="date" className="input input-bordered" value={date} onChange={(e) => setDate(e.target.value)} aria-label={tw('certDateLabel')} />
            </label>
            <div className="flex items-end">
              <button className="btn btn-primary" disabled={save.isPending} onClick={submit}>
                {tw('certSaveBtn')}
              </button>
            </div>
          </div>
          <p className="text-xs text-ink-500">{tw('certNote')}</p>
        </div>
      )}
    </section>
  );
}
