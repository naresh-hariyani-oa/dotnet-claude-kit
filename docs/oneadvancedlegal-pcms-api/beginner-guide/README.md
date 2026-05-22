# PCMS Integration API — Beginner Onboarding Guide

Welcome to the PCMS Integration API. This guide is for developers who have just cloned the repository and need to become productive without introducing risky changes.

## Recommended Reading Order

| Step | Document | Time | What you'll learn |
|---|---|---|---|
| 1 | [00 — Project Overview](00-project-overview.md) | 5 min | What the system is, who uses it, the tech stack |
| 2 | [01 — Repository Structure](01-repository-structure.md) | 10 min | Where everything lives, the 80-project layout, domain list |
| 3 | [02 — Architecture Overview](02-architecture-overview.md) | 15 min | Layered architecture, multi-tenancy, middleware pipeline |
| 4 | [04 — Request Flow](04-request-flow.md) | 15 min | What happens on every HTTP request, end-to-end |
| 5 | [03 — Design Patterns](03-design-patterns.md) | 20 min | Repository, Service Layer, AutoMapper, Exception hierarchy |
| 6 | [05 — Build and Run](05-build-and-run.md) | 20 min | Local setup, build commands, test commands, common startup issues |
| 7 | [07 — Coding Standards](07-coding-standards.md) | 15 min | Naming, SQL safety, async conventions, DTO/entity separation |
| 8 | [06 — Testing Guide](06-testing-guide.md) | 15 min | xUnit + Moq patterns, fixture setup, how to add tests |
| 9 | [09 — Common Pitfalls](09-common-pitfalls.md) | 10 min | What to avoid — read before your first PR |
| 10 | [08 — Debugging Guide](08-debugging-guide.md) | 10 min | Logs, correlation IDs, common error scenarios |
| 11 | [10 — Safe Development Workflow](10-safe-development-workflow.md) | 10 min | Risk map, safe extension points, PR checklist |

---

## Quick Reference

| I want to... | Read this |
|---|---|
| Set up the project locally | [05 — Build and Run](05-build-and-run.md) |
| Understand where a file should go | [01 — Repository Structure](01-repository-structure.md) |
| Add a new API endpoint | [03 — Design Patterns](03-design-patterns.md) + [10 — Safe Development Workflow](10-safe-development-workflow.md) |
| Debug a 401/403/404/500 error | [08 — Debugging Guide](08-debugging-guide.md) |
| Add a new test | [06 — Testing Guide](06-testing-guide.md) |
| Understand the database access approach | [02 — Architecture Overview §Dual Database](02-architecture-overview.md) |
| Know which areas are dangerous to change | [10 — Safe Development Workflow §Risk Classification](10-safe-development-workflow.md) |
| Understand multi-tenancy | [02 — Architecture Overview §Multi-Tenancy](02-architecture-overview.md) |

---

## Key Facts to Know on Day One

1. **There are ~80 C# projects** — one per domain per layer. This is intentional; it enforces boundaries at compile time.

2. **The only runnable project is `api/IntegrationApi/`**. Everything else is a class library.

3. **Connection strings go in User Secrets, not `appsettings.Development.json`** — that file is committed to git.

4. **Multi-tenancy is per-request.** The JWT's `organisation_reference` claim determines which SQL Server database the request connects to.

5. **Never throw `null` to signal not-found.** Always throw `ResourceNotFoundException`.

6. **Always use `CreateParameter()` for SQL.** String-interpolated SQL is a SQL injection vulnerability and will fail code review.

7. **The Common layer has the highest blast radius.** Test the full suite before merging any Common change.
