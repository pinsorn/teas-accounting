// Sprint 13g — manual-demo personas (seed: company_id=2).
export const personas = {
  admin: { username: 'demo-admin', password: 'Demo@1234' },
  accountant: { username: 'demo-accountant', password: 'Demo@1234' },
  // Non-VAT company (company_id=3, "ร้านนอนแวต เดโม", vat_registered=false) —
  // used by the non-VAT walkthrough to contrast VAT vs non-VAT company behaviour.
  // Seeded by 550_seed_rbac_e2e_users.sql (company_admin on co3).
  nonvat: { username: 'rbac_nv_company_admin', password: 'Admin@1234' },
  // No-company super-admin (seed 562). Has is_super_admin=TRUE but NO role assignment →
  // login returns company_id=0 → (dashboard) layout redirects to /onboarding. Used by the
  // 00.01 onboarding-wizard walkthrough (self-bootstrap; it logs in inside its own body).
  'setup-admin': { username: 'setup-admin', password: 'Setup@1234' },
} as const;

export type PersonaName = keyof typeof personas;

// Per-walkthrough persona. 02.03/04/05 need admin (master/admin scopes);
// the rest are fine as accountant (BU/Product CRUD). '01.01' is special —
// it tests the login flow itself, so it does NOT pre-login (self-bootstrap).
const ADMIN_IDS = new Set(['02.03', '02.04', '02.05']);
// 00.01 self-bootstraps too: the setup-admin persona lands on /onboarding (company_id=0),
// not '/', so the run-capture driver's "login then waitForURL('/')" would hang — the
// walkthrough body does its own login instead.
export const SELF_BOOTSTRAP_IDS = new Set(['01.01', '00.01']);

export function personaFor(id: string, override?: PersonaName): PersonaName {
  if (override) return override;
  return ADMIN_IDS.has(id) ? 'admin' : 'accountant';
}
