# CoreWebForms: Web Forms on .NET 9, scaled out

ASP.NET Web Forms 4.8 app migrated to .NET 9 with
[CoreWebForms](https://github.com/corewebforms/corewebforms), then split into
stateless services behind a YARP gateway: 2x frontend, 2x catalog, 2x orders,
Postgres, Redis. Runs on docker compose (default) and kind/k8s with HPA.
The demo runs unauthenticated by owner decision; TLS termination, rate
limiting, and health guards stay on.

## Quickstart (compose)

```bash
pwsh infra/make-cert.ps1                 # dev cert for gateway TLS (once)
docker compose -f compose.yaml -f compose.test-ports.yaml up -d --wait
```

Open `http://localhost:8080`. All published ports are loopback-only by
design; `scripts/check-exposure.ps1` fails the build if that ever changes.
Localdev credentials in compose files are committed on purpose (demo).

Tear down: `docker compose -f compose.yaml -f compose.test-ports.yaml down`.

## Topology

Compose (9 services):

```
browser -> gateway :8080 http / :8443 tls   YARP: round-robin, rate limit
                   :8090 mgmt (health only) 500/10s per IP, XFF hygiene
              | round-robin
   +----------+----------+
   |                     |
frontend x2 :8081   frontend2 :8081    ---- session + DataProtection keys
   |      |                |         ---> redis :6379
   |      +-------+--------+
   |              |
catalog :8094  orders :8095   orders --> catalog (registry lookups)
catalog2 :8094 orders2 :8095
   |              |
   +------+-------+
          v
   postgres :5432   ccw_catalog / ccw_orders, pool 20 per process

   (all four apps + the otel-collector ship traces/metrics via OTLP;
   logs are structured JSON on stdout - docker/kubectl logs is the source)
```

k8s (k8s/*.yaml, kind via `k8s/apply.ps1`): same blocks as Deployments
with HPA (2..4) + PDB for catalog/orders/frontend, gateway at 1, metrics-
server, Calico with default-deny NetworkPolicy, ClusterIP-only services.
Test-ports overlay publishes replica ports 18094-18097 (loopback) so the
suites can probe each replica directly.

## Test suites

| Suite | What it proves | Needs |
|---|---|---|
| `CoreWebForms.UnitTests` (24) | DB error classification (lock vs unique), correlation-id format rule, reserve lock ordering - the pure rules, in-process | nothing |
| `CoreWebForms.CharacterizationTests` (11) | Behavior parity of catalog/orders APIs against the live services | compose stack |
| `scripts/smoke-parity.ps1` (15) | Gateway/page parity, API-derived page content, cross-replica `__webforms/resource` tokens (DataProtection ring) | compose stack |
| `scripts/test-failures.ps1` | Failure injection: per-container pool counts, redis stop, replica kill, error propagation | compose stack |
| `scripts/test-observability.ps1` | One request = one trace across >= 3 services; app meter flows; key-ring parity across replicas; redis kill flips readiness + key-ring monitors and recovers | compose stack |
| `scripts/test-concurrent-reserve.ps1` | Concurrent reserve correctness ladder | stack |
| `k8s/test-k8s.ps1` | k8s bring-up, health-split semantics, per-pod pool counts, correlation propagation | kind cluster |
| `k8s/test-netpol.ps1` (14) | Calico-enforced default-deny with positive controls | kind cluster |
| `check-exposure.ps1` (compose + k8s) | Every published port is loopback; no NodePort/Ingress/hostPort | either |

CI runs the fast tier on every push (.github/workflows/ci.yml) and the
kind/k8s tier nightly (.github/workflows/nightly.yml).

## Migration record

In-place migration phases (docs 01-06): see
[docs/migration/00-index.md](docs/migration/00-index.md).

Scale-out plan 09, phases 0-7 complete: baseline load, key-ring/session
extraction, Postgres split, gateway routing, N-replica scale-out, HPA,
default-deny netpol, and the bind/auth demo cycle (auth removed). Full
execution record with evidence: [docs/migration/09-scaling-and-production-plan.md](docs/migration/09-scaling-and-production-plan.md),
evidence snapshots in [docs/evidence/](docs/evidence/). Phase 8
(observability) and 9 are planned next.

## Layout

```
Microservices/   Catalog, Orders, Frontend, Gateway, Contracts, tests/
scripts/         compose-side suites + publish/run helpers
k8s/             manifests, apply.ps1, k8s suites
infra/           postgres init, calico/metrics-server manifests, make-cert.ps1
load/            k6 scenarios + results (tracked)
docs/migration/  phase-by-phase plans and execution records
docs/evidence/   committed phase evidence snapshots
```

The retired in-process `CoreWebForms/` tree is recoverable from the
`corewebforms-final` tag; the legacy 4.8 project lives on
`feature/legacy-inventory`.
