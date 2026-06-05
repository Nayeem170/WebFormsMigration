using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using LegacyWebForms.Core;
using LegacyWebForms.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyWebForms.Services
{
    public class OrderService
    {
        private readonly IOrderRepository _repo;
        private readonly ILogger _log;

        public OrderService(IOrderRepository repo, ILogger log)
        {
            _repo = repo;
            _log = log;
        }

        public List<Order> GetAll(bool includeDeleted = false)
        {
            return _repo.GetAll(includeDeleted);
        }

        public List<Order> GetAll(bool includeDeleted, string? status)
        {
            return _repo.GetAll(includeDeleted, status);
        }

        public Order GetById(int id)
        {
            return _repo.GetById(id);
        }

        public int Count(bool includeDeleted = false)
        {
            return _repo.Count(includeDeleted);
        }

        public int Count(bool includeDeleted, string? status)
        {
            return _repo.Count(includeDeleted, status);
        }

        public List<Order> GetPaged(int skip, int take, bool includeDeleted, string? status)
        {
            return _repo.GetPaged(skip, take, includeDeleted, status);
        }

        public int CountByStatus(string status)
        {
            return _repo.CountByStatus(status);
        }

        public List<Order> GetRecent(int count)
        {
            return _repo.GetRecent(count);
        }

        public List<OrderItem> GetItems(int orderId)
        {
            return _repo.GetItems(orderId);
        }

        public int PlaceOrder(Order order)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (order.Items == null || !order.Items.Any())
                throw new ArgumentException("Order must contain at least one item.", nameof(order));
            Validate(order);
            foreach (var item in order.Items)
                Validate(item);
            order.Total = order.Items.Sum(i => i.Quantity * i.UnitPrice);

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
                _log.Info(string.Format("Order #{0} placed for {1} ({2} items, ${3:F2})", order.Id, order.CustomerName, order.Items.Count, order.Total));
                return order.Id;
        }

        public void UpdateStatus(int orderId, string status, string priority)
        {
            using var db = AppData.CreateDbContext();
            var existing = db.Orders.Find(orderId);
            if (existing == null || existing.IsDeleted)
            {
                _log.Warning(string.Format("Rejected status update on deleted/missing order #{0}", orderId));
                return;
            }
            existing.Status = status;
            existing.Priority = priority;
            Validate(existing);
            db.SaveChanges();
            _log.Info(string.Format("Order #{0} status changed to {1}", orderId, status));
        }

        public void DeleteOrder(int id)
        {
            using var db = AppData.CreateDbContext();
            using var tx = db.Database.BeginTransaction();

                var order = db.Orders.Include(o => o.Items).FirstOrDefault(o => o.Id == id);
                if (order == null || order.IsDeleted) return;

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
                _log.Info(string.Format("Order #{0} deleted, stock restored for {1} items", id, order.Items.Count));
        }

        private static void Validate(object model)
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, new ValidationContext(model), results, true))
                throw new ValidationException(string.Join("; ", results.Select(r => r.ErrorMessage)));
        }
    }
}
