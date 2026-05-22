# 00 — Project Overview

## What is PCMS?

OneAdvancedLegal PCMS (Practice & Case Management System) is a **legal practice management web application** used by law firms to manage matters, clients, documents, billing, time recording, diary appointments, and compliance workflows.

It is a **migration of a legacy Windows desktop application (IRIS.Law)** to a browser-based web application. Rather than rewrite everything from scratch, the team ported the WinForms UI layer to [Wisej-3](https://wisej.com/) — a web framework that provides a WinForms-like programming model running in the browser.

---

## Technology Stack at a Glance

| Layer | Technology |
|---|---|
| **Web UI Framework** | Wisej-3 (WinForms API, runs in browser) |
| **Backend Framework** | .NET Framework 4.8 + .NET 7.0 (dual-targeting) |
| **ORM** | Entity Framework 6 + Dapper |
| **Database** | SQL Server (via `Microsoft.Data.SqlClient`) |
| **Dependency Injection** | Autofac (with DynamicProxy2 for AOP) |
| **UI Controls** | DevExpress (dashboards, reports, rich editor) |
| **Testing** | xUnit + Moq + Coverlet |
| **Logging** | NLog |
| **Observability** | OpenTelemetry (Grafana custom build) + New Relic |
| **CI/CD** | Harness + GitHub Actions |
| **Cloud** | Microsoft Azure (AD, Blob, Graph API) |
| **Package Management** | NuGet with Central Package Management (CPM) |

---

## Who Works on This?

- **Backend developers** — working on C# business logic, data access, and API services
- **Full-stack / frontend developers** — working on Wisej forms and controls (C# with a WinForms-like API)
- **DevOps** — managing Harness pipelines and Azure infrastructure

If you are a developer new to the project, this guide is aimed at you.

---

## Dual Framework Targeting — The Most Important Thing to Understand First

Every core project in this solution targets **two frameworks simultaneously**:

```xml
<TargetFrameworks>net48;net7.0</TargetFrameworks>
```

- **net48 (.NET Framework 4.8)** — runs the actual production application via IIS/OWIN. This is the primary runtime.
- **net7.0 (.NET 7.0)** — enables modern tooling (e.g., `dotnet build`, `dotnet test`) and ASP.NET Core middleware support.

The application **cannot run on .NET 7.0 alone**. The .NET Framework 4.8 build is required for Wisej and the OWIN pipeline.

Platform-specific code uses separate files named with suffixes:
- `Startup.net48.cs` — .NET Framework 4.8 only
- `Startup.netcore.cs` — .NET 7.0 only

---

## The Migration Context

The codebase exists in two distinct layers:

1. **Legacy IRIS.Law layer** (`app/Source/IRIS.Law.*`) — The original WinForms application being migrated to web. This code was written for Windows desktop and is being adapted for Wisej. Expect older patterns, large files, and `// TODO:` comments where WinForms APIs are not supported on the web.

2. **Modern Legal.Cloud layer** (`app/Legal.Cloud/`) — Newer web-specific code, cloud integrations, and the startup/shell of the web application.

When modifying IRIS.Law code, be conservative. These files are often very large, deeply interconnected, and have been ported carefully from WinForms. Small changes can have wide effects.

---

## Business Domains Covered

The application covers multiple legal practice areas:

| Domain | What it covers |
|---|---|
| **PMS (Practice Management)** | Matters, clients, time recording, billing, fee earners |
| **Accounts** | Purchase ledger, accounts, financial transactions |
| **Diary** | Calendar, appointments, Exchange/Outlook sync |
| **Document Management (DMS)** | File storage, cloud document management |
| **Document Production** | Mail-merge, template-based document generation |
| **Conveyancing** | Property transaction workflows |
| **Probate** | Will and estate administration |
| **Personal Injury** | PI claim workflows |
| **Family Law** | Family law case management |
| **Debt** | Debt matter management |
| **Workflow** | User-defined workflow engine, dynamic screen builder |
| **NWR Integration** | National Will Register search and registration |
| **Exchange/Office 365** | Calendar sync, email, Teams, SharePoint |

---

## Quick Orientation — Where to Start

| Goal | Where to look |
|---|---|
| Understand the project | This file + [02-architecture-overview.md](02-architecture-overview.md) |
| Build and run locally | [05-build-and-run.md](05-build-and-run.md) |
| Understand folder layout | [01-repository-structure.md](01-repository-structure.md) |
| Understand design patterns | [03-design-patterns.md](03-design-patterns.md) |
| Understand application flows | [04-request-flow.md](04-request-flow.md) |
| Write tests | [06-testing-guide.md](06-testing-guide.md) |
| Follow coding standards | [07-coding-standards.md](07-coding-standards.md) |
| Debug problems | [08-debugging-guide.md](08-debugging-guide.md) |
| Avoid common mistakes | [09-common-pitfalls.md](09-common-pitfalls.md) |
| Make changes safely | [10-safe-development-workflow.md](10-safe-development-workflow.md) |

**Also read:** [`CLAUDE.md`](../../CLAUDE.md) at the root of the repository — it contains additional coding standards and architectural notes that govern all development in this project.
