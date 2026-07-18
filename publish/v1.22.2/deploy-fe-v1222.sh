#!/bin/bash
# FE deploy v1.22.2 — i18n/UX polish: statement-import CSV/PDF format hint (F-12/F-11 area),
# CN/DN reason dropdown + confirm-dialog Thai labels (AdjustmentNoteForm), CN confirm shows
# referenced doc-no instead of raw TI id, bank-match/unmatch toasts.
# package.json + pnpm-lock UNCHANGED vs v1.22.1 -> NO pnpm install. Full
# `git archive v1.22.2 frontend` overlay (--strip-components=1). Rebuild, restart, auto-rollback.
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old

echo "== overlay new source =="
tar xf /tmp/fe-src-v1222.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v1222.tar | wc -l) entries"
for f in 'components/bank/StatementImportSection.tsx' \
         'components/forms/AdjustmentNoteForm.tsx' \
         'app/(dashboard)/bank-accounts/[id]/imports/[importId]/page.tsx' \
         'messages/th.json' \
         'messages/en.json'; do
  test -e "$f" || { echo "FILE_MISSING: $f -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
done
grep -q 'จำนวนเงินผิด' 'messages/th.json' || { echo "CONTENT_CHECK_FAILED cn-dn-reason-th -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'รองรับไฟล์ CSV จาก KBiz' 'messages/th.json' || { echo "CONTENT_CHECK_FAILED import-hint-th -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'importFormatHint' 'components/bank/StatementImportSection.tsx' || { echo "CONTENT_CHECK_FAILED import-hint-wired -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'matchSuccess' 'messages/th.json' || { echo "CONTENT_CHECK_FAILED match-toast-i18n -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
echo "FILES_PRESENT ok"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v1222.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v1222.log
  mv .next .next.broken-v1221; mv .next.old .next
  echo "RESTORED old .next"
  exit 1
fi
echo "BUILD_OK"

echo "== content-check built output =="
BUILT_TH_OK=0
if grep -rq 'จำนวนเงินผิด' .next/server 2>/dev/null || grep -rq 'importFormatHint\|รองรับไฟล์ CSV' .next 2>/dev/null; then BUILT_TH_OK=1; fi
echo "built_output_scan=$BUILT_TH_OK (informational; messages are loaded at request time, may not appear in static server bundle)"

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
  mv .next .next.broken-v1221; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
