'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Download } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useGeneralLedger, useGlAccounts } from '@/lib/queries';
import { downloadFile, qs } from '@/lib/api';
import { errorToToast } from '@/lib/api/errors';
import { formatTHB, formatDate, bangkokMonthStart, bangkokMonthEnd } from '@/lib/utils';
import type { GeneralLedgerAccountOption } from '@/lib/types';

function accountLabel(a: GeneralLedgerAccountOption) {
  return `${a.accountCode} — ${a.accountNameTh}`;
}

export default function GeneralLedgerPage() {
  const t = useTranslations('report');
  const tc = useTranslations('common');
  const [accountText, setAccountText] = useState('');
  const [from, setFrom] = useState(bangkokMonthStart());
  const [to, setTo] = useState(bangkokMonthEnd());
  // Applied filters — the query only (re)fires when "แสดงรายงาน" is clicked, per spec
  // (`// ponytail: datalist; swap for combobox lib only if UX proves insufficient`).
  const [appliedAccountId, setAppliedAccountId] = useState<number | undefined>(undefined);
  const [appliedFrom, setAppliedFrom] = useState(from);
  const [appliedTo, setAppliedTo] = useState(to);

  const accounts = useGlAccounts();
  const gl = useGeneralLedger(appliedAccountId, appliedFrom, appliedTo);

  // Super-admin sees accounts across ALL companies (EF global-filter bypass —
  // pre-existing platform behavior, not changed here), so the same
  // "code — nameTh" label can legitimately belong to several different
  // accounts. Disambiguate duplicated labels with the accountId so the
  // datalist resolver below can't silently match the wrong company's account.
  const accountOptions = useMemo(() => {
    const list = accounts.data ?? [];
    const counts = new Map<string, number>();
    for (const a of list) {
      const label = accountLabel(a);
      counts.set(label, (counts.get(label) ?? 0) + 1);
    }
    return list.map((a) => {
      const label = accountLabel(a);
      return { ...a, label: (counts.get(label) ?? 0) > 1 ? `${label} [#${a.accountId}]` : label };
    });
  }, [accounts.data]);

  const resolvedAccountId = accountOptions.find((a) => a.label === accountText)?.accountId;

  function showReport() {
    if (resolvedAccountId == null) return;
    setAppliedAccountId(resolvedAccountId);
    setAppliedFrom(from);
    setAppliedTo(to);
  }

  async function exportFile(format: 'pdf' | 'csv') {
    if (!gl.data || appliedAccountId == null) return;
    const filename = `general-ledger-${gl.data.accountCode}-${appliedFrom}-${appliedTo}.${format}`;
    try {
      await downloadFile(
        `reports/general-ledger/export${qs({
          accountId: appliedAccountId, fromDate: appliedFrom, toDate: appliedTo, format,
        })}`,
        filename,
      );
    } catch (e) {
      toast.error(errorToToast(e));
    }
  }

  return (
    <>
      <PageHeader
        title={t('glTitle')}
        actions={
          <>
            <button className="btn btn-outline btn-sm gap-1" disabled={!gl.data}
              onClick={() => exportFile('pdf')}>
              <Download className="h-4 w-4" aria-hidden /> {t('exportPdf')}
            </button>
            <button className="btn btn-outline btn-sm gap-1" disabled={!gl.data}
              onClick={() => exportFile('csv')}>
              <Download className="h-4 w-4" aria-hidden /> {t('exportExcel')}
            </button>
          </>
        }
      />

      <div className="mb-4 flex flex-wrap items-end gap-3">
        <label className="form-control">
          <span className="label-text text-xs">{t('account')}</span>
          <input list="gl-account-options" className="input input-bordered input-sm w-72"
            placeholder={t('accountPlaceholder')}
            value={accountText} onChange={(e) => setAccountText(e.target.value)} />
          <datalist id="gl-account-options">
            {accountOptions.map((a) => (
              <option key={a.accountId} value={a.label} />
            ))}
          </datalist>
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('from')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="form-control">
          <span className="label-text text-xs">{t('to')}</span>
          <input type="date" className="input input-bordered input-sm"
            value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <button className="btn btn-primary btn-sm" disabled={resolvedAccountId == null}
          onClick={showReport}>
          {t('showReport')}
        </button>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-zebra">
          <thead>
            <tr>
              <th>{tc('date')}</th>
              <th>{t('docNo')}</th>
              <th>{t('description')}</th>
              <th>{t('ref')}</th>
              <th className="text-right">{t('debit')}</th>
              <th className="text-right">{t('credit')}</th>
              <th className="text-right">{t('runningBalance')}</th>
            </tr>
          </thead>
          <tbody>
            {!appliedAccountId && (
              <tr><td colSpan={7} className="py-6 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {appliedAccountId != null && gl.isLoading && (
              <tr><td colSpan={7} className="py-6 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {gl.data && (
              <>
                <tr className="font-semibold">
                  <td colSpan={4}>{t('openingBalance')}</td>
                  <td className="text-right tabular-nums" />
                  <td className="text-right tabular-nums" />
                  <td className="text-right tabular-nums">{formatTHB(gl.data.openingBalance)}</td>
                </tr>
                {gl.data.rows.map((r, i) => (
                  <tr key={`${r.journalId}-${i}`}>
                    <td>{formatDate(r.docDate)}</td>
                    <td>
                      <Link href={`/journals/${r.journalId}`} className="link link-primary">
                        {r.docNo}
                      </Link>
                    </td>
                    <td>{r.description}</td>
                    <td>{r.reference}</td>
                    <td className="text-right tabular-nums">{r.debit ? formatTHB(r.debit) : ''}</td>
                    <td className="text-right tabular-nums">{r.credit ? formatTHB(r.credit) : ''}</td>
                    <td className="text-right tabular-nums">{formatTHB(r.runningBalance)}</td>
                  </tr>
                ))}
              </>
            )}
          </tbody>
          {gl.data && (
            <tfoot>
              <tr className="font-bold">
                <td colSpan={4} className="text-right">{t('totalRow')}</td>
                <td className="text-right tabular-nums">{formatTHB(gl.data.totalDebit)}</td>
                <td className="text-right tabular-nums">{formatTHB(gl.data.totalCredit)}</td>
                <td className="text-right tabular-nums" />
              </tr>
              <tr className="font-bold">
                <td colSpan={6} className="text-right">{t('closingBalance')}</td>
                <td className="text-right tabular-nums">{formatTHB(gl.data.closingBalance)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </>
  );
}
