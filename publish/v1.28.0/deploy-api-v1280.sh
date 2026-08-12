#!/usr/bin/env bash
# TEAS API deploy — v1.28.0 (R1 ledger integrity: WP-1..WP-5)
#
# DB BACKUP IS MANDATORY: this release carries an EF migration
# (20260811115620_AddBillingNoteJournalEntryId — adds sales.billing_notes.journal_entry_id).
# Migrations run at API startup, so a bad boot must be recoverable.
#
# Atomic swap + auto-rollback, same shape as v1.22.9's script.
set -u
cd /opt/npm-sites/teas.kazaki-rio.com/api || exit 1
TS=$(date +%Y%m%d-%H%M%S)
VER=1.28.0
mkdir -p ~/backups

echo "== backup DB =="
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz \
  && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz | awk '{print $5}')" \
  || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

# Pre-deploy fact: R1's guard refuses >2dp amounts at the posting seam. The WP-6.1 audit
# (2026-08-12) found ZERO polluted rows on both real tenants, which is why this deploy is safe.
# Re-assert it here rather than trusting a day-old reading — cheap, and it is the release gate.
echo "== re-check the deploy gate (sub-satang rows on live tenants) =="
BAD=$(sudo -u postgres psql -d teas -tAc "
  SELECT count(*) FROM gl.journal_lines jl
  JOIN gl.journal_entries je ON je.journal_id=jl.journal_id
  WHERE je.company_id IN (2,3)
    AND (round(jl.debit_amount,2)<>jl.debit_amount OR round(jl.credit_amount,2)<>jl.credit_amount);")
if [ "$BAD" != "0" ]; then
  echo "GATE_FAIL: $BAD sub-satang journal lines on a REAL tenant (co2/co3)."
  echo "R1's guard would strand year-close / payroll pay / backfill there. Abort and remediate first."
  exit 1
fi
echo "GATE_OK real_tenant_subsatang_rows=0"

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-$VER-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi

echo "== swap + restart =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 12

ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')
TOTALSCRIPTS=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM public.applied_sql_scripts;" 2>/dev/null | tr -d ' ')

# --- R1-specific probes -------------------------------------------------------
# 1. the EF migration actually applied (WP-1's column exists)
COL=$(sudo -u postgres psql -d teas -tAc "
  SELECT count(*) FROM information_schema.columns
  WHERE table_schema='sales' AND table_name='billing_notes' AND column_name='journal_entry_id';" | tr -d ' ')
# 2. the new backfill endpoint is routed (401/403 = exists; 404 = missing)
BF=$(curl -s -o /dev/null -w "%{http_code}" -X POST "https://teas.kazaki-rio.com/api/proxy/admin/nonvat-ar-backfill?mode=preview")
# 3. the new expense-category PUT is routed
EC=$(curl -s -o /dev/null -w "%{http_code}" -X PUT "https://teas.kazaki-rio.com/api/proxy/expense-categories/999999")
# 4. public login still 200 through the full CDN -> proxy -> app path
LOGIN=$(curl -s -o /dev/null -w "%{http_code}" "https://teas.kazaki-rio.com/login")

echo "-- probe results --"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$VERSION" = "$VER" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION want=$VER"
[ "$COL" = "1" ] && echo "PASS migration_column_present" || echo "FAIL migration_column missing (count=$COL)"
[ "$BF" != "404" ] && echo "PASS backfill_route_exists http=$BF" || echo "FAIL backfill_route 404"
[ "$EC" != "404" ] && echo "PASS expense_category_put_exists http=$EC" || echo "FAIL expense_category_put 404"
[ "$LOGIN" = "200" ] && echo "PASS public_login=$LOGIN" || echo "FAIL public_login=$LOGIN"

if [ "$ST" = "online" ] && [ "$VERSION" = "$VER" ] && [ "$COL" = "1" ] \
   && [ "$BF" != "404" ] && [ "$EC" != "404" ] && [ "$LOGIN" = "200" ]; then
  echo "DEPLOY_OK version=$VERSION scripts=$TOTALSCRIPTS"
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- rolling back"
  pm2 logs teas-api --lines 60 --nostream 2>&1 | grep -iE 'error|exception|fail|42501' | tail -15
  mv unpacked unpacked.broken-v$VER; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  echo "ROLLED_BACK status=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')"
  echo "NOTE: the DB migration is NOT rolled back by this script. The added column is nullable and"
  echo "      additive, so the previous binary runs against it fine — no action needed unless the"
  echo "      failure was migration-related, in which case restore from the backup above."
  exit 1
fi
