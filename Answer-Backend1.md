# Answer-Backend1 — Decisions on Report-Backend1

**Date:** 2026-05-16  
**From:** Ham (via Sana, Cowork)  
**To:** Claude Code  
**Re:** [Report-Backend1.md](./Report-Backend1.md) §4 + §5

> Excellent report. Backend Phase 1 is signed off. Decisions below are **binding** for the next sprint.
> Anything not addressed here = your call within CLAUDE.md §9 ("Skill Boundaries").

---

## 0. ✅ Sign-off

- Build clean (0/0), all 5 backlog items + bonus e-Tax XAdES inert: **accepted**
- The 9 latent bugs you found and fixed are exactly the kind of thing the runtime smoke
  was meant to flush out — good catch. Don't second-guess: ship.

---

## 1. Q1 — e-Tax C14N (was: BLOCKING) → **RESOLVED**

**Decision: Path 3 (corrected) — fix the spec, not the implementation strategy.**

### Root cause: spec was wrong, not your code

The previous `etax-xades-spec.md` §1 said "use inclusive C14N everywhere". That was a misreading
of the ETDA Java sample. The Java sample doesn't override C14N for the SignedProperties
Reference, which means it uses **xades4j's default** — which is **Exclusive C14N**
(`http://www.w3.org/2001/10/xml-exc-c14n#`), per ETSI TS 101 903 §6.3.1 + xades4j
`XadesBesSigningProfile` defaults.

Your hesitation was correct — CLAUDE.md §8 worked exactly as intended. Thank you for not
silently switching to Exclusive C14N to make a test pass.

### What I (Sana) already did

Updated `docs/etax-xades-spec.md` with:

1. **§1 algorithm table** — split the single "Canonicalization" row into three:
   - Outer `SignedInfo CanonicalizationMethod` = **Inclusive C14N** (unchanged)
   - Data Reference Transform after Enveloped = **Inclusive C14N** (unchanged)
   - **SignedProperties Reference Transform = Exclusive C14N** ⚠ (this is the fix)
2. **§1 errata callout** — explains the misreading + why namespace context differs
3. **§2 example XML** — added explicit `<ds:Transforms><ds:Transform Algorithm=".../xml-exc-c14n#"/></ds:Transforms>` inside the SignedProperties Reference
4. **§3.4 .NET signer code** — added `spRef.AddTransform(new XmlDsigExcC14NTransform());` before `signedXml.AddReference(spRef);` with a `// REQUIRED` comment

### Your next moves

1. Re-pull `docs/etax-xades-spec.md`, re-read §1 + §3.4 (just the diff).
2. Add the `XmlDsigExcC14NTransform` to `XadesBesSigner` (one line).
3. **Un-skip** the 3 round-trip tests in `EtaxXadesSignerTests` (or wherever they are) — they
   should now pass on a clean doc and fail on tampering.
4. Add a 4th test: signed XML round-trips through `string` → re-parse → still verifies
   (catches BOM / encoding regressions).
5. Keep `ETaxBehaviorOptions.Enabled = false` — production is still gated on §4.2.

**No need to email ETDA.** The spec correction is internally consistent with the Java reference.
Once tests are green, mark this issue closed in `progress.md`.

---

## 2. Q2 — ETDA sandbox cert: **NOT YET, NOT BLOCKING**

Status: I have not yet registered TEAS as a Service Provider with RD (it's a Phase 0 prereq
in `docs/accounting-system-plan.md` §22 with 4-6 week lead time).

**What you do meanwhile:**
- Keep using the in-memory self-signed dev cert for tests.
- `ETaxBehaviorOptions.Enabled = false` stays.
- When I get the sandbox cert + endpoint, I'll drop them into `appsettings.<env>.json` /
  user-secrets and ask you to re-test against ETDA UAT in a follow-up report.

No code change required from you for this question.

---

## 3. Q3 — Priority for next sprint: **VERTICAL SLICES**

### Sprint outline (you decide pace; this is the **order**)

#### Sprint 1 — Harden Phase 1 (target: 1-2 days)

Pick the highest-leverage tests from your "test depth" list:

1. **NumberSequence concurrency** — a parallel-task test that fires N concurrent posts and
   asserts `0..N-1` allocation with no gaps and no dupes. The recent ON CONFLICT rewrite
   needs a regression test.
2. **Tenant isolation idempotency** — the open issue from §2.9 of your report. Use
   randomized customer codes or proper per-test DB teardown so the suite re-runs cleanly.
3. **Period gating** — JV in a closed period must reject; opening period is admin-only.
4. **PV + WHT happy path** — already touched once, give it a real integration test:
   create vendor → vendor invoice → PV with WHT 3% → 50 ทวิ generated → JV balanced.
5. **Number-gap audit view** — query `tax.v_number_gaps` after a known-good run and after
   a forced fail; the failed sequence value must NOT appear (you fixed this in the
   ON CONFLICT rewrite — pin it down with a test).

Don't bloat: 5 tests, real PG, ship.

#### Sprint 2+ — Tax Invoice end-to-end vertical slice

The first user-facing slice through both stacks. Goal: a non-developer can issue a Tax
Invoice in the browser end-to-end.

**Backend** (likely small — most of this exists):
- TI list endpoint with filter/sort/paginate (cursor-based per `openapi.yaml`).
- TI detail endpoint with `/xml` and `/pdf` download.
- TI `resend` endpoint (no-op until ETax enabled).

**Frontend** (this is the meat):
- Login screen (BFF cookie wiring is already done — §4.3 of the report).
- Dashboard with the 4 hero stats from `Design(UI).md` §5.1 (placeholders fine if data is light).
- Tax Invoice list (`Design(UI).md` §7.1).
- Tax Invoice create form (`Design(UI).md` §7.7) — incl. the Post Confirm dialog.
- Tax Invoice detail/print view (`Design(UI).md` §7.6).
- Use **DaisyUI** components per `frontend/tailwind.config.ts` (theme `teas` / `teas-dark`).
- Use the **`ui-ux-pro-max`** plugin (CLAUDE.md §0.1.b) — that's what it's there for.

**Why this slice and not "all of Phase 2 backend":** demoing a working invoice flow proves
the architecture end-to-end and gets feedback from real users before we sink time into PND3/53,
Fixed Assets, Quotation→SO→DO, etc. Those are next-quarter work.

#### Deferred (next-next, do not start now)

- Quotation → SO → DO chain
- Vendor Invoice 3-way match
- PND3/53 returns
- Fixed Assets module
- ภ.พ.30 auto-submit (still manual mode per `appsettings`)

These are all in `accounting-system-plan.md` §22 — pick from there in the order I just laid out
once Sprint 2's slice is on the screen.

---

## 4. Q4 — Code location: **DO NOT RELOCATE** (decision changed)

**Keep current setup:**
- `code/` is canonical (Cowork-side, where `CLAUDE.md` + `docs/` + `Report-Backend1.md` live)
- `Y:\AccountApp\backend` is your **build/test mirror** (workaround for the ~230-char path issue)
- Mirror direction: `code/` → `Y:\AccountApp` (one-way, robocopy)
- Cowork outputs/ stays as the source of truth for documentation + spec edits

**Why not relocate** (I considered it earlier and changed my mind):
- Cowork session integration to `code/` lets me (Sana) keep doc-level oversight without
  asking for a new directory mount each time.
- The mirror has been stable since you set it up — if it ain't broke.
- Drift risk is real but bounded: docs are Sana-edited (one direction), code is Claude-Code-edited
  (the other) — overlap is minimal.

**Rules going forward to keep mirror clean:**
- **You** (Claude Code) own everything under `code/backend/`, `code/frontend/`, `code/db/`,
  `code/infra/`, `code/design/`, `code/tests/` — Sana will not edit there without asking.
- **Sana** owns `code/docs/`, `code/CLAUDE.md`, `code/Report-Backend*.md`,
  `code/Answer-Backend*.md`, root-level `*.md` — Claude Code can read but should ping me
  before editing (drop a line in `progress.md` like "needs CLAUDE.md update for X").
- If you need to change a `docs/*.md` (like the spec correction above), tell me and I'll
  do it — that's why this answer exists.
- Mirror script: keep running it after every Claude Code commit. Use `/MIR /XD code/.git`
  if we ever add `.git` to `code/`.

---

## 5. Open Questions Going Forward

You raised these in §5 of the report. Updated answers:

1. ~~e-Tax C14N~~ → **Resolved.** Use Exclusive C14N for SignedProperties Reference
   (per spec correction). Production-validate with `xmlsec1` + ETDA UAT when cert arrives.
2. **ETDA sandbox cert** → I'll deliver when registration completes (4-6 weeks).
3. **Backlog priority** → Sprint 1 hardening tests, Sprint 2 TI vertical slice (UI + API);
   defer everything else per §3 above.
4. **Code path** → Keep mirror; ownership rules in §4 above.

---

## 6. Notes for Coordination

- **Don't expect me to edit code files in `code/backend/` or `code/frontend/`.** If a
  doc/spec correction implies a code change, I'll write it in the `Answer-Backend*.md`
  and you implement.
- **Reports cadence:** keep the `Report-Backend{N}.md` rhythm — they're useful. Each
  sprint produces one. Sprint 1 wrap-up will be `Report-Backend2.md`.
- **`progress.md` + `plan.md`** — keep updating those (append-only is fine). They're your
  primary log.
- If you hit something that contradicts CLAUDE.md or the spec docs, **flag it like you did
  for C14N** — don't silently work around. That escalation path worked.

---

## 7. Action Items Checklist

For Claude Code, in order:

- [ ] Re-read `docs/etax-xades-spec.md` (lines around §1 errata + §3.4 spRef AddTransform)
- [ ] Add `XmlDsigExcC14NTransform()` to `XadesBesSigner.SignedProperties` reference
- [ ] Un-skip the 3 round-trip XAdES tests; add 4th (string round-trip + BOM check)
- [ ] Run `dotnet test` — all green
- [ ] Update `progress.md` — close C14N item
- [ ] Begin Sprint 1 hardening tests (5 listed in §3 above)
- [ ] Acknowledge Q4 mirror rules in `plan.md` (technical-debt log)

Then write `Report-Backend2.md` when Sprint 1 done.

---

**Acknowledge by editing `progress.md` with a single line: "Answer-Backend1 received, executing."**
