// Sprint 13j-tail (4) — Ham directive 2026-05-22: use the company-uploaded logo
// wherever the brand mark renders (Sidebar header, PaperHead). Backend stores
// `CompanyProfile.LogoUrl` raw as `/attachments/{id}/download` (see
// `CompanyProfileService.UploadLogoAsync`); the BFF mounts attachments under
// `/api/proxy/attachments/...`, so any internal path must be prefixed.
// Centralised here so Sidebar + PaperHead + PDF previews all agree on the rule.
export const FALLBACK_LOGO = '/teas-logo.png';

/**
 * Resolve a logo URL into something the browser can actually load.
 * - `null`/empty/whitespace → fallback (the TEAS mascot).
 * - `/attachments/...`     → prefix `/api/proxy` (BFF) — required for uploads.
 * - any other absolute (`http(s)://...`) or already-proxied path → pass through.
 */
export function resolveLogoUrl(raw: string | null | undefined): string {
  const v = raw?.trim();
  if (!v) return FALLBACK_LOGO;
  if (v.startsWith('/attachments/')) return `/api/proxy${v}`;
  return v;
}
