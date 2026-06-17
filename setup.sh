#!/usr/bin/env bash
# =============================================================================
# TEAS first-run setup (macOS / Linux / Git-Bash on Windows).
#
# Brings up the PostgreSQL dependency via Docker, lets you choose Development vs
# Production and whether to seed a demo company, and prints how to start the
# backend + frontend. A FRESH install seeds NO placeholder data: you create the
# super-admin and your first company in the app's onboarding wizard. The demo
# company is OPTIONAL.
#
# Idempotent: safe to re-run. It does not start the long-running servers itself —
# it prints the exact commands so you stay in control of those terminals.
# =============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT"

say()  { printf '\033[1;36m%s\033[0m\n' "$*"; }
warn() { printf '\033[1;33m%s\033[0m\n' "$*"; }
err()  { printf '\033[1;31m%s\033[0m\n' "$*" >&2; }

# --- 1. Docker present? ------------------------------------------------------
if ! command -v docker >/dev/null 2>&1; then
  err "Docker is not installed or not on PATH. Install Docker Desktop / Engine first:"
  err "  https://docs.docker.com/get-docker/"
  exit 1
fi
if ! docker info >/dev/null 2>&1; then
  err "Docker is installed but the daemon is not running. Start Docker and re-run ./setup.sh"
  exit 1
fi

# 'docker compose' (v2) is preferred; fall back to legacy 'docker-compose'.
if docker compose version >/dev/null 2>&1; then
  COMPOSE="docker compose"
elif command -v docker-compose >/dev/null 2>&1; then
  COMPOSE="docker-compose"
else
  err "Neither 'docker compose' nor 'docker-compose' is available."
  exit 1
fi

# --- 2. Bring up PostgreSQL --------------------------------------------------
say "Starting PostgreSQL (docker compose up -d)…"
$COMPOSE up -d
say "PostgreSQL is starting on localhost:5432 (db: accounting_dev / user: accounting)."

# --- 3. Development or Production? --------------------------------------------
echo
say "Setup mode:"
echo "  [1] Development  — for trying it out / local development."
echo "                     Swagger UI at /swagger, verbose logging, a built-in dev"
echo "                     JWT signing key. Easiest way to get going."
echo "  [2] Production   — for a real deployment."
echo "                     No Swagger, stricter logging. You MUST set a strong"
echo "                     Jwt:SigningKey (in backend/src/Accounting.Api/appsettings.Secrets.json"
echo "                     or the Jwt__SigningKey env var), and set the instance MFA key"
echo "                     during onboarding. Do NOT use the demo data."
read -r -p "Choose [1/2] (default 1): " MODE_REPLY || MODE_REPLY=""
case "${MODE_REPLY}" in
  2) ASPNET_ENV="Production" ;;
  *) ASPNET_ENV="Development" ;;
esac
export ASPNETCORE_ENVIRONMENT="$ASPNET_ENV"
say "Mode: $ASPNET_ENV"

# --- 4. Demo-data choice -----------------------------------------------------
echo
read -r -p "Seed a demo company with sample data? [y/N] " REPLY || REPLY=""
case "${REPLY}" in
  [yY]|[yY][eE][sS]) SEED_DEMO=true ;;
  *)                 SEED_DEMO=false ;;
esac
export Database__SeedDemoData="$SEED_DEMO"
if [ "$SEED_DEMO" = "true" ]; then
  warn "Demo data ENABLED — the backend will also seed sample companies and a seeded 'admin' login."
  if [ "$ASPNET_ENV" = "Production" ]; then
    warn "  (Demo data in Production is for evaluation only — not a real deployment.)"
  fi
else
  say "Demo data DISABLED — a clean install. You will create the super-admin in onboarding."
fi

# --- 5. How to run -----------------------------------------------------------
cat <<EOF

$(say "Next steps")
-------------------------------------------------------------------------------
1) Backend (.NET 10): run it from THIS shell so the choices above are in effect,
   or set the env vars yourself.

   Database__SeedDemoData=$SEED_DEMO \\
   ASPNETCORE_ENVIRONMENT=$ASPNET_ENV \\
   ASPNETCORE_URLS=http://localhost:5080 \\
   dotnet run --project backend/src/Accounting.Api

2) Frontend (Next.js 15), in a SECOND terminal:

   cd frontend
   pnpm install
   pnpm dev

3) Open the app:  http://localhost:3000

   Complete ONBOARDING:
     • create the first super-admin (username + password), then
     • create your first company.
EOF
if [ "$ASPNET_ENV" = "Production" ]; then
  cat <<'EOF'

   PRODUCTION: before exposing this, set a strong Jwt:SigningKey, e.g. create
   backend/src/Accounting.Api/appsettings.Secrets.json (git-ignored) with:
     { "Jwt": { "SigningKey": "<a long random secret>" } }
EOF
fi
if [ "$SEED_DEMO" != "true" ]; then
  cat <<'EOF'
   (No demo company was seeded — onboarding starts from a clean slate.)
EOF
fi
echo "-------------------------------------------------------------------------------"
