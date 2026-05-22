# 02 — Architecture Overview

This document describes the architecture **of the kit itself** — how its components are organized and why. It also describes the architectures the kit **recommends for user projects** and the decision records that justify those choices.

---

## Part 1: Architecture of the Kit Itself

### Overall Structure: Plugin + MCP Server

The kit has two distinct runtime components:

```
┌─────────────────────────────────────────────────────────────┐
│  Claude Code (the AI assistant)                              │
│                                                              │
│  ┌──────────────┐  ┌───────────────┐  ┌──────────────────┐  │
│  │   Rules      │  │   Skills      │  │   Agents         │  │
│  │ (always on)  │  │ (on demand)   │  │ (specialist)     │  │
│  └──────────────┘  └───────────────┘  └──────────────────┘  │
│           │                │                  │              │
│           └────────────────┴──────────────────┘              │
│                            │                                 │
│                   ┌────────▼────────┐                        │
│                   │   Commands      │                        │
│                   │  (orchestrate)  │                        │
│                   └────────┬────────┘                        │
└────────────────────────────┼────────────────────────────────┘
                             │ MCP protocol (stdio)
                             │
              ┌──────────────▼──────────────┐
              │  CWM.RoslynNavigator         │
              │  (dotnet global tool)        │
              │                             │
              │  WorkspaceManager           │
              │  ├── 15 MCP Tools           │
              │  └── 9 Anti-pattern         │
              │       Detectors             │
              └──────────────────────────────┘
                             │
                        Roslyn API
                             │
              ┌──────────────▼──────────────┐
              │  Your .NET Solution          │
              │  (MSBuildWorkspace)          │
              └──────────────────────────────┘
```

**Component 1 — The Claude Code Plugin (markdown files)**

The plugin is purely text files. No build step, no runtime compilation. When Claude Code loads the plugin, it reads the rule files into context. Skills and agents are loaded lazily by name when a command or user query triggers them.

- Rules → `.claude/rules/` — injected automatically into every prompt
- Skills → `skills/*/SKILL.md` — loaded by agents when needed
- Agents → `agents/*.md` — activated by routing logic in `AGENTS.md`
- Commands → `commands/*.md` — activated by `/command-name` slash syntax
- Knowledge → `knowledge/*.md` — referenced by agents/skills, not loaded wholesale

**Component 2 — The Roslyn MCP Server (C# application)**

`CWM.RoslynNavigator` is a real .NET 10 application that runs as a background process. Claude Code communicates with it via the Model Context Protocol (MCP) over stdio. It opens your solution using Roslyn's MSBuildWorkspace and provides semantic code analysis through 15 read-only tools.

---

### MCP Server Architecture (Detailed)

The server follows a simple layered design:

```
Program.cs
    │
    ├── MSBuildLocator.RegisterDefaults()   ← must be first
    │
    └── IHost (Microsoft.Extensions.Hosting)
            │
            ├── WorkspaceManager (singleton)
            │       ├── MSBuildWorkspace
            │       ├── LRU compilation cache (max 30 projects)
            │       └── Change detection with cooldowns
            │
            ├── WorkspaceInitializer (IHostedService)
            │       └── Loads solution in background on startup
            │
            └── MCP Server (stdio transport)
                    └── Tool discovery via reflection (WithToolsFromAssembly)
```

**Key design decisions in the MCP server:**

1. **Non-blocking startup** — The server starts accepting connections immediately. Solution loading happens in the background. Tools return a `StatusResponse` ("loading") rather than failing if the workspace isn't ready yet.

2. **Lazy MCP roots discovery** — If no solution is found at startup (e.g., global tool with no `--solution` arg), the first tool call triggers one-shot discovery via `IMcpServer.RequestRootsAsync()`. This asks the MCP host (Claude Code) for workspace roots.

3. **LRU compilation cache** — For large solutions (50+ projects), not all compilations are loaded upfront. The cache holds at most 30 compilations. Least-recently-used entries are evicted to bound memory usage.

4. **No FileSystemWatcher** — Intentional. `FileSystemWatcher` exhausts Linux inotify limits in large solutions. Instead, change detection is cooldown-based:
   - `.csproj` file changes: checked with 5-second cooldown
   - New `.cs` files: checked with 60-second cooldown (directory walk is expensive)

5. **Token optimization** — Response records use short property names. File paths are trimmed to `"parent/file.cs"`. All results are capped (100 references, 50 dead code symbols, 100 anti-pattern violations). Snippets are truncated at 80 characters.

---

### Skill Architecture

Each skill is a single markdown file following the Agent Skills open standard:

```
skills/<skill-name>/SKILL.md
    │
    ├── Frontmatter (name, description with trigger keywords)
    ├── Core Principles (3-5 numbered, opinionated defaults with rationale)
    ├── Patterns (code examples with explanation)
    ├── Anti-patterns (BAD/GOOD comparisons)
    └── Decision Guide (Scenario → Recommendation table)
```

Skills are **lazy by design**. Loading all 47 skills upfront would consume too many context tokens. Agents load only what they need for the task at hand.

---

## Part 2: Architectures the Kit Recommends for User Projects

The kit supports four architectures and selects between them via questionnaire (never by assumption):

### Vertical Slice Architecture (VSA) — Default for New Projects

**What:** Feature-first organization. All code for one feature lives in one folder, regardless of technical layer.

```
Features/
├── Orders/
│   ├── CreateOrder.cs          ← Command/handler + validator + DTO in one file
│   ├── OrderEndpoints.cs       ← IEndpointGroup implementation
│   └── GetOrders.cs
└── Products/
    ├── CreateProduct.cs
    └── ProductEndpoints.cs
```

**Why chosen (ADR-001/ADR-005):** Minimizes cross-feature coupling. When a feature changes, you touch one folder. No "which layer does this belong to?" ambiguity. Scales to large teams because features are independently owned.

**When to use:** Simple CRUD APIs, startups, teams new to .NET architecture, projects where shipping speed matters.

### Clean Architecture — For Long-Lived, Medium-Complexity Systems

**What:** Four projects with strict inward dependency direction.

```
Domain/           ← No dependencies (entities, value objects, interfaces)
Application/      ← Depends on Domain (use cases, DTOs, service interfaces)
Infrastructure/   ← Depends on Application (EF Core, HTTP clients, file I/O)
Api/              ← Depends on Application (controllers, Minimal API endpoints)
```

**Why:** Protects domain logic from infrastructure concerns. Enables swapping infrastructure (e.g., PostgreSQL → SQL Server) without touching business logic. Makes testing domain logic trivial.

**When to use:** 2+ year project lifespan, complex business rules, multiple frontend clients, teams with separate domain vs. infrastructure ownership.

### DDD + Clean Architecture — For Domain-Rich, High-Complexity Systems

**What:** Clean Architecture layers with DDD tactical patterns inside the Domain project.

```
Domain/
├── Orders/
│   ├── Order.cs              ← Aggregate root
│   ├── OrderItem.cs          ← Entity
│   ├── OrderStatus.cs        ← Value object
│   └── OrderDomainEvents.cs  ← Domain events
├── Shared/
│   └── Money.cs              ← Shared value object
└── SeedWork/
    ├── AggregateRoot.cs
    └── ValueObject.cs
```

**Why:** Explicit domain model that non-technical stakeholders can read. Aggregates enforce business invariants. Domain events decouple modules.

**When to use:** Financial systems, healthcare, e-commerce with complex rules, experienced teams comfortable with DDD concepts.

### Modular Monolith — For Systems With Multiple Bounded Contexts

**What:** Single deployable unit organized as isolated modules. Modules communicate via in-process messages, not direct calls.

```
src/
├── Modules/
│   ├── Orders/
│   │   ├── OrdersModule.cs         ← Module registration
│   │   ├── Domain/
│   │   ├── Application/
│   │   └── Infrastructure/
│   └── Inventory/
│       ├── InventoryModule.cs
│       └── ...
└── Host/                           ← API host, wires up modules
    └── Program.cs
```

**Why:** Avoids microservice operational complexity (no network calls between modules in development). Module boundaries enforce domain isolation. Can be split into microservices later.

**When to use:** Multiple bounded contexts, team per bounded context, want microservice boundaries without microservice ops overhead.

---

## Part 3: Key Architecture Decisions (ADRs)

The kit has five Architecture Decision Records that explain its defaults. Every decision follows the same template: context → decision → consequences.

| ADR | Decision | Summary |
|-----|----------|---------|
| ADR-001 | VSA as default | Feature folders beat layer folders for most new projects |
| ADR-002 | Result over exceptions | Exceptions for unexpected failures; Result for expected failures |
| ADR-003 | EF Core as default ORM | No repository pattern wrapper; DbContext directly is enough |
| ADR-004 | HybridCache as default | L1+L2 with stampede protection beats manual IMemoryCache |
| ADR-005 | Multi-architecture support | No single default — questionnaire picks the right one |

ADR-005 supersedes ADR-001: the kit now asks about project context before recommending architecture, rather than always defaulting to VSA.

---

## Part 4: Cross-Cutting Concerns in User Projects

### Error Handling Architecture

```
Incoming Request
       │
       ▼
[ValidationFilter]     ← FluentValidation, returns 400 ValidationProblem
       │
       ▼
[Endpoint Handler]
       │
       ▼
[Service / Use Case]   ← Returns Result<T> (not throws)
       │
       ├── Success → map to TypedResults.Ok/Created/etc.
       └── Failure → map to TypedResults.Problem/UnprocessableEntity/etc.
       │
       ▼
[GlobalExceptionHandler]  ← Catches anything unexpected, returns 500 ProblemDetails
```

### Data Access Architecture

```
Endpoint
    │
    ▼
[Service or Minimal Handler]
    │
    ▼
[DbContext] ← Direct injection, no repository wrapper
    │
    ▼
[Database]
```

No `IRepository<T>` abstraction over EF Core. The kit explicitly prohibits this (see `.claude/rules/architecture.md`) because `DbContext` already implements Unit of Work + Repository, and wrapping it prevents access to EF Core features.

### Endpoint Registration Architecture

```
Program.cs
    │
    └── app.MapEndpoints()       ← Single call, scans assembly for IEndpointGroup
                │
                ├── OrderEndpoints.Map(app)
                ├── ProductEndpoints.Map(app)
                └── UserEndpoints.Map(app)
```

No endpoint registration in `Program.cs` beyond the single `app.MapEndpoints()` call. New endpoint groups are discovered automatically by reflection. This is a deliberate pattern to prevent `Program.cs` from growing unbounded.
