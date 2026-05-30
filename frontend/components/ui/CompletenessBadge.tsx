'use client';

import { useTranslations } from 'next-intl';
import { AlertTriangle } from 'lucide-react';
import type { CompletenessMissingCode } from '@/lib/types';

// purchase-completeness Phase 3 — advisory (NON-BLOCKING) completeness UI.
// Backend computes completeness for POSTED docs only; callers gate on status.
//
// `CompletenessChips` (detail header): "ไม่สมบูรณ์" badge + one reason chip per
// missing code, with friendly TH (primary) + EN labels from the `completeness`
// i18n namespace.
// `IncompleteFlag` (list rows): a compact "ไม่สมบูรณ์" indicator when isComplete=false.

export function CompletenessChips({ missing }: { missing: CompletenessMissingCode[] }) {
  const t = useTranslations('completeness');
  if (!missing || missing.length === 0) return null;
  return (
    <div className="flex flex-wrap items-center gap-2" data-testid="completeness-chips">
      <span className="badge badge-warning gap-1">
        <AlertTriangle className="h-3 w-3" aria-hidden /> {t('incomplete')}
      </span>
      {missing.map((code) => (
        <span key={code} className="badge badge-outline badge-warning text-xs"
          title={t(`missing.${code}`)}>
          {t(`missing.${code}`)}
        </span>
      ))}
    </div>
  );
}

export function IncompleteFlag({ isComplete }: { isComplete: boolean }) {
  const t = useTranslations('completeness');
  if (isComplete) return null;
  return (
    <span className="badge badge-warning badge-sm gap-1" data-testid="incomplete-flag"
      title={t('incomplete')}>
      <AlertTriangle className="h-3 w-3" aria-hidden /> {t('incomplete')}
    </span>
  );
}
