param(
    [int[]]$Ns = @(8, 16, 32, 64, 128, 256),
    [string]$CatalogUrl = 'http://localhost:8094',
    [string]$CatalogUrl2 = '',
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
$diversityFailed = $false
foreach ($n in $Ns) {
    $suffix = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $name = "sweep-$n-$suffix"
    $createUrl = if ($CatalogUrl2 -and ($n % 2 -eq 0)) { $CatalogUrl2 } else { $CatalogUrl }
    $created = Req Post "$createUrl/api/products" @{ name = $name; category = 'Sweep'; price = 1; stock = $n; isActive = $true }
    if ($created.StatusCode -ne 201) { throw "product create failed: $($created.StatusCode) body=[$($created.Content)]" }
    $id = $created.Content.Trim('"')
    $runId = [guid]::NewGuid().ToString('N')

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $results = 1..$n | ForEach-Object -Parallel {
        # Alternate replicas per request: even-indexed reserves hit CatalogUrl,
        # odd hit CatalogUrl2, so one rung exercises BOTH processes against the
        # shared row - the genuinely multi-instance oversell test.
        $url = if ($using:CatalogUrl2 -and ($_ % 2 -eq 1)) { $using:CatalogUrl2 } else { $using:CatalogUrl }
        $body = @{
            reservationKey = "sweep-$using:runId-$_"
            items          = @(@{ productId = [int]$using:id; quantity = 1 })
        } | ConvertTo-Json -Depth 6
        try {
            $r = Invoke-WebRequest "$url/api/products/reserve" -Method Post -Body $body -ContentType 'application/json' -UseBasicParsing -SkipHttpErrorCheck -TimeoutSec 90
            "$($r.StatusCode)|$($r.Headers['X-Instance'])"
        } catch { 'EX|' }
    } -ThrottleLimit $n
    $sw.Stop()

    $successes = @($results | Where-Object { $_ -like '200|*' }).Count
    $instances = @($results | ForEach-Object { ($_ -split '\|', 2)[1] } | Where-Object { $_ } | Select-Object -Unique)
    if ($CatalogUrl2 -and $instances.Count -lt 2) { $diversityFailed = $true }
    $other = ($results | ForEach-Object { ($_ -split '\|', 2)[0] } | Group-Object | Where-Object Name -ne '200' | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ' '
    $checkUrl = if ($CatalogUrl2 -and ($n % 2 -eq 0)) { $CatalogUrl2 } else { $CatalogUrl }
    $product = Invoke-RestMethod "$checkUrl/api/products/$id"
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
        Instances  = $instances.Count
        WallMs     = [int]$sw.ElapsedMilliseconds
    }
    $rows.Add($row)
    Write-Host ("N={0,-4} successes={1,-4} other=[{2,-12}] stock={3,-4} invariant={4} goal={5} instances={6} wall={7}ms" -f $row.N, $row.Successes, $row.Other, $row.StockAfter, $row.Invariant, $row.Goal, $row.Instances, $row.WallMs)

    # Cleanup must not fail silently: a delete that 5xx's leaves a live
    # sweep product in the catalog, which drifts the products grid and the
    # throughput read scenarios. One retry, then loud failure.
    $deleted = $false
    foreach ($attempt in 1..2) {
        $del = Req Delete "$checkUrl/api/products/$id"
        if ($del.StatusCode -eq 204) { $deleted = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $deleted) { throw "sweep product cleanup failed for $name (id $id): last status $($del.StatusCode) body=[$($del.Content)]" }
}

if ($diversityFailed) {
    throw 'replica diversity failed: some rung did not observe 2 distinct catalog instances (X-Instance) - requests are not actually spanning replicas.'
}

$header = @(
    '| N | successes | other statuses | stock after | invariant | goal | instances | wall ms |',
    '|---|---|---|---|---|---|---|---|'
)
$lines = $rows | ForEach-Object { "| $($_.N) | $($_.Successes) | $($_.Other) | $($_.StockAfter) | $($_.Invariant) | $($_.Goal) | $($_.Instances) | $($_.WallMs) |" }
($header + $lines) | Set-Content $outPath
Write-Host "wrote $OutFile"
