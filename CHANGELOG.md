# Changelog

## [2.3.1](https://github.com/pinsorn/teas-accounting/compare/v2.3.0...v2.3.1) (2026-08-20)


### Bug Fixes

* **fe,master:** paper preview refreshes after edit, explicit 0% VAT rate persists, cancel/reject reasons are the user's own (Codex UI review 1-3) ([3424fb0](https://github.com/pinsorn/teas-accounting/commit/3424fb0b77275b9a3222716985bac187fd11513c))
* **fe:** master-data edits refresh paper previews; orphaned quotation i18n removed (Tier-2 follow-up F-3/N2) ([3aaafb0](https://github.com/pinsorn/teas-accounting/commit/3aaafb061c45154f678def623b75b86b22414641))
* **fe:** mobile layouts stop clipping controls, labeled company selectors, honest activity error states, no empty quick-action section (Codex UI review R2-R5) ([1b6d992](https://github.com/pinsorn/teas-accounting/commit/1b6d9928194be2bb7019e407d3da411b155e4216))
* **rbac:** demo approver loses SUPER_ADMIN (seed 160 + reconcile 642); document activity gated by the document's own read permission (Codex UI review R1+R3) ([3054c89](https://github.com/pinsorn/teas-accounting/commit/3054c8958257faeab209849dcaf1813adea7e4d3))
* **seed,bank:** 160 survives both pre- and post-510 shapes under RLS; import delete tolerates an already-deleted attachment (Tier-2 REJECT remediation) ([1995412](https://github.com/pinsorn/teas-accounting/commit/19954120964eda52eda9ef9c3f575a6e7a4ebd13))
* **seed,bank:** scope demo repairs to the demo identity; close the import-delete race and attachment orphan (Codex review F1-F4) ([72b25ad](https://github.com/pinsorn/teas-accounting/commit/72b25ad24f342f177683bd398b3839f25139e46d))

## [2.3.0](https://github.com/pinsorn/teas-accounting/compare/v2.2.1...v2.3.0) (2026-08-20)


### Features

* **fe:** draft fixed-asset edit page + bank-rec modal dialog roles (r2 U8/L3-12+L2-1) ([677707e](https://github.com/pinsorn/teas-accounting/commit/677707eafd1890864e934f14ba9311b9001af5a5))
* **fixedasset:** day-prorated first month + units-indexed depreciation schedule (cleanup C4/C7) ([cecfac7](https://github.com/pinsorn/teas-accounting/commit/cecfac7279b1f337f069c2374b3a64263336be3c))
* **rbac:** name-only employee lookup for expense-claim submitters (r2 U6/L4-1) ([e81f92d](https://github.com/pinsorn/teas-accounting/commit/e81f92dd1495d64a234f87de7faef574823bc30b))
* **tax:** ภ.พ.36 filled-PDF export with ภ.ง.ด.54 parity (O5, Ham ruling 2026-08-20) ([9f81d6f](https://github.com/pinsorn/teas-accounting/commit/9f81d6f765932922ea51655f05506817a2ec132d))


### Bug Fixes

* **api,mcp:** binding failures return typed 400s, and settling a receipt now requires the right to read the tax invoice ([701aa7c](https://github.com/pinsorn/teas-accounting/commit/701aa7c3938d615e26edfc1436971372a07c9eb1))
* **bank:** deterministic statement closing balance, typed import errors, superseded-import deletion (r2 U3+U4/L2-2,3,4) ([f1f334b](https://github.com/pinsorn/teas-accounting/commit/f1f334b883fa1ac4483ec42f9f6b4d9ad5fc8312))
* **fe:** FA modal dialog roles, PO/DO forms stop hardcoding tax code 1, back-dated-claim pay note (cleanup C2) ([5a4981c](https://github.com/pinsorn/teas-accounting/commit/5a4981cadf238c83a02d73bb2ea970ce6bff8e0e))
* **fe:** route typed API errors to problemToast across 20 screens (r2 U7/L6-3) ([635e21f](https://github.com/pinsorn/teas-accounting/commit/635e21f3bd9938685009c3364b10bda639b87ca2))
* **fe:** Thai toast mappings for the r2 batch's new error codes ([7f8269b](https://github.com/pinsorn/teas-accounting/commit/7f8269bc3f121e42490ddb29ced680878486e2c4))
* **fixedasset:** refuse disposal dated before acquisition or depreciation start (r2 U5/L3-9) ([bb5d10a](https://github.com/pinsorn/teas-accounting/commit/bb5d10a27739b8df5a2faa454abba2dabcdc6d9d))
* **payroll:** refuse tax/SSO filing artifacts on a placeholder payer tax ID; seed 638 re-syncs company_profile (r2 U1/L1-1) ([ce54051](https://github.com/pinsorn/teas-accounting/commit/ce54051d12aa9e2aefb31144acaba4dddc716a50))
* **purchase,mcp:** PO tax-code resolver closes the last verbatim-id writer; MCP employee scope narrowed to lookup with manage back-compat; seed-640 direct-grant arm now tested (cleanup C1) ([1074384](https://github.com/pinsorn/teas-accounting/commit/10743842cd064e61b1d02c2f14a9f58063fc752a))
* **purchase:** a 50 ทวิ can no longer be issued with an all-zero payer tax ID ([9197077](https://github.com/pinsorn/teas-accounting/commit/91970772df34095d795e396c057fce945c59b99b))
* **rbac:** seed 181's role inserts silently no-op'd under FORCE RLS; seed 641 reconciles existing DBs (cleanup C11) ([a1b6b99](https://github.com/pinsorn/teas-accounting/commit/a1b6b994562fa825bbd38a9ec5543c127338b3b9))
* **reports,sales:** P&L default stops hiding untagged activity, and the tax-invoice header finally reports the discount it gave ([ee9a594](https://github.com/pinsorn/teas-accounting/commit/ee9a594aa1b37e1ff58df706f23f7d21d3bf4ce5))
* **sales:** billing note issue refuses when manual lines don't reconcile with linked tax invoices (O2b, Ham ruling 2026-08-20) ([10ed939](https://github.com/pinsorn/teas-accounting/commit/10ed939b8c9ac376c8fa19e90726c0ba800fbb90))
* **sales:** billing notes accept null tax codes, launder inherited ids against the company master; seed 639 repairs foreign tax_code_id on sales lines (r2 U2/L6-1+L6-4) ([9a780d5](https://github.com/pinsorn/teas-accounting/commit/9a780d53fdfb9bd2a12f9d3d6eda469c833cb96e))
* **sales:** document conversions stop losing the discount, and the tax code the user picks is the one that is charged ([d2afbdd](https://github.com/pinsorn/teas-accounting/commit/d2afbddd31148fa148c85d9dee079b9db640844f))
* **sales:** exempt products can no longer be charged VAT, one posted tax invoice per quotation, and tax-code lookup honours its ignore-case contract ([fa31f2b](https://github.com/pinsorn/teas-accounting/commit/fa31f2b69834914fa4341c46b1c6c1f98e4067af))
* **security:** an MCP api key can no longer mint a Tax Invoice it has no scope for ([a9244af](https://github.com/pinsorn/teas-accounting/commit/a9244afc4281e584f7ba6651d8f3285218658cd0))
* **seed:** a company seeded after script 510 no longer ends up with no roles ([7f86a00](https://github.com/pinsorn/teas-accounting/commit/7f86a00bd7127db2b2a18d97cbcf7ef112f79569))
* **tax:** extend the payer-tax-ID refusal to all remaining filing artifacts (r2 U10, Tier-2 N3) ([3e368e5](https://github.com/pinsorn/teas-accounting/commit/3e368e567abbeb6c98e34471dc0a69aefbb6e563))
* **ui:** convert buttons say why they are unavailable instead of failing on click ([4d4d492](https://github.com/pinsorn/teas-accounting/commit/4d4d492aca01753ca18ac452def76397638b5b5d))
* **ui:** payment-voucher preview showed the wrong amount leaving the bank ([6b54d23](https://github.com/pinsorn/teas-accounting/commit/6b54d230d7a9b5d42a91915a7ec99433f1bd5a4d))

## [2.2.1](https://github.com/pinsorn/teas-accounting/compare/v2.2.0...v2.2.1) (2026-08-14)


### Bug Fixes

* **api:** a lost double-post race now returns 409, not a raw 500 ([eb795e7](https://github.com/pinsorn/teas-accounting/commit/eb795e7ed20d504a9602698e5584ecd772202e4e))
* **security:** converting a document now requires permission to create the target, not just read the source ([91e5147](https://github.com/pinsorn/teas-accounting/commit/91e5147083fa334c5c4124094d6f84d839d0b8aa))

## [2.2.0](https://github.com/pinsorn/teas-accounting/compare/v2.1.0...v2.2.0) (2026-08-14)


### ⚠ BREAKING CHANGES

* **db:** make duplicate document numbers structurally impossible

### Features

* **db:** make duplicate document numbers structurally impossible ([36fb7e1](https://github.com/pinsorn/teas-accounting/commit/36fb7e165b0a448de585483cd522ea6cf518b943))


### Miscellaneous Chores

* release as 2.2.0, not 3.0.0 ([a18831b](https://github.com/pinsorn/teas-accounting/commit/a18831b1df4e9ccc9201c3a6c881a4bb0ad2d019))

## [2.1.0](https://github.com/pinsorn/teas-accounting/compare/v2.0.0...v2.1.0) (2026-08-13)


### Features

* **tax:** surface foreign-service payments that ภ.พ.36 would otherwise miss ([18f6fcc](https://github.com/pinsorn/teas-accounting/commit/18f6fcccd368a46bed510b893a337752e25abbd7))


### Bug Fixes

* **numbering:** document numbers are now sequenced per company, not per login channel ([ca820f5](https://github.com/pinsorn/teas-accounting/commit/ca820f533adcdc7f1564b1725fbe01d6d31b75ec))
* **release:** make the v2.0.0 deploy probes actually prove what they claim ([3c8e032](https://github.com/pinsorn/teas-accounting/commit/3c8e0322d9875e3897beb09c15f8e00f9ac58cc6))
* **security:** attachment download and delete now authorize against the parent document ([0381d60](https://github.com/pinsorn/teas-accounting/commit/0381d602ce91f58357203167ea69ccd5ca226068))

## [2.0.0](https://github.com/pinsorn/teas-accounting/compare/v1.28.0...v2.0.0) (2026-08-13)


### ⚠ BREAKING CHANGES

* **sales:** delete "customer has paid" — the receipt is the only proof of settlement

### Features

* **sales:** delete "customer has paid" — the receipt is the only proof of settlement ([01fc85f](https://github.com/pinsorn/teas-accounting/commit/01fc85f52ca893280905b17b6ab6e1b06c1f02a8))


### Bug Fixes

* **deps:** pin SSH.NET past GHSA-q939-rpr3-3284 — it was failing the build ([9b40940](https://github.com/pinsorn/teas-accounting/commit/9b4094051fa45fdb5f05ba2955675a65f017dc99))
* **payroll:** a government filing artifact now requires a Posted run ([55a572e](https://github.com/pinsorn/teas-accounting/commit/55a572ec9f78534cab422eb8a0b8328ea9532820))
* **payroll:** give a stuck payroll run a way out before the filing guard traps it ([4c9c7d9](https://github.com/pinsorn/teas-accounting/commit/4c9c7d95e377152bdcd0eda31a2e4bd0d723b39b))
* **payroll:** refuse a สปส.1-10 / ภ.ง.ด.1 that would ship silently-wrong data ([a46572d](https://github.com/pinsorn/teas-accounting/commit/a46572d4a2be15e0f3497a7620dcdd972bdcae42))
* **sales:** hide "create receipt" on a Settled invoice — it was a guaranteed 422 ([accfa6d](https://github.com/pinsorn/teas-accounting/commit/accfa6d23997e79b57f1d1f558d8cb15f5376744))
* **tax:** a company with no VAT registration can no longer file a ภ.พ.30 ([ffa7e82](https://github.com/pinsorn/teas-accounting/commit/ffa7e820d9afab35f1705d92631ab3ac6295730c))
* **tax:** reject a nonsense filing year on ภ.ง.ด.50/51 with 422, not a 500 ([722abc9](https://github.com/pinsorn/teas-accounting/commit/722abc9777b01875aca693a299b8d08167cb56ad))
* **tax:** ภ.พ.36 declares a foreign service once, at the payment tax point ([20308d2](https://github.com/pinsorn/teas-accounting/commit/20308d2eb8af59003b621fa3bde5662d155af0d4))

## [1.28.0](https://github.com/pinsorn/teas-accounting/compare/v1.27.1...v1.28.0) (2026-08-12)


### Features

* **gl:** R1/WP-2 — non-VAT AR backfill endpoint (preview/apply) for pre-WP-1 invoices ([2eb61c3](https://github.com/pinsorn/teas-accounting/commit/2eb61c38a3e5ebe71855091d2c75eaf93dc6a274))


### Bug Fixes

* **expense:** R1/WP-4 — an expense claim can no longer debit bank, AP, revenue or equity (C5) ([5111919](https://github.com/pinsorn/teas-accounting/commit/5111919c98c8f648f173baf41706f54d7c3f9d8e))
* **gl:** R1/WP-1 — non-VAT invoices accrue revenue+AR at issue (C6) ([e750780](https://github.com/pinsorn/teas-accounting/commit/e750780f318075462d926683102fce13c43b7cb4))
* **gl:** R1/WP-3 — reject sub-satang amounts at the posting seam (C1) ([7eaa81a](https://github.com/pinsorn/teas-accounting/commit/7eaa81ad48eb9d28a23d00edc546f9a4de8e0a95))
* **payroll:** R1/WP-5 — payroll can no longer post into a closed period (C3) ([018babe](https://github.com/pinsorn/teas-accounting/commit/018babe1bf236efaebfee9075991331ab66e78fe))
* **tools:** audit SQL — companies column is name_th, verified against prod ([b513e8a](https://github.com/pinsorn/teas-accounting/commit/b513e8a20c533e9ad6fd45b6dc6d95a44abc2fb2))
* **tools:** correct audit-subsatang.sql schema names after its first real prod run ([23e7f35](https://github.com/pinsorn/teas-accounting/commit/23e7f35df447960f45af317be9c32deb9d37ac0c))

## [1.27.1](https://github.com/pinsorn/teas-accounting/compare/v1.27.0...v1.27.1) (2026-07-30)


### Bug Fixes

* **journals:** approve banner no longer flashes 'no permission' while permissions load ([d1264d7](https://github.com/pinsorn/teas-accounting/commit/d1264d738d82e914f49682f1f413513aa81763e6))

## [1.27.0](https://github.com/pinsorn/teas-accounting/compare/v1.26.1...v1.27.0) (2026-07-30)


### Features

* **mcp:** create_manual_journal_draft — agents draft manual JVs, humans post ([10a8814](https://github.com/pinsorn/teas-accounting/commit/10a8814ee0ba24dbb45dbd9e12ec5311d3e5a6b2))

## [1.26.1](https://github.com/pinsorn/teas-accounting/compare/v1.26.0...v1.26.1) (2026-07-29)


### Bug Fixes

* **paper:** screen WHT totals mirror PaperFootPlan; INTR books to 5500; PV form ภ.ง.ด.2 hint ([4b7769b](https://github.com/pinsorn/teas-accounting/commit/4b7769b357f9bbf7eddf1af5ef754b5db5171f4e))

## [1.26.0](https://github.com/pinsorn/teas-accounting/compare/v1.25.0...v1.26.0) (2026-07-29)


### Features

* **tax:** ภ.ง.ด.2 filing for interest/dividends paid to individuals (ม.50(2)) ([d3c540b](https://github.com/pinsorn/teas-accounting/commit/d3c540b036a4aa2e9b2ba34320b8cdb973d08b3c))

## [1.25.0](https://github.com/pinsorn/teas-accounting/compare/v1.24.2...v1.25.0) (2026-07-29)


### Features

* **gl:** manual journal vouchers and chart-of-accounts management ([b00d639](https://github.com/pinsorn/teas-accounting/commit/b00d63934570f5efa594f65dad8697689525b999))

## [1.24.2](https://github.com/pinsorn/teas-accounting/compare/v1.24.1...v1.24.2) (2026-07-28)


### Bug Fixes

* **sales:** let a billing note save with an empty grid when tax invoices are linked ([c9e7f8a](https://github.com/pinsorn/teas-accounting/commit/c9e7f8a9444386ce16e4220199c66da2017f8867))

## [1.24.1](https://github.com/pinsorn/teas-accounting/compare/v1.24.0...v1.24.1) (2026-07-28)


### Bug Fixes

* **payroll:** seed 2180 per company so it survives RLS on prod ([48a220d](https://github.com/pinsorn/teas-accounting/commit/48a220dc9c736d0c4fd58de1c3df7560a309a9a5))

## [1.24.0](https://github.com/pinsorn/teas-accounting/compare/v1.23.0...v1.24.0) (2026-07-28)


### Features

* **fe:** expense-claim edit for Draft/Rejected, billing-note back-link chips, non-VAT PV VAT label (Wave 5: O4 / O2a / G5) ([d877286](https://github.com/pinsorn/teas-accounting/commit/d877286dbd4a2d32680296cc4396cd5ecf9e54e2))
* **gl:** reopen a closed monthly accounting period (O14) ([d6cce40](https://github.com/pinsorn/teas-accounting/commit/d6cce405a8779b341084064d96e7b43f6ad1b90f))
* **payroll:** net-pay deductions with a GL counterpart account (O10-A, backend) ([e62102f](https://github.com/pinsorn/teas-accounting/commit/e62102f11dec186026288f33497817dfe85528d3))
* **payroll:** persist and surface the deduction reason (O10-B) — O10 complete ([93d5ee4](https://github.com/pinsorn/teas-accounting/commit/93d5ee47855e6eeb4bf367c00d0ebd711dc28bdf))
* **payroll:** show the สปส.1-10 ส่วนที่ 2 schedule on screen (O11-alt) ([bf87333](https://github.com/pinsorn/teas-accounting/commit/bf87333e61240bcce0a1b990dc9aad7c637648e7))
* **sales:** linking tax invoices generates the billing-note lines (O2b) ([1706d72](https://github.com/pinsorn/teas-accounting/commit/1706d728f5a6d2cec01283b50b785ad649037b2b))

## [1.23.0](https://github.com/pinsorn/teas-accounting/compare/v1.22.12...v1.23.0) (2026-07-26)


### Features

* employee termination date, SSO account-no validation, PV DocDate boundary rule, fixed-asset no-GL-cost warning (Wave 1: O9/O12/O13/O1) ([3877df7](https://github.com/pinsorn/teas-accounting/commit/3877df7c12224320f5b220176d7a67bbd55c0216))
* **payroll:** calendar-day salary proration for mid-month joiners and leavers (O8) ([af51a6d](https://github.com/pinsorn/teas-accounting/commit/af51a6d688df9f991f8919c0f76a1212c14c745d))


### Bug Fixes

* **fe:** filter the pending-agent-approvals widget by the viewer's read permission (O7) ([d6568ef](https://github.com/pinsorn/teas-accounting/commit/d6568ef017488e92fb3dfbe9dceb0c5c0bf19e86))

## [1.22.12](https://github.com/pinsorn/teas-accounting/compare/v1.22.11...v1.22.12) (2026-07-25)


### Bug Fixes

* **fe:** PV VI-prefill uses the dual-flag VAT predicate (V1-F1) ([479baae](https://github.com/pinsorn/teas-accounting/commit/479baaebc234382c45e017279b89d433bbe61a15))
* **payroll:** RD/SSO filing PDFs gated on the filing permission, not payroll administration ([6b689be](https://github.com/pinsorn/teas-accounting/commit/6b689be0bd95b921d7bfa7cacf416971cae269e6))
* **pv:** non-VAT company gate on the payment-voucher path (army B2-nv F1/F2) ([2b6fc28](https://github.com/pinsorn/teas-accounting/commit/2b6fc28dddfe33e619e44339b31c6c874ba802f3))

## [1.22.11](https://github.com/pinsorn/teas-accounting/compare/v1.22.10...v1.22.11) (2026-07-25)


### Bug Fixes

* **bank:** K-Plus PDF real-statement parse — margin-watermark row-bridging + footer fake-row (army B-br F1) + parse-error 422 hardening ([b71e5cd](https://github.com/pinsorn/teas-accounting/commit/b71e5cdfa21d1362ba90244f3f9f7e85b305045f))
* **company,mcp:** super-admin company update RLS re-pin (raw 500 on tax-field edit) + MCP ArgumentException surfacing ([a8d54b4](https://github.com/pinsorn/teas-accounting/commit/a8d54b49c330d570293c99a6724b6130b69c6d20))
* **fe:** expense-claim status i18n (Submitted/Paid), depreciation already-posted toast, expense-claims 403 clean-deny ([aaf62c5](https://github.com/pinsorn/teas-accounting/commit/aaf62c59bc5046780bc38aa719a4d07493275165))
* **gl,pv:** self-withhold gross-up debit on VI-linked PV posting + dual-flag VAT derivation on PV form ([e17d232](https://github.com/pinsorn/teas-accounting/commit/e17d232a2bbe159713d1d79065e50d127d2f64de))
* **pv:** WHT income-type validated at draft-save/approve + Draft/Approved cancel escape hatch + live PV concurrency token ([3835e96](https://github.com/pinsorn/teas-accounting/commit/3835e96e36b98fcecc6193f38bb6e0d5bff3a4d8))

## [1.22.10](https://github.com/pinsorn/teas-accounting/compare/v1.22.9...v1.22.10) (2026-07-22)


### Bug Fixes

* **expense,purchase:** non-VAT company VAT guard + purchase-paper vendor address + UX polish ([c1f54d8](https://github.com/pinsorn/teas-accounting/commit/c1f54d83d5fcd68b31cd3851aa2b1484025a4e19))
* **fe:** round-5 residual LOW nits — Thai pnd30 toast, bank-recon 403-vs-empty, deny-gate mount race ([1550e39](https://github.com/pinsorn/teas-accounting/commit/1550e3983fdb5347e23a59fac1ddfb7513815ac3))

## [1.22.9](https://github.com/pinsorn/teas-accounting/compare/v1.22.8...v1.22.9) (2026-07-21)


### Bug Fixes

* **deps:** bump System.Security.Cryptography.Xml 10.0.8 -&gt; 10.0.10 (NU1903 HIGH advisory) ([0829332](https://github.com/pinsorn/teas-accounting/commit/0829332fe6c8bee15a8fc8aa98ab8085ad781caa))

## [1.22.8](https://github.com/pinsorn/teas-accounting/compare/v1.22.7...v1.22.8) (2026-07-21)


### Bug Fixes

* **rbac,fe:** WP1 route-guard on write pages + WP2 auditor/approver read grants ([5c49234](https://github.com/pinsorn/teas-accounting/commit/5c49234325f4a0b0a77017e3b8806a97fbdd1545))
* **rbac:** WP6 read/manage split for quotation/SO/DO/vendor/business-unit ([4e1407c](https://github.com/pinsorn/teas-accounting/commit/4e1407c23c6b9c66d04f46b1f8169b176fadd629))
* **reports,settings:** WP3 reports UX clarity + WP5 misc gate/guard/i18n ([043935c](https://github.com/pinsorn/teas-accounting/commit/043935ce9678d07df4e654bc93585f7b8169ce4e))

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
