using System;
using System.Collections.Generic;
using System.Linq;

namespace LegacyWebForms
{
    internal class DbSeeder
    {
        private readonly AppDbContext _db;

        public DbSeeder(AppDbContext db) => _db = db;

        public void Seed()
        {
            SeedProducts();
            SeedOrders();
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

        private void SeedOrders()
        {
            var products = _db.Products.OrderBy(p => p.Id).ToList();
            var orders = new List<Order>
            {
                new Order { CustomerName = "Alice Johnson", CustomerEmail = "alice@example.com", OrderDate = new DateTime(2024, 5, 1), DeliveryDate = new DateTime(2024, 5, 7), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string> { "Gift wrap" }, Items = new List<OrderItem> { new OrderItem { ProductId = products[0].Id, ProductName = products[0].Name, Quantity = 2, UnitPrice = products[0].Price } } },
                new Order { CustomerName = "Bob Smith", CustomerEmail = "bob@example.com", OrderDate = new DateTime(2024, 5, 3), DeliveryDate = new DateTime(2024, 5, 10), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.Low, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = products[7].Id, ProductName = products[7].Name, Quantity = 1, UnitPrice = products[7].Price } } },
                new Order { CustomerName = "Carol White", CustomerEmail = "carol@example.com", OrderDate = new DateTime(2024, 5, 10), DeliveryDate = new DateTime(2024, 5, 17), Status = AppConstants.OrderStatus.Shipped, Priority = AppConstants.OrderPriority.High, Extras = new List<string> { "Express delivery" }, Items = new List<OrderItem> { new OrderItem { ProductId = products[1].Id, ProductName = products[1].Name, Quantity = 1, UnitPrice = products[1].Price } } },
                new Order { CustomerName = "Dave Lee", CustomerEmail = "dave@example.com", OrderDate = new DateTime(2024, 5, 12), DeliveryDate = new DateTime(2024, 5, 20), Status = AppConstants.OrderStatus.Processing, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = products[4].Id, ProductName = products[4].Name, Quantity = 3, UnitPrice = products[4].Price } } },
                new Order { CustomerName = "Eve Davis", CustomerEmail = "eve@example.com", OrderDate = new DateTime(2024, 5, 14), DeliveryDate = new DateTime(2024, 5, 22), Status = AppConstants.OrderStatus.Pending, Priority = AppConstants.OrderPriority.Low, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = products[10].Id, ProductName = products[10].Name, Quantity = 1, UnitPrice = products[10].Price } } },
                new Order { CustomerName = "Frank Miller", CustomerEmail = "frank@example.com", OrderDate = new DateTime(2024, 5, 15), DeliveryDate = new DateTime(2024, 5, 23), Status = AppConstants.OrderStatus.Pending, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string> { "Insurance" }, Items = new List<OrderItem> { new OrderItem { ProductId = products[0].Id, ProductName = products[0].Name, Quantity = 1, UnitPrice = products[0].Price } } },
                new Order { CustomerName = "Grace Kim", CustomerEmail = "grace@example.com", OrderDate = new DateTime(2024, 5, 16), DeliveryDate = new DateTime(2024, 5, 24), Status = AppConstants.OrderStatus.Pending, Priority = AppConstants.OrderPriority.High, Extras = new List<string> { "Gift wrap", "Express delivery" }, Items = new List<OrderItem> { new OrderItem { ProductId = products[10].Id, ProductName = products[10].Name, Quantity = 1, UnitPrice = products[10].Price }, new OrderItem { ProductId = products[11].Id, ProductName = products[11].Name, Quantity = 3, UnitPrice = products[11].Price } } },
                new Order { CustomerName = "Henry Patel", CustomerEmail = "henry@example.com", OrderDate = new DateTime(2024, 5, 18), DeliveryDate = new DateTime(2024, 5, 26), Status = AppConstants.OrderStatus.Processing, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = products[4].Id, ProductName = products[4].Name, Quantity = 1, UnitPrice = products[4].Price }, new OrderItem { ProductId = products[7].Id, ProductName = products[7].Name, Quantity = 1, UnitPrice = products[7].Price } } },
                new Order { CustomerName = "Iris Chen", CustomerEmail = "iris@example.com", OrderDate = new DateTime(2024, 5, 20), DeliveryDate = new DateTime(2024, 5, 28), Status = AppConstants.OrderStatus.Shipped, Priority = AppConstants.OrderPriority.Low, Extras = new List<string> { "Insurance" }, Items = new List<OrderItem> { new OrderItem { ProductId = products[2].Id, ProductName = products[2].Name, Quantity = 1, UnitPrice = products[2].Price } } },
                new Order { CustomerName = "Jack Brown", CustomerEmail = "jack@example.com", OrderDate = new DateTime(2024, 5, 22), DeliveryDate = new DateTime(2024, 5, 30), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = products[1].Id, ProductName = products[1].Name, Quantity = 1, UnitPrice = products[1].Price } } },
                new Order { CustomerName = "Karen Novak", CustomerEmail = "karen@example.com", OrderDate = new DateTime(2024, 5, 25), DeliveryDate = new DateTime(2024, 6, 2), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.High, Extras = new List<string> { "Gift wrap", "Insurance" }, Items = new List<OrderItem> { new OrderItem { ProductId = products[7].Id, ProductName = products[7].Name, Quantity = 1, UnitPrice = products[7].Price }, new OrderItem { ProductId = products[11].Id, ProductName = products[11].Name, Quantity = 3, UnitPrice = products[11].Price } } },
                new Order { CustomerName = "Leo Garcia", CustomerEmail = "leo@example.com", OrderDate = new DateTime(2024, 5, 28), DeliveryDate = new DateTime(2024, 6, 5), Status = AppConstants.OrderStatus.Processing, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = products[0].Id, ProductName = products[0].Name, Quantity = 1, UnitPrice = products[0].Price } } }
            };

            foreach (var o in orders)
                o.Total = o.Items.Sum(i => i.Quantity * i.UnitPrice);

            _db.Orders.AddRange(orders);
            _db.SaveChanges();

            foreach (var item in orders.SelectMany(o => o.Items))
            {
                var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product != null)
                {
                    product.Stock -= item.Quantity;
                    if (product.Stock <= 0) product.IsActive = false;
                }
            }
            _db.SaveChanges();
        }
    }
}
