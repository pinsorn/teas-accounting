#!/usr/bin/env bash
# TEAS API deploy — v2.0.0 (R2 compliance filings)
#
# ⚠️ BEFORE RUNNING: this file must have UNIX line endings. A CRLF copy killed the v1.28.0 FE
#    deploy with "set: pipefail: invalid option name" and a `cd` into a path ending in \r.
#    On the box, always:  tr -d '\r' < deploy-api-v200.sh > d.sh && bash d.sh
#
# R2 ships NO EF migration (all four work packages are guard-only). The DB backup stays mandatory
# anyway: SqlScripts still run at API startup, so a bad boot must be recoverable.
#
# Atomic swap + auto-rollback, same shape as v1.28.0's script.
set -u
cd /opt/npm-sites/teas.kazaki-rio.com/api || exit 1
TS=$(date +%Y%m%d-%H%M%S)
VER=2.0.0
mkdir -p ~/backups

echo "== backup DB =="
sudo -u postgres pg_dump teas | gzip > ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz
gunzip -t ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz \
  && echo "DB_BACKUP_OK $(ls -la ~/backups/teas-pre-v$VER-deploy-$TS.sql.gz | awk '{print $5}')" \
  || { echo "DB_BACKUP_FAILED -- abort"; exit 1; }

# Pre-deploy fact: R1's precision guard is already live and both real tenants measured ZERO
# sub-satang rows. Re-assert rather than trust a stale reading — same gate v1.28.0 used, and it is
# cheap. R2 adds no new posting path, but a regression here would strand year-close on a real tenant.
echo "== re-check the R1 gate (sub-satang rows on live tenants) =="
BAD=$(sudo -u postgres psql -d teas -tAc "
  SELECT count(*) FROM gl.journal_lines jl
  JOIN gl.journal_entries je ON je.journal_id=jl.journal_id
  WHERE je.company_id IN (2,3)
    AND (round(jl.debit_amount,2)<>jl.debit_amount OR round(jl.credit_amount,2)<>jl.credit_amount);")
if [ "$BAD" != "0" ]; then
  echo "GATE_FAIL: $BAD sub-satang journal lines on a REAL tenant (co2/co3). Abort and remediate."
  exit 1
fi
echo "GATE_OK real_tenant_subsatang_rows=0"

# R2-specific pre-deploy fact: WP-7 REMOVES a public endpoint. Record how many billing notes are
# currently Settled per real tenant, so the post-deploy count can be compared. Deleting the route
# must not touch data — these numbers must be IDENTICAL afterwards.
echo "== pre-deploy settled-invoice census (must not change) =="
sudo -u postgres psql -d teas -tAc "
  SELECT 'co'||company_id||' settled='||count(*)
  FROM sales.billing_notes WHERE upper(status)='SETTLED' AND company_id IN (2,3)
  GROUP BY company_id ORDER BY company_id;"

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

# --- R2-specific probes ------------------------------------------------------
# 1. WP-7: the mark-settled route must be GONE.
#    NOT probed over HTTP, deliberately. The first version of this script asked for a 404 and got a
#    401, which rolled back a perfectly good deploy: this app authenticates BEFORE routing, so an
#    unauthenticated request returns 401 for a real route, a deleted route AND a route that never
#    existed — measured all three on the box. Going through /api/proxy/* is worse still, since the
#    Next.js BFF answers 401 itself without ever forwarding. There is no credential-free HTTP probe
#    that can tell "removed" from "present" here.
#    So probe the ARTIFACT instead. .NET stores these literals as UTF-16LE, which is why `strings`
#    with no -el finds nothing and would silently "pass" on any build.
MS=$(strings -a -el unpacked/Accounting.Api.dll 2>/dev/null | grep -c "mark-settled")
# 2. Control for probe 1: a sibling route that MUST still be present. Without this, a zero above
#    could equally mean the grep found nothing at all (wrong path, wrong encoding, unreadable file).
CTRL=$(strings -a -el unpacked/Accounting.Api.dll 2>/dev/null | grep -c "create-tax-invoice")
# 3. public login still 200 through the full CDN -> proxy -> app path
LOGIN=$(curl -s -o /dev/null -w "%{http_code}" "https://teas.kazaki-rio.com/login")

echo "-- probe results --"
[ "$ST" = "online" ] && echo "PASS pm2_status=$ST" || echo "FAIL pm2_status=$ST"
[ "$VERSION" = "$VER" ] && echo "PASS version=$VERSION" || echo "FAIL version=$VERSION want=$VER"
[ "$MS" = "0" ] && echo "PASS mark_settled_route_removed (0 refs in the deployed assembly)" || echo "FAIL mark_settled still present in the assembly (refs=$MS)"
if [ "$CTRL" -ge 1 ] 2>/dev/null; then
  echo "PASS control_route_present refs=$CTRL (so the zero above is a real absence, not a failed grep)"
else
  echo "FAIL control_route refs=$CTRL -- the grep found nothing at all, probe 1 proves nothing"
fi
[ "$LOGIN" = "200" ] && echo "PASS public_login=$LOGIN" || echo "FAIL public_login=$LOGIN"

echo "== post-deploy settled-invoice census (compare with pre-deploy above) =="
sudo -u postgres psql -d teas -tAc "
  SELECT 'co'||company_id||' settled='||count(*)
  FROM sales.billing_notes WHERE upper(status)='SETTLED' AND company_id IN (2,3)
  GROUP BY company_id ORDER BY company_id;"

if [ "$ST" = "online" ] && [ "$VERSION" = "$VER" ] && [ "$MS" = "0" ] \
   && [ "$CTRL" -ge 1 ] 2>/dev/null && [ "$LOGIN" = "200" ]; then
  echo "DEPLOY_OK version=$VERSION scripts=$TOTALSCRIPTS"
  echo "NOTE: the ภ.พ.30 non-VAT 422 and the draft-run filing refusal need an AUTHENTICATED"
  echo "      session, so they are NOT probed here — they are the Tier-4 browser leg."
  rm -rf unpacked.old
else
  echo "DEPLOY_FAILED -- rolling back"
  pm2 logs teas-api --lines 60 --nostream 2>&1 | grep -iE 'error|exception|fail|42501' | tail -15
  mv unpacked unpacked.broken-v$VER; mv unpacked.old unpacked
  pm2 restart teas-api >/dev/null 2>&1; sleep 10
  echo "ROLLED_BACK status=$(pm2 jlist | jq -r '.[]|select(.name=="teas-api")|.pm2_env.status')"
  echo "NOTE: R2 carries no EF migration, so a rollback of the binary is a complete rollback."
  exit 1
fi
