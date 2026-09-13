using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using Inventory.Contracts;
using Microsoft.EntityFrameworkCore;
using Orders;
using Orders.Logging;

var builder = WebApplication.CreateBuilder(args);

var urls = builder.Configuration["Urls"] ?? "http://localhost:8095";
builder.WebHost.UseUrls(urls);

var dbPathSetting = builder.Configuration["Database:Path"];
var dbPath = !string.IsNullOrEmpty(dbPathSetting)
    ? dbPathSetting
    : Path.Combine(builder.Environment.ContentRootPath,
        builder.Configuration["Database:RelativePath"] ?? "App_Data/inventory.db");

var catalogBaseUrl = builder.Configuration["Services:Catalog:BaseUrl"] ?? "http://localhost:8094";

var logPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "logs", "app.log");
Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
builder.Logging.AddProvider(new FileLoggerProvider(logPath));

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
    if (!db.Orders.Any())
        new DbSeeder(db).Seed();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/orders", (bool includeDeleted, string? status, int skip, int take, AppDbContext db) =>
{
    var query = db.Orders.Include(o => o.Items).AsNoTracking().AsQueryable();
    if (!includeDeleted)
        query = query.Where(o => !o.IsDeleted);
    if (!string.IsNullOrEmpty(status))
        query = query.Where(o => o.Status == status);
    var total = query.Count();
    var ordered = query.OrderByDescending(o => o.OrderDate);
    var rows = take > 0 ? ordered.Skip(skip).Take(take).ToList() : ordered.Skip(skip).ToList();
    return Results.Ok(new PagedResult<OrderDto> { Items = rows.Select(ToDto).ToList(), TotalCount = total });
});

app.MapGet("/api/orders/recent/{count:int}", (int count, AppDbContext db) =>
{
    var rows = db.Orders.Include(o => o.Items).AsNoTracking()
        .Where(o => !o.IsDeleted)
        .OrderByDescending(o => o.OrderDate)
        .Take(count).ToList();
    return Results.Ok(rows.Select(ToDto).ToList());
});

app.MapGet("/api/orders/{id:int}", (int id, AppDbContext db) =>
{
    var order = db.Orders.Include(o => o.Items).AsNoTracking().FirstOrDefault(o => o.Id == id);
    return order == null ? Results.NotFound() : Results.Ok(ToDto(order));
});

app.MapGet("/api/orders/{id:int}/items", (int id, AppDbContext db) =>
{
    var items = db.OrderItems.AsNoTracking().Where(i => i.OrderId == id).ToList();
    return Results.Ok(items.Select(ToItemDto).ToList());
});

app.MapPost("/api/orders", (OrderDto dto, HttpContext http, AppDbContext db) =>
{
    var invalid = ValidateOrder(dto);
    if (invalid != null) return invalid;

    var correlationId = http.Items[CorrelationHeader.Name] as string;
    var reservationKey = Guid.NewGuid().ToString("N");
    var stockItems = dto.Items
        .Select(i => new StockItemDto { ProductId = i.ProductId, Quantity = i.Quantity })
        .ToList();

    try
    {
        CatalogClient.Reserve(catalogBaseUrl, new ReserveStockRequest
        {
            ReservationKey = reservationKey,
            Items = stockItems
        }, correlationId);
    }
    catch (CatalogRuleException ex)
    {
        return Results.Json(new ApiErrorResponse { ErrorCode = ex.ErrorCode, Message = ex.Message }, statusCode: 409);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        app.Logger.LogError(ex, "Reserve failed after retries for reservation {ReservationKey}", reservationKey);
        return Results.Json(
            new ApiErrorResponse { ErrorCode = "CatalogUnavailable", Message = "Catalog is unavailable; the order was not created." },
            statusCode: 502);
    }

    var order = ToEntity(dto);
    try
    {
        order.Total = order.Items.Sum(i => i.Quantity * i.UnitPrice);
        db.Orders.Add(order);
        db.SaveChanges();
    }
    catch (Exception ex)
    {
        try
        {
            CatalogClient.Release(catalogBaseUrl, new ReleaseStockRequest
            {
                ReleaseKey = "reserve:" + reservationKey,
                Items = stockItems
            }, correlationId);
        }
        catch (Exception releaseEx)
        {
            app.Logger.LogError(releaseEx, "Compensation release failed for reservation {ReservationKey}; manual reconciliation required", reservationKey);
        }
        app.Logger.LogError(ex, "Order insert failed after successful reserve for reservation {ReservationKey}", reservationKey);
        return Results.Json(
            new ApiErrorResponse { ErrorCode = "OrderWriteFailed", Message = "The order could not be written; reserved stock was released." },
            statusCode: 500);
    }

    return Results.Created($"/api/orders/{order.Id}", order.Id);
});

app.MapPut("/api/orders/{id:int}/status", (int id, UpdateStatusRequest request, AppDbContext db) =>
{
    var results = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), results, true))
        return Results.Json(
            new ApiErrorResponse
            {
                ErrorCode = ApiErrorCodes.Validation,
                Message = string.Join("; ", results.Select(r => r.ErrorMessage))
            },
            statusCode: 400);

    var existing = db.Orders.Find(id);
    if (existing == null || existing.IsDeleted)
        return Results.NotFound();

    existing.Status = request.Status;
    existing.Priority = request.Priority;
    db.SaveChanges();
    return Results.NoContent();
});

app.MapDelete("/api/orders/{id:int}", (int id, HttpContext http, AppDbContext db) =>
{
    var order = db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == id);
    if (order == null || order.IsDeleted)
        return Results.NotFound();

    order.IsDeleted = true;
    db.SaveChanges();

    try
    {
        CatalogClient.Release(catalogBaseUrl, new ReleaseStockRequest
        {
            ReleaseKey = "order:" + order.Id,
            Items = order.Items
                .Select(i => new StockItemDto { ProductId = i.ProductId, Quantity = i.Quantity })
                .ToList()
        }, http.Items[CorrelationHeader.Name] as string);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Release after delete failed for order {OrderId}; manual reconciliation required", order.Id);
    }

    return Results.NoContent();
});

app.Run();

static OrderDto ToDto(Order o) => new()
{
    Id = o.Id,
    CustomerName = o.CustomerName,
    CustomerEmail = o.CustomerEmail,
    OrderDate = o.OrderDate,
    DeliveryDate = o.DeliveryDate,
    Status = o.Status,
    Priority = o.Priority,
    Extras = o.Extras,
    Total = o.Total,
    IsDeleted = o.IsDeleted,
    Items = o.Items.Select(ToItemDto).ToList()
};

static OrderItemDto ToItemDto(OrderItem i) => new()
{
    ProductId = i.ProductId,
    ProductName = i.ProductName,
    Quantity = i.Quantity,
    UnitPrice = i.UnitPrice
};

static Order ToEntity(OrderDto dto) => new()
{
    CustomerName = dto.CustomerName,
    CustomerEmail = dto.CustomerEmail,
    OrderDate = dto.OrderDate,
    DeliveryDate = dto.DeliveryDate,
    Status = dto.Status,
    Priority = dto.Priority,
    Extras = dto.Extras,
    Total = dto.Total,
    IsDeleted = dto.IsDeleted,
    Items = dto.Items.Select(i => new OrderItem
    {
        ProductId = i.ProductId,
        ProductName = i.ProductName,
        Quantity = i.Quantity,
        UnitPrice = i.UnitPrice
    }).ToList()
};

static IResult? ValidateOrder(OrderDto dto)
{
    var messages = new List<string>();
    if (dto.Items.Count == 0)
        messages.Add("Order must contain at least one item.");

    var entity = ToEntity(dto);
    var results = new List<ValidationResult>();
    if (!Validator.TryValidateObject(entity, new ValidationContext(entity), results, true))
        messages.AddRange(results.Select(r => r.ErrorMessage!));
    foreach (var item in entity.Items)
    {
        results.Clear();
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
