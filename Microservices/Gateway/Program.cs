using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;
using Inventory.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));

var urls = builder.Configuration["Urls"] ?? "http://localhost:8080";
builder.WebHost.UseUrls(urls);

// TLS termination + management port are endpoint config (env/appsettings):
//   Kestrel__Endpoints__Https__Url=https://0.0.0.0:8443 (+ certificate path)
//   Kestrel__Endpoints__Management__Url=http://0.0.0.0:8090
// The public port must never serve /health* (recon oracle) - enforced below.

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Phase 7 edge security. The services stay unauthenticated BY DESIGN; the
// network boundary is the enforcement for them (NetworkPolicy in k8s).
// The IdP is ROUTED through this gateway (/realms, /js, /resources), so
// there is exactly one exposed surface. Authority stays the INTERNAL
// Keycloak URL (metadata + backchannel stay on the isolated network);
// Issuer is the EXTERNAL https URL the browser sees (KC_HOSTNAME). With
// backchannel-dynamic on, the metadata document carries internal
// backchannel endpoints and external browser endpoints, so no self-loop
// and no self-signed-cert validation on the handler's backchannel.
var authority = builder.Configuration["Oidc:Authority"];
var issuer = builder.Configuration["Oidc:Issuer"];
var audience = builder.Configuration["Oidc:Audience"] ?? "gateway";
if (!string.IsNullOrEmpty(authority))
{
    builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = "Smart";
        o.DefaultChallengeScheme = "Smart";
    })
    .AddPolicyScheme("Smart", "Bearer first, cookies for browsers", o =>
    {
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : CookieAuthenticationDefaults.AuthenticationScheme;
        // Challenges must go to the IdP (OIDC handler), never to the cookie
        // handler - the cookie handler's challenge is a redirect to its
        // LoginPath (/Account/Login), which is not an IdP.
        o.ForwardChallenge = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
    {
        o.Authority = authority;
        // Plain HTTP is deliberate for the internal hop (TLS terminates at
        // the gateway; Keycloak sits on the isolated network). Local-dev
        // deviation recorded in the Phase 7 decisions.
        o.RequireHttpsMetadata = false;
        o.TokenValidationParameters.ValidAudiences = new[] { audience };
        if (!string.IsNullOrEmpty(issuer)) o.TokenValidationParameters.ValidIssuers = new[] { issuer };
        o.TokenValidationParameters.NameClaimType = "preferred_username";
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        // Deliberate session-expiry decision: sliding expiration sized to
        // cover a working session. An expired mid-postback POST redirects to
        // the IdP and the form body is lost - accepted and recorded in the
        // plan rather than discovered during smoke.
        o.SlidingExpiration = true;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.Cookie.Name = "ccw.session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Lax;
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
    {
        o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        o.Authority = authority;
        // Same internal plain-HTTP deviation as the bearer handler.
        o.RequireHttpsMetadata = false;
        o.ClientId = builder.Configuration["Oidc:ClientId"] ?? "gateway";
        o.CallbackPath = "/signin-oidc";
        o.ResponseType = "code";
        o.ResponseMode = "form_post";
        o.GetClaimsFromUserInfoEndpoint = false;
        o.TokenValidationParameters.NameClaimType = "preferred_username";
        o.TokenValidationParameters.ValidAudiences = new[] { audience };
        if (!string.IsNullOrEmpty(issuer)) o.TokenValidationParameters.ValidIssuers = new[] { issuer };
        // form_post callbacks break without SameSite=None; Secure on the
        // correlation and nonce cookies. Works on loopback, fails on the
        // first real hostname if forgotten. MUTATE the default builders -
        // replacing them with fresh CookieBuilders drops their Name
        // prefixes (.AspNetCore.OpenIdConnect.Nonce. /
        // .AspNetCore.Correlation.), and ReadNonceCookie matches cookies
        // BY that prefix: the write side would emit prefix-less cookies
        // the read side can never find, and every login 500s with
        // IDX21323. Found the hard way; verified against the .NET 9
        // handler source.
        o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
        o.CorrelationCookie.HttpOnly = true;
        o.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
        o.NonceCookie.HttpOnly = true;
    });
    builder.Services.AddAuthorization();
}

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
    // format) or replace. Newline injection, forged correlation, and Phase 8
    // forged trace attributes all start with trusting this header.
    var correlationId = incoming.Length == 32 && incoming.All(Uri.IsHexDigit)
        ? incoming
        : Guid.NewGuid().ToString("N");
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

// Rate limit BEFORE auth-adjacent handling.
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

// The write gate: reads public, writes authenticated. WebForms postbacks
// are POSTs to the .aspx pages; any non-GET/HEAD/OPTIONS requires an
// authenticated user. Browsers get a 302 to the IdP, API callers a 401.
// The routed IdP surface is exempt: its token and login-action endpoints
// are unauthenticated POSTs BY OIDC DESIGN and enforce their own auth. The
// rate limiter still covers them (it runs before this gate).
if (!string.IsNullOrEmpty(authority))
{
    app.UseAuthentication();
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path;
        var isIdpSurface = path.StartsWithSegments("/realms")
            || path.StartsWithSegments("/js")
            || path.StartsWithSegments("/resources");
        var method = context.Request.Method;
        var isWrite = !(method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase));
        if (isWrite && !isIdpSurface && (context.User.Identity?.IsAuthenticated != true))
        {
            if (context.Request.Headers.Accept.ToString().Contains("text/html"))
            {
                await context.ChallengeAsync();
                return;
            }
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }
        await next(context);
    });
}

app.Logger.LogInformation("Gateway frontend destinations: {Destinations}",
    string.Join(", ", builder.Configuration.GetSection("ReverseProxy:Clusters:frontend:Destinations").GetChildren()
        .Select(c => c.Key + "=" + c.GetValue<string>("Address"))));

app.MapGet("/health", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

app.MapReverseProxy();

app.Run();
