# Changelog

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
