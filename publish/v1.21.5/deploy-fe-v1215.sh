#!/bin/bash
# FE deploy v1.21.5 — payroll+reports UX fix round (W1 7bb293d, W2 ce9aba1, W3 c71c13b):
# blank-toast fix in openPdf/downloadFile (P1), global-error + dashboard error boundaries
# w/ ChunkLoadError auto-reload-once (R1), employees modal fresh-seed + error toast (P2/P4),
# i18n common.yes/no + report.total (P3/R3), P&L dev-note removal (R2), payroll zero-salary
# warnings + payslip breakdown modal (P5/P6), CE period hint, report date consistency (R4),
# TB/BS/P&L CSV + financial-statements PDF buttons (R5), shared csvCell w/ OWASP guard,
# date presets + defaults (R7), GL picker code-prefix (R8), bank-recon empty link (R9),
# outstanding-po copy (R11), sales-summary basis footnote (R6).
# package.json + pnpm-lock UNCHANGED vs v1.21.4 -> NO pnpm install. Full
# `git archive v1.21.5 frontend` overlay (--strip-components=1). Rebuild, restart, auto-rollback.
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old

echo "== overlay new source =="
tar xf /tmp/fe-src-v1215.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v1215.tar | wc -l) entries"
for f in 'app/global-error.tsx' \
         'app/(dashboard)/error.tsx' \
         'lib/api.ts' \
         'lib/utils.ts' \
         'app/(dashboard)/payroll/[id]/page.tsx' \
         'app/(dashboard)/reports/trial-balance/page.tsx'; do
  test -e "$f" || { echo "FILE_MISSING: $f -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
done
grep -q 'throwFileResponseError' 'lib/api.ts' || { echo "CONTENT_CHECK_FAILED P1-blank-toast -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'teas-chunk-reload-attempted' 'app/global-error.tsx' || { echo "CONTENT_CHECK_FAILED R1-chunk-retry -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'payslipModal' 'messages/th.json' || { echo "CONTENT_CHECK_FAILED P6-breakdown-i18n -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'function csvCell' 'lib/utils.ts' || { echo "CONTENT_CHECK_FAILED W3-shared-csvcell -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
echo "FILES_PRESENT ok"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v1215.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v1215.log
  mv .next .next.broken-v1215; mv .next.old .next
  echo "RESTORED old .next"
  exit 1
fi
echo "BUILD_OK"

echo "== restart + verify =="
pm2 restart teas-web >/dev/null 2>&1
sleep 10
ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-web")|.pm2_env.status')
LOGIN=$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:3100/login)
PDF=$(curl -s -o /dev/null -w '%{http_code}' 'http://127.0.0.1:3100/public/pdf?t=garbage')
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$LOGIN" = "200" ] && echo "PASS login=$LOGIN" || echo "FAIL login=$LOGIN"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
if [ "$ST" = "online" ] && [ "$LOGIN" = "200" ] && [ "$PDF" = "404" ]; then
  echo "FE_DEPLOY_OK"
  rm -rf .next.old
else
  echo "FE_DEPLOY_FAILED -- ROLLING BACK"
  pm2 logs teas-web --lines 12 --nostream 2>&1 | tail -6
  mv .next .next.broken-v1215; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
