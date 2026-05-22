# 02 — Architecture Overview

## Architecture Style: Layered Architecture

This codebase follows a **classic three-layer architecture**. The layers enforce a strict dependency direction:

```
┌─────────────────────────────────────────┐
│             API Layer                   │  ← HTTP Controllers (IntegrationApi)
│       Receives HTTP, returns JSON       │
└──────────────────┬──────────────────────┘
                   │ calls
┌──────────────────▼──────────────────────┐
│           Business Layer                │  ← Services (Business.{Domain})
│   Business rules, validation, mapping  │
└──────────────────┬──────────────────────┘
                   │ calls
┌──────────────────▼──────────────────────┐
│             Data Layer                  │  ← Repositories (Data.{Domain})
│     SQL queries, EF Core, ADO.NET      │
└──────────────────┬──────────────────────┘
                   │ reads/writes
┌──────────────────▼──────────────────────┐
│            SQL Server                   │  ← PCMS database(s)
└─────────────────────────────────────────┘
```

**Rule:** Dependencies only flow **downward**. Business never imports Data directly by class name — it uses repository interfaces. Controllers never import Data at all.

## Multi-Tenancy Architecture

The API serves **multiple law firms** (tenants) from a single deployment. Each firm has its own SQL Server database.

```
Incoming request
      │
      │  JWT claim: organisation_reference = "FIRM_A"
      ▼
IRequestService.OrgRef ──────────────────────────┐
                                                  ▼
                                    IMultiTenantService.GetConnectionString(orgRef)
                                                  │
                              ┌───────────────────┴───────────────────┐
                              ▼                                        ▼
                      FIRM_A Database                         FIRM_B Database
```

The correct database is resolved **per request** at the repository layer by calling `IMultiTenantService.GetConnectionString()`. The tenant key (`OrgRef`) is extracted from the authenticated JWT token by `IRequestService`.

Three implementations of `IMultiTenantService` exist:
- `BaseMultiTenantService` — single-tenant (local dev without multi-tenancy)
- `DevMultiTenantService` — development multi-tenant
- `ProdMultiTenantService` — production multi-tenant

The active implementation is selected in `Program.cs` based on the `ApplicationOptions:Common:Multitenant` config flag.

## Dual Database Access Strategy

Two ORM/database approaches coexist in the Data layer:

| Approach | When used | How |
|---|---|---|
| **EF Core** (`AppDbContext`) | Complex queries, LINQ, entity graphs | Inject `IDbContextFactory<AppDbContext>` |
| **ADO.NET via BaseRepository** | Stored procedures, raw SQL, bulk ops | Call `GetStoredProcedureData()` or `ExecuteStoredProcedureData()` |

This is intentional — the legacy PCMS database has many stored procedures and triggers. The EF Core `AppDbContext` uses **trigger awareness** on several tables (Matter, AuditMatter, Project, Label) so EF Core does not conflict with existing database triggers.

There is also a **separate database** for Custom Fields, accessed via `CustomFieldsDbContext`. Its connection string is retrieved from an environment variable or Azure Key Vault at startup.

## API Versioning

All endpoints are versioned at the URL path level:

```
/v1/matters
/v1/clients/{id}
```

The version is declared on each controller with `[ApiVersion("1.0")]` and the route template `/v{version:apiVersion}`. Only v1 exists today. When a v2 is introduced, both will coexist.

## Authentication Architecture

All endpoints require a valid JWT Bearer token (except the health check).

```
Client ──── Bearer <JWT> ──► ExceptionHandlingMiddleware
                              │
                              ▼
                       AuthenticationMiddleware
                       (validates JWT against IdentityServer)
                              │
                              ▼
                       AuthorizationMiddleware
                       ([Authorize] on controller)
                              │
                              ▼
                       IRequestService
                       (extracts OrgRef, UserId, Permissions from claims)
```

The `IRequestService` is injected into services and repositories. It provides the tenant reference (`OrgRef`), user identity, and the raw Bearer token for downstream calls.

## Configuration Architecture

Configuration is layered:

```
appsettings.json           ← base values (checked into repo)
     +
appsettings.{env}.json     ← environment overrides
     +
User Secrets               ← local developer secrets (never committed)
     +
Azure App Configuration    ← cloud config (when Using=1)
     +
Environment Variables      ← container/deployment overrides
```

All settings are strongly typed via `ApplicationOptions`. Never hard-code connection strings or secrets.

## Caching Architecture

Caching is optional and configurable:

| Setting (`CachingService:Using`) | Behaviour |
|---|---|
| `0` | Disabled — all reads go to the database |
| `1` | In-Memory — local process cache (default in Development) |
| `2` | Azure Redis — distributed cache for multi-instance deployments |

Services use a caching abstraction — they do not reference the cache implementation directly.

## Middleware Pipeline Order

The middleware pipeline is defined in `Program.cs` via `UseCommonApiApplications()`. Order matters:

```
1. ExceptionHandlingMiddleware    ← catches all unhandled exceptions, maps to ProblemDetails
2. Extended Request Logging       ← Serilog request/response logging
3. IpValidateMiddleware           ← blocks requests from non-whitelisted IPs
4. CustomHeadersMiddleware        ← strips/adds HTTP headers
5. HTTPS Redirection              ← when HttpsOnly=true
6. Security Headers               ← CSP, X-Frame-Options, etc.
7. Swagger                        ← API documentation UI
8. Routing
9. CORS
10. Authentication                ← JWT validation
11. Authorization                 ← [Authorize] enforcement
12. Controller Endpoint Mapping
```

> **Why order matters:** `ExceptionHandlingMiddleware` must be first so it can catch exceptions thrown by any later middleware. Authentication must come before Authorization.

## Logging Architecture

Serilog is configured with three log categories:

| Category | Namespace | Purpose |
|---|---|---|
| `BUS` | `Legal.Pcms.Integration.Api.Business` | Service-layer operations |
| `DTA` | `Legal.Pcms.Integration.Api.Data` | Repository-layer SQL operations |
| `SYS` | `Microsoft.*` | Framework logs |

Each service logs **method entry** with serialized input parameters and **exceptions** with full context. This is the standard convention — new code must follow it.

Correlation IDs are generated per request by `ExceptionHandlingMiddleware` (using `X-Correlation-ID` header if present, otherwise a new GUID). They appear in every log line for request tracing.

## What Makes This Codebase Different

1. **Domain isolation via projects, not just folders.** Each domain is a separate `.csproj`. This makes it impossible to accidentally import internal implementation details across domains.

2. **80+ projects, not a monolith.** This feels heavy initially but it enforces boundaries at compile time.

3. **Two database technologies in the same solution.** EF Core handles complex entity relationships; ADO.NET/stored procedures handle legacy PCMS operations.

4. **Multi-tenancy at the repository level.** The connection string is resolved on every database call, not at startup. This is unusual but intentional for per-request tenant isolation.
