#!/bin/bash
# API deploy v1.22.7 — CRIT-1 round-2 fix: NumberedDocumentWriter off-by-one retry cap +
# explicit savepoint (ambient-tx posting paths TI/RC/VI/PV/expense/adjustment now recover from a
# doc_no collision instead of 500ing). ONE .cs file. NO new SqlScripts (626/627 already applied in
# v1.22.6) → applied_sql_scripts MUST stay UNCHANGED (73). DB backup still per SOP.
set -uo pipefail
API=/opt/npm-sites/teas.kazaki-rio.com/api
cd "$API"

echo "== pre-deploy baseline =="
PREDEPLOY_TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
EXPECT_TOTAL=$PREDEPLOY_TOTALSCRIPTS   # no new scripts this release
echo "PREDEPLOY_TOTALSCRIPTS=$PREDEPLOY_TOTALSCRIPTS (expect unchanged)"

echo "== backup DB =="
TS=$(date +%Y%m%d-%H%M%S)
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v1.22.7-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v1.22.7-deploy-$TS.sql.gz && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v1.22.7-deploy-$TS.sql.gz | awk '{print $5}')" || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-1.22.7-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi
echo "STAGE_OK so=$SO"

echo "== swap + restart =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 28
BASE=http://127.0.0.1:5180
HTTP=$(curl -s -o /dev/null -w '%{http_code}' $BASE/.well-known/oauth-authorization-server)
ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
PDF=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/public/pdf?t=garbage")
TI=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/tax-invoices")
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')
TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")

echo "-- probe results --"
[ "$HTTP" = "200" ] && echo "PASS http_health=$HTTP" || echo "FAIL http_health=$HTTP"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
[ "$TI" = "401" ] && echo "PASS tax_invoices_auth=$TI" || echo "FAIL tax_invoices_auth=$TI"
[ "$VERSION" = "1.22.7" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION"
[ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && echo "PASS sql_scripts_unchanged=$TOTALSCRIPTS" || echo "FAIL scripts=$TOTALSCRIPTS (expected $EXPECT_TOTAL)"

if [ "$HTTP" = "200" ] && [ "$ST" = "online" ] && [ "$PDF" = "404" ] && [ "$TI" = "401" ] && [ "$VERSION" = "1.22.7" ] && [ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ]; then
  echo "DEPLOY_OK version=$VERSION"
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- ROLLING BACK BINARIES"
  pm2 logs teas-api --lines 50 --nostream 2>&1 | grep -iE 'error|exception|fail|23505' | tail -12
  mv unpacked unpacked.broken-v1227; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  curl -s -o /dev/null -w 'ROLLBACK http=%{http_code}\n' $BASE/.well-known/oauth-authorization-server
  exit 1
fi
