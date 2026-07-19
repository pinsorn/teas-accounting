# Changelog

## [1.22.7](https://github.com/pinsorn/teas-accounting/compare/v1.22.6...v1.22.7) (2026-07-19)


### Bug Fixes

* **numbering:** off-by-one retry cap left last-attempt doc_no collision uncaught (ambient-tx 500) ([af5ab8a](https://github.com/pinsorn/teas-accounting/commit/af5ab8a29333fef092e5ac220c80b652595b1ee3))

## [1.22.6](https://github.com/pinsorn/teas-accounting/compare/v1.22.5...v1.22.6) (2026-07-19)


### Bug Fixes

* **numbering,rbac:** heal doc-number sequence drift under concurrency + grant TAX_OFFICER tax filing ([3531052](https://github.com/pinsorn/teas-accounting/commit/35310523837e538683c817a8c369d3948f3ef20f))

## [1.22.5](https://github.com/pinsorn/teas-accounting/compare/v1.22.4...v1.22.5) (2026-07-19)


### Bug Fixes

* **reports,sales:** AR aging includes net-credit customers; QT-&gt;TI convert keeps line unit ([d04b290](https://github.com/pinsorn/teas-accounting/commit/d04b2900c2037b3d024cca611c1683a3a66708f1))

## [1.22.4](https://github.com/pinsorn/teas-accounting/compare/v1.22.3...v1.22.4) (2026-07-19)


### Bug Fixes

* **i18n:** add missing common.deleted key — delete toasts showed raw 'common.deleted' (CN/DN + pre-existing on quotation delete) ([4d3548c](https://github.com/pinsorn/teas-accounting/commit/4d3548c02a21034983c701f3678686b9bb9e4e5a))

## [1.22.3](https://github.com/pinsorn/teas-accounting/compare/v1.22.2...v1.22.3) (2026-07-19)


### Bug Fixes

* **cn:** list shows referenced TI doc number (server JOIN); draft-only delete for CN/DN ([2ec7cdf](https://github.com/pinsorn/teas-accounting/commit/2ec7cdfdd35c220401aee4411988be8b5566bc44))

## [1.22.2](https://github.com/pinsorn/teas-accounting/compare/v1.22.1...v1.22.2) (2026-07-18)


### Bug Fixes

* **cn,bank:** F-11 CN/DN reason Thai labels on form + legal doc; F-12 import format hint, doc-no in confirm dialog, match toasts ([3fc7619](https://github.com/pinsorn/teas-accounting/commit/3fc7619c43309f1dc5a9a6484bc0fb1272d85164))

## [1.22.1](https://github.com/pinsorn/teas-accounting/compare/v1.22.0...v1.22.1) (2026-07-18)


### Bug Fixes

* **db:** 625 COGS seed must pin app.company_id per company — prod runs SqlScripts under NOBYPASSRLS ([570254b](https://github.com/pinsorn/teas-accounting/commit/570254b13809aba98472b9ec15bf644d961c8fb4))

## [1.22.0](https://github.com/pinsorn/teas-accounting/compare/v1.21.6...v1.22.0) (2026-07-18)


### Features

* **payroll,tax:** opening-YTD (ยอดยกมา), Pay settlement JE, COGS account, tax-summary PND1 + i18n batch ([7fed441](https://github.com/pinsorn/teas-accounting/commit/7fed441f4c169db5d1edb40da534ca88f42c07f5))

## [1.21.6](https://github.com/pinsorn/teas-accounting/compare/v1.21.5...v1.21.6) (2026-07-18)


### Bug Fixes

* **master:** company creation atomic + RLS-correct tenant seeding ([4b92efd](https://github.com/pinsorn/teas-accounting/commit/4b92efd50c675a25d2ca73c86e540476445d9070))

## [1.21.5](https://github.com/pinsorn/teas-accounting/compare/v1.21.4...v1.21.5) (2026-07-17)


### Bug Fixes

* **fe:** payroll UX round W2 — zero-salary warnings + payslip breakdown modal ([ce9aba1](https://github.com/pinsorn/teas-accounting/commit/ce9aba15596e7a69fd90261ce2b5647e2f7b19e4))
* **fe:** payroll/reports UX round W1 — error infra + employees modal + i18n ([7bb293d](https://github.com/pinsorn/teas-accounting/commit/7bb293d9adc363c9ce0e0b6a6ed9d7d9151bc0c1))
* **fe:** reports UX round W3 — dates, exports, presets, picker, basis notes ([c71c13b](https://github.com/pinsorn/teas-accounting/commit/c71c13b9ac9df7387826106ec9c77f62ba6e2312))

## [1.21.4](https://github.com/pinsorn/teas-accounting/compare/v1.21.3...v1.21.4) (2026-07-16)


### Bug Fixes

* **sales-ui:** WP-B — confirm dialogs on QT send/accept/reject + SO post + INV issue/mark-settled (S11), live side-rail refetch + activity wording (S12-FE), SO+INV draft edit routes (S15), receipt BU prefill from invoice (S16), BU badge on detail pages (S10), BFF proxy 30s timeout with distinct 504 (S13a) ([e71f3e3](https://github.com/pinsorn/teas-accounting/commit/e71f3e321a810d3bbec43d70aa4196306313b332))
* **sales-ui:** WP-C polish — hydration skeleton + vatMode never-flash (S1), breadcrumb i18n all routes (S2), status filter Thai labels (S3), BE date hints on forms + list filters (S5/S7), customer picker create link (S8) ([996d91a](https://github.com/pinsorn/teas-accounting/commit/996d91aac3373be4632edd00fd1586526237e570))
* **sales:** WP-A backend — BU in QT/SO/DO list DTOs (S4), company BU requirement on create/edit/send gates (S9), invoice due date honors customer credit term (S14), draft-edit activity entries (S12-BE), SO draft update endpoint (S15-BE), double-call transition guards verified (S13b) ([83e47f9](https://github.com/pinsorn/teas-accounting/commit/83e47f9bc80068f09ab0e55c114a1dd2bd2eb866))

## [1.21.3](https://github.com/pinsorn/teas-accounting/compare/v1.21.2...v1.21.3) (2026-07-15)


### Bug Fixes

* **ui:** BU column stuck on #id via TanStack row._valuesCache — resolve in cell, all 9 list pages ([f6a8356](https://github.com/pinsorn/teas-accounting/commit/f6a835602f4d54f7e1fbd6da5c9fcbd108f7dc33))

## [1.21.2](https://github.com/pinsorn/teas-accounting/compare/v1.21.1...v1.21.2) (2026-07-15)


### Bug Fixes

* **purchase:** R2 Option B — preserve DocDate on PO draft edit (Ham decision) ([6368451](https://github.com/pinsorn/teas-accounting/commit/6368451ff2a0a7fdd6f8f19f26203ce93022c3c2))
* **ui:** BU column stale-memo bug on remaining 8 list pages (same root cause as R1) ([0258260](https://github.com/pinsorn/teas-accounting/commit/0258260c4502ae358d65413a791fbe1090da8050))

## [1.21.1](https://github.com/pinsorn/teas-accounting/compare/v1.21.0...v1.21.1) (2026-07-15)


### Bug Fixes

* **purchase:** R1 PO-list BU column stale memo; R3 Thai for po/vi/pv error codes; R4 PV-from-VI prefill lands exactly on VI outstanding ([731e775](https://github.com/pinsorn/teas-accounting/commit/731e7752307390660e906e091af27f190e1b68ce))
* **purchase:** R2 FE — lock docDate display to server-pinned today (create+edit) ([526a55b](https://github.com/pinsorn/teas-accounting/commit/526a55bea99fb35eb75a88f697f14fdaa845db38))

## [1.21.0](https://github.com/pinsorn/teas-accounting/compare/v1.20.1...v1.21.0) (2026-07-15)


### Features

* **auth:** WP2 sliding session + global 401 recovery + 308 fix + Thai errors (F16/F19/F21) ([d5a9c69](https://github.com/pinsorn/teas-accounting/commit/d5a9c6964301184c098594901f6b035f0053a598))
* **purchase-ux:** WP3+WP4 FE flow/discoverability/polish (findings F2-F24) ([d88ee51](https://github.com/pinsorn/teas-accounting/commit/d88ee51126904b829223f73d200632fdd759c102))
* **purchase:** WP1 money/compliance — non-VAT non-recoverable, percent-UI, category backfill (F13/F15/F20/F27) ([65b9b2b](https://github.com/pinsorn/teas-accounting/commit/65b9b2b08757ca9afd245f62f8f67ce47558256d))
* **purchase:** WP3.4 PO close/reopen + confirm dialog; WP4.9 SoD text (F29/F25) ([a86de78](https://github.com/pinsorn/teas-accounting/commit/a86de78e3509c02ff12467981d45bb79fa9a959e))


### Bug Fixes

* **purchase:** F-C re-switch company after modal re-login; F-5 service-level rate bounds ([91e374d](https://github.com/pinsorn/teas-accounting/commit/91e374d626e9ee83f12e3b40cb0220446908ffbf))

## [1.20.1](https://github.com/pinsorn/teas-accounting/compare/v1.20.0...v1.20.1) (2026-07-13)


### Bug Fixes

* **receipt:** settle + guard direct BillingNote applications; chain edges for skip-DO FKs ([9289ded](https://github.com/pinsorn/teas-accounting/commit/9289ded08a19857fc9511b1399549880bf40744c))
* **receipt:** settle + guard direct BillingNote applications; chain edges for skip-DO FKs ([3b082ce](https://github.com/pinsorn/teas-accounting/commit/3b082cee33f3dff070aee9478f9564616d851a7a))

## [1.20.0](https://github.com/pinsorn/teas-accounting/compare/v1.19.0...v1.20.0) (2026-07-13)


### Features

* **mcp:** agent-draftable document chain (sales + purchase) with human approval per hop ([f9d9ddb](https://github.com/pinsorn/teas-accounting/commit/f9d9ddb4bf8869400f2cccc7cc2f99b9e9159a36))
* **mcp:** agent-draftable document chain (sales + purchase) with human approval per hop ([972dddb](https://github.com/pinsorn/teas-accounting/commit/972dddb8aaf2ef8ea8153a280bd7da78a8750e31))

## [1.19.0](https://github.com/pinsorn/teas-accounting/compare/v1.18.0...v1.19.0) (2026-07-12)


### Features

* **bank:** warn on manual match confirm outside the 7-day suggest window ([806f241](https://github.com/pinsorn/teas-accounting/commit/806f241e66d99ecb867c4d4451da5916103de531))
* **bank:** warn on manual match confirm outside the 7-day window ([215525d](https://github.com/pinsorn/teas-accounting/commit/215525de506531d65ec3ecb65832c11bba7f34f9))
* **mcp:** surface business errors to MCP clients + master-data resolver tools ([6a56bba](https://github.com/pinsorn/teas-accounting/commit/6a56bbac55150682bd04363e425e7e3453560c46))
* **mcp:** surface business errors to MCP clients + master-data resolver tools ([c2736e7](https://github.com/pinsorn/teas-accounting/commit/c2736e70cbbfc2ea502ee5093d0bc40489a5a56a))

## [1.18.0](https://github.com/pinsorn/teas-accounting/compare/v1.17.0...v1.18.0) (2026-07-10)


### Features

* **mcp:** 14 read + draft-create tools for bank rec, expense claims, fixed assets ([00f14df](https://github.com/pinsorn/teas-accounting/commit/00f14dff51e79c71077ba79b36553688e790e333))
* **mcp:** read + draft-create tools for bank rec, expense claims, fixed assets ([cbd17a7](https://github.com/pinsorn/teas-accounting/commit/cbd17a7e1150a4e39dd069528d554be9a5eb286f))


### Bug Fixes

* harden money paths per Codex cross-family review ([b475b36](https://github.com/pinsorn/teas-accounting/commit/b475b3621544403c6e86f269d31d8c076eac0cdd))
* harden money paths per cross-family review (bank rec scoping, validation, races, CSV) ([7445581](https://github.com/pinsorn/teas-accounting/commit/7445581acea6cfde943bed9ac44d60ffdf7de8d4))

## [1.17.0](https://github.com/pinsorn/teas-accounting/compare/v1.16.0...v1.17.0) (2026-07-10)


### Features

* **expense:** expense claims - submit/approve/pay with GL posting ([4df215b](https://github.com/pinsorn/teas-accounting/commit/4df215b3f90498584876e1399642819c9ad936a4))
* **expense:** expense claims (Cycle C) ([d516de5](https://github.com/pinsorn/teas-accounting/commit/d516de53543af02fa210458844df78e082ab09d4))
* **fixedasset:** fixed assets + depreciation (Cycle D) ([e804878](https://github.com/pinsorn/teas-accounting/commit/e8048788a1114b3e0a79770e43821efd87d06a09))
* **fixedasset:** fixed assets register + straight-line depreciation + disposal ([7a013c0](https://github.com/pinsorn/teas-accounting/commit/7a013c016f7dcd0012ac726525de1eb3674cd238))

## [1.16.0](https://github.com/pinsorn/teas-accounting/compare/v1.15.1...v1.16.0) (2026-07-09)


### Features

* **bank:** bank reconciliation B1 - schema + bank account master ([4381cd7](https://github.com/pinsorn/teas-accounting/commit/4381cd75b6dfa410678b817bd9a62d77345c3148))
* **bank:** bank reconciliation B2 - statement import + KBiz CSV adapter ([c90d70e](https://github.com/pinsorn/teas-accounting/commit/c90d70ebb105587621c6a8e6320d3f351ffd0b0a))
* **bank:** bank reconciliation B3 - K-Plus PDF adapter ([a8f3602](https://github.com/pinsorn/teas-accounting/commit/a8f3602a6b7088fa9a465eff5a3ee5170b436a1b))
* **bank:** bank reconciliation B4+B5 - matching engine, inline JE, report ([cd23713](https://github.com/pinsorn/teas-accounting/commit/cd237133c27aeefc41d7bb99a63f0d35acb4dd52))
* **bank:** Cycle B — bank reconciliation (KBiz CSV + K-Plus PDF) ([68574db](https://github.com/pinsorn/teas-accounting/commit/68574db7c523a531f1a044d418e8d3fc67da8045))


### Bug Fixes

* **bank:** cross-review findings + CI-portable storage in import tests ([c4c71d6](https://github.com/pinsorn/teas-accounting/commit/c4c71d6cbbad9cbea048c502ffed53119776f66c))

## [1.15.1](https://github.com/pinsorn/teas-accounting/compare/v1.15.0...v1.15.1) (2026-07-08)


### Bug Fixes

* **seeds:** make startup seeds 610/611 RLS-safe on prod ([4ea8902](https://github.com/pinsorn/teas-accounting/commit/4ea8902d2d3a970b0371121bb470fc88d50dbbf3))
* **seeds:** make startup seeds 610/611 RLS-safe on prod (42501 / silent zero fan-out) ([d3f091d](https://github.com/pinsorn/teas-accounting/commit/d3f091dee1f4a3d93890c3486ce85c6ae30e10a8))

## [1.15.0](https://github.com/pinsorn/teas-accounting/compare/v1.14.1...v1.15.0) (2026-07-08)


### Features

* **accounting:** Cycle A — year-end closing, period close UI, AR aging CSV, docType labels ([70c485e](https://github.com/pinsorn/teas-accounting/commit/70c485e2b1dc77e9a3b84d9a8f38b67f4ff9d5a7))
* **accounting:** year-end closing entries, period close UI, AR aging CSV export, docType labels ([51c5731](https://github.com/pinsorn/teas-accounting/commit/51c57318e79f5627a90472515ff1ad97490fbc87))

## [1.14.1](https://github.com/pinsorn/teas-accounting/compare/v1.14.0...v1.14.1) (2026-07-08)


### Bug Fixes

* **security:** scope super admin data access to the selected company ([2cdb037](https://github.com/pinsorn/teas-accounting/commit/2cdb0371db9ceffa4bb8c335045e0aba643aa124))
* **security:** scope super admin data access to the selected company ([b406528](https://github.com/pinsorn/teas-accounting/commit/b406528194fa923b70ea9ca950fabc2f2e45fd4b))

## [1.14.0](https://github.com/pinsorn/teas-accounting/compare/v1.13.2...v1.14.0) (2026-07-08)


### Features

* **reports:** AR/AP sub-ledger suite with GL control-account reconciliation ([9095868](https://github.com/pinsorn/teas-accounting/commit/9095868978e818d6971cde643b4213866f8456f0))
* **reports:** balance sheet + AR/AP sub-ledger suite with reconciliation ([2e7999c](https://github.com/pinsorn/teas-accounting/commit/2e7999c9d3e5b9934b46bee9224f963c7924bd37))
* **reports:** balance sheet page + get_balance_sheet MCP tool ([e3b4aa6](https://github.com/pinsorn/teas-accounting/commit/e3b4aa6789df52b6c9e371ca3ebb703b0b16c188))

## [1.13.2](https://github.com/pinsorn/teas-accounting/compare/v1.13.1...v1.13.2) (2026-07-08)


### Bug Fixes

* **mcp:** field-test findings — P&L untagged-BU default, doc-status visibility docs, GL id-vs-code message ([be039a5](https://github.com/pinsorn/teas-accounting/commit/be039a5ed4f9a6627f337a1810ec56bdb077c29d))
* **mcp:** field-test findings — P&L untagged-BU default, visibility docs, GL error message ([e50f47b](https://github.com/pinsorn/teas-accounting/commit/e50f47b95cc6cae96508ce301c663fc9fc39bd68))

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
