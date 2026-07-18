#!/bin/bash
# FE deploy v1.22.1 — VAT-round findings fix: employees modal opening-YTD section (F-3),
# Pay dialog bank-account selector (F-6), SSO header label "(รวมนายจ้าง)" (F-4),
# payroll status filter i18n + Thai duplicate-period toast + next-open-period prefill (F-7),
# tax-summary PND1 footnote (F-8), PP30 deadline warning Thai (F-10).
# package.json + pnpm-lock UNCHANGED vs v1.22.1 -> NO pnpm install. Full
# `git archive v1.22.1 frontend` overlay (--strip-components=1). Rebuild, restart, auto-rollback.
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old

echo "== overlay new source =="
tar xf /tmp/fe-src-v1221.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v1221.tar | wc -l) entries"
for f in 'app/(dashboard)/settings/employees/page.tsx' \
         'app/(dashboard)/payroll/[id]/page.tsx' \
         'app/(dashboard)/payroll/page.tsx' \
         'app/(dashboard)/reports/pnd30/page.tsx' \
         'lib/i18n/problems.ts'; do
  test -e "$f" || { echo "FILE_MISSING: $f -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
done
grep -q 'ytdOpeningYear' 'app/(dashboard)/settings/employees/page.tsx' || { echo "CONTENT_CHECK_FAILED F3-opening-ytd -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'payBankAccount' 'messages/th.json' || { echo "CONTENT_CHECK_FAILED F6-bank-selector-i18n -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'ytdOpening' 'messages/th.json' || { echo "CONTENT_CHECK_FAILED F3-i18n -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'payroll.duplicate_period' 'lib/i18n/problems.ts' || { echo "CONTENT_CHECK_FAILED F7-dup-toast -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
echo "FILES_PRESENT ok"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v1221.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v1221.log
  mv .next .next.broken-v1220; mv .next.old .next
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
  mv .next .next.broken-v1220; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
