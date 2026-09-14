param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [string]$CatalogUrl = 'http://localhost:8094',
    [string]$OrdersUrl = 'http://localhost:8095'
)

$ErrorActionPreference = 'Stop'
$failures = 0

function Check([string]$name, [bool]$ok) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name"; $script:failures++ }
}

function GetPage([string]$path) {
    for ($i = 1; $i -le 15; $i++) {
        try {
            $r = Invoke-WebRequest ($BaseUrl.TrimEnd('/') + $path) -UseBasicParsing -TimeoutSec 30
            if ($r.StatusCode -eq 200) { return $r.Content }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    throw "GET $path did not return 200"
}

function Req($method, $url, $body) {
    $json = if ($null -ne $body) { $body | ConvertTo-Json -Depth 6 } else { $null }
    $r = Invoke-WebRequest $url -Method $method -Body $json -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
    [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
}

$home_ = GetPage '/'
Check 'home shows Leo Garcia (newest seeded order)' ($home_ -match 'Leo Garcia')
Check 'home shows Karen Novak' ($home_ -match 'Karen Novak')

# The grid pages Id ASC with an active-only filter, so fixed names can drift
# (and an absence can be paging, not filter). Re-derive the expected page 1
# from the API with the page's own rule and require an exact match: a broken
# filter, sort, or page size all fail this comparison.
# Invoke-WebRequest + ConvertFrom-Json, not Invoke-RestMethod: on this
# PowerShell (7.6.6) IRM returns a JSON top-level array as ONE nested
# Object[] element inside @() when run from a script, which member-enumerates
# past Where-Object filters and silently defeats Select-Object -First.
$apiProducts = @(ConvertFrom-Json (Invoke-WebRequest "$CatalogUrl/api/products" -UseBasicParsing).Content)
$expectedNames = @($apiProducts | Where-Object { $_.isActive -and $_.stock -gt 0 } | Sort-Object id | Select-Object -First 5 | ForEach-Object { $_.name })
$expectedTotal = @($apiProducts | Where-Object { $_.isActive -and $_.stock -gt 0 }).Count

$products = GetPage '/Pages/Products/'
$renderedNames = @(
    foreach ($row in [regex]::Matches($products, '(?s)<tr[^>]*>.*?</tr>')) {
        $cells = [regex]::Matches($row.Value, '<td[^>]*>(.*?)</td>')
        if ($cells.Count -ge 2 -and $cells[0].Groups[1].Value.Trim() -match '^\d+$') { $cells[1].Groups[1].Value.Trim() }
    }
)
Check "products grid page 1 matches API-derived active-only page [$($expectedNames -join ', ')]" (($renderedNames -join '|') -eq ($expectedNames -join '|'))
$rowCountClaim = if ($products -match '(\d+) products?\b') { [int]$Matches[1] } else { -1 }
Check "products row count label matches active-only total (claims $rowCountClaim, expected $expectedTotal)" ($rowCountClaim -eq $expectedTotal)

# Seeded names are asserted on home (bounded recent window). The orders list
# grows with every run and paginates, so a fixed seeded name falls off it
# eventually - assert structure here, name-presence where it cannot drift.
$orders = GetPage '/Pages/Orders/'
Check 'orders page renders order rows' (([regex]::Matches($orders, '<tr')).Count -gt 3)

$catalogProduct = Invoke-RestMethod "$CatalogUrl/api/products/1"
$baselineStock = $catalogProduct.Stock
Check "baseline product 1 stock readable ($baselineStock)" ($null -ne $baselineStock)

$order = @{
    customerName  = 'Parity Run'
    customerEmail = 'parity@example.com'
    orderDate     = (Get-Date).ToUniversalTime().ToString('o')
    deliveryDate  = (Get-Date).ToUniversalTime().AddDays(7).ToString('o')
    status        = 'Pending'
    priority      = 'Normal'
    extras        = @()
    items         = @(@{
        productId   = 1
        productName = $catalogProduct.Name
        quantity    = 3
        unitPrice   = $catalogProduct.Price
    })
}
$r = Req Post "$OrdersUrl/api/orders" $order
Check 'place order -> 201' ($r.Status -eq 201)
$orderId = $r.Body.Trim('"')

$ordersAfterPlace = GetPage '/Pages/Orders/'
Check 'orders page shows Parity Run after place' ($ordersAfterPlace -match 'Parity Run')
$stockAfterPlace = (Invoke-RestMethod "$CatalogUrl/api/products/1").Stock
Check "stock after place is baseline - 3 ($baselineStock -> $stockAfterPlace)" ($stockAfterPlace -eq $baselineStock - 3)

$bad = $order.Clone()
$bad.items = @(@{
    productId   = 1
    productName = $catalogProduct.Name
    quantity    = 99999
    unitPrice   = $catalogProduct.Price
})
$r = Req Post "$OrdersUrl/api/orders" $bad
$errorMessage = ($r.Body | ConvertFrom-Json).message
Check 'insufficient -> 409' ($r.Status -eq 409)
Check "insufficient message names the rule ($errorMessage)" ($errorMessage -match 'Insufficient stock for product ID')

$r = Req Delete "$OrdersUrl/api/orders/$orderId"
Check 'delete order -> 204' ($r.Status -eq 204)
$stockAfterDelete = (Invoke-RestMethod "$CatalogUrl/api/products/1").Stock
Check "stock after delete is restored to baseline ($stockAfterDelete)" ($stockAfterDelete -eq $baselineStock)

$ordersAfterDelete = GetPage '/Pages/Orders/'
$deletedRow = [regex]::IsMatch($ordersAfterDelete, 'line-through"[^>]*>\s*<td class="mono">\d+</td>\s*<td>Parity Run</td>')
Check 'orders page shows Parity Run struck through after delete' ($deletedRow)

if ($failures -gt 0) {
    Write-Host "smoke-parity: $failures check(s) failed"
    exit 1
}
Write-Host 'smoke-parity: all checks passed'
