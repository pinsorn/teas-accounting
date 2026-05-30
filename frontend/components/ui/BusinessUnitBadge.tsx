'use client';

import { useTranslations } from 'next-intl';
import { useBusinessUnits } from '@/lib/queries';

// Sprint BU-PURCH — compact read-only chip showing the document's business unit.
// PV/VI detail carry code+name directly; PO detail carries only the id, so we
// resolve code/name from the active BU list. Renders nothing when no BU is set.
export function BusinessUnitBadge({
  businessUnitId,
  code,
  name,
}: {
  businessUnitId: number | null | undefined;
  code?: string | null;
  name?: string | null;
}) {
  const t = useTranslations('businessUnit');
  const { data: units = [] } = useBusinessUnits();

  if (businessUnitId == null) return null;

  const resolved = code ? null : units.find((u) => u.businessUnitId === businessUnitId);
  const shownCode = code ?? resolved?.code ?? `#${businessUnitId}`;
  const shownName = name ?? resolved?.nameTh ?? '';

  return (
    <span className="badge badge-outline gap-1" data-testid="bu-badge">
      {t('title')}: {shownCode}{shownName ? ` — ${shownName}` : ''}
    </span>
  );
}
