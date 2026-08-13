#!/usr/bin/env bash
# TEAS API deploy — v2.1.0 (R3: F1 ภ.พ.36 payment detection · H4 attachment guard · H1 numbering)
#
# ⚠️ BEFORE RUNNING: this file must have UNIX line endings.
#    On the box:  tr -d '\r' < deploy-api-v210.sh > d.sh && bash d.sh
#
# NO EF migration in this release. It DOES carry two new SqlScripts (634 reconcile, 635 duplicate
# view) which run at API startup — so the DB backup is mandatory and the probes below check they
# actually applied.
#
# Probe design, learned the hard way on v2.0.0 (one unnecessary rollback):
#   * NEVER probe a route's existence over HTTP. This app authenticates BEFORE it routes, so a real
#     route, a deleted route and a route that never existed all answer 401 — measured on the box.
#   * Grep the ARTIFACT instead, with `strings -a -el`: .NET stores these literals as UTF-16LE, so
#     plain `strings` finds nothing and would "pass" on any build.
#   * Error codes live in Accounting.Infrastructure.dll, NOT Accounting.Api.dll. Grepping only the
#     API assembly returns 0 for every code and reads exactly like "the guard did not ship".
#   * Always pair an absence/presence check with a CONTROL, so a zero cannot mean "the grep failed".
set -u
cd /opt/npm-sites/teas.kazaki-rio.com/api || exit 1
TS=$(date +%Y%m%d-%H%M%S)
VER=2.1.0
mkdir -p ~/backups

echo "== backup DB =="
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz \
  && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz | awk '{print $5}')" \
  || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

echo "== pre-deploy: duplicate document numbers (expect 11 — the known set, unchanged) =="
DUPES_BEFORE=$(sudo -u postgres psql -d teas -tAc "
  WITH docs AS (
    SELECT company_id, doc_no FROM sales.tax_invoices WHERE doc_no IS NOT NULL
    UNION ALL SELECT company_id, doc_no FROM sales.receipts WHERE doc_no IS NOT NULL
    UNION ALL SELECT company_id, doc_no FROM sales.tax_adjustment_notes WHERE doc_no IS NOT NULL
    UNION ALL SELECT company_id, doc_no FROM purchase.vendor_invoices WHERE doc_no IS NOT NULL)
  SELECT count(*) FROM (SELECT company_id, doc_no FROM docs
    GROUP BY company_id, doc_no HAVING count(*) > 1) d;" | tr -d ' ')
echo "duplicates_before=$DUPES_BEFORE"

echo "== stage =="
rm -rf unpacked.new; mkdir unpacked.new
tar xf /tmp/teas-api-$VER-sc.tar -C unpacked.new
chmod +x unpacked.new/Accounting.Api
SO=$(ls unpacked.new/*.so 2>/dev/null | wc -l)
if [ ! -x unpacked.new/Accounting.Api ] || [ "$SO" -lt 10 ]; then echo "STAGE_FAIL so=$SO"; exit 1; fi

echo "== swap + restart =="
rm -rf unpacked.old; mv unpacked unpacked.old; mv unpacked.new unpacked
pm2 restart teas-api >/dev/null 2>&1
sleep 14

ST=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')
VERSION=$(grep -o '"Accounting.Api/[0-9.]*"' unpacked/Accounting.Api.deps.json | head -1 | grep -o '[0-9][0-9.]*')

# --- artifact probes (UTF-16LE, correct assembly, each with a control) --------
INF=unpacked/Accounting.Infrastructure.dll
F1=$(strings -a -el "$INF" | grep -c "pnd36.unreconciled_not_acknowledged")
CTRL=$(strings -a -el "$INF" | grep -c "pp30.non_vat_blocked")   # shipped in v2.0.0, must still be there
# H4 is an IDENTIFIER, not a string literal — and the two live in different metadata heaps. .NET puts
# string literals in the UTF-16LE #US heap (hence -el above) but method and type NAMES in the UTF-8
# #Strings heap. Using -el here found nothing and rolled back a perfectly good deploy on the first
# v2.1.0 attempt. Measured on the box: utf8=1, utf16=0.
H4=$(strings -a unpacked/Accounting.Application.dll | grep -c "ResolveParentAsync")

# --- the two new SqlScripts actually applied ---------------------------------
S634=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts WHERE script_name LIKE '634%';" | tr -d ' ')
S635=$(sudo -u postgres psql -d teas -tAc "SELECT count(*) FROM sys.applied_sql_scripts WHERE script_name LIKE '635%';" | tr -d ' ')

# --- H1's real gate: after the reconcile, the company-wide bucket (branch 0) must be >= every
#     other branch's counter for the same number space. NOTE it is NOT "only one row per space" —
#     634 lifts branch 0 and deliberately leaves the old per-branch rows in place, so a
#     one-row-per-space assertion would FAIL on a perfectly correct deploy.
BADSEQ=$(sudo -u postgres psql -d teas -tAc "
  SELECT count(*) FROM sys.number_sequences s
  WHERE s.branch_id <> 0
    AND s.current_value > COALESCE((SELECT z.current_value FROM sys.number_sequences z
        WHERE z.company_id=s.company_id AND z.branch_id=0 AND z.prefix_code=s.prefix_code
          AND z.sub_prefix=s.sub_prefix AND z.period_year=s.period_year
          AND z.period_month=s.period_month), -1);" | tr -d ' ')

# --- the duplicate set must NOT have grown ------------------------------------
DUPES_AFTER=$(sudo -u postgres psql -d teas -tAc "
  WITH docs AS (
    SELECT company_id, doc_no FROM sales.tax_invoices WHERE doc_no IS NOT NULL
    UNION ALL SELECT company_id, doc_no FROM sales.receipts WHERE doc_no IS NOT NULL
    UNION ALL SELECT company_id, doc_no FROM sales.tax_adjustment_notes WHERE doc_no IS NOT NULL
    UNION ALL SELECT company_id, doc_no FROM purchase.vendor_invoices WHERE doc_no IS NOT NULL)
  SELECT count(*) FROM (SELECT company_id, doc_no FROM docs
    GROUP BY company_id, doc_no HAVING count(*) > 1) d;" | tr -d ' ')

LOGIN=$(curl -s -o /dev/null -w "%{http_code}" "https://teas.kazaki-rio.com/login")

echo "-- probe results --"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$VERSION" = "$VER" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION want=$VER"
[ "$F1" -ge 1 ] 2>/dev/null && echo "PASS f1_pnd36_guard_present refs=$F1" || echo "FAIL f1_pnd36_guard missing"
[ "$H4" -ge 1 ] 2>/dev/null && echo "PASS h4_attachment_parent_resolver_present refs=$H4" || echo "FAIL h4_resolver missing"
[ "$CTRL" -ge 1 ] 2>/dev/null && echo "PASS control_v200_guard_still_present refs=$CTRL" || echo "FAIL control refs=$CTRL -- the grep found nothing, the probes above prove nothing"
[ "$S634" = "1" ] && echo "PASS sqlscript_634_applied" || echo "FAIL sqlscript_634 count=$S634"
[ "$S635" = "1" ] && echo "PASS sqlscript_635_applied" || echo "FAIL sqlscript_635 count=$S635"
[ "$BADSEQ" = "0" ] && echo "PASS reconcile_branch0_is_max" || echo "FAIL $BADSEQ sequence rows sit ABOVE their company-wide bucket -- the next post can mint a duplicate"
[ "$DUPES_AFTER" = "$DUPES_BEFORE" ] && echo "PASS duplicates_unchanged before=$DUPES_BEFORE after=$DUPES_AFTER" || echo "FAIL duplicates moved $DUPES_BEFORE -> $DUPES_AFTER"
[ "$LOGIN" = "200" ] && echo "PASS public_login=$LOGIN" || echo "FAIL public_login=$LOGIN"

if [ "$ST" = "online" ] && [ "$VERSION" = "$VER" ] && [ "$F1" -ge 1 ] 2>/dev/null \
   && [ "$H4" -ge 1 ] 2>/dev/null && [ "$CTRL" -ge 1 ] 2>/dev/null \
   && [ "$S634" = "1" ] && [ "$S635" = "1" ] && [ "$BADSEQ" = "0" ] \
   && [ "$DUPES_AFTER" = "$DUPES_BEFORE" ] && [ "$LOGIN" = "200" ]; then
  echo "DEPLOY_OK version=$VERSION"
  echo "NOTE: WP-4 (the unique indexes) is deliberately NOT in this release — it cannot ship while"
  echo "      the $DUPES_AFTER known duplicates exist, because a failed EF migration is not recorded"
  echo "      and would retry on every boot."
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- rolling back"
  pm2 logs teas-api --lines 60 --nostream 2>&1 | grep -iE 'error|exception|fail|42501|22003|23502' | tail -15
  mv unpacked unpacked.broken-v$VER; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  echo "ROLLED_BACK status=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')"
  echo "NOTE: no EF migration in this release, so the binary rollback is complete. The two SqlScripts"
  echo "      are additive (a reconcile that only lifts, and a CREATE VIEW) and safe to leave applied."
  exit 1
fi
