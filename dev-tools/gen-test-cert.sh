#!/usr/bin/env bash
# Sprint 13c — generate a self-signed dev PFX for XAdES-BES testing.
# NOT for production (Tier 2/3 use a CA-issued Class-2 cert). Subject mirrors
# the expected Thai company cert format (serialNumber = 13-digit Tax ID).
#
#   ./dev-tools/gen-test-cert.sh [password] [out.pfx]
#   ./dev-tools/gen-test-cert.sh dev123 backend/secrets/dev-cert.pfx
#
# Then set (appsettings.Development.json or .env):
#   ETax:Signing:PfxPath     = secrets/dev-cert.pfx
#   ETax:Signing:PfxPassword = dev123
set -euo pipefail

PASSWORD="${1:-DevPfxPassword}"
OUT="${2:-./secrets/dev-cert.pfx}"

mkdir -p "$(dirname "$OUT")"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout "$TMP/key.pem" -out "$TMP/cert.pem" \
  -subj "/C=TH/O=TEAS Dev Company/CN=teas-dev/serialNumber=0123456789012"

openssl pkcs12 -export -out "$OUT" \
  -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
  -password "pass:$PASSWORD" -name "TEAS Dev Signing Key"

echo "Generated: $OUT (password: $PASSWORD)"
echo "Set ETax:Signing:PfxPath / PfxPassword accordingly. NEVER commit the .pfx."
