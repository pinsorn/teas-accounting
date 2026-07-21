#!/bin/bash
# API deploy v1.22.8 — finding batch: WP2 628 (AUDITOR reads + APPROVER pending-approvals read),
# WP6 629 (read/manage split + regression re-grants), WP3 AP-aging reconciliation field.
# TWO new SqlScripts run at startup: 628 + 629 → applied_sql_scripts MUST go +2 (73 -> 75).
# DB backup mandatory (seed scripts mutate role_permissions).
set -uo pipefail
API=/opt/npm-sites/teas.kazaki-rio.com/api
cd "$API"

echo "== pre-deploy baseline =="
PREDEPLOY_TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
EXPECT_TOTAL=$((PREDEPLOY_TOTALSCRIPTS + 2))   # 628 + 629
echo "PREDEPLOY_TOTALSCRIPTS=$PREDEPLOY_TOTALSCRIPTS  EXPECT_AFTER=$EXPECT_TOTAL"

echo "== backup DB =="
TS=$(date +%Y%m%d-%H%M%S)
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v1.22.8-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v1.22.8-deploy-$TS.sql.gz && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v1.22.8-deploy-$TS.sql.gz | awk '{print $5}')" || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-1.22.8-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi
echo "STAGE_OK so=$SO"

echo "== swap + restart (628/629 run at boot) =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 30
BASE=http://127.0.0.1:5180
HTTP=$(curl -s -o /dev/null -w '%{http_code}' $BASE/.well-known/oauth-authorization-server)
ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
PDF=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/public/pdf?t=garbage")
APAGING=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/reports/ap-aging?asOf=2026-07-21")
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')
TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
# grant sanity: AUDITOR template must now hold the new read codes (simple existence check)
AUDREADS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.role_permission_templates WHERE role_code='AUDITOR' AND permission_code IN ('purchase.purchase_order.read','sales.quotation.read','master.business_unit.read')")

echo "-- probe results --"
[ "$HTTP" = "200" ] && echo "PASS http_health=$HTTP" || echo "FAIL http_health=$HTTP"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
[ "$APAGING" = "401" ] && echo "PASS ap_aging_auth=$APAGING" || echo "FAIL ap_aging_auth=$APAGING"
[ "$VERSION" = "1.22.8" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION"
[ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && echo "PASS sql_scripts=$TOTALSCRIPTS (+2)" || echo "FAIL sql_scripts=$TOTALSCRIPTS (expected $EXPECT_TOTAL)"
[ "$AUDREADS" = "3" ] && echo "PASS auditor_read_grants=$AUDREADS/3" || echo "FAIL auditor_read_grants=$AUDREADS (expected 3)"

if [ "$HTTP" = "200" ] && [ "$ST" = "online" ] && [ "$PDF" = "404" ] && [ "$APAGING" = "401" ] && [ "$VERSION" = "1.22.8" ] && [ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && [ "$AUDREADS" = "3" ]; then
  echo "DEPLOY_OK version=$VERSION scripts=$TOTALSCRIPTS"
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- ROLLING BACK BINARIES (applied seed scripts are idempotent/additive; investigate before re-run)"
  pm2 logs teas-api --lines 60 --nostream 2>&1 | grep -iE 'error|exception|fail|42501' | tail -15
  mv unpacked unpacked.broken-v1228; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  curl -s -o /dev/null -w 'ROLLBACK http=%{http_code}\n' $BASE/.well-known/oauth-authorization-server
  exit 1
fi
