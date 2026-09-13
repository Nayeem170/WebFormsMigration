using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CoreWebForms.CharacterizationTests
{
    public abstract class DatabaseTest : IDisposable
    {
        protected DatabaseTest()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cwf-characterization");
            AppData.Initialize(Path.Combine(dir, Guid.NewGuid().ToString("N") + ".db"));
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(AppData.DbPath); } catch { }
        }

        protected static Product NewProduct(int stock = 10, bool isActive = true)
        {
            return new Product
            {
                Name = "Char Product",
                Category = "CharCat",
                Price = 5m,
                Stock = stock,
                IsActive = isActive,
                AddedDate = DateTime.Now
            };
        }

        protected static OrderItem NewItem(int productId, int quantity, decimal unitPrice = 5m, string productName = "Char Product")
        {
            return new OrderItem
            {
                ProductId = productId,
                Quantity = quantity,
                UnitPrice = unitPrice,
                ProductName = productName
            };
        }

        protected static Order NewOrder(params OrderItem[] items)
        {
            return new Order
            {
                CustomerName = "Char Customer",
                CustomerEmail = "char@example.com",
                OrderDate = DateTime.Now,
                DeliveryDate = DateTime.Today.AddDays(3),
                Status = "Pending",
                Priority = "Normal",
                Items = items.ToList()
            };
        }
    }
}
