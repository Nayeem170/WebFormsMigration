using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Catalog;
using Catalog.Logging;
using Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var urls = builder.Configuration["Urls"] ?? "http://localhost:8094";
builder.WebHost.UseUrls(urls);

var logPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs", "app.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
builder.Logging.AddProvider(new FileLoggerProvider(logPath));

var dbPathSetting = builder.Configuration["Database:Path"];
var dbPath = !string.IsNullOrEmpty(dbPathSetting)
    ? dbPathSetting
    : Path.Combine(builder.Environment.ContentRootPath,
        builder.Configuration["Database:RelativePath"] ?? "App_Data/inventory.db");

builder.Services.AddScoped<AppDbContext>(_ => new AppDbContext(dbPath));

var app = builder.Build();

app.Use(async (context, next) =>
{
    var incoming = context.Request.Headers[CorrelationHeader.Name].ToString();
    var correlationId = string.IsNullOrWhiteSpace(incoming)
        ? Guid.NewGuid().ToString("N")
        : incoming;
    context.Items[CorrelationHeader.Name] = correlationId;
    context.Response.Headers[CorrelationHeader.Name] = correlationId;
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

using (var scope = app.Services.CreateScope())
{
    var dbDir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dbDir))
        Directory.CreateDirectory(dbDir);

    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    db.Database.OpenConnection();
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    db.Database.CloseConnection();
    if (!db.Products.Any())
        new DbSeeder(db).Seed();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/products", (bool includeDeleted, AppDbContext db) =>
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

    using var tx = db.Database.BeginTransaction();
    foreach (var item in request.Items)
    {
        var product = db.Products.Find(item.ProductId);
        if (product == null)
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
});

app.MapPost("/api/products/release", (ReleaseStockRequest request, AppDbContext db) =>
{
    var invalid = ValidateStockRequest(request.ReleaseKey, "ReleaseKey", request.Items);
    if (invalid != null) return invalid;

    if (db.ReleaseKeys.Find(request.ReleaseKey) != null)
        return Results.Ok(new { released = true, replayed = true });

    using var tx = db.Database.BeginTransaction();
    foreach (var item in request.Items)
    {
        var product = db.Products.Find(item.ProductId);
        if (product == null) continue;
        product.Stock += item.Quantity;
        if (product.Stock > 0 && !product.IsDeleted)
            product.IsActive = true;
    }
    db.ReleaseKeys.Add(new ReleaseKey { Key = request.ReleaseKey, CreatedAt = DateTime.UtcNow });
    db.SaveChanges();
    tx.Commit();
    return Results.Ok(new { released = true, replayed = false });
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
