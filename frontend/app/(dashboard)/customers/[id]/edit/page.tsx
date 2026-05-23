'use client';

import { use } from 'react';
import { useTranslations } from 'next-intl';
import { CustomerForm } from '@/components/forms/CustomerForm';
import { useCustomer } from '@/lib/queries';

export default function EditCustomerPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const tc = useTranslations('common');
  const q = useCustomer(Number(id));

  if (q.isLoading) return <div className="p-6 text-ink-400">{tc('loading')}</div>;
  if (q.isError || !q.data) return <div className="p-6 text-status-danger">{tc('error')}</div>;

  return <CustomerForm edit={q.data} />;
}
