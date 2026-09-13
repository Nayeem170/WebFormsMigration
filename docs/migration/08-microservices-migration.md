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
6. Remove product CRUD from the monolith; order-driven stock mutation stays in `OrderService` until Phase 4.
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
 - Rollback means flip the Phase 1 config switch back to in-process implementations and stop the services.

During Phase 5 parity verification only, the shared-database smoke tests exercise both `CoreWebForms` and `Microservices/Frontend` against the same repo-root-anchored database path.

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

For the split phase, start Catalog first, then Orders, then the UI.

During Phase 5 parity verification, reseed the shared database between tree runs and run the same smoke tests against both `CoreWebForms` and `Microservices/Frontend`.

### Local ports and base URLs

Use fixed localhost endpoints for the local stack.

- `Frontend` -> `http://localhost:8081`
- `CoreWebForms` -> `http://localhost:8082`
- `Catalog` -> `http://localhost:8091`
- `Orders` -> `http://localhost:8092`

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
