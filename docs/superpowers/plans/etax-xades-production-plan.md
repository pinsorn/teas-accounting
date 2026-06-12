# e-Tax XAdES-BES — Production Activation Plan (STANDALONE, GATED)

> **STATUS: DO NOT EXECUTE.** Ham directive 2026-06-12: this plan lives separately from `plan.md`
> and is **not to be worked on until Ham explicitly orders it**. Activation gate: company revenue
> approaching **฿30M** (e-Tax readiness becomes a business requirement at that scale; until then
> the pipeline stays inert). Any session that stumbles onto this file: record it, do not start it.

---

## 0. Where things stand today (already built, inert)

Everything below is **shipped and committed**; the pipeline is dormant behind
`ETaxBehaviorOptions.Enabled = false` (never signs/sends at runtime).

- **Signer** (per `docs/etax-xades-spec.md` §1/§5): `XadesNs`, `QualifyingPropertiesBuilder`,
  `XadesBesSigner` — RSA-SHA512, SHA-512 digests, C14N **inclusive**, XAdES v1.3.2, 2 signed
  References (doc + `SignedProperties`), decimal X509SerialNumber, BOM-free output.
  `X509CertificateLoader` + custom `XadesSignedXml.GetIdElement` resolving `#SignedProperties`.
- **Pipeline** (Sprint 13c): `IETaxSubmissionPipeline` (build→sign→validate→send),
  `ETaxRetryWorker` (backoff 1m…24h, dead-letter @6), `etax.submissions` append-only audit
  (+ DB trigger rejecting UPDATE/DELETE), `ETaxRecipientResolver` redirect/whitelist (Tier-2 safety),
  `LocalXsdValidator` (graceful skip in Tier 1), `MockRdEfilingClient` + HTTP client skeleton.
- **Tier model**: `docs/etax-environment-tiers.md` — Tier 1 (mock, local) → Tier 2 (ETDA sandbox)
  → Tier 3 (production). Config-only promotion; no code change between tiers.
- **Structural proof**: test `Emits_mandatory_xades_profile_per_spec` passes (structure +
  algorithms). 3 round-trip self-verify tests are honestly `Skip`-ped (see Blocker B1).

## 1. Blockers (must clear IN ORDER before production)

### B1 — C14N round-trip question (decision: Ham + ETDA, do NOT improvise)
.NET `SignedXml.CheckSignature` cannot self-verify our XAdES output: it canonicalizes
`SignedProperties` as a standalone DataObject fragment at sign time vs an in-tree node at verify
time; spec §1's **inclusive** C14N then captures ancestor-scope namespaces at verify →
SignedProperties digest mismatch. Exclusive C14N would fix it but **violates spec §1**.
Resolution options (pick ONE with evidence):
  (a) validate with ETDA's official reference validator / `xmlsec1` instead of .NET CheckSignature;
  (b) custom canonicalizer that pins the namespace context;
  (c) ETDA confirms exclusive C14N is accepted (some ETDA samples use Excl) → spec errata via Sana.
**Exit criterion:** a signed sample validates green on an ETDA-recognized validator, documented.

### B2 — Signing certificate
- Sandbox: ETDA test cert. Production: CA-issued `.pfx` (Thailand NRCA/TUC).
- Wire via `.env` `ETax:Signing:PfxPath`/`PfxPassword` — **never committed** (gitignore already blocks).
- Procurement lead time: CA issuance requires company paperwork — start ~1 month before go-live.

### B3 — ETDA sandbox UAT (Tier 2)
1. Obtain sandbox credentials + ETDA XSD bundle (unblocks `LocalXsdValidator` strict mode).
2. Submit a signed test invoice end-to-end (real SMTP + cc sandbox RD address per tier doc).
3. Confirm ETDA parses `xades:SigningCertificate` / `SigningTime`; resolve B1 question there.
4. Exercise the retry worker against induced failures (bad XML, SMTP down) — audit rows correct.

### B4 — Production cutover (Tier 3)
- Flip config in a non-prod env first; then production `ETax:Email:RdCcAddress=csemail@rd.go.th`,
  `Enabled=true`. Per-invoice real-time send; after submit status=SUBMITTED; errors → CN + reissue
  (§4.4 — submitted XML is immutable, never resigned in place).
- Day-1 smoke: one real low-value invoice, verify RD cc + customer receipt + audit chain, then ramp.

## 2. Work items when activated (estimate: 2–3 sessions)

| # | Item | Notes |
|---|---|---|
| 1 | Resolve B1 (validator evidence + decision record) | needs ETDA contact via Ham |
| 2 | Un-skip the 3 round-trip tests against the chosen validator path | keep .NET-CheckSignature skips documented if (a) |
| 3 | ETDA XSD bundle → `LocalXsdValidator` strict in Tier 2+ | ops prereq, flagged in 13c |
| 4 | Tier-2 UAT checklist (B3) run + evidence in progress.md | real SMTP, sandbox cc |
| 5 | Cert procurement + secret wiring (B2) | `.env` only |
| 6 | Tier-3 cutover + day-1 smoke (B4) | Ham present |
| 7 | FE: submission audit viewer (`GET /etax/submissions` UI) | deferred from 13c, nice-to-have |

## 3. Hard rules carried from CLAUDE.md (§4.4, §10, §11)

- Anything touching e-Tax submission = **ASK Ham first** — this whole plan is opt-in by order.
- Posted/submitted documents immutable; corrections via CN only.
- No improvising on compliance: if spec and reality conflict (B1), escalate — the escalation
  path already worked once; reuse it.
- `docs/Design(Architect).md` stays untouched.

## 4. References

- `docs/etax-xades-spec.md` (signing profile, §1 C14N, §5 self-verify)
- `docs/etax-environment-tiers.md` (Tier 1→2→3 config promotion)
- `plan.md` "TECHNICAL DEBT — e-Tax XAdES-BES" (historical record; superseded by this file as
  the actionable plan)
- Sprint 13c record (`plan.md` §Sprint-13c, Report-Backend18) — pipeline build evidence
