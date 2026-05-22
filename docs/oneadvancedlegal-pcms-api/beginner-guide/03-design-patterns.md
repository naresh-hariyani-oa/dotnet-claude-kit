# 03 — Design Patterns

This document describes the design patterns actually in use in this codebase, where they live, and how to work within them correctly.

---

## 1. Repository Pattern

**Where:** Every `Data/{Domain}/v1/Implementation/{Domain}Repository.cs`

**What it does:** Hides all SQL Server access behind an interface. Business code never writes SQL — it calls a repository method.

**How it works:**

```csharp
// Interface in Data layer
public interface IMatterRepository : IBaseRepository<Matter, Guid>
{
    Task<MatterDetails> GetMatterDetailsById(Guid matterId);
    Task<bool> IsMatterAuthorizedForIntegrator(Guid matterId, string integratorKey);
    // ...
}

// Implementation inherits BaseRepository
public class MatterRepository : BaseRepository<Matter, Guid>, IMatterRepository
{
    public MatterRepository(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<MatterRepository> logger,
        IConfiguration configuration,
        IRequestService requestService,
        IMultiTenantService multiTenantService)
        : base(dbContextFactory, logger, configuration, requestService, multiTenantService)
    { }

    public async Task<MatterDetails> GetMatterDetailsById(Guid matterId)
    {
        // Uses BaseRepository helpers for SQL execution
    }
}
```

**How to extend safely:**
- Add a new method to `I{Domain}Repository` interface first
- Implement it in `{Domain}Repository`
- Always use `CreateParameter()` helpers for SQL parameters — never string-interpolate SQL
- Register in `Program.cs` if it is a new domain

---

## 2. Service Layer Pattern

**Where:** Every `Business/{Domain}/v1/Implementation/{Domain}Service.cs`

**What it does:** Holds business rules, orchestrates repository calls, performs DTO mapping. The controller is thin; the service is the unit of logic.

**How it works:**

```csharp
// Interface in Business layer
public interface IMatterService
{
    Task<MatterDto> GetMatterDetailsById(Guid matterId);
    Task CreateMatterAsync(CreateMatterRequest request);
    // ...
}

// Implementation inherits BaseService
public class MatterService : BaseService<MatterDto, Data.Models.Matters.Matter, Guid>, IMatterService
{
    private readonly IMatterRepository _matterRepository;
    private readonly IMapper _mapper;
    private readonly IBaseLogger _logger;

    public MatterService(
        ILogger<MatterService> logger,
        IMapper mapper,
        IMatterRepository matterRepository)
        : base(logger, mapper, matterRepository)
    {
        _matterRepository = matterRepository;
        _mapper = mapper;
        _logger = new BaseLogger(logger);
    }

    public async Task<MatterDto> GetMatterDetailsById(Guid matterId)
    {
        _logger.LogMethodEntry(nameof(GetMatterDetailsById), matterId);
        // ... logic ...
    }
}
```

**How to extend safely:**
- New service methods go in the interface first, then the implementation
- Always call `_logger.LogMethodEntry()` at the start of each method
- Always use `_mapper.Map<>()` when converting between DTO and entity types
- Do not access `AppDbContext` directly from a service — go through the repository

---

## 3. BaseRepository — Template Method Pattern

**Where:** `api/Common/Legal.Pcms.Api.Common.Data/v1/Implementation/BaseRepository.cs`

**What it does:** Provides pre-built CRUD operations, SQL execution helpers, parameter factories, and connection management. Repositories call these inherited methods instead of writing boilerplate.

**Key methods provided:**
| Method | What it does |
|---|---|
| `Add()` / `Get()` / `GetAll()` / `Delete()` / `Update()` | Generic async CRUD via stored procedures or SQL |
| `GetStoredProcedureData<T>()` | Executes a stored proc and maps result to a list of `T` |
| `ExecuteStoredProcedureData()` | Executes a stored proc with no return (INSERT/UPDATE/DELETE) |
| `CreateParameter()` | Creates a typed `SqlParameter` — use this, never `new SqlParameter("@p", value)` raw |
| `CreateParametersForInClause()` | Creates parameterized `IN (...)` lists safely |
| `CreateIdTableParameter()` | Creates a table-valued parameter for bulk ID lookups |
| `GetConnectionString()` | Resolves tenant-specific connection string from `IMultiTenantService` |

**Why this matters:** Every repository inheriting `BaseRepository` automatically gets connection management, multi-tenant routing, and consistent error wrapping. Do not bypass it.

---

## 4. Dependency Injection (Constructor Injection)

**Where:** Everywhere. This is the universal wiring mechanism.

**How it works:** `Program.cs` registers all services, repositories, and infrastructure. ASP.NET Core resolves constructor parameters automatically.

```csharp
// In Program.cs
builder.Services.AddScoped<IMatterRepository, MatterRepository>();
builder.Services.AddScoped<IMatterService, MatterService>();
builder.Services.AddAutoMapper(typeof(MatterProfile));

// In controller constructor
public MattersController(IMatterService matterService)
{
    _matterService = matterService;
}
```

**Lifetime rules:**
| Lifetime | Used for |
|---|---|
| `AddScoped` | Domain services and repositories (one instance per HTTP request) |
| `AddSingleton` | `IMultiTenantService` — shared, stateless, no per-request state |
| `AddTransient` | Short-lived utilities |

**How to extend safely:** When adding a new domain, register its `IService`/`IRepository` pair in `Program.cs` as `AddScoped`. Never use `new` to create services — always inject.

---

## 5. AutoMapper — DTO/Entity Separation

**Where:** `Business/{Domain}/v1/{Domain}Profile.cs`

**What it does:** Maps between Business DTOs (what the API returns) and Data entities (what the database stores). The two models are intentionally separate.

```csharp
// Profile example
public class MatterProfile : Profile
{
    public MatterProfile()
    {
        CreateMap<Data.Models.Matters.Matter, Business.Models.Matter.v1.Matter>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.ProjectId));
        // reverse map if needed
        CreateMap<Business.Models.Matter.v1.CreateMatterRequest, Data.Models.Matters.Matter>();
    }
}
```

**How to extend safely:**
- Add new mappings to the existing profile for the domain — do not create a second profile for the same domain
- Register the profile in `Program.cs`: `builder.Services.AddAutoMapper(typeof(MatterProfile))`
- Avoid `.Ignore()` unless intentional — missing mappings fail at runtime validation

---

## 6. Custom Exception Hierarchy — Strategy Pattern for Error Responses

**Where:** `api/Common/Legal.Pcms.Api.Common/Exceptions/`

**What it does:** Every business error is thrown as a typed exception. `ExceptionHandlingMiddleware` catches it and converts it to the correct HTTP status code and ProblemDetails response — no `try/catch` in controllers needed.

```
BaseException (abstract)
├── BadDataException          → 400 (includes List<BadRequestDetails>)
├── UnauthorizedException     → 401
├── ForbiddenException        → 403
├── ResourceNotFoundException → 404
├── NotAcceptableException    → 406
├── ResourceConflictException → 409 (includes errors list)
├── PreconditionFailedException → 412
├── UnSupportedMediaTypeException → 415
├── TooManyRequestsException  → 429 (includes Retry-After)
├── InternalServerErrorException → 500
└── NotImplementedException   → 501
```

**How to use:**
```csharp
// In a service method
var matter = await _matterRepository.Get(matterId);
if (matter == null)
    throw new ResourceNotFoundException($"Matter {matterId} not found.");

// Validation error with details
throw new BadDataException(new List<BadRequestDetails>
{
    new() { Field = "ClientId", Message = "Client does not exist." }
});
```

**How to extend safely:**
- Always use an existing exception type if one fits
- Do not create new exception types without discussing with the team — the middleware mapping must be updated too
- Never catch `BaseException` in a service and swallow it

---

## 7. Middleware Pipeline Pattern

**Where:** `api/Common/Legal.Pcms.Api.Common.Web/Middlewares/`

**What it does:** Cross-cutting concerns (security headers, IP validation, exception translation, logging) are implemented as ASP.NET Core middleware rather than being duplicated in every controller.

Key middleware:
- `ExceptionHandlingMiddleware` — converts any `BaseException` to RFC 7807 ProblemDetails JSON
- `SecurityHeadersMiddleware` — injects CSP, X-Frame-Options, etc. from config
- `IpValidateMiddleware` — checks the remote IP against a whitelist; throws `ForbiddenException` if blocked

**How to extend safely:**
- Add new cross-cutting concerns as middleware, not as base controller methods
- Register new middleware in `UseCommonApiApplications()` in the correct position (see [02-architecture-overview.md](02-architecture-overview.md) for pipeline order)
- Middleware changes affect every request — test thoroughly

---

## 8. Multi-Tenant Strategy Pattern

**Where:** `api/Common/Legal.Pcms.Api.Common.Data/v1/IMultiTenantService.cs` + implementations

**What it does:** Abstracts tenant-specific connection string resolution behind an interface. Three concrete strategies exist for different environments.

```csharp
public interface IMultiTenantService
{
    string GetConnectionString(string orgRef);
    Dictionary<string, string> GetConnectionString();
    TenantDetails GetTenantDetails(string orgRef);
}
```

**How to extend safely:**
- Do not change `IMultiTenantService` unless you are modifying all three implementations
- Do not bypass it to hard-code a connection string anywhere

---

## 9. Correlation ID Pattern

**Where:** `ExceptionHandlingMiddleware`

**What it does:** Every HTTP request is assigned a correlation ID (from `X-Correlation-ID` header, or a new GUID). This ID propagates through all log lines for the request, enabling end-to-end tracing.

**How to use:** Access it via `IRequestService.CorrelationId`. It is automatically included in 500 error responses so consumers can reference it when reporting issues.

---

## Summary: When Implementing New Functionality

| Layer | Pattern to follow |
|---|---|
| New endpoint | Add method to `I{Domain}Service`, implement it, call from controller |
| New DB query | Add method to `I{Domain}Repository`, implement using `BaseRepository` helpers |
| New error condition | Throw appropriate `BaseException` subclass |
| New DTO/entity mapping | Add `CreateMap<>` to the domain's `{Domain}Profile` |
| New DI registration | Add to `Program.cs` as `AddScoped<IFoo, Foo>()` |
| Cross-cutting concern | Implement as middleware, register in pipeline |
