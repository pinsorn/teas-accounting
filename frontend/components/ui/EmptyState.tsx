import Image from 'next/image';
import Link from 'next/link';
import type { ReactNode } from 'react';

// Sprint 13j-FE — empty/zero-result/error fallback with brand mascot.
// Used on empty list pages, empty search results, and error fallbacks.
export function EmptyState({
  title,
  description,
  cta,
  action,
}: {
  title: string;
  description?: string;
  cta?: { label: string; href: string };
  action?: ReactNode;
}) {
  return (
    <div className="flex flex-col items-center justify-center px-5 py-16 text-center">
      <span className="mb-3.5 grid h-[120px] w-[120px] place-items-center overflow-hidden rounded-full bg-gradient-to-br from-peach-100 to-peach-50 shadow-[inset_0_0_0_2px_rgba(232,168,124,0.3)]">
        <Image
          src="/teas-mascot.png"
          alt=""
          width={120}
          height={120}
          aria-hidden
          className="h-full w-full scale-[1.4] object-cover object-[center_30%]"
        />
      </span>
      <h3 className="mb-1.5 text-lg font-bold text-ink-900">{title}</h3>
      {description && <p className="mb-[18px] max-w-md text-[13.5px] text-ink-600">{description}</p>}
      {cta && (
        <Link href={cta.href} className="btn btn-primary btn-sm">
          {cta.label}
        </Link>
      )}
      {action}
    </div>
  );
}
