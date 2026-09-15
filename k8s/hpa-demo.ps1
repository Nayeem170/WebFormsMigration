# Exit criterion: a load ramp grows replicas and a load drop shrinks them,
# with suites green at both ends. Per-pod positive controls: a scaled-up pod
# must be shown to have SERVED traffic (per-pod logs), not merely Ready.
param(
    [string]$GatewayUrl = 'http://localhost:18080',
    [string]$OrdersUrl = 'http://localhost:18095'
)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}
function Get-Replicas([string]$dep) {
    kubectl -n corewebforms get hpa $dep -o jsonpath='{.status.currentReplicas}'
}

foreach ($dep in 'catalog', 'orders', 'frontend') {
    Check "$dep starts at 2 replicas" ((Get-Replicas $dep) -eq '2')
}
$podsBefore = @(kubectl -n corewebforms get pods -o name)

$root = Split-Path -Parent $PSScriptRoot
$k6 = Join-Path $root 'artifacts\tools\k6\k6-v2.2.0-windows-amd64\k6.exe'
if (-not (Test-Path $k6)) { throw "k6 not found at $k6" }
$scenarios = Join-Path $root 'load\k6-scenarios.js'

Write-Host '  starting load ramp (orders-write 16 VUs + dashboard 8 VUs, 150s)'
$env:ORDERS = $OrdersUrl
$env:FRONT = $GatewayUrl
$env:OP = 'orders-write'; $env:VUS = '16'; $env:DUR = '150s'
$k6a = Start-Process $k6 -ArgumentList 'run', $scenarios -WindowStyle Hidden -PassThru -RedirectStandardOutput "$env:TEMP\k6-orders.out" -RedirectStandardError "$env:TEMP\k6-orders.err"
$env:OP = 'dashboard'; $env:VUS = '8'; $env:DUR = '150s'
$k6b = Start-Process $k6 -ArgumentList 'run', $scenarios -WindowStyle Hidden -PassThru -RedirectStandardOutput "$env:TEMP\k6-dash.out" -RedirectStandardError "$env:TEMP\k6-dash.err"

$peak = @{}
$deadline = (Get-Date).AddSeconds(200)
while ((Get-Date) -lt $deadline -and -not ($k6a.HasExited -and $k6b.HasExited)) {
    foreach ($dep in 'catalog', 'orders', 'frontend') {
        $r = [int](Get-Replicas $dep)
        if (-not $peak.ContainsKey($dep) -or $r -gt $peak[$dep]) { $peak[$dep] = $r }
    }
    Start-Sleep -Seconds 10
}
Write-Host ("  peak replicas during ramp: catalog={0} orders={1} frontend={2}" -f $peak['catalog'], $peak['orders'], $peak['frontend'])

Check 'orders or catalog scaled up (>= 3)' (($peak['orders'] -ge 3) -or ($peak['catalog'] -ge 3))

# Positive control: a NEW pod (created during the ramp) must have served.
$newServing = @()
foreach ($pod in kubectl -n corewebforms get pods -o name) {
    if ($podsBefore -notcontains $pod) {
        $short = $pod -replace '^pod/', ''
        $app = ($short -split '-')[0]
        $logs = kubectl -n corewebforms logs $short --since=3m 2>$null
        $served = @($logs | Select-String 'Request finished HTTP').Count
        if ($served -gt 0) { $newServing += "$short($served reqs)" }
    }
}
Check "scaled-up pods served traffic ($($newServing.Count): $($newServing -join ', '))" ($newServing.Count -ge 1)

Write-Host '  waiting for scale-down (60s stabilization + 1 pod/30s policy)'
$deadline = (Get-Date).AddSeconds(360)
$shrunk = $false
while ((Get-Date) -lt $deadline) {
    $c = [int](Get-Replicas 'catalog'); $o = [int](Get-Replicas 'orders'); $f = [int](Get-Replicas 'frontend')
    if ($c -le 2 -and $o -le 2 -and $f -le 2) { $shrunk = $true; break }
    Start-Sleep -Seconds 10
}
Check 'all services back at <= 2 replicas after load drop' $shrunk

foreach ($dep in 'catalog', 'orders', 'frontend') {
    $ready = kubectl -n corewebforms get deployment $dep -o jsonpath='{.status.readyReplicas}'
    Check "$dep has 2 ready replicas at the end" ($ready -eq '2')
}

if ($failures -gt 0) { throw "hpa-demo: $failures failing check(s)" }
Write-Host 'hpa-demo: all checks passed'
