using System;
using System.Collections.Generic;
using System.Linq;
using LegacyWebForms.Core;
using Microsoft.EntityFrameworkCore;

namespace LegacyWebForms.Data
{
    public class ProductRepository : IProductRepository
    {
        public List<Product> GetAll(bool includeDeleted = false)
        {
            using (var db = AppData.CreateDbContext())
            {
                var query = db.Products.AsNoTracking().AsQueryable();
                if (!includeDeleted)
                    query = query.Where(p => !p.IsDeleted);
                return query.OrderBy(p => p.Id).ToList();
            }
        }

        public Product GetById(int id)
        {
            using (var db = AppData.CreateDbContext())
                return db.Products.AsNoTracking().FirstOrDefault(p => p.Id == id);
        }

        public int Add(Product p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            using (var db = AppData.CreateDbContext())
            {
                db.Products.Add(p);
                db.SaveChanges();
                return p.Id;
            }
        }

        public void Update(Product p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            using (var db = AppData.CreateDbContext())
            {
                var existing = db.Products.Find(p.Id);
                if (existing == null) return;
                existing.Name     = p.Name;
                existing.Category = p.Category;
                existing.Price    = p.Price;
                existing.Stock    = p.Stock;
                existing.IsActive = p.IsActive;
                db.SaveChanges();
            }
        }

        public void Delete(int id)
        {
            using (var db = AppData.CreateDbContext())
            {
                var p = db.Products.Find(id);
                if (p == null) return;
                p.IsDeleted = true;
                p.IsActive = false;
                db.SaveChanges();
            }
        }
    }
}
