#!/usr/bin/env bash
# FE deploy v2.0.0 — R2 compliance filings.
#
# ⚠️ BEFORE RUNNING: this file must have UNIX line endings. A CRLF copy killed the v1.28.0 FE
#    deploy with "set: pipefail: invalid option name" and a `cd` into a path ending in \r.
#    On the box, always:  tr -d '\r' < deploy-fe-v200.sh > d.sh && bash d.sh
#
# RUN AS THE NORMAL DEPLOY USER — NEVER sudo THIS SCRIPT (troubles-wiki: sudo corrupts ownership).
#
# The frontend change in this release is mostly a DELETION (WP-7 removes the "ยืนยันชำระครบแล้ว"
# button and its 3 i18n keys per locale), so the content anchors below are NEGATIVE assertions:
# the overlaid source must NOT contain the removed strings. A positive-only check would pass
# happily on a stale tarball.
# package.json + pnpm-lock unchanged -> no pnpm install.
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old

echo "== overlay new source =="
tar xf /tmp/fe-src-v200.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v200.tar | wc -l) entries"

fail() { echo "$1 -- abort"; rm -rf .next; mv .next.old .next; exit 1; }

for f in 'app/(dashboard)/invoices/[id]/page.tsx' 'messages/th.json' 'messages/en.json' 'lib/i18n/problems.ts'; do
  test -e "$f" || fail "FILE_MISSING: $f"
done

# NEGATIVE anchors — prove the deletion actually landed, not just that a tarball unpacked.
grep -q 'mark-settled' 'app/(dashboard)/invoices/[id]/page.tsx' && fail "CONTENT_CHECK_FAILED: mark-settled still referenced in the invoice page"
# POSITIVE anchor — prove the receipt route (the surviving settlement path) is still wired up.
grep -q 'receipts' 'app/(dashboard)/invoices/[id]/page.tsx' || fail "CONTENT_CHECK_FAILED: the invoice page lost its receipt path too"
# Both locale files must still be valid JSON after the key removals (th/en.json have a known
# trailing-comma trap around lines 99-103).
node -e "JSON.parse(require('fs').readFileSync('messages/th.json','utf8'))" || fail "th.json is not valid JSON"
node -e "JSON.parse(require('fs').readFileSync('messages/en.json','utf8'))" || fail "en.json is not valid JSON"
echo "ANCHORS_OK"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v200.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v200.log
  mv .next .next.broken-v200; mv .next.old .next
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
PUB=$(curl -s -o /dev/null -w '%{http_code}' https://teas.kazaki-rio.com/login)
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$LOGIN" = "200" ] && echo "PASS login=$LOGIN" || echo "FAIL login=$LOGIN"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
# A route can be green on 127.0.0.1 and unreachable publicly (missing proxy passthrough cost a
# hotfix release 2026-07-08) — always probe through the real domain too.
[ "$PUB" = "200" ] && echo "PASS public_domain_login=$PUB" || echo "FAIL public_domain_login=$PUB"
if [ "$ST" = "online" ] && [ "$LOGIN" = "200" ] && [ "$PDF" = "404" ] && [ "$PUB" = "200" ]; then
  echo "FE_DEPLOY_OK"
  echo "NOTE: the button's actual absence on an Issued invoice is the Tier-4 browser leg —"
  echo "      /invoices/3 on co2 must show 'สร้างใบเสร็จ' and NO 'ยืนยันชำระครบแล้ว'."
  rm -rf .next.old
else
  echo "FE_DEPLOY_FAILED -- ROLLING BACK"
  pm2 logs teas-web --lines 12 --nostream 2>&1 | tail -6
  mv .next .next.broken-v200; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
