# Phase 1: Project Setup

Replace the .NET Framework 4.8 project with a CoreWebForms.Sdk project targeting .NET 9.

**Reference commit**: `f760a49`

## Steps

### 1. Copy the legacy project as a starting point

Copy the entire legacy project directory to serve as the new project. This preserves all pages, controls, code-behind, and assets.

```
cp -r LegacyWebForms/ CoreWebForms/
```

### 2. Replace the .csproj file

Delete the old `LegacyWebForms.csproj` and create a new `CoreWebForms.csproj` using the `CoreWebForms.Sdk`.

**Before** (`LegacyWebForms.csproj`):
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>latest</LangVersion>
    <RootNamespace>LegacyWebForms</RootNamespace>
    <AssemblyName>LegacyWebForms</AssemblyName>
    <OutputType>Library</OutputType>
    <OutputPath>bin\</OutputPath>
    <Nullable>enable</Nullable>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="3.1.32" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="3.1.32" />
    <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.6" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="System.Configuration" />
    <Reference Include="System" />
    <Reference Include="System.Core" />
    <Reference Include="System.Data" />
    <Reference Include="System.Drawing" />
    <Reference Include="System.Web" />
    <Reference Include="System.Web.ApplicationServices" />
    <Reference Include="System.Web.DynamicData" />
    <Reference Include="System.Web.Entity" />
    <Reference Include="System.Web.Extensions" />
    <Reference Include="System.Web.Services" />
    <Reference Include="System.Xml" />
  </ItemGroup>

</Project>
```

**After** (`CoreWebForms.csproj`):
```xml
<Project Sdk="CoreWebForms.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <EnableRuntimeAspxCompilation>true</EnableRuntimeAspxCompilation>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <RootNamespace>CoreWebForms</RootNamespace>
    <AssemblyName>CoreWebForms</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Using Include="System" />
    <Using Include="System.Collections.Generic" />
    <Using Include="System.IO" />
    <Using Include="System.Linq" />
    <Using Include="System.Net.Http" />
    <Using Include="System.Threading" />
    <Using Include="System.Threading.Tasks" />
    <Using Include="Microsoft.AspNetCore.Http.HttpContext" Alias="HttpContextCore" />
    <Using Include="Microsoft.AspNetCore.Http.HttpResponse" Alias="HttpResponseCore" />
    <Using Include="Microsoft.AspNetCore.Http.HttpRequest" Alias="HttpRequestCore" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.17" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.17" />
  </ItemGroup>

</Project>
```

**Key changes**:
- SDK: `Microsoft.NET.Sdk` → `CoreWebForms.Sdk`
- Target: `net48` → `net9.0`
- `OutputType=Library` removed — CoreWebForms SDK produces an executable
- `OutputPath=bin\` removed — replaced by `Directory.Build.props` output path
- `LangVersion` removed — .NET 9 default is sufficient
- `AppendTargetFrameworkToOutputPath` / `AppendRuntimeIdentifierToOutputPath` removed — handled by `Directory.Build.props`
- `ImplicitUsings=disable` — `ImplicitUsings=enable` would pull in ASP.NET Core types that conflict with `System.Web` types; all namespaces must be explicit
- `EnableRuntimeAspxCompilation=true` — ASPX files compiled at runtime by Roslyn via CoreWebForms.Sdk
- `GenerateAssemblyInfo=false` — preserve existing `AssemblyInfo.cs`
- All `<Reference>` items removed — `System.Web.*` provided by CoreWebForms.Sdk
- `SQLitePCLRaw.bundle_e_sqlite3` removed — EF Core 9 / Microsoft.Data.Sqlite bundles native SQLite automatically; explicit bundle was only needed on EF Core 3.x + .NET Framework
- `<Using>` items replace framework implicit namespaces; alias usings disambiguate `System.Web.HttpContext` from `Microsoft.AspNetCore.Http.HttpContext`

### 3. Pin the CoreWebForms.Sdk version

Create `global.json` in the project root to pin the SDK version:

```json
{
  "msbuild-sdks": {
    "CoreWebForms.Sdk": "1.0.0"
  }
}
```

### 4. Configure NuGet sources

Create `nuget.config` with standard sources:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
    <add key=".NET Libraries Daily" value="https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-libraries/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

### 5. Configure build output paths (optional)

Create `Directory.Build.props` to customize output paths:

```xml
<Project>
  <PropertyGroup>
    <BaseIntermediateOutputPath>..\artifacts\obj\$(Configuration)\</BaseIntermediateOutputPath>
    <OutputPath>..\artifacts\bin\$(Configuration)\net$(TargetFramework)\</OutputPath>
  </PropertyGroup>
</Project>
```

### 6. Rename namespace across all files

Rename the root namespace from `LegacyWebForms` to `CoreWebForms` in all `.cs` files:

```bash
find . -name '*.cs' -exec sed -i 's/namespace LegacyWebForms/namespace CoreWebForms/g' {} +
find . -name '*.cs' -exec sed -i 's/using LegacyWebForms/using CoreWebForms/g' {} +
```

## Verification

```bash
dotnet build CoreWebForms.csproj
```

The project should restore NuGet packages and compile without errors.
