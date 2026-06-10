# Phase 2: ASP.NET Core Hosting

Replace IIS hosting with Kestrel self-hosting via `Program.cs`. Adapt `Global.asax.cs` for ASP.NET Core integration.

**Reference commit**: `54047cb`

## Steps

### 1. Create Program.cs

Create `Program.cs` at the project root with the ASP.NET Core hosting setup:

```csharp
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

            builder.WebHost.UseUrls("http://localhost:8081");

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

            // DB init (moved from Global.asax Application_Start)
            var dbPath = Path.Combine(contentRoot, "App_Data", "inventory.db");
            AppData.Initialize(dbPath);

            // Logging setup (moved from Global.asax)
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

            // Static files
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

            // Error handling
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Pages/Errors/ErrorPage.aspx");
            }

            // Middleware pipeline (order matters!)
            app.UseRouting();
            app.UseSession();
            app.UseSystemWebAdapters();

            // Route registration
            app.Services.GetRequiredService<IHostApplicationLifetime>()
                .ApplicationStarted.Register(() =>
                {
                    RouteTable.Routes.MapPageRoute("Default", "", "~/Pages/Default/Default.aspx");
                    RouteTable.Routes.MapPageRoute("Dashboard", "Pages/Default/", "~/Pages/Default/Default.aspx");
                    RouteTable.Routes.MapPageRoute("Products", "Pages/Products/", "~/Pages/Products/Products.aspx");
                    RouteTable.Routes.MapPageRoute("Orders", "Pages/Orders/", "~/Pages/Orders/Orders.aspx");

                    if (app.Environment.IsDevelopment())
                        Process.Start(new ProcessStartInfo("http://localhost:8081/") { UseShellExecute = true });
                });

            app.MapHttpHandlers();
            app.MapScriptManager();

            app.Run();
        }
    }
}
```

**Key points**:
- DB init must happen **after** `builder.Build()` — `ContentRootPath` is not available before
- Routes are registered in `ApplicationStarted` callback — `RouteTable` requires the host to be running
- Middleware order: `UseRouting()` → `UseSession()` → `UseSystemWebAdapters()` → `MapHttpHandlers()` → `MapScriptManager()`
- `AddHttpApplication<InventoryApp>()` hooks the `Global.asax.cs` class into the pipeline

### 2. Simplify Global.asax.cs

Move DB init, logging, and session config out of `Global.asax.cs`. Keep only error handling:

**Before**:
```csharp
public class InventoryApp : HttpApplication
{
    void Application_Start(object sender, EventArgs e)
    {
        // DB init, logging, session config — all here
    }

    void Application_Error(object sender, EventArgs e)
    {
        // Error logging
    }
}
```

**After**:
```csharp
public class InventoryApp : HttpApplication
{
    void Application_Error(object sender, EventArgs e)
    {
        var ex = Server.GetLastError();
        if (ex == null) return;
        var baseEx = ex.GetBaseException();
        AppData.Services?.Log.Error(
            string.Format("Unhandled error on {0}", Request?.RawUrl), baseEx);
    }
}
```

### 3. Create launchSettings.json

Create `Properties/launchSettings.json` for VS Code / dotnet run:

```json
{
  "profiles": {
    "CoreWebForms": {
      "commandName": "Project",
      "launchBrowser": false,
      "applicationUrl": "http://localhost:8081",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

**Note**: `launchBrowser: false` — the C# extension reads this file and can open the browser before the server is ready if `true`. Browser launch is handled by `Program.cs` instead (see step 2).

### 4. Configure VS Code launch.json

Update `.vscode/launch.json` for F5 debugging:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Launch CoreWebForms",
            "type": "coreclr",
            "request": "launch",
            "preLaunchTask": "build",
            "program": "${workspaceFolder}/../artifacts/bin/Debug/net/net9.0/CoreWebForms.exe",
            "args": [],
            "cwd": "${workspaceFolder}",
            "stopAtEntry": false,
            "env": {
                "ASPNETCORE_ENVIRONMENT": "Development",
                "ASPNETCORE_URLS": "http://localhost:8081"
            }
        }
    ]
}
```

**Note**: No `serverReadyAction` — VS Code's coreclr debugger doesn't reliably capture stdout for pattern matching. Browser is opened from `Program.cs` via `Process.Start` inside the `ApplicationStarted` callback, which fires only after routes are registered and Kestrel is accepting requests.

## Verification

```bash
dotnet run --project CoreWebForms.csproj
```

Open `http://localhost:8081` in the browser.
