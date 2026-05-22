# 07 — Coding Standards

These conventions are observed consistently across the codebase. Deviating from them introduces inconsistency that reviewers will flag.

---

## Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Classes, methods, properties | PascalCase | `MatterService`, `GetMatterById`, `ClientId` |
| Private fields | `_camelCase` (underscore prefix) | `_matterRepository`, `_mapper` |
| Interfaces | `I` + PascalCase | `IMatterService`, `IRequestService` |
| Async methods | Suffix with `Async` | `GetMatterAsync()`, `CreateMatterAsync()` |
| Constants | `UPPER_SNAKE_CASE` | `DEFAULT_LIMIT`, `MAX_OFFSET` |
| Local variables | camelCase | `matterEntity`, `connectionString` |
| Test methods | `MethodName_Condition_ExpectedResult` | `GetMatter_ShouldReturnNull_WhenNotFound` |

> **Note:** The `Async` suffix convention is declared in standards but not applied 100% consistently in the older parts of the codebase. For new code, always add the suffix on `Task`-returning methods.

---

## Project and Folder Structure Conventions

- One domain = one Business project + one Data project
- All versioned code lives under a `v1/` subfolder
- Interfaces are at `v1/I{Domain}Service.cs` (not in a subfolder)
- Implementations are at `v1/Implementation/{Domain}Service.cs`
- DTOs live in `Legal.Pcms.Integration.Api.Business.Models/{Domain}/v1/`
- Database entities live in `Legal.Pcms.Integration.Api.Data.Models/{Domain}/`
- AutoMapper profiles live in the Business layer alongside their DTOs

---

## API Controller Conventions

Every controller must have these attributes:

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("/v{version:apiVersion}/[controller]")]
[Authorize]
[EnableCors("AllowedOrigins")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces("application/json")]
public class MattersController : ControllerBase
{
    private readonly IMatterService _matterService;

    public MattersController(IMatterService matterService)
    {
        _matterService = matterService;
    }
}
```

Actions should use `[SwaggerResponse]` for documentation and return `ActionResult<T>`:

```csharp
[HttpGet("{matterId}")]
[SwaggerResponse(200, "Matter found", typeof(MatterDto))]
[SwaggerResponse(404, "Matter not found")]
public async Task<ActionResult<MatterDto>> GetMatter(Guid matterId)
{
    var result = await _matterService.GetMatterDetailsById(matterId);
    return Ok(result);
}
```

---

## Pagination Conventions

All list endpoints use `offset`/`limit` pagination with these defaults from `AppConstants`:

```csharp
[HttpGet]
public async Task<ActionResult<PagedResult<MatterDto>>> GetMatters(
    [FromQuery][Range(AppConstants.MIN_OFFSET, AppConstants.MAX_OFFSET)]
    int offset = AppConstants.DEFAULT_OFFSET,      // 0
    [FromQuery][Range(1, AppConstants.PAGINATION_LIMIT_MAX)]
    int limit = AppConstants.DEFAULT_LIMIT)        // 20
```

Do not define your own default/max values — always use `AppConstants`.

---

## Service Logging Convention

Every service method must log its entry and parameters:

```csharp
public async Task<MatterDto> GetMatterDetailsById(Guid matterId)
{
    _logger.LogMethodEntry(nameof(GetMatterDetailsById), matterId);
    // ... rest of method
}
```

Log on exception:

```csharp
catch (Exception ex)
{
    _logger.LogException(nameof(GetMatterDetailsById), ex);
    throw;
}
```

Do not log sensitive data (PII, tokens, passwords). The `Serilog:LogInputParameters` setting controls whether input parameters are logged in production (default: `false`).

---

## Database Access Conventions

### Always use parameterized queries

```csharp
// CORRECT — parameterized
var parameters = new List<SqlParameter>
{
    CreateParameter("@MatterId", matterId, SqlDbType.UniqueIdentifier),
    CreateParameter("@Status", status, SqlDbType.NVarChar, 50)
};
var results = await GetStoredProcedureData<MatterEntity>("sp_GetMatter", parameters);

// WRONG — string interpolation, SQL injection risk
var sql = $"SELECT * FROM Matters WHERE Id = '{matterId}'";
```

Use `BaseRepository.CreateParameter()` — never construct `SqlParameter` manually inline.

### Use stored procedures for complex operations

Legacy PCMS operations use stored procedures. Prefer `CommandType.StoredProcedure` for existing operations. Use `CommandType.Text` for simple queries on the newer tables.

### Transactional operations

When a repository method must write to multiple tables atomically (e.g., `CreateMatterWithRelatedEntitiesAsync` which creates Project + Matter + ProjectMapType + MatterBalance + Label + AuditMatter), wrap in a transaction. Look at `MatterRepository` for the established pattern.

---

## DTO / Entity Separation

Business DTOs and Data entities must remain separate:

```
Business.Models.Matter.v1.Matter      ← DTO (what the API returns/accepts)
Data.Models.Matters.Matter            ← Entity (what the DB stores)
```

Never pass a Data entity up to the controller. Always map through AutoMapper:

```csharp
// In service
var entity = await _repository.Get(id);
return _mapper.Map<MatterDto>(entity);
```

Never pass a Business DTO down to the repository. Map first:

```csharp
// In service
var entity = _mapper.Map<Data.Models.Matters.Matter>(createRequest);
await _repository.Add(entity);
```

---

## Exception Handling Conventions

```csharp
// Not found
throw new ResourceNotFoundException($"Matter {matterId} was not found.");

// Validation failure with field details
throw new BadDataException(new List<BadRequestDetails>
{
    new() { Field = "ClientId", Message = "The specified client does not exist." },
    new() { Field = "FeeEarnerId", Message = "The fee earner is inactive." }
});

// Access denied
throw new ForbiddenException("You do not have access to this matter.");

// Business rule violation
throw new ResourceConflictException("A matter with this reference already exists.");
```

Do not use `try/catch` to swallow exceptions. Do not return `null` from a service when the resource is not found — throw `ResourceNotFoundException`.

---

## Async / Await Conventions

- All I/O methods (database, HTTP) must be `async Task<T>` — no `.Result` or `.Wait()`
- All controller actions must be `async Task<ActionResult<T>>`
- All service methods that call the repository must be `async Task<T>`
- Do not use `async void` — use `async Task`

```csharp
// CORRECT
public async Task<MatterDto> GetMatterDetailsById(Guid matterId)
{
    return await _matterRepository.GetMatterDetailsById(matterId);
}

// WRONG — blocks the thread
public MatterDto GetMatterDetailsById(Guid matterId)
{
    return _matterRepository.GetMatterDetailsById(matterId).Result;
}
```

---

## Configuration Access Conventions

- Access configuration via strongly-typed options classes, not raw `IConfiguration["key"]`
- Connection strings come from `IMultiTenantService.GetConnectionString()` — never from `IConfiguration` directly in a repository
- Never hard-code environment-specific values in source code

---

## AutoMapper Conventions

- One `Profile` class per domain
- Define both directions if both are needed (don't assume AutoMapper reverses automatically unless `.ReverseMap()` is called)
- If a property name differs between DTO and entity, use `.ForMember()` explicitly — do not rely on string similarity
- Never call `_mapper.Map<>()` in a repository — only in services

---

## SQL Parameter Naming

SQL parameter names must match the stored procedure parameter names exactly (including the `@` prefix):

```csharp
CreateParameter("@MatterId", matterId, SqlDbType.UniqueIdentifier)
// matches stored proc: @MatterId UNIQUEIDENTIFIER
```

---

## Constants Usage

Use `AppConstants` for all magic numbers and strings:

```csharp
// CORRECT
if (limit > AppConstants.PAGINATION_LIMIT_MAX) limit = AppConstants.PAGINATION_LIMIT_MAX;
var memberType = AppConstants.MEMBER_TYPE_CLIENT;

// WRONG
if (limit > 100) limit = 100;
var memberType = 1;
```

The full list is in [api/Common/Legal.Pcms.Api.Common/Constants/AppConstants.cs](../../api/Common/Legal.Pcms.Api.Common/Constants/AppConstants.cs).
