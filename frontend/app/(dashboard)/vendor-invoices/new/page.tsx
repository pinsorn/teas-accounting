'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Trash2 } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { DateInput } from '@/components/ui/DateInput';
import { VendorSelector } from '@/components/ui/VendorSelector';
import { ExpenseCategorySelector } from '@/components/ui/ExpenseCategorySelector';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import {
  useCreateVendorInvoice, usePostVendorInvoice, useVendor,
  usePurchaseOrders, usePurchaseOrder,
} from '@/lib/queries';
import { bangkokToday, formatTHB } from '@/lib/utils';

interface Row {
  key: number; categoryId: number | null; recoverable: boolean;
  description: string; amount: number; vatRate: number;
}
const emptyRow = (k: number): Row =>
  ({ key: k, categoryId: null, recoverable: true, description: '', amount: 0, vatRate: 0.07 });

// ม.82/4 — claim period options: vendor TI month .. +6 (yyyymm).
function claimOptions(vendorTiDate: string): number[] {
  const d = new Date(vendorTiDate + 'T00:00:00');
  if (Number.isNaN(d.getTime())) return [];
  return Array.from({ length: 7 }, (_, m) => {
    const x = new Date(d.getFullYear(), d.getMonth() + m, 1);
    return x.getFullYear() * 100 + (x.getMonth() + 1);
  });
}

export default function VendorInvoiceNewPage() {
  const t = useTranslations('vi');
  const tc = useTranslations('common');
  const router = useRouter();
  const create = useCreateVendorInvoice();
  const post = usePostVendorInvoice();
  const docDate = bangkokToday(); // locked (CLAUDE.md §10)

  const [vendorId, setVendorId] = useState<number | null>(null);
  const [vendorLabel, setVendorLabel] = useState('');
  const [tiNo, setTiNo] = useState('');
  const [tiDate, setTiDate] = useState(docDate);
  const [claim, setClaim] = useState<number | null>(null);
  const [rows, setRows] = useState<Row[]>([emptyRow(1)]);
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);
  const [poId, setPoId] = useState<number | null>(null);

  // Sprint 12 — optional PO link: list Approved POs of the chosen vendor;
  // selecting one pulls its lines into the VI (category still picked by user).
  const approvedPos = usePurchaseOrders('Approved', vendorId ?? undefined).data ?? [];
  const poDetail = usePurchaseOrder(poId).data;
  useEffect(() => {
    if (!poDetail || poDetail.purchaseOrderId !== poId) return;
    setRows(poDetail.lines.map((l, i) => ({
      key: i + 1, categoryId: null, recoverable: true,
      description: l.descriptionTh,
      amount: l.lineAmount,
      vatRate: l.lineAmount > 0 ? Math.round((l.taxAmount / l.lineAmount) * 100) / 100 : 0.07,
    })));
  }, [poDetail, poId]);

  const options = useMemo(() => claimOptions(tiDate), [tiDate]);
  const effClaim = claim ?? options[0] ?? null;

  // Sprint 8.7 — vendor flags drive has_input_vat (auto/lock for non-VAT /
  // foreign-no-VAT-D vendors). Backend re-derives if not sent; we send explicit.
  const vendor = useVendor(vendorId ?? 0).data;
  const autoNoInputVat = !!vendor &&
    (!vendor.vatRegistered || (vendor.isForeign && !vendor.hasThaiVatDReg));
  const foreignNoVatD = !!vendor?.isForeign && !vendor.hasThaiVatDReg;
  const hasInputVat = !autoNoInputVat;

  const subtotal = rows.reduce((s, r) => s + r.amount, 0);
  const vatRec = rows.reduce((s, r) => s + (r.recoverable ? r.amount * r.vatRate : 0), 0);
  const vatNon = rows.reduce((s, r) => s + (!r.recoverable ? r.amount * r.vatRate : 0), 0);
  const total = subtotal + vatRec + vatNon;

  const canSave =
    vendorId !== null && tiNo.trim() !== '' &&
    rows.length > 0 && rows.every((r) => r.categoryId !== null && r.amount > 0 && r.description.trim() !== '');

  function setRow(key: number, patch: Partial<Row>) {
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }

  async function saveDraft(): Promise<number | null> {
    try {
      const res = await create.mutateAsync({
        docDate,
        vendorId: vendorId!,
        vendorTaxInvoiceNo: tiNo,
        vendorTaxInvoiceDate: tiDate,
        vatClaimPeriod: effClaim,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: null,
        purchaseOrderId: poId,
        lines: rows.map((r) => ({
          expenseCategoryId: r.categoryId!,
          expenseAccountId: null,
          description: r.description,
          amount: r.amount,
          vatRate: r.vatRate,
        })),
        hasInputVat,
      });
      toast.success(tc('save'));
      return res.vendor_invoice_id;
    } catch {
      toast.error(tc('error'));
      return null;
    }
  }

  return (
    <>
      <PageHeader title={t('create')} subtitle={`${t('docDate')}: ${docDate}`} />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <VendorSelector
          value={vendorId}
          onChange={(id, label) => {
            setVendorId(id); setVendorLabel(label); setPoId(null);
          }}
        />
        <DateInput value={docDate} locked label={t('docDate')} />
        {vendorId !== null && approvedPos.length > 0 && (
          <label className="form-control md:col-span-2">
            <span className="label-text">{t('linkPo')}</span>
            <select className="select select-bordered" data-testid="vi-po-select"
              value={poId ?? ''}
              onChange={(e) => setPoId(e.target.value ? Number(e.target.value) : null)}>
              <option value="">{t('noPo')}</option>
              {approvedPos.map((p) => (
                <option key={p.purchaseOrderId} value={p.purchaseOrderId}>
                  {p.docNo ?? `#${p.purchaseOrderId}`} — {formatTHB(p.totalAmount)}
                </option>
              ))}
            </select>
            <span className="label-text-alt text-base-content/50">{t('linkPoHint')}</span>
          </label>
        )}
        {vendor && (
          <div className="md:col-span-2 text-xs">
            {foreignNoVatD
              ? <span className="text-warning">{t('pnd36Warning')}</span>
              : autoNoInputVat
                ? <span className="text-info">{t('noInputVatInfo')}</span>
                : vendor.isForeign
                  ? <span className="text-info">{t('vatDInfo')}</span>
                  : null}
          </div>
        )}
        <label className="form-control">
          <span className="label-text">{t('vendorTiNo')} *</span>
          <input className="input input-bordered" value={tiNo}
            onChange={(e) => setTiNo(e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('vendorTiDate')} *</span>
          <input type="date" className="input input-bordered" value={tiDate}
            max={docDate} onChange={(e) => { setTiDate(e.target.value); setClaim(null); }} />
        </label>
        <label className="form-control">
          <span className="label-text">{t('claimPeriod')}</span>
          <select className="select select-bordered" value={effClaim ?? ''}
            onChange={(e) => setClaim(Number(e.target.value))}>
            {options.map((o) => (
              <option key={o} value={o}>
                {String(o).slice(0, 4)}-{String(o).slice(4)}
              </option>
            ))}
          </select>
          <span className="label-text-alt text-base-content/50">{t('claimHint')}</span>
        </label>
      </div>

      <div className="mt-6">
        <div className="mb-2 flex items-center justify-between">
          <h3 className="font-semibold">{t('lines')}</h3>
          <button className="btn btn-ghost btn-xs gap-1"
            onClick={() => setRows((rs) => [...rs, emptyRow(Date.now())])}>
            <Plus className="h-3 w-3" /> {t('addLine')}
          </button>
        </div>
        <div className="space-y-3">
          {rows.map((r) => (
            <div key={r.key} className="rounded-lg border border-base-300 p-3">
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                <ExpenseCategorySelector
                  value={r.categoryId}
                  onChange={(id, cat) =>
                    setRow(r.key, { categoryId: id, recoverable: cat.defaultIsRecoverableVat })}
                />
                <label className="form-control">
                  <span className="label-text">{t('description')} *</span>
                  <input className="input input-bordered input-sm" value={r.description}
                    onChange={(e) => setRow(r.key, { description: e.target.value })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('amount')} *</span>
                  <input type="number" className="input input-bordered input-sm" value={r.amount}
                    onChange={(e) => setRow(r.key, { amount: Number(e.target.value) || 0 })} />
                </label>
                <label className="form-control">
                  <span className="label-text">{t('vatRate')}</span>
                  <input type="number" step="0.01" className="input input-bordered input-sm"
                    value={r.vatRate}
                    onChange={(e) => setRow(r.key, { vatRate: Number(e.target.value) || 0 })} />
                </label>
              </div>
              <div className="mt-2 flex items-center justify-between">
                {!r.recoverable && r.categoryId !== null && (
                  <span className="text-warning text-xs">⚠ {t('nonRecWarn')}</span>
                )}
                <button className="btn btn-ghost btn-xs text-error ml-auto gap-1"
                  onClick={() => setRows((rs) => rs.length > 1 ? rs.filter((x) => x.key !== r.key) : rs)}>
                  <Trash2 className="h-3 w-3" /> {t('remove')}
                </button>
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="mt-6 flex items-end justify-between">
        <dl className="w-72 space-y-1 text-sm">
          <div className="flex justify-between"><dt className="text-base-content/60">{t('subtotal')}</dt>
            <dd className="tabular-nums">{formatTHB(subtotal)}</dd></div>
          <div className="flex justify-between"><dt className="text-base-content/60">{t('vat')}</dt>
            <dd className="tabular-nums">{formatTHB(vatRec)}</dd></div>
          <div className="flex justify-between"><dt className="text-base-content/60">{t('nonRecVat')}</dt>
            <dd className="tabular-nums">{formatTHB(vatNon)}</dd></div>
          <div className="flex justify-between border-t pt-1 font-bold"><dt>{t('total')}</dt>
            <dd className="tabular-nums">{formatTHB(total)}</dd></div>
        </dl>
        <div className="flex gap-2">
          <button className="btn btn-ghost" disabled={!canSave || create.isPending}
            onClick={async () => { const id = await saveDraft(); if (id) router.push('/vendor-invoices'); }}>
            {t('saveDraft')}
          </button>
          <button className="btn btn-primary" disabled={!canSave || create.isPending}
            onClick={async () => { const id = await saveDraft(); if (id) setConfirm({ id }); }}>
            {t('post')}
          </button>
        </div>
      </div>

      <PostConfirmDialog
        docType="vendor_invoice"
        open={confirm !== null}
        busy={post.isPending}
        summary={{ customer: vendorLabel || `#${vendorId}`, total, vat: vatRec }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try {
            const r = await post.mutateAsync(confirm.id);
            toast.success(tc('save'));
            if (r?.poOverReceiptWarning)
              toast.warning(t('poOverReceipt'), { description: r.poOverReceiptWarning });
            router.push(`/vendor-invoices/${confirm.id}`);
          } catch {
            toast.error(tc('error'));
          } finally {
            setConfirm(null);
          }
        }}
      />
    </>
  );
}
