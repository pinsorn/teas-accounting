'use client';

import { Suspense, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Trash2 } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { DateInput } from '@/components/ui/DateInput';
import { VendorSelector } from '@/components/ui/VendorSelector';
import { ExpenseCategorySelector } from '@/components/ui/ExpenseCategorySelector';
import { apiPost } from '@/lib/api';
import { useVendor } from '@/lib/queries';
import { bangkokToday, formatTHB } from '@/lib/utils';

interface Row {
  key: number; description: string; amount: number;
  vatRate: number; whtRate: number; recoverable: boolean;
}
const emptyRow = (k: number): Row =>
  ({ key: k, description: '', amount: 0, vatRate: 0, whtRate: 0, recoverable: true });

function PvForm() {
  const t = useTranslations('pv');
  const tc = useTranslations('common');
  const router = useRouter();
  const params = useSearchParams();
  const fromVi = params.get('fromVendorInvoiceId');
  const docDate = bangkokToday();

  const [vendorId, setVendorId] = useState<number | null>(null);
  const [categoryId, setCategoryId] = useState<number | null>(null);
  const [catRecoverable, setCatRecoverable] = useState(true);
  const [method, setMethod] = useState<'Cash' | 'Transfer' | 'Cheque'>('Transfer');
  const [chequeNo, setChequeNo] = useState('');
  const [chequeDate, setChequeDate] = useState(docDate);
  const [rows, setRows] = useState<Row[]>([emptyRow(1)]);
  const [busy, setBusy] = useState(false);
  const [manualSelfWithhold, setManualSelfWithhold] = useState(false);

  // Sprint 8.7 — vendor flags drive self-withhold (auto/lock for foreign).
  const vendor = useVendor(vendorId ?? 0).data;
  const foreignNoVatD = !!vendor?.isForeign && !vendor.hasThaiVatDReg;
  const foreignVatD = !!vendor?.isForeign && !!vendor.hasThaiVatDReg;
  const selfWithholdLocked = !!vendor?.isForeign;
  const selfWithhold = foreignNoVatD ? true : foreignVatD ? false : manualSelfWithhold;

  const subtotal = rows.reduce((s, r) => s + r.amount, 0);
  const vat = rows.reduce((s, r) => s + r.amount * r.vatRate, 0);
  const wht = rows.reduce((s, r) => s + r.amount * r.whtRate, 0);
  const net = subtotal + vat - wht;

  const canSave =
    vendorId !== null && categoryId !== null &&
    rows.every((r) => r.description.trim() !== '' && r.amount > 0) &&
    (method !== 'Cheque' || (chequeNo.trim() !== '' && chequeDate !== ''));

  function setRow(key: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  async function saveDraft() {
    setBusy(true);
    try {
      const res = await apiPost<{ payment_voucher_id: number }>('payment-vouchers/', {
        docDate,
        vendorId,
        expenseCategoryId: categoryId,
        paymentMethod: method,
        chequeNo: method === 'Cheque' ? chequeNo : null,
        chequeDate: method === 'Cheque' ? chequeDate : null,
        bankAccountId: null,
        currencyCode: 'THB',
        exchangeRate: 1,
        description: fromVi ? `${t('settlingVi')} #${fromVi}` : null,
        notes: null,
        lines: rows.map((r) => ({
          expenseAccountId: null,
          description: r.description,
          amount: r.amount,
          taxCodeId: null,
          vatRate: r.vatRate,
          isRecoverableVat: catRecoverable,
          whtTypeId: null,
          whtRate: r.whtRate,
        })),
        vendorInvoiceId: fromVi ? Number(fromVi) : null,
        selfWithholdMode: fromVi ? null : selfWithhold,
      });
      toast.success(tc('save'));
      router.push(`/payment-vouchers/${res.payment_voucher_id}`);
    } catch {
      toast.error(tc('error'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader
        title={t('create')}
        subtitle={fromVi ? `${t('settlingVi')} #${fromVi}` : `${docDate}`}
      />
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <VendorSelector value={vendorId} onChange={(id) => setVendorId(id)} />
        <DateInput value={docDate} locked label="วันที่" />
        <ExpenseCategorySelector
          value={categoryId}
          onChange={(id, cat) => { setCategoryId(id); setCatRecoverable(cat.isRecoverableVat); }}
        />
        <label className="form-control">
          <span className="label-text">{t('method')}</span>
          <select className="select select-bordered" value={method}
            onChange={(e) => setMethod(e.target.value as 'Cash' | 'Transfer' | 'Cheque')}>
            <option value="Cash">Cash</option>
            <option value="Transfer">Transfer</option>
            <option value="Cheque">Cheque</option>
          </select>
        </label>
        {method === 'Cheque' && (
          <>
            <label className="form-control">
              <span className="label-text">Cheque No. *</span>
              <input className="input input-bordered" value={chequeNo}
                onChange={(e) => setChequeNo(e.target.value)} />
            </label>
            <label className="form-control">
              <span className="label-text">Cheque Date *</span>
              <input type="date" className="input input-bordered" value={chequeDate}
                onChange={(e) => setChequeDate(e.target.value)} />
            </label>
          </>
        )}
      </div>

      <div className="mt-6">
        <div className="mb-2 flex items-center justify-between">
          <h3 className="font-semibold">{t('lines')}</h3>
          <button className="btn btn-ghost btn-xs gap-1"
            onClick={() => setRows((rs) => [...rs, emptyRow(Date.now())])}>
            <Plus className="h-3 w-3" /> เพิ่มรายการ
          </button>
        </div>
        <div className="space-y-3">
          {rows.map((r) => (
            <div key={r.key} className="grid grid-cols-1 gap-3 rounded-lg border border-base-300 p-3 md:grid-cols-4">
              <label className="form-control md:col-span-2">
                <span className="label-text">รายละเอียด *</span>
                <input className="input input-bordered input-sm" value={r.description}
                  onChange={(e) => setRow(r.key, { description: e.target.value })} />
              </label>
              <label className="form-control">
                <span className="label-text">{t('subtotal')} *</span>
                <input type="number" className="input input-bordered input-sm" value={r.amount}
                  onChange={(e) => setRow(r.key, { amount: Number(e.target.value) || 0 })} />
              </label>
              <label className="form-control">
                <span className="label-text">VAT</span>
                <input type="number" step="0.01" className="input input-bordered input-sm"
                  value={r.vatRate}
                  onChange={(e) => setRow(r.key, { vatRate: Number(e.target.value) || 0 })} />
              </label>
              <label className="form-control">
                <span className="label-text">WHT</span>
                <input type="number" step="0.01" className="input input-bordered input-sm"
                  value={r.whtRate}
                  onChange={(e) => setRow(r.key, { whtRate: Number(e.target.value) || 0 })} />
              </label>
              <button className="btn btn-ghost btn-xs text-error md:col-span-3 md:ml-auto md:w-fit"
                onClick={() => setRows((rs) => rs.length > 1 ? rs.filter((x) => x.key !== r.key) : rs)}>
                <Trash2 className="h-3 w-3" /> ลบ
              </button>
            </div>
          ))}
        </div>
      </div>

      {!fromVi && (
        <div className="mt-4 rounded-lg border border-base-300 p-3">
          <label className="label cursor-pointer justify-start gap-3">
            <input type="checkbox" className="toggle toggle-warning toggle-sm"
              checked={selfWithhold} disabled={selfWithholdLocked}
              onChange={(e) => setManualSelfWithhold(e.target.checked)} />
            <span className="font-semibold">{t('selfWithhold.toggle')}</span>
          </label>
          {foreignNoVatD && (
            <p className="mt-1 text-xs text-warning">{t('selfWithhold.autoLockedForeign')}</p>
          )}
          {foreignVatD && (
            <p className="mt-1 text-xs text-info">{t('selfWithhold.vatDInfo')}</p>
          )}
          {!selfWithholdLocked && selfWithhold && (
            <p className="mt-1 text-xs text-base-content/60">{t('selfWithhold.explanation')}</p>
          )}
        </div>
      )}

      <div className="mt-6 flex items-end justify-between">
        <dl className="w-72 space-y-1 text-sm">
          <div className="flex justify-between"><dt className="text-base-content/60">{t('subtotal')}</dt>
            <dd className="tabular-nums">{formatTHB(subtotal)}</dd></div>
          <div className="flex justify-between"><dt className="text-base-content/60">{t('vat')}</dt>
            <dd className="tabular-nums">{formatTHB(vat)}</dd></div>
          <div className="flex justify-between"><dt className="text-base-content/60">{t('wht')}</dt>
            <dd className="tabular-nums">−{formatTHB(wht)}</dd></div>
          <div className="flex justify-between border-t pt-1 font-bold"><dt>{t('netPaid')}</dt>
            <dd className="tabular-nums">{formatTHB(net)}</dd></div>
        </dl>
        <button className="btn btn-primary" disabled={!canSave || busy} onClick={saveDraft}>
          {tc('save')}
        </button>
      </div>
    </>
  );
}

export default function PaymentVoucherNewPage() {
  return (
    <Suspense fallback={null}>
      <PvForm />
    </Suspense>
  );
}
