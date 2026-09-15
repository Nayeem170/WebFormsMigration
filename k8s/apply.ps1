# Brings up the kind cluster with the full stack. Local dev credentials are
# parsed from compose.yaml at runtime (the same source compose itself uses);
# no secret literals are committed here.
param(
    [string]$ClusterName = 'corewebforms',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$kind = 'C:\Users\nayee\.config\kilo\bin\kind.exe'

$m = Select-String -Path (Join-Path $root 'compose.yaml') -Pattern 'POSTGRES_PASSWORD:\s*(\S+)'
$pgPassword = $m.Matches[0].Groups[1].Value
if (-not $pgPassword) { throw 'could not parse POSTGRES_PASSWORD from compose.yaml' }

Set-Location $root

if (-not $SkipBuild) {
    docker compose build catalog orders frontend gateway 2>&1 | Select-Object -Last 1
}

$clusters = & $kind get clusters 2>$null
if (-not ($clusters -match $ClusterName)) {
    & $kind create cluster --name $ClusterName 2>&1 | Select-Object -Last 2
}
& $kind export kubeconfig --name $ClusterName --kubeconfig "$env:USERPROFILE\.kube\config" 2>&1 | Out-Null
kubectl config use-context "kind-$ClusterName" | Out-Null

foreach ($image in 'corewebforms-catalog', 'corewebforms-orders', 'corewebforms-frontend', 'corewebforms-gateway') {
    & $kind load docker-image "${image}:latest" --name $ClusterName 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "kind load failed for $image" }
}

kubectl apply -f k8s/00-namespace.yaml | Out-Null

kubectl -n corewebforms delete secret corewebforms-postgres corewebforms-catalog-db corewebforms-orders-db --ignore-not-found | Out-Null
kubectl -n corewebforms create secret generic corewebforms-postgres --from-literal=user=postgres --from-literal="password=$pgPassword" | Out-Null
kubectl -n corewebforms create secret generic corewebforms-catalog-db `
    --from-literal="app=Host=postgres;Port=5432;Database=ccw_catalog;Username=ccw_app;Password=$pgPassword;MaxPoolSize=20" `
    --from-literal="migrator=Host=postgres;Port=5432;Database=ccw_catalog;Username=postgres;Password=$pgPassword" | Out-Null
kubectl -n corewebforms create secret generic corewebforms-orders-db `
    --from-literal="app=Host=postgres;Port=5432;Database=ccw_orders;Username=ccw_app;Password=$pgPassword;MaxPoolSize=20" `
    --from-literal="migrator=Host=postgres;Port=5432;Database=ccw_orders;Username=postgres;Password=$pgPassword" | Out-Null

kubectl -n corewebforms delete configmap postgres-init --ignore-not-found | Out-Null
kubectl -n corewebforms create configmap postgres-init --from-file=infra/postgres-init.sql | Out-Null

kubectl apply -f k8s/ | Out-Null
kubectl -n corewebforms wait --for=condition=ready pod -l app=catalog --timeout=300s
kubectl -n corewebforms wait --for=condition=ready pod -l app=orders --timeout=300s
kubectl -n corewebforms wait --for=condition=ready pod -l app=frontend --timeout=300s
kubectl -n corewebforms wait --for=condition=ready pod -l app=gateway --timeout=300s
kubectl -n corewebforms get pods -o wide
