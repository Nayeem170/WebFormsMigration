param(
    [int[]]$Ns = @(8, 16, 32, 64, 128, 256),
    [string]$CatalogUrl = 'http://localhost:8094',
    [string]$OutFile = 'load\concurrent-reserve.md'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outPath = Join-Path $root $OutFile

function Req($method, $url, $body) {
    $json = if ($null -ne $body) { $body | ConvertTo-Json -Depth 6 } else { $null }
    Invoke-WebRequest $url -Method $method -Body $json -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 60
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($n in $Ns) {
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $name = "sweep-$n-$suffix"
    $created = Req Post "$CatalogUrl/api/products" @{ name = $name; category = 'Sweep'; price = 1; stock = $n; isActive = $true }
    if ($created.StatusCode -ne 201) { throw "product create failed: $($created.StatusCode)" }
    $id = $created.Content.Trim('"')
    $runId = [guid]::NewGuid().ToString('N')

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $results = 1..$n | ForEach-Object -Parallel {
        $body = @{
            reservationKey = "sweep-$using:runId-$_"
            items          = @(@{ productId = [int]$using:id; quantity = 1 })
        } | ConvertTo-Json -Depth 6
        try {
            $r = Invoke-WebRequest "$using:CatalogUrl/api/products/reserve" -Method Post -Body $body -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 90
            "$($r.StatusCode)"
        } catch { 'EX' }
    } -ThrottleLimit $n
    $sw.Stop()

    $successes = @($results | Where-Object { $_ -eq '200' }).Count
    $other = ($results | Group-Object | Where-Object Name -ne '200' | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ' '
    $product = Invoke-RestMethod "$CatalogUrl/api/products/$id"
    $stock = $product.stock
    $invariant = ($stock -ge 0) -and ($stock -eq ($n - $successes))
    $goal = ($successes -eq $n)

    $row = [pscustomobject]@{
        N          = $n
        Successes  = $successes
        Other      = if ($other) { $other } else { '-' }
        StockAfter = $stock
        Invariant  = if ($invariant) { 'PASS' } else { 'FAIL' }
        Goal       = if ($goal) { 'PASS' } else { 'FAIL' }
        WallMs     = [int]$sw.ElapsedMilliseconds
    }
    $rows.Add($row)
    Write-Host ("N={0,-4} successes={1,-4} other=[{2,-12}] stock={3,-4} invariant={4} goal={5} wall={6}ms" -f $row.N, $row.Successes, $row.Other, $row.StockAfter, $row.Invariant, $row.Goal, $row.WallMs)

    Req Delete "$CatalogUrl/api/products/$id" | Out-Null
}

$header = @(
    '| N | successes | other statuses | stock after | invariant | goal | wall ms |',
    '|---|---|---|---|---|---|---|'
)
$lines = $rows | ForEach-Object { "| $($_.N) | $($_.Successes) | $($_.Other) | $($_.StockAfter) | $($_.Invariant) | $($_.Goal) | $($_.WallMs) |" }
($header + $lines) | Set-Content $outPath
Write-Host "wrote $OutFile"
