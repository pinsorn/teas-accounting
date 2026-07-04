// M6 (auth-oauth review 2026-07-04) — same-origin path guard for the post-login `returnTo`
// redirect (v1.10.2 deep-link resume, app/(auth)/login/page.tsx). Only a same-origin RELATIVE
// path is safe: must start with exactly one '/' followed by a char that is neither '/' nor '\'.
// This rejects '//evil.com' (protocol-relative) AND '/\evil.com' (browsers normalize a literal
// '\' to '/' during URL resolution, so '/\evil.com' behaves like '//evil.com' -> off-origin).
export function isSafeReturnTo(rt: string | null | undefined): rt is string {
  return !!rt && /^\/[^/\\]/.test(rt);
}
