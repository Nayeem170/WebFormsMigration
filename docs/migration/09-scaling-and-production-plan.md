# Scaling and Production Plan for the Inventory Stack

Status: proposed. Not started. This plan picks up where
[08-microservices-migration.md](08-microservices-migration.md) ended: three
services, two SQLite files, one UI tree, loopback only, no auth, every process
started by hand. It answers three questions that the local plan deliberately
deferred:

1. Can instance count grow and shrink automatically under load?
2. How is it decided which instance serves a request?
3. What has to be true in the code before either of those is safe?

The short answers: autoscaling needs an orchestrator (Phase 6), request
assignment happens at a reverse proxy with an explicit policy (Phase 4), and
two state problems block replicas today - per-process session and DataProtection
keys in Frontend, and single-writer SQLite files in Catalog and Orders.

## Principles (inherited from the microservices plan)

- Every phase is demonstrable locally before the next begins.
- No big-bang cutover; each phase has a rollback boundary.
- The existing wire-level verification is the parity net: the 10 HTTP
  characterization facts, the 18-check failure suite, and the smoke suite are
  provider- and topology-agnostic. They run unchanged against Postgres, a
  gateway, or a container stack.
- Nothing is added ahead of a need it demonstrably serves. The deferred list
  from plan 08 (gateway-as-product, outbox, event bus, circuit breakers,
  read-model service) stays deferred unless a phase here justifies it.

## Current state, with the numbers that matter

- Three processes: Frontend (8081, CoreWebForms.Sdk, ASPX runtime compilation,
  ~10-15s cold first request), Catalog (8094), Orders (8095). All bind
  loopback. Manual start via `dotnet run` or the published DLLs.
- Frontend state: `AddDistributedMemoryCache` + `AddSession` with the System.Web
  adapters JSON serializer (one registered key: `CartItems`, a
  `List<OrderItem>`), and `AddDataProtection()` with no shared key ring. Both
  are per-process. A second Frontend instance would see an empty cart and
  could not decrypt what the first instance protected.
- Catalog and Orders state: SQLite in WAL mode at repo-root
  `App_Data/catalog.db` and `App_Data/orders.db`, seeded at startup if empty.
  SQLite WAL permits concurrent readers but a single writer at a time; under
  write load each service serializes on its file. This is the first capacity
  ceiling, and it exists before instance count matters at all.
- Frontend to services: `ServiceHttp` with a 2s timeout, 2-minute connection
  pool lifetime, GET-only bounded retry; single `BaseUrl` per service from
  appsettings (`Services:Products:BaseUrl`, `Services:Orders:BaseUrl`). No
  client-side load balancing, no service discovery.
- Orders to Catalog: `CatalogClient`, 3s timeout, reserve/release with
  executor-side idempotency keys (`ReservationKeys` / `ReleaseKeys` tables).
  This design is already correct under retries and, once the keys table is
  shared storage, correct under multiple Orders replicas too.
- Correlation: `X-Correlation-ID` minted in Frontend, forwarded by Orders,
  logged by all three. Already the right shape for distributed tracing.
- Auth: none. Every product and order endpoint is open to anything that can
  reach the port. Acceptable on loopback only; the plan makes this a hard gate.
- Build: `CoreWebForms.Sdk` 1.0.0 from the dnceng daily feed (see
  `nuget.config`), net9.0 only, `global.json` pinned. Container and CI builds
  must be able to reach that feed.

## Target architecture

```text
                       users
                         |
                    [ TLS, auth, rate limits ]        Phase 7
                         |
                   +-----------+
                   |   YARP    |  round-robin         Phase 4
                   |  gateway  |  over healthy
                   +-----+-----+  backends
              +----------+----------+
              |          |          |            N replicas   Phase 5/6
         +--------+ +--------+ +--------+
         |Frontend| |Frontend| |Frontend|
         +---+----+ +---+----+ +---+----+
             |           |          |
             +-----+-----+----+-----+
                   | (round-robin,
                   |  multi-endpoint)
            +------+------+------+
            |             |      |         N replicas
       +---------+   +---------+ ...
       | Catalog |   | Orders  |
       +----+----+   +----+----+
            |             |
       [ PostgreSQL shared by both, own schemas ]   Phase 2
       [ Redis: session cache + DataProtection keys ] Phase 1
```

Request assignment is decided in exactly two places: the gateway policy for
browser-to-Frontend traffic (round-robin or least-connections, skipping
unhealthy backends), and `ServiceHttp` round-robin across configured endpoints
for Frontend-to-service traffic. Instance count grows and shrinks via
orchestrator rules (Phase 6), and the same health endpoints the orchestrator
uses are what the gateway probes.

## Phases

### Phase 0 - Baseline and capacity measurement

Goal: know what one instance can do before planning for many.

Steps:

1. Pick a load tool (k6 recommended; `bombardier` or `hey` acceptable) and
   write three scenarios: dashboard read, products page read, place-order
   write (with its delete-restore cleanup so stock stays canonical).
2. Record single-instance ceilings: requests/sec and p95 latency per scenario
   for Frontend, Catalog, and Orders separately, and through the full path.
3. Demonstrate the SQLite write ceiling explicitly: concurrent place-order
   load against Orders, watch write serialization and any `database is locked`
   behavior in the logs. This is the number Phase 2 has to beat.
4. Record cold-start time for Frontend (observed ~10-15s ASPX compilation);
  Phase 6 readiness probes must account for it.

Exit criteria: a `load/` directory with the scenarios, a results file with the
baseline numbers, and the write-ceiling observation written down.

Rollback: none needed; measurement only.

### Phase 1 - Shared session and key ring (Redis)

Goal: any Frontend replica can serve any request.

Steps:

1. Add `Microsoft.Extensions.Caching.StackExchangeRedis`; replace
   `AddDistributedMemoryCache` with `AddStackExchangeRedisCache` pointed at a
   configurable connection string (`Session:Redis`), defaulting back to the
   memory cache when unset so local runs without Redis keep working.
2. Persist DataProtection keys to the same Redis
   (`PersistKeysToStackExchangeRedis`) so cookies and protected payloads
   survive instance switches. Set a stable application name.
3. Prove it: run Redis (container or `memurai` on Windows), run two Frontend
   instances on different ports against one Catalog/Orders pair, add an item
   to the cart on instance A, continue the wizard on instance B.

Exit criteria: cart and session survive an instance switch; failure suite and
smoke green on the single-instance configuration.

Rollback: unset `Session:Redis`; the memory cache path is the fallback.

### Phase 2 - PostgreSQL for Catalog and Orders

Goal: multiple writers, real concurrency, managed-service options.

Steps:

1. Add `Npgsql.EntityFrameworkCore.PostgreSQL`; make the provider a
   configuration choice (`Database:Provider` = `sqlite` | `postgres`) with
   separate migration assemblies per provider. Follow the Phase 6 precedent:
   fresh `InitialCreate` per provider rather than trying to subset or rewrite
   the SQLite migration history.
2. Mind the semantic differences called out in advance: string case-sensitivity
   in queries and unique indexes (product names, reservation keys), decimal
   and DateTime mapping, boolean columns, and `DateTime.UtcNow` defaults.
   The reservation and release key tables get real unique constraints.
3. Data copy: extend the `DbSplit` tool pattern with a `DbCopy` mode that
   reads the SQLite files and writes Postgres, using the ProductId resolution
   approach already proven there. Backup both SQLite files first, as in
   Phase 6.
4. Verification: the 10 HTTP characterization facts and the smoke suite run
   unchanged against the Postgres-backed services; the Phase 0 write scenario
   re-run to quantify the concurrency gain.

Exit criteria: all suites green on Postgres; write ceiling measurably raised;
SQLite remains runnable as the local default or is retired explicitly with a
recorded decision.

Rollback: config flip back to the sqlite provider and the untouched files
until the Postgres run is verified; after data diverges, rollback is a data
migration (same boundary shape as Phase 6 of the previous plan - state it).

### Phase 3 - Containers and compose

Goal: the whole stack reproducible with one command, ready for replicas.

Steps:

1. Dockerfiles per service: `mcr.microsoft.com/dotnet/sdk:9.0` build stage
   (needs the dnceng daily feed from `nuget.config` - verify feed access
   inside the build), `aspnet:9.0` runtime stage running the publish output.
   The published-local proof from the previous plan is the evidence this
   works; watch image size (SDK stage keeps the ASPX compiler out of the
   runtime image).
2. `compose.yaml`: postgres, redis, gateway placeholder, 1x frontend,
   1x catalog, 1x orders; health checks on `/health`; configuration by
   environment variables only (URLs, connection strings, base URLs); logs to
   stdout (the file logger stays but is no longer the primary source).
3. Smoke suite and failure suite run against the composed stack with
   `BaseUrl` pointed at the gateway/frontend port.

Exit criteria: `docker compose up` gives a green smoke run on a clean
machine state; no hardcoded localhost anywhere in container configuration.

Rollback: the stack runs the same binaries outside containers; compose is
additive.

### Phase 4 - Request routing: the gateway

Goal: one entry point; request-to-instance assignment becomes an explicit,
observable policy.

Steps:

1. Add YARP as a service (a .NET minimal app, config-as-code, no new
   language). Routes: `/` and `/Pages/*` to the frontend backends.
2. Policy: round-robin (least-connections later if measurements justify it);
   active health checks against Frontend; unhealthy backends skipped
   automatically. This is the direct answer to "which instance gets the
   request": the gateway decides, by policy, from the healthy set.
3. Internal traffic: extend `ServiceHttp` to accept multiple endpoints per
   service (comma-separated `BaseUrl`) with round-robin selection per request
   and an endpoint marked unhealthy on repeated transport failure until the
   pool lifetime refreshes it. Client-side balancing keeps the failure
   domains of Frontend and the services separate and needs no internal
   proxy hop. The alternative (routing internal calls through the gateway)
   is recorded as rejected-for-now: it couples Frontend's availability to a
   second hop with no demonstrated need.
4. Correlation IDs pass through the gateway unchanged (it logs them too).

Exit criteria: browser traffic lands on both frontend replicas in a
two-instance test; killing one backend shifts traffic with no user-visible
failure; failure suite green through the gateway port.

Rollback: point the browser and `BaseUrl` configs back at direct ports; the
gateway is stateless and removable.

### Phase 5 - Scale-out proof: N replicas

Goal: two or more of everything, correctness intact.

Steps:

1. Compose runs 2x frontend, 2x catalog, 2x orders behind the Phase 4
   routing.
2. Correctness checks that only matter with replicas: cart continuity
   across instances (Phase 1 work), reserve replay dedupe with both Orders
   replicas writing to the shared key tables, correlation IDs traceable
   across which-instance-served-what, and the degrade/restart behavior from
   the failure suite applied per-replica (kill one catalog replica: no
   user-visible failure; kill all: the existing degradation, not a hang).
3. Re-run the Phase 0 load scenarios against the scaled stack; compare
   throughput to the single-instance baseline.

Exit criteria: suites green under round-robin; per-replica kill tests behave
as specified; throughput scales measurably.

Rollback: scale counts back to 1; nothing else changes.

### Phase 6 - Autoscaling: grow and shrink on metrics

Goal: instance count follows load without a human.

Steps:

1. Move from compose (static `--scale`) to Kubernetes. Start local:
   `kind` or Docker Desktop's cluster. Deployment per service plus gateway;
   readiness and liveness probes on `/health` with `initialDelaySeconds`
   sized to the Frontend cold start, or better, a startup probe.
2. HPA per service on CPU (start: 60% target) with sensible min/max: min 2
   for frontend (cold-start and availability), min 2 for catalog and orders
   (write availability), max set by the load tests, not optimism.
3. Graceful shutdown: SIGTERM handling so Orders can finish or compensate an
   in-flight reserve before a scale-down event reaps it; termination grace
   period sized to the slowest reserve+insert path (measured, not guessed).
   PodDisruptionBudgets so scale-down never takes the last replica.
4. ConfigMaps and Secrets replace env-file values; the secrets policy from
   the repo's shared conventions applies (nothing in git).

Exit criteria: a load ramp grows replicas and a load drop shrinks them, on
the local cluster, with all suites green at both ends; a scale-down during
place-order traffic produces no lost or double-charged stock.

Rollback: fixed replica counts with the HPA removed; the cluster itself is
disposable locally. Production target choice (managed K8s, App Service with
autoscale rules, or similar) is a decision recorded when there is a real
deployment target - the manifests stay portable until then.

### Phase 7 - Edge security: the exposure gate

Goal: the stack stops being loopback-only, safely. This phase is a
precondition for any non-localhost exposure, exactly as the review of the
previous plan warned.

Steps:

1. Authenticate at the edge: OIDC (any IdP - Entra ID, Keycloak, Auth0) with
   cookie sessions for browser flows at the gateway, and JWT bearer
   validation for the API paths. Services stay on the internal network and
   accept traffic only from the gateway.
2. Authorization: at minimum an authenticated-users policy on write
   endpoints (product create/update/delete, order writes); read paths can
   stay public if the product wants a public catalog - decide and record.
3. TLS termination at the gateway; internal traffic can stay plain HTTP on
   an isolated network, or use mTLS if the platform makes it cheap.
4. Rate limiting: ASP.NET Core rate-limiter middleware at the gateway
   (per-IP and global limits informed by Phase 0 numbers), applied before
   auth-adjacent endpoints.
5. Secrets (IdP client secrets, connection strings) from the orchestrator's
   secret store; never in compose files or git.

Exit criteria: unauthenticated writes rejected; authenticated flows pass the
full smoke; limits demonstrably trip under the load tool; the stack answers
on a non-loopback interface only with all of the above in place.

Rollback: back to loopback bindings; security configuration is additive.

### Phase 8 - Observability

Goal: see which instance did what, and know before users do.

Steps:

1. OpenTelemetry: traces and metrics from all three services plus the
   gateway, with `X-Correlation-ID` carried as the trace attribute so the
   existing three-log walk continues to work.
2. Prometheus + Grafana: request rate, error rate, latency histograms,
   replica counts, HPA decisions, Postgres and Redis health.
3. Alerts: 5xx rate, p95 latency, HPA at max (the "you need more max"
   signal), Postgres connection saturation.

Exit criteria: a single place-order traces across gateway, frontend, orders,
catalog with one identifier; a Grafana dashboard shows a load ramp driving
HPA.

Rollback: none needed; additive.

### Phase 9 - Operations runbook

Goal: day-2 reality written down.

Steps:

1. EF migrations under replicas: run as a pre-deploy Job with a lock (or the
   platform's equivalent) so exactly one replica migrates; deployments wait
   on its success.
2. Backups: replace the SQLite file-copy procedure with `pg_dump` schedules
   or managed PITR; test an actual restore, not the backup file's existence.
3. Deployment pipeline: build containers from the merged branch, run the
   HTTP characterization tests and failure suite against a composed
   environment as CI gates, push images, deploy.
4. Runbook: reseed-equivalent for Postgres, scale overrides, the
   kill-a-replica diagnosis flow, correlation-ID trace walkthrough.

Exit criteria: a restore actually performed; a deploy actually executed end
to end once; the runbook followed by someone who did not write it.

## Risk register

- ASPX runtime compilation in containers: the published-local runs prove the
  binaries execute, but image size, feed access inside builds, and the ~10-15s
  cold start are real. Phase 3 exists to surface this before anything depends
  on it; Phase 6 compensates with startup probes and min replicas.
- Postgres semantic drift: case-sensitive string behavior and unique index
  semantics are the likeliest silent behavior changes; the characterization
  facts and the seed-compare discipline from the previous plan are the net.
- The dnceng daily feed: any outage or package eviction breaks container and
  CI builds. Mirror or vendor the SDK packages once Phase 3 works.
- net9-only SDK pin: the previous plan documented the .NET 10 block; base
  images and the SDK pin must move together when upstream unblocks.
- Autoscale cold-start storms: a load spike plus 15s per new Frontend replica
  can queue users; min replicas and startup probes are the mitigation, and
  the Phase 0 numbers size them.
- Scale-down in the middle of reserve/release: Phase 6's graceful shutdown
  work is the guard; the compensation release path already exists and is
  tested, this extends it to shutdown.

## What stays out, still

Service mesh, multi-region, CDN, outbox, event bus, read-model service,
client-side circuit breakers. Each stays out until a measured need from the
running system says otherwise - the same discipline that held through the
previous plan's eight phases.
