# .NET 10 Migration — Blocked

Migration from `.NET 9` to `.NET 10` is **not currently possible** for this project.

## Primary Blocker: CoreWebForms.Sdk has no .NET 10 release

This project uses `CoreWebForms.Sdk` (pinned to `1.0.0` in `global.json`) as its MSBuild SDK. This SDK:

- Is **not published on NuGet.org** — no public stable release exists
- Targets **.NET 9 only** — `net10.0` TFM is not supported by `1.0.0`
- Has no available version (on either NuGet.org or the `.NET Libraries Daily` Azure feed) that declares `net10.0` support

Changing `<TargetFramework>net9.0</TargetFramework>` to `net10.0` without a compatible SDK version causes build failure at restore time.

## Secondary Blocker: ASPX runtime compilation fails on .NET 10

A migration to .NET 10 was previously attempted (with EF Core bumped to `10.0.9`). It failed due to **ASPX runtime compilation blockers** — the Roslyn-based runtime ASPX compiler bundled in `CoreWebForms.Sdk 1.0.0` is not compatible with the .NET 10 runtime.

`EnableRuntimeAspxCompilation=true` (required for this project) relies on internal compiler APIs that changed between .NET 9 and .NET 10. Until `CoreWebForms.Sdk` ships a version that rebuilds its runtime compiler against .NET 10, ASPX pages will fail to compile at startup.

## What needs to change upstream

| Requirement | Status |
|-------------|--------|
| `CoreWebForms.Sdk` version supporting `net10.0` | Not released |
| Runtime ASPX compiler rebuilt for .NET 10 | Not done |
| Published to NuGet.org or accessible feed | Not done |

## How to check if unblocked

1. Watch https://github.com/corewebforms/corewebforms for a release targeting `net10.0`
2. Check NuGet.org for `CoreWebForms.Sdk` with a version `>1.0.0`
3. When a compatible version ships, follow [the migration plan](../net10-migration-plan.md) and update `global.json` + `CoreWebForms.csproj`

## Files that will need updating when unblocked

| File | Change |
|------|--------|
| `global.json` | `CoreWebForms.Sdk` version → new version |
| `CoreWebForms.csproj` | `net9.0` → `net10.0` |
| `CoreWebForms.csproj` | EF Core `9.0.17` → `10.0.x` |
| `.vscode/launch.json` | exe path `net9.0` → `net10.0` |
