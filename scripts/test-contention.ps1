param(
    [string]$DbPath = "$PSScriptRoot\..\App_Data\contention.db",
    [int]$Iterations = 15,
    [int[]]$Ports = @(8096, 8097)
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dll = "$repo\Microservices\Catalog\bin\Debug\net9.0\Catalog.dll"

dotnet build "$repo\Microservices\Catalog\Catalog.csproj" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Catalog build failed" }
if (!(Test-Path $dll)) { throw "Catalog dll not found at $dll" }

Remove-Item -Force "$DbPath*" -ErrorAction SilentlyContinue

$procs = @()
$out = "$env:TEMP\catalog-contention"
New-Item -ItemType Directory -Force $out | Out-Null

try {
    foreach ($port in $Ports) {
        $env:Urls = "http://localhost:$port"
        $env:Database__Path = $DbPath
        $procs += Start-Process -FilePath dotnet -ArgumentList "`"$dll`"" -PassThru -WindowStyle Hidden `
            -RedirectStandardOutput "$out\catalog-$port.log" -RedirectStandardError "$out\catalog-$port.err"

        $ready = $false
        for ($i = 0; $i -lt 120 -and -not $ready; $i++) {
            try { Invoke-RestMethod "http://localhost:$port/health" -TimeoutSec 2 | Out-Null; $ready = $true } catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $ready) { throw "Catalog on $port did not become healthy" }
    }

    $hammer = {
        param($Port, $Count)
        $failures = @()
        for ($i = 0; $i -lt $Count; $i++) {
            try {
                $p = Invoke-RestMethod "http://localhost:$Port/api/products/1" -TimeoutSec 10
                $p.stock = 42
                Invoke-RestMethod -Method Put "http://localhost:$Port/api/products/1" -Body ($p | ConvertTo-Json -Depth 4) -ContentType 'application/json' -TimeoutSec 10 | Out-Null
            } catch {
                $failures += "iteration $i`: $($_.Exception.Message)"
            }
        }
        return $failures
    }

    $jobs = $Ports | ForEach-Object { Start-Job -ScriptBlock $hammer -ArgumentList $_, $Iterations }
    $all = $jobs | Receive-Job -Wait
    $jobs | Remove-Job

    $failures = @($all)
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "FAIL: $_" }
        throw "Contention test failed with $($failures.Count) failures"
    }

    Write-Host "Contention test passed: $($Iterations * $Ports.Count) concurrent writes across $($Ports.Count) processes, 0 failures"
}
finally {
    foreach ($p in $procs) { try { Stop-Process -Id $p.Id -Force } catch { } }
    Remove-Item -Force "$DbPath*" -ErrorAction SilentlyContinue
}
