import type { LucideIcon } from 'lucide-react';

export function StatCard({
  label,
  value,
  icon: Icon,
  tone = 'default',
}: {
  label: string;
  value: string;
  icon?: LucideIcon;
  tone?: 'default' | 'success' | 'warning' | 'error';
}) {
  const toneCls =
    tone === 'success' ? 'text-success'
    : tone === 'warning' ? 'text-warning'
    : tone === 'error' ? 'text-error'
    : 'text-primary';
  return (
    <div className="card bg-base-100 shadow-sm">
      <div className="card-body p-5">
        <div className="flex items-center justify-between">
          <span className="text-sm text-base-content/60">{label}</span>
          {Icon && <Icon className={`h-5 w-5 ${toneCls}`} aria-hidden />}
        </div>
        <div className={`mt-2 text-2xl font-bold tabular-nums ${toneCls}`}>{value}</div>
      </div>
    </div>
  );
}
