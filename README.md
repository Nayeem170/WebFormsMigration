# WebForms Migration

ASP.NET Web Forms 4.8 to .NET 9 migration using [CoreWebForms](https://github.com/corewebforms/corewebforms).

## Branches

| Branch | Description |
|---|---|
| `develop` | Default branch with merged migration |
| `feature/legacy-inventory` | Legacy Web Forms project with EF Core service layer |
| `feature/corewebforms-migration` | Phase-aligned migration commits |

## Migration Phases

1. **Project setup** — CoreWebForms.Sdk (net9.0), namespace rename
2. **ASP.NET Core hosting** — Program.cs, Kestrel, Global.asax simplification, config externalized to appsettings.json
3. **Data layer** — EF Core 9 ValueConverter compatibility fix, JSON Extras converter, EF migrations (Migrate)
4. **ASPX pages** — Replace Bind() with typed Container.DataItem binding, remove unsupported controls
5. **Session** — Distributed session with JSON serializer
6. **Static files & routing** — UseStaticFiles, MapPageRoute, middleware pipeline

See [docs/migration/](docs/migration/00-index.md) for the full migration guide, including [the microservices migration plan](docs/migration/08-microservices-migration.md).

## Running

```bash
dotnet run --project Microservices/Catalog
dotnet run --project Microservices/Orders
dotnet run --project Microservices/Frontend
```

Frontend opens at `http://localhost:8081` (Catalog on `8094`, Orders on `8095`). `scripts/publish-local.ps1` publishes all three to `artifacts/publish`. The retired in-process `CoreWebForms/` tree is recoverable from the `corewebforms-final` tag.
