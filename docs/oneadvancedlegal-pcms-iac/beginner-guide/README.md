# PCMS Legal Cloud — Beginner Onboarding Guide

Welcome to the PCMS Legal Cloud IaC repository. This guide helps you become productive without introducing risky changes.

---

## Quick Navigation

| # | Document | What It Covers |
|---|---|---|
| 00 | [Project Overview](00-project-overview.md) | What the system does, who uses it, technology stack |
| 01 | [Repository Structure](01-repository-structure.md) | Folder layout, key files, what to edit and what to avoid |
| 02 | [Architecture Overview](02-architecture-overview.md) | Three-tier Terraform model, layering, networking, CI/CD |
| 03 | [Design Patterns](03-design-patterns.md) | Module composition, Provider pattern, Durable Functions, HMAC validation |
| 04 | [Application Flows](04-request-flow.md) | Customer onboarding, DB upgrade, secret rotation — step by step |
| 05 | [Build and Run](05-build-and-run.md) | Prerequisites, local setup, commands, common issues |
| 06 | [Testing Guide](06-testing-guide.md) | How to validate changes (no automated tests currently exist) |
| 07 | [Coding Standards](07-coding-standards.md) | Naming, async, SQL safety, logging, DI conventions |
| 08 | [Debugging Guide](08-debugging-guide.md) | Local debugging, App Insights queries, Terraform errors |
| 09 | [Common Pitfalls](09-common-pitfalls.md) | 15 mistakes new developers commonly make |
| 10 | [Safe Development Workflow](10-safe-development-workflow.md) | Risk classification, high-risk areas, PR checklist |
| 11 | [Security Patterns](11-security-patterns.md) | Auth, Key Vault, HMAC, SQL injection prevention |

---

## Recommended Reading Order

**Day 1:** [00](00-project-overview.md) → [01](01-repository-structure.md) → [02](02-architecture-overview.md)

**Day 2:** [04](04-request-flow.md) → [05](05-build-and-run.md) → [09](09-common-pitfalls.md)

**Before writing code:** [07](07-coding-standards.md) → [10](10-safe-development-workflow.md) → [11](11-security-patterns.md)

**When debugging:** [08](08-debugging-guide.md) + [03](03-design-patterns.md)

---

## Known Documentation Gaps

- No automated test projects exist in the C# solution — [06](06-testing-guide.md) covers manual testing only
- Harness pipeline internals are not fully documented here — refer to `.harness/*.yaml` files directly
- Azure B2C Trust Framework XML policies are not explained in depth — the Microsoft B2C documentation is the reference
- CosmosDB schema and data model is documented by reading `TenantDBContext.cs` and the `O365TenantApplications` model directly
