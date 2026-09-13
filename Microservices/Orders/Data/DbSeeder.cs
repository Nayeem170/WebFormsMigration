using System;
using System.Collections.Generic;
using System.Linq;

namespace Orders
{
    internal class DbSeeder
    {
        private readonly AppDbContext _db;

        public DbSeeder(AppDbContext db) => _db = db;

        public void Seed()
        {
            var orders = new List<Order>
            {
                new Order { CustomerName = "Alice Johnson", CustomerEmail = "alice@example.com", OrderDate = new DateTime(2024, 5, 1), DeliveryDate = new DateTime(2024, 5, 7), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string> { "Gift wrap" }, Items = new List<OrderItem> { new OrderItem { ProductId = 1, ProductName = "Wireless Headphones", Quantity = 2, UnitPrice = 79.99m } } },
                new Order { CustomerName = "Bob Smith", CustomerEmail = "bob@example.com", OrderDate = new DateTime(2024, 5, 3), DeliveryDate = new DateTime(2024, 5, 10), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.Low, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = 8, ProductName = "Clean Code", Quantity = 1, UnitPrice = 34.99m } } },
                new Order { CustomerName = "Carol White", CustomerEmail = "carol@example.com", OrderDate = new DateTime(2024, 5, 10), DeliveryDate = new DateTime(2024, 5, 17), Status = AppConstants.OrderStatus.Shipped, Priority = AppConstants.OrderPriority.High, Extras = new List<string> { "Express delivery" }, Items = new List<OrderItem> { new OrderItem { ProductId = 2, ProductName = "Mechanical Keyboard", Quantity = 1, UnitPrice = 129.99m } } },
                new Order { CustomerName = "Dave Lee", CustomerEmail = "dave@example.com", OrderDate = new DateTime(2024, 5, 12), DeliveryDate = new DateTime(2024, 5, 20), Status = AppConstants.OrderStatus.Processing, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = 5, ProductName = "Dev T-Shirt (M)", Quantity = 3, UnitPrice = 24.99m } } },
                new Order { CustomerName = "Eve Davis", CustomerEmail = "eve@example.com", OrderDate = new DateTime(2024, 5, 14), DeliveryDate = new DateTime(2024, 5, 22), Status = AppConstants.OrderStatus.Pending, Priority = AppConstants.OrderPriority.Low, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = 11, ProductName = "Ergonomic Mouse", Quantity = 1, UnitPrice = 49.99m } } },
                new Order { CustomerName = "Frank Miller", CustomerEmail = "frank@example.com", OrderDate = new DateTime(2024, 5, 15), DeliveryDate = new DateTime(2024, 5, 23), Status = AppConstants.OrderStatus.Pending, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string> { "Insurance" }, Items = new List<OrderItem> { new OrderItem { ProductId = 1, ProductName = "Wireless Headphones", Quantity = 1, UnitPrice = 79.99m } } },
                new Order { CustomerName = "Grace Kim", CustomerEmail = "grace@example.com", OrderDate = new DateTime(2024, 5, 16), DeliveryDate = new DateTime(2024, 5, 24), Status = AppConstants.OrderStatus.Pending, Priority = AppConstants.OrderPriority.High, Extras = new List<string> { "Gift wrap", "Express delivery" }, Items = new List<OrderItem> { new OrderItem { ProductId = 11, ProductName = "Ergonomic Mouse", Quantity = 1, UnitPrice = 49.99m }, new OrderItem { ProductId = 12, ProductName = "Standing Desk Mat", Quantity = 3, UnitPrice = 44.99m } } },
                new Order { CustomerName = "Henry Patel", CustomerEmail = "henry@example.com", OrderDate = new DateTime(2024, 5, 18), DeliveryDate = new DateTime(2024, 5, 26), Status = AppConstants.OrderStatus.Processing, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = 5, ProductName = "Dev T-Shirt (M)", Quantity = 1, UnitPrice = 24.99m }, new OrderItem { ProductId = 8, ProductName = "Clean Code", Quantity = 1, UnitPrice = 34.99m } } },
                new Order { CustomerName = "Iris Chen", CustomerEmail = "iris@example.com", OrderDate = new DateTime(2024, 5, 20), DeliveryDate = new DateTime(2024, 5, 28), Status = AppConstants.OrderStatus.Shipped, Priority = AppConstants.OrderPriority.Low, Extras = new List<string> { "Insurance" }, Items = new List<OrderItem> { new OrderItem { ProductId = 3, ProductName = "USB-C Hub", Quantity = 1, UnitPrice = 39.99m } } },
                new Order { CustomerName = "Jack Brown", CustomerEmail = "jack@example.com", OrderDate = new DateTime(2024, 5, 22), DeliveryDate = new DateTime(2024, 5, 30), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = 2, ProductName = "Mechanical Keyboard", Quantity = 1, UnitPrice = 129.99m } } },
                new Order { CustomerName = "Karen Novak", CustomerEmail = "karen@example.com", OrderDate = new DateTime(2024, 5, 25), DeliveryDate = new DateTime(2024, 6, 2), Status = AppConstants.OrderStatus.Delivered, Priority = AppConstants.OrderPriority.High, Extras = new List<string> { "Gift wrap", "Insurance" }, Items = new List<OrderItem> { new OrderItem { ProductId = 8, ProductName = "Clean Code", Quantity = 1, UnitPrice = 34.99m }, new OrderItem { ProductId = 12, ProductName = "Standing Desk Mat", Quantity = 3, UnitPrice = 44.99m } } },
                new Order { CustomerName = "Leo Garcia", CustomerEmail = "leo@example.com", OrderDate = new DateTime(2024, 5, 28), DeliveryDate = new DateTime(2024, 6, 5), Status = AppConstants.OrderStatus.Processing, Priority = AppConstants.OrderPriority.Normal, Extras = new List<string>(), Items = new List<OrderItem> { new OrderItem { ProductId = 1, ProductName = "Wireless Headphones", Quantity = 1, UnitPrice = 79.99m } } }
            };

            foreach (var o in orders)
                o.Total = o.Items.Sum(i => i.Quantity * i.UnitPrice);

            _db.Orders.AddRange(orders);
            _db.SaveChanges();
        }
    }
}
