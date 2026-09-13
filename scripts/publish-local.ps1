$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

dotnet publish "$root\Microservices\Frontend\CoreWebForms.csproj" -c Release -o "$root\artifacts\publish\Frontend"
if ($LASTEXITCODE -ne 0) { throw "Frontend publish failed" }

dotnet publish "$root\Microservices\Catalog\Catalog.csproj" -c Release -o "$root\artifacts\publish\Catalog"
if ($LASTEXITCODE -ne 0) { throw "Catalog publish failed" }

dotnet publish "$root\Microservices\Orders\Orders.csproj" -c Release -o "$root\artifacts\publish\Orders"
if ($LASTEXITCODE -ne 0) { throw "Orders publish failed" }

Write-Host "Published to artifacts\publish\Frontend, Catalog, and Orders"
