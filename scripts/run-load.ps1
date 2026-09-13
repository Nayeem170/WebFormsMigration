param(
    [string[]]$Ops = @('dashboard', 'products-page', 'catalog-read', 'orders-read', 'orders-write'),
    [string]$Dur = '20s',
    [string]$OutFile = 'load\baseline-results.md',
    [switch]$All
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$k6 = Join-Path $root 'artifacts\tools\k6\k6-v2.2.0-windows-amd64\k6.exe'
if (-not (Test-Path $k6)) { throw "k6 not found at $k6 (see load\baseline.md for install)" }

$resultsDir = Join-Path $root 'load\results'
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$outPath = Join-Path $root $OutFile

$levels = @{
    'dashboard'     = @(5, 25, 50)
    'products-page' = @(5, 25, 50)
    'catalog-read'  = @(5, 25, 50)
    'orders-read'   = @(5, 25, 50)
    'orders-write'  = @(2, 8, 16, 32)
}

$rows = [System.Collections.Generic.List[object]]::new()
$opsExpanded = $Ops | ForEach-Object { $_ -split ',' } | Where-Object { $_ }
foreach ($op in $opsExpanded) {
    foreach ($vus in $levels[$op]) {
        Write-Host "running ${op} at ${vus} VUs for ${Dur}..."
        $output = & $k6 'run' '--quiet' '-e' "OP=$op" '-e' "VUS=$vus" '-e' "DUR=$Dur" (Join-Path $root 'load\k6-scenarios.js') 2>&1 | ForEach-Object { "$_" }
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $output | Set-Content (Join-Path $resultsDir "$($stamp)-$op-vus$vus.txt")

        $text = $output -join "`n"
        $reqs = [regex]::Match($text, 'http_reqs[\.:\s]+(\d+)\s+([\d.]+)/s')
        $fail = [regex]::Match($text, 'http_req_failed[\.:\s:.]+([\d.]+)%')
        $p95 = [regex]::Match($text, 'p\(95\)=(\d+\.?\d*)ms')
        $row = [pscustomobject]@{
            Op     = $op
            Vus    = $vus
            Reqs   = if ($reqs.Success) { [int]$reqs.Groups[1].Value } else { 0 }
            Rps    = if ($reqs.Success) { [double]$reqs.Groups[2].Value } else { 0 }
            P95ms  = if ($p95.Success) { [double]$p95.Groups[1].Value } else { 0 }
            FailPc = if ($fail.Success) { [double]$fail.Groups[1].Value } else { -1 }
        }
        $rows.Add($row)
        Write-Host ("  {0,-14} vus {1,-3} rps {2,8:n1} p95 {3,8:n0}ms fail {4,5:n1}%" -f $row.Op, $row.Vus, $row.Rps, $row.P95ms, $row.FailPc)
    }
}

$header = @(
    '| op | vus | requests | rps | p95 ms | fail % |',
    '|---|---|---|---|---|---|'
)
$lines = $rows | ForEach-Object { "| $($_.Op) | $($_.Vus) | $($_.Reqs) | $([math]::Round($_.Rps, 1)) | $([math]::Round($_.P95ms, 0)) | $([math]::Round($_.FailPc, 1)) |" }
$existing = if (Test-Path $outPath) { Get-Content $outPath } else { @() }
($existing + '' + $header + $lines) | Set-Content $outPath
Write-Host "appended $($rows.Count) rows to $OutFile"
