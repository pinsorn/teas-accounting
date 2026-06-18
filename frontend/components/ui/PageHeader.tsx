import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';
import type { ReactNode } from 'react';

export function PageHeader({
  title,
  subtitle,
  actions,
  backHref,
}: {
  title: string;
  subtitle?: string;
  actions?: ReactNode;
  backHref?: string;
}) {
  return (
    <div className="mb-6 flex items-end justify-between gap-4 border-b border-base-300 pb-4">
      <div>
        {backHref && (
          <Link href={backHref} className="mb-1 flex items-center gap-1 text-sm text-base-content/60 hover:text-base-content">
            <ArrowLeft className="h-3.5 w-3.5" aria-hidden /> Back
          </Link>
        )}
        <h1 className="text-2xl font-bold">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-base-content/60">{subtitle}</p>}
      </div>
      {actions && <div className="flex gap-2">{actions}</div>}
    </div>
  );
}
