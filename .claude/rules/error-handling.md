---
alwaysApply: true
description: >
  Enforces consistent error handling patterns for .NET projects. Supports both
  custom exception hierarchies in existing codebases and the Result pattern for new code.
---

# Error Handling Rules

## Existing Projects: Custom Exception Hierarchy

- **In projects with an established custom exception hierarchy** (e.g., `ResourceNotFoundException → 404`, `ValidationException → 400`, `ForbiddenException → 403`), work within that hierarchy. Throw the appropriate domain exception and let the global middleware map it to a ProblemDetails response.
- **Do NOT suggest replacing a working exception hierarchy** with the Result pattern in existing code. Exception hierarchies are deeply embedded in test expectations and middleware configuration.

```csharp
// DO — in projects with custom exception hierarchy
if (order is null)
    throw new ResourceNotFoundException($"Order {id} not found");

// DON'T — introducing Result pattern into a codebase built on exceptions
return Result.Failure<Order>(ErrorCode.NotFound);  // breaks existing tests and consumers
```

## New Code: Result Pattern

- **For new services or new projects without an established pattern**, prefer a `Result<T>` return type for expected failure paths (not found, validation, conflict).
- Exceptions are expensive and hide control flow. Results make failure explicit in the type system.
- Define typed error codes: `NotFound`, `Validation`, `Conflict`, `Unauthorized`.

## ProblemDetails for HTTP Responses

- **Return ProblemDetails (RFC 9457) for all HTTP error responses.** Industry standard format that clients can parse consistently.
- **Never return bare strings or ad-hoc JSON for errors.** Inconsistent error shapes break client error handling.
- Every web project must have a global exception handler (`IExceptionHandler` or `UseExceptionHandler` middleware) that ensures all unhandled exceptions produce ProblemDetails, never raw stack traces.

## Exception Handling Boundaries

- **DON'T** catch bare `Exception` unless at the application boundary (middleware/top-level handler).
- **DON'T** catch and rethrow without adding context. Either handle it or let it propagate.
- **DO** validate at system boundaries: API input, external service responses, file/config data.

## Quick Reference

| Scenario | Existing project with exception hierarchy | New/greenfield code |
|---|---|---|
| Entity not found | `throw new ResourceNotFoundException(...)` | `return Result.Failure(ErrorCode.NotFound)` |
| Input invalid | `throw new ValidationException(...)` | `return Result.Failure(ErrorCode.Validation)` |
| Unhandled crash | Global `IExceptionHandler` middleware | Global `IExceptionHandler` middleware |
| External API failure | Catch specific exception, rethrow as domain exception | Catch specific exception, return Result |
