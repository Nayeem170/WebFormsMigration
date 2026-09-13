$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

dotnet publish "$root\CoreWebForms\CoreWebForms.csproj" -c Release -o "$root\artifacts\publish\CoreWebForms"
if ($LASTEXITCODE -ne 0) { throw "CoreWebForms publish failed" }

dotnet publish "$root\Microservices\Catalog\Catalog.csproj" -c Release -o "$root\artifacts\publish\Catalog"
if ($LASTEXITCODE -ne 0) { throw "Catalog publish failed" }

Write-Host "Published to artifacts\publish\CoreWebForms and artifacts\publish\Catalog"
