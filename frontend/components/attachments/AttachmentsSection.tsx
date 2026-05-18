'use client';

import { useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import {
  useAttachments, useUploadAttachment, useDeleteAttachment,
  attachmentDownloadUrl,
} from '@/lib/queries';

const CATEGORIES = [
  'TAX_INVOICE', 'RECEIPT', 'PURCHASE_ORDER', 'DELIVERY_ORDER', 'QUOTATION',
  'WHT_CERT_50TAWI', 'BANK_SLIP', 'CONTRACT', 'EXPENSE_CLAIM_FORM',
  'CUSTOMS_DECL', 'OTHER',
] as const;

function fmtSize(b: number) {
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${Math.round(b / 1024)} KB`;
  return `${(b / 1024 / 1024).toFixed(1)} MB`;
}

export function AttachmentsSection({
  parentType, parentId,
}: { parentType: string; parentId: number }) {
  const t = useTranslations('attachment');
  const tc = useTranslations('common');
  const q = useAttachments(parentType, parentId);
  const upload = useUploadAttachment();
  const del = useDeleteAttachment();
  const [open, setOpen] = useState(false);
  const [category, setCategory] = useState<string>('OTHER');
  const [description, setDescription] = useState('');
  const fileRef = useRef<HTMLInputElement>(null);
  const items = q.data?.items ?? [];

  async function submit() {
    const f = fileRef.current?.files?.[0];
    if (!f) { toast.error(t('dropHere')); return; }
    if (category === 'OTHER' && !description.trim()) {
      toast.error(t('descRequired')); return;
    }
    const fd = new FormData();
    fd.set('parent_type', parentType);
    fd.set('parent_id', String(parentId));
    fd.set('category', category);
    if (description.trim()) fd.set('description', description.trim());
    fd.set('file', f);
    try {
      await upload.mutateAsync(fd);
      toast.success(t('uploaded'));
      setOpen(false); setDescription('');
      if (fileRef.current) fileRef.current.value = '';
    } catch (e) {
      toast.error(e instanceof Error ? e.message : tc('error'));
    }
  }

  return (
    <section className="mt-8" data-testid="attachments-section">
      <div className="mb-2 flex items-center gap-3">
        <h2 className="font-semibold">
          📎 {t('title')} (<span data-testid="att-count">{items.length}</span>)
        </h2>
        <button className="btn btn-primary btn-xs" data-testid="att-upload-open"
          onClick={() => setOpen(true)}>+ {t('upload')}</button>
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
            {items.map((a) => (
              <tr key={a.attachmentId} data-testid="att-row">
                <td>
                  <span className="badge badge-ghost badge-sm mr-2">
                    {t(`category.${a.category}`)}
                  </span>
                  <span className="font-medium">{a.fileName}</span>
                  <span className="ml-2 text-xs text-base-content/60">
                    {fmtSize(a.sizeBytes)} · {a.uploadedByName} ·{' '}
                    {a.uploadedAt.slice(0, 10)}
                  </span>
                  {a.description && (
                    <div className="text-xs text-base-content/60">{a.description}</div>
                  )}
                </td>
                <td className="text-right whitespace-nowrap">
                  <a className="btn btn-ghost btn-xs" href={attachmentDownloadUrl(a.attachmentId)}
                    target="_blank" rel="noreferrer">{t('download')}</a>
                  <button className="btn btn-ghost btn-xs text-error"
                    data-testid="att-delete"
                    onClick={async () => {
                      if (!window.confirm(t('deleteConfirm'))) return;
                      try { await del.mutateAsync(a.attachmentId); toast.success(tc('save')); }
                      catch (e) { toast.error(e instanceof Error ? e.message : tc('error')); }
                    }}>{t('delete')}</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {open && (
        <div className="modal modal-open">
          <div className="modal-box">
            <h3 className="mb-3 text-lg font-bold">{t('upload')}</h3>
            <div className="flex flex-col gap-3">
              <input ref={fileRef} type="file" data-testid="att-file"
                className="file-input file-input-bordered file-input-sm"
                accept=".pdf,.jpg,.jpeg,.png,.webp,.xls,.xlsx,.msg" />
              <span className="text-xs text-base-content/60">
                {t('maxSize')} · {t('allowedTypes')}
              </span>
              <label className="form-control">
                <span className="label-text text-xs">{t('categoryLabel')}</span>
                <select className="select select-bordered select-sm" value={category}
                  data-testid="att-category"
                  onChange={(e) => setCategory(e.target.value)}>
                  {CATEGORIES.map((c) => (
                    <option key={c} value={c}>{t(`category.${c}`)}</option>
                  ))}
                </select>
              </label>
              <label className="form-control">
                <span className="label-text text-xs">{t('description')}</span>
                <input className="input input-bordered input-sm" value={description}
                  data-testid="att-desc"
                  onChange={(e) => setDescription(e.target.value)} />
              </label>
            </div>
            <div className="modal-action">
              <button className="btn btn-ghost btn-sm" onClick={() => setOpen(false)}>
                {tc('cancel')}
              </button>
              <button className="btn btn-primary btn-sm" data-testid="att-submit"
                disabled={upload.isPending} onClick={submit}>
                {t('upload')}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
