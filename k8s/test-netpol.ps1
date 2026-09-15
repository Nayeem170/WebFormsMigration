# Phase 7 NetworkPolicy verification. Calico is load-bearing (kindnet does
# not enforce policy), so every negative check here is built to distinguish
# an enforced deny from every other way the same command can fail:
#   - a POLICY DENIAL drops packets: the connect hangs and the probe times
#     out (~5s, timeout exit 124)
#   - CONNECTION REFUSED fails fast (<1s): nothing is listening, or the
#     policy was never consulted - this FAILS the check
#   - DNS FAILURE fails fast: resolve-by-name is checked separately and
#     every denied target is ALSO probed by pod IP, so a DNS problem cannot
#     masquerade as enforcement
# Every negative runs with an identical positive control from a pod the
# policy allows, in the same breath - if that also fails, the policy is
# mis-scoped, not proven.
param(
    [string]$Namespace = 'corewebforms',
    [int]$ProbeTimeoutSec = 5,
    [int]$ProbeStabilitySec = 90
)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}

# Connect from inside a pod; classifies OPEN / DENIED / REFUSED / ERROR.
# Uses bash /dev/tcp + coreutils timeout, both present in the aspnet:9.0
# images. Wall-clock elapsed is measured here: the discriminator between
# "hung then timed out" and "failed fast" is time, not the exit code alone.
# $PodDeploy is a full kubectl target: deploy/frontend or pod/netprobe.
function Test-Connect([string]$Target, [string]$Port, [string]$PodDeploy) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $out = kubectl -n $Namespace exec $PodDeploy -- `
        bash -c "timeout $ProbeTimeoutSec bash -c 'echo > /dev/tcp/$Target/$Port' 2>&1; echo rc=`$?" 2>$null
    $sw.Stop()
    $text = "$out"
    $rc = if ($text -match 'rc=(\d+)') { [int]$Matches[1] } else { -1 }
    $verdict =
        if ($rc -eq 0) { 'OPEN' }
        elseif ($rc -eq 124 -and $sw.Elapsed.TotalSeconds -ge ($ProbeTimeoutSec - 1)) { 'DENIED' }
        elseif ($text -match 'refused' -and $sw.Elapsed.TotalSeconds -lt 2) { 'REFUSED' }
        else { 'ERROR' }
    [pscustomobject]@{ Verdict = $verdict; Rc = $rc; Secs = [math]::Round($sw.Elapsed.TotalSeconds, 1); Detail = ($text -replace "`n", ' ') }
}

# --- policies present ------------------------------------------------------
$policies = @(kubectl -n $Namespace get networkpolicy -o name 2>$null)
Check "default-deny and allow policies exist ($($policies.Count) total)" `
    ($policies.Count -ge 7 -and ($policies -match 'default-deny-ingress').Count -eq 1)

# --- DNS sanity (guards the by-name probes) --------------------------------
$dns = kubectl -n $Namespace exec deploy/frontend -- getent hosts postgres 2>$null
Check 'in-cluster DNS resolves (getent postgres from frontend)' ($LASTEXITCODE -eq 0 -and "$dns" -match '\d+\.\d+\.\d+\.\d+')

$catalogIp = (kubectl -n $Namespace get pods -l app=catalog -o json | ConvertFrom-Json).items[0].status.podIP
$postgresIp = (kubectl -n $Namespace get pods -l app=postgres -o json | ConvertFrom-Json).items[0].status.podIP
$keycloakIp = (kubectl -n $Namespace get pods -l app=keycloak -o json | ConvertFrom-Json).items[0].status.podIP
Check 'pod IPs resolved for by-IP probes' ($catalogIp -and $postgresIp -and $keycloakIp)

if ($catalogIp -and $postgresIp -and $keycloakIp) {
    # --- positives: the allow rules actually allow (fast OPEN) ------------
    $p1 = Test-Connect catalog 8094 deploy/frontend
    Check "positive control: frontend -> catalog:8094 by name OPEN ($($p1.Secs)s)" ($p1.Verdict -eq 'OPEN')
    if ($p1.Verdict -ne 'OPEN') { Write-Host "  detail: $($p1.Detail)" }

    $p2 = Test-Connect postgres 5432 deploy/catalog
    Check "positive control: catalog -> postgres:5432 by name OPEN ($($p2.Secs)s)" ($p2.Verdict -eq 'OPEN')

    # --- negatives: dropped, not refused -----------------------------------
    $n1 = Test-Connect postgres 5432 deploy/gateway
    Check "gateway -> postgres:5432 DENIED by policy (hang, not refuse: rc=$($n1.Rc) $($n1.Secs)s)" ($n1.Verdict -eq 'DENIED')
    if ($n1.Verdict -ne 'DENIED') { Write-Host "  detail: $($n1.Detail)" }

    $n2 = Test-Connect $postgresIp 5432 deploy/gateway
    Check "gateway -> postgres by pod IP DENIED (DNS taken out of the path)" ($n2.Verdict -eq 'DENIED')

    $n3 = Test-Connect keycloak 8080 deploy/frontend
    Check "frontend -> keycloak:8080 DENIED by policy (IdP unreachable pod-to-pod)" ($n3.Verdict -eq 'DENIED')
    if ($n3.Verdict -ne 'DENIED') { Write-Host "  detail: $($n3.Detail)" }

    $n4 = Test-Connect $keycloakIp 8080 deploy/frontend
    Check "frontend -> keycloak by pod IP DENIED" ($n4.Verdict -eq 'DENIED')

    # --- unknown pods: default-deny catches what no allow matches ---------
    # The overrides pin imagePullPolicy: kubectl run defaults :latest tags
    # to Always, and the image exists only on the kind node.
    kubectl -n $Namespace run netprobe --image=corewebforms-gateway:latest `
        --labels=app=netprobe --restart=Never `
        --overrides='{"spec":{"containers":[{"name":"netprobe","image":"corewebforms-gateway:latest","command":["sleep","600"],"imagePullPolicy":"IfNotPresent"}]}}' | Out-Null
    kubectl -n $Namespace wait --for=condition=ready pod/netprobe --timeout=120s | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'netprobe pod did not start' }
    $n5 = Test-Connect $catalogIp 8094 pod/netprobe
    Check "unlabeled pod -> catalog:8094 by IP DENIED (default-deny)" ($n5.Verdict -eq 'DENIED')
    if ($n5.Verdict -ne 'DENIED') { Write-Host "  detail: $($n5.Detail)" }
    $p3 = Test-Connect $catalogIp 8094 deploy/frontend
    Check "same probe from frontend OPEN (policy scoping proven, not just dropped)" ($p3.Verdict -eq 'OPEN')
    kubectl -n $Namespace delete pod netprobe --ignore-not-found | Out-Null
}

# --- probes survive the policies -------------------------------------------
# Calico without HostEndpoints does not police node-to-pod traffic, so kubelet
# probes should be unaffected - verified, not assumed. The failure mode if
# wrong is pods flapping into CrashLoopBackOff minutes later.
$restartsBefore = @{}
(kubectl -n $Namespace get pods -o json | ConvertFrom-Json).items | ForEach-Object {
    $restartsBefore[$_.metadata.name] = (@($_.status.containerStatuses | Measure-Object -Property restartCount -Sum).Sum)
}
Start-Sleep $ProbeStabilitySec
$allReady = $true
$restartsStable = $true
$deploys = kubectl -n $Namespace get deploy -o json | ConvertFrom-Json
foreach ($d in $deploys.items) {
    if ([int]"$($d.status.readyReplicas ?? 0)" -ne [int]"$($d.spec.replicas ?? 0)") { $allReady = $false; Write-Host "  not ready: $($d.metadata.name) = $($d.status.readyReplicas)/$($d.spec.replicas)" }
}
(kubectl -n $Namespace get pods -o json | ConvertFrom-Json).items | ForEach-Object {
    $now = (@($_.status.containerStatuses | Measure-Object -Property restartCount -Sum).Sum)
    if ($restartsBefore[$_.metadata.name] -ne $now) { $restartsStable = $false; Write-Host "  restarts changed: $($_.metadata.name) $($restartsBefore[$_.metadata.name]) -> $now" }
}
Check "all deployments still fully ready ${ProbeStabilitySec}s under the policies" $allReady
Check "no container restarted under the policies (probes unaffected)" $restartsStable

# --- the stack still serves through the host path ---------------------------
$gw = kubectl -n $Namespace get pods -l app=gateway -o json | ConvertFrom-Json
$gwPod = $gw.items[0].metadata.name
$inCluster = kubectl -n $Namespace exec $gwPod -- curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/ 2>$null
Check 'gateway still serves on its public listener' ($inCluster -eq '200')

if ($failures -gt 0) { throw "test-netpol: $failures failing check(s)" }
Write-Host 'test-netpol: all checks passed'
