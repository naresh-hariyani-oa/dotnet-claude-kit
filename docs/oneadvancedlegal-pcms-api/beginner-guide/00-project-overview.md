# 00 — Project Overview

## What Is This Project?

**oneadvancedlegal-pcms-api** is a .NET 8 Web API that acts as an **integration layer** on top of the PCMS (Practice and Case Management System) — a legal practice management platform used by law firms.

The API exists so that external integrations (document management systems, portals, mobile apps, third-party tooling) can interact with PCMS data through a stable, versioned, authenticated REST interface — rather than touching the legacy PCMS database directly.

## Who Uses It?

| Consumer | What they use it for |
|---|---|
| Internal integrations (SharePoint, cloud storage) | Document upload/download, folder mapping |
| Third-party portals | Client/matter lookups, fee earner queries |
| Automated workflows | Time recording, disbursement, order management |
| Internal tooling | AML status checks, custom field management |

## Key Responsibilities

- Expose PCMS business data as a versioned REST API
- Enforce authentication and authorisation (JWT Bearer / IdentityServer)
- Support multi-tenant firms (each firm has its own database)
- Provide business rules and validation over raw PCMS data
- Translate between API DTOs and internal database entities
- Audit API-level operations for compliance

## What This API Is NOT

- It is **not** a replacement for the PCMS application itself
- It is **not** a direct database proxy — all access goes through service and repository layers
- It is **not** a public-facing consumer API — it is an integration API for controlled consumers

## Technology Snapshot

| Area | Technology |
|---|---|
| Runtime | .NET 8 / C# 12 |
| Web framework | ASP.NET Core Web API |
| ORM | Entity Framework Core 9 |
| Database | SQL Server (via ADO.NET & EF Core) |
| Authentication | JWT Bearer / IdentityServer (Microsoft.Identity.Web) |
| Mapping | AutoMapper 14 |
| Logging | Serilog 9 |
| Caching | In-Memory / Azure Redis (configurable) |
| Testing | xUnit 2.8 + Moq 4.20 + AutoBogus |
| CI/CD | Harness pipelines |
| API documentation | Swagger / Swashbuckle 8 |

## Current API Version

The API is versioned. All live endpoints are at **v1**. Routes follow the pattern:

```
/v1/{resource}
/v1/{resource}/{id}
```

The version is embedded in the URL path (`/v{version:apiVersion}`). Only v1 exists today.

## Solution Overview

The solution is split into two `.sln` files:

| Solution file | Purpose |
|---|---|
| `api/Legal.Pcms.Integration.Api.sln` | Main API — build and run this |
| `api/Legal.Pcms.Integration.Api.Test.sln` | Test suite — run this for tests |

There are approximately **80 C# projects** in total, organised into four layers. See [01-repository-structure.md](01-repository-structure.md) for the full layout.

## Health Check

A health check endpoint is registered at:

```
GET /v{version}/health
```

This is the standard liveness probe used by the deployment infrastructure.
