import { SidebarNav } from '@/components/app-shell/SidebarNav';

export default function DashboardLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen bg-base-100">
      <SidebarNav />
      <main className="flex-1 overflow-x-auto p-6">{children}</main>
    </div>
  );
}
