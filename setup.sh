#!/usr/bin/env bash
# =============================================================================
# TEAS first-run setup (macOS / Linux / Git-Bash on Windows).
#
# Brings up the PostgreSQL dependency via Docker, lets you choose whether to seed
# a demo company with sample data, and prints how to start the backend + frontend.
# A FRESH install seeds NO placeholder data: you create the super-admin and your
# first company in the app's onboarding wizard. The demo company is OPTIONAL.
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

# --- 3. Demo-data choice -----------------------------------------------------
echo
read -r -p "Seed a demo company with sample data? [y/N] " REPLY || REPLY=""
case "${REPLY}" in
  [yY]|[yY][eE][sS]) SEED_DEMO=true ;;
  *)                 SEED_DEMO=false ;;
esac
export Database__SeedDemoData="$SEED_DEMO"
if [ "$SEED_DEMO" = "true" ]; then
  warn "Demo data ENABLED — the backend will also seed sample companies and a seeded 'admin' login."
else
  say "Demo data DISABLED — a clean install. You will create the super-admin in onboarding."
fi

# --- 4. How to run -----------------------------------------------------------
cat <<EOF

$(say "Next steps")
-------------------------------------------------------------------------------
1) Backend (.NET 10):  this same SeedDemoData choice must be visible to it, so run
   it from THIS shell (the export above is in effect), or set the env var yourself.

   Database__SeedDemoData=$SEED_DEMO \\
   ASPNETCORE_ENVIRONMENT=Development \\
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
if [ "$SEED_DEMO" != "true" ]; then
  cat <<'EOF'
   (No demo company was seeded — onboarding starts from a clean slate.)
EOF
fi
echo "-------------------------------------------------------------------------------"
