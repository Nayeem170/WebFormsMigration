param(
    [string]$Project = 'corewebforms',
    # The post-bind state: gateway TLS 8443 is the ONE port allowed to bind
    # non-loopback. Everything else must stay loopback or unpublished, and
    # any port outside the expected set fails - so a debug publish left
    # behind cannot pass silently. Default (no switch) is the pre-bind
    # state: everything loopback.
    [switch]$OpenedGate
)

$ErrorActionPreference = 'Stop'

$failures = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    Write-Host "$(if ($ok) { 'PASS' } else { 'FAIL' }) $name ($detail)"
    if (-not $ok) { $script:failures++ }
}

$raw = docker compose -p $Project ps --all --format json 2>$null
if (-not $raw) { throw "no running compose project '$Project' - start the stack first" }
$services = @($raw | ForEach-Object { $_ | ConvertFrom-Json })

# The complete expected host-published set (base shape; the TEST-PORTS
# overlay adds the direct-suite ports, also loopback).
$expected = @{
    'gateway'        = @(8080, 8443)
    'postgres'       = @(15432)
    'redis'          = @(16379)
    'catalog'        = @(18094)
    'orders'         = @(18095)
    'catalog2'       = @(18096)
    'orders2'        = @(18097)
    'frontend'       = @()
    'frontend2'      = @()
    'catalog-migrator' = @()
    'orders-migrator'  = @()
    'otel-collector'   = @()
}

# One port may leave loopback, ever: gateway TLS. Plain-HTTP 8080 stays
# loopback even after the bind - the exposed surface is TLS-only. Keyed
# 'service/port' (a nested @(@(...)) would flatten in PS and silently
# match nothing - the bug this line had on first use).
$nonLoopbackAllowed = if ($OpenedGate) { @{ 'gateway/8443' = $true } } else { @{} }

$seen = @{}
foreach ($svc in $services) {
    $name = $svc.Service
    $hostPublished = @($svc.Publishers | Where-Object { $_.PublishedPort -gt 0 })
    $allowedPorts = if ($expected.ContainsKey($name)) { $expected[$name] } else { @() }

    foreach ($pub in $hostPublished) {
        $port = [int]$pub.PublishedPort
        $seen["$name/$port"] = $true
        if ($allowedPorts -notcontains $port) {
            Check "$name port $port->/$($pub.TargetPort) is in the EXPECTED set" $false 'unexpected host publish'
            continue
        }
        $mayExpose = $nonLoopbackAllowed.ContainsKey("$name/$port")
        $ip = $pub.URL
        $bindsLoopback = ($ip -eq '127.0.0.1')
        Check "$name port $port->/$($pub.TargetPort) binds $(if ($mayExpose) { 'an allowed ' } else { 'loopback only ' })($ip)" `
            ($bindsLoopback -or [bool]$mayExpose) "HostIp=$ip"
    }
}

if ($failures -gt 0) { Write-Host "exposure-check: $failures FAILED"; exit 1 }
Write-Host 'exposure-check: all checks passed'
