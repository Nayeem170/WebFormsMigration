using System.Diagnostics;
using Inventory.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

var urls = builder.Configuration["Urls"] ?? "http://localhost:8080";
builder.WebHost.UseUrls(urls);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

var instanceId = Environment.MachineName + ":" + Process.GetCurrentProcess().Id;

app.Use(async (context, next) =>
{
    var incoming = context.Request.Headers[CorrelationHeader.Name].ToString();
    var correlationId = string.IsNullOrWhiteSpace(incoming)
        ? Guid.NewGuid().ToString("N")
        : incoming;
    context.Items[CorrelationHeader.Name] = correlationId;
    context.Request.Headers[CorrelationHeader.Name] = correlationId;
    context.Response.OnStarting(() =>
    {
        if (!context.Response.Headers.ContainsKey(CorrelationHeader.Name))
            context.Response.Headers[CorrelationHeader.Name] = correlationId;
        if (!context.Response.Headers.ContainsKey("X-Gateway-Instance"))
            context.Response.Headers["X-Gateway-Instance"] = instanceId;
        return Task.CompletedTask;
    });
    await next(context);
});

app.Logger.LogInformation("Gateway frontend destinations: {Destinations}",
    string.Join(", ", builder.Configuration.GetSection("ReverseProxy:Clusters:frontend:Destinations").GetChildren()
        .Select(c => c.Key + "=" + c.GetValue<string>("Address"))));

app.MapGet("/health", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

app.MapReverseProxy();

app.Run();
