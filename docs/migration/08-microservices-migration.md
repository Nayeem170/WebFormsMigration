# Microservices Migration Plan for CoreWebForms

This plan is corrected for the current repo. It is local-only, scoped to three services, and based on the actual code paths in `CoreWebForms`.

The current `CoreWebForms` app is the source that will be copied into the new `Frontend` service.

## What the code actually does

- `ProductService` owns product reads and writes.
- `OrderService` owns order reads, and it also performs all order writes directly with `AppData.CreateDbContext()`.
- `OrderRepository` is read-only.
- `PlaceOrder()` and `DeleteOrder()` both mutate product stock directly inside `OrderService`.
- `ProductService.Update()` and `OrderService` disagree on the inactive-stock rule (`== 0` vs `<= 0`).
- `OrderItem` already denormalizes `ProductName` and `UnitPrice`, so order history does not need live Catalog reads.
- Dashboard composition is page-level, not a dedicated read-model service: one product fetch plus two order count calls.
- The app has no auth, no tests, no production deployment process, and a single SQLite database today.
- SQLite does not support schemas, so any data split means separate database files.

## Scope

Keep the plan to what this repo can justify now.

In scope:

- Three services: `Frontend`, `Catalog`, and `Orders`
- Local development only
- HTTP between services
- No authn/authz exists yet; services must bind to localhost and must not be exposed off-loopback until auth is added
- Characterization tests
- A short-lived shared database phase before physical split
- A copy step that creates `Microservices/Frontend` as a parallel frontend service
- Local publish-and-test verification

Out of scope for now:

- Gateway
- Outbox
- Event bus
- Circuit breakers
- Read-model service
- Retention and archival policy
- Production rollout plan
- Feature-flag rollout
- Multi-environment deployment strategy

Those can be added later if real traffic or ops needs justify them.

## Correct migration order

The order below fixes the actual seams in the codebase.

### Phase 0 - Characterization tests and fact cleanup

Objective: lock current behavior before any split.

Steps:

1. Spike the test project setup first: `Microsoft.NET.Sdk` + xUnit + `ProjectReference` to the WebForms project.
2. Keep the characterization test project in `Microservices/tests` and reference `CoreWebForms/CoreWebForms.csproj` on purpose.
3. Add characterization tests for `ProductService.Update()`.
4. Add characterization tests for `OrderService.PlaceOrder()`.
5. Add characterization tests for `OrderService.DeleteOrder()`.
6. Add a dashboard smoke test for the current page composition.
7. Run Phase 0 tests serially with a temp `.db` per test and re-initialize `AppData` between tests.
8. Resolve the `IsActive` rule divergence before extraction.
9. Decide the canonical inactive-stock rule; Catalog takes ownership in Phase 3.
10. State that the Phase 0 characterization tests intentionally reference `CoreWebForms/CoreWebForms.csproj` from `Microservices/tests`.

Spike result (2026-09-13, verified on this repo):

- `CoreWebForms.csproj` with a `ProjectReference` to a plain `Microsoft.NET.Sdk` class library builds clean and copies the DLL to the output directory. `EnableRuntimeAspxCompilation` is not broken by the reference.
- The runtime ASPX compiler does not reference the library automatically. Inline `<script runat="server">` code using a library type fails with CS0103 and the page serves a compiler-diagnostics payload instead of rendering.
- Adding `<%@ Assembly Name="AssemblyName" %>` to the page fixes it; the page then compiles and renders the library output correctly.
- Code-behind compiles at build time, so seam types consumed from code-behind need nothing extra. Only ASPX markup and inline script that touch library types need the directive.
- Existing pages are unaffected: root and Products pages served 200 throughout the spike run.

Inactive-stock rule decision (2026-09-13):

- Canonical rule: stock at or below zero deactivates; restock above zero reactivates unless the product is soft-deleted.
- `ProductService.Update()` used `== 0`; it now uses `<= 0` to match `OrderService`. The change is behavior-preserving: `Product.Stock` carries `[Range(0, 999999)]` so validation rejects negative stock before the rule runs, and `PlaceOrder()` throws on insufficient stock before decrementing, so stock never goes below zero through any service path.
- The characterization tests pin this with `Update_WithNegativeStock_ThrowsValidationException` and cover both deactivation paths and the `!IsDeleted` reactivation guard.
- Catalog takes ownership of this rule unchanged in Phase 3.

Facts to preserve in tests:

- `PlaceOrder()` decrements stock and may deactivate products.
- `DeleteOrder()` restores stock and may reactivate products.
- `UpdateStatus()` does not touch stock.
- Order history should remain independent of Catalog because `OrderItem` stores product name and unit price.

Exit criteria:

- Current behavior is documented by tests.
- The `<= 0` versus `== 0` rule is resolved and covered.
- The test project spike works with the current SDK.
- Phase 0 tests are explicitly serial because `AppData` is static.

### Phase 1 - In-process service seam

Objective: introduce interfaces behind the pages while still running one process.

This is the cheapest seam and should come before any new deployable.

Steps:

1. Extract `IProductService` from `ProductService`.
2. Extract `IOrderService` from `OrderService`.
3. Add the missing write-side port for orders, because `OrderRepository` has no write methods.
4. Update `ServiceContainer` to hold interfaces and select the implementation by config.
5. Add a config switch for in-process implementation versus HTTP client implementation.
6. Update pages and controls to depend on interfaces instead of concrete services.
7. Keep the implementations in-process for now.
8. Keep the session payload stable; model types do not move in this plan.
9. Use a shared, stateless `HttpClient` when the config switch selects HTTP; do not new one per page instance.

Important note:

- `OrderWizard` stores `List<OrderItem>` in session, and `Program.cs` registers `List<OrderItem>` under the `CartItems` key with the JSON session serializer.
- Model types do not move to another assembly anywhere in this plan, so the session payload's registered type identity never changes and no compatibility window is needed.
- `UpdateStatus` validates status and priority before the order lookup. Invalid values throw even when the order is missing or deleted; valid values on a deleted order are still silently rejected. This is pinned by tests and pre-lands the Phase 4 semantics, where the HTTP endpoint makes caller input unconstrained.

Exit criteria:

- Pages call interfaces, not concrete service classes.
- There is a clear write-side abstraction for orders.
- The system still runs as one process.
- The service container can switch implementations by configuration.

### Phase 2 - Contracts and build spike

Objective: prove the shared contracts project and build layout are valid before extraction.

Steps:

1. Create a contracts project with DTOs only.
2. Spike `CoreWebForms.Sdk` referencing a plain `Microsoft.NET.Sdk` class library.
3. Verify that the runtime ASPX compiler and the contracts project build together.
4. Keep `Product`, `Order`, and `OrderItem` out of Contracts; the contracts project stays DTO-only.
5. Keep the contracts project free of business logic.

Type ownership decision:

- Contracts holds HTTP DTOs only. `Product`, `Order`, and `OrderItem` stay in the app and out of Contracts.
- Each service keeps its own model classes internally and maps to DTOs at its HTTP boundary. Frontend seam implementations map DTOs back to the app's model types at the client boundary.
- `Program.cs` registers `List<OrderItem>` under the `CartItems` session key; moving `OrderItem` changes the registered type identity and breaks existing session blobs.
- Markup cost of moving types is small but not zero: only `Products.aspx` binds model types inline (9 typed `Container.DataItem` casts to `CoreWebForms.Product`). Keeping models in place means those casts compile with no changes. The other markup files use late-bound `Eval`, which does not reference types at compile time either way.
- The models carry EF persistence concerns (`IsDeleted`, `AddedDate`) that do not belong on a wire contract.
- If markup ever must reference a Contracts type, add `<%@ Assembly Name="..." %>` to that page per the Phase 0 spike result.

Contracts decisions (2026-09-13):

- Contracts lives at `Microservices/Contracts` (assembly `Inventory.Contracts`) with a real `ProjectReference` from `CoreWebForms.csproj`. Nothing consumes it until Phase 3; the standing reference makes the build-layout proof permanent instead of resting on the deleted spike library.
- DTO fields were derived from usage, not mirrored from the entities:
  - `IsDeleted` is on the wire: the Products grid, the wizard product guard, and the orders table all render or filter on it.
  - `AddedDate` is on the wire: `ProductDetail` displays it. The server still owns it on create.
  - `CustomerEmail` and `Extras` are on the wire: the order confirmation reads both back.
  - `OrderDto` embeds `Items`: `OrdersTable` binds `o.Items` directly and only falls back to `GetItems(orderId)` for uncached rows.
- Error contract: `ApiErrorResponse` carries `ErrorCode` and `Message` for the log. All five page-level catch blocks show a fixed generic string and never surface `ex.Message`, so exception-type fidelity across HTTP is unnecessary. The binding rule is client-side: the HTTP arm must throw on any non-success response, never return null or a default, because those catch blocks are the entire user-visible error path.
- `PagedResult<T>` is a transport shape only. The `IOrderService` interface stays frozen as mirrored in Phase 1; a Phase 3 HTTP implementation may serve `Count` and `GetPaged` from one `PagedResult<T>` response internally.

Build concerns to prove early:

- Project reference support must work under `CoreWebForms.Sdk`.
- No shared model types move, so session serialization stays untouched.
- The contracts assembly must not force a bigger refactor than needed.

Exit criteria:

- Contracts build cleanly.
- The SDK reference spike passes.
- The session payload is untouched because no shared types move.

### Phase 3 - Catalog service, shared DB, Catalog as sole writer

Objective: move product behavior out first while keeping rollback simple.

This phase keeps the physical database shared. Catalog becomes the only writer for product data.

Steps:

1. Add the local publish-and-test scripts or commands here, before any split work.
2. Create `Catalog`.
3. Expose HTTP endpoints for product list, lookup, add, edit, delete, and `/health`.
4. Move product reads and writes into Catalog.
5. Move product activation logic into Catalog for product CRUD paths only.
6. Stop calling product CRUD in the monolith: `Services:Products:Mode` defaults to `Http`. The in-process arm stays intact as the rollback path and is deleted only in Phase 8. Order-driven stock mutation stays in `OrderService` until Phase 4.
7. Keep the shared SQLite file at a repo-root-anchored absolute path so both processes open the same file.
8. Route product pages through HTTP behind the interface seam.
9. Run only one process with migrations enabled during the shared-DB phase.
10. Enable WAL once on the shared database file and set `busy_timeout` on every connection; then test writer-vs-writer contention explicitly.

Rules for this phase:

- Phase 4 onward: Orders must not set `Product.IsActive`.
- Phase 4 onward: Catalog owns the inactive-stock rule.
- Catalog owns the full shared-file migration history and seeding path during this phase, even though the domain scope is products.
- The monolith can still share the database with Catalog during this phase.
- This is the last phase where rollback is still cheap.
- The shared database phase is short-lived and must be tested for SQLITE_BUSY contention.
- Only `Catalog` runs `Migrate()` during the shared-database phase; the monolith starts with migrations disabled.
- The current seeding path moves with migrations in Phase 3, then splits cleanly in Phase 6.
- `DbSeeder` is internal, so Catalog needs a visibility change or `InternalsVisibleTo` if it reuses the current seeding code.
- The `!db.Products.Any()` gate seeds orders too; replace it when the paths split in Phase 6.

Exit criteria:

- Product pages use Catalog through HTTP.
- Catalog is the only writer for product CRUD state.
- The monolith can still start against the shared database after Catalog has migrated and seeded it.
- The shared DB still allows a rollback to the monolith if needed.
- Source-run and published-local smoke tests pass for the product flow.
- Rollback means flip `Services:Products:Mode` back to `InProcess` and stop the services.

During Phase 5 parity verification only, the shared-database smoke tests exercise both `CoreWebForms` and `Microservices/Frontend` against the same repo-root-anchored database path.

Phase 3 execution record (2026-09-13):

- Catalog lives at `Microservices/Catalog` (plain `Microsoft.NET.Sdk.Web`) with `AppDbContext`, the three models, `AppConstants`, `DbSeeder`, and all three migration files copied in and namespace-swapped to `Catalog.*`. The migration IDs are unchanged, so the shared file's `__EFMigrationsHistory` stays valid for both processes. Constraint recorded: no schema changes during Phase 3; anything schema-shaped waits for Phase 6.
- Catalog owns `Migrate()`, seeding, and WAL at startup (`PRAGMA journal_mode=WAL` once, on the file). The monolith runs with `Database:Migrate` set to `false` and only opens the file. Both contexts set `Default Timeout=5` in the connection string, which is the per-connection busy timeout.
- Config keys: `Database:Path` (absolute override, used by published runs), `Database:RelativePath` (repo-root-anchored: `../App_Data/inventory.db` from the monolith, `../../App_Data/inventory.db` from Catalog), `Services:Products:Mode` (`Http` by default now) and `Services:Products:BaseUrl`. The Phase 1 blanket `Services:Mode` key became the per-service `Services:Products:Mode` so orders can stay in-process until Phase 4; rollback is flipping the key back to `InProcess`.
- Catalog endpoints: `GET /health`, `GET /api/products?includeDeleted=`, `GET /api/products/{id}` (404 on missing), `POST /api/products` (201, id in body; 400 with `ApiErrorResponse` on validation failure), `PUT /api/products/{id}` (204; applies the canonical `stock <= 0` deactivation rule server-side), `DELETE /api/products/{id}` (204, soft delete).
- `HttpProductService` in the monolith implements `IProductService` over a single shared static `HttpClient`, maps `ProductDto` to `Product` internally, and throws `HttpRequestException` on any non-success (404 on `GetById` maps to `null`, which is a legitimate not-found signal).
- Contention: `scripts/test-contention.ps1` ran 30 concurrent product writes across two Catalog processes against one file: 0 failures. This proves the file-level writer-vs-writer behavior under WAL plus busy timeout. The domain-true variant (monolith order placement racing a Catalog edit) is not reachable by script until Orders has an API in Phase 4; both sides use the identical connection settings, so the file-level proof covers the lock behavior.
- Publish findings: `Content/` and `Scripts/` needed explicit `CopyToPublishDirectory` items (the SDK publishes `Pages/` and `Layout/` on its own). The published monolith must run with its publish directory as the working directory because `Program.cs` maps physical file providers against the content root.
- Verified: Catalog serves all endpoints standalone; product pages render through the HTTP arm in both source-run and published-local configurations against the same shared file; the in-process arm stays green at 21/21.

### Phase 4 - Orders service and missing write port

Objective: move order writes out and cover both cross-boundary stock flows.

This is where the missing write seam is created and used.

Steps:

1. Create `Orders`.
2. Implement the missing order write-side repository or port.
3. Move order create, update status, and delete into Orders.
4. Replace the direct stock mutation in `PlaceOrder()` with a Catalog reserve call that decrements stock immediately.
5. Replace the direct stock mutation in `DeleteOrder()` with a Catalog release call that increments stock immediately.
6. Keep `UpdateStatus()` local to Orders because it does not touch stock.
7. Add a client-generated request id for create reserve calls and an order-scoped idempotency key for release calls.
8. Expose `/health` on Orders.

Important design change:

- `PlaceOrder()` crosses the boundary to Catalog for stock decrement.
- `DeleteOrder()` crosses the boundary to Catalog for stock release and product reactivation.
- `UpdateStatus()` stays inside Orders.

Suggested order flow:

1. UI sends create request to Orders.
2. Orders validates the request.
3. Orders asks Catalog to reserve stock, which decrements stock immediately.
4. Catalog approves or rejects.
5. Orders writes the order only after the reserve call succeeds.
6. If order creation fails, Orders releases the reservation with a bounded inline retry and a separate release key derived from the create request id, then logs for manual reconciliation if retries are exhausted.

Suggested delete flow:

1. UI asks Orders to delete the order.
2. Orders marks the order deleted locally.
3. Orders asks Catalog to release the reserved quantity with an order-scoped idempotency key.
4. Catalog restores stock and reactivates the product only if `Stock > 0 && !IsDeleted`.
5. If the release call fails, Orders retries inline a bounded number of times and then logs for manual reconciliation.

Exit criteria:

- All order writes are behind the new Orders write port.
- Both `PlaceOrder()` and `DeleteOrder()` no longer mutate product rows directly.
- Idempotency is available for reserve and release calls.
- Source-run and published-local smoke tests pass for the order flow.

During Phase 5 parity verification only, the order-flow smoke tests exercise both `CoreWebForms` and `Microservices/Frontend` against the same HTTP services.

Phase 4 execution record (2026-09-13):

- Reserve and release are batch calls: `POST /api/products/reserve` and `POST /api/products/release` each take the whole item list and run all-or-nothing inside Catalog's transaction. The loop is the one from `OrderRepository.PlaceOrder`, moved verbatim, so partial failure rolls back items 1..n-1 exactly as the in-process transaction did. This was the load-bearing decision: per-item reserve over HTTP would have invented a compensation story (partial reserve) that never existed in the monolith. The only compensation case is reserve-succeeded-but-order-write-failed, handled by a release call keyed to the reservation.
- Idempotency storage: `ReservationKeys` (client GUID from Orders) and `ReleaseKeys` (release key strings) live in the shared file and are created by Catalog's migration `AddStockTransactionKeys`, keeping the single-migrator rule. Dedupe runs Catalog-side on both: replaying a reserve or release returns 200 without re-applying. Release keys are order-scoped values (`order:{id}` for deletes, `reserve:{guid}` for create compensation) but stored in Catalog's table, not an Orders-side table: replay safety after an ambiguous timeout requires the executor of the stock change to dedupe, and caller-side keys alone cannot deliver that. The dangling cases (reserve committed but Orders never wrote the order; order deleted but release exhausted its retries) are logged for manual reconciliation and left for Phase 7.
- The pinned exception messages are a wire contract. Catalog generates `Product ID {0} not found.` and `Insufficient stock for product ID {0}: requested {1}, available {2}` in the reserve loop and returns them on the 409 body with machine-readable codes (`ProductNotFound`, `InsufficientStock`); `HttpOrderService` rethrows them as `InvalidOperationException`. Verified deliberately through HTTP (the 21 in-process tests cannot catch a broken HTTP-side message). Validation errors round-trip the same way as `ValidationException` messages, including the joined-message shape of `UpdateStatus` failures.
- Migration ownership: Catalog's migrator carries both new tables; the monolith's and Orders' contexts do not map them. Explicit check run: the in-process monolith arm serves all pages against a Catalog-migrated database containing both key tables. Extra unmapped tables are invisible to EF; rollback needs no schema action.
- Orders lives at `Microservices/Orders` with an orders-only context (Orders + OrderItems; no Product mapping, no migrations folder, never runs `Migrate()`). Catalog must start first: it owns migrations, seeding, and WAL. Config: `Services:Orders:Mode`/`BaseUrl` on the monolith (default `Http` now), `Services:Catalog:BaseUrl` on Orders. Rollback is flipping the Orders mode back to `InProcess`.
- Parity details fixed during verification: Orders validates the client-passed `Total` before computing it from items (matching `OrderService.PlaceOrder`, which never range-checks the computed sum); `GetById` returns soft-deleted orders like the in-process repository; `UpdateStatus` validates before lookup, so an invalid status on a missing order still returns the validation error over HTTP.
- Contention: `scripts/test-order-contention.ps1` places 12 orders through Orders.Api (reserve via Catalog plus local insert, two processes, one file) racing 12 product edits through Catalog: 0 failures, exact stock accounting. The test edits a different product row than the reserved one on purpose: the product edit is a full-object PUT, so read-modify-write on the same row would test last-writer-wins semantics that predate the split, not lock behavior.
- Bounded inline retry: Orders retries reserve/release twice on `HttpRequestException` or timeout (safe because both calls are idempotent); rule rejections (409) are never retried. No correlation ids, no broader retry policy; that is Phase 7.
- Verified: batch atomicity under partial failure, both pinned messages through HTTP, `PlaceOrder`/`DeleteOrder` touching no product rows in the HTTP arm, reserve and release replay, double-delete restoring once, 21/21 on the in-process arm, order-vs-edit contention, and source-run plus published-local three-process order-flow smokes (place, insufficient, delete-restore, pages 200).

### Phase 5 - Copy the frontend

Objective: copy the current app into `Microservices/Frontend` after the service seam is HTTP-based so both trees coexist.

This is a copy, not a move.

Steps:

1. Copy the current `CoreWebForms` app into `Microservices/Frontend`.
2. Update the solution so `Frontend` is the UI service copy.
3. Keep the WebForms shell behavior unchanged in both trees.
4. Point the copied UI at `Catalog` and `Orders` over HTTP.
5. Keep `Frontend` as a plain WebForms shell, not a BFF.

Rules:

- The original `CoreWebForms` app stays live during the coexistence period.
- The copy happens after the in-process seam is already using HTTP implementations.
- `Frontend` is a parallel copy of the source app, not a rename.
- `CoreWebForms` becomes frozen once the copy is verified, meaning no functional changes while it remains runnable as a reference tree until Phase 8.

Exit criteria:

- The copied UI runs from `Microservices/Frontend`.
- The original UI still runs from `CoreWebForms`.
- Both trees behave the same while `Frontend` calls `Catalog` and `Orders` over HTTP.
- The copy is a packaging step, not a new behavior step.
- The parity smoke suite runs only during Phase 5 and only after reseeding the shared database back to the same state.

Phase 5 execution record (2026-09-13):

- The copy needed two depth fixes before it could run, both the same class as the Phase 3 path bug: `Database:RelativePath` became `../../App_Data/inventory.db` (the inherited `../App_Data` resolves to `Microservices/App_Data` from the copy's location) and the Contracts `ProjectReference` became `..\Contracts\Contracts.csproj`. The copy otherwise landed verbatim, including the csproj name and namespaces, so parity tested an unmodified copy. Frontend build outputs go to `Microservices/artifacts` through the copied `Directory.Build.props`, which also avoids output collisions with the frozen tree.
- Database path verified by file evidence, not by startup: the pre-strip copy ran once with both arms forced `InProcess` and `Database:Migrate=false`, served all pages from the repo-root seeded file, and a repo-wide scan found exactly one `inventory.db` and no `Microservices/App_Data`.
- Ports: `CoreWebForms` moved to `8082` before the freeze; `Frontend` keeps `8081`. The stale port table (Catalog 8091 / Orders 8092) was corrected to the real values (8094 / 8095).
- Parity protocol: reseed, full suite against `CoreWebForms` (8082), reseed, full suite against `Frontend` (8081), compare. `scripts/smoke-parity.ps1` asserts observable behavior only (page status codes, seeded fixture names on the home/products/orders pages, place/insufficient/delete-restore through the shared services, stock accounting 38 -> 35 -> 38, the pinned insufficient-stock message) and never prints autoincrement order ids or `AddedDate`, the two fields that are nondeterministic by construction. Two suite corrections came from real page behavior: the products page hides out-of-stock items, and the orders page renders deleted orders struck through with a `Deleted` badge rather than hiding them. Both runs produced byte-identical output.
- Strip commit removed `AppDbContext`, both repositories and their interfaces, `DbSeeder`, the design-time factory, the migrations folder, and the in-process `ProductService`/`OrderService` from Frontend (-1049 lines), dropped all EF package references, and reduced `AppData` to an HTTP-only factory that throws on `Database:Migrate=true` or any `InProcess` mode. The same parity suite against the stripped Frontend reproduced suite A's output exactly.
- Freeze: `CoreWebForms` is frozen as of `a85475f`'s parent state verified by a final full-suite run (identical output, recorded as the last known-good proof that the fallback tree works against the shared database) plus 21/21 on the in-process characterization suite. From Phase 6 on, changes land in Frontend only. `CoreWebForms` stays out of every solution file, as it is today; the freeze is structural, not conventional.
- Operational note: building the monolith (or the test project, which references it) requires the 8082 instance to be stopped, because the running process locks the shared `artifacts/bin` output.

### Phase 6 - Physical database split

Objective: separate the data files once both services are already stable.

This is the rollback boundary.

Steps:

1. Move Catalog to its own SQLite database file.
2. Move Orders to its own SQLite database file.
3. Remove shared transaction assumptions.
4. Move schema migration ownership into each service.
5. Add a data migration step from the shared file to the split files.
6. Update local publish and smoke tests to point at the split files.

Rules:

- SQLite does not support schemas, so use separate `.db` files.
- After the split, rollback is no longer a simple switch because data diverges.
- If rollback is needed later, it must be a controlled data migration, not just code reversal.

Exit criteria:

- Each service owns its own database file.
- The rollback window has intentionally closed.
- Local startup uses the split databases successfully.
- Source-run and published-local smoke tests pass after the split.

Phase 6 execution record (2026-09-14):

- Code split: Catalog owns `App_Data/catalog.db` (Products, ReservationKeys, ReleaseKeys) and Orders owns `App_Data/orders.db` (Orders, OrderItems). Both migration histories started fresh (`20260913160000_InitialCreate` per assembly); the old shared-history IDs could not be subsetted because they belong to an assembly that created tables the other service now owns. Catalog's Order/OrderItem models and its now-unused AppConstants were deleted; Orders gained its own AppConstants, a Migrations folder, and a startup `Migrate` + WAL + seed-if-empty block mirroring Catalog's. Orders also no longer depends on Catalog at startup: its seeder uses hardcoded ProductId/ProductName/UnitPrice literals, legitimate because OrderItem denormalizes them, while Catalog's seeder applies the seeded-order stock decrements as hardcoded (productId, quantity) pairs.
- Split-seed verification came before anything else: the split seeds produced byte-identical API snapshots to the pre-split baseline captured from the last shared-code seed (12 products with exact stocks 38/16/4/0/71/60/3/17/12/200/28/2, 12 orders, 15 items, zero JSON diff), so later failures could not masquerade as split bugs.
- Step 3 (shared transaction assumptions) confirmed as a no-op by search: the Orders context has no Products mapping and no explicit transactions, and Catalog's only transactions are the single-file reserve/release blocks.
- Data migration: `Microservices/tools/DbSplit` creates each target schema via the owning service's context `Migrate()`, then copies rows raw (explicit ids, DateTime/decimal TEXT values preserved) and verifies per-table counts plus cross-file ProductId resolution through an ATTACH. `scripts/split-database.ps1` refuses to run while either service is listening, backs up the shared file tagged with sidecars (`App_Data/inventory.backup-20260914-010505.db`), always works from a copy, and supports `-DryRun`. The dry run on a copy verified first; the real run moved 12/3/3/15/18 rows (Products/ReservationKeys/ReleaseKeys/Orders/OrderItems), all counts MATCH, ProductId resolution ALL, total stock 451, 3 soft-deleted orders carried over honestly.
- Rollback boundary: the last commit where rollback is a config flip is the Phase 6 code-split commit (`feat(services): split database per service`). From the data-migration commit onward, rollback requires restoring the tagged backup or a reverse data migration, and CoreWebForms's frozen in-process fallback no longer sees data written after the split. That is intended per plan and recorded here.
- `scripts/test-contention.ps1` and `scripts/test-order-contention.ps1` were deleted: their scenario (two processes against one file) ceased to exist, and leaving them green would have been vacuous coverage.
- `scripts/publish-local.ps1` now publishes Frontend alongside CoreWebForms, Catalog, and Orders. `scripts/smoke-parity.ps1` needed no path edits because it is HTTP-only; the split paths live in each service's appsettings (`Database:RelativePath` of `../../App_Data/catalog.db` and `../../App_Data/orders.db`).
- Verification: both services start independently against their own file in any order; source-run and published-local smokes are green (38 -> 35 -> 38 stock accounting, pinned insufficient-stock message, struck-through deleted order rendering); 21/21 characterization tests; Frontend required zero file changes (confirmed by diff), satisfying the strip payoff check.

### Phase 7 - Local hardening only

Objective: keep the split stable locally.

Steps:

1. Add correlation IDs to local requests.
2. Add short HTTP timeouts and simple retries.
3. Bind services to localhost only for local testing.
4. Add explicit error handling for service unavailability.
5. Extend the publish-and-test scripts for the split topology.

Keep this small.

Do not add gateway, event bus, outbox, circuit breakers, or production rollout logic unless there is a real need.

Exit criteria:

- The local stack tolerates service restarts cleanly.
- Smoke tests still pass after publishing the services locally.

### Phase 8 - Retire CoreWebForms

Objective: clean up the original tree after `Frontend` proves stable on its own.

Steps:

1. Run the full smoke suite against `Microservices/Frontend` as the active tree.
2. Keep `CoreWebForms` as a frozen reference tree after the copy is verified.
3. Archive or delete `CoreWebForms` after the copy has proven stable.

Exit criteria:

- `Frontend` is the only actively maintained frontend tree.
- `CoreWebForms` is frozen or retired.
- Coexistence has an explicit end.

## Local test workflow

Use this workflow for every milestone.

### Source-run verification

1. Run the current app from source.
2. Run characterization tests.
3. Exercise the UI flows in the browser.
4. Confirm current behavior did not change.

### Local startup order

For the shared-DB phase, start Catalog first, then the UI or monolith against the shared file.

After the Phase 6 split, Catalog and Orders are independent (each owns its file and schema); start them in any order, then the UI.

To reseed canonical fixtures after the split, delete `App_Data/catalog.db`, `App_Data/orders.db`, and their WAL sidecars, then restart both services.

During Phase 5 parity verification, reseed the shared database between tree runs and run the same smoke tests against both `CoreWebForms` and `Microservices/Frontend`.

### Local ports and base URLs

Use fixed localhost endpoints for the local stack.

- `Frontend` -> `http://localhost:8081`
- `CoreWebForms` -> `http://localhost:8082`
- `Catalog` -> `http://localhost:8094`
- `Orders` -> `http://localhost:8095`

Database files after the Phase 6 split:

- Catalog -> `App_Data/catalog.db` (Products, ReservationKeys, ReleaseKeys)
- Orders -> `App_Data/orders.db` (Orders, OrderItems)
- Retired shared file -> `App_Data/inventory.db` (kept as frozen-tree fallback data; tagged backups beside it)

Configure base URLs in `appsettings.Development.json` or environment variables.

### Published local verification

1. Publish the services locally.
2. Start the published binaries.
3. Verify each health endpoint.
4. Point the UI at the published local services.
5. Re-run the smoke tests.
6. Compare logs and error handling against the source-run version.

### Minimal smoke tests

1. Load the dashboard.
2. Add a product.
3. Update product stock.
4. Place an order with enough stock.
5. Place an order with insufficient stock.
6. Delete an order and confirm stock restoration.
7. Refresh the UI and confirm data still loads.

## Local repo shape

Keep the transition aligned to the current repository instead of assuming a greenfield layout.

Current root should evolve toward:

```text
CoreWebForms/ (repo root)
|-- CoreWebForms/ (current app)
|-- Microservices/
    |-- Frontend/
    |-- Catalog/
    |-- Orders/
    |-- Contracts/
    |-- tests/
    |-- Microservices.sln
|-- docs/
```

The existing `LegacyWebForms/` folder and `LegacyWebForms.sln` should stay untouched until the microservice replacement is proven locally. `Microservices.sln` should include `Frontend`, `Catalog`, `Orders`, `Contracts`, and the test projects. `CoreWebForms` stays live until Phase 5 freezes it, while `Frontend` is a copied sibling tree.

## What to keep out of this plan

The following are later concerns, not first-pass requirements:

- Gateway
- Outbox
- Event bus
- Circuit breakers
- Retention and archival policy
- Feature flags for production rollout
- Production traffic cutover
- Read-model service for the dashboard

## Review checklist

- The plan matches the actual write paths in the code.
- The plan treats `OrderRepository` as read-only.
- The plan covers both `PlaceOrder()` and `DeleteOrder()` stock mutations.
- The plan makes Catalog own product activation logic.
- The plan uses characterization tests before the first extraction.
- The plan stays local-only and small enough for this repo.
- The plan closes the rollback window at the database split.
