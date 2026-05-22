# Multi-Tenant Patterns

> Reference for JWT-claims-based multi-tenancy where each tenant (firm) has its own database.
> Used in: `oneadvancedlegal-pcms-api`.

---

## Architecture Overview

Each law firm is a separate tenant. The tenant identity is resolved per HTTP request from the JWT bearer token. The resolved tenant determines which database connection string is used for that request.

```
HTTP Request (with JWT Bearer token)
    │
    ▼
JWT Validation Middleware
    │ Extracts claims including tenant/firm identifier
    ▼
IMultiTenantService.ResolveTenant(HttpContext)
    │ Reads claim → resolves ConnectionString
    ▼
Per-request DbContext / ADO.NET connection
    │ Uses the resolved tenant connection string
    ▼
Tenant-specific SQL Server database
```

---

## JWT Claims Structure

The tenant identifier is embedded as a claim in the JWT. The middleware reads this claim to determine which database to use.

```csharp
// Reading the tenant claim from HttpContext
var orgRef = httpContext.User.FindFirst("organisation_reference")?.Value
    ?? throw new UnauthorizedAccessException("Tenant claim missing from token");
```

---

## IMultiTenantService

This interface is the high-blast-radius core of the multi-tenant system. It resolves a connection string from the tenant identifier.

```csharp
public interface IMultiTenantService
{
    Task<string> GetConnectionString(string orgRef);
    string GetConnectionStringSync(string orgRef);
    Task<Dictionary<string, string>> GetConnectionString();
    Task<TenantInfo> GetTenantDetails(string orgRef);
    Task<List<string>> GetAllTenantOrgRefs();
}
```

The `orgRef` is the `organisation_reference` JWT claim value extracted from the current request via `IRequestService.OrgRef`.

**Warning: Do not modify `IMultiTenantService` or its implementation without:**
1. Understanding all callers (use `find_references` via MCP)
2. Running the complete test suite
3. Testing both development mode (`DevMultiTenantService`) and production mode (`ProdMultiTenantService`)

---

## Local Development Mode

For local development, `DevMultiTenantService` is used (registered when `ASPNETCORE_ENVIRONMENT=Development`). For production, `ProdMultiTenantService` is used. When multi-tenancy is disabled entirely, `BaseMultiTenantService` handles requests.

This is configured in `Program.cs`:
```csharp
if (builder.Configuration.GetValue<bool>("ApplicationOptions:Common:Multitenant"))
{
    if (builder.Environment.IsDevelopment())
        builder.Services.AddSingleton<IMultiTenantService, DevMultiTenantService>();
    else
        builder.Services.AddSingleton<IMultiTenantService, ProdMultiTenantService>();
}
else
{
    builder.Services.AddSingleton<IMultiTenantService, BaseMultiTenantService>();
}
```

**Always use `ApplicationOptions:Common:Multitenant = false` (or DevMultiTenantService) for local development.** Production multi-tenant mode requires the full identity infrastructure and firm registry.

---

## DbContext Registration

`AppDbContext` is configured via `services.SetupDBContext()`, which uses a pooled DbContext factory with lazy loading proxies. The connection string is resolved per-request by passing the `orgRef` (from the JWT claim) to `IMultiTenantService.GetConnectionString(orgRef)` at the repository level — not at DbContext registration time.

`IMultiTenantService` itself is registered as **Singleton** — this is intentional. It maintains an internal cache of connection strings keyed by `orgRef`, so the expensive lookup (e.g., calling the firm registry) only happens once per tenant. Subsequent calls return the cached string immediately.

```csharp
// Singleton is correct — connection string is resolved per-call with orgRef, not at startup
builder.Services.AddSingleton<IMultiTenantService, ProdMultiTenantService>();
```

---

## Audit Trail

Every mutating operation (INSERT/UPDATE/DELETE) must record the tenant context in the audit trail. The tenant ID is available from `IMultiTenantService.GetTenantId()`.

---

## Common Pitfalls

### Pitfall: Tenant claim missing during background jobs

**Cause:** Background jobs and hosted services do not have an `HttpContext`, so there is no JWT claim to resolve the tenant from.

**Fix:** Background jobs that need to access tenant data must receive the tenant ID as a parameter (e.g., from a message queue or job argument) and explicitly set the tenant context rather than relying on JWT claim resolution.

### Pitfall: Hardcoded connection string in a new service

Any new service that accesses the database must use `IMultiTenantService.GetConnectionString()` to get the connection string. Never hardcode or read connection strings directly from `IConfiguration` in services.

---

## Safe Extension Points

**Safe:** Adding a new repository that accepts `IMultiTenantService` via constructor injection and calls `GetConnectionString(orgRef)`.

**Safe:** Reading `IRequestService.OrgRef` to get the current tenant's org reference.

**High risk:**
- `IMultiTenantService` itself and its implementations (`DevMultiTenantService`, `ProdMultiTenantService`) — changes affect every repository in the system
- The JWT claim name `organisation_reference` — changing this breaks authentication for all tenants
- The `orgRef` parameter passed to `GetConnectionString` — must always come from `IRequestService.OrgRef`

---

## Decision Guide

| Scenario | Approach |
|---|---|
| New repository needs DB connection | Inject `IMultiTenantService` + `IRequestService`; call `GetConnectionString(requestService.OrgRef)` |
| Audit trail entry | Use `IRequestService.OrgRef` for the tenant identifier |
| Local development | Set `ApplicationOptions:Common:Multitenant = false` or use `DevMultiTenantService` |
| Background job needs tenant DB | Receive `orgRef` as job parameter; pass explicitly to `GetConnectionString(orgRef)` |
| New service needs multi-tenant access | Inject the repository (which handles connection resolution internally) |
