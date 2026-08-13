#!/usr/bin/env bash
# FE deploy v2.1.0 — R3.
#
# ⚠️ BEFORE RUNNING: UNIX line endings required.
#    On the box:  tr -d '\r' < deploy-fe-v210.sh > d.sh && bash d.sh
# RUN AS THE NORMAL DEPLOY USER — NEVER sudo (troubles-wiki: sudo corrupts ownership).
#
# The frontend change is additive this time: the number-gaps page grows a duplicates table, the
# dashboard grows a separate `dup` alert, and the green "compliant" shield now requires BOTH lists
# empty. So the content anchors are POSITIVE — but anchored on artifacts (a test id, an i18n key),
# never on a bare word, because v2.0.0's negative anchor matched the source COMMENT documenting a
# deletion and aborted a good deploy.
set -uo pipefail
D=/opt/npm-sites/teas.kazaki-rio.com/frontend
cd "$D"

echo "== rename current build (rollback point) =="
rm -rf .next.old
mv .next .next.old
# Carry the build cache forward. `next/font/google` downloads Noto Sans Thai at BUILD time and caches
# it under .next/cache — moving .next away wholesale makes the build re-fetch every weight cold, and
# Google rate-limits that burst. It is what failed the v2.0.0 FE deploy on its first attempt.
if [ -d .next.old/cache ]; then
  mkdir -p .next
  cp -a .next.old/cache .next/cache
  echo "CACHE_CARRIED_FORWARD $(du -sh .next/cache | cut -f1)"
else
  echo "NOTE: no .next.old/cache to carry forward — a cold font fetch may rate-limit."
fi

echo "== overlay new source =="
tar xf /tmp/fe-src-v210.tar --strip-components=1
echo "overlaid: $(tar tf /tmp/fe-src-v210.tar | wc -l) entries"

fail() { echo "$1 -- abort"; rm -rf .next; mv .next.old .next; exit 1; }

for f in 'app/(dashboard)/number-gaps/page.tsx' 'app/(dashboard)/page.tsx' 'messages/th.json' 'messages/en.json'; do
  test -e "$f" || fail "FILE_MISSING: $f"
done

# POSITIVE anchors — the artifacts that exist only when this release's UI is present.
grep -q 'numberDuplicates' 'messages/th.json' || fail "CONTENT_CHECK_FAILED: the duplicates i18n key is missing from th.json"
grep -q 'numberDuplicates' 'messages/en.json' || fail "CONTENT_CHECK_FAILED: the duplicates i18n key is missing from en.json"
# The whole point of WP-3b: the compliant shield must consider duplicates, not just gaps.
grep -q 'duplicates.length === 0' 'app/(dashboard)/number-gaps/page.tsx' || fail "CONTENT_CHECK_FAILED: the compliant shield does not consider duplicates"
# The one-character guard Tier-2 caught — without it the dashboard white-screens during the window
# where the frontend is new and the API has not restarted yet.
grep -q 'duplicates?.length' 'app/(dashboard)/page.tsx' || fail "CONTENT_CHECK_FAILED: dashboard reads duplicates unguarded (deploy-skew white-screen)"
node -e "JSON.parse(require('fs').readFileSync('messages/th.json','utf8'))" || fail "th.json is not valid JSON"
node -e "JSON.parse(require('fs').readFileSync('messages/en.json','utf8'))" || fail "en.json is not valid JSON"
echo "ANCHORS_OK"

echo "== next build =="
export NODE_ENV=production
export PUBLIC_BASE_URL=https://teas.kazaki-rio.com
node node_modules/next/dist/bin/next build > /tmp/fe-build-v210.log 2>&1
RC=$?
if [ $RC -ne 0 ]; then
  echo "BUILD_FAILED rc=$RC -- rolling back"
  tail -30 /tmp/fe-build-v210.log
  mv .next .next.broken-v210; mv .next.old .next
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
# A route can be green on 127.0.0.1 and unreachable publicly — that cost a hotfix release 2026-07-08.
[ "$PUB" = "200" ] && echo "PASS public_domain_login=$PUB" || echo "FAIL public_domain_login=$PUB"
if [ "$ST" = "online" ] && [ "$LOGIN" = "200" ] && [ "$PDF" = "404" ] && [ "$PUB" = "200" ]; then
  echo "FE_DEPLOY_OK"
  echo "NOTE: Tier-4 is the browser leg — /number-gaps must list co2's duplicate receipt and show NO"
  echo "      green compliant shield, and the dashboard must carry a separate duplicates alert."
  rm -rf .next.old
else
  echo "FE_DEPLOY_FAILED -- ROLLING BACK"
  pm2 logs teas-web --lines 12 --nostream 2>&1 | tail -6
  mv .next .next.broken-v210; mv .next.old .next
  pm2 restart teas-web >/dev/null 2>&1; sleep 6
  curl -s -o /dev/null -w 'ROLLBACK login=%{http_code}\n' http://127.0.0.1:3100/login
  exit 1
fi
