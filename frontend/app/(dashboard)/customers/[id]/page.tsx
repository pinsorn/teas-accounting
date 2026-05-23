'use client';

import { use } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Pencil, Power, PowerOff } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { useCustomer, useUpdateCustomer } from '@/lib/queries';
import { formatTaxId, formatTHB } from '@/lib/utils';
import { useConfirm } from '@/hooks/useConfirm';
import type { ReactNode } from 'react';

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-0.5 py-2">
      <dt className="text-xs font-semibold uppercase tracking-wide text-ink-500">{label}</dt>
      <dd className="text-[15px] text-ink-900">{children || <span className="text-ink-300">—</span>}</dd>
    </div>
  );
}

export default function CustomerDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const cid = Number(id);
  const t = useTranslations('cust');
  const tc = useTranslations('common');
  const confirm = useConfirm();
  const q = useCustomer(cid);
  const update = useUpdateCustomer(cid);
  const d = q.data;

  if (q.isLoading) return <div className="p-6 text-ink-400">{tc('loading')}</div>;
  if (q.isError || !d) return <div className="p-6 text-status-danger">{tc('error')}</div>;

  async function toggleActive() {
    if (!d) return;
    const goingOff = d.isActive;
    const ok = await confirm({
      description: goingOff ? t('deactivateConfirm') : t('activateConfirm'),
      variant: goingOff ? 'destructive' : 'default',
    });
    if (!ok) return;
    try {
      await update.mutateAsync({
        nameTh: d.nameTh, nameEn: d.nameEn, taxId: d.taxId, branchCode: d.branchCode,
        branchName: d.branchName, vatRegistered: d.vatRegistered, billingAddress: d.billingAddress,
        contactPerson: d.contactPerson, phone: d.phone, email: d.email, creditLimit: d.creditLimit,
        paymentTermDays: d.paymentTermDays, defaultCurrency: d.defaultCurrency, isActive: !d.isActive,
      });
      toast.success(tc('save'));
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  return (
    <>
      <PageHeader
        title={d.nameTh}
        subtitle={d.customerCode}
        actions={
          <>
            <Link href={`/customers/${cid}/edit`} className="btn btn-secondary btn-sm gap-1">
              <Pencil className="h-4 w-4" aria-hidden /> {t('edit')}
            </Link>
            <button
              className={`btn btn-sm gap-1 ${d.isActive ? 'btn-danger' : 'btn-primary'}`}
              disabled={update.isPending}
              onClick={toggleActive}
            >
              {d.isActive ? <PowerOff className="h-4 w-4" aria-hidden /> : <Power className="h-4 w-4" aria-hidden />}
              {d.isActive ? t('deactivate') : t('activate')}
            </button>
          </>
        }
      />

      <div className="mb-4 flex items-center gap-3">
        {d.isActive ? (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-status-success-bg px-2.5 py-0.5 text-xs font-semibold text-status-success">
            <span className="h-1.5 w-1.5 rounded-full bg-current" />{tc('active')}
          </span>
        ) : (
          <span className="inline-flex items-center gap-1.5 rounded-full bg-status-draft-bg px-2.5 py-0.5 text-xs font-semibold text-status-draft">
            <span className="h-1.5 w-1.5 rounded-full bg-current" />{tc('inactive')}
          </span>
        )}
        <span className="text-ink-600">{d.customerType === 'Individual' ? t('individual') : t('corporate')}</span>
        {d.vatRegistered && (
          <span className="rounded-full bg-status-success-bg px-2 py-0.5 text-xs font-semibold text-status-success">VAT</span>
        )}
      </div>

      <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <h2 className="mb-2 text-sm font-bold text-ink-900">{t('secTax')}</h2>
          <dl className="divide-y divide-ink-100">
            <Row label={t('nameEn')}>{d.nameEn}</Row>
            <Row label={t('taxId')}><span className="font-mono">{formatTaxId(d.taxId)}</span></Row>
            <Row label={t('branchCode')}><span className="font-mono">{d.branchCode}</span></Row>
            <Row label={t('branchName')}>{d.branchName}</Row>
          </dl>
        </section>

        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm">
          <h2 className="mb-2 text-sm font-bold text-ink-900">{t('secContact')}</h2>
          <dl className="divide-y divide-ink-100">
            <Row label={t('billingAddress')}>{d.billingAddress}</Row>
            <Row label={t('contactPerson')}>{d.contactPerson}</Row>
            <Row label={t('phone')}>{d.phone}</Row>
            <Row label={t('email')}>{d.email}</Row>
          </dl>
        </section>

        <section className="rounded-card border border-ink-100 bg-base-100 p-5 shadow-warm-sm lg:col-span-2">
          <h2 className="mb-2 text-sm font-bold text-ink-900">{t('secTerms')}</h2>
          <dl className="grid grid-cols-1 gap-x-8 sm:grid-cols-3">
            <Row label={t('creditLimit')}><span className="tabular-nums">{formatTHB(d.creditLimit)}</span></Row>
            <Row label={t('paymentTermDays')}><span className="tabular-nums">{d.paymentTermDays}</span></Row>
            <Row label={t('currency')}>{d.defaultCurrency}</Row>
          </dl>
        </section>
      </div>
    </>
  );
}
