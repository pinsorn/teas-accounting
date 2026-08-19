'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Pencil } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { StatusBadge } from '@/components/ui/StatusBadge';
import { useConfirm } from '@/hooks/useConfirm';
import {
  useFixedAsset, useActivateFixedAsset, useDisposeFixedAsset, useWriteOffFixedAsset, useCancelFixedAsset,
} from '@/lib/queries';
import { problemToast } from '@/lib/api';
import { bangkokToday } from '@/lib/utils';

const money = (n: number) => n.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

// Cycle D (specs/fixed-assets.md §9.14) — detail + lifecycle actions. Mirrors
// expense-claims/[id]'s PermissionGate + disabled={m.isPending} idiom for every button.
export default function FixedAssetDetailPage() {
  const id = Number(useParams<{ id: string }>().id);
  const t = useTranslations('fixedAssets');
  const tc = useTranslations('common');
  const confirm = useConfirm();
  const { data: d, isLoading, isError } = useFixedAsset(id);
  const activate = useActivateFixedAsset();
  const dispose = useDisposeFixedAsset();
  const writeOff = useWriteOffFixedAsset();
  const cancelAsset = useCancelFixedAsset();

  const [disposeOpen, setDisposeOpen] = useState(false);
  const [disposalDate, setDisposalDate] = useState(bangkokToday());
  const [proceeds, setProceeds] = useState(0);
  const [vatAmount, setVatAmount] = useState(0);
  const [buyerName, setBuyerName] = useState('');

  const [writeOffOpen, setWriteOffOpen] = useState(false);
  const [writeOffDate, setWriteOffDate] = useState(bangkokToday());
  const [writeOffReason, setWriteOffReason] = useState('');

  const depreciatedThrough = useMemo(() => {
    if (!d || d.runLines.length === 0) return null;
    const latest = d.runLines.reduce((a, b) =>
      (a.periodYear * 12 + a.periodMonth) >= (b.periodYear * 12 + b.periodMonth) ? a : b);
    return `${latest.periodYear}-${String(latest.periodMonth).padStart(2, '0')}`;
  }, [d]);

  if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (isError || !d) return <p className="text-error">{tc('error')}</p>;

  async function doActivate() {
    const ok = await confirm({
      title: t('confirmActivateTitle'), description: t('confirmActivateDesc'),
      confirmText: t('activate'),
    });
    if (!ok) return;
    try { await activate.mutateAsync(id); toast.success(t('activated')); }
    catch (e) { problemToast(e, tc('error')); }
  }
  async function doCancel() {
    const ok = await confirm({
      title: t('confirmCancelTitle'), description: t('confirmCancelDesc'), variant: 'destructive',
    });
    if (!ok) return;
    try { await cancelAsset.mutateAsync(id); toast.success(t('cancelled')); }
    catch (e) { problemToast(e, tc('error')); }
  }
  async function doDispose() {
    try {
      await dispose.mutateAsync({
        id, req: { disposalDate, proceeds, vatAmount: vatAmount || null, buyerName: buyerName.trim() || null },
      });
      toast.success(t('disposed'));
      setDisposeOpen(false);
    } catch (e) { problemToast(e, tc('error')); }
  }
  async function doWriteOff() {
    if (!writeOffReason.trim()) { toast.error(t('writeOffReason')); return; }
    try {
      await writeOff.mutateAsync({ id, req: { date: writeOffDate, reason: writeOffReason.trim() } });
      toast.success(t('writtenOff'));
      setWriteOffOpen(false);
    } catch (e) { problemToast(e, tc('error')); }
  }

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={d.docNo ?? d.name}
        actions={
          <div className="flex gap-2">
            {d.status === 'Draft' && (
              <PermissionGate scope="fixedasset.manage">
                <Link data-testid="fa-edit" href={`/fixed-assets/${id}/edit`} className="btn btn-secondary btn-sm gap-1">
                  <Pencil className="h-4 w-4" aria-hidden /> {tc('edit')}
                </Link>
              </PermissionGate>
            )}
            {d.status === 'Draft' && (
              <PermissionGate scope="fixedasset.manage">
                <button className="btn btn-primary btn-sm" disabled={activate.isPending} onClick={doActivate}>
                  {t('activate')}
                </button>
              </PermissionGate>
            )}
            {d.status === 'Draft' && (
              <PermissionGate scope="fixedasset.manage">
                <button className="btn btn-ghost btn-sm text-error" disabled={cancelAsset.isPending} onClick={doCancel}>
                  {t('cancelAsset')}
                </button>
              </PermissionGate>
            )}
            {d.status === 'Active' && (
              <PermissionGate scope="fixedasset.dispose">
                <button className="btn btn-secondary btn-sm" disabled={dispose.isPending} onClick={() => setDisposeOpen(true)}>
                  {t('dispose')}
                </button>
              </PermissionGate>
            )}
            {d.status === 'Active' && (
              <PermissionGate scope="fixedasset.dispose">
                <button className="btn btn-ghost btn-sm text-error" disabled={writeOff.isPending} onClick={() => setWriteOffOpen(true)}>
                  {t('writeOff')}
                </button>
              </PermissionGate>
            )}
          </div>
        }
      />

      <div className="mb-4 flex flex-wrap items-center gap-3">
        <StatusBadge status={d.status} />
        <span className="text-sm text-base-content/60">{d.acquireDate}</span>
        {d.category && <span className="badge badge-ghost">{d.category}</span>}
      </div>

      {/* FA-A / O1 (specs/fix-army-findings-2026-07-22.md) — acquisition/activation deliberately
          posts NO journal entry (FixedAssetService.cs: the linked Vendor Invoice, or a manual
          opening-balance JE, is expected to book the cost). Without either, the cost never hits
          the GL — yet Dispose/WriteOff unconditionally credit the full registered cost later. */}
      {!d.vendorInvoiceId && (
        <div role="alert" className="alert alert-warning mb-4 text-sm" data-testid="fa-no-gl-cost-warning">
          <span>{t('costNotOnGlWarning')}</span>
        </div>
      )}

      <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
        <div className="grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
          <div><span className="text-base-content/60">{t('name')}:</span> {d.name}</div>
          <div><span className="text-base-content/60">{t('cost')}:</span> {money(d.cost)}</div>
          <div><span className="text-base-content/60">{t('salvageValue')}:</span> {money(d.salvageValue)}</div>
          <div><span className="text-base-content/60">{t('usefulLife')}:</span> {d.usefulLifeMonths} {t('usefulLifeMonths')}</div>
          <div><span className="text-base-content/60">{t('monthlyAmount')}:</span> {money(d.monthlyAmount)}</div>
          <div><span className="text-base-content/60">{t('depreciationStartDate')}:</span> {d.depreciationStartDate}</div>
          <div><span className="text-base-content/60">{t('accumulatedDepreciation')}:</span> {money(d.accumulatedDepreciation)}</div>
          <div><span className="text-base-content/60 font-semibold">{t('nbv')}:</span> <span className="font-semibold">{money(d.nbv)}</span></div>
          <div className="md:col-span-2">
            <span className="text-base-content/60">{depreciatedThrough ? t('depreciatedThrough', { period: depreciatedThrough }) : t('notYetDepreciated')}</span>
          </div>
          {d.notes && (
            <div className="md:col-span-2"><span className="text-base-content/60">{t('notes')}:</span> {d.notes}</div>
          )}
        </div>

        {(d.status === 'Disposed' || d.status === 'WrittenOff') && (
          <div className="mt-4 rounded-lg border border-base-300 bg-base-200/50 p-3 text-sm">
            <h3 className="mb-2 font-semibold">{t(d.status === 'Disposed' ? 'disposed' : 'writtenOff')}</h3>
            <div className="grid grid-cols-1 gap-2 md:grid-cols-2">
              {d.disposalDate && <div><span className="text-base-content/60">{t('disposalDate')}:</span> {d.disposalDate}</div>}
              {d.disposalProceeds != null && <div><span className="text-base-content/60">{t('proceeds')}:</span> {money(d.disposalProceeds)}</div>}
              {d.disposalVatAmount != null && <div><span className="text-base-content/60">{t('vatAmount')}:</span> {money(d.disposalVatAmount)}</div>}
              {d.disposalGainLoss != null && <div><span className="text-base-content/60">{t('gainLoss')}:</span> {money(d.disposalGainLoss)}</div>}
              {d.disposalBuyerName && <div><span className="text-base-content/60">{t('buyerName')}:</span> {d.disposalBuyerName}</div>}
              {d.disposalJournalEntryId != null && (
                <div>
                  <span className="text-base-content/60">{t('journalEntry')}:</span>{' '}
                  <Link href={`/journals/${d.disposalJournalEntryId}`} className="link link-primary">{d.disposalJournalEntryId}</Link>
                </div>
              )}
              {d.writeoffReason && <div className="md:col-span-2"><span className="text-base-content/60">{t('writeOffReason')}:</span> {d.writeoffReason}</div>}
            </div>
          </div>
        )}

        <div className="mt-4">
          <h3 className="mb-2 font-semibold">{t('runHistory')}</h3>
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table table-sm">
              <thead>
                <tr>
                  <th>{t('period')}</th>
                  <th className="text-right">{t('amount')}</th>
                  <th className="text-right">{t('accumulatedAfter')}</th>
                </tr>
              </thead>
              <tbody>
                {d.runLines.length === 0 && (
                  <tr><td colSpan={3} className="py-4 text-center text-base-content/50">{t('notYetDepreciated')}</td></tr>
                )}
                {d.runLines.map((l) => (
                  <tr key={l.depreciationRunId}>
                    <td>{l.periodYear}-{String(l.periodMonth).padStart(2, '0')}</td>
                    <td className="text-right">{money(l.amount)}</td>
                    <td className="text-right">{money(l.accumulatedAfter)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      {disposeOpen && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="mb-3 text-lg font-bold">{t('dispose')}</h3>
            <p className="mb-3 rounded-lg bg-warning/10 p-2 text-xs text-warning-content">{t('vatNotice')}</p>
            <label className="form-control">
              <span className="label-text">{t('disposalDate')} *</span>
              <input type="date" className="input input-bordered" value={disposalDate}
                onChange={(e) => setDisposalDate(e.target.value)} />
            </label>
            <label className="form-control mt-2">
              <span className="label-text">{t('proceeds')} *</span>
              <input type="number" className="input input-bordered" value={proceeds}
                onChange={(e) => setProceeds(Number(e.target.value) || 0)} />
            </label>
            <label className="form-control mt-2">
              <span className="label-text">{t('vatAmount')}</span>
              <div className="flex gap-2">
                <input type="number" className="input input-bordered flex-1" value={vatAmount}
                  onChange={(e) => setVatAmount(Number(e.target.value) || 0)} />
                <button type="button" className="btn btn-outline btn-sm"
                  onClick={() => setVatAmount(Math.round(proceeds * 0.07 * 100) / 100)}>
                  7%
                </button>
              </div>
            </label>
            <label className="form-control mt-2">
              <span className="label-text">{t('buyerName')}</span>
              <input className="input input-bordered" value={buyerName}
                onChange={(e) => setBuyerName(e.target.value)} />
            </label>
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setDisposeOpen(false)}>{tc('cancel')}</button>
              <button className="btn btn-primary btn-sm" disabled={dispose.isPending} onClick={doDispose}>{t('dispose')}</button>
            </div>
          </div>
        </div>
      )}

      {writeOffOpen && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box">
            <h3 className="mb-3 text-lg font-bold">{t('writeOff')}</h3>
            <label className="form-control">
              <span className="label-text">{t('disposalDate')} *</span>
              <input type="date" className="input input-bordered" value={writeOffDate}
                onChange={(e) => setWriteOffDate(e.target.value)} />
            </label>
            <label className="form-control mt-2">
              <span className="label-text">{t('writeOffReason')} *</span>
              <textarea className="textarea textarea-bordered" value={writeOffReason}
                onChange={(e) => setWriteOffReason(e.target.value)} />
            </label>
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setWriteOffOpen(false)}>{tc('cancel')}</button>
              <button className="btn btn-error btn-sm" disabled={writeOff.isPending} onClick={doWriteOff}>{t('writeOff')}</button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
