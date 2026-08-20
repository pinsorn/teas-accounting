'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { usePnd36 } from '@/lib/queries';
import { openPdf } from '@/lib/api';
import { formatTHB } from '@/lib/utils';
import type { Pnd36Filing } from '@/lib/types';
import { useConfirm } from '@/hooks/useConfirm';

function thisMonth() {
  return new Date().toISOString().slice(0, 7);
}

export default function Pnd36Page() {
  const t = useTranslations('tf');
  const [ym, setYm] = useState(thisMonth());
  const [filing, setFiling] = useState<Pnd36Filing | null>(null);
  // F1 (specs/fix-pnd36-payment-detection.md §3.1) — reset whenever a fresh filing is loaded, so
  // a stale tick from a previous period can never carry over to this one.
  const [ack, setAck] = useState(false);
  const mut = usePnd36();
  const confirm = useConfirm();

  const unreconciled = filing?.unreconciled;
  const needsAck = unreconciled?.requiresAcknowledgement ?? false;
  // R3/F1 Tier-2 remediation (also-fix) — once finalized the filing is immutable; the warning
  // block still shows the flagged entries for the record, but the checkbox must read as "this
  // WAS acknowledged" (checked, disabled), not "please tick" — a dead-looking prompt under an
  // already-immutable filing was the finding.
  const isFinalized = filing != null && filing.status !== 'Preview';

  async function run(mode: 'preview' | 'finalize') {
    if (mode === 'finalize'
      && !(await confirm({ description: t('pnd36FinalizeConfirm'), variant: 'destructive' }))) return;
    mut.mutate(
      {
        period: Number(ym.replace('-', '')), mode,
        acknowledge: mode === 'finalize' ? ack : undefined,
        // R3/F1 Tier-2 remediation (FIX 3b) — bind the sign-off to the figure the filer actually
        // saw on screen, not just the boolean.
        ackAmount: mode === 'finalize' && ack ? unreconciled?.unexplainedApDebit : undefined,
      },
      {
        onSuccess: (f) => {
          setFiling(f);
          setAck(false);
          toast.success(mode === 'finalize' ? t('finalized') : t('previewed'));
        },
        onError: (e: unknown) =>
          toast.error(e instanceof Error ? e.message : 'Error'),
      },
    );
  }

  return (
    <>
      <PageHeader title={t('pnd36Title')} />
      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('period')}</span>
          <input type="month" className="input input-bordered input-sm"
            value={ym} onChange={(e) => {
              // R3/F1 Tier-2 remediation (FIX 3a) — a stale filing/ack from the PREVIOUS period
              // must never carry over: without this, ticking June then switching to July left the
              // Finalize guard reading June's stale `filing`, enabled on a period never previewed.
              setYm(e.target.value);
              setFiling(null);
              setAck(false);
            }} />
        </label>
        <button className="btn btn-sm btn-primary"
          disabled={mut.isPending} onClick={() => run('preview')}>
          {t('preview')}
        </button>
        <PermissionGate scope="tax.filing.finalize">
          <button className="btn btn-sm btn-secondary"
            disabled={mut.isPending || !filing || (needsAck && !ack)} onClick={() => run('finalize')}>
            {t('finalize')}
          </button>
        </PermissionGate>
        <button className="btn btn-sm btn-outline" data-testid="tf-download-pdf"
          onClick={() => openPdf(`tax-filings/pp36/pdf?period=${ym.replace('-', '')}`)
            .catch((e: unknown) => toast.error(e instanceof Error ? e.message : 'Error'))}>
          {t('downloadPdf')}
        </button>
        {filing && (
          <span data-testid="tf-status"
            className={`badge ${filing.status === 'Preview' ? 'badge-ghost' : 'badge-success'}`}>
            {filing.status} · {filing.submissionMode}
          </span>
        )}
      </div>

      {filing && (
        <>
          <div className="mb-3 text-sm text-base-content/70">
            {t('due')} {filing.filingDueDate}
            {filing.reverseChargeJournalId != null && (
              <> · {t('jvPosted')} #{filing.reverseChargeJournalId}</>
            )}
          </div>
          <div className="overflow-x-auto rounded-lg border border-base-300">
            <table className="table table-zebra">
              <thead>
                <tr>
                  <th>{t('vendor')}</th><th>{t('country')}</th><th>{t('refDoc')}</th>
                  <th className="text-right">{t('serviceAmount')}</th>
                  <th className="text-right">{t('whtRate')}</th>
                  <th className="text-right">{t('vatAmount')}</th>
                </tr>
              </thead>
              <tbody>
                {filing.rows.length === 0 && (
                  <tr><td colSpan={6} className="py-6 text-center text-base-content/50">—</td></tr>
                )}
                {filing.rows.map((r, i) => (
                  <tr key={i}>
                    <td>{r.vendorName}</td>
                    <td>{r.vendorCountry ?? '—'}</td>
                    <td className="font-mono">{r.refDoc}</td>
                    <td className="text-right tabular-nums">{formatTHB(r.serviceAmountThb)}</td>
                    <td className="text-right tabular-nums">{(r.vatRate * 100).toFixed(0)}%</td>
                    <td className="text-right tabular-nums">{formatTHB(r.vatAmount)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="font-bold">
                  <td colSpan={3} className="text-right">{t('total')}</td>
                  <td className="text-right tabular-nums">{formatTHB(filing.totalService)}</td>
                  <td />
                  <td className="text-right tabular-nums">{formatTHB(filing.totalVat)}</td>
                </tr>
              </tfoot>
            </table>
          </div>
          <p data-testid="pnd36-jv-note"
            className="mt-3 text-xs text-base-content/60">{t('pnd36JvNote')}</p>

          {/* F1 §3.1(a) — informational, always shown when non-empty, no friction. */}
          {unreconciled && unreconciled.outstandingForeignInvoices.length > 0 && (
            <div data-testid="pnd36-outstanding-info"
              className="alert alert-info mt-4 text-sm">
              {t('pnd36OutstandingInfo', {
                count: unreconciled.outstandingForeignInvoices.length,
                value: formatTHB(unreconciled.outstandingForeignInvoices
                  .reduce((s, r) => s + r.outstandingAmount, 0)),
                vat: formatTHB(unreconciled.outstandingForeignInvoices
                  .reduce((s, r) => s + r.vatIfPaid, 0)),
              })}
            </div>
          )}

          {/* F1 §3.1(b) — warning + candidate entries + mandatory acknowledgement. */}
          {needsAck && (
            <div data-testid="pnd36-unreconciled-warning" className="alert alert-warning mt-4 flex-col items-stretch gap-2">
              <p className="font-semibold">
                ⚠ {t('pnd36UnexplainedWarning', { amount: formatTHB(unreconciled!.unexplainedApDebit) })}
              </p>
              <p className="text-sm">{t('pnd36UnexplainedHint')}</p>
              <p className="text-sm font-medium">{t('pnd36UnexplainedFix')}</p>

              {unreconciled!.entries.length > 0 && (
                <div className="overflow-x-auto rounded-lg border border-base-300 bg-base-100">
                  <table className="table table-sm">
                    <thead>
                      <tr>
                        <th>{t('docNo')}</th><th>{t('docDate')}</th>
                        <th>{t('pnd36EntryDescription')}</th>
                        <th className="text-right">{t('pnd36Debit')}</th>
                        <th className="text-right">{t('pnd36Credit')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {unreconciled!.entries.map((e, i) => (
                        <tr key={i}>
                          <td className="font-mono">{e.journalDocNo}</td>
                          <td>{e.docDate}</td>
                          <td>{e.description}</td>
                          <td className="text-right tabular-nums">{formatTHB(e.debitAmount)}</td>
                          <td className="text-right tabular-nums">{formatTHB(e.creditAmount)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}

              <label className="label cursor-pointer justify-start gap-2">
                <input type="checkbox" className="checkbox checkbox-sm" data-testid="pnd36-ack-checkbox"
                  checked={isFinalized ? true : ack} disabled={isFinalized}
                  onChange={(e) => setAck(e.target.checked)} />
                <span className="label-text">{t('pnd36AckCheckbox')}</span>
              </label>
              {!isFinalized && !ack && <p className="text-xs text-base-content/60">{t('pnd36AckRequired')}</p>}
            </div>
          )}
        </>
      )}
    </>
  );
}
