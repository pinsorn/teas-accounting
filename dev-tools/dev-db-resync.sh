#!/usr/bin/env bash
# ============================================================
# dev-tools/dev-db-resync.sh  —  Sprint 14.5 §14 one-time repair
# ============================================================
# Resyncs sys.number_sequences.current_value to the MAX running number
# actually present in posted documents, on the long-lived shared dev DB.
# Idempotent + non-destructive (see tools/dev-db-resync.sql header).
#
# Usage:
#   dev-tools/dev-db-resync.sh                 # uses defaults below
#   PGHOST=... PGPORT=... PGDATABASE=... \
#   PGUSER=... PGPASSWORD=... dev-tools/dev-db-resync.sh
#
# Defaults match infra/.env.example + appsettings.json (accounting_dev).
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SQL_FILE="${SCRIPT_DIR}/../tools/dev-db-resync.sql"

export PGHOST="${PGHOST:-localhost}"
export PGPORT="${PGPORT:-5432}"
export PGDATABASE="${PGDATABASE:-accounting_dev}"
export PGUSER="${PGUSER:-accounting}"
export PGPASSWORD="${PGPASSWORD:-accounting_dev_password}"

if [[ ! -f "${SQL_FILE}" ]]; then
  echo "ERROR: ${SQL_FILE} not found" >&2
  exit 1
fi

echo "▶ Resyncing number sequences on ${PGUSER}@${PGHOST}:${PGPORT}/${PGDATABASE}"
echo "  (idempotent — re-running on a clean DB updates 0 rows)"

# -v ON_ERROR_STOP=1 → the wrapped transaction rolls back on any error.
psql -v ON_ERROR_STOP=1 -f "${SQL_FILE}"

echo "✓ dev-db-resync complete."
