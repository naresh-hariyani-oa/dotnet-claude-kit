---
alwaysApply: true
description: >
  Enforces correct NuGet package version management for .NET projects,
  including Central Package Management (CPM) awareness.
---

# Package Management Rules

## Always Use Latest Stable Versions

- **Never hardcode package versions from memory.** Training data contains outdated versions. Always verify the latest stable version before adding a package.
- **Run `dotnet add package <name>` without a `--version` flag** to automatically pull the latest stable release from NuGet.org. This is the safest default.

```bash
# DO — gets latest stable automatically
dotnet add package Serilog.AspNetCore
dotnet add package FluentValidation
dotnet add package AutoMapper

# DON'T — hardcoded version likely outdated
dotnet add package Serilog.AspNetCore --version 8.0.0
```

- **For Microsoft.* packages, match the target framework.** .NET 8 projects use 8.x; .NET 10 projects use 10.x. Examples: `Microsoft.EntityFrameworkCore` 8.x for a `net8.0` project.
- **When writing `<PackageReference>` in .csproj files**, use `dotnet add package` first to resolve the correct version, then copy it into the project file.

## Central Package Management (CPM)

- **When a solution uses `Directory.Packages.props`** with `<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`, all version pins live in `Directory.Packages.props` only.
  - Individual `.csproj` files must NOT include `Version=` on `<PackageReference>` elements.
  - All version additions and changes go to `Directory.Packages.props`, not to `.csproj` files.
  - Running `dotnet add package` adds a version to the `.csproj` — move it to `Directory.Packages.props` and remove it from the `.csproj`.

```xml
<!-- Directory.Packages.props — version lives here -->
<PackageVersion Include="Serilog.AspNetCore" Version="8.0.3" />

<!-- .csproj — NO Version attribute when CPM is active -->
<PackageReference Include="Serilog.AspNetCore" />
```

- **For solutions without CPM:** specify the version in the `.csproj` as resolved by `dotnet add package`.

## Version Verification

- If unsure about the latest version, suggest the user verify on NuGet.org or run `dotnet package search <name>`.
- **Never downgrade a package** that is already in the project unless explicitly asked or there is a known compatibility issue.
- Prefer release versions over preview/RC unless the project explicitly targets preview features.
