# 05 — Build and Run

## Prerequisites

| Tool | Minimum version | Notes |
|---|---|---|
| .NET SDK | 8.0 | Download from microsoft.com/dotnet |
| SQL Server | 2016+ | Local or remote; connection string required |
| Git | Any | For cloning and branching |
| IDE | VS 2022 or VS Code | VS 2022 17.8+ recommended for .NET 8 |

Verify your SDK version:

```bash
dotnet --version
# Expected: 8.0.x
```

---

## First-Time Setup

### 1. Clone the repository

```bash
git clone <repo-url>
cd oneadvancedlegal-pcms-api
```

### 2. Set up User Secrets (connection strings)

**Do not** put connection strings in `appsettings.Development.json` — that file is committed to git.

Use .NET User Secrets instead. The project already has a `UserSecretsId` configured.

```bash
cd api/IntegrationApi

# Set a direct connection string (single-tenant mode)
dotnet user-secrets set "ConnectionStrings:Database" "Server=localhost;Database=PCMS_Dev;Trusted_Connection=True;"

# Set the Custom Fields database connection string
dotnet user-secrets set "ConnectionStrings:CustomFieldsDatabase" "Server=localhost;Database=PCMS_CustomFields_Dev;Trusted_Connection=True;"
```

For multi-tenant mode, the connection strings are resolved via `IMultiTenantService` — ask your team lead for the tenant configuration format.

### 3. Configure single-tenant mode for local development

In `appsettings.Development.json`, confirm:

```json
"ApplicationOptions": {
  "Common": {
    "Multitenant": false
  }
}
```

Setting `Multitenant` to `false` uses `BaseMultiTenantService`, which reads the fixed `ConnectionStrings:Database` value — simpler for local dev.

### 4. Restore packages

```bash
dotnet restore api/Legal.Pcms.Integration.Api.sln
```

---

## Building

### Build the main API solution

```bash
dotnet build api/Legal.Pcms.Integration.Api.sln
```

### Build the test solution

```bash
dotnet build api/Legal.Pcms.Integration.Api.Test.sln
```

### Build a specific project

```bash
dotnet build api/Business/Legal.Pcms.Integration.Api.Business.Matter/Legal.Pcms.Integration.Api.Business.Matter.csproj
```

### Release build (matches CI)

```bash
dotnet build api/Legal.Pcms.Integration.Api.sln --configuration Release
```

---

## Running the API

### From the IntegrationApi directory

```bash
cd api/IntegrationApi
dotnet run
```

### With hot reload (recommended for active development)

```bash
cd api/IntegrationApi
dotnet watch run
```

### From the repo root

```bash
dotnet run --project api/IntegrationApi/Legal.Pcms.Integration.Api.csproj
```

The API starts on:
- **HTTPS:** `https://localhost:7186`
- **HTTP:** (redirected to HTTPS when `HttpsOnly=true`)

Swagger UI is available at:
```
https://localhost:7186/swagger
```

Health check:
```
https://localhost:7186/v1/health
```

---

## Running Tests

### Run all tests

```bash
dotnet test api/Legal.Pcms.Integration.Api.Test.sln
```

### Run only Business layer tests

```bash
dotnet test api/Test/Business/Legal.Pcms.Integration.Api.Business.Test.csproj
```

### Run only Data layer tests

```bash
dotnet test api/Test/Data/Legal.Pcms.Integration.Api.Data.Test.csproj
```

### Run tests with coverage

```bash
dotnet test api/Legal.Pcms.Integration.Api.Test.sln /p:CollectCoverage=true
```

### Run tests filtered by domain (using Trait)

```bash
# Run only Matter service tests
dotnet test api/Test/Business/Legal.Pcms.Integration.Api.Business.Test.csproj \
  --filter "MatterService"

# Run tests for a specific scenario
dotnet test api/Test/Business/Legal.Pcms.Integration.Api.Business.Test.csproj \
  --filter "AccountsService=billingCodes"
```

---

## Configuration Reference

### Key settings in `appsettings.json` / `appsettings.Development.json`

| Setting | Purpose | Typical dev value |
|---|---|---|
| `ConnectionStrings:Database` | Main PCMS database | Set via User Secrets |
| `ApplicationOptions:Common:Multitenant` | Enable multi-tenant mode | `false` for local dev |
| `ApplicationOptions:Common:IpSafeList` | IP whitelist (semicolon-delimited) | Empty = allow all |
| `ApplicationOptions:CachingService:Using` | Cache mode: 0=off, 1=in-memory, 2=Redis | `1` |
| `ApplicationOptions:Common:HttpsOnly` | Force HTTPS redirect | `true` |
| `Serilog:MinimumLevel:Default` | Log verbosity | `Debug` |
| `ApplicationOptions:ConfigService:Using` | 0=disabled, 1=Azure App Config | `0` |

### Disabling IP validation locally

If requests are blocked with 403 during local development, check `ApplicationOptions:Common:IpSafeList`. An empty string allows all IPs. A populated string restricts access.

---

## Common Startup Issues

### Issue: SSL certificate not trusted

```bash
dotnet dev-certs https --trust
```

### Issue: Port 7186 already in use

Edit `api/IntegrationApi/Properties/launchSettings.json` and change the port number.

### Issue: 401 on all requests

The JWT Bearer token is required. Use Swagger UI's "Authorize" button to input a valid Bearer token. Ask your team lead for a development token or how to obtain one from IdentityServer.

### Issue: 403 from IpValidateMiddleware

Your local IP is not in the safe list. In `appsettings.Development.json`, set `IpSafeList` to an empty string to disable IP filtering locally.

### Issue: Connection string not found

Confirm User Secrets are set correctly:
```bash
cd api/IntegrationApi
dotnet user-secrets list
```

### Issue: AutoMapper configuration error at startup

A mapping is missing or misconfigured. Check the startup error message — it will name the type pair that failed. The fix is to add or correct the mapping in the relevant `{Domain}Profile`.

---

## CI/CD Pipeline Overview

Pipelines are defined in `.harness/*.yaml` and run on Harness CI:

| Pipeline file | Trigger | Purpose |
|---|---|---|
| `apibuildmain.yaml` | Push to `main` | Build + artifact publish |
| `apibuildmainpr.yaml` | Pull request | Build + test validation |
| `deployapianddbnonprod.yaml` | Manual / merge | Deploy to non-prod |

The build pipeline uses the `mcr.microsoft.com/dotnet/sdk:8.0` Docker image and runs:
1. `dotnet restore`
2. `dotnet build --configuration Release`
3. `dotnet publish` (IntegrationApi project)

Tests (`UNIT_TEST_ENABLE`) can be toggled in the pipeline variables. SonarQube (`SONAR_ENABLE`) is also configurable.

---

## Debugging in Visual Studio / VS Code

### Visual Studio 2022

Open `api/Legal.Pcms.Integration.Api.sln`. Set `IntegrationApi` as the startup project. Press F5. Swagger opens automatically.

### VS Code

Use the `ms-dotnettools.csharp` extension. A `launch.json` targeting the `IntegrationApi` project should be configured. Use "Run and Debug" → ".NET Core Launch".

### Useful Serilog log categories for debugging

| Category prefix | Set level | What you see |
|---|---|---|
| `Legal.Pcms.Integration.Api.Business` | `Debug` | Service method entries, parameters |
| `Legal.Pcms.Integration.Api.Data` | `Debug` | SQL queries and parameters |
| `Microsoft.EntityFrameworkCore.Database.Command` | `Information` | EF Core generated SQL |
