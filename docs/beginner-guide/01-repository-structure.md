# 01 — Repository Structure

## Top-Level Layout

```
dotnet-claude-kit/
├── .claude/                # Claude Code plugin local configuration
│   └── rules/              # 10 always-loaded rule files (coding style, security, testing, etc.)
├── .claude-plugin/         # Plugin metadata for the Claude Code marketplace
├── .codex/                 # Cursor IDE agent definitions
├── .cursor/                # Cursor IDE rules (dotnet-rules.md)
├── .github/                # GitHub Actions workflows + issue templates
├── agents/                 # 10 specialist agent definition files
├── commands/               # 16 slash command definition files
├── docs/                   # Documentation (you are here)
│   └── beginner-guide/     # This guide
├── hooks/                  # 7 automation shell scripts + hooks.json config
├── knowledge/              # Reference documents and Architecture Decision Records
│   └── decisions/          # 5 ADR files (ADR-001 through ADR-005)
├── mcp/                    # Roslyn MCP server (.NET 10 solution)
│   └── CWM.RoslynNavigator/
│       ├── src/            # 32 C# source files
│       └── tests/          # Test project + sample solution test data
├── mcp-configs/            # Template MCP server JSON configs for users
├── rules/                  # (See .claude/rules/ — rules live under .claude/)
├── skills/                 # 47 skill directories, one SKILL.md per skill
└── templates/              # 5 project templates (Web API, Blazor, Class Library, etc.)
```

---

## Directory-by-Directory Explanation

### `skills/`

Contains 47 subdirectories. Each subdirectory has exactly one file: `SKILL.md`.

```
skills/
├── architecture-advisor/SKILL.md
├── caching/SKILL.md
├── clean-architecture/SKILL.md
├── ddd/SKILL.md
├── ef-core/SKILL.md
├── error-handling/SKILL.md
├── minimal-api/SKILL.md
├── testing/SKILL.md
├── vertical-slice/SKILL.md
└── ... (38 more)
```

Skills are **not automatically loaded**. An agent or command loads a skill by name when the task requires it. Skills tell Claude *how* to approach a domain — they contain core principles, code patterns, anti-patterns with BAD/GOOD comparisons, and a decision guide table.

**Do not read all 47 skills to understand the codebase.** Read the one relevant to your current task.

### `agents/`

Contains 10 `.md` files. Each defines a specialist agent persona.

```
agents/
├── api-designer.md
├── build-error-resolver.md
├── code-reviewer.md
├── devops-engineer.md
├── dotnet-architect.md
├── ef-core-specialist.md
├── performance-analyst.md
├── refactor-cleaner.md
├── security-auditor.md
└── test-engineer.md
```

Agent files define: role, which skills to load, which MCP tools to prefer, and what NOT to handle. Routing logic is in `AGENTS.md` at the repository root — check that file to understand which agent handles which user intent.

### `commands/`

Contains 16 `.md` files. Each is a slash command (e.g., `/scaffold`, `/health-check`, `/tdd`).

Commands are **orchestrators** — they describe a multi-step workflow by invoking skills and agents. They do not implement logic directly. Each command has YAML frontmatter with a `description` field.

```
commands/
├── build-fix.md       → /build-fix
├── checkpoint.md      → /checkpoint
├── code-review.md     → /code-review
├── de-sloppify.md     → /de-sloppify
├── dotnet-init.md     → /dotnet-init
├── health-check.md    → /health-check
├── migrate.md         → /migrate
├── plan.md            → /plan
├── scaffold.md        → /scaffold
├── security-scan.md   → /security-scan
├── tdd.md             → /tdd
├── verify.md          → /verify
├── wrap-up.md         → /wrap-up
└── ... (3 more instinct commands)
```

### `.claude/rules/`

Contains 10 `.md` files. These are **always loaded into every Claude conversation** when the kit is active. They enforce non-negotiable baseline behaviors:

| File | What it enforces |
|------|-----------------|
| `agents.md` | Use MCP tools before reading files; subagent routing |
| `architecture.md` | No repository pattern over EF Core; endpoint group auto-discovery |
| `coding-style.md` | Primary constructors, records, sealed classes, file-scoped namespaces |
| `error-handling.md` | Result pattern for expected failures; ProblemDetails for HTTP |
| `git-workflow.md` | Conventional commit format; atomic commits; no force-push to main |
| `hooks.md` | Accept format hooks; never skip pre-commit with --no-verify |
| `packages.md` | Never hardcode versions; always `dotnet add package` without `--version` |
| `performance.md` | CancellationToken propagation; TimeProvider; IHttpClientFactory; HybridCache |
| `security.md` | No hardcoded secrets; parameterized queries; explicit [Authorize] or [AllowAnonymous] |
| `testing.md` | Integration tests first; no in-memory database; AAA pattern |

**Warning:** These rules are intentionally brief (max 100 lines each). They are not comprehensive guides — each has a corresponding skill that goes deeper.

### `knowledge/`

Reference material that agents and templates can cite. Unlike skills, knowledge files have no required format and are loaded on-demand rather than loaded by agents automatically.

```
knowledge/
├── breaking-changes.md          # .NET 9 → .NET 10 migration guide
├── common-antipatterns.md       # Patterns Claude should never generate
├── common-infrastructure.md     # Copy-paste implementations (Result, IEndpointGroup, etc.)
├── dotnet-whats-new.md          # .NET 10 new features
├── mediatr-to-mediator-migration.md  # Migration from MediatR to source-generated Mediator
├── package-recommendations.md   # Vetted NuGet package list
└── decisions/
    ├── 001-vsa-default.md       # ADR: Vertical Slice as default
    ├── 002-result-over-exceptions.md
    ├── 003-ef-core-default-orm.md
    ├── 004-hybrid-cache-default.md
    ├── 005-multi-architecture.md
    └── template.md              # Template for writing new ADRs
```

`knowledge/common-infrastructure.md` is particularly useful — it contains ready-to-copy implementations of the `Result` type, `IEndpointGroup`, `ValidationFilter`, `GlobalExceptionHandler`, and pagination helpers that the skills reference.

### `mcp/CWM.RoslynNavigator/`

The Roslyn MCP server. This is a real .NET 10 application that ships as a dotnet global tool.

```
mcp/CWM.RoslynNavigator/
├── CWM.RoslynNavigator.slnx    # Solution file
├── src/                        # Main project
│   ├── CWM.RoslynNavigator.csproj
│   ├── Program.cs              # Entry point
│   ├── WorkspaceManager.cs     # Roslyn workspace + LRU compilation cache
│   ├── WorkspaceInitializer.cs # Background service that loads solution on startup
│   ├── SolutionDiscovery.cs    # BFS solution file finder
│   ├── SymbolResolver.cs       # Cross-project symbol resolution
│   ├── Responses/
│   │   └── ToolResponses.cs    # 22 response record types
│   ├── Analyzers/              # 9 anti-pattern detectors (IAntiPatternDetector implementations)
│   └── Tools/                  # 15 MCP tool implementations
└── tests/
    ├── CWM.RoslynNavigator.Tests.csproj
    ├── SolutionDiscoveryTests.cs
    ├── Fixtures/
    │   └── TestSolutionFixture.cs
    └── TestData/
        └── SampleSolution/     # Real .NET solution used as test fixture
            ├── SampleApi/
            ├── SampleDomain/
            └── SampleInfrastructure/
```

See [05-build-and-run.md](05-build-and-run.md) for build and test commands.

### `templates/`

Five project templates, each with two files:

```
templates/
├── web-api/
│   ├── CLAUDE.md      # Drop into your Web API project root
│   └── README.md      # When and how to use this template
├── blazor-app/
├── class-library/
├── modular-monolith/
└── worker-service/
```

These are for **users of the kit**, not for kit development itself. A user copies the relevant `CLAUDE.md` into their project root to activate the kit's guidance for their project type.

### `hooks/`

Automation scripts that Claude Code executes automatically on certain events:

```
hooks/
├── hooks.json                  # Hook configuration (maps events to scripts)
├── pre-bash-guard.sh           # Runs before every Bash tool call (safety checks)
├── post-edit-format.sh         # Runs after every Edit/Write (auto-formats code)
├── post-scaffold-restore.sh    # Runs after Edit/Write (restores NuGet if .csproj changed)
├── post-test-analyze.sh        # Analyzes test output after tests run
├── pre-build-validate.sh       # Validates project before build
├── pre-commit-antipattern.sh   # Checks for anti-patterns before git commit
└── pre-commit-format.sh        # Formats code before git commit
```

The active hooks (wired in `hooks.json`) are:
- `PreToolUse[Bash]` → `pre-bash-guard.sh`
- `PostToolUse[Edit|Write]` → `post-edit-format.sh` + `post-scaffold-restore.sh`

### `docs/`

Four existing documentation files (before this guide):

| File | Purpose |
|------|---------|
| `dotnet-claude-kit-SPEC.md` | Complete repository specification (33 KB) — authoritative reference |
| `longform-guide.md` | Deep dive guide (13 KB) — setup, workflows, optimization |
| `shorthand-guide.md` | Quick reference (9 KB) — tables of commands, skills, agents, rules |
| `skill-benchmark-report.md` | Skill analysis and value rankings |

### Root Files

| File | Purpose |
|------|---------|
| `CLAUDE.md` | Development instructions for working on the kit itself |
| `AGENTS.md` | Agent routing table and skill load order |
| `README.md` | Public-facing introduction (26 KB) |
| `CONTRIBUTING.md` | Contribution guidelines |
| `CHANGELOG.md` | Version history |
| `.mcp.json` | Configures the MCP server for this repo (uses `cwm-roslyn-navigator` command) |
| `.editorconfig` | Code style for all file types |

---

## File Count Summary

| Category | Files |
|----------|-------|
| Skills | 47 `SKILL.md` files |
| Agents | 10 `.md` files |
| Commands | 16 `.md` files |
| Rules | 10 `.md` files |
| Knowledge | 12 `.md` files |
| Templates | 10 files (5 × CLAUDE.md + README.md) |
| MCP C# source | 32 `.cs` files |
| MCP test C# | 15 `.cs` files |
| Hooks | 7 shell scripts |
| **Total markdown** | **~115 `.md` files** |

---

## Where to Find Things Quickly

| "I want to find..." | "Look in..." |
|---|---|
| How a skill is structured | Any `skills/*/SKILL.md` |
| Which agent handles what | `AGENTS.md` → Routing Table |
| A command's workflow steps | `commands/<name>.md` |
| Why an architectural choice was made | `knowledge/decisions/00N-*.md` |
| A ready-to-copy infrastructure class | `knowledge/common-infrastructure.md` |
| The MCP tool signatures | `mcp/CWM.RoslynNavigator/src/Tools/` |
| Anti-pattern detector logic | `mcp/CWM.RoslynNavigator/src/Analyzers/` |
| What a rule enforces | `.claude/rules/<rule>.md` |
| Commit/PR conventions | `.claude/rules/git-workflow.md` |
| Vetted NuGet packages | `knowledge/package-recommendations.md` |
| .NET 10 breaking changes | `knowledge/breaking-changes.md` |
