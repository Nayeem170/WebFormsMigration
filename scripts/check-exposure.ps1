param(
    [string]$Project = 'corewebforms'
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

$internalOnly = @('catalog', 'orders', 'catalog-migrator', 'orders-migrator')
foreach ($svc in $services) {
    $name = $svc.Service
    $publishers = @($svc.Publishers)
    $hostPublished = @($publishers | Where-Object { $_.PublishedPort -gt 0 })

    if ($internalOnly -contains $name) {
        $count = $hostPublished.Count
        Check "$name publishes nothing to the host" ($count -eq 0) "host-published ports=$count"
        continue
    }

    foreach ($pub in $hostPublished) {
        $ip = $pub.URL
        Check "$name port $($pub.PublishedPort)->$($pub.TargetPort) binds loopback only" ($ip -eq '127.0.0.1') "HostIp=$ip"
    }
}

if ($failures -gt 0) { Write-Host "exposure-check: $failures FAILED"; exit 1 }
Write-Host "exposure-check: all checks passed"
