# Phase 8 observability positive controls (compose topology). Presence is
# not value: a collector that is "up" proves nothing. These checks assert
# the SIGNALS are correct:
#   1. one request produces ONE trace spanning frontend+catalog+orders
#      (W3C propagation through the YARP hop), not merely that traces exist
#   2. the app meter flows through the collector (ccw_readiness,
#      ccw_dataprotection instrument names in the export)
#   3. both frontend replicas report the SAME active key-ring key (the
#      skew signal; smoke-parity's resource rounds stay the regression gate)
#   4. killing redis flips the readiness gauge/log AND trips the key-ring
#      monitor, and both recover - "know before users do", tested
param(
    [string]$BaseUrl = 'http://127.0.0.1:8080'
)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    $suffix = if ($detail) { " ($detail)" } else { '' }
    Write-Host "$(if ($ok) { 'PASS' } else { 'FAIL' }) $name$suffix"
    if (-not $ok) { $script:failures++ }
}

function Get-CollectorLog() {
    (docker compose logs --no-log-prefix otel-collector 2>$null | Out-String)
}

function Get-ServiceLog([string]$service) {
    (docker compose logs --no-log-prefix $service 2>$null | Out-String)
}

# --- control 1: one request, one trace across services ---------------------
$corr = [guid]::NewGuid().ToString('N')
Invoke-WebRequest $BaseUrl -UseBasicParsing -TimeoutSec 30 -Headers @{ 'X-Correlation-ID' = $corr } | Out-Null
Start-Sleep -Seconds 8

$collector = Get-CollectorLog
$traceServices = @{}
$currentTrace = $null
foreach ($line in ($collector -split "`n")) {
    if ($line -match 'Trace ID\s*:?\s*([0-9a-f]{32})') { $currentTrace = $Matches[1] }
    elseif ($line -match 'service\.name:\s*(\S+)') {
        if ($currentTrace) {
            if (-not $traceServices.ContainsKey($currentTrace)) { $traceServices[$currentTrace] = New-Object System.Collections.Generic.HashSet[string] }
            [void]$traceServices[$currentTrace].Add($Matches[1])
        }
    }
}
$cross = @($traceServices.GetEnumerator() | Where-Object { @($_.Value).Count -ge 3 })
$detail = if ($cross.Count -ge 1) { "trace $((($cross | Select-Object -First 1).Key -replace '^(.{8}).*', '$1'))... -> $((($cross | Select-Object -First 1).Value) -join ',')" } else { 'no trace with >= 3 services' }
Check 'one gateway request yields one trace across >= 3 services' ($cross.Count -ge 1) $detail

# --- control 2: the app meter reaches the collector ------------------------
Check 'ccw_readiness metric exported through collector' ($collector -match 'ccw_readiness')
Check 'ccw_dataprotection metrics exported through collector' ($collector -match 'ccw_dataprotection_active_key_fingerprint')

# --- control 3: key-ring parity across replicas ----------------------------
function Get-LastKeyRing([string]$service) {
    $lines = (Get-ServiceLog $service) -split "`n" | Select-String 'keyring: keys='
    if (@($lines).Count -lt 1) { return $null }
    $last = $lines[-1].ToString()
    if ($last -match 'keys=(\d+).*fingerprint=(\d+)') {
        return @{ Keys = [int]$Matches[1]; Fingerprint = [int]$Matches[2] }
    }
    return $null
}
$fe1 = Get-LastKeyRing frontend
$fe2 = Get-LastKeyRing frontend2
$parity = ($null -ne $fe1 -and $null -ne $fe2 -and $fe1.Fingerprint -eq $fe2.Fingerprint)
Check 'both frontend replicas report the same active key-ring key' $parity `
    ("fe1=$(if ($fe1) { $fe1.Fingerprint } else { 'none' }) fe2=$(if ($fe2) { $fe2.Fingerprint } else { 'none' })")

# --- control 4: kill redis, readiness + key-ring react, both recover ------
docker compose stop redis | Out-Null
$deadline = (Get-Date).AddSeconds(60)
$sawNotReady = $false
$sawKeyringError = $false
while ((Get-Date) -lt $deadline -and (-not $sawNotReady -or -not $sawKeyringError)) {
    $logs = (Get-ServiceLog frontend) + (Get-ServiceLog frontend2)
    if ($logs -match 'readiness: False') { $sawNotReady = $true }
    if ($logs -match 'keyring: read failed') { $sawKeyringError = $true }
    Start-Sleep -Seconds 5
}
Check 'redis loss flips readiness monitor (both replicas aggregated)' $sawNotReady
Check 'redis loss trips key-ring monitor' $sawKeyringError

docker compose start redis | Out-Null
$deadline = (Get-Date).AddSeconds(60)
$recovered = $false
while ((Get-Date) -lt $deadline -and -not $recovered) {
    $logs = (Get-ServiceLog frontend) + (Get-ServiceLog frontend2)
    if ($logs -match 'readiness: True') { $recovered = $true }
    Start-Sleep -Seconds 5
}
# Gated on having OBSERVED the flip - recovery without loss is vacuous.
Check 'readiness recovers when redis returns' ($sawNotReady -and $recovered)

if ($failures -gt 0) { throw "observability controls: $failures failing check(s)" }
Write-Host 'observability controls: all checks passed'
