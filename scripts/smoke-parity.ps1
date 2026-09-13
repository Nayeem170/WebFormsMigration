param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [string]$CatalogUrl = 'http://localhost:8094',
    [string]$OrdersUrl = 'http://localhost:8095'
)

$ErrorActionPreference = 'Stop'

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
"GET / -> 200"
"home shows Leo Garcia (newest seeded order): $($(if ($home_ -match 'Leo Garcia') { 'YES' } else { 'NO' }))"
"home shows Karen Novak: $($(if ($home_ -match 'Karen Novak') { 'YES' } else { 'NO' }))"

$products = GetPage '/Pages/Products/'
"GET /Pages/Products/ -> 200"
"products shows Wireless Headphones: $($(if ($products -match 'Wireless Headphones') { 'YES' } else { 'NO' }))"
"products shows Mechanical Keyboard: $($(if ($products -match 'Mechanical Keyboard') { 'YES' } else { 'NO' }))"
"products hides out-of-stock Webcam HD: $($(if ($products -notmatch 'Webcam HD') { 'YES' } else { 'NO' }))"

$orders = GetPage '/Pages/Orders/'
"GET /Pages/Orders/ -> 200"
"orders shows Leo Garcia: $($(if ($orders -match 'Leo Garcia') { 'YES' } else { 'NO' }))"
"orders shows Karen Novak: $($(if ($orders -match 'Karen Novak') { 'YES' } else { 'NO' }))"

$catalogProduct = Invoke-RestMethod "$CatalogUrl/api/products/1"
$baselineStock = $catalogProduct.Stock
"baseline product 1 stock: $baselineStock"

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
"place order -> $($r.Status)"
$orderId = $r.Body.Trim('"')

$ordersAfterPlace = GetPage '/Pages/Orders/'
"orders page shows Parity Run after place: $($(if ($ordersAfterPlace -match 'Parity Run') { 'YES' } else { 'NO' }))"
"stock after place: $((Invoke-RestMethod "$CatalogUrl/api/products/1").Stock)"

$bad = $order.Clone()
$bad.items = @(@{
    productId   = 1
    productName = $catalogProduct.Name
    quantity    = 99999
    unitPrice   = $catalogProduct.Price
})
$r = Req Post "$OrdersUrl/api/orders" $bad
$errorMessage = ($r.Body | ConvertFrom-Json).message
"insufficient -> $($r.Status)"
"insufficient message: $errorMessage"

$r = Req Delete "$OrdersUrl/api/orders/$orderId"
"delete order -> $($r.Status)"
"stock after delete: $((Invoke-RestMethod "$CatalogUrl/api/products/1").Stock)"

$ordersAfterDelete = GetPage '/Pages/Orders/'
$deletedRow = [regex]::IsMatch($ordersAfterDelete, 'line-through"[^>]*>\s*<td class="mono">\d+</td>\s*<td>Parity Run</td>')
"orders page shows Parity Run struck through after delete: $($(if ($deletedRow) { 'YES' } else { 'NO' }))"
