# 04 — Request Flow

This document traces the major flows in the system. Understanding these flows is critical before making changes, because touching one step can break another.

---

## Flow 1: How Claude Uses the Kit (Plugin Load Flow)

When a developer opens Claude Code in a project that has dotnet-claude-kit configured, here is what happens:

```
Developer opens Claude Code
         │
         ▼
Claude Code reads .mcp.json
         │
         ├── Finds "cwm-roslyn-navigator" server entry
         │       │
         │       └── Spawns: cwm-roslyn-navigator --solution <path>
         │               │
         │               └── MCP server starts (see Flow 2)
         │
         ▼
Claude Code loads plugin rules
         │
         └── Reads all .claude/rules/*.md files into context
                 (agents.md, architecture.md, coding-style.md, ...)
                 These are always present in every conversation.

Developer sends a message (e.g., "add an Orders endpoint")
         │
         ▼
AGENTS.md routing logic matches intent
         │
         └── "create endpoint" → route to api-designer agent
                 │
                 ▼
         api-designer.md loaded
                 │
                 ├── Declares skill dependencies: minimal-api, error-handling, authentication
                 │
                 ▼
         Skills loaded (lazy):
         skills/minimal-api/SKILL.md
         skills/error-handling/SKILL.md
         skills/authentication/SKILL.md
                 │
                 ▼
         MCP tools called (before reading files):
         get_project_graph  → understand solution structure
         find_symbol        → find existing endpoint patterns
         get_public_api     → see what types already exist
                 │
                 ▼
         Claude generates response
         (guided by rules + skills + MCP tool results)
```

**What to remember:** Rules are always on. Skills load lazily. MCP tools run before file reads. This ordering is why the kit uses far fewer tokens than naive Claude usage.

---

## Flow 2: MCP Server Startup Flow

```
cwm-roslyn-navigator process starts
         │
         ▼
1. MSBuildLocator.RegisterDefaults()
   ← MUST be first, before any Roslyn types are referenced
   ← Locates the MSBuild instance installed with the .NET SDK
         │
         ▼
2. IHost builds with DI:
   - WorkspaceManager (singleton)
   - WorkspaceInitializer (IHostedService)
   - MCP Server with stdio transport
         │
         ▼
3. SolutionDiscovery.FindSolutionPath(args)
   ├── Check --solution / -s CLI argument
   │     ├── If .sln/.slnx file → use directly
   │     └── If directory → scan it (BFS, 3 levels)
   ├── If no arg → scan current working directory (BFS, 3 levels)
   │     └── Skips: .git, .vs, .idea, node_modules, bin, obj, packages, artifacts
   └── Returns null if nothing found
         │
         ▼
4. WorkspaceInitializer.SolutionPath set (may be null)

5. Host starts:
   - MCP server begins accepting stdio connections (IMMEDIATE)
   - WorkspaceInitializer.StartAsync() fires (BACKGROUND)
         │
         ▼
6. Background: WorkspaceManager.LoadSolutionAsync(path)
   ├── MSBuildWorkspace.Create()
   ├── OpenSolutionAsync(path)
   └── If ≤ 50 projects: WarmCompilationsAsync() (4 concurrent max)
       If > 50 projects: set lazy-load flag
         │
         ▼
7. Workspace READY
   (tools now return real results instead of StatusResponse)
```

**First-tool-call lazy discovery (if solution was null at startup):**

```
Tool call arrives before solution found
         │
         ▼
EnsureReadyOrStatusAsync() detects workspace not ready
         │
         ▼
IMcpServer.RequestRootsAsync()
         ← Asks the MCP host (Claude Code) for workspace roots
         │
         ▼
Scans each root for .sln/.slnx files
         │
         ├── Found → LoadSolutionAsync(path) [one-shot, never retried]
         └── Not found → Return StatusResponse("loading", "No solution found")
```

---

## Flow 3: MCP Tool Execution Flow

```
Claude calls a tool (e.g., find_symbol("Order"))
         │
         ▼
MCP server routes to FindSymbolTool.ExecuteAsync()
         │
         ▼
EnsureReadyOrStatusAsync(workspace)
         ├── Workspace ready → continue
         └── Not ready → return StatusResponse("loading", "...")
                         ← Claude sees this and retries or reports
         │
         ▼
RefreshChangedDocumentsAsync()
         ├── Check .csproj timestamps (5s cooldown)
         ├── Check new .cs files (60s cooldown)
         └── Reload changed documents, invalidate cache entries
         │
         ▼
Core tool logic (per-tool):
   find_symbol → Roslyn SymbolFinder.FindDeclarationsAsync()
   find_references → SymbolFinder.FindReferencesAsync()
   detect_antipatterns → run all IAntiPatternDetector.DetectAsync()
   get_diagnostics → compilation.GetDiagnostics()
   ... etc.
         │
         ▼
Results serialized to JSON (System.Text.Json)
         │
         └── File paths trimmed to "parent/file.cs"
             Snippets truncated at 80 chars
             Results capped (100/50/100 per tool)
         │
         ▼
JSON string returned to Claude via MCP protocol
```

---

## Flow 4: Slash Command Execution Flow

```
Developer types: /scaffold
         │
         ▼
Claude Code loads commands/scaffold.md
         │
         ▼
Command reads its "How" section:
   Step 1: Load architecture-advisor skill
   Step 2: Ask about existing architecture (call get_project_graph)
   Step 3: Ask about the feature to scaffold
   Step 4: Load scaffolding skill
   Step 5: Generate feature files in the appropriate pattern
   Step 6: Run verify (dotnet build)
         │
         ▼
Each step in sequence:
   - Load a skill (reads SKILL.md into context)
   - Call MCP tools (token-efficient analysis)
   - Ask the developer clarifying questions
   - Generate code
   - Verify (run build)
         │
         ▼
Command completes: feature files created and build verified
```

**Important:** Commands are pure markdown — they describe the workflow. Claude executes the workflow by following the command's "How" instructions.

---

## Flow 5: HTTP Request Flow (in a User's .NET Project)

This is the request lifecycle the kit recommends for ASP.NET Core Minimal APIs:

```
HTTP Request arrives
         │
         ▼
1. HTTPS Redirection (app.UseHttpsRedirection)
         │
         ▼
2. Exception Handler middleware (app.UseExceptionHandler)
   ← GlobalExceptionHandler wraps everything below
         │
         ▼
3. Authentication middleware (app.UseAuthentication)
   ← Decodes JWT / validates cookie
         │
         ▼
4. Authorization middleware (app.UseAuthorization)
   ← Checks [Authorize] / [AllowAnonymous] on endpoint
         │
         ▼
5. Endpoint routing (app.MapEndpoints())
   ← Matches URL to OrderEndpoints.Map() registered route
         │
         ▼
6. ValidationFilter<TRequest> runs
   ← FluentValidation fires before handler body
   ← Returns 400 ValidationProblem if invalid
         │
         ▼
7. Endpoint handler executes
   ← e.g., CreateOrderAsync(CreateOrderCommand cmd, CreateOrderHandler handler, ct)
         │
         ▼
8. Handler calls service
   ← e.g., handler.HandleAsync(cmd, ct) returns Result<Guid>
         │
         ├── Service calls DbContext directly (no repository)
         │     └── SaveChangesAsync(ct) commits
         │
         └── Service may publish message (Wolverine outbox)
         │
         ▼
9. Handler maps Result<T> to TypedResults
   ├── Success → TypedResults.Created(...)
   └── Failure → TypedResults.Problem(...)
         │
         ▼
10. Response serialized (System.Text.Json)
         │
         ▼
HTTP Response (200/201/400/422/500)
```

**If an exception escapes step 8:**

```
Unhandled exception
         │
         ▼
GlobalExceptionHandler.TryHandleAsync()
         ├── Logs exception with structured logging
         └── Returns 500 ProblemDetails
             (includes exception.Message only in Development)
```

---

## Flow 6: Anti-Pattern Detection Flow

This is how `detect_antipatterns` works internally:

```
Claude calls detect_antipatterns(file: "OrderService.cs")
         │
         ▼
DetectAntiPatternsTool.ExecuteAsync()
         │
         ▼
Get list of files to analyze (filtered by file/project arg)
         │
         ▼
For each file:
   ├── Get SyntaxTree from Roslyn
   ├── Get SemanticModel (for semantic detectors only)
   │
   └── Run all 9 detectors concurrently:
         │
         ├── AsyncVoidDetector (syntax)
         │   └── Find: async void methods
         │         (exception: event handlers — void EventHandler signature)
         │
         ├── SyncOverAsyncDetector (syntax)
         │   └── Find: .Result, .Wait(), .GetAwaiter().GetResult()
         │
         ├── HttpClientInstantiationDetector (syntax)
         │   └── Find: new HttpClient()
         │
         ├── DateTimeDirectUseDetector (syntax)
         │   └── Find: DateTime.Now, DateTime.UtcNow, DateTimeOffset.Now
         │
         ├── BroadCatchDetector (syntax)
         │   └── Find: catch(Exception), empty catch blocks
         │
         ├── LoggingInterpolationDetector (syntax)
         │   └── Find: $"..." inside logging method calls
         │
         ├── PragmaWithoutRestoreDetector (syntax)
         │   └── Find: #pragma warning disable without matching restore
         │
         ├── MissingCancellationTokenDetector (semantic)
         │   └── Find: public async methods without CancellationToken parameter
         │
         └── EfCoreNoTrackingDetector (semantic)
             └── Find: EF Core LINQ queries without .AsNoTracking()
         │
         ▼
Aggregate violations (cap at maxResults=100)
         │
         ▼
Return: [{Id, Severity, Message, File, Line, Snippet, Suggestion}]
```

**Syntax vs. semantic detectors:**
- Syntax-only: fast, no compilation needed (7 of 9 detectors)
- Semantic: requires `SemanticModel` (2 of 9 detectors — `MissingCancellationTokenDetector`, `EfCoreNoTrackingDetector`)

Semantic detectors are slower because getting a `SemanticModel` triggers Roslyn compilation for that file's project. This is why the cap (100 results) matters — it prevents unbounded compilation chains.

---

## Flow 7: Git Commit Flow (Hooks)

When a developer makes a commit:

```
git commit -m "feat: add order endpoint"
         │
         ▼
pre-commit-antipattern.sh runs
   ← Checks staged files for anti-patterns
   ← Blocks commit if critical violations found
         │
         ▼
pre-commit-format.sh runs
   ← Runs dotnet format on staged .cs files
         │
         ▼
Git creates commit object
         │
         ▼
(no post-commit hooks active by default)
```

During Claude Code editing sessions:

```
Claude calls Edit or Write tool
         │
         ▼
post-edit-format.sh fires (PostToolUse hook)
   ← Runs dotnet format on the edited file
   ← Format changes are auto-applied
         │
         ▼
post-scaffold-restore.sh fires
   ← Checks if a .csproj file was modified
   ← If yes, runs dotnet restore
   ← Prevents build errors from missing NuGet packages
```

And before Bash commands:

```
Claude calls Bash tool
         │
         ▼
pre-bash-guard.sh fires (PreToolUse hook)
   ← Validates the command for safety
   ← Blocks known dangerous commands
```

**Key implication:** As a developer on the kit, you will see auto-formatting applied after every file edit. Accept these changes — fighting the format hook causes style drift. (See `.claude/rules/hooks.md`.)
