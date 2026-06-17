@echo off
REM ============================================================================
REM  TEAS first-run setup (Windows / cmd.exe).
REM
REM  Brings up the PostgreSQL dependency via Docker, lets you choose whether to
REM  seed a demo company with sample data, and prints how to start the backend +
REM  frontend. A FRESH install seeds NO placeholder data: you create the
REM  super-admin and your first company in the app's onboarding wizard. The demo
REM  company is OPTIONAL.
REM
REM  Idempotent: safe to re-run. It does not start the long-running servers
REM  itself - it prints the exact commands so you stay in control.
REM ============================================================================
setlocal EnableDelayedExpansion
cd /d "%~dp0"

REM --- 1. Docker present? ----------------------------------------------------
where docker >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Docker is not installed or not on PATH. Install Docker Desktop first:
  echo         https://docs.docker.com/get-docker/
  exit /b 1
)
docker info >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Docker is installed but the daemon is not running. Start Docker and re-run setup.bat
  exit /b 1
)

REM Prefer 'docker compose' (v2); fall back to legacy 'docker-compose'.
set "COMPOSE=docker compose"
docker compose version >nul 2>&1
if errorlevel 1 (
  where docker-compose >nul 2>&1
  if errorlevel 1 (
    echo [ERROR] Neither "docker compose" nor "docker-compose" is available.
    exit /b 1
  )
  set "COMPOSE=docker-compose"
)

REM --- 2. Bring up PostgreSQL ------------------------------------------------
echo Starting PostgreSQL (%COMPOSE% up -d)...
%COMPOSE% up -d
if errorlevel 1 (
  echo [ERROR] Failed to start PostgreSQL via Docker Compose.
  exit /b 1
)
echo PostgreSQL is starting on localhost:5432 (db: accounting_dev / user: accounting).

REM --- 3. Demo-data choice ---------------------------------------------------
echo.
set "SEED_DEMO=false"
set /p "REPLY=Seed a demo company with sample data? [y/N] "
if /i "!REPLY!"=="y"   set "SEED_DEMO=true"
if /i "!REPLY!"=="yes" set "SEED_DEMO=true"
set "Database__SeedDemoData=!SEED_DEMO!"
if "!SEED_DEMO!"=="true" (
  echo Demo data ENABLED - the backend will also seed sample companies and a seeded 'admin' login.
) else (
  echo Demo data DISABLED - a clean install. You will create the super-admin in onboarding.
)

REM --- 4. How to run ---------------------------------------------------------
echo.
echo Next steps
echo -------------------------------------------------------------------------------
echo 1^) Backend (.NET 10^): run it from THIS window so the SeedDemoData choice is in
echo    effect, or set Database__SeedDemoData yourself.
echo.
echo    set Database__SeedDemoData=!SEED_DEMO!
echo    set ASPNETCORE_ENVIRONMENT=Development
echo    set ASPNETCORE_URLS=http://localhost:5080
echo    dotnet run --project backend\src\Accounting.Api
echo.
echo 2^) Frontend (Next.js 15^), in a SECOND terminal:
echo.
echo    cd frontend
echo    pnpm install
echo    pnpm dev
echo.
echo 3^) Open the app:  http://localhost:3000
echo.
echo    Complete ONBOARDING:
echo      - create the first super-admin (username + password^), then
echo      - create your first company.
if not "!SEED_DEMO!"=="true" echo    (No demo company was seeded - onboarding starts from a clean slate.^)
echo -------------------------------------------------------------------------------
endlocal
