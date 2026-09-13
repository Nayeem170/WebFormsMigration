using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CoreWebForms.Core;
using CoreWebForms.Data;

namespace CoreWebForms.Services
{
    public class OrderService : IOrderService
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

        public Order? GetById(int id)
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

            var orderId = _repo.PlaceOrder(order);
            _log.Info(string.Format("Order #{0} placed for {1} ({2} items, ${3:F2})", order.Id, order.CustomerName, order.Items.Count, order.Total));
            return orderId;
        }

        public void UpdateStatus(int orderId, string status, string priority)
        {
            ValidateStatusFields(status, priority);
            if (!_repo.TryUpdateStatus(orderId, status, priority))
            {
                _log.Warning(string.Format("Rejected status update on deleted/missing order #{0}", orderId));
                return;
            }
            _log.Info(string.Format("Order #{0} status changed to {1}", orderId, status));
        }

        public void DeleteOrder(int id)
        {
            var deleted = _repo.DeleteOrder(id);
            if (deleted == null) return;
            _log.Info(string.Format("Order #{0} deleted, stock restored for {1} items", id, deleted.Items.Count));
        }

        private static void Validate(object model)
        {
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, new ValidationContext(model), results, true))
                throw new ValidationException(string.Join("; ", results.Select(r => r.ErrorMessage)));
        }

        private static void ValidateStatusFields(string status, string priority)
        {
            var probe = new Order();
            var results = new List<ValidationResult>();
            var ok = Validator.TryValidateProperty(status, new ValidationContext(probe) { MemberName = nameof(Order.Status) }, results);
            ok &= Validator.TryValidateProperty(priority, new ValidationContext(probe) { MemberName = nameof(Order.Priority) }, results);
            if (!ok)
                throw new ValidationException(string.Join("; ", results.Select(r => r.ErrorMessage)));
        }
    }
}
