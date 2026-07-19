#!/bin/bash
# FE deploy v1.22.3 — CN/DN list column shows referenced TI doc number
# (AdjustmentNoteListItem.originalTiDocNo, server-joined) instead of "#<id>";
# draft-only delete button on the CN/DN detail page (note-delete-action ->
# destructive confirm -> useDeleteAdjustmentNote -> toast -> redirect to list).
# package.json + pnpm-lock UNCHANGED vs v1.22.2 -> NO pnpm install. Full
# `git archive v1.22.3 frontend` overlay (--strip-components=1). Rebuild, restart, auto-rollback.
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old

echo "== overlay new source =="
tar xf /tmp/fe-src-v1223.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v1223.tar | wc -l) entries"
for f in 'components/AdjustmentNoteScreens.tsx' \
         'lib/queries.ts' \
         'lib/types.ts'; do
  test -e "$f" || { echo "FILE_MISSING: $f -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
done
grep -q 'originalTiDocNo' 'lib/types.ts' || { echo "CONTENT_CHECK_FAILED docno-type -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'originalTiDocNo' 'components/AdjustmentNoteScreens.tsx' || { echo "CONTENT_CHECK_FAILED docno-list-wired -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'note-delete-action' 'components/AdjustmentNoteScreens.tsx' || { echo "CONTENT_CHECK_FAILED delete-button-wired -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
grep -q 'useDeleteAdjustmentNote' 'lib/queries.ts' || { echo "CONTENT_CHECK_FAILED delete-hook-wired -- abort"; rm -rf .next; mv .next.old .next; exit 1; }
echo "FILES_PRESENT ok"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v1223.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v1223.log
  mv .next .next.broken-v1222; mv .next.old .next
  echo "RESTORED old .next"
  exit 1
fi
echo "BUILD_OK"

echo "== content-check built output =="
BUILT_OK=0
if grep -rq 'note-delete-action\|originalTiDocNo' .next 2>/dev/null; then BUILT_OK=1; fi
echo "built_output_scan=$BUILT_OK (informational; confirms anchors survived minification)"

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
  mv .next .next.broken-v1222; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
