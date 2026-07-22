using System.Diagnostics;
using System.IO;
using System.Web.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CoreWebForms
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var urls = builder.Configuration["Urls"] ?? "http://localhost:8081";
            builder.WebHost.UseUrls(urls);

            builder.Services.AddDataProtection();
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession();

            builder.Services.AddSystemWebAdapters()
                .AddJsonSessionSerializer(options =>
                {
                    options.RegisterKey<List<OrderItem>>("CartItems");
                })
                .AddHttpApplication<InventoryApp>()
                .AddWrappedAspNetCoreSession()
                .AddRouting()
                .AddWebForms()
                .AddScriptManager()
                .AddDynamicPages();

            var app = builder.Build();

            var contentRoot = app.Environment.ContentRootPath;

            var dbRelativePath = builder.Configuration["Database:RelativePath"] ?? "App_Data/inventory.db";
            var dbPath = Path.Combine(contentRoot, dbRelativePath);
            AppData.Initialize(dbPath);

            var logDir = Path.Combine(contentRoot, "App_Data", "logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "app.log");
            if (Trace.Listeners["file"] == null)
            {
                var listener = new TextWriterTraceListener(logPath, "file")
                {
                    TraceOutputOptions = TraceOptions.DateTime
                };
                Trace.Listeners.Add(listener);
            }
            Trace.AutoFlush = true;

            app.MapGet("/favicon.ico", () => Results.File(
                Path.Combine(contentRoot, "favicon.ico"), "image/x-icon"));

            foreach (var staticPath in new[] { "Content", "Scripts", "Pages" })
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(
                        Path.Combine(contentRoot, staticPath)),
                    RequestPath = "/" + staticPath,
                });
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Pages/Errors/ErrorPage.aspx");
            }

            app.UseRouting();
            app.UseSession();
            app.UseSystemWebAdapters();

            app.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStarted.Register(() =>
                {
                    RouteTable.Routes.MapPageRoute("Default", "", "~/Pages/Default/Default.aspx");
                    RouteTable.Routes.MapPageRoute("Dashboard", "Pages/Default/", "~/Pages/Default/Default.aspx");
                    RouteTable.Routes.MapPageRoute("Products", "Pages/Products/", "~/Pages/Products/Products.aspx");
                    RouteTable.Routes.MapPageRoute("Orders", "Pages/Orders/", "~/Pages/Orders/Orders.aspx");

                    if (app.Environment.IsDevelopment())
                        Process.Start(new ProcessStartInfo(urls.Split(';')[0]) { UseShellExecute = true });
                });

            app.MapHttpHandlers();
            app.MapScriptManager();

            app.Run();
        }
    }
}
