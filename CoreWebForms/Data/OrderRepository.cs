using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace CoreWebForms.Data
{
    public class OrderRepository : IOrderRepository
    {
        public List<Order> GetAll(bool includeDeleted = false)
        {
            using (var db = AppData.CreateDbContext())
            {
                var query = db.Orders.Include(o => o.Items).AsNoTracking().AsQueryable();
                if (!includeDeleted)
                    query = query.Where(o => !o.IsDeleted);
                return query.OrderByDescending(o => o.OrderDate).ToList();
            }
        }

        public List<Order> GetAll(bool includeDeleted, string? status)
        {
            using (var db = AppData.CreateDbContext())
            {
                var query = db.Orders.Include(o => o.Items).AsNoTracking().AsQueryable();
                if (!includeDeleted)
                    query = query.Where(o => !o.IsDeleted);
                if (!string.IsNullOrEmpty(status))
                    query = query.Where(o => o.Status == status);
                return query.OrderByDescending(o => o.OrderDate).ToList();
            }
        }

        public Order? GetById(int id)
        {
            using (var db = AppData.CreateDbContext())
                return db.Orders.Include(o => o.Items).AsNoTracking().FirstOrDefault(o => o.Id == id);
        }

        public int Count(bool includeDeleted = false)
        {
            using (var db = AppData.CreateDbContext())
            {
                var query = db.Orders.AsQueryable();
                if (!includeDeleted)
                    query = query.Where(o => !o.IsDeleted);
                return query.Count();
            }
        }

        public int CountByStatus(string status)
        {
            using (var db = AppData.CreateDbContext())
                return db.Orders.Count(o => !o.IsDeleted && o.Status == status);
        }

        public List<Order> GetRecent(int count)
        {
            using (var db = AppData.CreateDbContext())
                return db.Orders.Include(o => o.Items).AsNoTracking()
                    .Where(o => !o.IsDeleted)
                    .OrderByDescending(o => o.OrderDate)
                    .Take(count).ToList();
        }

        public int Count(bool includeDeleted, string? status)
        {
            using (var db = AppData.CreateDbContext())
            {
                var query = db.Orders.AsQueryable();
                if (!includeDeleted)
                    query = query.Where(o => !o.IsDeleted);
                if (!string.IsNullOrEmpty(status))
                    query = query.Where(o => o.Status == status);
                return query.Count();
            }
        }

        public List<Order> GetPaged(int skip, int take, bool includeDeleted, string? status)
        {
            using (var db = AppData.CreateDbContext())
            {
                var query = db.Orders.Include(o => o.Items).AsNoTracking().AsQueryable();
                if (!includeDeleted)
                    query = query.Where(o => !o.IsDeleted);
                if (!string.IsNullOrEmpty(status))
                    query = query.Where(o => o.Status == status);
                return query.OrderByDescending(o => o.OrderDate).Skip(skip).Take(take).ToList();
            }
        }

        public List<OrderItem> GetItems(int orderId)
        {
            using (var db = AppData.CreateDbContext())
                return db.OrderItems.AsNoTracking().Where(i => i.OrderId == orderId).ToList();
        }

        public int PlaceOrder(Order order)
        {
            using var db = AppData.CreateDbContext();
            using var tx = db.Database.BeginTransaction();

            db.Orders.Add(order);
            db.SaveChanges();

            foreach (var item in order.Items)
            {
                var product = db.Products.Find(item.ProductId);
                if (product == null)
                    throw new InvalidOperationException(
                        string.Format("Product ID {0} not found.", item.ProductId));
                if (product.Stock < item.Quantity)
                    throw new InvalidOperationException(
                        string.Format("Insufficient stock for product ID {0}: requested {1}, available {2}", item.ProductId, item.Quantity, product.Stock));
                product.Stock -= item.Quantity;
                if (product.Stock <= 0) product.IsActive = false;
            }
            db.SaveChanges();
            tx.Commit();
            return order.Id;
        }

        public bool TryUpdateStatus(int orderId, string status, string priority)
        {
            using var db = AppData.CreateDbContext();
            var existing = db.Orders.Find(orderId);
            if (existing == null || existing.IsDeleted)
                return false;

            existing.Status = status;
            existing.Priority = priority;
            db.SaveChanges();
            return true;
        }

        public Order? DeleteOrder(int id)
        {
            using var db = AppData.CreateDbContext();
            using var tx = db.Database.BeginTransaction();

            var order = db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == id);
            if (order == null || order.IsDeleted) return null;

            foreach (var item in order.Items)
            {
                var product = db.Products.Find(item.ProductId);
                if (product != null)
                {
                    product.Stock += item.Quantity;
                    if (product.Stock > 0 && !product.IsDeleted)
                        product.IsActive = true;
                }
            }

            order.IsDeleted = true;
            db.SaveChanges();
            tx.Commit();
            return order;
        }
    }
}
