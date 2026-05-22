---
alwaysApply: true
description: >
  Enforces C# coding conventions, naming standards, and file organization.
  Compatible with .NET 8+ projects using traditional constructor injection.
---

# C# Coding Style

## File Organization

- **File-scoped namespaces always.** Block-scoped namespaces waste indentation for zero benefit.
- **One type per file.** File name must match the type name exactly (`OrderService.cs` contains `OrderService`).
- **Order members:** constants, fields, constructors, properties, public methods, private methods. Consistent ordering reduces cognitive load when scanning a file.

## Type Declarations

- **`sealed` on classes not designed for inheritance.** The JIT can devirtualize calls on sealed types, and it communicates intent clearly.
- **`internal` by default, `public` only when needed.** Minimize the public API surface. If nothing outside the project references it, it should be `internal`.
- **Match the project's existing constructor style.** For projects using traditional constructor injection with `_camelCase` private fields, continue that pattern. For new greenfield code, primary constructors are preferred.

## Expressions and Patterns

- **Pattern matching over if-else chains.** Switch expressions and `is` patterns are more readable and exhaustiveness-checked.

```csharp
// DO
var label = status switch
{
    OrderStatus.Pending => "Awaiting payment",
    OrderStatus.Shipped => "On the way",
    _ => "Unknown"
};

// DON'T
string label;
if (status == OrderStatus.Pending) label = "Awaiting payment";
else if (status == OrderStatus.Shipped) label = "On the way";
else label = "Unknown";
```

## Naming and Modifiers

- **`var` for obvious types, explicit types when clarity matters.** Use `var` when the right-hand side makes the type self-evident (`var order = new Order()`); spell it out when it does not (`HttpResponseMessage response = await ...`).
- **Async suffix on all async methods.** `GetOrderAsync`, not `GetOrder`, for methods returning `Task` or `ValueTask`. Prevents accidental sync calls.
- **PascalCase** for public members, types, namespaces, and methods. **camelCase** for local variables and parameters.
- **Private fields: match the project convention.** Many production projects use `_camelCase` prefix — preserve that if it is the established standard. Do not rename existing fields.
