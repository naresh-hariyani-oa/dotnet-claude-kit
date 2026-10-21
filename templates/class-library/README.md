# Class Library Template

## When to Use

Use this template when you are building:
- A reusable class library distributed as a NuGet package
- A shared utilities or abstractions library consumed by other projects in a solution
- An SDK or client library for an API or service
- A domain-specific library (parsing, validation, calculations, etc.)

## How to Use

1. Copy `CLAUDE.md` into the root of your .NET class library project
2. Replace `[ProjectName]` with your actual project name
3. Replace `[Author]`, `[Description]`, `[org]`, `[repo]`, and tag placeholders in the NuGet package configuration section
4. Update the **Tech Stack** section to match your actual dependencies
5. Remove any optional items (BenchmarkDotNet, Verify, DI abstractions) that do not apply
6. Remove any skills references that are not relevant to your project

## What's Included

This template configures Claude Code to:
- Maintain a clean public API surface with internal implementation details
- Require XML documentation on all public members
- Use .NET 10 / C# 14 modern patterns (primary constructors, collection expressions, records)
- Return immutable collections from public APIs
- Follow semantic versioning discipline for public API changes
- Pack and publish NuGet packages with SourceLink and symbol packages
- Write tests with xUnit v3
- Minimize third-party dependencies to reduce consumer burden

## Customization

### Adding DI Registration Extensions

If your library integrates with `Microsoft.Extensions.DependencyInjection`, add an `Extensions/ServiceCollectionExtensions.cs` file with `Add[LibraryName]()` methods. Reference `Microsoft.Extensions.DependencyInjection.Abstractions` (not the full DI package) to keep the dependency lightweight.

### Enabling Strong Naming

If consumers require strong-named assemblies, add to your `.csproj`:

```xml
<PropertyGroup>
  <SignAssembly>true</SignAssembly>
  <AssemblyOriginatorKeyFile>key.snk</AssemblyOriginatorKeyFile>
</PropertyGroup>
```

### Multi-targeting

To support multiple .NET versions, replace the single `TargetFramework` with:

```xml
<PropertyGroup>
  <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
</PropertyGroup>
```

Use `#if NET10_0_OR_GREATER` preprocessor directives for APIs only available in newer frameworks.

### Adding Benchmarks

Add a `benchmarks/[ProjectName].Benchmarks/` project using `BenchmarkDotNet` to track performance regressions.
