param(
    [string]$CatalogUrl = 'http://localhost:8096',
    [string]$OrdersUrl = 'http://localhost:8097',
    [int]$OrderCount = 12,
    [int]$EditCount = 12
)

$ErrorActionPreference = 'Stop'

function GetProduct([int]$id) {
    Invoke-RestMethod "$CatalogUrl/api/products/$id"
}

function NewOrderBody($product, [int]$quantity) {
    @{
        customerName  = 'Contention Test'
        customerEmail = 'contention@test.local'
        orderDate     = (Get-Date).ToUniversalTime().ToString('o')
        deliveryDate  = (Get-Date).ToUniversalTime().AddDays(7).ToString('o')
        status        = 'Pending'
        priority      = 'Normal'
        extras        = @()
        items         = @(@{
            productId    = $product.Id
            productName  = $product.Name
            quantity     = $quantity
            unitPrice    = $product.Price
        })
    }
}

$ordered = GetProduct 1
$edited = GetProduct 5
if ($ordered.Stock -lt $OrderCount) { throw "Product 1 stock $($ordered.Stock) too low for $OrderCount orders" }

$stockBefore = $ordered.Stock
$ordersBefore = (Invoke-RestMethod "$OrdersUrl/api/orders?includeDeleted=true&skip=0&take=0").totalCount

$orderResults = 1..$OrderCount | ForEach-Object -Parallel {
    $body = $using:ordered
    $url = $using:OrdersUrl
    $json = @{
        customerName  = 'Contention Test'
        customerEmail = 'contention@test.local'
        orderDate     = (Get-Date).ToUniversalTime().ToString('o')
        deliveryDate  = (Get-Date).ToUniversalTime().AddDays(7).ToString('o')
        status        = 'Pending'
        priority      = 'Normal'
        extras        = @()
        items         = @(@{
            productId    = $body.Id
            productName  = $body.Name
            quantity     = 1
            unitPrice    = $body.Price
        })
    } | ConvertTo-Json -Depth 6
    try {
        $r = Invoke-WebRequest "$url/api/orders" -Method Post -Body $json -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
        [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
    }
    catch {
        [pscustomobject]@{ Status = 0; Body = $_.Exception.Message }
    }
} -ThrottleLimit $OrderCount

$editBody = @{
    name     = $edited.Name
    category = $edited.Category
    price    = $edited.Price
    stock    = $edited.Stock
    isActive = $edited.IsActive
} | ConvertTo-Json

$editResults = 1..$EditCount | ForEach-Object -Parallel {
    $url = $using:CatalogUrl
    $json = $using:editBody
    try {
        $r = Invoke-WebRequest "$url/api/products/5" -Method Put -Body $json -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
        [int]$r.StatusCode
    }
    catch { 0 }
} -ThrottleLimit $EditCount

$placed = @($orderResults | Where-Object { $_.Status -eq 201 })
$rejected = @($orderResults | Where-Object { $_.Status -eq 409 })
$failedOrders = @($orderResults | Where-Object { $_.Status -ne 201 -and $_.Status -ne 409 })
$failedEdits = @($editResults | Where-Object { $_ -ne 204 })

$ordersAfter = (Invoke-RestMethod "$OrdersUrl/api/orders?includeDeleted=true&skip=0&take=0").totalCount
$orderedAfter = GetProduct 1
$editedAfter = GetProduct 5
$expectedStock = $stockBefore - $placed.Count

$ok = $true
if ($failedOrders.Count -gt 0) { $ok = $false; "FAIL: $($failedOrders.Count) order calls returned unexpected status:"; $failedOrders | ForEach-Object { "  $($_.Status) $($_.Body)" } }
if ($failedEdits.Count -gt 0) { $ok = $false; "FAIL: $($failedEdits.Count) edit calls returned unexpected status: $($failedEdits -join ', ')" }
if ($ordersAfter -ne $ordersBefore + $placed.Count) { $ok = $false; "FAIL: order count $ordersAfter != $($ordersBefore + $placed.Count)" }
if ($orderedAfter.Stock -ne $expectedStock) { $ok = $false; "FAIL: stock $($orderedAfter.Stock) != $expectedStock" }
if ($editedAfter.Stock -ne $edited.Stock) { $ok = $false; "FAIL: edited product stock drifted: $($editedAfter.Stock) != $($edited.Stock)" }

"orders placed: $($placed.Count), rejected 409: $($rejected.Count), edits ok: $(@($editResults | Where-Object { $_ -eq 204 }).Count)"
"stock: $stockBefore -> $($orderedAfter.Stock) (expected $expectedStock)"
if ($ok) { "CONTENTION PASS" } else { exit 1 }
