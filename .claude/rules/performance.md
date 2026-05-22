---
alwaysApply: true
description: >
  Enforces performance best practices for .NET applications including async
  patterns, time handling, resource management, and hot-path optimizations.
---

# Performance Rules

## Async Patterns

- **Always propagate `CancellationToken` through the call chain.** Dropped tokens mean cancelled requests continue burning server resources.

```csharp
// DO
public Task<Order?> GetOrderAsync(Guid id, CancellationToken ct) =>
    db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

// DON'T — token silently ignored
public Task<Order?> GetOrderAsync(Guid id, CancellationToken ct) =>
    db.Orders.FirstOrDefaultAsync(o => o.Id == id);
```

- **Async all the way — no `.Result` or `.Wait()`.** Synchronously blocking on async code causes thread pool starvation and deadlocks. The only acceptable sync-over-async location is `Program.cs` top-level statements.

## Time and Clock

- **`TimeProvider` over `DateTime.Now` / `DateTime.UtcNow`.** `TimeProvider` is injectable and testable. `DateTime.Now` is a static dependency that makes time-sensitive logic untestable. When `TimeProvider` is not yet wired in an existing project, use `DateTime.UtcNow` (not `DateTime.Now`) as a minimum.

```csharp
// BEST — injectable, testable
public sealed class AuditService(TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();
}

// ACCEPTABLE in legacy code — at least UTC-consistent
var now = DateTime.UtcNow;

// DON'T — local time, non-deterministic
var now = DateTime.Now;
```

## Resource Management

- **`IHttpClientFactory` over `new HttpClient()`.** Direct instantiation causes socket exhaustion under load. The factory manages connection pooling and DNS rotation.
- **Use `ArrayPool<T>` / `MemoryPool<T>` for buffer-heavy operations.** Renting from a pool avoids GC pressure from frequent large allocations.

## Caching

- **For new projects:** prefer `HybridCache` for stampede protection and L1+L2 caching out of the box.
- **For existing projects with `IMemoryCache` or `IDistributedCache` (Redis):** work within the established caching infrastructure. Do not suggest replacing working production caching.

## EF Core and Hot Paths

- **Use compiled queries for hot-path EF Core queries.** Compiled queries skip expression tree translation on every call.

```csharp
private static readonly Func<AppDbContext, Guid, CancellationToken, Task<Order?>> GetById =
    EF.CompileAsyncQuery((AppDbContext db, Guid id, CancellationToken ct) =>
        db.Orders.FirstOrDefault(o => o.Id == id));
```

- **Prefer `ValueTask<T>` over `Task<T>` for high-throughput paths that often complete synchronously.** Use `Task` for general-purpose code where simplicity matters more.
