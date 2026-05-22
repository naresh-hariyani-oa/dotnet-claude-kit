# Developer Onboarding Guide — OneAdvancedLegal PCMS

Welcome to the PCMS codebase. This guide is designed for developers who have recently joined the project and need to understand the architecture, patterns, and standards before making changes.

**Start here before touching any code.**

---

## Guide Index

| # | Document | What you will learn |
|---|---|---|
| [00](00-project-overview.md) | **Project Overview** | What PCMS is, the technology stack, dual-framework targeting, the migration context, and how to navigate the rest of this guide |
| [01](01-repository-structure.md) | **Repository Structure** | Every major folder and project in the repository, what each one is for, and how they relate |
| [02](02-architecture-overview.md) | **Architecture Overview** | The layered architecture, both DI mechanisms (Autofac + Wisej), the microservices layer, data access patterns, and cross-cutting concerns |
| [03](03-design-patterns.md) | **Design Patterns** | Repository, Unit of Work, Autofac Module, Keyed Strategy, AOP Decorator, Factory, and all other patterns used in the codebase |
| [04](04-request-flow.md) | **Request Flow** | Step-by-step flows for startup, login, UI interactions, data access, microservice calls, session timeout, and logout |
| [05](05-build-and-run.md) | **Build and Run** | Prerequisites, build commands, running the application, configuration, test execution, and common build errors |
| [06](06-testing-guide.md) | **Testing Guide** | Test project layout, framework (xUnit + Moq), writing tests, handling singletons in tests, coverage targets |
| [07](07-coding-standards.md) | **Coding Standards** | Naming conventions, class organization, comment style, SQL rules, async/await, security, package management |
| [08](08-debugging-guide.md) | **Debugging Guide** | Visual Studio debugging, reading logs, diagnosing DI issues, data access debugging, common error messages |
| [09](09-common-pitfalls.md) | **Common Pitfalls** | 15 documented mistakes new developers make — read before writing your first PR |
| [10](10-safe-development-workflow.md) | **Safe Development Workflow** | High-risk areas to handle carefully, safe extension points, step-by-step change workflow, suggested learning path |

---

## Also Read

- [`CLAUDE.md`](../../CLAUDE.md) — Authoritative project standards (coding conventions, architecture notes, build commands). **This overrides anything in this guide if there is a conflict.**
- [`CPMReadme.md`](../../CPMReadme.md) — NuGet Central Package Management guide — required reading before adding or updating any package.
- [`docs/OTel_Naming_Convention.md`](../OTel_Naming_Convention.md) — OpenTelemetry span naming standards for new data access code.
- [`docs/performance/README.md`](../performance/README.md) — Performance analysis documentation — useful context for investigating slow features.

---

## Quick Reference

### Main solution file

```
app/Legal.Cloud.sln
```

### Build

```bash
dotnet build app/Legal.Cloud.sln
```

### Run all tests

```bash
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj
```

### Key startup files

| File | Purpose |
|---|---|
| [app/Legal.Cloud/Legal.Cloud/Program.cs](../../app/Legal.Cloud/Legal.Cloud/Program.cs) | Application entry point |
| [app/Legal.Cloud/Legal.Cloud.Main/CloudApplicationStartup.cs](../../app/Legal.Cloud/Legal.Cloud.Main/CloudApplicationStartup.cs) | Service registration and app initialization |
| [app/Source/IRISLegal.Fields/ContainerConfiguration.cs](../../app/Source/IRISLegal.Fields/ContainerConfiguration.cs) | Master Autofac DI container assembly |

### Key configuration files

| File | Purpose |
|---|---|
| [app/Legal.Cloud/Legal.Cloud/Web.config](../../app/Legal.Cloud/Legal.Cloud/Web.config) | IIS / OWIN configuration (.NET Framework 4.8) |
| [app/Legal.Cloud/Legal.Cloud/appsettings.json](../../app/Legal.Cloud/Legal.Cloud/appsettings.json) | Application settings (ASP.NET Core / .NET 7.0) |
| [Directory.Packages.props](../../Directory.Packages.props) | Central NuGet package versions |
