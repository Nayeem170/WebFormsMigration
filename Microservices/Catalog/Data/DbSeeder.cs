using System;
using System.Collections.Generic;
using System.Linq;

namespace Catalog
{
    internal class DbSeeder
    {
        private readonly AppDbContext _db;

        public DbSeeder(AppDbContext db) => _db = db;

        public void Seed()
        {
            SeedProducts();
            ApplySeededOrderDecrement();
        }

        private void SeedProducts()
        {
            _db.Products.AddRange(
                new Product { Name = "Wireless Headphones", Category = "Electronics", Price = 79.99m, Stock = 42, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 1, 15) },
                new Product { Name = "Mechanical Keyboard", Category = "Electronics", Price = 129.99m, Stock = 18, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 2, 3) },
                new Product { Name = "USB-C Hub", Category = "Electronics", Price = 39.99m, Stock = 5, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 3, 10) },
                new Product { Name = "Webcam HD", Category = "Electronics", Price = 59.99m, Stock = 0, IsActive = false, IsDeleted = false, AddedDate = new DateTime(2024, 1, 20) },
                new Product { Name = "Dev T-Shirt (M)", Category = "Clothing", Price = 24.99m, Stock = 75, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 2, 28) },
                new Product { Name = "Dev T-Shirt (L)", Category = "Clothing", Price = 24.99m, Stock = 60, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 2, 28) },
                new Product { Name = "Hoodie (XL)", Category = "Clothing", Price = 49.99m, Stock = 3, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 3, 5) },
                new Product { Name = "Clean Code", Category = "Books", Price = 34.99m, Stock = 20, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 1, 8) },
                new Product { Name = "The Pragmatic Programmer", Category = "Books", Price = 39.99m, Stock = 12, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 1, 8) },
                new Product { Name = "Protein Bar (Box)", Category = "Food", Price = 19.99m, Stock = 200, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 3, 15) },
                new Product { Name = "Ergonomic Mouse", Category = "Electronics", Price = 49.99m, Stock = 30, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 4, 1) },
                new Product { Name = "Standing Desk Mat", Category = "Sports", Price = 44.99m, Stock = 8, IsActive = true, IsDeleted = false, AddedDate = new DateTime(2024, 3, 20) }
            );
            _db.SaveChanges();
        }

        private void ApplySeededOrderDecrement()
        {
            var seededOrderItems = new (int ProductId, int Quantity)[]
            {
                (1, 2), (8, 1), (2, 1), (5, 3), (11, 1), (1, 1), (11, 1), (12, 3), (5, 1), (8, 1), (3, 1), (2, 1), (8, 1), (12, 3), (1, 1)
            };

            foreach (var item in seededOrderItems)
            {
                var product = _db.Products.Find(item.ProductId);
                if (product == null)
                    throw new InvalidOperationException($"Seed decrement references missing product ID {item.ProductId}.");
                product.Stock -= item.Quantity;
                if (product.Stock <= 0) product.IsActive = false;
            }
            _db.SaveChanges();
        }
    }
}
