# Changelog

## [1.13.1](https://github.com/pinsorn/teas-accounting/compare/v1.13.0...v1.13.1) (2026-07-08)


### Bug Fixes

* **frontend:** add Next passthrough route for public PDF links ([3dc4ee7](https://github.com/pinsorn/teas-accounting/commit/3dc4ee78447e93f0582325e53c95bcca2c9e7ee8))
* **frontend:** Next passthrough route for public PDF links ([361c1ae](https://github.com/pinsorn/teas-accounting/commit/361c1ae96e9443dd5814a004ca22f46fd3b21f53))

## [1.13.0](https://github.com/pinsorn/teas-accounting/compare/v1.12.0...v1.13.0) (2026-07-08)


### Features

* **mcp:** browser-openable PDF links, fuzzy search, read expansion, draft/master edit tools, document filters ([72c8509](https://github.com/pinsorn/teas-accounting/commit/72c85095a17dc10fc8c1f10edeb6c2c2ecaf6aec))
* **mcp:** public PDF links, fuzzy search, read expansion, edit tools, document filters ([a7a201f](https://github.com/pinsorn/teas-accounting/commit/a7a201fa09108ab55b8a68f4b0ee23d68027739d))

## [1.12.0](https://github.com/pinsorn/teas-accounting/compare/v1.11.1...v1.12.0) (2026-07-07)


### Features

* **reports:** general ledger report with journal entry drill-down ([4953d04](https://github.com/pinsorn/teas-accounting/commit/4953d04ae00a6310fb0deb0b81b7743f54d8b7d3))
* **reports:** general ledger report with journal entry drill-down ([a272d37](https://github.com/pinsorn/teas-accounting/commit/a272d3704cbb0ec627de554b4dfbf5676e8c61c1))


### Bug Fixes

* **reports:** force CRLF line endings in general ledger CSV export ([6ae381a](https://github.com/pinsorn/teas-accounting/commit/6ae381a91ccad8408f6c45a118f9013e3a7a6d1b))
* **reports:** force CRLF line endings in general ledger CSV export ([ac4a11b](https://github.com/pinsorn/teas-accounting/commit/ac4a11b8f06fcb58dcf8c147403f583dc0c90ad4))

## [1.11.1](https://github.com/pinsorn/teas-accounting/compare/v1.11.0...v1.11.1) (2026-07-05)


### Bug Fixes

* **onboarding:** seed HQ branch on company creation; move OAuth consent out of onboarding gate ([33cd835](https://github.com/pinsorn/teas-accounting/commit/33cd835a8d8f01120e4bdc29fdaafb855c2219a0))
* **onboarding:** seed HQ branch on company creation; move OAuth consent out of onboarding gate ([e15c44e](https://github.com/pinsorn/teas-accounting/commit/e15c44e2d89fb781a18e976ce172526ddcb75ef7))

## [1.11.0](https://github.com/pinsorn/teas-accounting/compare/v1.10.4...v1.11.0) (2026-07-05)


### Features

* **oauth:** implement RFC 7591 Dynamic Client Registration for MCP connectors ([6a8a233](https://github.com/pinsorn/teas-accounting/commit/6a8a23371bd9762dc843b0003adfbf991a0a5762))
* **oauth:** RFC 7591 Dynamic Client Registration for MCP connectors ([f3351e5](https://github.com/pinsorn/teas-accounting/commit/f3351e5549f2707a18402102f33e87c53e8e128b))

## [1.10.4](https://github.com/pinsorn/teas-accounting/compare/v1.10.3...v1.10.4) (2026-07-04)


### Bug Fixes

* run scheduled jobs in the API, delete Accounting.Workers, pin is_super_admin off in VAT snapshot ([6533a4c](https://github.com/pinsorn/teas-accounting/commit/6533a4c580fe24bc73811ca9845d9d48f420be6e))
* **workers:** run scheduled jobs inside the API + delete Accounting.Workers; pin is_super_admin off in the VAT-snapshot job ([fbf9ef1](https://github.com/pinsorn/teas-accounting/commit/fbf9ef18d43e0fcb18259e944a48126169feda9d))

## [1.10.3](https://github.com/pinsorn/teas-accounting/compare/v1.10.2...v1.10.3) (2026-07-04)


### Bug Fixes

* 2026-07-04 full-codebase review — 10 HIGH + 12 MED + 7 LOW + MCP connect ([4f65afa](https://github.com/pinsorn/teas-accounting/commit/4f65afa5c75459b5c702e209e8d8517a4a7f1dbb))
* **api:** validate PUT/update endpoints (H3, review 2026-07-04) ([4e7e398](https://github.com/pinsorn/teas-accounting/commit/4e7e39837ec18b3076bfb09bc1c1ff45a48f6712))
* **auth:** pin api-key lookup so RLS doesn't 401 it in prod — H5 (review 2026-07-04) ([7238186](https://github.com/pinsorn/teas-accounting/commit/72381868622d1bf1e94e29bb6a2869d7ff720d08))
* **backend:** Wave-4 medium/low — per-IP login limit, N+1, vestigial key, labels (M4/M5/M9/L1/L2/L4/L5/F2) ([9f2124c](https://github.com/pinsorn/teas-accounting/commit/9f2124ca8864c36e840f9541b8476a4d6a436aa2))
* **db:** RLS + immutability hardening — H1/H7/M2/M1 (review 2026-07-04) ([523fa9a](https://github.com/pinsorn/teas-accounting/commit/523fa9af613b843d6b8cdb79139d1c9c662ab38c))
* **db:** RLS backstop on audit.activity_log — M12 (review 2026-07-04) ([7027749](https://github.com/pinsorn/teas-accounting/commit/70277490ba9ebaca0b52214220d2a1f8e08dde8b))
* **etax:** pin the cross-tenant retry scans so 581 RLS doesn't hide them — M3 (review 2026-07-04) ([a67ddf9](https://github.com/pinsorn/teas-accounting/commit/a67ddf90a2b3f4865b1e971d7beaa7b28d83e253))
* **infra:** WHT cross-tenant leak + numbering tx-safety — H6/H8 (review 2026-07-04) ([af6f26e](https://github.com/pinsorn/teas-accounting/commit/af6f26e47e9e2e15ddb57c5cecf757d67517456d))
* **oauth:** consent + refresh bind MCP scopes to the user's RBAC — H4/M11 (review 2026-07-04) ([9872841](https://github.com/pinsorn/teas-accounting/commit/9872841202023e8513b4d12fe9960e90d518388f))
* **oauth:** register Claude's MCP callbacks + advertise 'none' auth method — MCP connect (2026-07-04) ([756d0d6](https://github.com/pinsorn/teas-accounting/commit/756d0d65d63aa0aa7d9856ea1a5b9ade5fdfbf12))
* **tax:** ภ.พ.30 correctness — input-VAT gate, credit no double-count, CN/DN category (H9/H10/M10) ([4f591a9](https://github.com/pinsorn/teas-accounting/commit/4f591a9636aac029fd2797a543a6dc54e15b2ff9))
* **web:** login BFF forwards the real client IP so the per-IP limit works (M4/M5 completion) ([3f10f97](https://github.com/pinsorn/teas-accounting/commit/3f10f97eb5a0b8290409608252b288f225f8c58b))
* **web:** returnTo backslash open-redirect + ภ.พ.30 preview credit + label dedup (M6/F1/L6) ([8df318e](https://github.com/pinsorn/teas-accounting/commit/8df318e88e76ffb45a84ad533738a57eb7050e8c))
* **web:** surface API error detail + Zod on VendorForm + i18n PV strings (M7/M8/L3) ([41e476e](https://github.com/pinsorn/teas-accounting/commit/41e476e5a084afaf8fca3cc1818a9fd81174be6f))
* **workers:** pin ภ.พ.30 snapshot per company — Workers ran tenant-blind (H2, review 2026-07-04) ([87aec21](https://github.com/pinsorn/teas-accounting/commit/87aec21887574305bca5f124da3e1fefbbda530c))

## [1.10.2](https://github.com/pinsorn/teas-accounting/compare/v1.10.1...v1.10.2) (2026-07-03)


### Bug Fixes

* **auth:** carry returnTo through the login redirect so deep links resume after login ([#37](https://github.com/pinsorn/teas-accounting/issues/37)) ([9eea69d](https://github.com/pinsorn/teas-accounting/commit/9eea69d7ba51dadcf069ac7e22ee4f78fa273c74))

## [1.10.1](https://github.com/pinsorn/teas-accounting/compare/v1.10.0...v1.10.1) (2026-07-03)


### Bug Fixes

* **etax:** remove inert e-Tax buttons from TI detail; XML endpoint requires POSTED ([#36](https://github.com/pinsorn/teas-accounting/issues/36)) ([e743ed3](https://github.com/pinsorn/teas-accounting/commit/e743ed3c34f95504067bb90bd4cf4e8269271c95))
* **ui:** agent approve banner — CTA right-aligned, duplicate action button hidden ([#34](https://github.com/pinsorn/teas-accounting/issues/34)) ([5c4fd9d](https://github.com/pinsorn/teas-accounting/commit/5c4fd9d8f3bc248a584d852ebc1e7640e882af64))

## [1.10.0](https://github.com/pinsorn/teas-accounting/compare/v1.9.0...v1.10.0) (2026-07-03)


### Features

* **oauth:** TEAS Connect OAuth 2.1 Authorization Server — OpenIddict (Claude Mobile/Desktop native connectors) ([769434d](https://github.com/pinsorn/teas-accounting/commit/769434d89cd97ac1ff0c72e8f5e467b5fc165b67))

## [1.9.0](https://github.com/pinsorn/teas-accounting/compare/v1.8.5...v1.9.0) (2026-07-02)


### Features

* **pdf:** screen==print parity + canonical paper DTO (GET /{doc}/{id}/paper) ([92ddaf9](https://github.com/pinsorn/teas-accounting/commit/92ddaf99bec2ef585afa2b49486c0b6efa802987))


### Bug Fixes

* **mcp:** make TEAS Connect reachable (Claude Code + Desktop) and tools usable ([#29](https://github.com/pinsorn/teas-accounting/issues/29)) ([43b4c45](https://github.com/pinsorn/teas-accounting/commit/43b4c450a331887c4339be99500a5077aedced05))

## [1.8.5](https://github.com/pinsorn/teas-accounting/compare/v1.8.4...v1.8.5) (2026-06-22)


### Bug Fixes

* **ui:** @tailwindcss/forms strategy:class — DaisyUI checkboxes/radios/toggles render correctly site-wide ([#26](https://github.com/pinsorn/teas-accounting/issues/26)) ([4d8f696](https://github.com/pinsorn/teas-accounting/commit/4d8f69693b6ef47c81c5ee341847b6bc4f195993))

## [1.8.4](https://github.com/pinsorn/teas-accounting/compare/v1.8.3...v1.8.4) (2026-06-22)


### Bug Fixes

* **ui:** checked toggle/radio stay visible on hover/focus + WHT table fits its card ([#24](https://github.com/pinsorn/teas-accounting/issues/24)) ([2fb688c](https://github.com/pinsorn/teas-accounting/commit/2fb688c78825b2c0248c32eec1f45b8eb604c498))

## [1.8.3](https://github.com/pinsorn/teas-accounting/compare/v1.8.2...v1.8.3) (2026-06-22)


### Bug Fixes

* **ui:** sidebar highlights parent + child when on a child route (prefix match) ([#22](https://github.com/pinsorn/teas-accounting/issues/22)) ([00b46b4](https://github.com/pinsorn/teas-accounting/commit/00b46b49b74dce256c3dfbb94ca450d7a5c4e745))

## [1.8.2](https://github.com/pinsorn/teas-accounting/compare/v1.8.1...v1.8.2) (2026-06-22)


### Bug Fixes

* **ui:** toggle goes blank/white after click — @tailwindcss/forms focus ring clobbered the DaisyUI thumb ([#20](https://github.com/pinsorn/teas-accounting/issues/20)) ([4df10aa](https://github.com/pinsorn/teas-accounting/commit/4df10aaaf0a66a7f14fcfcbc90160665d1e04838))

## [1.8.1](https://github.com/pinsorn/teas-accounting/compare/v1.8.0...v1.8.1) (2026-06-22)


### Bug Fixes

* **auth:** resolve RLS-hidden roles/permissions during login → non-super users got an empty token ([#16](https://github.com/pinsorn/teas-accounting/issues/16)) ([1940f9c](https://github.com/pinsorn/teas-accounting/commit/1940f9cdc8369c878b09cf12b1c1c9366a3fda0c))

## [1.8.0](https://github.com/pinsorn/teas-accounting/compare/v1.7.3...v1.8.0) (2026-06-22)


### Features

* **rbac:** admin user management — create user, toggle active, reset password ([#14](https://github.com/pinsorn/teas-accounting/issues/14)) ([0f37e10](https://github.com/pinsorn/teas-accounting/commit/0f37e107b80aabb78a8ff004e6a235fe5403b538))

## [1.7.3](https://github.com/pinsorn/teas-accounting/compare/v1.7.2...v1.7.3) (2026-06-22)


### Bug Fixes

* non-VAT sales chain — DO VAT backstop + full CoA on onboarding ([#12](https://github.com/pinsorn/teas-accounting/issues/12)) ([29a8e7d](https://github.com/pinsorn/teas-accounting/commit/29a8e7d1e9ff75f2b0e1aeab79c7e85086885c75))

## [1.7.2](https://github.com/pinsorn/teas-accounting/compare/v1.7.1...v1.7.2) (2026-06-21)


### Bug Fixes

* super-admin with companies stuck in /onboarding loop (auto-switch instead) ([3702451](https://github.com/pinsorn/teas-accounting/commit/370245150478a9fdbafb5850dd7dd6ac3f8160a3))
* super-admin with companies stuck in /onboarding loop (auto-switch instead) ([3f489f8](https://github.com/pinsorn/teas-accounting/commit/3f489f8c50eba2811f9322cb13daea4beb2ab208))

## [1.7.1](https://github.com/pinsorn/teas-accounting/compare/v1.7.0...v1.7.1) (2026-06-21)


### Bug Fixes

* editable company info — correct founding identity / branch / VAT on a fresh install ([b8a93d5](https://github.com/pinsorn/teas-accounting/commit/b8a93d593ac79e53072602e670982c15280eae05))
* editable company info — correct founding identity / branch / VAT on a fresh install ([4cd03fe](https://github.com/pinsorn/teas-accounting/commit/4cd03fe7e8ee99895490374433eb97b4fa293a6c))

## [1.7.0](https://github.com/pinsorn/teas-accounting/compare/v1.6.0...v1.7.0) (2026-06-21)


### Features

* RLS-safe SeedDemoData=false clean install + first-run onboarding entry ([60cc975](https://github.com/pinsorn/teas-accounting/commit/60cc975be06934d2b1cbde36a07c2de89a319fa2))
* RLS-safe SeedDemoData=false clean install + first-run onboarding entry ([7cf12d4](https://github.com/pinsorn/teas-accounting/commit/7cf12d4c67c5da1815d35b16632f07b67bd95f5f))

## [1.6.0](https://github.com/pinsorn/teas-accounting/compare/v1.5.0...v1.6.0) (2026-06-21)


### Features

* RD Prep "Format กลาง" .txt export for ภ.พ.30 + financial-statement PDF ([73c0257](https://github.com/pinsorn/teas-accounting/commit/73c0257a04db04e03b94c1bf5659a5985af7ca03))
* RD Prep Format-กลาง .txt export (ภ.พ.30) + financial-statement PDF ([ddcfae2](https://github.com/pinsorn/teas-accounting/commit/ddcfae28d3875cee2b87da2002cd47ea4695e53e))

## [1.5.0](https://github.com/pinsorn/teas-accounting/compare/v1.4.0...v1.5.0) (2026-06-19)


### Features

* **review:** complete B3 agent-draft visibility (detail badges + dashboard PO/VI/PV) ([544d3e6](https://github.com/pinsorn/teas-accounting/commit/544d3e6ee53c10400aee284badf968e0d896e0ee))


### Bug Fixes

* **ci:** commit missing AgentPendingBadge + make Pnd50 size test env-robust ([166b373](https://github.com/pinsorn/teas-accounting/commit/166b3737a3e4df85e3f8ccb47c5f30f39997624d))
* **review:** dual-reviewed code-review 2026-06-19 fixes (compliance/security/correctness) ([9936877](https://github.com/pinsorn/teas-accounting/commit/9936877cdf22f71ea8419419d3cf2db3356e5580))
