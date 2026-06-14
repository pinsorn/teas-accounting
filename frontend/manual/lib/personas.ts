// Sprint 13g — manual-demo personas (seed: company_id=2).
export const personas = {
  admin: { username: 'demo-admin', password: 'Demo@1234' },
  accountant: { username: 'demo-accountant', password: 'Demo@1234' },
  // Non-VAT company (company_id=3, "ร้านนอนแวต เดโม", vat_registered=false) —
  // used by the non-VAT walkthrough to contrast VAT vs non-VAT company behaviour.
  // Seeded by 550_seed_rbac_e2e_users.sql (company_admin on co3).
  nonvat: { username: 'rbac_nv_company_admin', password: 'Admin@1234' },
} as const;

export type PersonaName = keyof typeof personas;

// Per-walkthrough persona. 02.03/04/05 need admin (master/admin scopes);
// the rest are fine as accountant (BU/Product CRUD). '01.01' is special —
// it tests the login flow itself, so it does NOT pre-login (self-bootstrap).
const ADMIN_IDS = new Set(['02.03', '02.04', '02.05']);
export const SELF_BOOTSTRAP_IDS = new Set(['01.01']);

export function personaFor(id: string, override?: PersonaName): PersonaName {
  if (override) return override;
  return ADMIN_IDS.has(id) ? 'admin' : 'accountant';
}
