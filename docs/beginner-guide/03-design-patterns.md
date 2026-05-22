# 03 — Design Patterns

This document covers the design patterns **actually used in this repository** — both in the MCP server (C# code) and in the patterns the kit teaches for user projects.

---

## Patterns in the MCP Server (`mcp/CWM.RoslynNavigator/`)

### 1. Strategy Pattern — Anti-Pattern Detectors

**Where:** `mcp/CWM.RoslynNavigator/src/Analyzers/`

**How it's implemented:**

An `IAntiPatternDetector` interface defines a contract that all 9 detector implementations follow:

```csharp
public interface IAntiPatternDetector
{
    Task<IEnumerable<AntiPatternInfo>> DetectAsync(
        SyntaxNode root,
        SemanticModel? semanticModel,
        string filePath,
        CancellationToken ct);
}
```

Each detector (`AsyncVoidDetector`, `SyncOverAsyncDetector`, `HttpClientInstantiationDetector`, etc.) implements this interface independently. `DetectAntiPatternsTool` collects all detectors and runs them in parallel.

**Why:** Adding a new detector requires only implementing the interface — no changes to `DetectAntiPatternsTool`. Open/Closed Principle in practice.

**How to extend safely:** Create a new class implementing `IAntiPatternDetector`. Register it in the DI container or in the collection used by `DetectAntiPatternsTool`. Do not modify existing detectors unless fixing a bug.

---

### 2. Hosted Service Pattern — Background Workspace Loading

**Where:** `mcp/CWM.RoslynNavigator/src/WorkspaceInitializer.cs`

**How it's implemented:**

`WorkspaceInitializer` implements `IHostedService` (via `BackgroundService` or direct implementation). It starts the Roslyn workspace loading in the background when the host starts.

```csharp
public sealed class WorkspaceInitializer(WorkspaceManager workspace) : IHostedService
{
    public static string? SolutionPath { get; set; }

    public async Task StartAsync(CancellationToken ct)
    {
        if (SolutionPath is not null)
            await workspace.LoadSolutionAsync(SolutionPath);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**Why:** The MCP server must respond to incoming tool calls immediately, even before the solution finishes loading. A hosted service decouples startup from readiness.

**Key implication for new developers:** All MCP tools check `WorkspaceManager` readiness before doing work. If the workspace isn't ready, they return a `StatusResponse` (not an error). This is intentional — the server never crashes because a solution is loading.

---

### 3. Singleton + LRU Cache Pattern — Compilation Management

**Where:** `mcp/CWM.RoslynNavigator/src/WorkspaceManager.cs`

**How it's implemented:**

`WorkspaceManager` is registered as a singleton. It holds a `ConcurrentDictionary<ProjectId, Compilation>` as an LRU cache with a maximum of 30 entries. An `Interlocked` counter tracks access order. Eviction removes the entry with the lowest access counter.

**Why:** For large solutions (50+ projects), loading all compilations upfront would use excessive memory. The LRU cache ensures recently-accessed projects are warm while cold projects are evicted.

**Key implication:** If you add a new MCP tool that accesses compilations, use the existing `WorkspaceManager.GetCompilationAsync()` method — never open a new `MSBuildWorkspace` directly.

---

### 4. Factory Method via Reflection — MCP Tool Discovery

**Where:** `mcp/CWM.RoslynNavigator/src/Program.cs`

**How it's implemented:**

```csharp
mcpBuilder.WithToolsFromAssembly();
```

The MCP SDK scans the executing assembly for static methods decorated with `[McpServerTool]` on classes decorated with `[McpServerToolType]`. Tool registration is automatic — no manual wiring required.

**Why:** Follows the same auto-discovery philosophy as `IEndpointGroup` in the kit's web API pattern. Adding a new tool requires creating a new file, not modifying `Program.cs`.

**How to extend safely:** Create a new static class in `Tools/`, decorate it with `[McpServerToolType]`, add a static method with `[McpServerTool(Name = "tool_name")]`. The tool is discovered automatically on next build.

---

### 5. Record Types for DTOs — Response Formatting

**Where:** `mcp/CWM.RoslynNavigator/src/Responses/ToolResponses.cs`

**How it's implemented:**

All MCP tool responses are C# records:

```csharp
record SymbolLocation(string Name, string Kind, string File, int Line, string Namespace);
record SymbolSearchResult(List<SymbolLocation> Symbols);
record ReferenceLocation(string File, int Line, string Snippet, string Kind);
record ReferencesResult(List<ReferenceLocation> References, int Count);
```

Short property names are intentional — they reduce JSON payload size, which directly reduces token consumption in Claude.

**Why:** Records give value equality, immutability, and with-expressions for free, with minimal boilerplate. The short property names reflect the token budget discipline the kit enforces.

---

## Patterns the Kit Teaches for User Projects

### 6. Result Pattern — Explicit Failure Handling

**Where taught:** `skills/error-handling/SKILL.md`, `knowledge/common-infrastructure.md`, `.claude/rules/error-handling.md`

**How it's implemented (copy from `knowledge/common-infrastructure.md`):**

```csharp
public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public List<string> Errors { get; }

    protected Result(bool isSuccess, List<string>? errors = null)
    {
        IsSuccess = isSuccess;
        Errors = errors ?? [];
    }

    public static Result Success() => new(true);
    public static Result Failure(params string[] errors) => new(false, [..errors]);
    public static Result<T> Success<T>(T value) => new(value);
    public static Result<T> Failure<T>(params string[] errors) => new(errors);
}

public class Result<T> : Result
{
    public T Value { get; }
    internal Result(T value) : base(true) => Value = value;
    internal Result(IEnumerable<string> errors) : base(false, [..errors]) => Value = default!;
}
```

**Why:** Exceptions are expensive and hide control flow. `Result<T>` makes "this operation can fail" explicit in the type signature. Services that return `Result<T>` cannot be called without the caller handling both success and failure paths.

**How to extend:** Add typed error codes (e.g., `ErrorCode.NotFound`, `ErrorCode.Conflict`) to the `Result` type to enable consistent HTTP status code mapping.

---

### 7. Endpoint Group Auto-Discovery Pattern

**Where taught:** `.claude/rules/architecture.md`, `knowledge/common-infrastructure.md`, `skills/minimal-api/SKILL.md`

**How it's implemented:**

```csharp
// IEndpointGroup.cs — place in Shared/Common project
public interface IEndpointGroup
{
    void Map(IEndpointRouteBuilder app);
}

// EndpointExtensions.cs — auto-discovers all IEndpointGroup implementations
public static class EndpointExtensions
{
    public static IEndpointRouteBuilder MapEndpoints(this IEndpointRouteBuilder app, ...)
    {
        // Reflection: find all non-abstract types implementing IEndpointGroup
        // Instantiate each and call .Map(app)
        return app;
    }
}

// Program.cs — single call
app.MapEndpoints();

// OrderEndpoints.cs — auto-discovered
public sealed class OrderEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders");
        group.MapGet("/", ListOrders);
        group.MapPost("/", CreateOrder);
    }
}
```

**Why:** `Program.cs` never changes when you add new endpoints. Features are self-registering.

**Warning:** This uses reflection at startup. In AOT (Native AOT) scenarios, reflection-based discovery must be replaced with source-generated registration. Check the `aspire` skill for Aspire-specific patterns.

---

### 8. Options Pattern — Typed Configuration

**Where taught:** `skills/configuration/SKILL.md`, `.claude/rules/security.md`

**How it's implemented:**

```csharp
// Options class
public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public required string Secret { get; init; }
    public required string Issuer { get; init; }
    public int ExpirationMinutes { get; init; } = 60;
}

// Registration with validation
builder.Services
    .AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Injection
public sealed class JwtService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _opts = options.Value;
}
```

**Why:** Typed access to configuration over raw `IConfiguration["Key"]` strings. Validation at startup catches missing configuration before the app serves any requests.

---

### 9. Decorator Pattern via Scrutor — Service Enhancement

**Where taught:** `skills/dependency-injection/SKILL.md`

**How it's implemented:**

```csharp
// Add Scrutor NuGet package, then:
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.Decorate<IOrderService, LoggingOrderService>();
builder.Services.Decorate<IOrderService, CachingOrderService>();
```

**Why:** Adds cross-cutting concerns (logging, caching, metrics) to a service without modifying its class. The outer decorator wraps the inner one transparently.

**When to use:** When you want to add logging or caching to an existing service without inheritance or modifying the original class.

---

### 10. Outbox Pattern — Reliable Messaging

**Where taught:** `skills/messaging/SKILL.md`

**How it's implemented (with Wolverine):**

```csharp
// Within a transaction:
await dbContext.SaveChangesAsync();  // 1. Persist entity
await bus.PublishAsync(new OrderPlaced(order.Id));  // 2. Publish message (stored in outbox)
// Wolverine delivers the message asynchronously after commit
```

**Why:** Without an outbox, publishing a message and saving to the database are two separate operations. If the database commit succeeds but the message broker is down, the message is lost. The outbox pattern stores messages in the database transaction and delivers them asynchronously.

---

### 11. Vertical Slice Handler Pattern (CQRS-lite)

**Where taught:** `skills/vertical-slice/SKILL.md`

**How it's implemented:**

```csharp
// All code for "Create Order" lives in one file
// Features/Orders/CreateOrder.cs

// Command (DTO)
public sealed record CreateOrderCommand(string ProductId, int Quantity);

// Validator
public sealed class CreateOrderValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.ProductId).NotEmpty();
    }
}

// Handler (the use case)
public sealed class CreateOrderHandler(AppDbContext db, TimeProvider clock)
{
    public async Task<Result<Guid>> HandleAsync(
        CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order(cmd.ProductId, cmd.Quantity, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return Result.Success(order.Id);
    }
}

// Endpoint
public sealed class OrderEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/orders")
           .MapPost("/", CreateOrderAsync)
           .AddEndpointFilter<ValidationFilter<CreateOrderCommand>>();
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderCommand cmd,
        CreateOrderHandler handler,
        CancellationToken ct)
    {
        var result = await handler.HandleAsync(cmd, ct);
        return result.IsSuccess
            ? TypedResults.Created($"/api/orders/{result.Value}", result.Value)
            : result.ToProblemDetails();
    }
}
```

**Why:** All code for one feature is in one place. No jumping between Controllers/, Services/, Validators/, DTOs/ directories for a single feature change.

---

## Pattern Catalog Quick Reference

| Pattern | Location | Purpose |
|---------|----------|---------|
| Strategy (IAntiPatternDetector) | `mcp/.../Analyzers/` | Pluggable anti-pattern detectors |
| Hosted Service | `mcp/.../WorkspaceInitializer.cs` | Non-blocking background workspace load |
| LRU Cache (Singleton) | `mcp/.../WorkspaceManager.cs` | Bounded compilation cache |
| Auto-Discovery (Reflection) | `mcp/.../Program.cs` + `IEndpointGroup` | Zero-registration tool/endpoint wiring |
| Result Pattern | `knowledge/common-infrastructure.md` | Explicit failure handling |
| Options Pattern | `skills/configuration/SKILL.md` | Typed configuration with validation |
| Decorator (Scrutor) | `skills/dependency-injection/SKILL.md` | Cross-cutting service concerns |
| Outbox | `skills/messaging/SKILL.md` | Reliable async message delivery |
| Vertical Slice Handler | `skills/vertical-slice/SKILL.md` | Feature-collocated CQRS-lite |
| Pagination | `knowledge/common-infrastructure.md` | Standard paged query/response |

---

## Patterns Explicitly Prohibited

The kit rules forbid these patterns because they add indirection without value:

| Prohibited Pattern | Rule | Alternative |
|--------------------|------|-------------|
| `IRepository<T>` over EF Core | `.claude/rules/architecture.md` | Inject `DbContext` directly |
| `new HttpClient()` | `.claude/rules/performance.md` | `IHttpClientFactory` |
| `DateTime.Now` / `DateTime.UtcNow` | `.claude/rules/performance.md` | `TimeProvider.GetUtcNow()` |
| Repository pattern for unit-testable data access | `.claude/rules/architecture.md` | Testcontainers + real DB |
| `IMemoryCache` manual set/get | `.claude/rules/performance.md` | `HybridCache.GetOrCreateAsync()` |
| Generic endpoint registration in `Program.cs` | `.claude/rules/architecture.md` | `IEndpointGroup` + `app.MapEndpoints()` |
