# Phase 3: Data Layer Migration

Upgrade EF Core from 3.1 to 9.0.17 and fix API breaking changes.

**Reference commit**: `e9cf9ed`

## Steps

### 1. Upgrade EF Core packages

**Before** (`LegacyWebForms.csproj`):
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="3.1.32" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="3.1.32" />
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.6" />
```

**After** (`CoreWebForms.csproj`):
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.17" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.17" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.17">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

`SQLitePCLRaw.bundle_e_sqlite3` is no longer needed — .NET 9 includes the native SQLite library. The `Design` package is required to generate migrations with the EF CLI (`dotnet ef`).

### 2. Fix EF Core ValueConverter breaking change

EF Core 9 no longer supports implicit lambda converters in `HasConversion()`. Use explicit `ValueConverter<,>`.

**Before** (`AppDbContext.cs` — EF Core 3.1):
```csharp
builder.Property(o => o.Extras)
    .HasConversion(
        v => string.Join("|", v),
        v => v.Split('|').ToList());
```

**After** (`AppDbContext.cs` — EF Core 9, JSON serialization):
```csharp
e.Property(o => o.Extras).HasConversion(
    new ValueConverter<List<string>, string>(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()));
```

Key changes:
- Wrap in `new ValueConverter<TModel, TProvider>(...)` (EF Core 9 dropped implicit lambda converters)
- Serialize as JSON instead of a pipe-delimited string — values containing `|` would otherwise corrupt the column
- Pass `JsonSerializerOptions` explicitly: the converter expression compiles into an expression tree, which cannot use optional arguments

### 3. Review all HasConversion calls

Search for all `HasConversion` usage and verify each converter:

```bash
grep -rn "HasConversion" Data/
```

Common patterns to check:
- Bool-to-int conversions (`HasConversion<int>()`) — usually still work
- String-to-list conversions — need explicit `ValueConverter<,>`
- Enum-to-string conversions — usually still work

### 4. Return nullable from lookups

With nullable reference types enabled, lookup methods (`GetById`) return `null` when a row is missing instead of suppressing it with `!`. Keep the result nullable so callers are forced to check:

```csharp
// Repository / service
public Product? GetById(int id)
    => db.Products.AsNoTracking().FirstOrDefault(p => p.Id == id);

// Caller (every page handler already null-checks)
var p = _products.GetById(id);
if (p == null) return;
```

### 5. EF migrations (replaces `EnsureCreated`)

The schema is now managed by EF Core migrations instead of `EnsureCreated()`, so it can evolve. The context has no parameterless constructor and isn't DI-registered, so the EF CLI needs a design-time factory:

```csharp
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "design.db");
        return new AppDbContext(dbPath);
    }
}
```

Generate the baseline migration, then switch the seeder from `EnsureCreated` to `Migrate`:

```bash
dotnet ef migrations add InitialCreate --project CoreWebForms.csproj
```

```csharp
// AppData.EnsureDatabase — was db.Database.EnsureCreated()
db.Database.Migrate();
if (!db.Products.Any()) new DbSeeder(db).Seed();
```

> Migrating an existing `EnsureCreated` database: it lacks the `__EFMigrationsHistory` table, so `Migrate()` will try to re-create tables and fail. Delete the DB file (it re-seeds automatically) or stamp the history manually.

## Verification

```bash
dotnet build
# Run the app: the InitialCreate migration creates the schema and seeds on first start
```
