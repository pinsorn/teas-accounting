#!/bin/bash
# API deploy v1.22.5 — AR aging report now includes net-credit customers
# (posted CN/DN merged into per-customer buckets in SubledgerReportService.ArAgingAsync,
# CN negative / DN positive, bucketed by note DocDate; rows kept when net != 0)
# so the table/CSV grand total ties to the 1130 control banner. Report layer only,
# NO posting/JE changes.
# ** NO new SqlScripts, NO new EF migrations this release. applied_sql_scripts MUST stay unchanged. **
# DB backup mandatory per SOP even though no new scripts run.
set -uo pipefail
API=/opt/npm-sites/teas.kazaki-rio.com/api
cd "$API"

echo "== pre-deploy baseline =="
PREDEPLOY_TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
EXPECT_TOTAL=$PREDEPLOY_TOTALSCRIPTS  # no new scripts this release -> must stay identical
echo "PREDEPLOY_TOTALSCRIPTS=$PREDEPLOY_TOTALSCRIPTS (expect unchanged, no new scripts)"

echo "== backup DB =="
TS=$(date +%Y%m%d-%H%M%S)
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v1.22.5-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v1.22.5-deploy-$TS.sql.gz && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v1.22.5-deploy-$TS.sql.gz | awk '{print $5}')" || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-1.22.5-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi
echo "STAGE_OK so=$SO"

echo "== swap + restart =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 25
BASE=http://127.0.0.1:5180
HTTP=$(curl -s -o /dev/null -w '%{http_code}' $BASE/.well-known/oauth-authorization-server)
ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
PDF=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/public/pdf?t=garbage")
PAYROLL=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/payroll/runs")
ARAGING=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/reports/ar-aging?asOf=2026-07-19")
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')
TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")

echo "-- probe results --"
[ "$HTTP" = "200" ] && echo "PASS http_health=$HTTP" || echo "FAIL http_health=$HTTP"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
[ "$PAYROLL" = "401" ] && echo "PASS auth_endpoint=$PAYROLL" || echo "FAIL auth_endpoint=$PAYROLL"
[ "$ARAGING" = "401" ] && echo "PASS ar_aging_auth=$ARAGING" || echo "FAIL ar_aging_auth=$ARAGING"
[ "$VERSION" = "1.22.5" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION"
[ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && echo "PASS sql_scripts_unchanged total=$TOTALSCRIPTS" || echo "FAIL total_sql_scripts=$TOTALSCRIPTS (expected $EXPECT_TOTAL)"

if [ "$HTTP" = "200" ] && [ "$ST" = "online" ] && [ "$PDF" = "404" ] && [ "$PAYROLL" = "401" ] && [ "$ARAGING" = "401" ] && [ "$VERSION" = "1.22.5" ] && [ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ]; then
  echo "DEPLOY_OK version=$VERSION"
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- ROLLING BACK BINARIES"
  pm2 logs teas-api --lines 40 --nostream 2>&1 | grep -iE 'error|exception|fail' | tail -12
  mv unpacked unpacked.broken-v1225; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  curl -s -o /dev/null -w 'ROLLBACK http=%{http_code}\n' $BASE/.well-known/oauth-authorization-server
  exit 1
fi
