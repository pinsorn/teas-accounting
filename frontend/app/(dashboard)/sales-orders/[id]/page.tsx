'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { PageHeader } from '@/components/ui/PageHeader';
import { useSalesOrder, usePostSalesOrder, useCreateDeliveryOrder } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';
import { AttachmentsSection } from '@/components/attachments/AttachmentsSection';

export default function SalesOrderDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const soId = Number(id);
  const t = useTranslations('salesOrder');
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useSalesOrder(soId);
  const post = usePostSalesOrder();
  const makeDo = useCreateDeliveryOrder();
  const d = q.data;

  async function doPost() {
    try { await post.mutateAsync(soId); toast.success(tc('save')); }
    catch (e) { toast.error((e as { detail?: string })?.detail ?? tc('error')); }
  }

  async function createDelivery() {
    if (!d) return;
    try {
      const r = await makeDo.mutateAsync({
        soId,
        req: {
          docDate: new Date().toISOString().slice(0, 10),
          customerId: d.customerId, businessUnitId: d.businessUnitId,
          isCombinedWithTi: true, notes: null, fromSalesOrderId: soId,
          lines: d.lines.map((l) => ({
            salesOrderLineId: null, productId: l.productId,
            descriptionTh: l.descriptionTh, quantity: l.quantity,
            uomText: l.uomText, unitPrice: l.unitPrice, discountPercent: 0,
            taxCodeId: 1, taxCode: 'VAT7', taxRate: 0.07,
          })),
        },
      }) as { delivery_order_id: number };
      toast.success(tc('save'));
      router.push(`/delivery-orders/${r.delivery_order_id}`);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  return (
    <>
      <PageHeader title={`${t('listTitle')} ${d.docNo ?? `#${d.salesOrderId}`}`} />
      <div className="mb-4 flex flex-wrap items-center gap-3">
        <span data-testid="so-status" className="badge badge-lg badge-ghost">{d.status}</span>
        <span>{d.customerName}</span>
        {d.status === 'Draft' && (
          <button data-testid="so-post" className="btn btn-primary btn-sm"
            disabled={post.isPending} onClick={doPost}>{t('post')}</button>
        )}
        {d.status === 'Posted' && (
          <button data-testid="so-create-do" className="btn btn-secondary btn-sm"
            disabled={makeDo.isPending} onClick={createDelivery}>{t('createDo')}</button>
        )}
      </div>
      <div className="overflow-x-auto rounded-lg border border-base-300">
        <table className="table">
          <tbody>
            {d.lines.map((l) => (
              <tr key={l.lineNo}>
                <td>{l.descriptionTh}</td>
                <td className="text-right tabular-nums">{l.quantity}</td>
                <td className="text-right tabular-nums">{formatTHB(l.totalAmount)}</td>
              </tr>
            ))}
          </tbody>
          <tfoot><tr className="font-bold">
            <td colSpan={2} className="text-right">{t('total')}</td>
            <td className="text-right tabular-nums">{formatTHB(d.totalAmount)}</td>
          </tr></tfoot>
        </table>
      </div>
      <AttachmentsSection parentType="SALES_ORDER" parentId={soId} />
    </>
  );
}
