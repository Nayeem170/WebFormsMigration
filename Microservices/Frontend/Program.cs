using System.Diagnostics;
using System.IO;
using System.Web.Routing;
using Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CoreWebForms
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var urls = builder.Configuration["Urls"] ?? "http://localhost:8081";
            builder.WebHost.UseUrls(urls);

            var sessionRedis = builder.Configuration["Session:Redis"];
            if (!string.IsNullOrEmpty(sessionRedis))
            {
                var redisOptions = StackExchange.Redis.ConfigurationOptions.Parse(sessionRedis);
                redisOptions.ConnectTimeout = 2000;
                redisOptions.SyncTimeout = 2000;
                redisOptions.AbortOnConnectFail = true;
                var multiplexer = StackExchange.Redis.ConnectionMultiplexer.Connect(redisOptions);
                SessionState.Multiplexer = multiplexer;
                builder.Services.AddSingleton(multiplexer);
                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    options.ConfigurationOptions = redisOptions;
                    options.InstanceName = "ccw:";
                });
                builder.Services.AddDataProtection()
                    .PersistKeysToStackExchangeRedis(multiplexer, "DataProtection-Keys")
                    .SetApplicationName("CoreWebForms.Frontend");
            }
            else
            {
                builder.Services.AddDistributedMemoryCache();
                builder.Services.AddDataProtection();
            }
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

            app.Use(async (context, next) =>
            {
                var incoming = context.Request.Headers[CorrelationHeader.Name].ToString();
                var correlationId = string.IsNullOrWhiteSpace(incoming)
                    ? Guid.NewGuid().ToString("N")
                    : incoming;
                context.Items["CorrelationId"] = correlationId;
                context.Response.Headers[CorrelationHeader.Name] = correlationId;
                await next(context);
            });

            if (string.IsNullOrEmpty(sessionRedis) && !builder.Configuration.GetValue<bool>("Session:UseMemoryCache"))
            {
                app.Logger.LogError("Session state is running on the in-process memory cache: single-instance only. Set Session:Redis for multi-instance deployments, or Session:UseMemoryCache to acknowledge single-instance operation.");
            }

            var sessionReady = false;

            var contentRoot = app.Environment.ContentRootPath;

            var dbPathSetting = builder.Configuration["Database:Path"];
            var dbPath = !string.IsNullOrEmpty(dbPathSetting)
                ? dbPathSetting
                : Path.Combine(contentRoot, builder.Configuration["Database:RelativePath"] ?? "App_Data/inventory.db");
            var productsModeText = builder.Configuration["Services:Products:Mode"];
            var productsMode = ServiceMode.InProcess;
            if (!string.IsNullOrEmpty(productsModeText) && !Enum.TryParse<ServiceMode>(productsModeText, ignoreCase: true, out productsMode))
                throw new InvalidOperationException(string.Format("Unknown Services:Products:Mode value '{0}'.", productsModeText));
            var productsBaseUrl = builder.Configuration["Services:Products:BaseUrl"];
            var ordersModeText = builder.Configuration["Services:Orders:Mode"];
            var ordersMode = ServiceMode.InProcess;
            if (!string.IsNullOrEmpty(ordersModeText) && !Enum.TryParse<ServiceMode>(ordersModeText, ignoreCase: true, out ordersMode))
                throw new InvalidOperationException(string.Format("Unknown Services:Orders:Mode value '{0}'.", ordersModeText));
            var ordersBaseUrl = builder.Configuration["Services:Orders:BaseUrl"];
            var runMigrations = builder.Configuration.GetValue<bool?>("Database:Migrate") ?? true;
            AppData.Initialize(dbPath, productsMode, productsBaseUrl, runMigrations, ordersMode, ordersBaseUrl);

            if (Trace.Listeners["console"] == null)
            {
                Trace.Listeners.Add(new ConsoleTraceListener { Name = "console" });
            }
            try
            {
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
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Trace.AutoFlush = true;

            app.MapGet("/favicon.ico", () => Results.File(
                Path.Combine(contentRoot, "favicon.ico"), "image/x-icon"));

            app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
            app.MapGet("/health/ready", () => sessionReady
                ? Results.Ok(new { status = "ready" })
                : Results.Json(new { status = "warming" }, statusCode: 503));

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

                    if (builder.Configuration.GetValue<bool>("LaunchBrowser"))
                        Process.Start(new ProcessStartInfo(urls.Split(';')[0]) { UseShellExecute = true });
                });

            app.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStarted.Register(() =>
                {
                    _ = Task.Run(async () =>
                    {
                        var baseUri = urls.Split(';')[0].TrimEnd('/');
                        var host = new UriBuilder(baseUri);
                        if (host.Host == "0.0.0.0" || host.Host == "[::]" || host.Host == "::")
                            host.Host = "localhost";
                        baseUri = host.Uri.ToString().TrimEnd('/');
                        var warmed = false;
                        using (var warmClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) })
                        {
                            warmClient.DefaultRequestHeaders.Add("User-Agent", "ccw-warmup");
                            foreach (var path in new[] { "/", "/Pages/Products/", "/Pages/Orders/" })
                            {
                                for (var attempt = 0; attempt < 10; attempt++)
                                {
                                    try
                                    {
                                        var response = await warmClient.GetAsync(baseUri + path);
                                        if (response.IsSuccessStatusCode)
                                        {
                                            if (path == "/")
                                                warmed = true;
                                            break;
                                        }
                                    }
                                    catch { }
                                    await Task.Delay(2000);
                                }
                            }
                        }
                        sessionReady = warmed;
                    });
                });

            app.MapHttpHandlers();
            app.MapScriptManager();

            app.Run();
        }
    }
}
