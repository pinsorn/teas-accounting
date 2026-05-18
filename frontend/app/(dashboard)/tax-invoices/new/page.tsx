'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { PostConfirmDialog } from '@/components/ui/PostConfirmDialog';
import { CustomerSelector } from '@/components/ui/CustomerSelector';
import { DateInput } from '@/components/ui/DateInput';
import { LineItemsTable, EMPTY_LINE, type LineItem } from '@/components/ui/LineItemsTable';
import { BusinessUnitSelector } from '@/components/ui/BusinessUnitSelector';
import { useCreateTaxInvoice, usePostTaxInvoice, useCompanyBuSetting } from '@/lib/queries';
import { bangkokToday, formatTHB } from '@/lib/utils';

const lineSchema = z.object({
  descriptionTh: z.string().min(1),
  quantity: z.number().positive(),
  unitPrice: z.number().min(0),
  taxRate: z.number().min(0).max(1),
});
const schema = z.object({
  customerId: z.number().int().positive(),
  lines: z.array(lineSchema).min(1),
});
type FormValues = z.infer<typeof schema>;

export default function CreateTaxInvoicePage() {
  const router = useRouter();
  const t = useTranslations('ti.form');
  const tc = useTranslations('common');
  const docDate = bangkokToday(); // server is authoritative; UI locked (CLAUDE.md §10)

  const create = useCreateTaxInvoice();
  const post = usePostTaxInvoice();
  const buSetting = useCompanyBuSetting();
  const buRequired = buSetting.data?.requiresBusinessUnit ?? false;
  const [confirm, setConfirm] = useState<{ id: number } | null>(null);
  const [customerLabel, setCustomerLabel] = useState('');
  const [businessUnitId, setBusinessUnitId] = useState<number | null>(null);

  const {
    control,
    handleSubmit,
    watch,
    formState: { isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { customerId: 0, lines: [{ ...EMPTY_LINE }] },
  });

  const lines = watch('lines');
  const subtotal = lines.reduce((s, l) => s + l.unitPrice * l.quantity, 0);
  const vat = lines.reduce((s, l) => s + l.unitPrice * l.quantity * l.taxRate, 0);

  async function saveDraft(v: FormValues): Promise<number | null> {
    if (buRequired && businessUnitId === null) {
      toast.error(tc('error'));
      return null;
    }
    try {
      const res = await create.mutateAsync({
        docDate,
        customerId: v.customerId,
        businessUnitId,
        isTaxInclusive: false,
        currencyCode: 'THB',
        exchangeRate: 1,
        notes: null,
        paymentTerms: null,
        dueDate: null,
        lines: v.lines.map((l) => ({
          productId: null,
          productCode: null,
          descriptionTh: l.descriptionTh,
          quantity: l.quantity,
          uomId: 1,
          uomText: 'หน่วย',
          unitPrice: l.unitPrice,
          discountPercent: 0,
          taxCodeId: 1,
          taxCode: 'V7',
          taxRate: l.taxRate,
        })),
      });
      toast.success('Draft saved');
      return res.tax_invoice_id;
    } catch {
      toast.error(tc('error'));
      return null;
    }
  }

  return (
    <>
      <PageHeader title={t('post')} subtitle={`${t('docDate')}: ${docDate}`} />

      <form
        className="space-y-6"
        onSubmit={handleSubmit(async (v) => {
          const id = await saveDraft(v);
          if (id) router.push('/tax-invoices');
        })}
      >
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Controller
            control={control}
            name="customerId"
            render={({ field, fieldState }) => (
              <div>
                <CustomerSelector
                  value={field.value || null}
                  onChange={(id, label) => {
                    field.onChange(id);
                    setCustomerLabel(label);
                  }}
                />
                {fieldState.error && (
                  <span className="text-error text-sm">เลือกลูกค้า</span>
                )}
              </div>
            )}
          />
          <DateInput value={docDate} locked label={t('docDate')} />
          <BusinessUnitSelector value={businessUnitId} onChange={setBusinessUnitId} required={buRequired} />
        </div>

        <Controller
          control={control}
          name="lines"
          render={({ field }) => (
            <LineItemsTable
              value={field.value as LineItem[]}
              onChange={field.onChange}
            />
          )}
        />

        <div className="flex items-end justify-between">
          <dl className="w-64 space-y-1 text-sm">
            <div className="flex justify-between">
              <dt className="text-base-content/60">Subtotal</dt>
              <dd className="tabular-nums">{formatTHB(subtotal)}</dd>
            </div>
            <div className="flex justify-between">
              <dt className="text-base-content/60">VAT</dt>
              <dd className="tabular-nums">{formatTHB(vat)}</dd>
            </div>
            <div className="flex justify-between font-bold">
              <dt>Total</dt>
              <dd className="tabular-nums">{formatTHB(subtotal + vat)}</dd>
            </div>
          </dl>
          <div className="flex gap-2">
            <button type="submit" className="btn btn-ghost" disabled={isSubmitting}>
              {t('saveDraft')}
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={isSubmitting}
              onClick={handleSubmit(async (v) => {
                const id = await saveDraft(v);
                if (id) setConfirm({ id });
              })}
            >
              {t('post')}
            </button>
          </div>
        </div>
      </form>

      <PostConfirmDialog
        open={confirm !== null}
        busy={post.isPending}
        summary={{ customer: customerLabel || `#${watch('customerId')}`, total: subtotal + vat, vat }}
        recipients={[]}
        onClose={() => setConfirm(null)}
        onConfirm={async () => {
          if (!confirm) return;
          try {
            await post.mutateAsync(confirm.id);
            toast.success('Posted');
            router.push(`/tax-invoices/${confirm.id}`);
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
