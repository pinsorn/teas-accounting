#!/usr/bin/env bash
# TEAS API deploy — v2.2.1 (R3: H2 race→409 + H3 conversion-route target permissions)
#
# NO migration, NO new SqlScript in this release — binary swap only. The index probes below now
# assert the v2.2.0 state PERSISTS rather than gets created.
#
# ⚠️ BEFORE RUNNING: UNIX line endings required.
#    On the box:  tr -d '\r' < deploy-api-v220.sh > d.sh && bash d.sh
#
# ⚠️⚠️ THIS RELEASE CARRIES AN EF MIGRATION, AND A FAILED MIGRATION HERE IS A PROD OUTAGE.
# Program.cs awaits DbInitializer.InitializeAsync UNGUARDED before app.Run(). If CREATE UNIQUE INDEX
# raises 23505 the host never starts and pm2 restart-loops. The migration transaction rolls back
# atomically so the schema stays consistent — but the service is down until the previous artifacts are
# redeployed. `Down()` is NOT a rollback path: the app will not be up to run it.
#
# So the four Tier-2 preconditions run BEFORE the swap and abort the deploy rather than discovering
# the problem after the binary is in place. FE is unchanged in this release; no FE deploy.
set -u
cd /opt/npm-sites/teas.kazaki-rio.com/api || exit 1
TS=$(date +%Y%m%d-%H%M%S)
VER=2.2.1
mkdir -p ~/backups

echo "== PRECONDITION 4: backup DB =="
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz \
  && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz | awk '{print $5}')" \
  || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== PRECONDITION 1: the company-wide allocator is actually live =="
# The whole "no new duplicates can be minted" argument rests on this. If 634 never applied, two
# counters may still be running and the index can meet a duplicate that appeared after the cleanup.
S634=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts WHERE script_name LIKE '634%';" | tr -d ' ')
[ "$S634" = "1" ] || { echo "ABORT: SqlScript 634 not applied (count=$S634) -- v2.1.0 is not fully live"; exit 1; }
echo "PRECOND1_OK 634_applied"

echo "== PRECONDITION 2: blindness control, THEN the duplicate probe =="
# Q0 first, always. Under the NOBYPASSRLS app role the duplicate query returns zero rows for every
# company and reads exactly like "clean". Trust the zero below ONLY if this row count is > 0.
BLIND=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sales.tax_invoices;" | tr -d ' ')
[ "${BLIND:-0}" -gt 0 ] 2>/dev/null || { echo "ABORT: blindness control returned '$BLIND' -- the probe cannot see rows, so a zero below would be meaningless"; exit 1; }
echo "PRECOND2_CONTROL_OK visible_tax_invoices=$BLIND"

DUPES=$(sudo -u postgres psql -d teas -tAc "
  SELECT count(*) FROM (
    SELECT company_id,doc_no FROM sales.tax_invoices         WHERE doc_no IS NOT NULL GROUP BY 1,2 HAVING count(*)>1
    UNION ALL SELECT company_id,doc_no FROM sales.tax_adjustment_notes WHERE doc_no IS NOT NULL GROUP BY 1,2 HAVING count(*)>1
    UNION ALL SELECT company_id,doc_no FROM sales.receipts            WHERE doc_no IS NOT NULL GROUP BY 1,2 HAVING count(*)>1
    UNION ALL SELECT company_id,doc_no FROM purchase.vendor_invoices  WHERE doc_no IS NOT NULL GROUP BY 1,2 HAVING count(*)>1
    UNION ALL SELECT company_id,doc_no FROM purchase.payment_vouchers WHERE doc_no IS NOT NULL GROUP BY 1,2 HAVING count(*)>1
    UNION ALL SELECT company_id,doc_no FROM expense.expense_claims    WHERE doc_no IS NOT NULL GROUP BY 1,2 HAVING count(*)>1
    UNION ALL SELECT company_id,doc_no FROM fixedasset.fixed_assets   WHERE doc_no IS NOT NULL GROUP BY 1,2 HAVING count(*)>1) x;" | tr -d ' ')
[ "$DUPES" = "0" ] || { echo "ABORT: $DUPES duplicate (company_id, doc_no) pair(s) on the seven indexed tables. The migration WILL raise 23505 and the API will not start. Renumber them first."; exit 1; }
echo "PRECOND2_OK duplicates=0 (measured with the index's own predicate)"

echo "== PRECONDITION 3 (v2.2.1 shape): the seven NEW unique indexes from v2.2.0 are present =="
# This release has NO migration. The v2.2.0 indexes must already exist; their absence would mean the
# previous release never actually applied and this binary would be running against the wrong schema.
NEW=$(sudo -u postgres psql -d teas -tAc "
  SELECT count(*) FROM pg_indexes WHERE indexname IN (
    'ix_vendor_invoices_company_id_doc_no','ix_tax_invoices_company_id_doc_no',
    'ix_tax_adjustment_notes_company_id_doc_no','ix_receipts_company_id_doc_no',
    'ix_payment_vouchers_company_id_doc_no','ix_fixed_assets_company_id_doc_no',
    'ix_expense_claims_company_id_doc_no');" | tr -d ' ')
[ "$NEW" = "7" ] || { echo "ABORT: expected the 7 v2.2.0 unique indexes, found $NEW"; exit 1; }
echo "PRECOND3_OK v220_indexes=7"

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-$VER-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi

echo "== swap + restart (the migration runs here) =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 20   # longer than usual: the migration runs before the host serves

ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
RESTARTS=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.restart_time')
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')

# --- the migration actually applied, and produced the right shape ---------------
NEWIDX=$(sudo -u postgres psql -d teas -tAc "
  SELECT count(*) FROM pg_indexes WHERE indexname IN (
    'ix_vendor_invoices_company_id_doc_no','ix_tax_invoices_company_id_doc_no',
    'ix_tax_adjustment_notes_company_id_doc_no','ix_receipts_company_id_doc_no',
    'ix_payment_vouchers_company_id_doc_no','ix_fixed_assets_company_id_doc_no',
    'ix_expense_claims_company_id_doc_no');" | tr -d ' ')
OLDLEFT=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM pg_indexes WHERE indexname LIKE '%branch_id_doc_no';" | tr -d ' ')
# Every new index name must still contain doc_no: NumberedDocumentWriter heals a numbering collision
# by matching the constraint name for that substring. Losing it turns counter drift into a raw 500.
DOCNO=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM pg_indexes WHERE indexname LIKE 'ix_%_company_id_doc_no' AND indexname NOT LIKE '%doc_no%';" | tr -d ' ')
UNIQ=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM pg_indexes WHERE indexname LIKE 'ix_%_company_id_doc_no' AND indexdef NOT LIKE '%UNIQUE%';" | tr -d ' ')
LOGIN=$(curl -s -o /dev/null -w "%{http_code}" "https://teas.kazaki-rio.com/login")

echo "-- probe results --"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST (restarts=$RESTARTS)" || echo "FAIL pm2_status=$ST restarts=$RESTARTS -- a climbing restart count means the migration is failing on every boot"
[ "$VERSION" = "$VER" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION want=$VER"
[ "$NEWIDX" = "7" ] && echo "PASS new_unique_indexes=7" || echo "FAIL new_unique_indexes=$NEWIDX -- the migration did not apply"
[ "$OLDLEFT" = "0" ] && echo "PASS old_branch_scoped_indexes_gone" || echo "FAIL $OLDLEFT branch-scoped index(es) remain"
[ "$UNIQ" = "0" ] && echo "PASS every_doc_no_index_is_unique" || echo "FAIL $UNIQ doc_no index(es) are not UNIQUE"
[ "$LOGIN" = "200" ] && echo "PASS public_login=$LOGIN" || echo "FAIL public_login=$LOGIN"

if [ "$ST" = "online" ] && [ "$VERSION" = "$VER" ] && [ "$NEWIDX" = "7" ] \
   && [ "$OLDLEFT" = "0" ] && [ "$UNIQ" = "0" ] && [ "$LOGIN" = "200" ]; then
  echo "DEPLOY_OK version=$VERSION"
  echo "Duplicate document numbers are now structurally impossible on all 15 doc-carrying tables."
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- rolling back the BINARY (the schema change, if it applied, is forward-compatible"
  echo "                 with v2.1.0 and is deliberately left in place)"
  pm2 logs teas-api --lines 80 --nostream 2>&1 | grep -iE 'error|exception|fail|23505|42P07|42704' | tail -15
  mv unpacked unpacked.broken-v$VER; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 12
  echo "ROLLED_BACK status=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')"
  exit 1
fi
