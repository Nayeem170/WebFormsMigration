# Phase 6 k8s verification. Fail-closed, per-pod evidence:
#   - bring-up: replicas Ready, HPA/PDB objects, pool counts = 1 per pod
#   - health split semantics (liveness unconditional, readiness sees DB loss)
#   - Frontend->Catalog X-Instance diversity via POD LOGS (not gateway) -
#     the gateway will look fine while the internal hop is pinned
#   - correlation id reaches catalog pod logs from a gateway page GET
param(
    [string]$GatewayUrl = 'http://127.0.0.1:18080'
)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}

# --- bring-up -------------------------------------------------------------
foreach ($dep in 'catalog', 'orders', 'frontend') {
    $ready = kubectl -n corewebforms get deployment $dep -o jsonpath='{.status.readyReplicas}'
    Check "$dep deployment has 2 ready replicas" ($ready -eq '2')
}
$gw = kubectl -n corewebforms get deployment gateway -o jsonpath='{.status.readyReplicas}'
Check 'gateway deployment has 1 ready replica' ($gw -eq '1')

foreach ($dep in 'catalog', 'orders', 'frontend') {
    $hpa = kubectl -n corewebforms get hpa $dep -o jsonpath='{.spec.minReplicas}/{.spec.maxReplicas}'
    Check "hpa $dep min/max is 2/4" ($hpa -eq '2/4')
}
foreach ($dep in 'catalog', 'orders', 'frontend') {
    $pdb = kubectl -n corewebforms get pdb $dep -o jsonpath='{.spec.minAvailable}'
    Check "pdb $dep minAvailable 1" ($pdb -eq '1')
}
foreach ($dep in 'catalog', 'orders') {
    $grace = kubectl -n corewebforms get deployment $dep -o jsonpath='{.spec.template.spec.terminationGracePeriodSeconds}'
    Check "$dep terminationGracePeriodSeconds 35" ($grace -eq '35')
}

# --- pool counts: single Service DNS endpoint, per pod --------------------
# Per-pod attribution (the standing rule): every RUNNING pod must have
# logged the pool with exactly 1 endpoint. Frontend writes these lines to
# its file logger (App_Data/logs/app.log, like bare/local mode); Orders
# logs to console. Source per app, pod by pod, so one pod cannot mask
# another's missing line.
function Test-K8sPool([string]$app, [string]$poolName, [switch]$FromFile) {
    $pods = kubectl -n corewebforms get pods -l "app=$app" -o json | ConvertFrom-Json
    $running = @($pods.items | Where-Object { $_.status.phase -eq 'Running' })
    if ($running.Count -lt 1) { return $false }
    foreach ($pod in $running) {
        if ($FromFile) {
            $logs = kubectl -n corewebforms exec $pod.metadata.name -- sh -c "grep '$poolName endpoint pool' /app/App_Data/logs/app.log 2>/dev/null" 2>$null
        } else {
            $logs = kubectl -n corewebforms logs $pod.metadata.name 2>$null
        }
        $hits = @($logs | Select-String "$poolName endpoint pool: (\d+) endpoint")
        if ($hits.Count -lt 1) { return $false }
        foreach ($hit in $hits) { if ([int]$hit.Matches[0].Groups[1].Value -ne 1) { return $false } }
    }
    return $true
}
Check 'frontend pods log Catalog pool with 1 endpoint each' (Test-K8sPool frontend 'Catalog' -FromFile)
Check 'frontend pods log Orders pool with 1 endpoint each' (Test-K8sPool frontend 'Orders' -FromFile)
Check 'orders pods log Catalog pool with 1 endpoint each' (Test-K8sPool orders 'Catalog')

# --- health split ---------------------------------------------------------
# Phase 7: /health* is unroutable from the public port (recon oracle) -
# the gateway must 404 it there and serve it on the management port.
$live = Invoke-WebRequest "$GatewayUrl/health/live" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 10
Check 'gateway public port refuses /health/live (404)' ($live.StatusCode -eq 404)
$mgmt = kubectl -n corewebforms exec deployment/gateway -- curl -sf http://localhost:8090/health/live 2>$null
Check 'gateway management port serves /health/live' ($LASTEXITCODE -eq 0 -and "$mgmt" -match 'alive')
$feReady = Invoke-WebRequest "$GatewayUrl/" -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 10
Check 'frontend answers through gateway' ($feReady.StatusCode -eq 200)

$catLive = kubectl -n corewebforms exec deploy/catalog -- curl -sf http://127.0.0.1:8094/health/live 2>$null
Check 'catalog pod /health/live answers ok' ($LASTEXITCODE -eq 0 -and "$catLive" -match 'alive')
$ordLive = kubectl -n corewebforms exec deploy/orders -- curl -sf http://127.0.0.1:8095/health/live 2>$null
Check 'orders pod /health/live answers ok' ($LASTEXITCODE -eq 0 -and "$ordLive" -match 'alive')
$catReady = kubectl -n corewebforms exec deploy/catalog -- curl -sf http://127.0.0.1:8094/health/ready 2>$null
Check 'catalog pod /health/ready answers ready' ($LASTEXITCODE -eq 0 -and "$catReady" -match 'ready')

# Readiness must SEE database loss: scale postgres to 0, catalog readiness
# must flip (Service ready-addresses shrink to none); liveness must keep
# pods alive. NOTE: query .subsets[0].addresses - the endpoints object
# keeps printing notReadyAddresses, so '{.subsets}' alone can never go
# empty and the check would be unfalsifiable.
kubectl -n corewebforms scale deployment postgres --replicas=0 | Out-Null
$deadline = (Get-Date).AddSeconds(150)
$notReady = $false
while ((Get-Date) -lt $deadline) {
    $eps = kubectl -n corewebforms get endpoints catalog -o jsonpath='{.subsets[0].addresses[*].ip}'
    if (-not $eps) { $notReady = $true; break }
    Start-Sleep -Seconds 5
}
Check 'catalog readiness sees DB loss (ready addresses empty)' $notReady
$restarts = (kubectl -n corewebforms get pods -l app=catalog -o json | ConvertFrom-Json).items | ForEach-Object { $_.status.containerStatuses[0].restartCount }
Check 'catalog pods NOT restarted during DB loss (liveness stayed quiet)' (@($restarts | Where-Object { $_ -gt 0 }).Count -eq 0)
kubectl -n corewebforms scale deployment postgres --replicas=1 | Out-Null
$deadline = (Get-Date).AddSeconds(120)
$recovered = $false
while ((Get-Date) -lt $deadline) {
    $eps = kubectl -n corewebforms get endpoints catalog -o jsonpath='{.subsets[0].addresses[*].ip}'
    if ($eps) { $recovered = $true; break }
    Start-Sleep -Seconds 5
}
# Gated on having actually OBSERVED emptiness - otherwise this passes
# vacuously when the loss check itself failed.
Check 'catalog readiness recovers when DB returns' ($notReady -and $recovered)

# --- Frontend->Catalog diversity: POD LOGS, not the gateway ---------------
# Burst dashboard loads for 60s (well past the 20s pooled-lifetime), then
# require >= 2 distinct catalog pods having served /api/products during the
# burst. The burst is tagged with a correlation marker so only burst lines
# count.
# 32-hex markers: the gateway validates correlation ids (32 hex) and
# replaces non-conforming ones (Phase 7), so markers must pass validation
# to be traceable through to the catalog pods.
$marker = [guid]::NewGuid().ToString('N')
$catalogPodsBefore = @(kubectl -n corewebforms get pods -l app=catalog -o name)
$burstEnd = (Get-Date).AddSeconds(60)
$burstOk = $true
while ((Get-Date) -lt $burstEnd) {
    try {
        Invoke-WebRequest "$GatewayUrl/" -UseBasicParsing -TimeoutSec 10 -Headers @{ 'X-Correlation-ID' = $marker } | Out-Null
    } catch { $burstOk = $false }
    Start-Sleep -Milliseconds 300
}
Check 'diversity burst completed without request errors' $burstOk
$servingPods = @()
foreach ($pod in kubectl -n corewebforms get pods -l app=catalog -o name) {
    $logs = kubectl -n corewebforms logs ($pod -replace '^pod/', '') --since=2m 2>$null
    if (@($logs | Select-String ([regex]::Escape($marker))).Count -gt 0) { $servingPods += $pod }
}
Check "Frontend->Catalog diversity: >= 2 catalog pods served the burst ($($servingPods.Count) of $(($catalogPodsBefore | Measure-Object).Count))" ($servingPods.Count -ge 2)

# --- correlation id reaches catalog pod logs ------------------------------
# One gateway page GET with a fresh correlation id; it must appear in at
# least one catalog pod's logs (kubectl logs aggregation across pods).
$corr = [guid]::NewGuid().ToString('N')
Invoke-WebRequest "$GatewayUrl/" -UseBasicParsing -TimeoutSec 10 -Headers @{ 'X-Correlation-ID' = $corr } | Out-Null
Start-Sleep -Seconds 2
$corrHit = $false
foreach ($pod in kubectl -n corewebforms get pods -l app=catalog -o name) {
    $logs = kubectl -n corewebforms logs ($pod -replace '^pod/', '') --since=1m 2>$null
    if (@($logs | Select-String ([regex]::Escape($corr))).Count -gt 0) { $corrHit = $true }
}
Check 'correlation id reaches catalog pod logs' $corrHit

if ($failures -gt 0) { throw "k8s verification: $failures failing check(s)" }
Write-Host 'k8s verification: all checks passed'
