@echo off
REM ============================================================================
REM  TEAS first-run setup (Windows / cmd.exe).
REM
REM  Brings up the PostgreSQL dependency via Docker, lets you choose Development
REM  vs Production and whether to seed a demo company, and prints how to start
REM  the backend + frontend. A FRESH install seeds NO placeholder data: you
REM  create the super-admin and your first company in the app's onboarding
REM  wizard. The demo company is OPTIONAL.
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

REM --- 3. Development or Production? -----------------------------------------
echo.
echo Setup mode:
echo   [1] Development  - for trying it out / local development.
echo                      Swagger UI at /swagger, verbose logging, a built-in dev
echo                      JWT signing key. Easiest way to get going.
echo   [2] Production   - for a real deployment.
echo                      No Swagger, stricter logging. You MUST set a strong
echo                      Jwt:SigningKey (in backend\src\Accounting.Api\appsettings.Secrets.json
echo                      or the Jwt__SigningKey env var), and set the instance MFA
echo                      key during onboarding. Do NOT use the demo data.
set "ASPNET_ENV=Development"
set /p "MODE_REPLY=Choose [1/2] (default 1): "
if "!MODE_REPLY!"=="2" set "ASPNET_ENV=Production"
echo Mode: !ASPNET_ENV!

REM --- 4. Demo-data choice ---------------------------------------------------
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

REM --- 5. How to run ---------------------------------------------------------
echo.
echo Next steps
echo -------------------------------------------------------------------------------
echo 1^) Backend (.NET 10^): run it from THIS window so the choices above are in
echo    effect, or set the env vars yourself.
echo.
echo    set Database__SeedDemoData=!SEED_DEMO!
echo    set ASPNETCORE_ENVIRONMENT=!ASPNET_ENV!
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
if /i "!ASPNET_ENV!"=="Production" (
  echo.
  echo    PRODUCTION: before exposing this, set a strong Jwt:SigningKey, e.g. create
  echo    backend\src\Accounting.Api\appsettings.Secrets.json ^(git-ignored^) with:
  echo      { "Jwt": { "SigningKey": "^<a long random secret^>" } }
)
if not "!SEED_DEMO!"=="true" echo    (No demo company was seeded - onboarding starts from a clean slate.^)
echo -------------------------------------------------------------------------------
endlocal
