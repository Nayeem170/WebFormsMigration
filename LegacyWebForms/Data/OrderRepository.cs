using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace LegacyWebForms.Data
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

        public Order GetById(int id)
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
    }
}
