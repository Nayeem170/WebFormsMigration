param(
    [string]$BaseUrl = 'http://localhost:8081',
    [string]$CatalogUrl = 'http://localhost:8094',
    [string]$OrdersUrl = 'http://localhost:8095'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$catalogLog = Join-Path $root 'Microservices\Catalog\App_Data\logs\app.log'
$ordersLog = Join-Path $root 'Microservices\Orders\App_Data\logs\app.log'
$frontendLog = Join-Path $root 'Microservices\Frontend\App_Data\logs\app.log'

$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
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
    Start-Process -FilePath 'dotnet' -ArgumentList (Join-Path $root 'Microservices\Catalog\bin\Debug\net9.0\Catalog.dll') -WorkingDirectory (Join-Path $root 'Microservices\Catalog') -WindowStyle Hidden
    Wait-Healthy $CatalogUrl 60 | Out-Null
}

function Start-Orders() {
    Start-Process -FilePath 'dotnet' -ArgumentList (Join-Path $root 'Microservices\Orders\bin\Debug\net9.0\Orders.dll') -WorkingDirectory (Join-Path $root 'Microservices\Orders') -WindowStyle Hidden
    Wait-Healthy $OrdersUrl 60 | Out-Null
}

function Req($method, $url, $body, $headers = $null) {
    $json = if ($null -ne $body) { $body | ConvertTo-Json -Depth 6 } else { $null }
    Invoke-WebRequest $url -Method $method -Body $json -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60 -Headers $headers
}

if (-not (Wait-Healthy $CatalogUrl 5) -or -not (Wait-Healthy $OrdersUrl 5)) {
    throw 'Catalog and Orders must be running before test-failures.ps1 (Frontend too).'
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
Check 'correlation id reaches Orders log' ((Get-Content $ordersLog -Raw) -match [regex]::Escape($corrId))
Check 'correlation id forwarded to Catalog log' ((Get-Content $catalogLog -Raw) -match [regex]::Escape($corrId))
Req Delete "$OrdersUrl/api/orders/$probeOrderId" | Out-Null

$frontendCorr = $null
Get-Dashboard | Out-Null
Start-Sleep -Seconds 1
$lastCorr = (Select-String -Path $frontendLog -Pattern '\[corr ([0-9a-f]{32})\] HTTP GET / -> 200' | Select-Object -Last 1).Matches[0].Groups[1].Value
Check 'Frontend mints correlation id on page GET' ($null -ne $lastCorr -and $lastCorr.Length -eq 32)
if ($lastCorr) {
    Check 'Frontend correlation id reaches Catalog log' ((Get-Content $catalogLog -Raw) -match [regex]::Escape($lastCorr))
}

Check 'stopped Catalog by port' (Stop-ServicePort 8094)
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$page = Get-Dashboard
$sw.Stop()
Check 'dashboard degrades with Catalog down (message, no trace)' ($page.StatusCode -eq 200 -and $page.Content -match 'Catalog is unavailable right now' -and $page.Content -notmatch 'StackTrace')
Check ('dashboard bounded wall-clock with Catalog down ({0:n1}s < 25s)' -f $sw.Elapsed.TotalSeconds) ($sw.Elapsed.TotalSeconds -lt 25)
$orderOne = $probeOrder.Clone(); $orderOne.items = @(@{ productId = 1; productName = 'Wireless Headphones'; quantity = 1; unitPrice = 79.99 })
$r = Req Post "$OrdersUrl/api/orders" $orderOne
Check 'place order fails 502 while Catalog down' ($r.StatusCode -eq 502 -and $r.Content -match 'CatalogUnavailable')

Start-Catalog
$page = Get-Dashboard
Check 'Frontend recovers after Catalog restart (no Frontend restart)' ($page.StatusCode -eq 200 -and $page.Content -notmatch 'Catalog is unavailable right now')

Check 'stopped Orders by port' (Stop-ServicePort 8095)
$page = Get-Dashboard
Check 'dashboard degrades with Orders down' ($page.StatusCode -eq 200 -and $page.Content -match 'Orders is unavailable right now')
$ordersDown = $false
try { Invoke-WebRequest "$OrdersUrl/api/orders?skip=0&take=1" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { $ordersDown = $true }
Check 'Orders API unreachable while stopped' $ordersDown

Start-Orders
$page = Get-Dashboard
Check 'Frontend recovers after Orders restart' ($page.StatusCode -eq 200 -and $page.Content -notmatch 'Orders is unavailable right now')

if ($failures -gt 0) { throw "test-failures: $failures failing checks" }
Write-Host 'test-failures: all checks passed'
