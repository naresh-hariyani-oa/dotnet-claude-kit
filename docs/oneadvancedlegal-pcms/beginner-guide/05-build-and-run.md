# 05 — Build and Run

## Prerequisites

Before building, ensure you have the following installed:

| Tool | Version | Purpose |
|---|---|---|
| **Visual Studio 2022** | Latest | Primary IDE (recommended) |
| **.NET Framework 4.8 Developer Pack** | 4.8 | Required for net48 target |
| **.NET 7.0 SDK** | 7.x | Required for net7.0 target and `dotnet` CLI |
| **NuGet CLI** | Latest | For restoring packages (CI/CD builds) |
| **MSBuild** | 17+ (shipped with VS 2022) | For full solution builds |
| **SQL Server** | 2019+ (or Developer Edition) | Database (local or remote) |
| **SQL Server Management Studio** | Latest | Optional — database inspection |

> **Note:** The application cannot run with only .NET 7.0. The .NET Framework 4.8 Developer Pack is required because the production runtime is IIS-hosted with .NET Framework 4.8 (OWIN pipeline).

---

## Opening the Solution

Always use the **main solution file**:

```
app/Legal.Cloud.sln
```

Do not use other `.sln` files unless you are working exclusively on that module (e.g., microservices). Opening the wrong solution will cause missing project reference errors.

---

## Building

### Development Build (Recommended for Local Development)

```bash
dotnet build app/Legal.Cloud.sln
```

This is sufficient for local development. It builds both `net48` and `net7.0` targets.

### Full CI/CD Build (MSBuild — as used in Harness pipeline)

```bash
# Step 1: Restore NuGet packages
nuget restore ./app/Legal.Cloud.sln -PackagesDirectory "./src/NugetPackages"

# Step 2: Build with MSBuild
msbuild ./app/Legal.Cloud/Legal.Cloud/Legal.Cloud.csproj \
    /t:Build \
    /p:Configuration=Release \
    /p:BuildProjectReferences=true \
    /p:GenerateSerializationAssemblies=Off \
    /v:q
```

> The MSBuild command only compiles the main `Legal.Cloud.csproj`. Other projects are compiled as build dependencies via `/p:BuildProjectReferences=true`.

---

## Package Restoration

The project uses **Central Package Management (CPM)**. Package versions are defined in [`Directory.Packages.props`](../../Directory.Packages.props) at the repo root.

### Development

In Visual Studio, packages restore automatically when you open the solution. If they don't:

```bash
dotnet restore app/Legal.Cloud.sln
```

### CI/CD (NuGet CLI)

```bash
nuget restore ./app/Legal.Cloud.sln -PackagesDirectory "./src/NugetPackages"
```

### DevExpress and Wisej Packages

These are served from a **local NuGet feed** at `app/Packages/`. Visual Studio should pick this up automatically via the `NuGet.Config` at the solution root. If packages are missing:
1. Check that `app/Packages/DevExpress/` and `app/Packages/Wisej.NET/` contain the expected `.nupkg` files.
2. Verify your Visual Studio NuGet sources include this local feed.

### Grafana OpenTelemetry DLLs

These are **NOT NuGet packages**. They are DLL files in `app/Legal.Cloud/Legal.Cloud/otel/netfx/`. They are copied to the build output automatically by an MSBuild target. No action needed.

---

## Running the Application

The application runs as a standard IIS-hosted .NET Framework 4.8 application using OWIN. There is no `dotnet run` for the main application.

### Option 1: IIS Express via Visual Studio

1. Set `Legal.Cloud` as the startup project.
2. Press F5 (Debug) or Ctrl+F5 (without debug).
3. IIS Express launches automatically.

### Option 2: Full IIS

1. Create a new IIS site pointing to the `Legal.Cloud` output directory.
2. Set the application pool to .NET Framework 4.8, Integrated pipeline.
3. Set environment variables (see Environment Variables section below).

### Required Environment Variables

Before starting the application, set these environment variables (in IIS, or as system variables for development):

```
PCMS_HOSTNAME=<your hostname>
DEFAULT_DB_PARAM_NAME=<your db connection string name>
GRAPH_TENANT_DOMAIN=<Azure AD tenant domain>
GRAPH_AUTH_CLIENT_ID=<Azure app registration client ID>
GRAPH_AUTH_KEY=<Azure auth key>
GRAPH_TENANT=<Azure tenant ID>
```

For local development without Azure, the `GRAPH_*` variables can be left unset — cloud integrations (Exchange, Office 365) will be unavailable but the core application will still start.

### Database Setup

1. Obtain a copy of the development database (ask your team for a backup or restore script).
2. Restore it to your local SQL Server instance.
3. Update the connection string in `Web.config` or set the `DEFAULT_DB_PARAM_NAME` environment variable.

The `app/SqlScripts/onboarding/` directory contains demo data scripts for setting up a development environment.

---

## Configuration Reference

### `Web.config` (Runtime — .NET Framework 4.8)

Located at [`app/Legal.Cloud/Legal.Cloud/Web.config`](../../app/Legal.Cloud/Legal.Cloud/Web.config).

Key settings developers need to know:

| Key | Purpose |
|---|---|
| `Wisej.LicenseKey` | Wisej framework license — do not modify |
| `Wisej.DefaultTheme` | Default UI theme (`LegalCloud-Light`) |
| `FlowDesignerUrl` | URL for workflow designer (`https://flow.oneadvanced.io/`) |

### `appsettings.json` (ASP.NET Core settings)

Located at [`app/Legal.Cloud/Legal.Cloud/appsettings.json`](../../app/Legal.Cloud/Legal.Cloud/appsettings.json).

Key settings:

```json
{
  "Account": {
    "Name": "ACME Solicitors",
    "PendoActive": true
  },
  "Theme": "Light",
  "StartAction": "CreateDashboard",
  "Exchange": {
    "DB": "...",
    "Domain": "..."
  }
}
```

Environment variables override values in `appsettings.json` at runtime. See [`ConfigurationManager.cs`](../../app/Legal.Cloud/Legal.Cloud.Main/Services/ConfigurationManager.cs) for which keys can be overridden.

---

## Running Unit Tests

All tests are in one test project:

```bash
# Run all tests
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj

# Run all tests with code coverage
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj \
    --verbosity quiet \
    /p:CollectCoverage=true \
    /p:CoverletOutputFormat=opencover

# Run tests for a specific class
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj \
    --filter FullyQualifiedName~BrMatterTests

# Run tests matching a name pattern
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj \
    --filter Name~LoadMatter
```

Tests target **net48 only**. They require .NET Framework 4.8 to run.

---

## Building Microservices (Solicitors.WebApi)

The microservices build separately:

```bash
# Restore and build the microservices solution
dotnet restore app/Solicitors.WebApi/Solicitors.WebApi.sln
dotnet build app/Solicitors.WebApi/Solicitors.WebApi.sln
```

Each microservice is packaged as a NuGet package and deployed independently into WFServer.

---

## CI/CD Pipeline Overview

The Harness pipelines (`.harness/`) run on every PR and on merge to `main`:

| Stage | What it does |
|---|---|
| **Security Tests** | SonarQube static analysis (can be skipped for emergency fixes) |
| **Queue** | Ensures only one build runs at a time (avoids race conditions) |
| **Build** | MSBuild on a Windows VM pool |
| **Quality** | Additional SonarQube analysis |
| **Update Version** | Bumps version variables (non-prod environments only) |

GitHub Actions (`.github/workflows/`) runs additionally:
- `CI-Sonar.yaml` — SonarQube on push to main
- `CI-Fossa.yaml` — License compliance check

---

## Common Build Issues

### "NU1008: Projects that use central package version management should not define the version"

You added a `Version="..."` attribute to a `<PackageReference>` in a `.csproj`. Remove it and ensure the version is in `Directory.Packages.props` instead.

### Missing DevExpress / Wisej packages

The local NuGet feed in `app/Packages/` is not configured. Check your NuGet sources in Visual Studio → Tools → Options → NuGet Package Manager → Package Sources.

### Build fails on "Could not load file or assembly OpenTelemetry"

The Grafana OTel DLLs in `app/Legal.Cloud/Legal.Cloud/otel/netfx/` are not present or not being copied. Verify the files exist and check the MSBuild `CopyGrafanaOpenTelemetryDlls` target in `Legal.Cloud.csproj`.

### `dotnet test` fails with platform errors

The test project targets `net48`. Ensure .NET Framework 4.8 Developer Pack is installed. On Windows, tests run natively. On Linux/Mac, you need `mono` or a Windows environment.

### "The type initializer for 'ApplicationSettings' threw an exception"

The database connection string is not configured or the database is not accessible. Check your connection strings in `Web.config` or environment variables.
