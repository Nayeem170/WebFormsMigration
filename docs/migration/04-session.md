# Phase 4: Session Migration

Replace in-process session with distributed session using JSON serialization.

**Reference commit**: `54047cb` (part of Program.cs in Phase 2)

## Background

ASP.NET Framework uses in-process session state by default. ASP.NET Core does not have in-process session — it uses `IDistributedCache`-backed session. The SystemWebAdapters bridge provides a `System.Web.HttpSessionState` wrapper over ASP.NET Core's distributed session.

## Steps

### 1. Register session services in Program.cs

```csharp
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
```

### 2. Register the JSON session serializer

Register each session key with its type for JSON serialization:

```csharp
builder.Services.AddSystemWebAdapters()
    .AddJsonSessionSerializer(options =>
    {
        options.RegisterKey<List<OrderItem>>("CartItems");
    })
    .AddWrappedAspNetCoreSession()
    // ... other registrations
```

**Important**: Every session key must be registered with its concrete type. If a key is not registered, deserialization will fail.

Find all session keys in code:

```bash
grep -rn 'Session\[' Pages/ --include='*.cs'
```

For each key, add a `RegisterKey<T>()` call.

### 3. Wire session middleware in the pipeline

Order matters — session middleware must come before `UseSystemWebAdapters()`:

```csharp
app.UseRouting();
app.UseSession();           // <-- must come before UseSystemWebAdapters
app.UseSystemWebAdapters();
```

### 4. Verify session code-behind is unchanged

Session access in code-behind files should work without changes:

```csharp
// This works identically in both legacy and CoreWebForms
var cart = (List<OrderItem>)Session["Cart"];
if (cart == null)
{
    cart = new List<OrderItem>();
    Session["Cart"] = cart;
}
```

The `AddWrappedAspNetCoreSession()` adapter translates between `System.Web.HttpSessionState` and ASP.NET Core's session transparently.

## Common Issues

- **Type mismatch**: If `RegisterKey<List<OrderItem>>` is registered but the actual value is `CartItem[]`, deserialization will fail. Match the exact type.
- **Missing registration**: If a session key is not registered, the session value will be `null`. Register all keys used in the application.
- **Complex types**: Session values must be JSON-serializable. Avoid storing `System.Drawing.Image` or other non-serializable types.

## Verification

- Add items to a shopping cart / session-dependent feature
- Navigate between pages — session data should persist
- Refresh the page — session data should still be available
