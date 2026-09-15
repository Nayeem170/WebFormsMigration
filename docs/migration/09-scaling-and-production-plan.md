# Scaling and Production Plan for the Inventory Stack

Status: Phases 0-1 complete (2026-09-14; execution records in each phase).
Phases 2+ not started. This plan picks up where
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

Phase 0 execution record (2026-09-14):

- Tooling: k6 2.2.0 (binary at `artifacts/tools/k6/`, not committed),
  `scripts/run-load.ps1` orchestrating `load/k6-scenarios.js`, and the
  concurrent sweep in `scripts/test-concurrent-reserve.ps1`. Numbers and
  raw summaries live in `load/` (`baseline.md`, `baseline-results.md`,
  `concurrent-reserve.md`, `results/`). Stack: source-run, freshly
  reseeded, closed-model constant-VUs, 20s per level.
- Cold start: 3.4s to first 200 with warm OS caches; first-render request
  durations of 10-13.3s observed in the same session's logs. ~13s is the
  number later phases size probes against.
- Read ceilings: dashboard (full path) saturates around 200 rps; products
  page 570-650; direct catalog/orders list reads 410-690. Zero read
  failures and zero degradation at 50 VUs. Throughput flattens or drops as
  VUs rise on every scenario - per-request cost grows under contention, so
  capacity claims name their concurrency level.
- Write ceiling: ~10 req/s peak at 8 VUs (orders POST+DELETE), collapsing
  to 5-6 req/s at 16-32 VUs with failures rising 0.5% -> 3.9% -> 10%.
  Failure modes, all observed in service logs: `database is locked` 500s
  stalling 6-15s (the 5s busy timeout plus queue); a `UNIQUE constraint
  failed: ReservationKeys.Key` 500 where a committed-but-timed-out reserve
  was retried with the same key - live evidence for Phase 2 step 2's
  violation-to-replayed catch; and one 502 CatalogUnavailable from the
  retry budget, the designed degradation.
- Concurrent-reserve sweep: invariant PASS at every N from 8 to 256 (no
  oversell, no negative stock, stock always exactly N minus successes).
  Goal (exactly N of N) PASS through N=64, FAIL at N=128 (118/128) and
  N=256 (235/256). SQLite's busy breaking point sits between 64 and 128
  parallel reserves on this machine. The invariant is pinned permanently
  by the `ConcurrentReserves_NeverOversell_AllSucceedAtLowN` fact (N=8);
  suite 11/11, test-data cleanup verified.
- One harness correction during the run: the k6 setup product initially
  requested stock 1,000,000, which the DTO's `[Range(0, 999999)]` rejects
  with 400; setup now uses the max. The first write-scenario rows were
  discarded and re-run.

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

Phase 1 execution record (2026-09-14):

- Redis: `ccw-redis` container (redis:7-alpine), published
  `127.0.0.1:16379` - host port 6379 sits in a Windows excluded port range,
  and the loopback-only publish follows the plan's exposure rule. The
  connection string must be the IPv4 literal: `localhost` resolves to `::1`
  on this machine, the container publishes IPv4 only, and the first
  start died at the multiplexer - fail-fast working as designed. Timeouts
  set to 2000ms connect and sync, matching the stack's 2s ServiceHttp
  budget; `AbortOnConnectFail=true` keeps startup loud when Redis is
  required but down.
- Key ring: `SetApplicationName("CoreWebForms.Frontend")` plus
  `PersistKeysToStackExchangeRedis` is the mechanism that overrides the
  per-content-root discriminator - load-bearing, not hygiene. The loud
  fallback works: without `Session:Redis`, startup logs an error-level
  "in-process memory cache" line unless `Session:UseMemoryCache` opts in.
- Health: `/health/live` answers 200 once listening; `/health/ready`
  returns 503 `{"status":"warming"}` until a one-shot background warm-up
  compiles `/`, `/Pages/Products/`, and `/Pages/Orders/` (no per-probe
  self-requests). Observed flip in ~6s warm-cache. Side discovery: the
  warm-up must send a User-Agent - UA-less requests crash
  `ValidationSummary` via `HttpCapabilitiesBase` (pre-existing quirk; a
  UA-less load tool hitting the orders page triggers it too).
- Before-state, from two distinct publish roots (`artifacts/phase1/
  frontend-a` and `frontend-b`): the same-root control pair (two instances
  of frontend-a on 8081/8083) already interoperate - shared
  `%LOCALAPPDATA%` ring, same discriminator - proving the reviewer's
  false-green warning. The distinct-root pair fails exactly as predicted:
  session-cookie unprotect throws `CryptographicException`, ViewState MAC
  validation fails, and the render aborts after headers - 200 with 0
  bytes, mechanism named in the instance log.
- After-state (`Session__Redis=127.0.0.1:16379` on both roots): the
  cross-instance postback A->B returns 200 with the full 43615-byte body,
  identical to same-instance; Redis holds both `ccw:<session-id>` entries
  and the `DataProtection-Keys` ring. Readiness 503->200 verified on the
  published instances.
- Redis-down degradation (the decided shape: reads survive, cart path
  degrades): dashboard and products pages never touch session and keep
  serving full content. Getting the orders page clean took three
  iterations, all recorded: a control-level catch alone is insufficient
  because the adapters' session load fails soft to empty while any session
  touch establishes one whose commit rethrows after response start (0-byte
  abort), and the broken session state surfaces as
  `NullReferenceException`, not `RedisException`, so the first filter
  missed it. The final shape is proactive: `SessionState.StoreUnavailable`
  (multiplexer `IsConnected`) gates `OrderWizard.Bind` away from session
  entirely and renders the standard unavailable message, with the catch
  broadened as backstop. Result: 200, message present, no stack trace,
  order history still rendered; recovery after Redis restart with no
  Frontend restart. Known limitation recorded: requests that already hold
  a session cookie during an outage still hit the commit-abort path and
  get a traceless blank - an adapters-level fix, out of this phase's
  scope.
- Verification: failure suite 22/22 in Redis mode (four new checks:
  dashboard survives, products survive, orders degrades with message and
  no trace, recovery after restart); 18/18 memory mode unchanged; 11/11
  characterization facts; smoke green after reseed - the suite's probe
  orders pushed a seed off the orders page once, the documented
  reseed-before-smoke protocol applied. Session surface confirmed
  single-key (`CartItems`) as predicted; no audit was spent there.

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

Phase 2 execution record (2026-09-14):

- Postgres: `ccw-postgres` container (postgres:17-alpine), published
  `127.0.0.1:15432`. Topology decision: two databases (`ccw_catalog`,
  `ccw_orders`), not one instance with schemas - the split files enforced the
  boundary physically and two databases keep it physical, at the cost of one
  connection string each. Roles: `postgres` owns DDL and runs the migrator;
  `ccw_app` holds CONNECT + USAGE plus default-privilege DML grants (executed
  BEFORE migrations so the grants cover the created tables). Services run as
  `ccw_app` only - verified live.
- Provider split: `Database:Provider` = `sqlite` (default) | `postgres`;
  `Database:ConnectionString` required when postgres. Contexts split into
  abstract `AppDbContext` + `SqliteAppDbContext`/`PostgresAppDbContext` per
  service, with design-time factories for both. Fresh `InitialCreate` per
  provider (sqlite `Migrations/`, pg `MigrationsPostgres/`): pg gets
  `numeric(18,2)`, native boolean, `timestamp with time zone`. Write-side UTC
  converters on pg DateTime properties - npgsql rejects Kind=Unspecified for
  timestamptz; first migrator run failed on it.
- Migrator entrypoint: `--migrate` arg applies migrations + seeds under
  `pg_advisory_lock(94001)` (Catalog) / `94002` (Orders), then exits.
  `Database:Migrate` flag has code default false (appsettings sets true for
  dev convenience; env-only deployments get the false). Two racing migrators
  serialize on the advisory lock; racing replicas find history current and
  skip the seed.
- Locking, final shape: items sorted by ProductId, then one
  `FromSqlRaw("SELECT * FROM \"Products\" WHERE \"Id\" IN (...) ORDER BY
  \"Id\" FOR UPDATE")` returning tracked entities - a single round trip that
  both takes the locks and loads the rows (the first cut did a bare
  `ExecuteSqlRaw` lock plus an EF `Find` per item inside the held lock;
  collapsing it moved the N=64 sweep from goal-FAIL to PASS with no budget
  change). SQLite branch takes the same single query without FOR UPDATE.
  `lock_timeout=1500ms` travels in the connection string as
  `Options=-c lock_timeout=1500` - applied in the startup packet, no extra
  round trip per pool rent, inherited by migrator and design-time factory
  for free (replaced a `DbConnectionInterceptor`; the guard skips the append
  when the caller supplies own Options).
- Exception mapping, final shape: `IsTransientLock` walks the inner exception
  chain so a 55P03/40P01 raised by `SaveChanges` inside `DbUpdateException`
  maps to the typed 503 `LockTimeout` - the first cut caught bare
  `PostgresException`, which only matches the raw-SQL lock statement, and
  the wrapped race escaped as an unhandled 500 (review-caught; reachable by
  exactly the duplicate-key race this code exists for). SQLite branch:
  SqliteException code 5 anywhere in the chain. Unique violations on
  ReservationKeys/ReleaseKeys unwrap to `replayed: true` - both reserve and
  release replay verified live (second call `replayed=true`, stock moves
  once).
- Concurrent-reserve sweep (single-row shape: one product, stock N, N racing
  reserves; harness does NOT retry - raw first-attempt queue drain; the
  Frontend's CatalogClient retries 503 3x at 200ms, so user-visible failure
  is lower than these numbers). Budgets differ by engine and are not
  normalized: sqlite Default Timeout 5s vs pg lock_timeout 1.5s. The table
  is not an engine horse race; what it gates is the invariant, and the
  failure mode:
  - Invariant (stock never negative, never oversold): PASS at every N, both
    engines, before and after the fixes. The gate held throughout.
  - Failure mode: Phase 0 sqlite showed ambiguous 500s at N=128+
    (`UNIQUE constraint failed` on committed-but-timed-out replays). pg
    post-fix, the same rungs yield typed, retryable 503 `LockTimeout` and
    nothing else. Corrupting-and-ambiguous -> typed-and-retryable is the
    correctness win.
  - Drain time is the headline, not success parity: N=8 2320ms (sqlite) ->
    210ms (pg), N=64 4909ms -> 376ms. Equal success counts, 11-13x faster
    to drain the same queue.
  - Before/after the drain fixes (same budget, same N; pre-fix run numbers
    from run notes - the file was overwritten before the rename discipline
    landed): pre-fix pg N=64 28/64 (503x36), N=128 46/70x503, N=256
    148/108x503. Post-fix: N=64 64/64, N=128 58/70x503, N=256 124/132x503.
    N=64 moved from FAIL to full drain.
  - N=128/256 still goal-FAIL on pg and are expected to: N writers on one
    row serialize on any engine - this shape cannot show pg's real win. It
    shows the queue drains or fails cleanly within budget.
- k6 orders-write ladder (multi-row: order insert + reserve/release spread
  across the seeded catalog; 20s constant-VUs; the Phase 0 baseline pair):

  | VUs | sqlite iter/s | pg iter/s | sqlite fail | pg fail | sqlite p95 | pg p95 |
  |-----|---------------|-----------|-------------|---------|------------|--------|
  | 2   | 3.88          | 38.38     | 0.00%       | 0.00%   | 913ms      | 52ms   |
  | 8   | 4.83          | 67.98     | 0.49%       | 0.00%   | 2.48s      | 153ms  |
  | 16  | 2.50          | 53.37     | 3.93%       | 0.00%   | 8.86s      | 399ms  |
  | 32  | 3.17          | 25.24     | 10.00%      | 0.87%   | 12.55s     | 3.32s  |

  ~14x throughput at 8 VUs, zero failures through 16 VUs (sqlite collapsed
  at 16+), collapse point moved past 32 VUs. Two independent measurements -
  single-row sweep and multi-row k6 - same direction. "Write ceiling
  measurably raised" is these numbers.
- DbCopy (`Microservices/tools/DbCopy`, plain Microsoft.Data.Sqlite + Npgsql,
  no EF or service references): TRUNCATE + RESTART IDENTITY, explicit-id
  inserts (UTC-stamped DateTimes, int bools, text decimals to numeric),
  then `setval` via `pg_get_serial_sequence` and a probe insert per table
  asserting the returned Id exceeds the copied max (13>12 products, 14>13
  orders, 17>16 items) - IdentityByDefaultColumn does not advance sequences
  on explicit-id copies and the first new insert would otherwise collide on
  PK in Phase 3 as a mystery. Verification is value-based, not row counts:
  SUM(Price), SUM(Stock), SUM(Total), per-order Total == sum of item lines
  on both engines. Run green: sqlite backup (12 products/451 stock, 13
  orders/16 items, 1 reservation + 1 release key) now lives in pg.
- Data state: sqlite files backed up to `artifacts/phase2/backup/` before
  any pg work; the backup is the DbCopy source of record. pg now holds the
  migrated dev data; canonical reseed = drop/recreate both DBs + grants +
  migrator (scripted; requires `pg_terminate_backend` first - live pooled
  connections block DROP, and the services briefly 500 while npgsql prunes
  the killed connections, self-healing within seconds).
- Verification: 11/11 characterization facts on pg (pre-fix and post-fix
  builds) and on the sqlite regression pass; failure suite 18/18 in pg mode
  (new `-PgMode` switch passes Database env to suite-spawned service
  processes) and 18/18 memory mode unchanged from Phase 1; smoke parity
  green on both stacks (place -> reserve decrements, delete -> release
  restores). sqlite regression required wiping `App_Data/*.db*` and letting
  Migrate reseed - the regenerated migrations changed history ids and the
  old files carried stale ones.
- Artifacts: `artifacts/phase2/concurrent-reserve-sqlite-baseline.md` (the
  full 6-rung Phase 0-era sqlite ladder - the only surviving copy; it was
  briefly mislabeled `-pg`, caught in review), `concurrent-reserve-pg.md`
  (post-fix 4-rung pg sweep), `concurrent-reserve-sqlite-regression.md`
  (2-rung regression pass), `failures-pg-run.txt` (18/18),
  `load/results/20260914-133445-orders-write-pg-vus*.txt` (k6 ladder).
- Env notes for reruns: `pwsh -File script.ps1 -Ns 8,64,128,256` can
  culture-parse the comma list as one integer (864128256) and fail create
  with 400 - run sweeps via `pwsh -Command "& script -Ns @(8,64,128,256)"`.
  `dotnet ef migrations add` builds before scaffolding - rebuild before
  running a migrator off new migration files, or it runs stale DLLs. psql
  quoting under pwsh is unreliable inline; pipe SQL from a temp file.
- Exit criteria, met: sweep invariant green on pg; raced replay returns
  `replayed: true` not 500; all suites green on both providers; write
  ceiling raised with numbers; migrations/seed via migrator entrypoint
  only; sqlite remains runnable as local default (wipe-and-reseed protocol
  documented above).

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

Phase 3 execution record (2026-09-14):

- Images: one Dockerfile per service (`Microservices/{Frontend,Catalog,
  Orders}/Dockerfile`), sdk:9.0 build stage -> aspnet:9.0 runtime, build
  context at repo root with a `.dockerignore`. Sizes: frontend 261MB,
  catalog/orders 268MB. The ASPX compiler ships in the runtime image BY
  DESIGN - `EnableRuntimeAspxCompilation` compiles pages at first render,
  and trimming Roslyn would 500 every page while health stayed green.
  `<SatelliteResourceLanguages>en</SatelliteResourceLanguages>` on Frontend
  drops the 13 Roslyn satellite language dirs without touching the compiler.
- The container compiler question is answered: aspnet:9.0 as non-root app
  compiled and rendered all three warm-up pages on a cold start - reference
  metadata is present, no sdk base needed. First-ready in ~23s cold. The
  gate is `/health/ready` (Phase 1's warm-up doubles as the compile gate:
  it flips only after `/` renders successfully), not `/health/live`. The
  compose healthcheck points at ready for Frontend, plain /health for the
  APIs.
- aspnet:9.0 ships no curl/wget (dropped in .NET 8): the runtime stage
  installs curl for the container healthchecks. Redis/Postgres use their
  own `redis-cli`/`pg_isready`.
- Binding: compose env sets `Urls=http://0.0.0.0:PORT` for all three
  services - the code defaults are loopback and would be unreachable in a
  container. Frontend's warm-up already rewrites 0.0.0.0/[::] back to
  localhost for its self-request, so readiness works unchanged.
- Migrators are compose services, not a flag at startup: `catalog-migrator`
  and `orders-migrator` run the same images with `--migrate`, depend on
  postgres `service_healthy`, and the services depend on them with
  `service_completed_successfully` plus `Database__Migrate=false`. This is
  the shape Phase 6 turns into a Job; it landed here, before replicas make
  the race live. Role/bootstrap SQL ships as `infra/postgres-init.sql`
  mounted into `/docker-entrypoint-initdb.d` (runs on first volume init:
  creates `ccw_app`, the second database, and per-database default
  privileges - re-creating the volume re-applies it). Pitfall recorded:
  `docker compose up --abort-on-container-exit` treats a migrator's clean
  exit 0 as a stop signal and tears the stack down; detached `up -d` is the
  correct invocation for one-shot init services.
- Container restore failed first with MSB4236: NuGetSdkResolver needs a
  version for the custom `CoreWebForms.Sdk`. Fix: root `global.json` with
  `msbuild-sdks: { "CoreWebForms.Sdk": "1.0.0" }`, COPYed into all three
  Dockerfiles before `dotnet restore`. Local cold-cache restore (isolated
  NUGET_PACKAGES) verified feed-only resolution works, so the dnceng daily
  feed access inside the build is proven, not assumed.
- Logs: stdout first. Frontend gained a `ConsoleTraceListener` alongside
  the file listener; Catalog/Orders keep the default console provider. The
  file sinks are now best-effort (try/catch on the directory create and
  provider registration): the .NET 8+ images run as non-root `app` against
  a root-owned `/app`, and the previous unconditional
  `Directory.CreateDirectory(App_Data/logs)` crashed startup before
  anything served. On the host the file logs still appear (failure suite
  keeps its local log reads); in containers stdout is the only sink, which
  is also what makes N-replica log assertions possible later.
- Correlation: Frontend gained the same mint/reuse/echo middleware the
  services have (`X-Correlation-ID` response header on every response,
  sharing the `CorrelationId` items key with ServiceHttp so outbound calls
  carry the same id). The failure suite's correlation checks moved to
  header assertions for the direct-API cases and `docker compose logs` for
  the forwarded/minted chain checks in compose topology; file-log reads
  remain for local topology.
- Failure suite topology: `-Topology local|compose`. Local keeps port-kill
  and `dotnet <dll>` restarts; compose uses `docker compose stop/start`.
  The suite needs host access to Catalog/Orders, which the default compose
  shape deliberately does not publish: `compose.test-ports.yaml` (override)
  publishes `127.0.0.1:18094/18095` for suite runs only. The default shape
  is what the exposure check gates.
- The find of the phase, caught by smoke after the suite false-passed: the
  first compose up left Orders' outbound Catalog URL at its localhost
  default - inside the orders container that reaches nothing, so
  place-order returned 502 with Catalog running. The suite's
  'place order fails 502 while Catalog down' check passed for the wrong
  reason (it would have passed with Catalog up too). Fixed with
  `Services__Catalog__BaseUrl=http://catalog:8094` in compose; re-verified:
  honest 502 only while stopped, 201 + stock 38->35->38 lifecycle green.
  Lesson recorded: a negative check can pass for the wrong reason; the
  positive lifecycle (smoke place-order) is what proves the wiring.
- Exposure check is a command against running state
  (`scripts/check-exposure.ps1`): `docker compose ps --format json`,
  asserting every host-published port binds HostIp 127.0.0.1 and that
  catalog/orders/migrators publish nothing (`PublishedPort > 0` count 0 -
  EXPOSE alone must not trip it). Inspecting running state catches
  override-file and profile publishes a compose-file grep would miss.
  Green on the default shape.
- Verification on the composed stack: failure suite all checks passed
  (23 PASS lines: 18 prior + header-based correlation rework) in compose
  topology with Redis mode, including degrade/recover for Catalog, Orders,
  and Redis; smoke parity green (place 201, delete 204, stock restored);
  `/health/ready` gate proven by cold start. Rollback proof: bare-metal
  stack (sqlite, memory session) - failure suite all checks passed and
  11/11 characterization facts, same binaries, no containers.
- Standing state after the phase: the compose stack is the default running
  stack (all healthy); the bare `ccw-postgres`/`ccw-redis` containers are
  stopped (superseded by the compose postgres/redis on the same loopback
  ports). Artifacts: `artifacts/phase3/failures-compose.txt`,
  `artifacts/phase3/failures-local.txt`.

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
4. Correlation IDs pass through the gateway unchanged, and the gateway mints
   one when the inbound request has none - the gateway's access log then
   always shares an ID with everything downstream (Phase 8's requirement).
   Host and scheme pass through untouched for now: WebForms builds postback
   targets and redirects from them, and the stack is plain HTTP until
   Phase 7.

Exit criteria: browser traffic lands on both frontend replicas in a
two-instance test; killing one backend shifts traffic with no user-visible
failure; failure suite green through the gateway port.

Standing verification rule for every remaining phase (introduced here after
the Phase 3 Orders BaseUrl find): a negative check that passes because the
mechanism under test was never wired is indistinguishable from a real pass.
Every exit criterion gets a positive control - per-instance evidence
(`X-Instance` response headers, instance-tagged logs) proving traffic
reached the specific instance/endpoint in question, before the negative
behavior is asserted. "Requests succeed" is not evidence of balancing;
"both instance ids observed" is.

Rollback: point the browser and `BaseUrl` configs back at direct ports; the
gateway is stateless and removable.

Phase 4 execution record (2026-09-14):

- Gateway: `Microservices/Gateway`, a minimal YARP app (config-as-code from
  appsettings, no custom policies). One cluster "frontend" with two explicit
  destinations (http://frontend:8081, http://frontend2:8081), RoundRobin
  balancing, active health checks every 2s against `/health/ready`
  (ConsecutiveFailures policy), passive health enabled. Compose runs
  `frontend` + `frontend2` as explicit services (not `deploy.replicas`) so
  YARP has stable per-instance DNS names - a replica-set's single service
  name gives YARP one logical destination and no per-instance skipping.
  The gateway is the only published application port (127.0.0.1:8080);
  frontend lost its host publish and moved into check-exposure's
  zero-published list alongside catalog/orders.
- The YARP lesson that cost the most debugging: marking destinations
  Unhealthy does NOT stop routing by default. YARP's default
  AvailableDestinationsPolicy is `HealthyOrPanic` - when every destination
  is unhealthy it "panics" and routes to all of them rather than returning
  503. Observed exactly that: both backends actively marked Unhealthy,
  gateway still serving 200s. The fix is the explicit cluster policy
  `AvailableDestinationsPolicy: HealthyAndUnknown` (confirmed against YARP
  source: ClusterDestinationsUpdater defaults to HealthyOrPanic; there is
  no "ReadyAndUnknown" - that name silently resolves to nothing). With the
  strict policy: all backends unready -> empty destination set -> typed 503
  at the edge. Also from source: ConsecutiveFailures' threshold is not an
  Active config key (a stray `FailureThreshold` in appsettings is silently
  ignored); it comes from the policy options default, overridable per
  cluster via metadata.
- `/health/ready` split per the review: the warm-up latch stays as a
  precondition AND a live dependency check runs - a Redis PING on the
  registered multiplexer (500ms budget, `GetDatabase().PingAsync()`);
  memory-session mode (local topology) skips the ping. Ready now means
  "warmed AND currently able to serve session traffic", which is what a
  routing decision needs. First implementation bug found by the suite: the
  multiplexer was registered via `AddSingleton(multiplexer)` which binds
  the CONCRETE type - `GetService<IConnectionMultiplexer>()` returned null
  and the ping was silently skipped (ready stayed 200 with Redis down).
  Registered as the interface; re-verified 503.
- Consequence for the failure suite, recorded deliberately: with Redis
  down, BOTH frontends report unready, the gateway excludes everything and
  returns 503 - the page-level degrade UX ("Session store is unavailable
  right now") is now unreachable through the gateway. The suite keeps the
  page-degrade assertions in local topology (no gateway, direct frontend)
  and asserts the typed 503 + recovery in compose topology. Both behaviors
  are still tested; which one a user sees depends on whether a gateway is
  in the path.
- Correlation at the edge: the gateway reuses an inbound X-Correlation-ID
  or mints one, echoes it, and sets it on the request BEFORE proxying so
  downstream sees the same id. Gateway-local response headers go through
  `Response.OnStarting` with ContainsKey guards - setting them pre-proxy
  duplicated values once YARP copied the backend's headers (both sides
  wrote the header).
- Instance identity: all three services + the gateway emit `X-Instance`
  (machine:pid) / `X-Gateway-Instance` response headers. This is the
  positive-control substrate: the suite proves round-robin by collecting
  the header over 12 gateway requests and asserting 2 distinct values, and
  proves the kill test by asserting pre-kill traffic included the victim,
  post-kill traffic hit ONLY the survivor, and the restarted victim
  rejoins. All three held.
- Multi-endpoint BaseUrl per plan step 3: `ServiceEndpointPool` in
  Contracts (comma-separated list, round-robin, endpoint benched after 2
  consecutive transport failures, re-admitted after 30s; HTTP-status
  failures do NOT bench an endpoint - transport vs application failure
  distinguished, matching the existing retry semantics). Frontend's
  ServiceHttp/HttpProductService/HttpOrderService and Orders' CatalogClient
  select an endpoint per ATTEMPT so retries naturally fail over. Each pool
  logs its resolved endpoint count at startup ("Catalog endpoint pool:
  N endpoint(s): ...") and the suite attributes every pool line to its
  container - log prefixes kept, containers cross-checked against
  `docker compose ps` names - then asserts each RUNNING container logged
  exactly the shape's expected endpoint count (bumped with the shape in
  Phase 5). A missed override falling back to the single-endpoint
  appsettings default fails, and so does a replica that never logged;
  attribution fails closed because an unparsable prefix cannot match a ps
  name. Teeth verified directly: expecting 2 against a live 1-endpoint
  pool fails the assertion. Frontend's pool
  log lines initially vanished: AppData.Initialize ran before the Trace
  listeners were attached; listener setup now precedes client
  construction. Compose Phase 4 shape is deliberately 1 catalog + 1 orders
  endpoint (2x everything is Phase 5); the pool logs show 1 endpoint(s)
  until then.
- Frontend's docker logs now carry the Trace output (ConsoleTraceListener
  landed in Phase 3); with the ordering fix, instance-level facts are
  greppable per replica - the substrate Phase 8 formalizes.
- Compose quirks recorded: YAML merge anchors (frontend2 = <<: *frontend)
  work, but each service still builds its own image tag
  (corewebforms-frontend2) - rebuilding "frontend" does NOT refresh
  frontend2; build both. And any `docker compose up` without the same -f
  override files as the original up silently recreates services under the
  base config (dropping test ports) - always pass both files.
- Retrofit before Phase 5, same review class one level up: smoke-parity.ps1
  was observational - content checks printed YES/NO and exited 0 either
  way, so only a human reading the output could fail it (the Phase 3
  Orders BaseUrl find happened exactly that way). Now fail-closed: every
  content line is a Check, place/insufficient/delete assert 201/409/204,
  stock arithmetic asserts baseline-3 then baseline, exit 1 on any
  failure. Teeth verified live: with Catalog stopped the script FAILs
  content and exits 1. The rewrite immediately caught real drift the old
  script had been silently printing NO for: the orders list paginates and
  repeated suite runs had pushed the seeded Karen Novak order off page 1.
  Seeded-name assertions now live on home's bounded recent window; the
  orders page asserts structure plus the self-referential place/
  struck-through pair, which cannot drift.
- Post-verification hardening (same review class, third pass): the products
  grid had the orders-page drift bug's twin shape, but the fix differs
  because the semantics differ - the grid's default sort is Id ASC, so NEW
  products append to later pages and can never push seeded names off page 1
  (Karen fell off orders because that list sorts newest-first), and a
  create-then-assert-absent probe would be vacuous here because a new
  product is past page 1 for paging reasons alone. The drift-proof form is
  parity: the smoke re-derives expected page 1 from the API using the
  page's own rule (active-only, Id ASC, first 5) and requires the rendered
  grid to match exactly - broken filter, sort, or page size all fail it.
  Teeth verified against a doctored expectation. The row-count label check
  parses the claimed number instead of substring-matching ("1 product"
  matches inside "11 products"). Also: the three unconditional
  Check 'GET / returns 200' ($true) lines were dropped; GetPage throwing
  already covers non-200 and unconditional PASS lines are the vacuous-pass
  shape in miniature.
- That rewrite found two more real bugs immediately. (1) The sweep's
  cleanup deleted each rung's product with SkipHttpErrorCheck and piped to
  Out-Null - a 5xx left a live sweep product in the catalog silently, which
  drifts the grid AND changes what Phase 5's read scenarios measure. Now:
  delete must return 204 (one retry), else the sweep stops loudly. The
  parity check's first API call also exposed (2) Catalog's product list
  endpoint taking a non-optional `bool includeDeleted` - every existing
  caller always sent the parameter, so a bare GET /api/products returned
  400 and nothing had ever noticed. Now optional, default false.
- PowerShell hazard recorded: on 7.6.6, `@(Invoke-RestMethod <url>)` inside
  a script returns a JSON top-level array as ONE nested Object[] element,
  which member-enumerates past Where-Object and silently defeats
  Select-Object -First (filter "passed" all 12 products, count came out 1).
  Invoke-WebRequest + ConvertFrom-Json has flat, predictable shape; smoke
  uses that for array endpoints.
- Verification: failure suite 28 checks green through the gateway port in
  compose topology with Redis mode (including both-replicas, pass-through,
  mint, pool-log, Redis-503, and the kill/rejoin sequence); smoke parity
  green through the gateway (place 201, stock 38->35->38, delete 204);
  11/11 characterization facts against the compose services; exposure
  check green on the default shape (frontend + frontend2 now zero-published,
  gateway 8080 loopback); local rollback proof - bare sqlite stack, suite
  all checks passed, 11/11 facts, same binaries (pool code active with a
  single-endpoint pool).
- Standing state: compose stack on the default shape, all healthy, gateway
  on 127.0.0.1:8080. Artifacts: `artifacts/phase4/failures-compose.txt`,
  `artifacts/phase4/failures-local.txt`.

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

Predicted throughput shape (written BEFORE running the Phase 5 ladder, per
the standing rule - baseline above is the single-instance run in
load/concurrent-reserve.md: 8/8 and 64/64 clean in 210/376ms; 128 ->
58 successes + 70x503 in 4425ms; 256 -> 124 + 132x503 in 6834ms):

- Low rungs (8, 64): wall time flat to slightly better (roughly 0.8-1.2x
  of single-instance). These rungs never saturate a single catalog, so a
  second replica adds little; nothing regresses.
- High rungs (128, 256): the bottleneck is per-product row serialization
  (FOR UPDATE on one shared row across both processes) against one
  Postgres, so wall time improves modestly at best. The 503 counts at
  128/256 stay the same order of magnitude or get slightly WORSE:
  two processes contending for the same row means more lock waits inside
  the same per-request timeout budget. The recently classified 53300
  should be invisible (MaxPoolSize=20 x 4 = 80 < 100).
- Invariant (stock >= 0, stock == N - successes) must PASS at every rung:
  cross-process oversell is the thing this phase exists to disprove.
- instances == 2 at every rung; a rung observing one catalog instance
  means the sweep is not actually spanning replicas and the run does not
  count.
- Read paths (dashboard/products through the gateway) should scale
  closer to linearly than the write path: reads are lock-free and
  pool-parallel.
- Table-size note before comparing numbers: soft-deleted sweep rows from
  earlier phases stood at 30 of 42 total products at Phase 5 start. If the
  ladder has been run heavily since, re-check; a much larger table makes
  the read numbers not-quite-comparable to Phase 0's baseline.

Rollback: scale counts back to 1; nothing else changes.

Phase 5 execution record (2026-09-14):

- Shape: explicit catalog2/orders2 services (same anchor/merge pattern as
  frontend2) rather than deploy.replicas - stable DNS names for the client
  pools. Frontend and Orders BaseUrls are now 2-endpoint CSVs; pools log
  "2 endpoint(s)" per process and the suite asserts the count per
  container. All four services cap Npgsql MaxPoolSize=20 in compose (4 x
  20 = 80 < max_connections=100, headroom for migrators/psql); the Npgsql
  default of 100-per-process would let four pools exhaust the server on
  their own. 53300 (too_many_connections) joined 55P03/40P01 in Catalog's
  IsTransientLock so a future pool storm degrades to a retried 503 rather
  than a raw 500.
- Suite (39 checks, compose): replica-failover phases for both services
  (stop catalog A -> dashboard still serves AND place order still succeeds
  via B; then stop B -> the pre-existing degrade/502 checks), both-replicas
  -back gates, and the cross-replica double-delete (204 on A, 404 on B,
  stock released exactly once - safe by construction: soft-delete is one
  shared row, the second DELETE sees IsDeleted). Read-ServiceLog now
  appends the twin replica's logs for the correlation checks; with 2x
  services the request may land on either replica, and the first run
  failed exactly there ('correlation id reaches Catalog log' grepped only
  catalog's logs while the request hit catalog2).
- Local regression caught a self-inflicted bug the same hour: the
  failover-phase rework initially dropped the local-topology stop calls
  entirely, so 'Catalog down' checks ran with catalog still up and failed
  on their own negation. Local stops restored; local suite 19 checks
  green, 11/11 facts.
- Content checks de-drifted: 'home shows Leo Garcia' and the Orders
  failover check now assert the newest LIVE order's name (fetched from the
  API before the stop) instead of a fixed seed - by Phase 5 the recent
  orders window was already buried under accumulated probe orders, so the
  fixed-name form would have failed for non-regression reasons (Karen
  Novak's lesson applied one window earlier). Orders' list endpoint got
  the same optional-params fix Catalog received (bool includeDeleted,
  status, skip, take were all required; the suite's take=1 probe 400ed).
- Sweep: alternates replicas per request (even -> A, odd -> B) so one rung
  exercises both processes against the shared row, reports instances=N per
  rung, and THROWS if any rung observed fewer than 2 distinct X-Instance
  values - a rung that never spans replicas does not count.
- Ladder, same-day apples-to-apples (gateway stack, same DB): 1x today =
  8/8 2120ms, 64/64 2632ms, 128/128 3058ms, 256 -> 81 successes + 175x503
  in 22199ms; 2x today = 8/8 2160ms, 64/64 2668ms, 128/128 3026ms,
  256/256 clean in 4033ms. Invariant PASS at every rung both shapes.
  Reads through the gateway (k6, frontend pair constant, only
  catalog2/orders2 differing): products-page 1.67-2.09x rps, dashboard
  1.45x at 5 VUs and 5.2x at 50 VUs where 1x collapses to 79 rps while 2x
  holds 408.
- Prediction outcome, honestly: the low-rung "flat" and reads-roughly
  -linear calls were right, and instances=2/invariant held everywhere. The
  high-rung call was WRONG in the good direction - predicted same-order
  503s or slightly worse, got zero 503s at 128 AND 256. Two reasons the
  prediction under-called: per-process contention halves (each replica
  serializes N/2, so per-request lock queues shorten), and MaxPoolSize=20
  changed the contention profile so much that today's 1x also runs 128
  clean where the original baseline dropped 70 - the pool cap, not the
  second replica, fixed the mid-rung storms. Only at 256 does 1x still
  collapse (22s wall) while 2x completes clean at 4s. Lesson recorded:
  when a tuning knob changes between measurements, re-run the OLD shape
  the same day before attributing the delta to the NEW shape.
- Table-size check per the review note: 30/42 soft-deleted before the
  Phase 5 ladder, 40/52 after (the four rung products, cleaned up but
  retained as soft-deletes) - small enough that read comparisons remain
  meaningful; re-check if the ladder runs heavily again.
- Standing state: default-shape compose stack, 9 services healthy
  (2x frontend, 2x catalog, 2x orders, gateway, postgres, redis),
  exposure all-pass (catalog2/orders2 join the zero-published list).
  Artifacts: artifacts/phase5/.

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

Phase 6 decisions (recorded before any manifest is written, per review):

- Connection-budget arithmetic FIRST. From the Phase 5 numbers:
  MaxPoolSize=20 per pod, Postgres max_connections=100, 20 held back for
  migrators/admin -> 80 budget, and (catalog R + orders R) x 20 <= 80
  gives R <= 2 - the HPA ceiling would equal today's static count and
  scaling would be arithmetically impossible. The structural problem:
  pool size is deploy-time, replica count is runtime, and nothing shrinks
  pools as pods multiply; every scale-up is a connection-budget event with
  no feedback path. DECISION: raise Postgres max_connections to 200 in
  BOTH compose and k8s (parity - one number, both topologies), keep
  MaxPoolSize=20. Budget 200 - 20 = 180 -> Rc + Ro <= 9 -> HPA max 4+4 =
  160 <= 180 with 20 spare. Why this lever: Phase 5's real finding was
  that the pool cap is admission control - it bounds concurrent
  transactions queuing on a row. Lowering MaxPoolSize to buy replicas
  re-derives that control at every HPA change and invalidates every Phase
  5 measurement; raising max_connections changes no per-pod behavior, so
  all Phase 5 baselines stay valid, and the wall moves to a documented
  place. PgBouncer is the only option where HPA max comes purely from
  load tests (pools decoupled from server connections), but it must also
  enter compose for parity and adds a new failure class; recorded as the
  designated next step if scaling past 4+4. HPA max 4 is load-justified,
  not optimistic: 2x ran the 256-rung sweep clean (4033 ms) and held
  408 rps on dashboard@50 - 4 is drain and burst headroom. min 2 each for
  frontend/catalog/orders (cold start, write availability), gateway
  static.
- Shutdown arithmetic (step 5, measured from code, not guessed):
  CatalogClient is 3 attempts x 3 s timeout + 200 ms backoff = 9.4 s per
  stock call. Worst in-flight place-order: reserve (9.4 s) + insert
  (< 0.5 s) + compensation release on insert failure (9.4 s) ~= 19.5 s -
  comfortably past the ASP.NET Core default ShutdownTimeout of 5 s,
  which would cut the process mid-compensation: stock reserved, order
  never inserted, release never sent. DECISION: HostOptions.ShutdownTimeout
  = 25 s in every service; terminationGracePeriodSeconds = 35 (the pod
  grace is useless if the app already gave up first).
- Probe split (step 1). Liveness gets /health/live and NOTHING else:
  /health/ready PINGs Redis, and wiring that to liveness turns a Redis
  blip into every Frontend pod failing its probe simultaneously -
  dependency degradation amplified into a restart storm and total outage.
  Catalog and Orders currently expose a single unconditional /health
  with no DB check: liveness-only would leave readiness blind to
  database loss. DECISION: add the same split there - /health/live
  (unconditional) + /health/ready (DB ping). Readiness SHOULD see DB
  loss (the Service pulls the pod); liveness must not.
  Startup probe on Frontend sized off Phase 3's measured ~23 s cold ASPX
  compile with margin for CPU-limit stretch: period 5 s x
  failureThreshold 30 = 150 s ceiling. An undersized startup probe
  produces CrashLoopBackOff that reads like an application fault.
- Migrations (step 3): Kubernetes has no service_completed_successfully.
  DECISION: run the migrator entrypoint (same image, --migrate) as an
  initContainer in each Deployment - the Phase 2 advisory lock
  serializes concurrent runners, the pod cannot go Ready before its own
  migration check completes, and gating is structural rather than
  orchestration-dependent. Divergence from compose (one-shot services)
  is deliberate and documented here.
- Pool retirement and pod pinning (step 2): k8s Service DNS replaces the
  multi-endpoint pools (single BaseUrl per service). But ClusterIP
  balances per connection and both ServiceHttp and CatalogClient set
  PooledConnectionLifetime = 2 min with keep-alives - Frontend pins to
  whichever Catalog pod it first connected to for two minutes at a
  stretch, new HPA pods receive no traffic until connections cycle, CPU
  stays high on the originals, and the HPA scales again: a feedback loop
  that looks like working autoscaling with lumpy distribution. DECISION:
  make PooledConnectionLifetime configurable
  (Http__PooledConnectionLifetimeSeconds, default 120 to preserve
  compose behavior) and set 20 s in the k8s ConfigMap. The retirement
  gets the standing rule applied to it: X-Instance diversity asserted on
  the Frontend->Catalog hop SPECIFICALLY (catalog pod logs across a
  dashboard burst must show >= 2 pods serving), not just at the gateway -
  the gateway will look fine while the internal hop is pinned.
- Exposure: ClusterIP-only Services (no NodePort, no LoadBalancer) until
  Phase 7; access is kubectl port-forward, which binds loopback by
  default. A k8s exposure check asserts this.

Exit criteria: a load ramp grows replicas and a load drop shrinks them, on
the local cluster, with all suites green at both ends; a scale-down during
place-order traffic produces no lost or double-charged stock.

Phase 6 HPA prediction (written before the run, per the standing rule):
mixed load = orders-write 16 VUs (via orders Service, exercising the
reserve path on both orders and catalog) + dashboard 8 VUs (via gateway).
CPU requests are 500m for catalog/orders, 250m for frontend, HPA target
60%. Expectation: catalog/orders cross 60% within ~30-60s of load start
and scale 2->3 (4 only if per-pod CPU saturates); frontend's rendered
dashboard is cheap post-compile so it may stay at 2. Scale-up pods Ready
within ~15s of the HPA decision and MUST serve traffic (per-pod log
evidence, the pinning guard). After load stops: 60s stabilization + 1
pod/30s policy -> back to 2 within ~3-4 min. Risk to watch: k6 write
load through one port-forward is itself a bottleneck - if measured rps
looks port-forward-bound, the HPA may see too little CPU to trip.

Phase 6 execution record (2026-09-15):

- Platform: kind v0.33.0 (winget has no kind package; binary fetched to
  the user tools dir), single control-plane node, metrics-server applied
  with --kubelet-insecure-tls (kind's kubelet certs are unsigned).
  k8s/apply.ps1 builds the four images, creates the cluster, kind-loads
  images, creates the postgres Secret by PARSING POSTGRES_PASSWORD out of
  compose.yaml at runtime (same source compose uses; no new literals in
  git), then applies manifests and waits Ready. First run failed on
  ordering: secrets created before the namespace existed - fixed by
  applying 00-namespace.yaml first. Frontend pods restart ~2x at first
  bring-up (Session Redis AbortOnConnectFail crashes before redis is
  Ready); kubelet's restart loop handles it, recorded as expected.
- Probes (decision block, implemented): liveness = /health/live
  everywhere; Catalog/Orders gained the split (/health/live +
  /health/ready with a DB check); frontend startupProbe 5s x 30 = 150s
  over the measured ~23s cold compile. One real lesson mid-phase:
  readiness first used EF CanConnectAsync, which BORROWS A WARM POOLED
  CONNECTION and validates nothing on the wire - with postgres gone, one
  pod flipped NotReady in ~15s (no pool) while the other stayed Ready
  for ~3 minutes (blackholed TCP looks alive until bytes move).
  /health/ready now runs SELECT 1 so bytes hit the socket; DB loss flips
  every pod within one probe threshold. Structural test in the battery:
  scale postgres to 0 -> catalog ready-addresses shrink to none, zero
  restarts (liveness quiet), recovery gated on having OBSERVED emptiness.
- Two vacuous-pass traps caught in my own k8s checks, same class as the
  Phase 5 ($true) sweep: (1) endpoints emptiness tested via
  '{.subsets}', which keeps printing notReadyAddresses forever - the
  check was unfalsifiable; now queries .subsets[0].addresses. (2) The
  recovery check passed while the loss check failed because endpoints
  never emptied; now gated. PowerShell also bit twice in psql asserts:
  \" is not an escape in PS strings (the SQL arrived as several mangled
  arguments, psql's error went to 2>$null, and empty output cast to 0
  made two checks pass vacuously), and ConvertTo-Json's default depth 2
  silently mangles nested items arrays. Both fixed with -f formatted
  single-quoted SQL and -Depth 5.
- Pool retirement (decision block, implemented): single Service DNS
  BaseUrls; per-pod pool count asserted = 1 via each pod's OWN logs
  (frontend writes pool lines to its file logger - read inside the pod
  with grep; orders logs console). Http__PooledConnectionLifetimeSeconds
  defaults 120 (compose unchanged) and the k8s ConfigMap sets 20.
  Frontend->Catalog diversity asserted from POD LOGS over a 60s marked
  burst: 2 of 2 catalog pods served; correlation id from a gateway page
  GET reaches catalog pod logs.
- Drain criterion (k8s/scale-drain-test.ps1): 60 sequential place-orders
  through the orders Service with a catalog pod DELETED at iteration 20
  (SIGTERM drain, ShutdownTimeout 25s, grace 35s overlapping in-flight
  reserves). Result: 60/60 x 201, DB-asserted (psql, immune to
  port-forward churn): 60 order rows, 60 charged items, stock 60 -> 0
  exactly (no lost charge, no double charge), no unexpected statuses,
  orders pods never restarted. Assertions query "Products"/"Orders"/
  "OrderItems" with quoted PascalCase identifiers (EF naming).
- HPA (k8s/hpa-demo.ps1): ramp = orders-write 16 VUs via orders Service
  + dashboard 8 VUs via gateway, 150s. orders 2 -> 4, frontend 2 -> 4,
  catalog stayed 2; all four new pods shown to have SERVED traffic by
  per-pod logs (new orders pods: 4144 and 5189 requests - the 20s pooled
  lifetime distributing load, the pinning guard earning its keep).
  Scale-down: 60s stabilization + 1 pod/30s -> all back at 2; smoke
  green at the end state. Prediction outcome, honestly: right that
  orders would scale (but to 4, not the predicted 3); WRONG that
  catalog's reserve path would trip CPU (reserve SQL is cheap - the
  write bottleneck is the orders-side insert/EF/JSON pipeline); WRONG
  that frontend might stay at 2 (dashboard render with session state
  costs more than predicted). The port-forward-bottleneck risk did not
  materialize.
- Parity after the connection-budget change: compose stack rebuilt with
  the same images, postgres recreated with max_connections=200 - full
  suite 39/39 with -RedisMode -PgMode, exposure all-pass on the default
  shape, local bare suite 19/19. The decision block's claim that raising
  max_connections invalidates no Phase 5 baseline is now tested, not
  just asserted.
- kubectl port-forward pins ONE backing pod: any churn of that pod
  silently kills the stream (it fails on next use). The drain test
  self-heals its forwards; and the k8s forwards collide with the compose
  overlay ports 18094/18095 - both stacks cannot use those local ports
  simultaneously. Killing a stray forward by port owner killed
  com.docker.backend once and took Docker Desktop (and the kind node)
  down; everything recovered on restart - kind containers auto-restart
  and the cluster came back with all pods Ready.
- Standing state: compose default shape 9 healthy + exposure all-pass;
  kind cluster up (9 pods at min replicas, HPA metrics live, ClusterIP
  only per k8s/check-exposure.ps1). Artifacts: artifacts/phase6/.

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
   ForwardedHeaders middleware becomes MANDATORY here (the gateway must
   forward X-Forwarded-For/Proto/Host and Frontend must honor them): the
   Phase 4 gateway deliberately passes Host and scheme through untouched,
   which is correct only while everything is plain HTTP end to end.
4. Rate limiting: ASP.NET Core rate-limiter middleware at the gateway
   (per-IP and global limits informed by Phase 0 numbers), applied before
   auth-adjacent endpoints.
5. Secrets (IdP client secrets, connection strings) from the orchestrator's
   secret store; never in compose files or git.

Exit criteria: unauthenticated writes rejected; authenticated flows pass the
full smoke; limits demonstrably trip under the load tool; the stack answers
on a non-loopback interface only with all of the above in place.

Rollback: back to loopback bindings; security configuration is additive.

Phase 7 decisions (recorded before any code, per review):

- SEQUENCING RULE (the structural point): the exit criteria bundle
  "unauthenticated writes rejected" with "answers on a non-loopback
  interface", but they are not peers. Everything else here is additive and
  reversible; the bind change is the one step that cannot be un-exposed on
  a shared network. The bind change is the LAST commit of the phase and is
  gated on the auth test passing WHILE STILL LOOPBACK-BOUND. The dangerous
  partial state is TLS and the listener landing before authorization is
  enforced on every write path; every intermediate commit must leave the
  stack safe overnight (loopback-bound or auth-enforcing).
- Blunt fact, recorded: the services have no authentication and will not
  after Phase 7. POST/PUT/DELETE on Catalog, /api/products/reserve, and
  every Orders write endpoint remain completely unauthenticated; the only
  thing between them and any caller is network reachability. That makes
  NetworkPolicy load-bearing in a way nothing else in this plan is:
  default-deny ingress with EXPLICIT allows (not allows over an open
  default), verified by a negative test with a positive control - exec
  into a pod that must NOT have access and see the call fail, then the
  same call from Frontend's pod succeed. A deny that was never configured
  looks identical to a deny that works. Compose networks cannot express
  default-deny the same way, so k8s is the REFERENCE topology for this
  phase's security posture; compose remains the functional-parity runner.
  kind's default CNI does not enforce NetworkPolicy - the cluster is
  rebuilt with Calico when this phase lands.
- Authorization decision: reads stay public (public catalog is the
  product), writes require an authenticated user. At the gateway this
  maps to: GET/HEAD/OPTIONS pass; every other method (the WebForms
  postbacks POST to Default/Orders/Products .aspx, plus any future API
  surface) requires authentication. IdP for local verification: Keycloak
  (container in both topologies, realm imported from
  infra/keycloak/corewebforms-realm.json). Browser flows use OIDC code
  flow + cookie sessions; automated verification uses the JWT bearer path
  (direct access grant against the local realm - enabled for local test
  automation ONLY, recorded). The WebForms session-expiry edge is decided
  deliberately: sliding expiration sized to cover a working session, and
  a POST arriving unauthenticated redirects to the IdP (the postback body
  is lost; that is accepted and recorded rather than discovered during
  smoke). Cookie attributes: SameSite=None; Secure on OIDC correlation
  and nonce cookies (form_post callbacks), session cookie Secure once TLS
  terminates at the gateway.
- ForwardedHeaders vs rate limiting: the gateway clears KnownProxies and
  KnownNetworks (pod IPs are dynamic), which makes X-Forwarded-For
  attacker-controlled. Mitigations, all three: (1) the gateway
  OVERWRITES X-Forwarded-For from the real socket address rather than
  appending to what arrived; (2) NetworkPolicy guarantees nothing but the
  gateway reaches Frontend directly, so the overwrite is trustworthy at
  the only consumer that matters; (3) the rate limiter partitions on
  Connection.RemoteIpAddress - the socket - registered BEFORE any header
  parsing, so a rotated XFF header cannot buy quota. Per-IP and global
  fixed windows; 429 on excess.
- Egress hygiene (correct internally, wrong once public): the gateway
  STRIPS X-Instance (machine:pid = pod name in k8s; discloses naming and
  replica-count signal) from responses on egress - it is not removed from
  services, the suites read it inside the network and keep working.
  X-Gateway-Instance gets the same treatment (same disclosure class).
  /health/live and /health/ready are not routable from outside: the
  gateway serves them on a separate MANAGEMENT PORT only (probes point
  there; the public port returns 404 for /health*). /health/ready reports
  Redis and DB reachability - free reconnaissance and an attack-timing
  oracle. X-Correlation-ID from external clients is VALIDATED (32 hex
  chars, the gateway's own mint format) and replaced when non-conforming
  - an attacker must not control a logged field (newline injection,
  forged correlation, Phase 8 forged trace attributes).
- Credentials, made explicit: compose.yaml carries POSTGRES_PASSWORD
  localdev and connection strings in git, and k8s/apply.ps1 parses the
  password back out of compose.yaml to build the Secret - the Secret is
  exactly as secret as the repository. DECISION: these are LOCAL-ONLY
  development credentials; they must never be pointed at a real database,
  and the apply.ps1 parse-path must be replaced with a real secret source
  before any non-local target exists. Writing that down is what stops
  someone reusing apply.ps1 against a shared cluster later. The plan's
  "never in compose files or git" applies the moment there is a real
  secret to keep.
- Migrator role, narrowed to match Phase 2's design: the migrator no
  longer connects as postgres (superuser); infra/postgres-init.sql adds
  ccw_migrator (DDL) alongside ccw_app (DML), with default privileges so
  migrator-created objects are DML-granted to ccw_app. Both topologies
  start from FRESH volumes for this change (objects in existing volumes
  are postgres-owned); recorded.
- Exposure mechanics for the final step: kubectl port-forward
  --address 0.0.0.0 (or kind extraPortMappings on a rebuilt cluster), on
  the TLS port, as the last commit - reversible by killing the process.

Phase 7 execution record (2026-09-15, part 1 - loopback-bound):

- Sequencing honored: everything below landed and was verified while the
  stack stayed loopback-bound. The non-loopback bind is NOT in this
  commit set; its gate (auth tests green while loopback) now passes on
  both topologies. Explicitly deferred to the remaining work: Calico
  cluster rebuild + default-deny NetworkPolicy + its negative/positive
  test, and the final bind commit.
- IdP: Keycloak 26.2 (container in both topologies, realm from
  infra/keycloak/corewebforms-realm.json, public client + audience
  mapper + tester user; direct access grants enabled for LOCAL test
  automation only - recorded deviation). Bring-up lessons: the keycloak
  image has no curl and its healthcheck must be a bash TCP-connect in
  exec form (a malformed printf HTTP request hangs grep forever);
  the issuer must be pinned via full-URL KC_HOSTNAME - request-derived
  issuers disagree between the internal and published interfaces, and
  pinning the EXTERNAL hostname makes the discovery document point the
  gateway at a URL unreachable from its own container (JWKS fetch
  fails). Pinned to the INTERNAL http://keycloak:8080: tokens minted via
  the published port carry the pinned issuer and validate; interactive
  browser login additionally needs a hosts-file entry (127.0.0.1
  keycloak), documented.
- Gateway hardening journey (each step caught by the auth test, not by
  review): JwtBearer/OpenIdConnect packages must be explicit in the
  csproj (not in the 9.0 container's shared framework);
  RequireHttpsMetadata=false is the recorded internal-plain-HTTP
  deviation; PolicyScheme needed ForwardChallenge=oidc - challenging the
  cookie scheme redirects to /Account/Login, which is not an IdP; the
  OIDC handler defaults to response_type=id_token (implicit), which
  Keycloak 26 rightly refuses - set to code; and the handler
  auto-attempts PAR (Keycloak advertises it), which failed until the
  127.0.0.1 redirect URIs were registered alongside localhost.
- THE REVIEW'S WARNED TRAP MATERIALIZED, live: ForwardedHeadersMiddleware
  with cleared knowns ran BEFORE the rate limiter and rewrote
  Connection.RemoteIpAddress from the client-supplied X-Forwarded-For.
  The XFF-rotation check caught it (rotating the header bought unlimited
  quota: 0 x 429 while a plain request 429'd). Fix: no
  ForwardedHeadersMiddleware at all - X-Forwarded-For is written by hand
  from the untouched socket, X-Forwarded-Proto from the listener scheme,
  and the limiter partitions on the socket forever. The ForwardedHeaders
  "correct internal, wrong once public" guidance is now implemented at
  the only layer that can see the true socket.
- A quieter sibling of the same class: the first rate-limiter wiring
  attached a NAMED policy and a loose global limiter - but
  UseRateLimiter only applies the GlobalLimiter unconditionally; the
  named per-IP policy was dead code and a 600-burst never tripped. The
  per-IP fixed window (500/10s, headroom for Phase 5's single-IP 408 rps
  benchmark) IS the GlobalLimiter now, and the test's re-heat step had
  to learn that a sub-window burst can never trip a 500-limit window.
- Egress hygiene (all verified): X-Instance and X-Gateway-Instance
  stripped at the gateway; /health* 404 on the public port, 200 on the
  management port 8090 (compose healthcheck and k8s probes moved there);
  correlation ids validated (32 hex) or replaced. Suite drift that
  followed, deliberately: 'gateway passes correlation id through
  unchanged' split into valid-passes + junk-replaced (compose suite is
  40 checks now); the X-Instance-based replica-balance and
  gateway-failover sections rewritten to marked-burst + per-container
  log attribution (stronger evidence than response headers anyway);
  test-k8s markers are full 32-hex to survive validation; the suite's
  gateway precheck probes the dashboard because /health 404s by design.
- Migrator role: ccw_migrator (DDL) + ccw_app (DML) in
  infra/postgres-init.sql, migrator connection strings in both
  topologies switched off the postgres superuser, both topologies on
  fresh volumes (recorded cost: the soft-deleted-row ledger restarted
  from zero). The k8s fresh-volume rollout exercised the role
  end-to-end: empty database, initContainer migrations as ccw_migrator,
  apps running as ccw_app.
- Kestrel endpoints env overrides Urls entirely - the main 8080 listener
  vanished when only Https/Management endpoints were declared (explicit
  Http endpoint added); k8s multi-port Services require names on every
  port (the apply error surfaced only as a dead TLS port-forward).
- Environment note, recorded because it cost an hour: after today's
  Docker restarts, IPv6-loopback SYNs to published ports are silently
  dropped (no listener, no RST - WFP debris), so every http://localhost
  URL pays a ~2s HappyEyeballs penalty per request. All scripts now use
  explicit 127.0.0.1 (client-side strings only). Port-forwards still
  pin one backing pod and die silently on rollout (recurring lesson).
- Verification state at this commit: auth 11/11 compose AND 11/11 k8s
  (public reads, 401/302 write rejection, bearer acceptance and
  invalid-token rejection, 429 trips with 105 throttled, XFF rotation
  buys nothing, TLS terminates); k8s battery 27/27 (health gating,
  diversity 2 of 2 pods, correlation to pod logs, DB-loss readiness
  still intact); compose suite 40/40 + smoke 11/11 on the final image;
  both exposure checks all-pass (compose default shape loopback-only,
  k8s ClusterIP-only). Facts and the local bare suite were not rerun:
  this phase changed gateway code and compose/k8s wiring only, no
  service code and no bare-topology path.
- Frontend honoring of X-Forwarded-Proto: no current consumer (the
  pages emit relative URLs); recorded as additive when a page needs
  absolute URLs.
- REMAINING for Phase 7 completion (next session, in order): (1) rebuild
  the kind cluster with a policy-capable CNI (kindnet does not enforce
  NetworkPolicy), default-deny ingress + explicit allows, negative test
  with positive control (exec from a pod that must be denied, same call
  from frontend's pod succeeds); (2) the final non-loopback bind on TLS
  as the last commit, gated on the auth suite above; (3) optionally the
  interactive browser login walkthrough (hosts entry + cookie flow).

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
