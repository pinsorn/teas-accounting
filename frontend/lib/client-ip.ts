// M4/M5 follow-up (2026-07-04) — real client IP for the login BFF's outbound X-Forwarded-For.
// The backend's per-IP login rate limiter + UseForwardedHeaders trusts this co-located BFF as a
// loopback proxy, so whatever XFF it sends is taken verbatim — it must therefore come from a
// source the client cannot forge. `cf-connecting-ip` is set by Cloudflare at the edge and cannot
// be spoofed by the client; the incoming `x-forwarded-for` (attacker-controllable) is only a
// fallback, and only its FIRST (left-most / original-client) entry is used.
export function extractClientIp(headers: Headers): string {
  const cf = headers.get('cf-connecting-ip');
  if (cf) return cf;
  const xff = headers.get('x-forwarded-for');
  return xff?.split(',')[0]?.trim() ?? '';
}
