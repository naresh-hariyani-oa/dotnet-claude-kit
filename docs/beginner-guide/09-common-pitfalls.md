# 09 — Common Pitfalls

This document documents real mistakes new contributors make in this repository. Each entry explains what the mistake is, why it's a problem, and the correct approach.

---

## Pitfalls When Working on the MCP Server

### Pitfall 1: Calling `MSBuildLocator.RegisterDefaults()` too late

**Mistake:** Adding code that references Roslyn types before `MSBuildLocator.RegisterDefaults()` runs.

```csharp
// WRONG — Roslyn types referenced before MSBuild is registered
using Microsoft.CodeAnalysis;

var solution = await workspace.OpenSolutionAsync(path); // JIT binds Roslyn here
MSBuildLocator.RegisterDefaults(); // Too late
```

**Why it's a problem:** Roslyn assemblies bind to MSBuild at the moment the first Roslyn type is JIT-compiled. If MSBuild hasn't been located yet, Roslyn fails to load MSBuild targets.

**Correct approach:** `MSBuildLocator.RegisterDefaults()` is the very first line in `Program.cs`, before the host is built, before any `using` directives bring Roslyn types into scope at runtime.

---

### Pitfall 2: Opening a new `MSBuildWorkspace` in a tool

**Mistake:**

```csharp
// WRONG — creating a second workspace inside a tool
public static async Task<string> ExecuteAsync(...)
{
    var workspace = MSBuildWorkspace.Create();
    var solution = await workspace.OpenSolutionAsync(solutionPath);
    // ... use solution
}
```

**Why it's a problem:** `MSBuildWorkspace` is expensive to create. Creating one per tool call would take 5–30 seconds per call. The server has one shared `WorkspaceManager` singleton for a reason.

**Correct approach:** Accept `WorkspaceManager workspace` as a parameter. Call `workspace.GetSolutionAsync()` or `workspace.GetCompilationAsync(projectId, ct)`. Never create your own workspace.

---

### Pitfall 3: Not checking workspace readiness

**Mistake:**

```csharp
// WRONG — calling workspace methods without checking readiness
public static async Task<string> ExecuteAsync(WorkspaceManager workspace, ...)
{
    var solution = await workspace.GetSolutionAsync(); // NullReferenceException if loading
    // ...
}
```

**Why it's a problem:** The workspace loads asynchronously. If a tool call arrives before loading completes, `GetSolutionAsync()` may return null or throw.

**Correct approach:** Every tool must call `EnsureReadyOrStatusAsync()` first:

```csharp
public static async Task<string> ExecuteAsync(WorkspaceManager workspace, ...)
{
    var statusOrNull = await workspace.EnsureReadyOrStatusAsync();
    if (statusOrNull is not null) return statusOrNull; // Returns StatusResponse JSON

    var solution = await workspace.GetSolutionAsync()!; // Safe now
    // ...
}
```

---

### Pitfall 4: Reusing a `SemanticModel` across documents

**Mistake:**

```csharp
// WRONG — getting SemanticModel once and reusing it for all documents
var compilation = await project.GetCompilationAsync(ct);
var semanticModel = compilation!.GetSemanticModel(someDocument.GetSyntaxTreeAsync().Result);

foreach (var doc in project.Documents)
{
    var tree = await doc.GetSyntaxTreeAsync(ct);
    var root = await tree.GetRootAsync(ct);
    // Using the WRONG semanticModel for this document
    var analysis = semanticModel.GetTypeInfo(someNode);
}
```

**Why it's a problem:** A `SemanticModel` is bound to a specific `SyntaxTree`. Using it with nodes from a different tree produces wrong results or throws.

**Correct approach:** Get a `SemanticModel` per document within the loop:

```csharp
foreach (var doc in project.Documents)
{
    var tree = await doc.GetSyntaxTreeAsync(ct);
    var root = await tree.GetRootAsync(ct);
    var semanticModel = compilation!.GetSemanticModel(tree!);
    // Now semanticModel is correct for this document
}
```

---

### Pitfall 5: Adding test data to `AntiPatternExamples.cs` for non-anti-pattern purposes

**Mistake:** Adding a clean, well-written class to `SampleApi/AntiPatternExamples.cs` to support a new test.

**Why it's a problem:** Tests for anti-pattern detectors assert that `AntiPatternExamples.cs` contains violations. If you add clean code there, you may unintentionally reduce violation counts and break existing tests.

**Correct approach:** Add new test data in a new file under `SampleApi/`, `SampleDomain/`, or `SampleInfrastructure/` with a clear name indicating its purpose. Name it after what it demonstrates, not where it lives.

---

### Pitfall 6: Hardcoding result caps in new tools inconsistently

**Mistake:**

```csharp
// WRONG — no cap; can return thousands of results
var results = symbols.Select(MapToResult).ToList();
return JsonSerializer.Serialize(new MyNewResult(results));
```

**Why it's a problem:** Uncapped results consume tokens proportionally. A tool returning 500 symbols in a large solution sends thousands of tokens to Claude, which wastes context window budget and slows response time.

**Correct approach:** Always apply a cap and expose it as a parameter with a sensible default:

```csharp
// CORRECT — consistent with other tools
public static async Task<string> ExecuteAsync(
    WorkspaceManager workspace,
    [Description("Maximum results to return")] int maxResults = 50,
    ...)
{
    var results = symbols
        .Take(maxResults)
        .Select(MapToResult)
        .ToList();

    return JsonSerializer.Serialize(new MyNewResult(results, Count: results.Count, TotalFound: symbols.Count()));
}
```

Include `TotalFound` so Claude knows if results were truncated.

---

## Pitfalls When Editing Plugin Files (Skills, Rules, Commands)

### Pitfall 7: Writing a skill over 400 lines

**Mistake:** A skill file grows past 400 lines because "there's more to cover."

**Why it's a problem:** Skills are loaded into Claude's context window. A 600-line skill costs 50% more tokens than a 400-line skill. Context window budget is finite — every unnecessary line reduces what Claude can hold for the actual work.

**Correct approach:** Cut ruthlessly. Every line must earn its place. If a topic needs more than 400 lines, split it into two skills with distinct names. Cross-reference them with a "See also:" note.

---

### Pitfall 8: Writing a rule over 100 lines

**Mistake:** A rule file in `.claude/rules/` grows past 100 lines.

**Why it's a problem:** Rules are loaded into EVERY conversation. They are in context at all times. A 200-line rule doubles the always-on token cost compared to a 100-line rule.

**Correct approach:** Rules are authoritative constraints, not tutorials. State the rule, state the rationale in one sentence, show a minimal BAD/GOOD example. The corresponding skill carries the full explanation.

---

### Pitfall 9: Adding a skill without required sections

**Mistake:** Creating a `skills/my-skill/SKILL.md` with just a description and some patterns, omitting Anti-patterns or Decision Guide.

**Why it's a problem:** The CI `validate.yml` workflow checks frontmatter, but not section completeness. An incomplete skill is usable but provides half its intended value — anti-patterns are often the most important part because they prevent common mistakes.

**Correct approach:** Every skill must have:
1. YAML frontmatter (`name`, `description`)
2. Core Principles (3-5 numbered)
3. Patterns (code examples)
4. Anti-patterns (BAD/GOOD comparisons)
5. Decision Guide (table)

---

### Pitfall 10: Writing anti-patterns without BAD/GOOD comparisons

**Mistake:**

```markdown
## Anti-patterns

- Don't use `new HttpClient()` — use `IHttpClientFactory`
- Don't use `DateTime.Now` — use `TimeProvider`
```

**Why it's a problem:** Bare bullet points don't stick. Developers need to see both the wrong code and the right code side by side to internalize the pattern.

**Correct approach:**

```markdown
## Anti-patterns

### Direct HttpClient instantiation

```csharp
// BAD — socket exhaustion under load
var client = new HttpClient();
var response = await client.GetAsync("/api/orders");
```

```csharp
// GOOD — pooled, DNS-aware
public sealed class OrderApiClient(HttpClient client) { }
// Registered: builder.Services.AddHttpClient<OrderApiClient>();
```
```

---

### Pitfall 11: Assuming a package version from memory

**Mistake:**

```xml
<PackageReference Include="FluentValidation" Version="11.0.0" />
```

**Why it's a problem:** Package versions in training data are outdated. `FluentValidation` may be at `11.10.x` or higher by the time you read this. Hardcoded old versions may conflict with .NET 10 compatibility or miss important bug fixes.

**Correct approach:**

```bash
# Always add without --version to get latest stable
dotnet add package FluentValidation
# Then look at the .csproj to see what version was resolved
```

---

### Pitfall 12: Modifying `Program.cs` for every new endpoint (in user projects)

**Mistake:**

```csharp
// Wrong — adding endpoint mapping calls to Program.cs
app.MapGroup("/api/orders").MapOrderEndpoints();
app.MapGroup("/api/products").MapProductEndpoints();
app.MapGroup("/api/users").MapUserEndpoints(); // Program.cs keeps growing
```

**Why it's a problem:** Every new feature requires a `Program.cs` change. In teams, this creates merge conflicts on every feature branch. It violates the Open/Closed Principle.

**Correct approach:** Use `IEndpointGroup` + `app.MapEndpoints()`. Program.cs calls `app.MapEndpoints()` once and never changes when new endpoints are added. See `knowledge/common-infrastructure.md` for the full implementation.

---

## Quick-Reference: What Not to Do

| Don't | Do instead |
|-------|-----------|
| Create `MSBuildWorkspace` in a tool | Use injected `WorkspaceManager` |
| Reference Roslyn types before `MSBuildLocator.RegisterDefaults()` | Ensure it's the first statement in `Program.cs` |
| Skip `EnsureReadyOrStatusAsync()` in a tool | Always check readiness first |
| Return uncapped results | Cap at 50–100 with `TotalFound` for overflow signal |
| Reuse `SemanticModel` across documents | Get one per document per loop iteration |
| Write a skill over 400 lines | Cut ruthlessly; split if needed |
| Write a rule over 100 lines | Rules are constraints, not tutorials |
| Create a skill without Anti-patterns section | Anti-patterns are mandatory |
| Hardcode package versions | `dotnet add package <name>` without `--version` |
| Add clean code to `AntiPatternExamples.cs` | Create a new test data file |
| Add new endpoint mappings to `Program.cs` | Use `IEndpointGroup` + `app.MapEndpoints()` |
| Wrap `DbContext` in a repository interface | Inject `DbContext` directly |
| Use `DateTime.Now` or `DateTime.UtcNow` | Inject `TimeProvider` |
| Use `new HttpClient()` | Use `IHttpClientFactory` |
| Use `IMemoryCache` manual cache-aside | Use `HybridCache.GetOrCreateAsync()` |
