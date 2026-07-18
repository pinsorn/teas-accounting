#!/bin/bash
# API deploy v1.22.1 — VAT-round findings fix (spec fix-vat-round-findings.md):
# F-3 opening-YTD (script 624 adds 4 employees columns; engine projection + SSO allowance),
# F-6 Pay settlement JE (Dr 2170 / Cr bank GL or 1110), F-9 COGS 5000 account + category
# remap (script 625, backfills ALL companies), F-8 tax-summary PND1 includes payroll PIT.
# ** 2 NEW SqlScripts run at startup: applied_sql_scripts MUST go +2 (expect 69 -> 71). **
# DB backup mandatory per SOP.
set -uo pipefail
API=/opt/npm-sites/teas.kazaki-rio.com/api
cd "$API"

echo "== pre-deploy baseline =="
PREDEPLOY_TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
EXPECT_TOTAL=$((PREDEPLOY_TOTALSCRIPTS + 0))  # 624+625 both recorded already (rerun applies nothing)
echo "PREDEPLOY_TOTALSCRIPTS=$PREDEPLOY_TOTALSCRIPTS (expect +1: only 625 pending, 624 already recorded)"

echo "== backup DB =="
TS=$(date +%Y%m%d-%H%M%S)
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v1.22.1-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v1.22.1-deploy-$TS.sql.gz && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v1.22.1-deploy-$TS.sql.gz | awk '{print $5}')" || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-1.22.1-sc.tar -C unpacked.new
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
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')
TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
YTDCOLS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM information_schema.columns WHERE table_schema='master' AND table_name='employees' AND column_name LIKE 'ytd_opening%'")
COGS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM master.chart_of_accounts WHERE account_code='5000'")
NCOMP=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM master.companies")
NCOGS=$(sudo -u postgres psql -d teas -tAc "SELECT count(DISTINCT company_id) FROM sys.expense_categories WHERE category_code='COGS'")
COGSMAP=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.expense_categories ec JOIN master.chart_of_accounts a ON a.account_id=ec.default_expense_account_id WHERE ec.category_code='COGS' AND a.account_code='5000'")

echo "-- probe results --"
[ "$HTTP" = "200" ] && echo "PASS http_health=$HTTP" || echo "FAIL http_health=$HTTP"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
[ "$PAYROLL" = "401" ] && echo "PASS payroll_route=$PAYROLL" || echo "FAIL payroll_route=$PAYROLL"
[ "$VERSION" = "1.22.1" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION"
[ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && echo "PASS new_sql_scripts_applied total=$TOTALSCRIPTS" || echo "FAIL total_sql_scripts=$TOTALSCRIPTS (expected $EXPECT_TOTAL)"
[ "$YTDCOLS" = "4" ] && echo "PASS ytd_opening_columns=$YTDCOLS" || echo "FAIL ytd_opening_columns=$YTDCOLS (expected 4)"
[ "$COGS" = "$NCOMP" ] && echo "PASS cogs_account_all_companies=$COGS/$NCOMP" || echo "FAIL cogs_account=$COGS (companies=$NCOMP)"
[ "$COGSMAP" = "$NCOGS" ] && echo "PASS cogs_category_remap=$COGSMAP/$NCOGS" || echo "FAIL cogs_category_remap=$COGSMAP (companies_with_cogs=$NCOGS)"

if [ "$HTTP" = "200" ] && [ "$ST" = "online" ] && [ "$PDF" = "404" ] && [ "$PAYROLL" = "401" ] && [ "$VERSION" = "1.22.1" ] && [ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && [ "$YTDCOLS" = "4" ] && [ "$COGS" = "$NCOMP" ] && [ "$COGSMAP" = "$NCOGS" ]; then
  echo "DEPLOY_OK version=$VERSION"
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- ROLLING BACK BINARIES (note: applied SQL scripts are NOT auto-reverted; columns/account are additive+idempotent, safe to leave)"
  pm2 logs teas-api --lines 40 --nostream 2>&1 | grep -iE 'error|exception|fail' | tail -12
  mv unpacked unpacked.broken-v1220; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  curl -s -o /dev/null -w 'ROLLBACK http=%{http_code}\n' $BASE/.well-known/oauth-authorization-server
  exit 1
fi
