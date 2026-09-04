# WP-E — frontend CI gates + working ESLint (GPT-5.6 review MEDIUM-03)

Board: `PLAN-gpt56-review-2026-09-04.md` §2 row E. Blast cap: **6 files**. No commits (orchestrator
commits). Repo: Y:\ClaudePlayground\TEAS-Project. Frontend tooling only — you never run `dotnet`.

## 0. Headline
CI's frontend job is install + `tsc` only; `pnpm lint` (`next lint`) opens an interactive wizard
because there is no ESLint config; CI pins pnpm 9 while `package.json` declares `pnpm@10.33.4` and
the Dockerfile uses corepack (pnpm 10, node 22). Fix = a flat ESLint config, a deterministic lint
script, and a CI job that runs vitest + lint + build on the same toolchain as Docker.
**Baseline rule:** this WP fixes NO source files. Pre-existing lint violations become `warn` via
config so CI stays green today; the counts are reported for the follow-up burn-down (WP-G).

## 1. Facts (VERIFIED 2026-09-04)
- `.github/workflows/ci.yml:42-58` frontend job: checkout · setup-node 20 · pnpm/action-setup@v4
  `version: 9` · `pnpm install --frozen-lockfile` · `pnpm exec tsc --noEmit`. Backend job :9-40
  (postgres:16 service, `TEAS_TEST_PG`, `TEAS_REPO_ROOT`, build + test) — DO NOT TOUCH.
- CI is green on main today (`gh run list` 2026-09-04) — the pnpm 9/10 mismatch does not hard-fail.
- `frontend/package.json`: `"lint": "next lint"`, `"packageManager": "pnpm@10.33.4"`,
  devDeps `eslint ^9.13.0`, `eslint-config-next 15.5.18`, `next 15.5.18`. No `eslint.config.*`,
  no `.eslintrc*`, no `eslintConfig` key anywhere in `frontend/` or repo root.
- `frontend/Dockerfile:9,15` `node:22-alpine` + `corepack enable` → pnpm from `packageManager`.
- `frontend/next.config.ts:22-26` `typescript: { ignoreBuildErrors: true }` with a documented
  reason (deploy-box OOM when tsc ran inside `docker build`). KEEP IT; CI now covers the gap.
- Unit tests: 15 vitest files (12 under `frontend/lib`, plus `app/(dashboard)/number-gaps/page.test.tsx`,
  `components/doc/ActivityLog.test.ts`, `components/paper/PaperFoot.test.ts`); reviewer ran them
  green (70 tests). Check the `test` script in package.json — if it is `vitest` (watch mode), CI
  must call `pnpm vitest run` (or `pnpm test -- --run`).
- Other workers hold FE source files right now (`frontend/app/api/**`, `frontend/lib/proxy-error.ts`,
  later `vendor-invoices/new/page.tsx`, `lib/po-line-vat*`, `lib/types.ts`). You do not edit any
  `.ts/.tsx` source. Local `next dev` on :3000 may be running — never run `next build` locally
  (it clobbers `.next/` under the dev server); CI verifies the build.

## 2. Design (exact)
### 2.1 `frontend/eslint.config.mjs` (new)
```js
import { FlatCompat } from '@eslint/eslintrc';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const compat = new FlatCompat({ baseDirectory: dirname(fileURLToPath(import.meta.url)) });

export default [
  { ignores: ['.next/**', 'node_modules/**', 'playwright-report/**', 'test-results/**', 'next-env.d.ts', 'coverage/**'] },
  ...compat.extends('next/core-web-vitals', 'next/typescript'),
  {
    rules: {
      // TODO(lint-baseline 2026-09-04): rules below are downgraded to `warn` because the codebase
      // has pre-existing violations (counts in specs/fix-fe-ci-lint-gates.md attempt log).
      // WP-G burns them down and flips them back to `error`.
    },
  },
];
```
Only add a rule to that block if `pnpm lint` reports it as an ERROR on the current tree; add
`'<rule>': 'warn'` with the count in a trailing comment. Warnings need no entry.
### 2.2 `frontend/package.json`
- `"lint": "eslint ."` (drop `next lint` — removed in Next 16, and the wizard is the current bug).
- devDependency `@eslint/eslintrc` (latest 3.x) — `pnpm add -D @eslint/eslintrc` from `frontend/`
  (this rewrites `pnpm-lock.yaml`; that is the 4th file). Run the install FIRST, before anything
  else, so the `node_modules` relink is over before other workers' vitest runs.
- Do NOT bump next/eslint/eslint-config-next.
### 2.3 `.github/workflows/ci.yml` — frontend job only
```yaml
  frontend:
    runs-on: ubuntu-latest
    defaults: { run: { working-directory: frontend } }
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4          # no `version:` — reads packageManager (pnpm@10.33.4), same as Docker
      - uses: actions/setup-node@v4
        with: { node-version: "22", cache: pnpm, cache-dependency-path: frontend/pnpm-lock.yaml }
      - name: Install
        run: pnpm install --frozen-lockfile
      - name: Typecheck
        run: pnpm exec tsc --noEmit
      - name: Lint
        run: pnpm lint
      - name: Unit tests
        run: pnpm vitest run
      - name: Build
        run: pnpm build
```
Order matters: pnpm/action-setup BEFORE setup-node when `cache: pnpm` is used. Keep the job name
`frontend` (branch protection will reference it). If `pnpm build` needs env (`NEXT_PUBLIC_*`,
`API_URL`), grep `frontend/.env.example` / `next.config.ts` / `docker-compose.coolify.yml` for what
the Docker build passes and add the same as job `env:` with placeholder values — never secrets.
### 2.4 `.gitignore` — only if `eslint .` produces a cache file (`.eslintcache`) — add it.

## 3. Invariants
- I1 `pnpm lint` exits 0 non-interactively on the current tree (warnings allowed) — T1.
- I2 A deliberately introduced lint ERROR fails `pnpm lint` — T2 (temporary file, deleted after).
- I3 CI frontend job runs tsc + lint + vitest + build on node 22 / pnpm 10.33.4 — T3 (CI run on
  the branch/PR; Fable triggers by pushing — you only prepare the diff).
- I4 No source file changes; `git status` shows exactly: `eslint.config.mjs`, `package.json`,
  `pnpm-lock.yaml`, `ci.yml` (+ `.gitignore` if 2.4).

## 4. Checklist
- [ ] `pnpm add -D @eslint/eslintrc` (first action).
- [ ] `eslint.config.mjs` per 2.1, with baseline downgrades only for ERROR-level rules found.
- [ ] `package.json` lint script.
- [ ] `ci.yml` frontend job per 2.3.
- [ ] Baseline counts recorded in the Attempt log: total errors before downgrades, per-rule
      counts, total warnings after.
- [ ] T1, T2 evidence pasted.

## 5. Tests
- T1 `pnpm lint` → exit 0; paste the summary line (`✖ N problems (0 errors, M warnings)`).
- T2 create `frontend/lib/__lint_probe.ts` containing `const x: any = 1; export {}` (or another
  construct that is an `error` under the active config — e.g. `no-unused-vars` after removing the
  export), run `pnpm lint` → exit ≠ 0 naming that file; DELETE the probe; rerun → exit 0.
- T3 (Fable) CI run on the pushed branch: all five steps green.
- Also run `pnpm exec tsc --noEmit` → 0 and `pnpm vitest run` → paste `Tests N passed` (this is
  the local proof that the CI step command is right; vitest is read-only on the tree).

## 6. Gates (worker)
T1, T2, tsc, vitest as above. NO `pnpm build` locally. NO `dotnet`. Do not edit any `.ts/.tsx`
outside the temporary probe.

## 7. Out of scope
Fixing lint violations (WP-G) · Playwright in CI (separate follow-up) · `ignoreBuildErrors` ·
backend job · Dockerfile · branch protection (Fable via `gh api` after the job lands).

## 8. Blast-radius cap
Max 6 files: `frontend/eslint.config.mjs`, `frontend/package.json`, `frontend/pnpm-lock.yaml`,
`.github/workflows/ci.yml`, `.gitignore`, `specs/fix-fe-ci-lint-gates.md`. Stop-and-re-spec if:
`eslint-config-next 15.5.18` does not work through FlatCompat (report the exact error and the
`eslint-config-next` flat-export alternative); the baseline shows > 200 errors (then discuss
whether to start from `next/core-web-vitals` only); `pnpm build` in CI needs a secret.

## Attempt log
- 2026-09-04 Fable: spec written; dispatch after Round 1a lands (node_modules relink must not
  overlap other workers' vitest).
