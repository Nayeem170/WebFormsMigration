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
- The verification net is real but partial, and the plan says exactly where it
  ends. The 10 HTTP characterization facts and the smoke suite (BaseUrl is a
  parameter) are wire-level: they run unchanged against Postgres, a gateway,
  or a container stack. The 18-check failure suite is host-bound in three
  mechanisms - it kills services by host port, restarts them with local
  dotnet, and asserts correlation by reading fixed log file paths - so it
  follows the stack into containers only after Phase 3 abstracts those
  mechanisms. And all of it is sequential: none of it can see the concurrency
  regressions Phase 2 makes possible. Phase 0 therefore adds a
  concurrent-reserve fact, and Phase 2 does not start until it passes on the
  current stack; without it, Phases 2 through 5 run without a net.
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
  SQLite WAL permits concurrent readers but a single writer at a time. That is
  the first capacity ceiling - and it is also the only thing making reserve
  correct. Catalog's reserve path is a read-check-decrement with no row lock
  (`db.Products.Find`, guard, `Stock -= quantity`); it cannot interleave
  today only because writes serialize on the file. The same applies to the
  idempotency key check, which is check-then-insert (`Find(key) != null`
  before insert). Replacing SQLite without replacing those two shapes trades
  a ceiling for an oversell bug; Phase 2 treats them as the primary work.
- Both services run `Database.Migrate()` and seed-if-empty on every startup
  (Catalog and Orders `Program.cs`). Correct for one instance per database; a
  duplicate-object failure and a double seed for two. Frontend has a
  `Database:Migrate` flag, but its code default is true (`?? true` in
  `Program.cs`) with appsettings.json supplying the false - a config-file
  guarantee, not a code one. Phase 3 moves configuration to environment
  variables only, and a compose service that omits `Database__Migrate` gets
  the racing behavior back. Phase 2 adds the flag to the services with the
  default false in code.
- Health endpoints: Catalog and Orders expose `/health`; Frontend has none,
  though Phases 3, 4, and 6 all depend on one existing and meaning something.
- Frontend opens a browser window on startup when the environment is
  Development (`Process.Start` in `Program.cs`). Fine on a desktop; a startup
  hang in a container. Phase 3 gates it on an explicit setting.
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
       [ PostgreSQL - one instance two schemas, or two databases: ]  Phase 2
       [              Phase 2 decides and records which            ]
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
4. Add the concurrent-reserve fact as two separate assertions, because they
   fail for different reasons. The invariant - no negative stock, no
   oversell, no success beyond available stock, on any provider - is the
   fact that must survive Phase 2 and gates it. The goal - exactly N of N
   parallel reserves succeeding - is provider-dependent: on SQLite it holds
   only while N stays under what the connection's 5-second busy timeout
   absorbs (`Default Timeout=5` in both AppDbContext connection strings),
   and past that point reserves fail with SQLITE_BUSY. A busy failure is a
   recorded baseline characteristic feeding step 3's ceiling number, not a
   red test; reporting it as correctness would make the strongest gate in
   the plan its flakiest. Run both against the current SQLite stack. The
   existing suites are sequential and structurally blind to this class of
   bug - this test is the only thing standing between Phase 2 and a silent
   oversell regression.
5. Record cold-start time for Frontend (observed ~10-15s ASPX compilation);
  Phase 6 readiness probes must account for it.

Exit criteria: a `load/` directory with the scenarios, a results file with the
baseline numbers, the write-ceiling observation, the concurrent-reserve
invariant green on SQLite, and the N-success goal recorded alongside the N
where SQLite's busy timeout starts failing it.

Rollback: none needed; measurement only.

### Phase 1 - Shared session and key ring (Redis)

Goal: any Frontend replica can serve any request.

Steps:

1. Add `Microsoft.Extensions.Caching.StackExchangeRedis`; replace
   `AddDistributedMemoryCache` with `AddStackExchangeRedisCache` pointed at a
   configurable connection string (`Session:Redis`). The memory-cache
   fallback must be loud, not silent: gate it on an explicit opt-in for
   single-instance local runs (a separate `Session:UseMemoryCache` flag) or
   log at error level when it activates. A container missing the Redis
   variable would otherwise run N replicas on per-process session - broken
   carts, nothing in the logs.
2. Persist DataProtection keys to the same Redis
   (`PersistKeysToStackExchangeRedis`) so cookies and protected payloads
   survive instance switches. Set a stable application name.
3. Prove it: run Redis (container or `memurai` on Windows), run two Frontend
   instances on different ports against one Catalog/Orders pair, add an item
   to the cart on instance A, continue the wizard on instance B.
4. Add Frontend `/health`, split in two from the start: liveness means the
   process is up; readiness means the first ASPX compilation has completed,
   verified by self-requesting a real page once (or hooking compile
   completion) - not a static ok. A readiness endpoint that answers 200
   before the ~10-15s compile lets the gateway route to a cold instance,
   which is the cold-start storm from the risk register arriving through the
   health endpoint itself. Compose checks, gateway active health checks, and
   Kubernetes probes all consume this split.

Exit criteria: cart and session survive an instance switch; a form page
rendered by instance A accepts its postback POSTed to instance B (ViewState
and the DataProtection key ring, not just session - this fails today and
passes only when the key ring is genuinely shared); readiness stays false
until first compile on a fresh instance; failure suite and smoke green on the
single-instance configuration.

Rollback: unset `Session:Redis`; the memory cache path is the fallback.

### Phase 2 - PostgreSQL for Catalog and Orders

Goal: multiple writers, real concurrency, managed-service options - without
losing the correctness SQLite's single writer was accidentally providing.

Steps:

1. Concurrency control first, because the unlocked read-check-decrement in
   reserve (`db.Products.Find` -> guard -> `Stock -= quantity`) and the
   check-then-insert on reservation and release keys are safe today only
   under SQLite's serialized writes. Under Postgres READ COMMITTED, two
   concurrent reserves of the same product both read Stock 5, both pass the
   guard, both decrement: oversell. Decide and write down the locking
   approach: `SELECT ... FOR UPDATE` via raw SQL in the reserve and release
   loops, or EF `xmin` concurrency tokens. Not left to default isolation
   levels. Whichever branch is chosen, two guards come with it. First, lock
   ordering: reserve iterates request items in caller order, and the cart
   makes that order user-controlled - two concurrent reserves, one [A, B]
   and one [B, A], each hold their first row and wait on their second until
   Postgres's deadlock detector kills one with 40P01, surfacing as an
   unhandled 500 on a path that today only ever returns 200 or 409. Sort
   items by ProductId before the lock loop, in both reserve and release, so
   every transaction takes locks in the same order; that removes the class
   rather than retrying it. Second, a bounded retry stays as the backstop
   for serialization failures either branch can still raise.
2. Key inserts under contention: with the unique constraints in place, a
   replayed reserve or release that races its first delivery raises a unique
   violation instead of returning `replayed: true`. Catch the unique
   violation on `ReservationKeys` / `ReleaseKeys` inserts and return the
   replay response - the wire fact the characterization suite pins. The
   catch is not replay ergonomics; it is half of what keeps concurrent
   delete safe. Orders' delete is check-then-set on `IsDeleted` - a TOCTOU -
   but two racing deletes send Catalog the same deterministic release key
   (`"order:" + order.Id`), and Catalog increments stock and inserts the key
   in one transaction, so the loser's key violation rolls back its increment
   with it. On SQLite the serialization provides that pairing; on Postgres
   it is the transaction plus this catch. Without the catch the increment is
   still rolled back (no stock inflation) but the wire answer becomes an
   unhandled 500 on a path that returns 200 today - the mirror of the
   oversell guard, and non-negotiable for the same reason.
3. Provider switch: add `Npgsql.EntityFrameworkCore.PostgreSQL`; make the
   provider a configuration choice (`Database:Provider` = `sqlite` |
   `postgres`). This touches the `AppDbContext` constructor and
   `OnConfiguring` in both services - today they are registered with a file
   path, not a connection string (`new AppDbContext(dbPath)`, Catalog and
   Orders `Program.cs`), so the ctor grows a connection-string form. Follow
   the Phase 6 precedent: fresh `InitialCreate` per provider rather than
   trying to subset or rewrite the SQLite migration history. Also decide and
   record the schema topology: one instance with separate schemas per
   service plus separate role grants (so Catalog's credentials cannot read
   Orders tables), or two databases outright. The split files enforced the
   service boundary physically; a shared instance without per-service
   grants makes a cross-service join merely impolite. The choice also sets
   `pg_dump` granularity for Phase 9 backups.
4. Retire the startup migration race while the provider is open: move both
   services to a `Database:Migrate` flag whose code default is false
   (Frontend's pattern, minus its true code default - the false must not
   depend on a config file Phase 3 is about to replace with env vars) plus
   a migrator entrypoint that applies migrations and seeds once, under a
   lock, before replicas exist. The migrator runs under its own role with
   DDL rights, distinct from both service roles - sharing a service
   connection string would give every runtime replica CREATE/DROP and make
   step 3's grants decorative. Service roles get DML on their own schema
   only. Two instances racing `Migrate()` on Postgres
   fail on duplicate objects; racing the seed check double-seeds. This is
   the fix Phase 5 would otherwise discover late.
5. The remaining semantic differences are real but secondary to the above:
   string case-sensitivity in queries and unique indexes (product names,
   reservation keys), decimal and DateTime mapping, boolean columns. One
   non-task, recorded so nobody goes looking for it: the reservation and
   release key tables need no new constraints - their keys are already the
   primary keys (`HasKey(k => k.Key)` in both AppDbContexts, enforced on
   SQLite today). Step 2's violation catch is the only key-related work.
6. Data copy: extend the `DbSplit` tool pattern with a `DbCopy` mode that
   reads the SQLite files and writes Postgres, using the ProductId resolution
   approach already proven there. Backup both SQLite files first, as in
   Phase 6.
7. Verification: the 10 HTTP characterization facts and the smoke suite run
   unchanged against the Postgres-backed services; the concurrent-reserve
   fact from Phase 0 re-run - N parallel reserves of a stock-N product, on
   Postgres, expecting exactly N successes, stock 0, no negatives (with one
   Catalog instance this already interleaves on the thread pool; Phase 5
   re-runs it across replicas); the Phase 0 write scenario re-run to
   quantify the concurrency gain.

Exit criteria: concurrent-reserve fact green on Postgres; replayed-key
responses under a raced replay; all suites green; write ceiling measurably
raised; migrations and seeding run via the migrator entrypoint, not at
service startup; SQLite remains runnable as the local default or is retired
explicitly with a recorded decision.

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
   runtime image). Gate Frontend's startup browser launch on an explicit
   setting, not the environment name - `Process.Start` under Development in
   a container throws or hangs startup.
2. `compose.yaml`: postgres, redis, gateway placeholder, 1x frontend,
   1x catalog, 1x orders; health checks on `/health` (Frontend's readiness
   from Phase 1); configuration by environment variables only (URLs,
   connection strings, base URLs); logs to stdout. Two logging changes, not
   one: Frontend writes through a `TextWriterTraceListener` while Catalog
   and Orders use their `FileLoggerProvider`.
3. Exposure rule, enforced in the file and checked in exit criteria:
   containers must bind `0.0.0.0` internally, but every port published to
   the host is written `127.0.0.1:PORT:PORT` - never bare `PORT:PORT`, which
   publishes on all host interfaces and puts unauthenticated product and
   order write endpoints on the LAN, reaching the condition Phase 7 exists
   to prevent, three phases early and silently. Only the gateway/frontend
   port is published at all; Catalog and Orders stay on the compose-internal
   network with no host publish.
4. Make the failure suite topology-agnostic. Abstract kill and start behind
   a provider: local keeps the current port-kill and `dotnet <dll>` restarts,
   compose uses `docker compose stop/start <service>`. Move the correlation
   assertions off log-file reads and onto the `X-Correlation-ID` response
   header - both services already echo it, and a header assertion is the
   only version that survives N replicas and stdout logs at Phase 8.
5. Smoke suite and failure suite run against the composed stack with
   `BaseUrl` pointed at the gateway/frontend port.

Exit criteria: `docker compose up` gives a green smoke run and a green
failure suite (via the provider abstraction) on a clean machine state; no
hardcoded localhost anywhere in container configuration; a published-port
check confirms every host binding is loopback-only and Catalog/Orders have
none.

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
   second hop with no demonstrated need. Scope note: this mechanism is
   compose-era. Phase 6 replaces it with a single Kubernetes Service DNS
   name per backend - a static endpoint list goes stale against pod IPs that
   churn on every scale event, so the platform takes over and the
   multi-endpoint code is retired rather than carried forward.
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
   replicas writing to the shared key tables, the concurrent-reserve fact
   re-run across both replicas (the oversell test, now genuinely
   multi-instance), correlation IDs traceable across
   which-instance-served-what, and the degrade/restart behavior from the
   failure suite applied per-replica (kill one catalog replica: no
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
   readiness and liveness probes on `/health` (Frontend's Phase 1 split)
   with `initialDelaySeconds` sized to the Frontend cold start, or better,
   a startup probe. The same exposure rule as Phase 3 applies: NodePort
   services and `kubectl port-forward` are loopback-only until Phase 7
   completes - a NodePort binds on every node interface by default.
2. Collapse the Phase 4 internal balancing: Frontend's service BaseUrls
   become the single k8s Service DNS name for each backend; the Service
   load-balances across healthy pods and the multi-endpoint client code is
   retired.
3. Migrations on the platform: the Phase 2 migrator entrypoint runs as a
   pre-deploy Job (one runner, lock held, gate the deployments on its
   success) so replica count never races a migration or a seed.
4. HPA per service on CPU (start: 60% target) with sensible min/max: min 2
   for frontend (cold-start and availability), min 2 for catalog and orders
   (write availability), max set by the load tests, not optimism - and sized
   against the database before trusting it: Npgsql pools per connection
   string with a default maximum of 100, Postgres defaults to
   max_connections 100, and two services times HPA max replicas times pool
   size exhausts that well before a CPU target trips. Cap pool size per
   replica so max_replicas x MaxPoolSize x services stays under
   max_connections, or put PgBouncer in front; record the arithmetic.
5. Graceful shutdown: SIGTERM handling so Orders can finish or compensate an
   in-flight reserve before a scale-down event reaps it; termination grace
   period sized to the slowest reserve+insert path (measured, not guessed).
   PodDisruptionBudgets so scale-down never takes the last replica.
6. ConfigMaps and Secrets replace env-file values; the secrets policy from
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
   accept traffic only from the gateway and Frontend - Frontend calls the
   services directly by the Phase 4 design, so "gateway only" would be a
   contradiction, and the internal boundary is a network-policy concern
   (compose networks now, NetworkPolicies at Phase 6), not an application
   one.
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

1. Deployment pipeline: build containers from the merged branch, run the
   HTTP characterization tests, the concurrent-reserve fact, and the
   (topology-abstracted) failure suite against a composed environment as CI
   gates, push images, deploy - with the Phase 2 migrator wired as the
   pre-deploy step the platform schedules.
2. Backups: replace the SQLite file-copy procedure with `pg_dump` schedules
   or managed PITR; test an actual restore, not the backup file's existence.
3. Runbook: reseed-equivalent for Postgres, scale overrides, the
   kill-a-replica diagnosis flow, correlation-ID trace walkthrough.

Exit criteria: a restore actually performed; a deploy actually executed end
to end once; the runbook followed by someone who did not write it.

## Risk register

- Bare port publishing is a default-behavior trap, not a one-time oversight:
  `ports: - "8081:8080"` in compose publishes on every host interface, and a
  NodePort binds on every node - either one puts unauthenticated product and
  order write endpoints on the network before Phase 7 exists. Phase 3
  exit-checks loopback-prefixed bindings and unpublished services; Phase 6
  restates it for NodePort and port-forward.
- The unlocked read-check-decrement and the key TOCTOU are invisible to every
  sequential test in the repo. The concurrent-reserve invariant (no oversell,
  no negative stock) is the only guard against Phase 2 trading SQLite's
  accidental serialization for an oversell bug, which is why the chain runs
  invariant-first: green on SQLite (with the exactly-N goal recorded as a
  baseline characteristic, not a gate, since SQLite's 5s busy timeout fails
  it past some N), then invariant and goal both green on Postgres, then
  across replicas at Phase 5.
- Connection budget is a hard ceiling that arrives before CPU targets do:
  Npgsql's default pool (100 connections) per service times HPA max
  replicas does not fit inside Postgres's default max_connections (100) at
  any interesting replica count. Phase 6 sizes it explicitly (pool caps or
  PgBouncer); Phase 8's saturation alert is detection, not the fix.
- ASPX runtime compilation in containers: the published-local runs prove the
  binaries execute, but image size, feed access inside builds, and the ~10-15s
  cold start are real. Phase 3 exists to surface this before anything depends
  on it; the Phase 1 readiness split plus Phase 6 startup probes and min
  replicas compensate. A readiness endpoint that answers 200 before first
  compile re-creates the cold-start storm through the health check itself.
- Postgres semantic drift: case-sensitive string behavior and unique index
  semantics are the likeliest silent behavior changes; the characterization
  facts and the seed-compare discipline from the previous plan are the net.
- The dnceng daily feed: any outage or package eviction breaks container and
  CI builds. Mirror or vendor the SDK packages once Phase 3 works.
- net9-only SDK pin: the previous plan documented the .NET 10 block; base
  images and the SDK pin must move together when upstream unblocks.
- Scale-down in the middle of reserve/release: Phase 6's graceful shutdown
  work is the guard; the compensation release path already exists and is
  tested, this extends it to shutdown.

## What stays out, still

Service mesh, multi-region, CDN, outbox, event bus, read-model service,
client-side circuit breakers. Each stays out until a measured need from the
running system says otherwise - the same discipline that held through the
previous plan's eight phases.
