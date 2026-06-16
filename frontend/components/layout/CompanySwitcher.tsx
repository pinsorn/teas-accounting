'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { Building2, ChevronDown, Check, Loader2 } from 'lucide-react';

// Onboarding-switcher spec (2026-06-16) — super-admin company switcher.
// Visible ONLY when /me reports isSuperAdmin. Lists allowedCompanies; on select,
// POSTs to the dedicated /api/auth/switch-company route (which re-issues the JWT
// + overwrites the httpOnly cookie), then hard-navigates to / so every Server
// Component + React Query refetches under the new company context.
// Resilient: hides itself if /me fails or the caller is not a super-admin.

type AllowedCompany = { id: number; nameTh: string; nameEn: string | null };
type Me = {
  companyId: number;
  isSuperAdmin: boolean;
  companyName: string | null;
  allowedCompanies: AllowedCompany[];
};

export function CompanySwitcher() {
  const t = useTranslations('topbar');
  const [me, setMe] = useState<Me | null>(null);
  const [switching, setSwitching] = useState(false);

  useEffect(() => {
    let alive = true;
    fetch('/api/proxy/me', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => {
        if (alive) setMe(data);
      })
      .catch(() => {
        if (alive) setMe(null);
      });
    return () => {
      alive = false;
    };
  }, []);

  if (!me || !me.isSuperAdmin || me.allowedCompanies.length === 0) return null;

  const current =
    me.companyName ??
    me.allowedCompanies.find((c) => c.id === me.companyId)?.nameTh ??
    t('noCompany');

  async function switchTo(id: number) {
    if (switching || id === me?.companyId) return;
    setSwitching(true);
    try {
      const res = await fetch('/api/auth/switch-company', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ companyId: id }),
      });
      if (!res.ok) {
        setSwitching(false);
        return;
      }
      // Hard navigation so RSC + React Query refetch under the new company.
      window.location.assign('/');
    } catch {
      setSwitching(false);
    }
  }

  return (
    <div className="dropdown dropdown-end">
      <button
        tabIndex={0}
        role="button"
        className="flex h-[34px] max-w-[220px] items-center gap-2 rounded-lg border border-ink-100 bg-base-100 px-3 text-[13px] text-ink-700 hover:bg-base-300 disabled:opacity-60"
        title={t('switchCompany')}
        aria-label={t('switchCompany')}
        disabled={switching}
      >
        {switching ? (
          <Loader2 className="h-4 w-4 shrink-0 animate-spin" aria-hidden />
        ) : (
          <Building2 className="h-4 w-4 shrink-0 text-peach-600" aria-hidden />
        )}
        <span className="truncate font-medium">{current}</span>
        <ChevronDown className="h-3.5 w-3.5 shrink-0 text-ink-400" aria-hidden />
      </button>
      <ul
        tabIndex={0}
        className="dropdown-content menu z-[60] mt-2 max-h-[60vh] w-72 overflow-y-auto rounded-box border border-ink-100 bg-base-100 p-2 shadow-warm-lg"
      >
        <li className="menu-title px-3 pb-1 text-[11px] uppercase tracking-wide text-ink-400">
          {t('switchCompany')}
        </li>
        {me.allowedCompanies.map((c) => {
          const active = c.id === me.companyId;
          return (
            <li key={c.id}>
              <button
                type="button"
                onClick={() => switchTo(c.id)}
                className={active ? 'active font-semibold' : ''}
                aria-current={active ? 'true' : undefined}
              >
                <span className="flex min-w-0 flex-1 flex-col text-left">
                  <span className="truncate text-[13px] text-ink-900">{c.nameTh}</span>
                  {c.nameEn && (
                    <span className="truncate text-[11px] text-ink-400">{c.nameEn}</span>
                  )}
                </span>
                {active && <Check className="h-4 w-4 shrink-0 text-peach-600" aria-hidden />}
              </button>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
