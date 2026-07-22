# Post-Migration Refactors

Recent good-practice improvements, each on its own `feature/*` branch merged to `develop`.

---

## 1. Typed `Container.DataItem` binding
**Why:** `Eval()` uses runtime reflection and has no type safety; a renamed property fails silently at request time.
**What:** Binding in `Products.aspx` casts `Container.DataItem` to the model. Two-way writes still use `FindControl()` (CoreWebForms has no two-way binding).
```aspx
Text='<%# ((CoreWebForms.Product)Container.DataItem).Name %>'
$<%#: ((CoreWebForms.Product)Container.DataItem).Price.ToString("F2") %>
```

## 2. Nullable `GetById`
**Why:** `FirstOrDefault(...)!` lied to the compiler; callers could dereference a null without warning.
**What:** Return `Product?`/`Order?`, drop `!`. Behavior unchanged — all callers already null-check.
```csharp
public Product? GetById(int id)
    => db.Products.AsNoTracking().FirstOrDefault(p => p.Id == id);
```

## 3. JSON `Order.Extras`
**Why:** Pipe-delimited (`"A|B|C"`) corrupts if a value contains `|`.
**What:** `ValueConverter` now serializes as JSON.
```csharp
new ValueConverter<List<string>, string>(
    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
```
> Options passed explicitly — expression trees can't use optional arguments.

## 4. Externalized config
**Why:** URL and DB path were hardcoded in `Program.cs`.
**What:** Read from `appsettings.json` with env-var override.
```json
{ "Urls": "http://localhost:8081", "Database": { "RelativePath": "App_Data/inventory.db" } }
```
```csharp
var urls = builder.Configuration["Urls"] ?? "http://localhost:8081";
var dbPath = Path.Combine(contentRoot, builder.Configuration["Database:RelativePath"] ?? "App_Data/inventory.db");
```

## 5. EF Core migrations
**Why:** `EnsureCreated()` can't evolve the schema.
**What:** `Database.Migrate()` + `InitialCreate` migration + design-time factory for the EF CLI.
```csharp
db.Database.Migrate();   // was EnsureCreated()
```
```bash
dotnet ef migrations add InitialCreate --project CoreWebForms.csproj
```
