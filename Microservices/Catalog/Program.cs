using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using Catalog;
using Inventory.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

    var builder = WebApplication.CreateBuilder(args);
    builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));
    builder.Services.AddAppTelemetry("catalog").AddOtlpExporting(builder.Configuration);

var urls = builder.Configuration["Urls"] ?? "http://localhost:8094";
builder.WebHost.UseUrls(urls);

var dbPathSetting = builder.Configuration["Database:Path"];
var dbPath = !string.IsNullOrEmpty(dbPathSetting)
    ? dbPathSetting
    : Path.Combine(builder.Environment.ContentRootPath,
        builder.Configuration["Database:RelativePath"] ?? "App_Data/inventory.db");

var provider = builder.Configuration["Database:Provider"] ?? "sqlite";
var usePostgres = string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase);
if (usePostgres)
{
    var connectionString = builder.Configuration["Database:ConnectionString"];
    if (string.IsNullOrEmpty(connectionString))
        throw new InvalidOperationException("Database:ConnectionString is required when Database:Provider is postgres.");
    builder.Services.AddScoped<AppDbContext>(_ => new PostgresAppDbContext(connectionString));
}
else
{
    builder.Services.AddScoped<AppDbContext>(_ => new SqliteAppDbContext(dbPath));
}

var runAsMigrator = args.Contains("--migrate");
var migrateOnStartup = runAsMigrator || builder.Configuration.GetValue<bool?>("Database:Migrate") == true;

// Readiness signal without a request: same SELECT 1 rule as the endpoint
// (real bytes on the socket), on a timer, into the ccw_readiness gauge.
builder.Services.AddHostedService(sp => new ReadinessMonitor(
    sp, sp.GetRequiredService<ILogger<ReadinessMonitor>>(),
    async (p, ct) =>
    {
        using var scope = p.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return true;
    }));

var app = builder.Build();

var instanceId = Environment.MachineName + ":" + Process.GetCurrentProcess().Id;

app.Use(async (context, next) =>
{
    var incoming = context.Request.Headers[CorrelationHeader.Name].ToString();
    var correlationId = string.IsNullOrWhiteSpace(incoming)
        ? Guid.NewGuid().ToString("N")
        : incoming;
    context.Items[CorrelationHeader.Name] = correlationId;
    context.Response.Headers[CorrelationHeader.Name] = correlationId;
    context.Response.Headers["X-Instance"] = instanceId;
    var stopwatch = Stopwatch.StartNew();
    try
    {
        await next(context);
    }
    finally
    {
        app.Logger.LogInformation("HTTP {Method} {Path} -> {StatusCode} in {Elapsed}ms corr={CorrelationId}",
            context.Request.Method, context.Request.Path, context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds, correlationId);
    }
});

if (migrateOnStartup)
{
    using (var scope = app.Services.CreateScope())
    {
        if (!usePostgres)
        {
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir))
                Directory.CreateDirectory(dbDir);
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (usePostgres)
        {
            db.Database.OpenConnection();
            db.Database.ExecuteSqlRaw("SELECT pg_advisory_lock(94001);");
            try
            {
                db.Database.Migrate();
            }
            finally
            {
                db.Database.ExecuteSqlRaw("SELECT pg_advisory_unlock(94001);");
                db.Database.CloseConnection();
            }
        }
        else
        {
            db.Database.Migrate();
            db.Database.OpenConnection();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            db.Database.CloseConnection();
        }
        if (!db.Products.Any())
            new DbSeeder(db).Seed();
    }

    if (runAsMigrator)
        return;
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (AppDbContext db) =>
{
    // Real SQL, not CanConnectAsync: CanConnect can borrow a warm pooled
    // connection and validate nothing on the wire, so a silently dead
    // connection (blackholed TCP) looks ready for minutes. SELECT 1 forces
    // bytes onto the socket, so readiness flips on real connection loss.
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ready" });
    }
    catch
    {
        return Results.Problem(statusCode: 503, title: "database unreachable");
    }
});

app.MapGet("/api/products", (AppDbContext db, bool includeDeleted = false) =>
{
    var query = db.Products.AsNoTracking().AsQueryable();
    if (!includeDeleted)
        query = query.Where(p => !p.IsDeleted);
    return Results.Ok(query.OrderBy(p => p.Id).ToList().Select(ToDto).ToList());
});

app.MapGet("/api/products/{id:int}", (int id, AppDbContext db) =>
{
    var product = db.Products.AsNoTracking().FirstOrDefault(p => p.Id == id);
    return product == null ? Results.NotFound() : Results.Ok(ToDto(product));
});

app.MapPost("/api/products", (ProductDto dto, AppDbContext db) =>
{
    var invalid = ValidateDto(dto);
    if (invalid != null) return invalid;

    var product = new Product
    {
        Name = dto.Name,
        Category = dto.Category,
        Price = dto.Price,
        Stock = dto.Stock,
        IsActive = dto.IsActive,
        AddedDate = DateTime.UtcNow
    };
    db.Products.Add(product);
    db.SaveChanges();
    return Results.Created($"/api/products/{product.Id}", product.Id);
});

app.MapPut("/api/products/{id:int}", (int id, ProductDto dto, AppDbContext db) =>
{
    var invalid = ValidateDto(dto);
    if (invalid != null) return invalid;

    var existing = db.Products.Find(id);
    if (existing == null) return Results.NotFound();

    existing.Name = dto.Name;
    existing.Category = dto.Category;
    existing.Price = dto.Price;
    existing.Stock = dto.Stock;
    existing.IsActive = dto.IsActive;
    if (existing.Stock <= 0)
        existing.IsActive = false;
    db.SaveChanges();
    return Results.NoContent();
});

app.MapDelete("/api/products/{id:int}", (int id, AppDbContext db) =>
{
    var existing = db.Products.Find(id);
    if (existing == null) return Results.NotFound();

    existing.IsDeleted = true;
    existing.IsActive = false;
    db.SaveChanges();
    return Results.NoContent();
});

app.MapPost("/api/products/reserve", (ReserveStockRequest request, AppDbContext db) =>
{
    var invalid = ValidateStockRequest(request.ReservationKey, "ReservationKey", request.Items);
    if (invalid != null) return invalid;

    if (db.ReservationKeys.Find(request.ReservationKey) != null)
        return Results.Ok(new { reserved = true, replayed = true });

    var items = StockRules.OrderItemsForLock(request.Items);
    try
    {
        using var tx = db.Database.BeginTransaction();
        var products = LoadProductsForUpdate(db, items);
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                return StockRuleError(ApiErrorCodes.ProductNotFound,
                    string.Format("Product ID {0} not found.", item.ProductId));
            if (product.Stock < item.Quantity)
                return StockRuleError(ApiErrorCodes.InsufficientStock,
                    string.Format("Insufficient stock for product ID {0}: requested {1}, available {2}", item.ProductId, item.Quantity, product.Stock));
            product.Stock -= item.Quantity;
            if (product.Stock <= 0) product.IsActive = false;
        }
        db.ReservationKeys.Add(new ReservationKey { Key = request.ReservationKey, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        tx.Commit();
        return Results.Ok(new { reserved = true, replayed = false });
    }
    catch (DbUpdateException ex) when (StockRules.IsUniqueViolation(ex, "ReservationKeys"))
    {
        return Results.Ok(new { reserved = true, replayed = true });
    }
    catch (Exception ex) when (StockRules.IsTransientLock(ex))
    {
        StockRules.CountLockTimeout(ex, "reserve");
        return TransientLockError();
    }
});

app.MapPost("/api/products/release", (ReleaseStockRequest request, AppDbContext db) =>
{
    var invalid = ValidateStockRequest(request.ReleaseKey, "ReleaseKey", request.Items);
    if (invalid != null) return invalid;

    if (db.ReleaseKeys.Find(request.ReleaseKey) != null)
        return Results.Ok(new { released = true, replayed = true });

    var items = StockRules.OrderItemsForLock(request.Items);
    try
    {
        using var tx = db.Database.BeginTransaction();
        var products = LoadProductsForUpdate(db, items);
        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
                continue;
            product.Stock += item.Quantity;
            if (product.Stock > 0 && !product.IsDeleted)
                product.IsActive = true;
        }
        db.ReleaseKeys.Add(new ReleaseKey { Key = request.ReleaseKey, CreatedAt = DateTime.UtcNow });
        db.SaveChanges();
        tx.Commit();
        return Results.Ok(new { released = true, replayed = false });
    }
    catch (DbUpdateException ex) when (StockRules.IsUniqueViolation(ex, "ReleaseKeys"))
    {
        return Results.Ok(new { released = true, replayed = true });
    }
    catch (Exception ex) when (StockRules.IsTransientLock(ex))
    {
        StockRules.CountLockTimeout(ex, "release");
        return TransientLockError();
    }
});

app.Run();

static ProductDto ToDto(Product p) => new()
{
    Id = p.Id,
    Name = p.Name,
    Category = p.Category,
    Price = p.Price,
    Stock = p.Stock,
    IsActive = p.IsActive,
    IsDeleted = p.IsDeleted,
    AddedDate = p.AddedDate
};

static IResult? ValidateDto(ProductDto dto)
{
    var results = new List<ValidationResult>();
    if (!Validator.TryValidateObject(dto, new ValidationContext(dto), results, true))
        return Results.Json(
            new ApiErrorResponse
            {
                ErrorCode = "Validation",
                Message = string.Join("; ", results.Select(r => r.ErrorMessage))
            },
            statusCode: 400);
    return null;
}

static IResult StockRuleError(string errorCode, string message)
{
    return Results.Json(new ApiErrorResponse { ErrorCode = errorCode, Message = message }, statusCode: 409);
}

    static Dictionary<int, Product> LoadProductsForUpdate(AppDbContext db, List<StockItemDto> items)
    {
        var ids = StockRules.OrderedLockIds(items);
        if (!db.Database.IsNpgsql())
            return db.Products.Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id);
        var idList = string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        return db.Products
        .FromSqlRaw($"SELECT * FROM \"Products\" WHERE \"Id\" IN ({idList}) ORDER BY \"Id\" FOR UPDATE")
            .ToDictionary(p => p.Id);
    }

static IResult TransientLockError()
    => Results.Json(new ApiErrorResponse
    {
        ErrorCode = ApiErrorCodes.LockTimeout,
        Message = "The operation could not complete because the database was busy; retry the request."
    }, statusCode: 503);

static IResult? ValidateStockRequest(string? key, string keyName, List<StockItemDto> items)
{
    var messages = new List<string>();
    if (string.IsNullOrWhiteSpace(key))
        messages.Add(string.Format("The {0} field is required.", keyName));
    if (items.Count == 0)
        messages.Add("At least one stock item is required.");
    foreach (var item in items)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(item, new ValidationContext(item), results, true))
            messages.AddRange(results.Select(r => r.ErrorMessage!));
    }
    if (messages.Count > 0)
        return Results.Json(
            new ApiErrorResponse
            {
                ErrorCode = ApiErrorCodes.Validation,
                Message = string.Join("; ", messages)
            },
            statusCode: 400);
    return null;
}
