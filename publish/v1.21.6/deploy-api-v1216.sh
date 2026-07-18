#!/bin/bash
# API deploy v1.21.6 — hotfix: CompanyService.CreateAsync atomic + RLS-correct tenant
# seeding (REPORT-vat-dummy-test.md F-1: company creation broken since RLS-600,
# 42501 on branches + orphan half-created tenant). Backend-only release, NO new
# SqlScripts/migrations — applied_sql_scripts count must be UNCHANGED (expect 69).
# DB backup mandatory per SOP. FE unchanged (no FE deploy this round).
set -uo pipefail
API=/opt/npm-sites/teas.kazaki-rio.com/api
cd "$API"

echo "== pre-deploy baseline =="
PREDEPLOY_TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
echo "PREDEPLOY_TOTALSCRIPTS=$PREDEPLOY_TOTALSCRIPTS (must be UNCHANGED post-deploy, expect 69)"

echo "== backup DB =="
TS=$(date +%Y%m%d-%H%M%S)
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v1.21.6-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v1.21.6-deploy-$TS.sql.gz && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v1.21.6-deploy-$TS.sql.gz | awk '{print $5}')" || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-1.21.6-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi
echo "STAGE_OK so=$SO"

echo "== swap + restart =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 20
BASE=http://127.0.0.1:5180
HTTP=$(curl -s -o /dev/null -w '%{http_code}' $BASE/.well-known/oauth-authorization-server)
ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
PDF=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/public/pdf?t=garbage")
ARAGING=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/reports/ar-aging")
MCPNOKEY=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/mcp")
PAYROLL=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/payroll/runs")
FINSTMT=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/reports/financial-statements/pdf?year=2026")
COMPANIESPOST=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$BASE/companies" -H 'Content-Type: application/json' -d '{}')
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')
TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
COMPANIES=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM master.companies")

echo "-- probe results --"
[ "$HTTP" = "200" ] && echo "PASS http_health=$HTTP" || echo "FAIL http_health=$HTTP"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
[ "$ARAGING" = "401" ] && echo "PASS ar_aging=$ARAGING" || echo "FAIL ar_aging=$ARAGING"
[ "$MCPNOKEY" = "401" ] && echo "PASS mcp_no_key=$MCPNOKEY" || echo "FAIL mcp_no_key=$MCPNOKEY"
[ "$PAYROLL" = "401" ] && echo "PASS payroll_runs_route=$PAYROLL" || echo "FAIL payroll_runs_route=$PAYROLL"
[ "$FINSTMT" = "401" ] && echo "PASS financial_statements_pdf_route=$FINSTMT" || echo "FAIL financial_statements_pdf_route=$FINSTMT"
[ "$COMPANIESPOST" = "401" ] && echo "PASS companies_post_route=$COMPANIESPOST" || echo "FAIL companies_post_route=$COMPANIESPOST (expected 401 auth-gated — the fixed endpoint must still be gated)"
[ "$VERSION" = "1.21.6" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION"
[ "$TOTALSCRIPTS" = "$PREDEPLOY_TOTALSCRIPTS" ] && echo "PASS total_sql_scripts_unchanged=$TOTALSCRIPTS" || echo "FAIL total_sql_scripts=$TOTALSCRIPTS (expected unchanged $PREDEPLOY_TOTALSCRIPTS)"
echo "companies=$COMPANIES"

if [ "$HTTP" = "200" ] && [ "$ST" = "online" ] && [ "$PDF" = "404" ] && [ "$ARAGING" = "401" ] && [ "$MCPNOKEY" = "401" ] && [ "$PAYROLL" = "401" ] && [ "$FINSTMT" = "401" ] && [ "$COMPANIESPOST" = "401" ] && [ "$VERSION" = "1.21.6" ] && [ "$TOTALSCRIPTS" = "$PREDEPLOY_TOTALSCRIPTS" ]; then
  echo "DEPLOY_OK version=$VERSION"
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- ROLLING BACK"
  pm2 logs teas-api --lines 40 --nostream 2>&1 | grep -iE 'error|exception|fail' | tail -12
  mv unpacked unpacked.broken-v1216; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  curl -s -o /dev/null -w 'ROLLBACK http=%{http_code}\n' $BASE/.well-known/oauth-authorization-server
  exit 1
fi
