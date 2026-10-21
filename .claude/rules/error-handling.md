---
alwaysApply: true
description: >
  Enforces Result pattern for expected failures, ProblemDetails for HTTP errors,
  and boundary-only exception handling in .NET projects.
---

# Error Handling Rules

## Result Pattern Over Exceptions

- **DO** use a Result/Result<T> pattern for expected failure paths (not found, validation, conflict).
  Rationale: Exceptions are expensive and hide control flow. Results make failure explicit in the type system.

- **DON'T** use try-catch for flow control. If you can predict the failure, return a Result.
  Rationale: try-catch obscures the happy path and makes code harder to reason about.

- **DO** define typed error codes in your Result type: `NotFound`, `Validation`, `Conflict`, `Unauthorized`.
  Rationale: Typed errors enable consistent mapping to HTTP status codes and structured logging.

## ProblemDetails for HTTP Responses

- **DO** return ProblemDetails (RFC 9457) for all HTTP error responses.
  Rationale: Industry standard format that clients can parse consistently across endpoints.

- **DON'T** return bare strings or ad-hoc JSON for errors.
  Rationale: Inconsistent error shapes break client error handling and make debugging harder.

## Exception Handling Boundaries

- **DO** verify `app.UseExceptionHandler()` + `IExceptionHandler` (or inline handler) exists in Program.cs for EVERY web project. If missing, scaffold it immediately.
  Rationale: Without a global handler, unhandled exceptions leak stack traces in production.

- **DON'T** catch bare `Exception` unless at the application boundary (middleware/top-level handler).
  Rationale: Broad catches swallow bugs silently. Only the outermost layer should catch everything.

- **DON'T** catch and rethrow without adding context. Either handle it or let it propagate.
  Rationale: Catch-and-rethrow without value destroys stack traces and adds noise.

## Boundary Validation

- **DO** validate at system boundaries: API input, external service responses, file/config data.
  Rationale: Bad data should be rejected at the edge before it corrupts internal state.

- **DON'T** defensively validate inside internal/private methods.
  Rationale: Internal code should trust validated data. Double-validation adds noise without safety.
