using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;
using Inventory.Contracts;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));
builder.Services.AddAppTelemetry("gateway").AddOtlpExporting(builder.Configuration);

var urls = builder.Configuration["Urls"] ?? "http://localhost:8080";
builder.WebHost.UseUrls(urls);

// TLS termination + management port are endpoint config (env/appsettings):
//   Kestrel__Endpoints__Https__Url=https://0.0.0.0:8443 (+ certificate path)
//   Kestrel__Endpoints__Management__Url=http://0.0.0.0:8090
// The public port must never serve /health* (recon oracle) - enforced below.

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Rate limiting: partitioned on the SOCKET address, before any header
// parsing. Clearing KnownProxies/KnownNetworks (dynamic pod IPs) makes
// X-Forwarded-For attacker-controlled, so a rotated header must not buy
// quota. Numbers leave headroom for the single-host benchmark rates
// measured in Phase 5 (dashboard@50 held 408 rps from one IP).
// The per-IP limiter IS the global limiter: UseRateLimiter applies
// GlobalLimiter to every request unconditionally (a named policy would
// need per-route attachment and silently cover nothing).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 500, Window = TimeSpan.FromSeconds(10) }));
});

var app = builder.Build();

var instanceId = Environment.MachineName + ":" + Process.GetCurrentProcess().Id;

// Forwarded headers are written MANUALLY, not via ForwardedHeadersMiddleware.
// The middleware would have to run with cleared KnownProxies/KnownNetworks
// (pod IPs are dynamic), which rewrites Connection.RemoteIpAddress from the
// client-supplied X-Forwarded-For BEFORE later middleware runs - the rate
// limiter would then partition on an attacker-controlled value, and the
// "overwrite XFF from the socket" step would copy the forged IP. Writing the
// headers by hand from the untouched socket keeps the limiter honest.
app.Use(async (context, next) =>
{
    var socketIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    context.Request.Headers["X-Forwarded-For"] = socketIp;
    context.Request.Headers["X-Forwarded-Proto"] = context.Request.Scheme;

    var incoming = context.Request.Headers[CorrelationHeader.Name].ToString();
    // Client-settable logged field: validate strictly (32 hex, the mint
    // format) or replace. With tracing on, the mint IS the trace id -
    // one identifier for logs, headers, and spans.
    var correlationId = CorrelationId.IsValid(incoming)
        ? incoming
        : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    context.Items[CorrelationHeader.Name] = correlationId;
    context.Request.Headers[CorrelationHeader.Name] = correlationId;
    context.Response.OnStarting(() =>
    {
        if (!context.Response.Headers.ContainsKey(CorrelationHeader.Name))
            context.Response.Headers[CorrelationHeader.Name] = correlationId;
        // Egress hygiene: X-Instance is machine:pid (pod name in k8s -
        // replica-count signal); X-Gateway-Instance is the same class.
        // Stripped at the edge, kept internally for the suites.
        context.Response.Headers.Remove("X-Instance");
        context.Response.Headers.Remove("X-Gateway-Instance");
        return Task.CompletedTask;
    });
    await next(context);
});

// Rate limit BEFORE proxying.
app.UseRateLimiter();

// Health lives on the management port only (8090). The public port returns
// 404 for /health*: /health/ready reports Redis and DB reachability, which
// is free reconnaissance and an attack-timing oracle.
app.Use(async (context, next) =>
{
    var isHealth = context.Request.Path.StartsWithSegments("/health");
    if (isHealth && context.Connection.LocalPort != 8090)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next(context);
});

// The write gate was removed with the demo auth decision (plan 09 part 4):
// the demo runs unauthenticated end to end. Rate limiting, health-guard,
// and forwarded-header hygiene above are independent of it and stay.

app.Logger.LogInformation("Gateway frontend destinations: {Destinations}",
    string.Join(", ", builder.Configuration.GetSection("ReverseProxy:Clusters:frontend:Destinations").GetChildren()
        .Select(c => c.Key + "=" + c.GetValue<string>("Address"))));

app.MapGet("/health", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

// W3C trace context across the proxy hop: YARP's forwarder does not go
// through HttpClient instrumentation, so traceparent is injected here,
// inside the proxy branch where Activity.Current is the request activity.
// Without it every frontend span would start a fresh root trace.
app.MapReverseProxy(proxyPipeline =>
{
    proxyPipeline.Use((ctx, next) =>
    {
        var activity = Activity.Current;
        if (activity != null)
            ctx.Request.Headers["traceparent"] = $"00-{activity.TraceId}-{activity.SpanId}-01";
        return next();
    });
});

app.Run();
