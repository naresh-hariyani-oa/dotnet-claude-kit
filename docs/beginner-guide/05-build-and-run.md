# 05 — Build and Run

This document covers how to set up the repository locally, build the MCP server, run tests, and configure the MCP server for use with Claude Code.

---

## Prerequisites

| Requirement | Version | Why needed |
|-------------|---------|-----------|
| .NET SDK | 10.0.x | Target framework for MCP server |
| Git | Any recent | Clone the repo |
| Claude Code | Latest | The AI assistant that uses the kit |
| bash / WSL / Git Bash | Any | Run hook scripts |

To verify your .NET SDK version:

```bash
dotnet --version
# Expected: 10.0.x
```

If you have multiple SDK versions installed, check that 10.x is active:

```bash
dotnet --list-sdks
```

---

## Clone the Repository

```bash
git clone https://github.com/CodeWithMukesh/dotnet-claude-kit.git
cd dotnet-claude-kit
```

---

## Building the MCP Server

The only buildable code in this repository is the Roslyn MCP server. All other components (skills, agents, commands, rules) are markdown files — no build step required.

### Build

```bash
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

A successful build outputs:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Check for Warnings

The kit enforces zero-warning discipline. If warnings appear after your changes:

```bash
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx -warnaserror
```

### Format Check

Before committing, verify formatting is clean:

```bash
dotnet format mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --verify-no-changes
```

If this fails, auto-fix with:

```bash
dotnet format mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

---

## Running Tests

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

### What the Tests Do

Tests use a shared `TestSolutionFixture` that loads the sample solution at `tests/TestData/SampleSolution/SampleSolution.sln` once per test collection. This is a real .NET solution with three projects:

- `SampleDomain/` — Domain entities (Order, Product, OrderItem)
- `SampleApi/` — Application services and anti-pattern examples
- `SampleInfrastructure/` — In-memory repository implementations

Tests then call each MCP tool against this real solution and assert on the results.

### Run a Specific Test

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx \
  --filter "FullyQualifiedName~FindSymbolTests"
```

### First-Run Note

The first test run may take 15–30 seconds because Roslyn loads the sample solution and compiles all three projects. Subsequent runs are faster (compilations are cached within the test session).

---

## Running the MCP Server Locally

### Option A: From Source (Development)

```bash
dotnet run --project mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj \
  -- --solution /path/to/your/solution.sln
```

The server starts and listens on stdio. You will not see a visible prompt — it's waiting for MCP protocol messages.

### Option B: Install as Global Tool (Recommended)

The MCP server ships as a NuGet global tool. Install the published version:

```bash
dotnet tool install -g CWM.RoslynNavigator
```

Or install from local build output:

```bash
dotnet pack mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj -o ./artifacts
dotnet tool install -g --add-source ./artifacts CWM.RoslynNavigator
```

Verify installation:

```bash
cwm-roslyn-navigator --version
# Expected: 0.7.1
```

### Updating the Global Tool

```bash
dotnet tool update -g CWM.RoslynNavigator
```

---

## Configuring the MCP Server with Claude Code

### Register globally (all projects)

```bash
claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator
```

This writes to `~/.claude/settings.json`:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator"
    }
  }
}
```

With this setup, the server auto-discovers the solution from the working directory when Claude Code opens a project.

### Register with explicit solution path (per-project)

Create or edit `.mcp.json` in your project root:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator",
      "args": ["--solution", "${workspaceFolder}"]
    }
  }
}
```

The `.mcp.json` in this repository's root already contains the minimal configuration:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator"
    }
  }
}
```

This works because the tool discovers the solution from the working directory automatically (BFS 3-level scan).

### Verify the MCP server is active in Claude Code

In a Claude Code conversation, ask:

```
What tools do you have available?
```

You should see `find_symbol`, `find_references`, `detect_antipatterns`, and the other 12 tools listed.

---

## Working on the Plugin (Markdown Files)

Skills, agents, commands, rules, and knowledge files require no build step. Edit them directly.

### Validate SKILL.md frontmatter

The GitHub Actions `validate.yml` workflow checks that all `SKILL.md` files have required frontmatter (`name`, `description`). To check locally:

```bash
# Check a specific skill's frontmatter
head -10 skills/caching/SKILL.md
```

Expected format:

```yaml
---
name: caching
description: >
  HybridCache patterns, output caching, ...
---
```

### Line count checks

Skills must stay under 400 lines, rules under 100 lines, commands under 200 lines. Check:

```bash
wc -l skills/caching/SKILL.md
wc -l .claude/rules/security.md
wc -l commands/scaffold.md
```

---

## CI/CD Pipelines

### `validate.yml` (runs on push to main, PR to main)

Checks:
- Every `skills/*/SKILL.md` has YAML frontmatter
- YAML frontmatter syntax is valid
- Markdown lint passes

### `publish-nuget.yml` (manual trigger)

Inputs: `version` (required), `dry-run` (optional)

Steps:
1. Build `mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj`
2. Run tests
3. Pack NuGet package
4. Publish to NuGet.org (or dry-run)

This workflow is for maintainers only. New contributors do not need to interact with it.

---

## Common Setup Issues

### "MSBuildLocator failed to register"

**Cause:** The MCP server calls `MSBuildLocator.RegisterDefaults()` on startup. If no MSBuild instance is found, this fails.

**Fix:** Install the .NET 10 SDK. MSBuild ships with the SDK. Ensure `dotnet` is in your `PATH`.

```bash
dotnet --version   # Must return 10.x
where dotnet        # Must find the SDK
```

### "Solution not found"

**Cause:** The server did BFS on the working directory and found no `.sln` or `.slnx` file.

**Fix:** Either:
1. Run `cwm-roslyn-navigator` from the directory containing your solution, or
2. Pass `--solution /explicit/path/to/Solution.sln`

### "Tools return 'loading' status"

**Cause:** The server started successfully but the solution is still loading in the background.

**Fix:** Wait 5–10 seconds after startup and retry. For very large solutions (100+ projects), initial load may take 30+ seconds.

### Build fails on `mcp/` with "unable to load project"

**Cause:** MSBuild cannot resolve all project references. Usually happens when packages are not restored.

**Fix:**

```bash
dotnet restore mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

### Format check fails

**Cause:** Code doesn't match `.editorconfig` rules (indentation, spacing, naming conventions).

**Fix:**

```bash
dotnet format mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

Then inspect the diff and commit the formatting changes separately.

---

## Development Workflow Summary

```
1. Clone repo
   git clone ...

2. Build to verify everything compiles
   dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

3. Run tests to establish baseline
   dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

4. Make changes (see 10-safe-development-workflow.md)

5. Verify changes
   dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
   dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
   dotnet format mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --verify-no-changes

6. Commit (conventional commit format)
   git add mcp/CWM.RoslynNavigator/src/Tools/NewTool.cs
   git commit -m "feat: add get_method_complexity tool"
```
