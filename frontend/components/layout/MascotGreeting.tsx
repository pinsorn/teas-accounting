import Image from 'next/image';
import Link from 'next/link';
import { ArrowRight } from 'lucide-react';

// Sprint 13j-FE — dashboard hero greeting with brand mascot.
// Copy + CTA are props (no mocked sales figures — §6: use real data only;
// the dashboard passes whatever real summary it has, or omits `subtitle`).
export function MascotGreeting({
  title = 'พร้อมทำงานวันที่ดี ๆ แล้วครับ',
  subtitle,
  cta,
}: {
  title?: string;
  subtitle?: string;
  cta?: { label: string; href: string };
}) {
  return (
    <div className="mb-6 flex items-center gap-4 rounded-card border border-ink-100 bg-peach-50 px-5 py-4">
      <span className="grid h-20 w-20 shrink-0 place-items-center overflow-hidden rounded-full bg-gradient-to-br from-peach-100 to-peach-50 shadow-[inset_0_0_0_2px_rgba(232,168,124,0.3)]">
        <Image
          src="/teas-mascot.png"
          alt="TEAS"
          width={80}
          height={80}
          className="h-full w-full scale-[1.4] object-cover object-[center_30%]"
        />
      </span>
      <div className="min-w-0 flex-1">
        <h2 className="text-lg font-bold text-ink-900">{title}</h2>
        {subtitle && <p className="mt-1 text-[13.5px] text-ink-600">{subtitle}</p>}
      </div>
      {cta && (
        <Link
          href={cta.href}
          className="inline-flex shrink-0 items-center gap-1.5 text-[13.5px] font-semibold text-peach-700 hover:text-peach-600"
        >
          {cta.label}
          <ArrowRight className="h-4 w-4" aria-hidden />
        </Link>
      )}
    </div>
  );
}
