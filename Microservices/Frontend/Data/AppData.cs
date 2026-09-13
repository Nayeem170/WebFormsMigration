using System;
using CoreWebForms.Core;
using CoreWebForms.Services;

namespace CoreWebForms
{
    public enum ServiceMode
    {
        InProcess,
        Http
    }

    public static class AppData
    {
        public static ServiceContainer Services { get; private set; } = null!;

        public static void Initialize(string dbPath, ServiceMode productsMode = ServiceMode.InProcess, string? productsBaseUrl = null, bool runMigrations = true, ServiceMode ordersMode = ServiceMode.InProcess, string? ordersBaseUrl = null)
        {
            if (runMigrations)
                throw new InvalidOperationException("Frontend does not own database migrations; Catalog migrates the shared file.");
            if (productsMode != ServiceMode.Http || ordersMode != ServiceMode.Http)
                throw new InvalidOperationException("Frontend has no in-process service arm; Services:Products:Mode and Services:Orders:Mode must be Http.");
            if (string.IsNullOrEmpty(productsBaseUrl))
                throw new ArgumentException("Services:Products:BaseUrl is required.", nameof(productsBaseUrl));
            if (string.IsNullOrEmpty(ordersBaseUrl))
                throw new ArgumentException("Services:Orders:BaseUrl is required.", nameof(ordersBaseUrl));

            var logger = new AppLogger();
            IProductService products = new HttpProductService(productsBaseUrl, logger);
            IOrderService orders = new HttpOrderService(ordersBaseUrl, logger);
            Services = new ServiceContainer(products, orders, logger);
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
