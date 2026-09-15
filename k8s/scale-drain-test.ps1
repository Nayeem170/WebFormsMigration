# Exit criterion: a scale-down during place-order traffic produces no lost
# or double-charged stock.
#
# Drives 60 sequential place-orders (1 unit of a fresh probe product each)
# through the orders Service and deletes a catalog POD mid-run, so its
# SIGTERM drain (ShutdownTimeout 25s, grace 35s) overlaps in-flight
# reserves. Invariants asserted against the DATABASE via psql (authoritative
# and immune to port-forward churn), not via APIs:
#   - every 201 corresponds to exactly one order row
#   - final stock == initial - count(201)   (catches lost AND double charge)
#   - any non-201 is a bounded 502/503, never a 500
#   - catalog replicas stayed at 2 throughout (no HPA confound)
param(
    [string]$OrdersUrl = 'http://127.0.0.1:18095',
    [string]$CatalogUrl = 'http://127.0.0.1:18094',
    [int]$OrderCount = 60,
    [int]$DeleteAtIteration = 20
)
$ErrorActionPreference = 'Stop'
$failures = 0
function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}
function Psql([string]$db, [string]$sql) {
    (kubectl -n corewebforms exec deployment/postgres -- psql -U postgres -d $db -t -A -c $sql 2>$null | Where-Object { $_ -ne '' })
}
# kubectl port-forward pins one backing pod; a pod churn mid-test leaves a
# dead stream that only fails on next use. Re-establish forwards whose
# current probe fails.
function Ensure-Forward([int]$localPort, [string]$service, [int]$svcPort) {
    $ok = $false
    try { Invoke-WebRequest "http://127.0.0.1:$localPort/health" -UseBasicParsing -TimeoutSec 3 | Out-Null; $ok = $true } catch { }
    if ($ok) { return }
    $owner = Get-NetTCPConnection -LocalPort $localPort -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($owner) { Stop-Process -Id $owner.OwningProcess -Force -ErrorAction SilentlyContinue; Start-Sleep 1 }
    $p = Start-Process kubectl -ArgumentList "-n","corewebforms","port-forward","svc/$service","${localPort}:${svcPort}","--address","127.0.0.1" -WindowStyle Hidden -PassThru
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $up = $false
        try { Invoke-WebRequest "http://127.0.0.1:$localPort/health" -UseBasicParsing -TimeoutSec 3 | Out-Null; $up = $true } catch { }
        if ($up) { return }
    }
    throw "port-forward $service->$localPort did not come up"
}
Ensure-Forward 18094 catalog 8094
Ensure-Forward 18095 orders 8095

$replicas = kubectl -n corewebforms get deployment catalog -o jsonpath='{.spec.replicas}'
Check 'catalog at 2 replicas before drain (HPA not already scaling)' ($replicas -eq '2')

$suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
$name = "drain-$suffix"
$created = Invoke-RestMethod -Method Post -Uri "$CatalogUrl/api/products" -Body (ConvertTo-Json @{
    name = $name; category = 'DrainTest'; price = 1; stock = $OrderCount; isActive = $true
} -Depth 5) -ContentType 'application/json'
# The endpoint returns the bare id, not an object.
$productId = if ($created -and $created.PSObject.Properties['id']) { $created.id } else { $created }
Check "probe product created (id $productId, stock $OrderCount)" ($null -ne $productId)

$stockBefore = [int](Psql ccw_catalog ('select "Stock" from "Products" where "Id" = {0}' -f $productId))
Check 'baseline stock matches probe' ($stockBefore -eq $OrderCount)

$statuses = New-Object System.Collections.Generic.List[string]
$deletedPod = $null
for ($i = 1; $i -le $OrderCount; $i++) {
    if ($i -eq $DeleteAtIteration) {
        $deletedPod = (kubectl -n corewebforms get pods -l app=catalog -o name | Select-Object -First 1) -replace '^pod/', ''
        Write-Host "  iteration ${i}: deleting catalog pod $deletedPod (SIGTERM drain)"
        kubectl -n corewebforms delete pod $deletedPod --wait=false | Out-Null
    }
    $body = ConvertTo-Json @{
        customerName = "$name-$i"; customerEmail = 'drain@test.local'
        orderDate = (Get-Date).ToUniversalTime().ToString('o')
        deliveryDate = (Get-Date).ToUniversalTime().AddDays(3).ToString('o')
        status = 'Pending'; priority = 'Normal'; extras = @()
        items = @(@{ productId = $productId; productName = $name; quantity = 1; unitPrice = 1 })
    } -Depth 5
    try {
        $r = Invoke-WebRequest -Method Post -Uri "$OrdersUrl/api/orders" -Body $body -ContentType 'application/json' -UseBasicParsing -TimeoutSec 30
        $statuses.Add([string]$r.StatusCode)
    } catch {
        $code = try { [string]$_.Exception.Response.StatusCode.value__ } catch { 'error' }
        $statuses.Add($code)
    }
}

$created201 = @($statuses | Where-Object { $_ -eq '201' }).Count
$degraded = @($statuses | Where-Object { $_ -notin @('201') })
Write-Host "  results: $created201 x 201, $($degraded.Count) non-201 ($($degraded -join ','))"

Start-Sleep -Seconds 5
$orderRows = [int](Psql ccw_orders ('select count(*) from "Orders" where "CustomerName" like ''{0}-%''' -f $name))
$stockAfter = [int](Psql ccw_catalog ('select "Stock" from "Products" where "Id" = {0}' -f $productId))
$chargedRows = [int](Psql ccw_orders ('select count(*) from "OrderItems" oi join "Orders" o on o."Id" = oi."OrderId" where o."CustomerName" like ''{0}-%'' and oi."Quantity" = 1 and oi."ProductId" = {1}' -f $name, $productId))

Check "every 201 has exactly one order row ($created201 orders, $orderRows rows)" ($orderRows -eq $created201)
Check "every order row charged exactly one item ($chargedRows)" ($chargedRows -eq $orderRows)
Check "final stock is initial minus created orders ($($OrderCount) - $created201 = $($OrderCount - $created201), actual $stockAfter)" ($stockAfter -eq $OrderCount - $created201)
Check 'no unexpected failure statuses (only bounded 502/503 allowed)' (@($degraded | Where-Object { $_ -notin @('502', '503') }).Count -eq 0)
Check 'at least the pre-drain orders all succeeded' (@($statuses | Select-Object -First ($DeleteAtIteration - 1) | Where-Object { $_ -ne '201' }).Count -eq 0)

Start-Sleep -Seconds 45
$replicasAfter = kubectl -n corewebforms get deployment catalog -o jsonpath='{.status.readyReplicas}'
Check 'catalog back at 2 ready replicas after replacement' ($replicasAfter -eq '2')
$restarts = (kubectl -n corewebforms get pods -l app=orders -o json | ConvertFrom-Json).items | ForEach-Object { $_.status.containerStatuses[0].restartCount }
Check 'orders pods never restarted' (@($restarts | Where-Object { $_ -gt 0 }).Count -eq 0)

$cleanupIds = Psql ccw_orders ('select string_agg("Id"::text, '','') from "Orders" where "CustomerName" like ''{0}-%''' -f $name)
if ($cleanupIds) {
    foreach ($id in $cleanupIds -split ',') {
        try { Invoke-WebRequest -Method Delete -Uri "$OrdersUrl/api/orders/$id" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { }
    }
}
try { Invoke-WebRequest -Method Delete -Uri "$CatalogUrl/api/products/$productId" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { }
Write-Host '  cleanup done (orders soft-deleted, probe product soft-deleted)'

if ($failures -gt 0) { throw "scale-drain: $failures failing check(s)" }
Write-Host 'scale-drain: all checks passed'
