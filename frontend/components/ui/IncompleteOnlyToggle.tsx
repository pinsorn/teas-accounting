'use client';

import { useRouter, useSearchParams } from 'next/navigation';
import { useTranslations } from 'next-intl';

// purchase-completeness Phase 3 — list filter toggle. URL-persisted (`incompleteOnly`),
// which the PV/VI list pages forward to the server query (?incompleteOnly=true) to
// return only POSTED docs whose isComplete===false.
export function IncompleteOnlyToggle() {
  const t = useTranslations('completeness');
  const router = useRouter();
  const params = useSearchParams();
  const on = params.get('incompleteOnly') === 'true';

  function toggle(next: boolean) {
    const sp = new URLSearchParams(params.toString());
    if (next) sp.set('incompleteOnly', 'true');
    else sp.delete('incompleteOnly');
    router.replace(sp.toString() ? `?${sp.toString()}` : '?');
  }

  return (
    <label className="mb-4 flex w-fit cursor-pointer items-center gap-2 text-sm">
      <input
        type="checkbox"
        className="toggle toggle-warning toggle-sm"
        data-testid="incomplete-only-toggle"
        checked={on}
        onChange={(e) => toggle(e.target.checked)}
      />
      <span>{t('incompleteOnly')}</span>
    </label>
  );
}
