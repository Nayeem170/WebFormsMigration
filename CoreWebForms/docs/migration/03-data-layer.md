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
```

`SQLitePCLRaw.bundle_e_sqlite3` is no longer needed — .NET 9 includes the native SQLite library.

### 2. Fix EF Core ValueConverter breaking change

EF Core 9 no longer supports implicit lambda converters in `HasConversion()`. Use explicit `ValueConverter<,>`.

**Before** (`AppDbContext.cs` — EF Core 3.1):
```csharp
builder.Property(o => o.Extras)
    .HasConversion(
        v => string.Join("|", v),
        v => v.Split('|').ToList());
```

**After** (`AppDbContext.cs` — EF Core 9):
```csharp
builder.Property(o => o.Extras)
    .HasConversion(
        new ValueConverter<List<string>, string>(
            v => string.Join("|", v),
            v => v.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList()));
```

Key changes:
- Wrap in `new ValueConverter<TModel, TProvider>(...)`
- Use `StringSplitOptions.RemoveEmptyEntries` to avoid empty strings

### 3. Review all HasConversion calls

Search for all `HasConversion` usage and verify each converter:

```bash
grep -rn "HasConversion" Data/
```

Common patterns to check:
- Bool-to-int conversions (`HasConversion<int>()`) — usually still work
- String-to-list conversions — need explicit `ValueConverter<,>`
- Enum-to-string conversions — usually still work

### 4. Add null-forgiving operators

EF Core 9 is stricter about nullable reference types. Add `!` operators where needed:

```csharp
// Before
var product = await db.Products.FindAsync(id);

// After
var product = await db.Products.FindAsync(id)!;
```

## Verification

```bash
dotnet build
# Check for CS-related warnings about ValueConverter or nullable
```
