---
name: modern-csharp
description: >
  Modern C# language features. Covers primary constructors, collection expressions,
  the field keyword, records, pattern matching, spans, and raw string literals.
  Note: C# 14 features target .NET 10. For .NET 8 projects, C# 12 features apply
  (collection expressions, required members, primary constructors for non-DI types).
  Load this skill when writing any new C# code, reviewing existing code for
  modernization, using "modern C#", "C# 14", "C# 12", "primary constructor",
  "collection expression", "records", "pattern matching", "span", "field keyword".
  Always loaded as the baseline for all agents.
---

# Modern C# (C# 12–14)

## Core Principles

1. **Apply features that match the project's target framework.** C# 14 features target .NET 10. For .NET 8 projects, use C# 12 features: collection expressions, required members, primary constructors for value types and non-DI classes.
2. **In existing codebases, do not introduce new language features that change the established constructor/field style.** Respect the project's patterns.
3. **Readability over cleverness** — Pattern matching and expression-bodied members improve readability when used appropriately; deeply nested patterns do not.
4. **Immutability by default** — Use `record`, `readonly`, `init`, and `required` to make illegal states unrepresentable where the project style allows.

## Patterns

### Well-Known Features Quick Reference

| Feature | Usage | Example |
|---------|-------|---------|
| Primary constructors | DI injection, eliminate field assignments | `public class OrderService(IOrderRepo repo, TimeProvider clock) { }` |
| Collection expressions | `[]` for all collection types + spread | `List<string> names = ["Alice", "Bob"];` / `int[] all = [..a, ..b, 99];` |
| Records | DTOs, value objects, immutable data | `public record CreateOrderRequest(string CustomerId, List<OrderItem> Items);` |
| `readonly record struct` | Small stack-allocated value types | `public readonly record struct Money(decimal Amount, string Currency);` |
| Pattern matching | Switch expressions, list/property patterns | `order switch { { Total: > 1000 } => "Premium", _ => "Standard" };` |
| List patterns | Deconstruct arrays/lists | `items switch { [] => "Empty", [var x] => $"One: {x}", [var f, .., var l] => $"{f}..{l}" };` |
| `Span<T>` | Zero-allocation slicing | `ReadOnlySpan<char> trimmed = input.Trim(); int.TryParse(trimmed[4..], out id);` |
| Raw string literals | Multi-line SQL, JSON, XML | `var sql = """ SELECT ... """;` / interpolated: `$$""" {"id": "{{id}}"} """;` |
| `required` members | Enforce initialization | `public required string ConnectionString { get; init; }` |
| `is` pattern + extraction | Null/type/property check | `if (result is { IsSuccess: true, Value: var order }) { ... }` |

### The `field` Keyword (C# 14)

Access the auto-generated backing field in property accessors without declaring it manually.

```csharp
// GOOD — field keyword for validation in auto-property
public class Product
{
    public string Name
    {
        get => field;
        set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
    }

    public decimal Price
    {
        get => field;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }
}
```

#### Lazy Initialization with `field`

```csharp
public class ProductCatalog
{
    // Lazy-load on first access — no manual Lazy<T> or backing field
    public IReadOnlyList<Product> Products
    {
        get => field ??= LoadProducts();
    }

    private static List<Product> LoadProducts() => /* expensive load */;
}
```

#### Change Notification with `field`

```csharp
// INotifyPropertyChanged without manual backing fields
public class OrderViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string CustomerName
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomerName)));
        }
    } = "";

    public decimal Total
    {
        get => field;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Total)));
        }
    }
}
```

### Extension Members (C# 14)

Extension members replace static extension method classes with a cleaner syntax.

```csharp
// GOOD — extension members (C# 14)
public extension OrderExtensions for Order
{
    public decimal TotalWithTax => Total * 1.2m;

    public bool IsHighValue => Total > 1000m;

    public string ToSummary() => $"Order #{Id}: {Total:C} ({Items.Count} items)";
}
```

## Anti-patterns

### Don't Use Obsolete Patterns When Modern Alternatives Exist

```csharp
// BAD — manual backing field when field keyword works
private string _name;
public string Name
{
    get => _name;
    set => _name = value ?? throw new ArgumentNullException();
}

// BAD — old-style collection initialization
var list = new List<int>() { 1, 2, 3 };

// BAD — Tuple instead of record for domain types
(string Name, decimal Price) product = ("Widget", 9.99m);
// GOOD — record
public record Product(string Name, decimal Price);
```

### Don't Over-pattern-match

```csharp
// BAD — deeply nested pattern that's hard to read
if (order is { Customer: { Address: { Country: { Code: "US" } } } })

// GOOD — extract to a clear method or use sequential checks
if (order.Customer.Address.Country.Code == "US")
```

### Don't Use `var` When the Type Is Not Obvious

```csharp
// BAD — what type is this?
var result = Process(order);

// GOOD — explicit type when not obvious
Result<Order> result = Process(order);
// Also GOOD — var is fine when type is apparent
var orders = new List<Order>();
```

## Decision Guide

| Scenario | Recommendation |
|----------|---------------|
| DTO / API contract | `record` (reference type) |
| Small value object (2-3 fields) | `readonly record struct` |
| Service with DI | Primary constructor |
| Collection creation | Collection expression `[]` |
| Property with validation | `field` keyword |
| Multi-line string (SQL, JSON) | Raw string literal `"""` |
| Slicing strings/arrays | `Span<T>` |
| Type checking + extraction | Pattern matching with `is` / `switch` |
| Enforced initialization | `required` modifier |
| Adding methods to external types | Extension members |
