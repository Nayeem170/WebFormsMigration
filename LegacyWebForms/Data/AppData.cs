using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LegacyWebForms.Core;
using LegacyWebForms.Data;
using LegacyWebForms.Services;
using Microsoft.EntityFrameworkCore;

namespace LegacyWebForms
{
    public static class AppData
    {
        private static readonly Regex _safeDbPath = new Regex(@"\A[^\x00-\x1f;]+\z", RegexOptions.Compiled);
        public static string DbPath { get; private set; } = null!;

        public static ServiceContainer Services { get; private set; } = null!;

        public static void Initialize(string dbPath)
        {
            DbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            if (!_safeDbPath.IsMatch(DbPath))
                throw new ArgumentException("DbPath contains invalid characters.", nameof(dbPath));

            var dir = Path.GetDirectoryName(DbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            EnsureDatabase(DbPath);
            Services = new ServiceContainer(
                new ProductRepository(),
                new OrderRepository(),
                new AppLogger()
            );
        }

        public static AppDbContext CreateDbContext()
        {
            if (string.IsNullOrEmpty(DbPath))
                throw new InvalidOperationException("Call Initialize() first.");
            return new AppDbContext(DbPath);
        }

        private static void EnsureDatabase(string dbPath)
        {
            using var db = new AppDbContext(dbPath);
            db.Database.EnsureCreated();

            if (!db.Products.Any())
                new DbSeeder(db).Seed();
        }
    }

    public class ServiceContainer
    {
        public ProductService Products { get; }
        public OrderService Orders { get; }
        public ILogger Log { get; }

        public ServiceContainer(IProductRepository productRepo, IOrderRepository orderRepo, ILogger logger)
        {
            Products = new ProductService(productRepo, logger);
            Orders = new OrderService(orderRepo, logger);
            Log = logger;
        }
    }
}
