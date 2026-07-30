// Sprint 13j-tail (4) — Ham directive 2026-05-22: use the company-uploaded logo
// wherever the brand mark renders (Sidebar header, PaperHead). Backend stores
// `CompanyProfile.LogoUrl` raw as `/attachments/{id}/download` (see
// `CompanyProfileService.UploadLogoAsync`); the BFF mounts attachments under
// `/api/proxy/attachments/...`, so any internal path must be prefixed.
// Centralised here so Sidebar + PaperHead + PDF previews all agree on the rule.
export const FALLBACK_LOGO = '/teas-logo.png';

/**
 * doc-signature-and-foot-layout §F2.2 — the generic form of the same `/attachments/... →
 * /api/proxy/attachments/...` BFF rule, with a **null** (not mascot) fallback on empty. Used for
 * signature images, the company stamp, and anything else that is an attachment reference but is
 * NOT a brand logo — a missing signature/stamp should render nothing, not the TEAS mascot.
 * - `null`/empty/whitespace → `null`.
 * - `/attachments/...`     → prefix `/api/proxy` (BFF) — required for uploads.
 * - any other absolute (`http(s)://...`) or already-proxied path → pass through.
 */
export function resolveAttachmentUrl(raw?: string | null): string | null {
  const v = raw?.trim();
  if (!v) return null;
  if (v.startsWith('/attachments/')) return `/api/proxy${v}`;
  return v;
}

/**
 * Resolve a logo URL into something the browser can actually load.
 * - `null`/empty/whitespace → fallback (the TEAS mascot).
 * - `/attachments/...`     → prefix `/api/proxy` (BFF) — required for uploads.
 * - any other absolute (`http(s)://...`) or already-proxied path → pass through.
 */
export function resolveLogoUrl(raw: string | null | undefined): string {
  return resolveAttachmentUrl(raw) ?? FALLBACK_LOGO;
}
