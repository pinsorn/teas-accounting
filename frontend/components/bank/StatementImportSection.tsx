'use client';

import { useRef, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { useStatementImports, useUploadStatement } from '@/lib/queries';

// Bank reconciliation (specs/bank-reconciliation.md B2.7) — statement import list + upload
// modal, mirrors AttachmentsSection.tsx's DaisyUI .modal-box pattern. The password field only
// applies to a future .pdf adapter (B3); the CSV adapter ignores it.
export function StatementImportSection({ bankAccountId }: { bankAccountId: number }) {
  const t = useTranslations('bank');
  const tc = useTranslations('common');
  const q = useStatementImports(bankAccountId);
  const upload = useUploadStatement(bankAccountId);
  const [open, setOpen] = useState(false);
  const [fileName, setFileName] = useState('');
  const [password, setPassword] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);
  const items = q.data ?? [];

  async function submit() {
    const f = fileRef.current?.files?.[0];
    if (!f) { toast.error(t('importFileRequired')); return; }
    const fd = new FormData();
    fd.set('file', f);
    if (password.trim()) fd.set('password', password.trim());
    try {
      const res = await upload.mutateAsync(fd);
      toast.success(t('importSuccess', { count: res.lineCount }));
      if (res.overlapWarning) toast.warning(t('importOverlapWarning'));
      setOpen(false);
      setPassword('');
      setFileName('');
      if (fileRef.current) fileRef.current.value = '';
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tc('error'));
    }
  }

  return (
    <section className="mt-8" data-testid="statement-imports-section">
      <div className="mb-2 flex items-center gap-3">
        <h2 className="font-semibold">
          {t('imports')} (<span data-testid="import-count">{items.length}</span>)
        </h2>
        <button className="btn btn-primary btn-xs" data-testid="import-upload-open"
          onClick={() => setOpen(true)}>+ {t('importUpload')}</button>
      </div>

      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table table-sm">
          <tbody>
            {q.isLoading && (
              <tr><td className="py-4 text-center text-base-content/50">{tc('loading')}</td></tr>
            )}
            {!q.isLoading && items.length === 0 && (
              <tr><td className="py-4 text-center text-base-content/50">{tc('empty')}</td></tr>
            )}
            {items.map((imp) => (
              <tr key={imp.statementImportId} data-testid="import-row">
                <td>
                  <Link href={`/bank-accounts/${bankAccountId}/imports/${imp.statementImportId}`}
                    className="link link-primary font-medium">
                    {imp.sourceFileName}
                  </Link>
                  <span className="ml-2 text-xs text-base-content/60">
                    {imp.periodStart} — {imp.periodEnd} · {imp.lineCount} {t('importLines')}
                  </span>
                </td>
                <td className="text-right whitespace-nowrap">
                  <span className={imp.status === 'Parsed' ? 'text-status-success' : 'text-status-danger'}>
                    {imp.status}
                  </span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {open && (
        <div className="modal modal-open">
          <div className="modal-box">
            <h3 className="mb-3 text-lg font-bold">{t('importUpload')}</h3>
            <p className="mb-2 text-xs text-base-content/60">{t('importFormatHint')}</p>
            <div className="flex flex-col gap-3">
              <input ref={fileRef} type="file" data-testid="import-file"
                className="file-input file-input-bordered file-input-sm"
                accept=".csv,.pdf"
                onChange={(e) => setFileName(e.target.files?.[0]?.name ?? '')} />
              {fileName.toLowerCase().endsWith('.pdf') && (
                <label className="form-control">
                  <span className="label-text text-xs">{t('importPassword')}</span>
                  <input type="password" className="input input-bordered input-sm" value={password}
                    data-testid="import-password"
                    onChange={(e) => setPassword(e.target.value)} />
                </label>
              )}
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setOpen(false)}>
                {tc('cancel')}
              </button>
              <button className="btn btn-primary btn-sm" data-testid="import-submit"
                disabled={upload.isPending} onClick={submit}>
                {t('importUpload')}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
