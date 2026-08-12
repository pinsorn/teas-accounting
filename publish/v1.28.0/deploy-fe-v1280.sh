#!/bin/bash
# FE deploy v1.28.0 — R1 ledger integrity. The ONLY frontend change in this release is
# lib/utils.ts's docType map gaining `Invoice: 'billingNote'`, so the AR movement rows a non-VAT
# company now produces (WP-1 accrues AR at invoice issue) render a translated label instead of the
# raw docType. package.json + pnpm-lock UNCHANGED -> no pnpm install. Full `git archive v1.28.0
# frontend` overlay (--strip-components=1). Rebuild, restart, auto-rollback.
# RUN AS THE NORMAL DEPLOY USER — NEVER sudo THIS SCRIPT (troubles-wiki: sudo corrupts ownership).
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old

echo "== overlay new source =="
tar xf /tmp/fe-src-v1280.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v1280.tar | wc -l) entries"
for f in 'lib/utils.ts' 'app/(dashboard)/reports/ar-aging/page.tsx'; do
  test -e "$f" || { echo "FILE_MISSING: $f -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
done
# R1 content anchors — prove the built tree is THIS release, not a stale checkout.
# WP-1's only FE change: the AR movement rows a non-VAT company now produces need a docType label.
grep -q "Invoice: 'billingNote'" 'lib/utils.ts' || { echo "CONTENT_CHECK_FAILED r1-doctype-map -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
echo "FILES_PRESENT ok"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v1280.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v1280.log
  mv .next .next.broken-v1280; mv .next.old .next
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
  mv .next .next.broken-v1280; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
