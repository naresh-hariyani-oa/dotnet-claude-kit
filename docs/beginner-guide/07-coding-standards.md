# 07 — Coding Standards

This document describes the coding conventions enforced in this repository. These are not suggestions — they are enforced by `.editorconfig`, the format hook, and the `.claude/rules/coding-style.md` rule file.

---

## C# Conventions

### File Organization

**One type per file.** The file name must match the type name exactly:

```
OrderService.cs       → contains OrderService
OrderEndpoints.cs     → contains OrderEndpoints
IOrderRepository.cs   → contains IOrderRepository
```

**File-scoped namespaces.** No block-scoped namespaces:

```csharp
// CORRECT
namespace MyProject.Features.Orders;

public sealed class OrderService { }

// WRONG — wastes indentation
namespace MyProject.Features.Orders
{
    public sealed class OrderService { }
}
```

**Member ordering within a class:**

1. Constants
2. Fields (private)
3. Constructors
4. Properties
5. Public methods
6. Private methods

### Type Declarations

**Primary constructors for DI injection.** Eliminates field boilerplate:

```csharp
// CORRECT
public sealed class OrderService(AppDbContext db, TimeProvider clock, ILogger<OrderService> logger)
{
    public async Task<Result<Guid>> CreateAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = new Order(cmd.ProductId, cmd.Quantity, clock.GetUtcNow());
        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);
        return Result.Success(order.Id);
    }
}

// WRONG — field assignment ceremony
public class OrderService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public OrderService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }
}
```

**`sealed` on classes not designed for inheritance:**

```csharp
// CORRECT — most classes should be sealed
public sealed class OrderService(AppDbContext db) { }

// Wrong intent — only remove sealed if you specifically need inheritance
public class OrderService(AppDbContext db) { }
```

**Records for DTOs and value objects:**

```csharp
// CORRECT — immutable, value equality, with-expression support
public sealed record CreateOrderRequest(string ProductId, int Quantity);
public sealed record Money(decimal Amount, string Currency);

// WRONG for DTOs — class adds unnecessary mutability and boilerplate
public class CreateOrderRequest
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
```

**`internal` by default, `public` only when needed:**

```csharp
// CORRECT — nothing outside this project references it
internal sealed class PricingCalculator { }

// WRONG — public for no reason; pollutes the assembly's API surface
public sealed class PricingCalculator { }
```

### Expressions and Patterns

**Collection expressions (C# 12+):**

```csharp
// CORRECT
List<int> ids = [1, 2, 3];
int[] arr = [4, 5, 6];
var empty = Array.Empty<string>(); // or [] when type is inferred

// WRONG
var ids = new List<int> { 1, 2, 3 };
var arr = new int[] { 4, 5, 6 };
```

**Pattern matching over if-else chains:**

```csharp
// CORRECT
var label = status switch
{
    OrderStatus.Pending => "Awaiting payment",
    OrderStatus.Shipped => "On the way",
    OrderStatus.Delivered => "Delivered",
    _ => "Unknown"
};

// WRONG
string label;
if (status == OrderStatus.Pending) label = "Awaiting payment";
else if (status == OrderStatus.Shipped) label = "On the way";
else if (status == OrderStatus.Delivered) label = "Delivered";
else label = "Unknown";
```

**`var` when type is obvious, explicit when it adds clarity:**

```csharp
// CORRECT — type is self-evident from right side
var order = new Order(productId, quantity);
var orders = await db.Orders.ToListAsync(ct);

// CORRECT — explicit type adds clarity (what does this method return?)
HttpResponseMessage response = await client.GetAsync("/api/orders", ct);
Result<Guid> result = await handler.HandleAsync(cmd, ct);

// WRONG — var obscures the type when it matters
var x = await SomeMethodThatReturnsUnknownType();
```

### Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Classes, records, structs | PascalCase | `OrderService`, `CreateOrderRequest` |
| Interfaces | `I` prefix + PascalCase | `IOrderRepository` |
| Methods | PascalCase | `GetOrderAsync`, `CreateAsync` |
| Properties | PascalCase | `OrderId`, `CreatedAt` |
| Constants | PascalCase | `MaxRetryCount` |
| Private fields | camelCase (no `_` prefix with primary constructors) | `orders`, `clock` |
| Local variables | camelCase | `order`, `totalAmount` |
| Parameters | camelCase | `orderId`, `cancellationToken` / `ct` |
| Namespaces | PascalCase, dot-separated | `MyProject.Features.Orders` |

**Note on `_` prefix:** When using primary constructors, the parameter IS the field reference — there is no backing field to prefix. Use `_` prefix only when you declare explicit private fields (rare with primary constructors).

**Async suffix:**

```csharp
// CORRECT — all methods returning Task or ValueTask get Async suffix
public Task<Order?> GetOrderAsync(Guid id, CancellationToken ct) { }
public ValueTask<bool> ExistsAsync(Guid id, CancellationToken ct) { }

// WRONG
public Task<Order?> GetOrder(Guid id, CancellationToken ct) { }
```

---

## Async Patterns

**Always propagate `CancellationToken`:**

```csharp
// CORRECT
public async Task<Order?> GetOrderAsync(Guid id, CancellationToken ct) =>
    await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

// WRONG — token silently dropped
public async Task<Order?> GetOrderAsync(Guid id, CancellationToken ct) =>
    await db.Orders.FirstOrDefaultAsync(o => o.Id == id);
```

**Never block on async code:**

```csharp
// WRONG — causes deadlocks and thread pool starvation
var result = GetOrderAsync(id, ct).Result;
GetOrderAsync(id, ct).Wait();
var result = GetOrderAsync(id, ct).GetAwaiter().GetResult();

// CORRECT — async all the way
var result = await GetOrderAsync(id, ct);
```

**`TimeProvider` over `DateTime.Now`:**

```csharp
// CORRECT — injectable and testable
public sealed class AuditService(TimeProvider clock)
{
    public DateTimeOffset Now => clock.GetUtcNow();
}

// WRONG — static dependency, untestable
var now = DateTime.UtcNow;
var now = DateTime.Now;
```

---

## Comments Policy

The kit enforces minimal comments. Default to writing **no comments**.

Write a comment only when the **WHY** is non-obvious to a future reader:

```csharp
// CORRECT — explains a non-obvious constraint
// MSBuildLocator.RegisterDefaults() must be called before any Roslyn type is referenced.
// The Roslyn assemblies bind to MSBuild at the moment the first type is JIT-compiled.
MSBuildLocator.RegisterDefaults();

// CORRECT — explains a workaround
// FileSystemWatcher exhausts inotify limits on Linux for large solutions.
// We poll instead, using per-call cooldowns.

// WRONG — narrates what the code already says
// Add order to database
db.Orders.Add(order);

// WRONG — documents "what was changed" (belongs in git history, not code)
// Fixed: null reference when order is not found (issue #42)
```

**Never write multi-line comment blocks or XML doc summaries** unless the method is a public API surface that external consumers will use.

---

## Project File Conventions (`.csproj`)

From `.editorconfig` — project files use 2-space indentation:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
```

**Never hardcode package versions in `.csproj`** unless using `Directory.Packages.props` (in which case only `Directory.Packages.props` has versions):

```xml
<!-- CORRECT — let Directory.Packages.props manage the version -->
<PackageReference Include="FluentValidation" />

<!-- CORRECT — explicit version only when no central management exists -->
<!-- First run: dotnet add package FluentValidation (no --version flag) -->
<PackageReference Include="FluentValidation" Version="11.10.0" />

<!-- WRONG — hardcoded old version from memory -->
<PackageReference Include="FluentValidation" Version="11.0.0" />
```

---

## YAML / JSON / Markdown Conventions

From `.editorconfig`:

- YAML files: 2-space indentation
- JSON files: 2-space indentation
- Markdown files: no trailing whitespace trimming (intentional — trailing spaces are meaningful in Markdown)
- All files: UTF-8 encoding, LF line endings

**Skill frontmatter must use the exact format:**

```yaml
---
name: skill-name
description: >
  First sentence describing what the skill covers.
  Second sentence with trigger keywords.
---
```

The `>` (folded block scalar) is required — not `|` (literal block scalar). The `validate.yml` CI workflow checks this format.

---

## Git Commit Conventions

All commits in this repository use conventional commit format:

```
feat: add get_method_complexity tool
fix: prevent LRU eviction during warm-up phase
refactor: extract MakeRelativePath to SymbolResolver
test: add edge cases for circular dependency detection
docs: add beginner onboarding guide
chore: update ModelContextProtocol to 0.3.0
```

**Format:** `<type>: <short imperative description>`

**Body (optional):** Explains WHY, not WHAT. The diff shows what changed.

```
feat: add lazy MCP roots discovery

Resolves the case where the global tool is used without a --solution arg
and the working directory does not contain a solution. On first tool call,
we now ask the MCP host for workspace roots and attempt discovery there.

This allows cwm-roslyn-navigator to work without any explicit configuration
in most cases.
```

**Types:**

| Type | When to use |
|------|------------|
| `feat` | New capability added |
| `fix` | Bug fixed |
| `refactor` | Code restructured, behavior unchanged |
| `test` | Tests added or changed |
| `docs` | Documentation only |
| `chore` | Maintenance (deps, CI, tooling) |
| `perf` | Performance improvement |

---

## Enforced by Tooling

These conventions are automatically enforced and do not require manual checking:

| Convention | Enforced by |
|-----------|------------|
| Indentation, spacing | `.editorconfig` + format hook |
| Naming conventions | `.editorconfig` [*.cs] rules |
| Auto-format after edit | `hooks/post-edit-format.sh` |
| Pre-commit format | `hooks/pre-commit-format.sh` |
| Anti-pattern check | `hooks/pre-commit-antipattern.sh` |
| CI format verification | `validate.yml` |

If the format hook changes your file after an edit, accept the changes. Do not revert them.
