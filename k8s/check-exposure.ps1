# Exposure gate for the k8s topology - same rule as Phase 3/4/5: loopback
# only until Phase 7 completes. Asserts, fail-closed:
#   - every Service in corewebforms is ClusterIP (no NodePort, no LoadBalancer)
#   - no Ingress objects exist
#   - nothing in the namespace publishes a host port
param()
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}

$services = kubectl -n corewebforms get svc -o json | ConvertFrom-Json
$badTypes = @($services.items | Where-Object { $_.spec.type -ne 'ClusterIP' })
Check 'all Services are ClusterIP' ($badTypes.Count -eq 0)
if ($badTypes.Count -gt 0) {
    $badTypes | ForEach-Object { Write-Host "  non-ClusterIP: $($_.metadata.name) $($_.spec.type)" }
}

$nodePorts = @($services.items | ForEach-Object { $_.spec.ports } | Where-Object { $_.nodePort })
Check 'no Service allocates a nodePort' ($nodePorts.Count -eq 0)

$ingress = @(kubectl -n corewebforms get ingress -o name 2>$null)
Check 'no Ingress objects' ($ingress.Count -eq 0)

$hostPorts = kubectl -n corewebforms get pods -o json | ConvertFrom-Json
$hostNetwork = @($hostPorts.items | Where-Object { $_.spec.hostNetwork })
$portMappings = @($hostPorts.items | ForEach-Object { $_.spec.containers } | ForEach-Object { $_.ports } | Where-Object { $_.hostPort })
Check 'no pods use hostNetwork' ($hostNetwork.Count -eq 0)
Check 'no containers publish hostPort' ($portMappings.Count -eq 0)

if ($failures -gt 0) { throw "exposure-check-k8s: $failures failing check(s)" }
Write-Host 'exposure-check-k8s: all checks passed'
