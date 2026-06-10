# Phase 6: Static Files, Routing, and Middleware Pipeline

Configure static file serving, URL routing, and the correct middleware order.

**Reference commit**: `54047cb` (part of Program.cs in Phase 2)

## Steps

### 1. Serve static files explicitly

IIS handles static files natively. In ASP.NET Core, configure `UseStaticFiles()` for each content directory:

```csharp
foreach (var staticPath in new[] { "Content", "Scripts", "Pages" })
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(
            Path.Combine(contentRoot, staticPath)),
        RequestPath = "/" + staticPath,
    });
}
```

Serve `favicon.ico` via an endpoint:

```csharp
app.MapGet("/favicon.ico", () => Results.File(
    Path.Combine(contentRoot, "favicon.ico"), "image/x-icon"));
```

### 2. Register URL routes

Move route registration from `web.config` default document / IIS config to `RouteTable.Routes.MapPageRoute()` in the `ApplicationStarted` callback:

```csharp
app.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStarted.Register(() =>
    {
        RouteTable.Routes.MapPageRoute("Default", "", "~/Pages/Default/Default.aspx");
        RouteTable.Routes.MapPageRoute("Dashboard", "Pages/Default/", "~/Pages/Default/Default.aspx");
        RouteTable.Routes.MapPageRoute("Products", "Pages/Products/", "~/Pages/Products/Products.aspx");
        RouteTable.Routes.MapPageRoute("Orders", "Pages/Orders/", "~/Pages/Orders/Orders.aspx");
    });
```

**Important**: Routes must be registered in the `ApplicationStarted` callback, not inline during `builder.Build()`. The `RouteTable` requires the host to be fully running.

### 3. Configure the middleware pipeline in correct order

The middleware pipeline order is critical. Incorrect ordering causes route resolution failures and session issues:

```csharp
// 1. Error handling (only in production)
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Pages/Errors/ErrorPage.aspx");
}

// 2. Routing
app.UseRouting();

// 3. Session (must come before SystemWebAdapters)
app.UseSession();

// 4. SystemWebAdapters bridge
app.UseSystemWebAdapters();

// 5. WebForms endpoints (must come last)
app.MapHttpHandlers();
app.MapScriptManager();
```

**Order diagram**:

```
Request → UseExceptionHandler → UseRouting → UseSession → UseSystemWebAdapters → MapHttpHandlers → MapScriptManager → Response
```

### 4. Remove IIS-specific route config from web.config

**Before** (legacy `web.config`):
```xml
<system.webServer>
  <defaultDocument>
    <files>
      <add value="Pages/Default/Default.aspx" />
    </files>
  </defaultDocument>
</system.webServer>
```

**After**: Remove the `<defaultDocument>` section entirely. Routes are handled by `RouteTable.Routes` in `Program.cs`.

## Verification

```bash
dotnet run --project CoreWebForms.csproj
```

- Test each URL route: `/`, `/Pages/Default/`, `/Pages/Products/`, `/Pages/Orders/`
- Verify CSS/JS static files load (`/Content/site.css`, `/Scripts/site.js`)
- Verify `favicon.ico` loads
- Verify 404 errors redirect to ErrorPage.aspx
