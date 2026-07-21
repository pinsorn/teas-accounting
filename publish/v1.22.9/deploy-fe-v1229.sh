#!/bin/bash
# FE deploy v1.22.9 — finding batch WP1 (route-guard deny on 16 /new routes + CN/DN buttons +
# tax-filing/period-close/attachment gates), WP3 (report date-basis labels, AP-aging tie banner,
# AR-aging negatives, bank-recon badge/auto-select), WP5 (api-keys deny+#418 fix, users
# self/peer-admin guard, internal_error Thai toast, VI-new category-clobber fix).
# package.json + pnpm-lock expected UNCHANGED -> NO pnpm install (verify below). Full
# `git archive v1.22.9 frontend` overlay (--strip-components=1). Rebuild, restart, auto-rollback.
# RUN AS THE NORMAL DEPLOY USER — NEVER sudo THIS SCRIPT (troubles-wiki: sudo corrupts ownership).
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old

echo "== overlay new source =="
tar xf /tmp/fe-src-v1229.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v1229.tar | wc -l) entries"
for f in 'app/(dashboard)/tax-invoices/new/page.tsx' \
         'app/(dashboard)/settings/users/page.tsx' \
         'app/(dashboard)/reports/ap-aging/page.tsx' \
         'lib/i18n/problems.ts'; do
  test -e "$f" || { echo "FILE_MISSING: $f -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
done
grep -q 'noAccessTitle' 'app/(dashboard)/tax-invoices/new/page.tsx' || { echo "CONTENT_CHECK_FAILED wp1-gate -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'isGuardedRow' 'app/(dashboard)/settings/users/page.tsx' || { echo "CONTENT_CHECK_FAILED wp5-users-guard -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'noAccessTitle' 'messages/th.json' || { echo "CONTENT_CHECK_FAILED i18n-keys -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
echo "FILES_PRESENT ok"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v1229.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v1229.log
  mv .next .next.broken-v1229; mv .next.old .next
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
  mv .next .next.broken-v1229; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
