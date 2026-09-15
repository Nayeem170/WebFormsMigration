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
    & $kind create cluster --name $ClusterName --config (Join-Path $PSScriptRoot 'kind-config.yaml') 2>&1 | Select-Object -Last 2
}
& $kind export kubeconfig --name $ClusterName --kubeconfig "$env:USERPROFILE\.kube\config" 2>&1 | Out-Null
kubectl config use-context "kind-$ClusterName" | Out-Null

# CNI: kindnet is disabled (kind-config.yaml) because it does not enforce
# NetworkPolicy - Phase 7's default-deny would pass vacuously on it. Calico
# comes from the pinned, locally patched manifest. Until the daemonset
# lands, every node sits NotReady: install it BEFORE anything else and wait
# the readiness out explicitly, or the first stack apply races an unready
# cluster.
kubectl apply -f infra/calico/calico.yaml | Out-Null
kubectl -n kube-system rollout status ds/calico-node --timeout=300s | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'calico-node daemonset did not become ready' }
kubectl wait node --all --for=condition=Ready --timeout=300s | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'nodes did not become Ready after Calico install' }

foreach ($image in 'corewebforms-catalog', 'corewebforms-orders', 'corewebforms-frontend', 'corewebforms-gateway') {
    & $kind load docker-image "${image}:latest" --name $ClusterName 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "kind load failed for $image" }
}

# Namespace FIRST: everything below creates namespaced objects, and on a
# fresh cluster the namespace does not exist yet. (The original order put
# this line after the secret/configmap block, which only ever worked
# because the namespace pre-existed - the creates failed silently into
# Out-Null and the gateway pods stuck on FailedMount.)
kubectl apply -f k8s/00-namespace.yaml | Out-Null

# metrics-server (HPA dependency; Phase 6 installed it by hand). Pinned,
# patched manifest (- --kubelet-insecure-tls) like Calico.
kubectl apply -f infra/metrics-server/components.yaml | Out-Null
kubectl -n kube-system rollout status deploy/metrics-server --timeout=180s | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'metrics-server did not become ready' }

# Gateway TLS cert: created from the local dev cert (infra/make-cert.ps1);
# the password rides in the same secret. Local-only credentials like every
# other secret in this script.
$certPath = Join-Path $root 'infra\certs\gateway.pfx'
if (-not (Test-Path $certPath)) { throw "gateway cert missing: run infra\make-cert.ps1 first" }
kubectl -n corewebforms delete secret gateway-tls --ignore-not-found | Out-Null
kubectl -n corewebforms create secret generic gateway-tls `
    --from-file=gateway.pfx=$certPath --from-literal='password=localdev-cert' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'gateway-tls secret creation failed' }

kubectl -n corewebforms delete secret corewebforms-postgres corewebforms-catalog-db corewebforms-orders-db --ignore-not-found | Out-Null
kubectl -n corewebforms create secret generic corewebforms-postgres --from-literal=user=postgres --from-literal="password=$pgPassword" | Out-Null
kubectl -n corewebforms create secret generic corewebforms-catalog-db `
    --from-literal="app=Host=postgres;Port=5432;Database=ccw_catalog;Username=ccw_app;Password=$pgPassword;MaxPoolSize=20" `
    --from-literal="migrator=Host=postgres;Port=5432;Database=ccw_catalog;Username=ccw_migrator;Password=$pgPassword" | Out-Null
kubectl -n corewebforms create secret generic corewebforms-orders-db `
    --from-literal="app=Host=postgres;Port=5432;Database=ccw_orders;Username=ccw_app;Password=$pgPassword;MaxPoolSize=20" `
    --from-literal="migrator=Host=postgres;Port=5432;Database=ccw_orders;Username=ccw_migrator;Password=$pgPassword" | Out-Null

kubectl -n corewebforms delete configmap postgres-init --ignore-not-found | Out-Null
kubectl -n corewebforms create configmap postgres-init --from-file=infra/postgres-init.sql | Out-Null

kubectl apply -f k8s/ | Out-Null
kubectl -n corewebforms wait --for=condition=ready pod -l app=catalog --timeout=300s
kubectl -n corewebforms wait --for=condition=ready pod -l app=orders --timeout=300s
kubectl -n corewebforms wait --for=condition=ready pod -l app=frontend --timeout=300s
kubectl -n corewebforms wait --for=condition=ready pod -l app=gateway --timeout=300s
kubectl -n corewebforms get pods -o wide
