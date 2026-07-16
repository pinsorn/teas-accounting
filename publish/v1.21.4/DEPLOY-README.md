# Deploy v1.21.4 — sales-side UX fix round (2026-07-16)

Tag `v1.21.4` @ f09cfcf (release-please PR #83, admin-merged — CI doesn't fire on the
release-please branch, see troubles-wiki). Prod teas.kazaki-rio.com (OVH VPS, pm2
teas-api + teas-web). **API + FE both deployed.**

## What shipped
Three commits from the sales UX findings fix round (S1–S16, all 16 findings from
REPORT-sales-uxtest.md, Ham-approved "แก้ทุก finding"):
- **83e47f9 WP-A backend**: BU in QT/SO/DO list DTOs (S4), company BU requirement on
  create/edit/send gates (S9), invoice due date honors customer credit term (S14),
  draft-edit activity entries (S12-BE), **new `PUT /sales-orders/{id}` draft-update
  endpoint** (S15-BE), double-call transition guards verified (S13b, test-covered).
  17 new integration tests (SalesUxFixesWpATests).
- **e71f3e3 WP-B frontend flow**: confirm dialogs on QT send/accept/reject + SO post +
  INV issue/mark-settled (S11), live side-rail refetch + activity wording (S12-FE),
  SO+INV draft edit routes (S15), receipt BU prefill from invoice (S16), BU badge on
  detail pages (S10), BFF proxy 30s timeout with distinct 504 (S13a).
- **996d91a WP-C polish**: hydration skeleton + vatMode never-flash (S1), breadcrumb
  i18n all routes (S2), status filter Thai labels (S3), BE date hints on forms + list
  filters (S5/S7), customer picker create link (S8).

## No schema change
Pure `fix()` commits — **no new EF migration, no new SqlScript**. `applied_sql_scripts`
must stay **69** (unchanged) post-deploy. DB backup taken anyway (mandatory per SOP).

## Result (verified)
- `deploy-api-v1214.sh`: DB backed up (`teas-pre-v1.21.4-deploy-*.sql.gz`, 281820B),
  all probes PASS incl. `total_sql_scripts_unchanged=69`, `version=1.21.4`, and the
  **new S15-B probe** `sales_order_put_route_exists=401` (unauthenticated `PUT
  /sales-orders/999999999` — auth gate fires before the handler runs, confirming the
  route exists without mutating any real doc) → `DEPLOY_OK`.
- `deploy-fe-v1214.sh`: full `git archive v1.21.4 frontend` overlay
  (`--strip-components=1`), content-check on the S13a proxy-timeout anchor
  (`AbortSignal.timeout(30_000)` + `classifyUpstreamFailure` import), no pnpm install
  (deps unchanged), `next build` OK, teas-web online, login 200 → `FE_DEPLOY_OK`.
- Public E2E through `teas.kazaki-rio.com`: login 200, `/mcp` 401, `/.well-known`
  200, and `PUT /api/proxy/sales-orders/999999999` 401 through the full CDN→NPM→app
  path (same non-mutating check as the API-local probe) → GREEN.
- Quotation-send double-call safety (S13b): verified by the 17 new backend tests
  (`SalesUxFixesWpATests`, part of the pre-deploy gate) — no live-prod probe, per plan.

## Known gap — footer version + live BU-column check
Footer `/system/info` requires an authenticated bearer token (fetched server-side in
`app/(dashboard)/layout.tsx` using the logged-in user's own session) — there is no
public/unauthenticated version endpoint. Same for confirming `businessUnitId` renders
correctly on the live `/quotations` list. Both need a fresh Ham login (session is
Ham-only, matches the pattern from every prior v1.21.x round) — **not yet verified live,
left for Ham.** All automated, non-credentialed probes (API + FE deploy gates, public
E2E) are green.

## Steps (repro)
1. `git worktree add <path> v1.21.4` at the REAL repo path (NOT subst — MinVer stamps
   0.0.0 from a subst drive). Verified: `Y:` and `Z:` are both real fixed disks
   (`DriveType=3`) this run.
2. `dotnet publish backend/src/Accounting.Api -c Release -r linux-x64 --self-contained
   -o out`; MinVer stamp confirmed `1.21.4` via
   `grep -o '"Accounting.Api/[0-9.]*"' out/Accounting.Api.deps.json`.
   `tar -cf teas-api-1.21.4-sc.tar -C out .`; `git archive v1.21.4 frontend -o
   fe-src-v1214.tar`. FE `tsc --noEmit` run clean from the real path (`Y:`, already
   fast-forwarded to the tag) — no local `next build` (that happens on the VPS).
3. scp both tars + both scripts to `/tmp`; md5-verified remote == local for both tars.
4. `sed -i 's/\r$//'` each script (CRLF), `bash /tmp/deploy-api-v1214.sh` then
   `deploy-fe-v1214.sh`.
5. Public E2E through the domain.
Rollback: each script keeps `unpacked.old` / `.next.old` and auto-rolls-back on any
gate fail (neither triggered this run — clean DEPLOY_OK / FE_DEPLOY_OK).
