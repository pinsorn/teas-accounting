'use client';

import { use, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import { useConfirm } from '@/hooks/useConfirm';
import {
  useStatementLines, useMatchSuggestions, useConfirmMatch, useUnmatchLine,
  useCreateInlineJournal, useIgnoreLine, useUnignoreLine, useGlAccounts,
} from '@/lib/queries';
import type { StatementImportLineItem } from '@/lib/types';
import { formatTHB, formatDate, dayGap } from '@/lib/utils';

// Bank reconciliation (specs/bank-reconciliation.md B4.4) — matching screen for one statement
// import: per-line suggest+confirm, inline-JE modal with a CoA contra-account selector,
// unmatch/ignore actions. A Posted (JE-backed) line shows the JE no and disables unmatch (D8).
export default function ImportMatchingPage(
  { params }: { params: Promise<{ id: string; importId: string }> },
) {
  const { id, importId } = use(params);
  const bankAccountId = Number(id);
  const impId = Number(importId);
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const confirm = useConfirm();

  const q = useStatementLines(bankAccountId, impId);
  const unmatch = useUnmatchLine(bankAccountId, impId);
  const ignore = useIgnoreLine(bankAccountId, impId);
  const unignore = useUnignoreLine(bankAccountId, impId);

  const [suggestLine, setSuggestLine] = useState<StatementImportLineItem | null>(null);
  const [journalLine, setJournalLine] = useState<StatementImportLineItem | null>(null);

  const lines = q.data ?? [];

  async function doUnmatch(l: StatementImportLineItem) {
    const ok = await confirm({ description: t('unmatchConfirm'), variant: 'destructive' });
    if (!ok) return;
    try {
      await unmatch.mutateAsync(l.statementLineId);
      toast.success(t('unmatchSuccess'));
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tc('error'));
    }
  }

  async function doIgnore(l: StatementImportLineItem) {
    try {
      await ignore.mutateAsync(l.statementLineId);
      toast.success(tc('save'));
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tc('error'));
    }
  }

  async function doUnignore(l: StatementImportLineItem) {
    try {
      await unignore.mutateAsync(l.statementLineId);
      toast.success(tc('save'));
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tc('error'));
    }
  }

  return (
    <>
      <PageHeader title={t('matching')} subtitle={`#${impId}`} />

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-sm" data-testid="matching-lines-table">
          <thead>
            <tr>
              <th>{t('importDate')}</th>
              <th>{t('importType')}</th>
              <th>{t('importDescription')}</th>
              <th className="text-right">{t('importAmount')}</th>
              <th className="text-right">{t('importBalance')}</th>
              <th>{tc('status')}</th>
              <th className="text-right">{tc('actions')}</th>
            </tr>
          </thead>
          <tbody>
            {q.isLoading && (
              <tr><td colSpan={7} className="py-4 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!q.isLoading && lines.length === 0 && (
              <tr><td colSpan={7} className="py-4 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {lines.map((l) => (
              <tr key={l.statementLineId} data-testid="matching-line-row">
                <td className="whitespace-nowrap">{formatDate(l.txnDate)}</td>
                <td>{l.txnType}</td>
                <td className="max-w-xs truncate" title={l.description}>{l.description}</td>
                <td className={`text-right tabular-nums ${l.direction === 'MoneyIn' ? 'text-status-success' : 'text-status-danger'}`}>
                  {l.direction === 'MoneyIn' ? '+' : '-'}{formatTHB(l.amount)}
                </td>
                <td className="text-right tabular-nums">{formatTHB(l.runningBalance)}</td>
                <td>
                  <span className={
                    l.matchStatus === 'Posted' ? 'text-status-success'
                      : l.matchStatus === 'Matched' ? 'text-status-info'
                      : l.matchStatus === 'Ignored' ? 'text-base-content/50'
                      : 'text-status-draft'
                  }>
                    {l.matchStatus === 'Posted' ? t('statusPosted') : l.matchStatus}
                  </span>
                </td>
                <td className="text-right whitespace-nowrap">
                  <PermissionGate scope="bank.reconcile">
                    {l.matchStatus === 'Unmatched' && (
                      <>
                        <button className="btn btn-ghost btn-xs" data-testid="suggest-open"
                          onClick={() => setSuggestLine(l)}>{t('suggest')}</button>
                        <button className="btn btn-ghost btn-xs" data-testid="journal-open"
                          onClick={() => setJournalLine(l)}>{t('postJournal')}</button>
                        <button className="btn btn-ghost btn-xs" onClick={() => doIgnore(l)}>{t('ignore')}</button>
                      </>
                    )}
                    {l.matchStatus === 'Matched' && (
                      <button className="btn btn-ghost btn-xs text-error" onClick={() => doUnmatch(l)}>
                        {t('unmatch')}
                      </button>
                    )}
                    {l.matchStatus === 'Ignored' && (
                      <button className="btn btn-ghost btn-xs" onClick={() => doUnignore(l)}>{t('unignore')}</button>
                    )}
                  </PermissionGate>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <PermissionGate scope="bank.reconcile">
        {suggestLine && (
          <SuggestModal
            bankAccountId={bankAccountId} importId={impId} line={suggestLine}
            onClose={() => setSuggestLine(null)}
          />
        )}
        {journalLine && (
          <JournalModal
            bankAccountId={bankAccountId} importId={impId} line={journalLine}
            onClose={() => setJournalLine(null)}
          />
        )}
      </PermissionGate>
    </>
  );
}

function SuggestModal({
  bankAccountId, importId, line, onClose,
}: { bankAccountId: number; importId: number; line: StatementImportLineItem; onClose: () => void }) {
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const q = useMatchSuggestions(bankAccountId, line.statementLineId);
  const confirmMatch = useConfirmMatch(bankAccountId, importId);
  const confirmDialog = useConfirm();
  const suggestions = q.data ?? [];

  async function confirm(docType: 'Receipt' | 'PaymentVoucher', docId: number, docDate: string) {
    // Ham ruling (Codex finding #7): manual confirm-match stays unrestricted
    // (cheques clearing >7 days are real), but warn — and require explicit
    // proceed — when the gap exceeds the auto-suggest window (D4, ±7 days).
    const gap = dayGap(line.txnDate, docDate);
    if (gap > 7) {
      const ok = await confirmDialog({
        description: (
          <div role="alert" className="alert alert-warning text-sm">
            {t('matchWindowWarning', { days: gap })}
          </div>
        ),
      });
      if (!ok) return;
    }
    try {
      await confirmMatch.mutateAsync({
        lineId: line.statementLineId,
        req: {
          receiptId: docType === 'Receipt' ? docId : null,
          paymentVoucherId: docType === 'PaymentVoucher' ? docId : null,
        },
      });
      toast.success(t('matchSuccess'));
      onClose();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="mb-3 text-lg font-bold">{t('suggest')}</h3>
        {q.isLoading && <p className="text-sm text-base-content/50">{tc('loading')}</p>}
        {!q.isLoading && suggestions.length === 0 && (
          <p className="text-sm text-base-content/50">{t('noSuggestions')}</p>
        )}
        <ul className="flex flex-col gap-2">
          {suggestions.map((s) => (
            <li key={`${s.docType}-${s.docId}`}
              className="flex items-center justify-between rounded border border-base-300 p-2">
              <span className="text-sm">
                <span className="font-mono">{s.docNo ?? `#${s.docId}`}</span>
                {' · '}{formatDate(s.docDate)}{' · '}{formatTHB(s.amount)}{' · '}{s.partyName}
              </span>
              <button className="btn btn-primary btn-xs" data-testid="suggest-confirm"
                disabled={confirmMatch.isPending} onClick={() => confirm(s.docType, s.docId, s.docDate)}>
                {tc('confirm')}
              </button>
            </li>
          ))}
        </ul>
        <div className="modal-action">
          <button className="btn btn-ghost btn-sm" onClick={onClose}>{tc('cancel')}</button>
        </div>
      </div>
    </div>
  );
}

function JournalModal({
  bankAccountId, importId, line, onClose,
}: { bankAccountId: number; importId: number; line: StatementImportLineItem; onClose: () => void }) {
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const glAccounts = useGlAccounts();
  const createJournal = useCreateInlineJournal(bankAccountId, importId);
  const [contraAccountId, setContraAccountId] = useState('');
  const [description, setDescription] = useState('');

  async function submit() {
    if (!contraAccountId) { toast.error(t('contraAccountRequired')); return; }
    try {
      await createJournal.mutateAsync({
        lineId: line.statementLineId,
        req: { contraAccountId: Number(contraAccountId), description: description.trim() || null },
      });
      toast.success(tc('save'));
      onClose();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box">
        <h3 className="mb-3 text-lg font-bold">{t('postJournal')}</h3>
        <div className="flex flex-col gap-3">
          <label className="form-control">
            <span className="label-text text-xs">{t('contraAccount')}</span>
            <select className="select select-bordered select-sm" data-testid="contra-account"
              value={contraAccountId} onChange={(e) => setContraAccountId(e.target.value)}>
              <option value="">{tc('all') /* placeholder-ish "select" — reuse existing key */}</option>
              {(glAccounts.data ?? []).map((a) => (
                <option key={a.accountId} value={a.accountId}>{a.accountCode} — {a.accountNameTh}</option>
              ))}
            </select>
          </label>
          <label className="form-control">
            <span className="label-text text-xs">{t('journalDescription')}</span>
            <input className="input input-bordered input-sm" value={description}
              onChange={(e) => setDescription(e.target.value)} />
          </label>
        </div>
        <div className="modal-action">
          <button className="btn btn-ghost btn-sm" onClick={onClose}>{tc('cancel')}</button>
          <button className="btn btn-primary btn-sm" data-testid="journal-submit"
            disabled={createJournal.isPending} onClick={submit}>{tc('save')}</button>
        </div>
      </div>
    </div>
  );
}
