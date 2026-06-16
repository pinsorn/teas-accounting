import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';
import { SidebarNav } from '@/components/app-shell/SidebarNav';
import { Topbar } from '@/components/layout/Topbar';

const BACKEND = process.env.BACKEND_API_URL ?? 'http://localhost:5000';

// Onboarding-switcher spec (2026-06-16) — first-run gate. A super-admin who logged
// in with NO role assignment has companyId===0 (LoginService primary-scope = 0). Send
// them to the chrome-free /onboarding wizard (top-level route, OUTSIDE this gating
// layout — putting it under (dashboard) would re-trigger this redirect → infinite loop).
// Normal users are never redirected. We read the cookie + call backend /me directly
// (relative fetch fails server-side; this mirrors the BFF proxy's Bearer forwarding).
export default async function DashboardLayout({ children }: { children: React.ReactNode }) {
  const token = (await cookies()).get('access_token')?.value;
  let needsOnboarding = false;
  if (token) {
    try {
      const res = await fetch(`${BACKEND}/me`, {
        headers: { Authorization: `Bearer ${token}` },
        cache: 'no-store',
      });
      if (res.ok) {
        const me = await res.json();
        needsOnboarding = me?.isSuperAdmin === true && me?.companyId === 0;
      }
    } catch {
      // /me unreachable → don't block the dashboard; middleware already gated auth.
    }
  }
  // redirect() throws a control-flow signal — call it OUTSIDE the try so it isn't swallowed.
  if (needsOnboarding) redirect('/onboarding');

  return (
    <div className="flex h-screen overflow-hidden bg-base-200">
      <SidebarNav />
      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar />
        <main className="flex-1 overflow-y-auto px-6 py-6 lg:px-8">{children}</main>
      </div>
    </div>
  );
}
