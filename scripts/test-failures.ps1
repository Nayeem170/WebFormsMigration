param(
[string]$BaseUrl = 'http://127.0.0.1:8081',
[string]$CatalogUrl = 'http://127.0.0.1:8094',
[string]$OrdersUrl = 'http://127.0.0.1:8095',
[string]$CatalogUrl2 = '',
[string]$OrdersUrl2 = '',
[switch]$RedisMode,
[string]$RedisStopCommand = '',
[string]$RedisStartCommand = '',
[ValidateSet('local', 'compose')]
[string]$Topology = 'local',
[switch]$PgMode
)
if ($Topology -eq 'compose') {
    if (-not $PSBoundParameters.ContainsKey('BaseUrl')) { $BaseUrl = 'http://127.0.0.1:8080' }
    # Direct service ports only exist under the test-ports overlay
    # (compose.yaml + compose.test-ports.yaml); the documented suite-run shape.
    if (-not $PSBoundParameters.ContainsKey('CatalogUrl')) { $CatalogUrl = 'http://127.0.0.1:18094' }
    if (-not $PSBoundParameters.ContainsKey('OrdersUrl')) { $OrdersUrl = 'http://127.0.0.1:18095' }
    if (-not $CatalogUrl2) { $CatalogUrl2 = 'http://127.0.0.1:18096' }
    if (-not $OrdersUrl2) { $OrdersUrl2 = 'http://127.0.0.1:18097' }
    if (-not $RedisStopCommand) { $RedisStopCommand = 'docker compose stop redis' }
    if (-not $RedisStartCommand) { $RedisStartCommand = 'docker compose start redis' }
}

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
# File sinks are gone (Phase 8a: structured JSON stdout only). In local
# topology the suite spawns the services, so it captures their stdout to
# files; compose topology reads `docker compose logs`.
$catalogLog = Join-Path $root 'Microservices\Catalog\App_Data\logs\stdout.log'
$ordersLog = Join-Path $root 'Microservices\Orders\App_Data\logs\stdout.log'
$frontendLog = Join-Path $root 'Microservices\Frontend\App_Data\logs\stdout.log'

$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}

function Get-ServicePoolEntries([string]$service, [string]$poolName) {
    # Log prefixes are KEPT ("<container>  | message") so pool counts are
    # attributed per container; a single noisy replica cannot mask another
    # replica's missing or wrong-count line.
    $pattern = '^(\S+)\s*\|\s+.*' + $poolName + ' endpoint pool: (\d+) endpoint'
    docker compose logs $service 2>$null | Where-Object { $_ -match $pattern } | ForEach-Object {
        [pscustomobject]@{ Container = $Matches[1]; Count = [int]$Matches[2] }
    }
}

function Test-ServicePool([string]$service, [string]$poolName, [int]$expected) {
    # Every RUNNING container of the service (per `compose ps`, not log
    # counting) must have logged the pool with exactly $expected endpoints.
    # Fails closed: unparsable prefixes or a replica that never logged
    # cannot match a ps name.
    $entries = @(Get-ServicePoolEntries $service $poolName)
    $running = @(docker compose ps $service --format json 2>$null | ConvertFrom-Json | ForEach-Object { $_.Name })
    if ($running.Count -lt 1) { return $false }
    foreach ($name in $running) {
        $mine = @($entries | Where-Object { $name.EndsWith($_.Container) })
        if ($mine.Count -lt 1 -or @($mine | Where-Object { $_.Count -ne $expected }).Count -gt 0) { return $false }
    }
    return $true
}

function Get-Dashboard() {
    Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
}

function Wait-Healthy([string]$url, [int]$seconds) {
    for ($i = 0; $i -lt ($seconds * 2); $i++) {
        try { Invoke-RestMethod ($url.TrimEnd('/') + '/health') -TimeoutSec 3 | Out-Null; return $true } catch {}
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Get-PortPid([int]$port) {
    (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess
}

function Stop-ServicePort([int]$port) {
    $pid2 = Get-PortPid $port
    if ($pid2) { Stop-Process -Id $pid2 -Force; Start-Sleep -Seconds 1 }
    return [bool](Get-PortPid $port) -eq $false
}

function Start-Catalog() {
    if ($Topology -eq 'compose') {
        docker compose start catalog | Out-Null
        Wait-Healthy $CatalogUrl 90 | Out-Null
        return
    }
    Start-LocalService 'Catalog' $CatalogUrl
}

function Start-Orders() {
    if ($Topology -eq 'compose') {
        docker compose start orders | Out-Null
        Wait-Healthy $OrdersUrl 90 | Out-Null
        return
    }
    Start-LocalService 'Orders' $OrdersUrl
}

function Start-LocalService([string]$name, [string]$healthUrl) {
    $logDir = Join-Path $root "Microservices\$name\App_Data\logs"
    New-Item -ItemType Directory -Force $logDir | Out-Null
    $p = @{
        FilePath = 'dotnet'
        ArgumentList = (Join-Path $root "Microservices\$name\bin\Debug\net9.0\$name.dll")
        WorkingDirectory = (Join-Path $root "Microservices\$name")
        WindowStyle = 'Hidden'
        RedirectStandardOutput = (Join-Path $logDir 'stdout.log')
        RedirectStandardError = (Join-Path $logDir 'stderr.log')
    }
    if ($PgMode) {
        $p.Environment = @{
            Database__Provider = 'postgres'
            Database__ConnectionString = "Host=127.0.0.1;Port=15432;Database=ccw_$($name.ToLower());Username=ccw_app;Password=localdev"
            Database__Migrate = 'false'
        }
    }
    Start-Process @p
    Wait-Healthy $healthUrl 60 | Out-Null
}

function Stop-CatalogSvc() {
    if ($Topology -eq 'compose') { docker compose stop catalog | Out-Null; return $true }
    return Stop-ServicePort 8094
}

function Stop-OrdersSvc() {
    if ($Topology -eq 'compose') { docker compose stop orders | Out-Null; return $true }
    return Stop-ServicePort 8095
}

function Req($method, $url, $body, $headers = $null) {
    $json = if ($null -ne $body) { $body | ConvertTo-Json -Depth 6 } else { $null }
    Invoke-WebRequest $url -Method $method -Body $json -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers $headers
}

if (-not (Wait-Healthy $CatalogUrl 5) -or -not (Wait-Healthy $OrdersUrl 5)) {
    throw 'Catalog and Orders must be running before test-failures.ps1 (Frontend too).'
}
if ($Topology -eq 'compose') {
    if (-not (Wait-Healthy $CatalogUrl2 15) -or -not (Wait-Healthy $OrdersUrl2 15)) {
        throw 'Second Catalog/Orders replicas must be running in compose topology (catalog2, orders2).'
    }
    # Phase 7: the gateway's public port no longer serves /health (recon
    # oracle; management port only), so readiness is the dashboard itself.
    $gwUp = $false
    for ($i = 0; $i -lt 60; $i++) {
        try { if ((Invoke-WebRequest $BaseUrl -UseBasicParsing -TimeoutSec 3).StatusCode -eq 200) { $gwUp = $true; break } } catch {}
        Start-Sleep -Milliseconds 500
    }
    if (-not $gwUp) {
        throw 'Gateway must be running before test-failures.ps1 in compose topology.'
    }
}
Get-Dashboard | Out-Null

$order = @{
    customerName  = 'Failure Probe'
    customerEmail = 'probe@example.com'
    orderDate     = (Get-Date).ToUniversalTime().ToString('o')
    deliveryDate  = (Get-Date).ToUniversalTime().AddDays(7).ToString('o')
    status        = 'Pending'
    priority      = 'Normal'
    extras        = @()
    items         = @()
}

$r = Req Post "$OrdersUrl/api/orders" $order
Check 'invalid order payload -> 400' ($r.StatusCode -eq 400 -and $r.Content -match 'at least one item')

$badProduct = @{ name = 'Probe'; category = 'Electronics'; price = -1; stock = 1; isActive = $true }
$r = Req Post "$CatalogUrl/api/products" $badProduct
Check 'invalid product payload -> 400' ($r.StatusCode -eq 400)

$probeRun = [guid]::NewGuid().ToString('N')
$before = (Invoke-RestMethod "$CatalogUrl/api/products/1").stock
$reserveBody = @{ reservationKey = "failurescript-reserve-$probeRun"; items = @(@{ productId = 1; quantity = 1 }) }
$r1 = Req Post "$CatalogUrl/api/products/reserve" $reserveBody
$r2 = Req Post "$CatalogUrl/api/products/reserve" $reserveBody
$after = (Invoke-RestMethod "$CatalogUrl/api/products/1").stock
Check 'reserve replay is idempotent' ($r1.StatusCode -eq 200 -and $r2.Content -match '"replayed":true' -and ($before - $after) -eq 1)
$releaseBody = @{ releaseKey = "failurescript-release-$probeRun"; items = @(@{ productId = 1; quantity = 1 }) }
Req Post "$CatalogUrl/api/products/release" $releaseBody | Out-Null
Check 'probe stock restored' ((Invoke-RestMethod "$CatalogUrl/api/products/1").stock -eq $before)

$p1 = Req Post "$CatalogUrl/api/products" @{ name = 'Failure Probe A'; category = 'Electronics'; price = 1; stock = 1; isActive = $true }
$p2 = Req Post "$CatalogUrl/api/products" @{ name = 'Failure Probe B'; category = 'Electronics'; price = 1; stock = 1; isActive = $true }
$id1 = $p1.Content.Trim('"'); $id2 = $p2.Content.Trim('"')
Check 'repeated product create is not idempotent (two rows)' ($id1 -ne $id2)
Req Delete "$CatalogUrl/api/products/$id1" | Out-Null
Req Delete "$CatalogUrl/api/products/$id2" | Out-Null

$corrId = 'failurescript-' + [guid]::NewGuid().ToString('N')
$probeOrder = @{
    customerName  = 'Corr Probe'
    customerEmail = 'corr@example.com'
    orderDate     = (Get-Date).ToUniversalTime().ToString('o')
    deliveryDate  = (Get-Date).ToUniversalTime().AddDays(7).ToString('o')
    status        = 'Pending'
    priority      = 'Normal'
    extras        = @()
    items         = @(@{ productId = 3; productName = 'USB-C Hub'; quantity = 1; unitPrice = 39.99 })
}
$r = Req Post "$OrdersUrl/api/orders" $probeOrder @{ 'X-Correlation-ID' = $corrId }
$probeOrderId = $r.Content.Trim('"')
Start-Sleep -Seconds 1
$echoedOrders = ($r.Headers['X-Correlation-ID'] | Select-Object -First 1)
Check 'correlation id echoed by Orders response header' ($echoedOrders -eq $corrId)
$r = Req Get "$CatalogUrl/api/products/1" $null @{ 'X-Correlation-ID' = $corrId }
$echoedCatalog = ($r.Headers['X-Correlation-ID'] | Select-Object -First 1)
Check 'correlation id echoed by Catalog response header' ($echoedCatalog -eq $corrId)
Req Delete "$OrdersUrl/api/orders/$probeOrderId" | Out-Null

function Read-ServiceLog([string]$logPath, [string]$service) {
    # Aggregated across replicas ON PURPOSE (--no-log-prefix): the correlation
    # checks only need "some replica logged this id", and with 2x services the
    # request may land on either replica, so the twin's logs are appended.
    # Per-replica attribution lives in Get-ServicePoolEntries, which keeps
    # prefixes.
    if ($Topology -eq 'compose') {
        $text = docker compose logs --no-log-prefix $service 2>$null | Out-String
        $twin = "${service}2"
        if (docker compose ps -q $twin 2>$null) { $text += (docker compose logs --no-log-prefix $twin 2>$null | Out-String) }
        return $text
    }
    return (Get-Content $logPath -Raw)
}

$frontendCorr = $null
$r = Get-Dashboard
Start-Sleep -Seconds 1
$mintedHeader = ($r.Headers['X-Correlation-ID'] | Select-Object -First 1)
Check 'Frontend mints correlation id on page GET' ($null -ne $mintedHeader -and $mintedHeader.Length -eq 32)
if ($mintedHeader) {
    Check 'Frontend correlation id reaches Catalog log' ((Read-ServiceLog $catalogLog 'catalog') -match [regex]::Escape($mintedHeader))
}
Check 'correlation id forwarded to Catalog log' ((Read-ServiceLog $catalogLog 'catalog') -match [regex]::Escape($corrId))

if ($Topology -eq 'compose') {
    # Phase 7: X-Instance is stripped at the gateway egress (pod-name
    # disclosure), so replica balance is attributed from each container's
    # OWN logs across a burst of marked requests (per-container evidence,
    # not response headers).
    $balanceMarker = [guid]::NewGuid().ToString('N')
    foreach ($i in 1..12) {
        Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers @{ 'X-Correlation-ID' = $balanceMarker } | Out-Null
    }
    $serving = @()
    foreach ($svc in 'frontend', 'frontend2') {
        if ((docker compose logs $svc 2>$null | Select-String ([regex]::Escape($balanceMarker))).Count -gt 0) { $serving += $svc }
    }
    Check 'browser traffic lands on both frontend replicas' ($serving.Count -eq 2)

    # Phase 7: the gateway validates inbound correlation ids (32 hex) and
    # replaces non-conforming ones - a client-settable logged field must
    # not pass unvalidated. Both directions asserted.
    $gwValid = [guid]::NewGuid().ToString('N')
    $r = Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers @{ 'X-Correlation-ID' = $gwValid }
    Check 'gateway passes valid correlation id through unchanged' (($r.Headers['X-Correlation-ID'] | Select-Object -First 1) -eq $gwValid)
    $gwJunk = 'gwprobe-' + [guid]::NewGuid().ToString('N')
    $r = Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers @{ 'X-Correlation-ID' = $gwJunk }
    $replaced = ($r.Headers['X-Correlation-ID'] | Select-Object -First 1)
    Check 'gateway replaces non-conforming correlation id' ($null -ne $replaced -and $replaced -ne $gwJunk -and $replaced.Length -eq 32)
    $r = Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
    $gwMinted = ($r.Headers['X-Correlation-ID'] | Select-Object -First 1)
    Check 'gateway mints correlation id when absent' ($null -ne $gwMinted -and $gwMinted.Length -eq 32)

    # Pool sizes for the CURRENT compose shape: Phase 5 = 2x catalog, 2x orders.
    # Bump WITH the shape. Attribution is per container (see Test-ServicePool),
    # so a stale replica image logging a different pool than its twin fails.
    $expectedPoolEndpoints = 2

    Check 'frontend logs resolved endpoint pools' ((Test-ServicePool 'frontend' 'Catalog' $expectedPoolEndpoints) -and (Test-ServicePool 'frontend' 'Orders' $expectedPoolEndpoints))
    Check 'orders logs resolved catalog endpoint pool' ((Test-ServicePool 'orders' 'Catalog' $expectedPoolEndpoints) -and (Test-ServicePool 'orders2' 'Catalog' $expectedPoolEndpoints))
}

$orderOne = $probeOrder.Clone(); $orderOne.items = @(@{ productId = 1; productName = 'Wireless Headphones'; quantity = 1; unitPrice = 79.99 })

if ($Topology -eq 'compose') {
    Check 'stopped one Catalog replica' (Stop-CatalogSvc)
    $page = Get-Dashboard
    Check 'dashboard still serves products with one Catalog replica down (pool failover)' ($page.StatusCode -eq 200 -and $page.Content -notmatch 'Catalog is unavailable right now' -and $page.Content -match 'Wireless Headphones')
    $r = Req Post "$OrdersUrl/api/orders" $orderOne
    Check 'place order succeeds via surviving Catalog replica' ($r.StatusCode -eq 201)
    docker compose stop catalog2 | Out-Null
    Check 'stopped second Catalog replica' ($true)
} else {
    Check 'stopped Catalog' (Stop-CatalogSvc)
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$page = Get-Dashboard
$sw.Stop()
Check 'dashboard degrades with Catalog down (message, no trace)' ($page.StatusCode -eq 200 -and $page.Content -match 'Catalog is unavailable right now' -and $page.Content -notmatch 'StackTrace')
Check ('dashboard bounded wall-clock with Catalog down ({0:n1}s < 25s)' -f $sw.Elapsed.TotalSeconds) ($sw.Elapsed.TotalSeconds -lt 25)
$r = Req Post "$OrdersUrl/api/orders" $orderOne
Check 'place order fails 502 while Catalog down' ($r.StatusCode -eq 502 -and $r.Content -match 'CatalogUnavailable')

if ($Topology -eq 'compose') {
    Start-Catalog
    docker compose start catalog2 | Out-Null
    $bothUp = $false
    foreach ($i in 1..30) {
        if ((Wait-Healthy $CatalogUrl 1) -and (Wait-Healthy $CatalogUrl2 1)) { $bothUp = $true; break }
        Start-Sleep 2
    }
    Check 'both Catalog replicas back' $bothUp
} else {
    Start-Catalog
}
$page = Get-Dashboard
Check 'Frontend recovers after Catalog restart (no Frontend restart)' ($page.StatusCode -eq 200 -and $page.Content -notmatch 'Catalog is unavailable right now')

if ($Topology -eq 'compose') {
    # Drift-proof content marker: assert the newest LIVE order (per the API,
    # before the stop) still renders. Fixed seeded names drift out of the
    # recent-orders window as runs accumulate - the Karen Novak lesson.
    $paged = Invoke-RestMethod "$OrdersUrl/api/orders?skip=0&take=1"
    $recentName = if ($paged.items) { @($paged.items)[0].customerName } else { $null }
    Check 'stopped one Orders replica' (Stop-OrdersSvc)
    $page = Get-Dashboard
    Check 'dashboard still serves with one Orders replica down (pool failover)' ($page.StatusCode -eq 200 -and $page.Content -notmatch 'Orders is unavailable right now' -and ($null -eq $recentName -or $page.Content -match [regex]::Escape($recentName)))
    $ordersDown = $false
    try { Invoke-WebRequest "$OrdersUrl/api/orders?skip=0&take=1" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { $ordersDown = $true }
    Check 'Orders replica A unreachable while stopped' $ordersDown
    docker compose stop orders2 | Out-Null
    Check 'stopped second Orders replica' ($true)
} else {
    Check 'stopped Orders' (Stop-OrdersSvc)
}

$page = Get-Dashboard
Check 'dashboard degrades with Orders down' ($page.StatusCode -eq 200 -and $page.Content -match 'Orders is unavailable right now')
$ordersDown = $false
try { Invoke-WebRequest "$OrdersUrl/api/orders?skip=0&take=1" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { $ordersDown = $true }
Check 'Orders API unreachable while stopped' $ordersDown

if ($Topology -eq 'compose') {
    Start-Orders
    docker compose start orders2 | Out-Null
    $bothUp = $false
    foreach ($i in 1..30) {
        if ((Wait-Healthy $OrdersUrl 1) -and (Wait-Healthy $OrdersUrl2 1)) { $bothUp = $true; break }
        Start-Sleep 2
    }
    Check 'both Orders replicas back' $bothUp
} else {
    Start-Orders
}
$page = Get-Dashboard
Check 'Frontend recovers after Orders restart' ($page.StatusCode -eq 200 -and $page.Content -notmatch 'Orders is unavailable right now')

if ($Topology -eq 'compose') {
    # Cross-replica double-delete: soft-delete + release on replica A, then the
    # same DELETE against replica B must 404 (already deleted) and must NOT
    # release stock a second time. Both replicas share the Orders database.
    $before = (Invoke-RestMethod "$CatalogUrl/api/products/3").stock
    $orderDel = $probeOrder.Clone(); $orderDel.items = @(@{ productId = 3; productName = 'USB-C Hub'; quantity = 1; unitPrice = 39.99 })
    $r = Req Post "$OrdersUrl/api/orders" $orderDel
    $delId = $r.Content.Trim('"')
    $placed = (Invoke-RestMethod "$CatalogUrl/api/products/3").stock
    Check 'double-delete setup: order placed via replica A' ($r.StatusCode -eq 201 -and $placed -eq $before - 1)
    $r = Req Delete "$OrdersUrl/api/orders/$delId"
    $afterFirst = (Invoke-RestMethod "$CatalogUrl/api/products/3").stock
    Check 'delete on replica A -> 204, stock released once' ($r.StatusCode -eq 204 -and $afterFirst -eq $before)
    $r = Req Delete "$OrdersUrl2/api/orders/$delId"
    $afterSecond = (Invoke-RestMethod "$CatalogUrl/api/products/3").stock
    Check 'delete on replica B -> 404, no double release' ($r.StatusCode -eq 404 -and $afterSecond -eq $before)
}

if ($RedisMode) {
    Invoke-Expression $RedisStopCommand | Out-Null
    Start-Sleep 3
    if ($Topology -eq 'compose') {
        $gateway503 = $false
        foreach ($i in 1..15) {
            $r = Get-Dashboard
            if ($r.StatusCode -eq 503) { $gateway503 = $true; break }
            Start-Sleep 2
        }
        Check 'gateway returns 503 while Redis down (backends report unready)' $gateway503
    }
    else {
        $paged = Invoke-RestMethod "$OrdersUrl/api/orders?skip=0&take=1"
        $recentName = if ($paged.items) { @($paged.items)[0].customerName } else { $null }
        $page = Get-Dashboard
        Check 'dashboard survives Redis down (content intact)' ($page.StatusCode -eq 200 -and ($null -eq $recentName -or $page.Content -match [regex]::Escape($recentName)))
        $products = Invoke-WebRequest ($BaseUrl + '/Pages/Products/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
        Check 'products page survives Redis down' ($products.StatusCode -eq 200 -and $products.Content -match 'Wireless Headphones')
        $ordersPage = Invoke-WebRequest ($BaseUrl + '/Pages/Orders/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
        Check 'orders page degrades with Redis down (message, no trace)' ($ordersPage.StatusCode -eq 200 -and $ordersPage.Content -match 'Session store is unavailable right now' -and $ordersPage.Content -notmatch 'StackTrace')
    }
    Invoke-Expression $RedisStartCommand | Out-Null
    if ($Topology -eq 'compose') {
        $recovered = $false
        foreach ($i in 1..45) {
            $page = Get-Dashboard
            if ($page.StatusCode -eq 200) { $recovered = $true; break }
            Start-Sleep 2
        }
        Check 'gateway serves 200 again after Redis restart' $recovered
    }
    else {
        $recovered = $false
        foreach ($i in 1..20) {
            $o = Invoke-WebRequest ($BaseUrl + '/Pages/Orders/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
            if ($o.Content -notmatch 'Session store is unavailable') { $recovered = $true; break }
            Start-Sleep 1
        }
        Check 'orders page recovers after Redis restart' $recovered
    }
}

if ($Topology -eq 'compose') {
    # Phase 7: X-Instance is stripped at the gateway egress, so failover
    # attribution uses marked bursts + per-container log grep.
    function Test-MarkerServed([string]$service, [string]$marker) {
        ((docker compose logs $service 2>$null | Select-String ([regex]::Escape($marker))).Count -gt 0)
    }
    $hostByService = @{}
    foreach ($svc in 'frontend', 'frontend2') {
        $hostName = (docker compose exec -T $svc printenv HOSTNAME 2>$null | Out-String).Trim()
        if ($hostName) { $hostByService[$svc] = $hostName }
    }
    if ($hostByService.Count -eq 2) {
        $preMarker = [guid]::NewGuid().ToString('N')
        foreach ($i in 1..12) {
            Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers @{ 'X-Correlation-ID' = $preMarker } | Out-Null
        }
        $victimSeen = (Test-MarkerServed 'frontend2' $preMarker) -and (Test-MarkerServed 'frontend' $preMarker)
        if ($victimSeen) {
            docker compose stop frontend2 | Out-Null
            Start-Sleep 10
            $shifted = $false
            foreach ($i in 1..15) {
                $postMarker = [guid]::NewGuid().ToString('N')
                $allOk = $true
                foreach ($j in 1..6) {
                    $r = Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers @{ 'X-Correlation-ID' = $postMarker }
                    if ($r.StatusCode -ne 200) { $allOk = $false; break }
                }
                if ($allOk -and (Test-MarkerServed 'frontend' $postMarker) -and -not (Test-MarkerServed 'frontend2' $postMarker)) { $shifted = $true; break }
                Start-Sleep 2
            }
            Check 'killing one backend shifts traffic to the survivor (200s, survivor only)' $shifted
            docker compose start frontend2 | Out-Null
            $deadline2 = (Get-Date).AddSeconds(120)
            $bothAgain = $false
            while ((Get-Date) -lt $deadline2 -and -not $bothAgain) {
                $rejoinMarker = [guid]::NewGuid().ToString('N')
                foreach ($i in 1..12) {
                    Invoke-WebRequest ($BaseUrl + '/') -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers @{ 'X-Correlation-ID' = $rejoinMarker } | Out-Null
                }
                if ((Test-MarkerServed 'frontend2' $rejoinMarker) -and (Test-MarkerServed 'frontend' $rejoinMarker)) { $bothAgain = $true; break }
                Start-Sleep 5
            }
            Check 'restarted backend rejoins rotation' $bothAgain
        }
        else {
            Check 'pre-kill positive control: gateway routes to frontend2' $false
        }
    }
    else {
        Check 'frontend hostnames resolvable via compose exec' $false
    }
}

if ($failures -gt 0) { throw "test-failures: $failures failing checks" }
Write-Host 'test-failures: all checks passed'
