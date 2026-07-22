# ASP.NET Web Forms to .NET 9 Migration Guide

Migrate an ASP.NET Web Forms application from .NET Framework 4.8 to .NET 9 using the CoreWebForms SDK.

## Source Reference

This guide is based on the actual migration of the **LegacyWebForms** inventory management application.

- **Branch**: `feature/corewebforms-migration`
- **Squashed commit**: `7f7a4fa` — Migrate LegacyWebForms to CoreWebForms: upgrade to .NET 9 with EF Core 9, ASP.NET Core hosting, and VS Code launch support
- **Legacy branch**: `feature/legacy-inventory`

## Migration Overview

| Area | .NET Framework 4.8 | .NET 9 (CoreWebForms) |
|---|---|---|
| SDK | `Microsoft.NET.Sdk` | `CoreWebForms.Sdk` |
| Target | `net48` | `net9.0` |
| Hosting | IIS / IIS Express | Kestrel (self-hosted) |
| EF Core | 3.1.32 | 9.0.17 |
| Session | In-process | Distributed (MemoryCache) + JSON serializer |
| Startup | `Global.asax.cs Application_Start` | `Program.cs Main()` |
| Data binding | `Bind()` (two-way) | Typed `Container.DataItem` cast (one-way) |
| UpdatePanel | Supported | Unsupported (full postback) |
| CustomValidator | Supported | Unsupported (manual validation) |

## Phases

| Phase | Description | Reference |
|---|---|---|
| 1 | Project setup — SDK, csproj, nuget, global.json | [Phase 1](01-project-setup.md) |
| 2 | ASP.NET Core hosting — Program.cs, Global.asax.cs, launchSettings | [Phase 2](02-hosting.md) |
| 3 | Data layer — EF Core upgrade, AppDbContext, AppData | [Phase 3](03-data-layer.md) |
| 4 | Session — distributed session, JSON serializer | [Phase 4](04-session.md) |
| 5 | ASPX pages — Bind to typed Container.DataItem cast, remove unsupported controls | [Phase 5](05-aspx-pages.md) |
| 6 | Static files, routing, middleware pipeline | [Phase 6](06-static-files-routing.md) |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) installed
- [CoreWebForms.Sdk](https://github.com/dotnet/CoreWebForms) 1.0.0+ available
- Source project on .NET Framework 4.8 with SDK-style csproj

## Commit History

The migration consists of 4 phase-aligned commits on `feature/corewebforms-migration`:

1. `c15ebe7` Phase 1: Replace project file with CoreWebForms.Sdk (net9.0) and rename namespace to CoreWebForms
2. `54047cb` Phase 2: Add ASP.NET Core hosting with Program.cs, simplify Global.asax, configure VS Code launch
3. `e9cf9ed` Phase 3: Upgrade AppDbContext EF Core 9 ValueConverter for HasConversion compatibility
4. `f95e95e` Phase 4-6: Fix ASPX runtime compilation — replace Bind() with Eval(), remove unsupported controls, simplify web.config. Add migration guide and reorganize implementation docs

## Key Blockers Encountered

- **`Bind()` not supported** in CoreWebForms SDK runtime ASPX compiler — replace with typed `Container.DataItem` cast
- **`UpdatePanel` / `AsyncPostBackTrigger` not supported** — unwrap to full postback
- **`CustomValidator` not supported** — replace with Label + manual validation
- **EF Core 9 removed implicit lambda converters** — use explicit `ValueConverter<,>`
- **ILogger ambiguity** between custom `ILogger` and `Microsoft.Extensions.Logging.ILogger`
- **In-process session does not exist** — use distributed session with JSON serializer
