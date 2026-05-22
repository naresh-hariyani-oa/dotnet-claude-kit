# 08 — Debugging Guide

This document covers how to diagnose and fix common problems in the kit, from MCP server issues to skill and hook problems.

---

## Debugging the MCP Server

### Problem: Tools return `{"State":"loading","Message":"..."}`

**Meaning:** The workspace is not yet ready. The solution is still loading in the background.

**Steps:**
1. Wait 5–10 seconds after Claude Code opens and try again
2. For very large solutions (100+ projects), wait up to 60 seconds
3. If the status never clears, check the next section

**Verify the server started:**

In a terminal, run the server manually against your solution:

```bash
cwm-roslyn-navigator --solution /path/to/MySolution.sln
```

If you see no output, it's waiting for MCP messages (expected). If you see an error, that's your clue.

### Problem: Tools return `{"State":"not_ready","Message":"No solution found"}`

**Meaning:** Solution discovery failed — no `.sln` or `.slnx` file was found.

**Diagnosis:**

```bash
# Check what's in your working directory
ls *.sln *.slnx 2>/dev/null

# Check 3 levels deep (what the server scans)
find . -maxdepth 3 -name "*.sln" -o -name "*.slnx" 2>/dev/null
```

**Fix options:**

Option A — Pass explicit path in `.mcp.json`:
```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator",
      "args": ["--solution", "${workspaceFolder}/MySolution.sln"]
    }
  }
}
```

Option B — Ensure Claude Code is opened at the directory containing the solution (not a subdirectory).

### Problem: MCP server fails to start

**Check 1: .NET 10 SDK installed**

```bash
dotnet --version
# Must be 10.x
```

**Check 2: Global tool installed**

```bash
dotnet tool list -g | grep cwm-roslyn-navigator
```

If missing:

```bash
dotnet tool install -g CWM.RoslynNavigator
```

**Check 3: Tool is on PATH**

```bash
which cwm-roslyn-navigator         # Linux/Mac
where cwm-roslyn-navigator          # Windows CMD
```

If not found, the dotnet tools directory may not be on your PATH. Add it:

```bash
# Add to .bashrc or .zshrc
export PATH="$PATH:$HOME/.dotnet/tools"
```

### Problem: `MSBuildLocator` error on startup

**Error message:** Something like `MSBuildLocator: Could not find MSBuild`

**Cause:** The .NET SDK is installed but MSBuild cannot be located automatically.

**Fix:**

```bash
# Verify MSBuild is available via dotnet
dotnet msbuild --version

# If that works but the tool fails, try setting MSBUILD_EXE_PATH explicitly
export MSBUILD_EXE_PATH=$(dotnet --list-sdks | sort -V | tail -1 | awk '{print $1}')
cwm-roslyn-navigator --solution MySolution.sln
```

### Problem: Tool returns wrong or stale results

**Cause:** The workspace has changed (you added a new file or edited a `.csproj`) but the server hasn't refreshed yet.

**How refresh works:**
- `.csproj` changes: detected with 5-second cooldown
- New `.cs` files: detected with 60-second cooldown
- Modified `.cs` files: checked on every tool call (file timestamp comparison)

**Fix:** Wait 60 seconds after adding new files, then retry. If the problem persists, restart the MCP server (restart Claude Code).

### Problem: `detect_antipatterns` misses a known anti-pattern

**Check 1:** Is the file in scope? By default the tool scans the whole solution. Use `--file` to narrow.

**Check 2:** Is the detector semantic or syntax-only? Semantic detectors (`MissingCancellationTokenDetector`, `EfCoreNoTrackingDetector`) require the project to compile. If there are compilation errors, semantic analysis may fail silently.

**Check 3:** Has the sample test been updated? Run:

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx \
  --filter "FullyQualifiedName~DetectAntiPatternsTests" --logger "console;verbosity=detailed"
```

Look at the test output to see which patterns are being detected against `AntiPatternExamples.cs`.

### Problem: Tests fail with "Solution not found" in CI

**Cause:** The test fixture resolves the sample solution path relative to the test assembly's output directory. If the test data is not copied to the output, the path is invalid.

**Check:** Verify `TestData/` is marked to copy on build in `CWM.RoslynNavigator.Tests.csproj`:

```xml
<ItemGroup>
  <Content Include="TestData/**">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

---

## Debugging Plugin Issues (Skills, Agents, Commands, Rules)

### Problem: A skill isn't being applied

**Cause:** Skills are loaded lazily — they only load when an agent or command explicitly loads them.

**How to check what's loaded:** Ask Claude directly in your session:

```
What skills and rules do you have loaded right now?
```

**Force-load a skill:** You can manually trigger a skill by referencing it:

```
Load the ef-core skill and help me optimize this query.
```

**If the skill file itself seems wrong:** Check the frontmatter format:

```bash
head -8 skills/ef-core/SKILL.md
```

Expected:
```yaml
---
name: ef-core
description: >
  EF Core 10 patterns...
---
```

### Problem: Rules aren't being applied

Rules load from `.claude/rules/`. All 10 rule files are always loaded. If a rule appears to be ignored:

1. Verify the file has `alwaysApply: true` in frontmatter:

```bash
head -5 .claude/rules/security.md
```

2. Check the rule file is ≤100 lines (longer files may be truncated):

```bash
wc -l .claude/rules/*.md
```

3. Rules take effect in the next message after they load — very long conversations may have compressed earlier context. Start a new conversation to reset.

### Problem: A command doesn't work as expected

Commands are markdown — they describe workflows in prose. If a command produces unexpected results:

1. Read the command file directly to see what it instructs:

```bash
cat commands/scaffold.md
```

2. Check if the command's required skills exist and have correct frontmatter:

```bash
# scaffold.md loads: scaffolding, vertical-slice, architecture-advisor
head -8 skills/scaffolding/SKILL.md
head -8 skills/vertical-slice/SKILL.md
head -8 skills/architecture-advisor/SKILL.md
```

3. Check the AGENTS.md routing table to confirm the right agent is handling the command.

---

## Debugging Hook Issues

### Problem: Post-edit format hook keeps changing my files

**Expected behavior:** The hook auto-formats after every edit. Accept these changes.

**If the hook changes something you believe is correct:**

1. Check what `dotnet format` produces manually:

```bash
dotnet format mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --verify-no-changes
```

2. Look at `.editorconfig` for the rule that triggers the change.

3. If the `.editorconfig` rule is wrong, fix it there — do not work around the format hook.

### Problem: Post-scaffold-restore runs when it shouldn't

The hook fires on every `Edit|Write` and checks if a `.csproj` was modified. If a non-csproj file was modified, the hook exits early with no action. If you're seeing actual restores triggering unexpectedly, check:

```bash
cat hooks/post-scaffold-restore.sh
```

### Problem: Pre-bash-guard blocks a safe command

The guard runs before every `Bash` tool call. If a legitimate command is blocked:

1. Read the guard script to understand what it blocks:

```bash
cat hooks/pre-bash-guard.sh
```

2. If the blocked command is genuinely safe for this project, the guard script may need an exception. This is a maintainer-level decision — do not unilaterally whitelist commands.

---

## Debugging Build Issues in the MCP Server

### Problem: `dotnet build` fails after my changes

**Step 1:** Read the full error output carefully:

```bash
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx 2>&1 | head -50
```

**Step 2:** Check for common Roslyn API issues:

- `SymbolFinder` methods require `Solution` or `Project`, not just `Compilation`
- `SemanticModel` is per-document — don't reuse across documents
- `MSBuildWorkspace` is single-use — don't call `OpenSolutionAsync` twice

**Step 3:** Use `get_diagnostics` via the MCP server (if it started) to get structured error output:

```
Claude, call get_diagnostics and show me the build errors.
```

**Step 4:** Run tests to see which assertions break and trace back to the cause:

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --logger "console;verbosity=detailed" 2>&1 | head -100
```

### Problem: Nullable warnings after changes

The MCP server has `<Nullable>enable</Nullable>`. Nullable violations appear as build warnings, and CI treats warnings as errors.

Fix patterns:

```csharp
// If a value could be null, handle it
var symbol = await SymbolFinder.FindDeclarationsAsync(...);
if (symbol is null) return new SymbolSearchResult([]);

// If you know it's not null, use ! (sparingly, with a comment why)
var compilation = await workspace.GetCompilationAsync(projectId, ct)!;
// compilation cannot be null here because we checked IsReady above

// Prefer null-coalescing for simple cases
var name = symbol?.Name ?? "Unknown";
```

---

## Useful Diagnostic Commands

```bash
# Verify MCP server is installed
dotnet tool list -g | grep cwm

# Check .NET SDK version
dotnet --version

# Build MCP server
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

# Run all tests
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

# Run specific test with verbose output
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx \
  --filter "FullyQualifiedName~TestClass" --logger "console;verbosity=detailed"

# Check formatting
dotnet format mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --verify-no-changes

# Check line counts (skills max 400, rules max 100, commands max 200)
wc -l skills/*/SKILL.md | sort -rn | head -10
wc -l .claude/rules/*.md | sort -rn | head -10
wc -l commands/*.md | sort -rn | head -10

# Validate skill frontmatter (manual check)
grep -r "^name:" skills/ | head -20
grep -r "^description:" skills/ | head -20
```
