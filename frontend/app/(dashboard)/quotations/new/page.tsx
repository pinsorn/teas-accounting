'use client';

import { useTranslations } from 'next-intl';
import { ShieldAlert } from 'lucide-react';
import { useMePermissions } from '@/lib/queries';
import { QuotationForm } from '@/components/forms/QuotationForm';

const SCOPE = 'sales.quotation.manage';

export default function NewQuotationPage() {
  const tc = useTranslations('common');
  const perms = useMePermissions();
  const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
  if (perms.data && !canCreate) {
    return (
      <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
        <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
        <div className="font-semibold">{tc('noAccessTitle')}</div>
        <div className="max-w-md text-sm text-base-content/60">{tc('noAccessBody', { perm: SCOPE })}</div>
      </div>
    );
  }
  return <QuotationForm />;
}
