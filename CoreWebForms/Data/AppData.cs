using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CoreWebForms.Core;
using CoreWebForms.Data;
using CoreWebForms.Services;
using Microsoft.EntityFrameworkCore;

namespace CoreWebForms
{
    public enum ServiceMode
    {
        InProcess,
        Http
    }

    public static class AppData
    {
        private static readonly Regex _safeDbPath = new Regex(@"\A[^\x00-\x1f;]+\z", RegexOptions.Compiled);
        public static string DbPath { get; private set; } = null!;

        public static ServiceContainer Services { get; private set; } = null!;

        public static void Initialize(string dbPath, ServiceMode productsMode = ServiceMode.InProcess, string? productsBaseUrl = null, bool runMigrations = true, ServiceMode ordersMode = ServiceMode.InProcess, string? ordersBaseUrl = null)
        {
            DbPath = dbPath ?? throw new ArgumentNullException(nameof(dbPath));
            if (!_safeDbPath.IsMatch(DbPath))
                throw new ArgumentException("DbPath contains invalid characters.", nameof(dbPath));

            var dir = Path.GetDirectoryName(DbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (runMigrations)
                EnsureDatabase(DbPath);
            Services = CreateServices(productsMode, productsBaseUrl, ordersMode, ordersBaseUrl);
        }

        private static ServiceContainer CreateServices(ServiceMode productsMode, string? productsBaseUrl, ServiceMode ordersMode, string? ordersBaseUrl)
        {
            if (productsMode == ServiceMode.Http && string.IsNullOrEmpty(productsBaseUrl))
                throw new ArgumentException("Services:Products:BaseUrl is required when Services:Products:Mode is Http.", nameof(productsBaseUrl));
            if (ordersMode == ServiceMode.Http && string.IsNullOrEmpty(ordersBaseUrl))
                throw new ArgumentException("Services:Orders:BaseUrl is required when Services:Orders:Mode is Http.", nameof(ordersBaseUrl));

            var logger = new AppLogger();
            IOrderService orders = ordersMode switch
            {
                ServiceMode.InProcess => new OrderService(new OrderRepository(), logger),
                ServiceMode.Http => new HttpOrderService(ordersBaseUrl!, logger),
                _ => throw new NotSupportedException(string.Format("Orders mode {0} is not implemented.", ordersMode))
            };
            IProductService products = productsMode switch
            {
                ServiceMode.InProcess => new ProductService(new ProductRepository(), logger),
                ServiceMode.Http => new HttpProductService(productsBaseUrl!, logger),
                _ => throw new NotSupportedException(string.Format("Products mode {0} is not implemented.", productsMode))
            };
            return new ServiceContainer(products, orders, logger);
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
            db.Database.Migrate();

            if (!db.Products.Any())
                new DbSeeder(db).Seed();
        }
    }

    public class ServiceContainer
    {
        public IProductService Products { get; }
        public IOrderService Orders { get; }
        public ILogger Log { get; }

        public ServiceContainer(IProductService products, IOrderService orders, ILogger logger)
        {
            Products = products;
            Orders = orders;
            Log = logger;
        }
    }
}
