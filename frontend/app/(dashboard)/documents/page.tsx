'use client';

import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { ExternalLink, FileDown, FileText, Info } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { openPdf } from '@/lib/api';
import {
  RD_FORMS,
  RD_FORM_CATEGORIES,
  RD_FILING_CHANNELS,
  type RdForm,
  type RdFormTier,
} from '@/lib/rd-forms';

// /documents — central catalogue of the RD forms TEAS works with. Pure reference page:
// data is static (lib/rd-forms.ts), names + deadlines come from i18n, and the links point
// at the official RD site (Ham 2026-06-11: link the official URLs, don't serve committed PDFs).

const TIER_BADGE: Record<RdFormTier, string> = {
  1: 'badge-success',
  2: 'badge-ghost',
  3: 'badge-outline',
};

export default function DocumentsPage() {
  const t = useTranslations('documents');

  return (
    <>
      <PageHeader title={t('title')} />
      <p className="mb-4 max-w-3xl text-sm text-base-content/60">{t('subtitle')}</p>

      {/* Filing channels / portals */}
      <div className="mb-5 rounded-lg border border-base-300 p-4">
        <h2 className="mb-2 text-sm font-semibold">{t('channelsTitle')}</h2>
        <div className="flex flex-wrap gap-2">
          {RD_FILING_CHANNELS.map((c) => (
            <a
              key={c.code}
              href={c.url}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-sm btn-outline gap-1.5"
            >
              <ExternalLink className="h-3.5 w-3.5" aria-hidden />
              {t(`channel.${c.code}` as 'channel.efiling')}
            </a>
          ))}
        </div>
      </div>

      {RD_FORM_CATEGORIES.map((category) => {
        const forms = RD_FORMS.filter((f) => f.category === category);
        if (forms.length === 0) return null;
        return (
          <section key={category} className="mb-6">
            <h2 className="mb-2 font-semibold">{t(`category.${category}` as 'category.vat')}</h2>
            <div className="overflow-x-auto rounded-lg border border-base-300">
              <table className="table table-sm">
                <thead>
                  <tr>
                    <th className="whitespace-nowrap">{t('col.code')}</th>
                    <th>{t('col.name')}</th>
                    <th className="whitespace-nowrap">{t('col.frequency')}</th>
                    <th className="min-w-[14rem]">{t('col.deadline')}</th>
                    <th className="text-right">{t('col.links')}</th>
                  </tr>
                </thead>
                <tbody>
                  {forms.map((f) => (
                    <FormRow key={f.code} form={f} />
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        );
      })}

      <p className="flex items-start gap-1.5 text-xs text-base-content/50">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
        {t('officialNote')}
      </p>
    </>
  );
}

function FormRow({ form }: { form: RdForm }) {
  const t = useTranslations('documents');
  return (
    <tr>
      <td className="align-top">
        <div className="flex flex-col gap-1">
          <span className="whitespace-nowrap font-mono text-xs font-semibold">{form.rdCode}</span>
          <span
            className={`badge badge-xs ${TIER_BADGE[form.tier]} w-fit`}
            title={`${t('tierLabel')}: ${t(`tier.${form.tier}` as 'tier.1')}`}
          >
            {t(`tier.${form.tier}` as 'tier.1')}
          </span>
        </div>
      </td>
      <td className="align-top">{t(`form.${form.code}.name` as 'form.pp30.name')}</td>
      <td className="align-top">
        <span className="badge badge-ghost badge-sm whitespace-nowrap">
          {t(`frequency.${form.frequency}` as 'frequency.monthly')}
        </span>
      </td>
      <td className="align-top text-sm text-base-content/70">
        {t(`form.${form.code}.deadline` as 'form.pp30.deadline')}
      </td>
      <td className="text-right align-top">
        <div className="flex justify-end gap-1.5">
          {form.prefillPath && (
            <button
              type="button"
              className="btn btn-xs btn-secondary gap-1"
              onClick={() => void openPdf(form.prefillPath!).catch(() => toast.error(t('prefillError')))}
            >
              <FileDown className="h-3 w-3" aria-hidden />
              {t('prefill')}
            </button>
          )}
          {form.pdfUrl && (
            <a
              href={form.pdfUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-xs btn-primary gap-1"
            >
              <FileText className="h-3 w-3" aria-hidden />
              {t('openPdf')}
            </a>
          )}
          <a
            href={form.sourceUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-xs btn-ghost gap-1"
          >
            <ExternalLink className="h-3 w-3" aria-hidden />
            {form.pdfUrl ? t('openSource') : t('noPdf')}
          </a>
        </div>
      </td>
    </tr>
  );
}
