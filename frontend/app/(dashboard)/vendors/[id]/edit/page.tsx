'use client';

import { useParams } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { VendorForm } from '@/components/forms/VendorForm';
import { useVendor } from '@/lib/queries';

export default function VendorEditPage() {
  const id = Number(useParams<{ id: string }>().id);
  const tc = useTranslations('common');
  const q = useVendor(id);

  if (q.isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
  if (q.isError || !q.data) return <p className="text-error">{tc('error')}</p>;

  return <VendorForm edit={q.data} />;
}
