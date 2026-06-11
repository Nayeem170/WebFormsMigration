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
2. **ASP.NET Core hosting** — Program.cs, Kestrel, Global.asax simplification
3. **Data layer** — EF Core 9 ValueConverter compatibility fix
4. **ASPX pages** — Replace Bind() with Eval(), remove unsupported controls
5. **Session** — Distributed session with JSON serializer
6. **Static files & routing** — UseStaticFiles, MapPageRoute, middleware pipeline

See [docs/migration/](docs/migration/00-index.md) for the full migration guide.

## Running

```bash
cd CoreWebForms
dotnet run
```

Opens at `http://localhost:8081`.
