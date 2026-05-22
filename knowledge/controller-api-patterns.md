# Controller-Based API Patterns

> Reference for traditional ASP.NET Core Controller APIs using `[ApiController]`.
> Used in: `oneadvancedlegal-pcms-api` (.NET 8, ~80 projects, 30+ business domains).

---

## Controller Conventions

Every controller in pcms-api requires these attributes. Do not omit any of them.

```csharp
[ApiController]
[ApiVersion("1.0")]
[Route("/v{version:apiVersion}")]
[Authorize]
[EnableCors("AllowedOrigins")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class OrderController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IOrderService orderService, ILogger<OrderController> logger)
    {
        _orderService = orderService;
        _logger = logger;
    }
}
```

**Attribute meanings:**
- `[ApiController]` — enables automatic model validation, binding, and ProblemDetails for 400 responses
- `[ApiVersion("1.0")]` — maps this controller to API version 1.0
- `[Route("v{version:apiVersion}/[controller]")]` — URL: `/v1/order`
- `[Authorize]` — ALL controllers require authentication unless explicitly marked `[AllowAnonymous]`
- `[EnableCors]` — required for cross-origin browser clients
- `[Consumes]` / `[Produces]` — explicit content type declaration for OpenAPI docs

---

## Layer Responsibilities

| Layer | Responsibility | What it must NOT do |
|---|---|---|
| Controller | Receive HTTP request, call service, return HTTP response | Access repository directly; contain business logic |
| Service | Business logic, orchestration, mapping (via AutoMapper) | Access HTTP context; call other controllers |
| Repository | Database access only (EF Core queries or stored procedures) | Contain business logic; call services |

### Controller action pattern

```csharp
[HttpGet("{id:int}")]
[ProducesResponseType(typeof(OrderDto), StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetOrderAsync(int id, CancellationToken cancellationToken)
{
    _logger.LogInformation("Getting order {OrderId}", id);
    var result = await _orderService.GetOrderAsync(id, cancellationToken);
    return Ok(result);
    // Note: ResourceNotFoundException → 404 is handled by ExceptionHandlingMiddleware
}
```

### Service method pattern

```csharp
public class OrderService : IOrderService
{
    private readonly IOrderRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IOrderRepository repository, IMapper mapper,
                        ILogger<OrderService> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<OrderDto> GetOrderAsync(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"{AppConstants.ExecutingServiceMethod}{nameof(GetOrderAsync)}");

        var order = await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {id} not found");

        return _mapper.Map<OrderDto>(order);
    }
}
```

### Repository method pattern

```csharp
public class OrderRepository : BaseRepository<Order>, IOrderRepository
{
    public OrderRepository(AppDbContext context) : base(context) { }

    public async Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await Context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    // For stored procedures: always use parameterized queries
    public async Task<IEnumerable<Order>> GetOrdersByFirmAsync(int firmId)
    {
        var param = CreateParameter("@FirmId", firmId);  // BaseRepository helper
        return await ExecuteStoredProcedureAsync("sp_GetOrdersByFirm", param);
    }
}
```

---

## Error Handling Flow

Controllers never wrap calls in try-catch. The exception hierarchy + middleware handles all error mapping:

```
Service throws ResourceNotFoundException("Order 42 not found")
    │
    ▼
ExceptionHandlingMiddleware.InvokeAsync()
    │ Catches ResourceNotFoundException
    ▼
Maps to: 404 ProblemDetails response
    {
      "type": "...",
      "title": "Not Found",
      "status": 404,
      "detail": "Order 42 not found"
    }
```

**Exception → HTTP status mapping:**

| Exception | HTTP Status |
|---|---|
| `ResourceNotFoundException` | 404 Not Found |
| `BadDataException` | 400 Bad Request |
| `ForbiddenException` | 403 Forbidden |
| `ResourceConflictException` | 409 Conflict |
| `UnauthorizedException` | 401 Unauthorized |
| `PreconditionFailedException` | 412 Precondition Failed |
| `TooManyRequestsException` | 429 Too Many Requests |
| Any unhandled `Exception` | 500 Internal Server Error |

---

## Logging Convention

Every service method must log at method entry and exit using `AppConstants` log markers:

```csharp
public async Task<OrderDto> GetOrderAsync(int id, CancellationToken ct)
{
    _logger.LogInformation($"{AppConstants.ExecutingServiceMethod}{nameof(GetOrderAsync)}");
    // ... method body
    _logger.LogInformation($"{AppConstants.ExecutedServiceMethod}{nameof(GetOrderAsync)}");
    return result;
}
```

Log categories in pcms-api:
- `BUS` — business layer (services)
- `DTA` — data layer (repositories)
- `SYS` — infrastructure (middleware, startup)

---

## Pagination Conventions

List endpoints use offset/limit pagination with values from `AppConstants`:

```csharp
[HttpGet]
public async Task<IActionResult> GetOrdersAsync(
    [FromQuery] int offset = AppConstants.DefaultOffset,
    [FromQuery] int limit = AppConstants.DefaultLimit)
{
    _logger.LogMethodEntry();
    var results = await _orderService.GetOrdersAsync(offset, limit);
    return Ok(results);
}
```

Never invent custom pagination parameters — use the values defined in `AppConstants`.

---

## Dependency Registration

New services and repositories must be registered in `Program.cs` (or the relevant DI registration method):

```csharp
// Services — Scoped lifetime (one per HTTP request)
builder.Services.AddScoped<IOrderService, OrderService>();

// Repositories — Scoped lifetime
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// AutoMapper profiles — discovered from assembly automatically
builder.Services.AddAutoMapper(Assembly.GetExecutingAssembly());
```

**Common pitfall:** Adding a new service/repository but forgetting to register it → `InvalidOperationException: Unable to resolve service for type 'IOrderService'` at runtime.

---

## Decision Guide

| Scenario | Approach |
|---|---|
| New endpoint for existing domain | Add action to existing `{Domain}Controller` |
| New business domain | Create `{Domain}Controller`, `I{Domain}Service`, `{Domain}Service`, `I{Domain}Repository`, `{Domain}Repository`, `{Domain}Profile`, `{Domain}ServiceTest` |
| 404 when entity not found | `throw new ResourceNotFoundException(...)` in service |
| Input validation | Model validation (data annotations) + FluentValidation; `[ApiController]` returns 400 automatically |
| Returning a list | `Ok(IEnumerable<T>)` with offset/limit pagination |
| File upload/download | Inject storage service; never store binary in DB |
| Authenticated-only endpoint | Default — all controllers have `[Authorize]`; add `[AllowAnonymous]` only if intentional |
