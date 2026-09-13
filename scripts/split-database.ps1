param(
    [string]$Source = "$PSScriptRoot\..\App_Data\inventory.db",
    [string]$CatalogOut = "$PSScriptRoot\..\App_Data\catalog.db",
    [string]$OrdersOut = "$PSScriptRoot\..\App_Data\orders.db",
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path $Source).Path

if (Get-NetTCPConnection -LocalPort 8094, 8095 -State Listen -ErrorAction SilentlyContinue) {
    throw "Catalog (8094) or Orders (8095) is running; stop both services before splitting."
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

if (-not $DryRun) {
    $backup = Join-Path (Split-Path -Parent $source) "inventory.backup-$stamp.db"
    Copy-Item $source $backup
    foreach ($sidecar in @('-wal', '-shm')) {
        if (Test-Path "$source$sidecar") { Copy-Item "$source$sidecar" "$backup$sidecar" }
    }
    "Backup: $backup"
}

$workDir = Join-Path $env:TEMP ("dbsplit-" + $stamp)
New-Item -ItemType Directory -Path $workDir -Force | Out-Null
$workSource = Join-Path $workDir 'inventory.db'
Copy-Item $source $workSource
if (Test-Path "$source-wal") { Copy-Item "$source-wal" "$workSource-wal" }
if (Test-Path "$source-shm") { Copy-Item "$source-shm" "$workSource-shm" }

$catalogOut = if ($DryRun) { Join-Path $workDir 'catalog.db' } else { $CatalogOut }
$ordersOut = if ($DryRun) { Join-Path $workDir 'orders.db' } else { $OrdersOut }

foreach ($target in @($catalogOut, $ordersOut)) {
    foreach ($ext in @('', '-wal', '-shm')) {
        if (Test-Path "$target$ext") { Remove-Item -Force "$target$ext" }
    }
}

dotnet run --project "$root\Microservices\tools\DbSplit\DbSplit.csproj" -- --source $workSource --catalog $catalogOut --orders $ordersOut
if ($LASTEXITCODE -ne 0) { throw "DbSplit failed" }

if ($DryRun) {
    "DRY RUN complete; outputs in $workDir (real run writes $CatalogOut and $OrdersOut)"
} else {
    "Split complete: $catalogOut, $ordersOut"
}