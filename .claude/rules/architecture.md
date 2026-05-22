---
alwaysApply: true
description: >
  Enforces dependency direction and module boundary rules for .NET solutions.
  Works with both layered (Controller/Service/Repository) and feature-organized codebases.
---

# Architecture Rules

## Ask First, Recommend Second

- **Never assume an architecture.** Every project has different constraints. Before making structural recommendations, understand the existing project layout using `get_project_graph` and `find_symbol`.

## Data Access

- **In existing projects with a repository layer, work within it.** If the codebase has `IRepository<T>` implementations and domain-specific repositories, extend that pattern — do not suggest removing it.
- **For new greenfield projects:** `DbContext` is already a Unit of Work + Repository. Wrapping it adds indirection with no value; inject `DbContext` directly.

## Dependency Direction

- **Dependency direction is inward.** Domain depends on nothing. Application/Business depends on Domain. Infrastructure/Data depends on Application/Business. Presentation/API depends on Application/Business. Never reverse these arrows.
- **Module boundaries enforced through project references.** If two modules should not couple, they must not have a project reference. Use shared contracts projects for cross-module communication.

## Shared Kernel

- **Shared kernel contains only contracts, never business logic.** Shared projects hold interfaces, DTOs, and event definitions. Domain logic belongs in the owning module. Leaking logic into shared projects creates hidden coupling.

```csharp
// DO — shared kernel has contracts
public interface IOrderPlaced
{
    Guid OrderId { get; }
    DateTimeOffset OccurredAt { get; }
}

// DON'T — shared kernel has domain logic
public static class PricingCalculator { /* business rules */ }
```
