'use client';

import { useTranslations } from 'next-intl';
import { FileText, TrendingUp, Receipt, ListChecks } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { StatCard } from '@/components/ui/StatCard';
import { MascotGreeting } from '@/components/layout/MascotGreeting';
import { useNumberGaps, useVatThresholdStatus } from '@/lib/queries';
import { formatTHB } from '@/lib/utils';

export default function DashboardPage() {
  const t = useTranslations('dashboard');
  // Number-gap count is real (compliance hero); sales/VAT/TI-count are placeholders
  // until their report endpoints exist (Answer-Backend2 §3 — placeholder ok).
  const gaps = useNumberGaps();
  const gapCount = gaps.data?.gaps.length ?? 0;
  // Sprint 8.5 — ม.85/1 VAT-registration threshold warning (non-VAT companies).
  const threshold = useVatThresholdStatus().data?.status;

  return (
    <>
      <MascotGreeting cta={{ label: 'ดูสรุปภาพรวม', href: '/reports/sales-summary' }} />
      <PageHeader title={t('title')} subtitle={t('subtitle')} />
      {threshold === 'Exceeded' && (
        <div role="alert" className="alert alert-error mb-4">
          {t('vatThreshold.exceeded')}
        </div>
      )}
      {threshold === 'Approaching' && (
        <div role="alert" className="alert alert-warning mb-4">
          {t('vatThreshold.approaching')}
        </div>
      )}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard label={t('stat.tiThisMonth')} value="—" icon={FileText} />
        <StatCard label={t('stat.salesThisMonth')} value={formatTHB(0)} icon={TrendingUp} />
        <StatCard label={t('stat.outputVat')} value={formatTHB(0)} icon={Receipt} />
        <StatCard
          label={t('stat.numberGaps')}
          value={gaps.isLoading ? '…' : String(gapCount)}
          icon={ListChecks}
          tone={gapCount > 0 ? 'error' : 'success'}
        />
      </div>
    </>
  );
}
