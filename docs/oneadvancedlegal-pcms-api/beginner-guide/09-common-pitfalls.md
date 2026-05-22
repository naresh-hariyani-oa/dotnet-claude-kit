# 09 — Common Pitfalls

This document lists mistakes that are easy to make and difficult to debug. Read it before making your first PR.

---

## 1. Forgetting to Register in `Program.cs`

**What happens:** The API compiles fine, but at runtime you get a `InvalidOperationException: Unable to resolve service for type...`

**Cause:** You added a new service or repository but forgot to register it:

```csharp
// Required in Program.cs
builder.Services.AddScoped<IMyNewRepository, MyNewRepository>();
builder.Services.AddScoped<IMyNewService, MyNewService>();
builder.Services.AddAutoMapper(typeof(MyNewProfile));
```

**Rule:** Any new `I{Domain}Service`, `I{Domain}Repository`, or `{Domain}Profile` must be registered in `Program.cs` before the API will start.

---

## 2. Missing AutoMapper Mapping Direction

**What happens:** `AutoMapperMappingException` at runtime when the service tries to map a type.

**Cause:** You defined a mapping `A → B` but called `_mapper.Map<A>(bInstance)` (the reverse direction).

```csharp
// Profile — only one direction
CreateMap<MatterEntity, MatterDto>();

// Service — tries reverse mapping
var entity = _mapper.Map<MatterEntity>(matterDto);  // throws!
```

**Fix:** Add `.ReverseMap()` or an explicit reverse `CreateMap<MatterDto, MatterEntity>()`.

---

## 3. String-Interpolated SQL (SQL Injection Risk)

**What happens:** Works at runtime but is a critical security vulnerability that will fail code review.

```csharp
// WRONG — SQL injection vulnerability
var sql = $"SELECT * FROM Matters WHERE ClientId = '{clientId}'";

// CORRECT — always use CreateParameter()
var parameters = new List<SqlParameter>
{
    CreateParameter("@ClientId", clientId, SqlDbType.UniqueIdentifier)
};
```

Every SQL query must use `CreateParameter()` helpers from `BaseRepository`. This is non-negotiable.

---

## 4. Calling `.Result` or `.Wait()` on an Async Method

**What happens:** Potential deadlocks, especially under load. This is an async code smell that blocks the thread pool.

```csharp
// WRONG — blocks thread, risks deadlock
var matter = _matterRepository.GetMatterDetailsById(matterId).Result;

// CORRECT — properly async
var matter = await _matterRepository.GetMatterDetailsById(matterId);
```

Never use `.Result` or `.Wait()` anywhere in this codebase.

---

## 5. Accessing a Data Entity from the Controller Layer

**What happens:** Compiles, but violates the layered architecture. Business logic leaks out of the service layer.

```csharp
// WRONG — controller knows about Data entities
var matterEntity = await _matterRepository.Get(id);  // can't even inject repository here!
return Ok(matterEntity);

// CORRECT — controller only knows about DTOs
var matterDto = await _matterService.GetMatterDetailsById(id);
return Ok(matterDto);
```

Controllers inject services only. Services inject repositories only. This is enforced by the project structure — controllers cannot reference Data projects.

---

## 6. Returning `null` Instead of Throwing `ResourceNotFoundException`

**What happens:** The controller receives `null`, returns `Ok(null)` which serialises to an empty response body with a 200 status. The client interprets this as success but gets no data.

```csharp
// WRONG — swallows the not-found condition
public async Task<MatterDto> GetMatter(Guid matterId)
{
    var entity = await _repository.Get(matterId);
    if (entity == null) return null;  // caller gets 200 OK with empty body
    return _mapper.Map<MatterDto>(entity);
}

// CORRECT — throws, middleware returns 404
public async Task<MatterDto> GetMatter(Guid matterId)
{
    var entity = await _repository.Get(matterId);
    if (entity == null) throw new ResourceNotFoundException($"Matter {matterId} not found.");
    return _mapper.Map<MatterDto>(entity);
}
```

---

## 7. Skipping `_logger.LogMethodEntry()` in a Service Method

**What happens:** The call appears in request logs but no service-layer context is recorded. Debugging production issues becomes much harder.

Every service method must start with:
```csharp
_logger.LogMethodEntry(nameof(YourMethod), parameter1, parameter2);
```

---

## 8. Multi-Tenant Confusion: Wrong Tenant Data Returned

**What happens:** Data from the wrong firm is returned, or a 500 error occurs because the connection string cannot be resolved.

**Cause:** The JWT's `organisation_reference` claim maps to `IRequestService.OrgRef`. If a test token has the wrong `OrgRef`, it will route to the wrong database.

**Debug steps:**
1. Decode the JWT and check the `organisation_reference` claim
2. Verify `IMultiTenantService` has an entry for that `OrgRef`
3. In local dev with `Multitenant: false`, all requests use the fixed connection string regardless of `OrgRef`

---

## 9. Modifying Common Infrastructure Without Running All Tests

**What happens:** A change to `BaseRepository`, `BaseService`, `AppDbContext`, or any middleware breaks behaviour across multiple domains.

**Rule:** Before merging any change to a project in `api/Common/`, run:
```bash
dotnet test api/Legal.Pcms.Integration.Api.Test.sln
```

The Common layer has the highest blast radius in the solution.

---

## 10. Adding a New `DbSet` Without Configuring Entity Relationships

**What happens:** EF Core migrations or queries fail with mapping errors.

If you add a `DbSet<T>` to `AppDbContext`, also:
1. Configure primary key (if non-convention)
2. Add `HasTrigger()` for tables that have database triggers (see existing `OnModelCreating`)
3. Configure composite keys for entities that need them (see `GeneralContactIdentityStatus` example)

---

## 11. Using `DateTime.Now` Instead of `DateTime.UtcNow`

**What happens:** Timestamp inconsistencies, especially when the API is deployed in Azure (UTC) but connected to a SQL Server in a different timezone.

Use `DateTime.UtcNow` for all timestamps. The constant `AppConstants.UTC_DATETIME_FORMAT = "yyyy-MM-ddTHH:mm:ss.fffZ"` defines the standard format.

---

## 12. Creating a New Profile for a Domain That Already Has One

**What happens:** Two profiles for the same type — AutoMapper becomes non-deterministic about which mapping to use.

Before creating a `{Domain}Profile`, check if one already exists in `api/Business/Legal.Pcms.Integration.Api.Business.{Domain}/v1/`.

---

## 13. Pagination Parameters Outside Allowed Range

**What happens:** `[Range]` validation rejects the request with a 400. This is intentional, but surprising if you don't know the limits.

| Parameter | Min | Max | Default |
|---|---|---|---|
| `offset` | 0 | 1,000,000,000 | 0 |
| `limit` | 1 | 100 | 20 |

Never accept a `limit` of 0 or greater than 100 from an API consumer.

---

## 14. Hard-Coding Magic Numbers

**What happens:** Review comment, and the value is scattered across the codebase.

Use `AppConstants` — it covers billing types, member types, AML statuses, application types, date limits, and more. If a constant is missing, add it to `AppConstants` rather than embedding it inline.
