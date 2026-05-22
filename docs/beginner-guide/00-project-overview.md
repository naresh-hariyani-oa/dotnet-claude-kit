# 00 — Project Overview

## What is dotnet-claude-kit?

dotnet-claude-kit is an **opinionated Claude Code companion for .NET developers**. It does not generate code by itself — it makes Claude Code dramatically better at understanding, generating, and reviewing .NET code by providing domain-specific skills, agents, templates, rules, and a Roslyn-powered MCP server.

Think of it as a developer toolkit that sits between you and Claude Code, giving Claude deep .NET expertise that it would otherwise lack or have to re-discover in every conversation.

---

## What the Kit Actually Contains

| Component | Count | Purpose |
|-----------|-------|---------|
| **Skills** | 47 | Domain-specific expertise Claude loads on demand (architecture, EF Core, testing, etc.) |
| **Agents** | 10 | Specialist personas for specific domains (e.g., `ef-core-specialist`, `security-auditor`) |
| **Commands** | 16 | Slash commands that orchestrate multi-step workflows (`/scaffold`, `/health-check`, etc.) |
| **Rules** | 10 | Always-loaded constraints that shape every Claude response (coding style, security, etc.) |
| **Templates** | 5 | Drop-in `CLAUDE.md` files for new projects (Web API, Blazor, Modular Monolith, etc.) |
| **Knowledge** | 12+ | Reference documents: ADRs, anti-patterns, package recommendations, .NET 10 updates |
| **MCP Server** | 1 | CWM.RoslynNavigator — 15 read-only Roslyn tools + 9 anti-pattern detectors |
| **Hooks** | 7 | Automation scripts that run on edit, save, commit, and test events |

---

## What Problem Does It Solve?

Without dotnet-claude-kit, Claude Code works well for general code tasks but:

- Doesn't know your team's architectural preferences (VSA vs Clean Architecture vs DDD)
- May suggest outdated patterns (`new HttpClient()`, `DateTime.Now`, repository pattern over EF Core)
- Reads full source files when a targeted symbol lookup would cost 10x fewer tokens
- Has no memory of .NET 10 / C# 14 idioms if training data skews older
- Lacks enforced conventions (commit format, error handling style, test naming)

dotnet-claude-kit solves these gaps through:

1. **Rules** — Always loaded, shape every response toward modern .NET patterns
2. **Skills** — On-demand deep expertise for specific domains
3. **MCP Server** — Token-efficient Roslyn analysis instead of reading full files
4. **Hooks** — Automated format/restore/guard actions so you don't have to remember them

---

## Who Built It and When?

- **Author:** Mukesh Murugan
- **Current version:** Plugin v0.7.0, MCP Server v0.7.1
- **License:** MIT
- **Target runtime:** .NET 10, C# 14

The changelog records significant versions from v0.5.0 (initial NuGet release) through v0.7.x (performance optimizations, multi-architecture support, MCP roots discovery).

---

## Repository vs. User Projects

This distinction is important and easy to confuse:

| | dotnet-claude-kit repo | Your .NET project |
|---|---|---|
| **Purpose** | Develop and maintain the kit itself | Build your application |
| **Instructions** | `CLAUDE.md` at repo root | Copy a template's `CLAUDE.md` into your project root |
| **Rules** | `.claude/rules/` — apply to kit dev | Same rules apply if you use the kit |
| **Skills** | Live in `skills/` | Claude loads them by name when needed |
| **Templates** | Live in `templates/` | You copy one template's `CLAUDE.md` to your project |

When you clone this repository, you are working **on the kit itself** — not on a .NET application. If you want to use the kit with a .NET project, the workflow is:

1. Install the MCP server globally (`dotnet tool install -g CWM.RoslynNavigator`)
2. Copy the appropriate template's `CLAUDE.md` to your project root
3. Open your project in Claude Code — the rules, skills, and MCP server activate automatically

---

## Philosophy at a Glance

The kit has four core design choices that affect everything:

1. **Guided over prescriptive** — The kit asks questions (via `architecture-advisor`) before recommending patterns. It does not impose a single architecture.

2. **Modern .NET only** — .NET 10, C# 14. No legacy patterns, no backwards compatibility with .NET Framework. `TimeProvider` over `DateTime.Now`. Records over classes for DTOs. Primary constructors for DI.

3. **Token-conscious** — Skills cap at 400 lines, rules at 100 lines, commands at 200 lines. The MCP server returns file:line snippets instead of full file contents. Every byte earns its place.

4. **Practical over theoretical** — Every recommendation has a code example and a rationale. No bare rules without justification.

---

## Suggested Reading Order for New Developers

If you're new to this repo, read in this order:

1. This file — understand what the kit is
2. [01-repository-structure.md](01-repository-structure.md) — navigate the file tree confidently
3. [02-architecture-overview.md](02-architecture-overview.md) — understand the design choices made in the kit itself
4. [05-build-and-run.md](05-build-and-run.md) — get the MCP server running locally
5. [07-coding-standards.md](07-coding-standards.md) — learn the conventions before writing code
6. [06-testing-guide.md](06-testing-guide.md) — understand how to run and write tests
7. [03-design-patterns.md](03-design-patterns.md) — understand the patterns used throughout the kit
8. [10-safe-development-workflow.md](10-safe-development-workflow.md) — learn the safe change workflow before touching anything
