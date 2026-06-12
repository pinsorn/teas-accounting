# TEAS M15 — nightly logical backup of the dev DB (accounting_dev).
# Registered as the Windows scheduled task "TEAS dev DB dump" (daily 03:30); run manually any time:
#   powershell -ExecutionPolicy Bypass -File scripts\dump-dev-db.ps1
# Custom-format dumps restore with:
#   pg_restore -h localhost -U accounting -d accounting_dev --clean --if-exists <file>
param(
    [string]$OutDir = 'Y:\TEAS-backups',
    [string]$PgBin  = 'S:\Program Files\PostgreSQL\18\bin',
    [int]$Keep      = 14
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$env:PGPASSWORD = 'accounting_dev_password'   # dev-only credentials (CLAUDE.md §6)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $OutDir "accounting_dev_$stamp.dump"
& (Join-Path $PgBin 'pg_dump.exe') -h localhost -p 5432 -U accounting -d accounting_dev -Fc -f $out
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit $LASTEXITCODE" }

# Prune: keep the newest $Keep dumps.
Get-ChildItem $OutDir -Filter 'accounting_dev_*.dump' |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip $Keep |
    Remove-Item -Force

Write-Output "dumped $out"
