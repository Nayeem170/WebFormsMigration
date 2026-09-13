using System.ComponentModel.DataAnnotations;
using Catalog;
using Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var urls = builder.Configuration["Urls"] ?? "http://localhost:8094";
builder.WebHost.UseUrls(urls);

var dbPathSetting = builder.Configuration["Database:Path"];
var dbPath = !string.IsNullOrEmpty(dbPathSetting)
    ? dbPathSetting
    : Path.Combine(builder.Environment.ContentRootPath,
        builder.Configuration["Database:RelativePath"] ?? "App_Data/inventory.db");

builder.Services.AddScoped<AppDbContext>(_ => new AppDbContext(dbPath));

var app = builder.Build();

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
