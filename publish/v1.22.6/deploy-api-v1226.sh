#!/bin/bash
# API deploy v1.22.6 — CRIT-1 doc-number sequence-drift heal (626 reconcile + retry guard) +
# CRIT-2 TAX_OFFICER tax.filing grant (627). TWO new SqlScripts run at startup this release:
# 626_reconcile_number_sequences.sql + 627_seed_tax_officer_filing_grant.sql.
# ** applied_sql_scripts MUST go 71 -> 73 (EXACTLY +2). DB backup mandatory (scripts mutate data). **
set -uo pipefail
API=/opt/npm-sites/teas.kazaki-rio.com/api
cd "$API"

echo "== pre-deploy baseline =="
PREDEPLOY_TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
EXPECT_TOTAL=$((PREDEPLOY_TOTALSCRIPTS + 2))   # 626 + 627
echo "PREDEPLOY_TOTALSCRIPTS=$PREDEPLOY_TOTALSCRIPTS  EXPECT_AFTER=$EXPECT_TOTAL"
# snapshot a drifted-bucket sanity target: max JV vs sequence for co5 BEFORE (reconcile should align)
sudo -u postgres psql -d teas -tAc "SELECT 'pre co5 JV seq=' || COALESCE((SELECT current_value FROM sys.number_sequences WHERE company_id=5 AND prefix_code='JV' AND period_year=2026 AND period_month=7),0)" 2>/dev/null || true

echo "== backup DB =="
TS=$(date +%Y%m%d-%H%M%S)
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v1.22.6-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v1.22.6-deploy-$TS.sql.gz && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v1.22.6-deploy-$TS.sql.gz | awk '{print $5}')" || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-1.22.6-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi
echo "STAGE_OK so=$SO"

echo "== swap + restart (scripts 626/627 run at boot) =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 30
BASE=http://127.0.0.1:5180
HTTP=$(curl -s -o /dev/null -w '%{http_code}' $BASE/.well-known/oauth-authorization-server)
ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
PDF=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/public/pdf?t=garbage")
PND30=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/tax-filings/pnd30/pdf")
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')
TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts")
# CRIT-1 heal proof: no sequence bucket may sit below the max doc_no in its own table (spot-check JV co5)
DRIFT=$(sudo -u postgres psql -d teas -tAc "
  WITH mx AS (SELECT (regexp_match(max(doc_no),'-([0-9]+)\$'))[1]::int AS m
              FROM gl.journal_entries WHERE company_id=5 AND doc_no LIKE '07-2026-JV-%')
  SELECT COALESCE((SELECT current_value FROM sys.number_sequences
     WHERE company_id=5 AND prefix_code='JV' AND period_year=2026 AND period_month=7),0) - COALESCE(mx.m,0) FROM mx")

echo "-- probe results --"
[ "$HTTP" = "200" ] && echo "PASS http_health=$HTTP" || echo "FAIL http_health=$HTTP"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$PDF" = "404" ] && echo "PASS public_pdf=$PDF" || echo "FAIL public_pdf=$PDF"
[ "$PND30" = "401" ] && echo "PASS pnd30_auth=$PND30" || echo "FAIL pnd30_auth=$PND30 (expect 401 unauth)"
[ "$VERSION" = "1.22.6" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION"
[ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && echo "PASS sql_scripts=$TOTALSCRIPTS (+2)" || echo "FAIL sql_scripts=$TOTALSCRIPTS (expected $EXPECT_TOTAL)"
[ "$DRIFT" -ge 0 ] 2>/dev/null && echo "PASS seq_no_drift co5_JV delta=$DRIFT (>=0 means counter at/above max)" || echo "FAIL seq_drift co5_JV delta=$DRIFT (<0 = still behind!)"

if [ "$HTTP" = "200" ] && [ "$ST" = "online" ] && [ "$PDF" = "404" ] && [ "$PND30" = "401" ] && [ "$VERSION" = "1.22.6" ] && [ "$TOTALSCRIPTS" = "$EXPECT_TOTAL" ] && [ "$DRIFT" -ge 0 ] 2>/dev/null; then
  echo "DEPLOY_OK version=$VERSION scripts=$TOTALSCRIPTS"
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- ROLLING BACK BINARIES (scripts already applied are idempotent/forward-safe; investigate before re-run)"
  pm2 logs teas-api --lines 60 --nostream 2>&1 | grep -iE 'error|exception|fail|42501|23505' | tail -15
  mv unpacked unpacked.broken-v1226; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  curl -s -o /dev/null -w 'ROLLBACK http=%{http_code}\n' $BASE/.well-known/oauth-authorization-server
  exit 1
fi
