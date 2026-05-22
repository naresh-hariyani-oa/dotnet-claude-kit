# dotnet-claude-kit — Project Setup Instructions

> These are instructions **for Claude** to execute when setting up a new project.
>
> **Developer:** Open `dotnet-claude-kit` in Claude Code, then send:
> ```
> @docs/setup-prompt.md
> Set up my project at C:\Projects\<your-project-path>
> ```
> Claude will handle everything below autonomously.

---

## What You Will Do

Given a target project path from the developer, perform these tasks in order:

1. Verify the machine prerequisites are met (MCP server installed, plugin registered)
2. Create `.mcp.json` at the target project root
3. Create `.claude/knowledge/` folder with three knowledge files (copied and adapted from this kit)
4. Create `.claude/settings.local.json` with pre-approved MCP permissions
5. Update the project's `CLAUDE.md` to import the knowledge files and MCP tool table
6. Confirm every file was created and report what was done

If the developer has not provided the target project path, ask for it before doing anything.

---

## Step 1 — Verify Machine Prerequisites

Check whether the MCP server is already installed and registered.

```bash
cwm-roslyn-navigator
```

If it starts and prints `CWM.RoslynNavigator 0.7.1.0` in the log, the server is installed. Press Ctrl+C or let it time out.

```bash
claude mcp list
```

If `cwm-roslyn-navigator` appears with `✓ Connected`, global MCP registration is done.

**If either check fails**, tell the developer to follow Parts 1 and 2 of `docs/manual-setup-guide.md` first, then come back. Do not proceed with project setup until the MCP server is working.

Check whether the plugin files are installed:

```bash
ls "C:/Users/$USERNAME/.claude/plugins/dotnet-claude-kit"
```

Expected output includes: `CLAUDE.md`, `skills/`, `rules/`, `commands/`, `agents/`. If the folder is missing or empty, the plugin files were not copied. Tell the developer to follow Part 3 Step 1 of `docs/manual-setup-guide.md`.

Check whether the plugin is registered in settings:

```bash
cat "C:/Users/$USERNAME/.claude/settings.json"
```

Look for both `extraKnownMarketplaces` with a `dotnet-claude-kit` entry AND `enabledPlugins` with `"dotnet-claude-kit@dotnet-claude-kit": true`. If either is missing, tell the developer to follow Part 3 Step 2 of `docs/manual-setup-guide.md`.

> On Windows with Git Bash, `$USERNAME` is the Windows environment variable. If `ls` fails, try replacing `$USERNAME` with your actual username.

---

## Step 2 — Confirm the Target Project

Verify the path the developer gave you exists and contains a `.sln` or `.slnx` file.

```bash
ls "<TARGET_PATH>"
```

Find the solution file:

```bash
find "<TARGET_PATH>" -maxdepth 3 -name "*.sln" -o -name "*.slnx" 2>/dev/null
```

Ask the developer to confirm which solution file to use if more than one is found. Record the solution path relative to `<TARGET_PATH>` — you will need it for `.mcp.json`.

Check whether `CLAUDE.md` already exists:

```bash
ls "<TARGET_PATH>/CLAUDE.md"
```

If it exists, you will update it (add to the top, not replace). If it does not exist, you will create it.

---

## Step 3 — Create `.mcp.json`

Create `<TARGET_PATH>/.mcp.json` with the solution path relative to the project root.

Example — if solution is at `api/MyProject.sln`:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator",
      "args": ["--solution", "api/MyProject.sln"]
    }
  }
}
```

Use the actual relative solution path confirmed in Step 2. Use forward slashes.

---

## Step 4 — Create `.claude/settings.local.json`

Create `<TARGET_PATH>/.claude/settings.local.json`:

```json
{
  "permissions": {
    "allow": [
      "mcp__cwm-roslyn-navigator__get_diagnostics",
      "mcp__cwm-roslyn-navigator__detect_antipatterns",
      "mcp__cwm-roslyn-navigator__get_project_graph",
      "mcp__cwm-roslyn-navigator__detect_circular_dependencies",
      "mcp__cwm-roslyn-navigator__get_test_coverage_map",
      "mcp__cwm-roslyn-navigator__find_dead_code",
      "mcp__cwm-roslyn-navigator__find_symbol",
      "mcp__cwm-roslyn-navigator__find_references",
      "mcp__cwm-roslyn-navigator__find_callers",
      "mcp__cwm-roslyn-navigator__find_implementations",
      "mcp__cwm-roslyn-navigator__get_public_api",
      "mcp__cwm-roslyn-navigator__get_type_hierarchy",
      "Bash(python3 *)",
      "Bash(dotnet build *)",
      "Bash(dotnet test *)",
      "Bash(dotnet run *)"
    ]
  },
  "enableAllProjectMcpServers": true,
  "enabledMcpjsonServers": [
    "cwm-roslyn-navigator"
  ]
}
```

> This includes the full set of MCP tool permissions so skills like `/health-check`, `/code-review`, `/verify`, and `/build-fix` can run without interruption.

---

## Step 5 — Create Knowledge Files

Create the folder:

```bash
mkdir -p "<TARGET_PATH>/.claude/knowledge"
```

Create three knowledge files. The content for each is below. **Do not copy them verbatim** — read the project's actual code first and adjust the patterns to match what is genuinely in that codebase. See `knowledge/controller-api-patterns.md`, `knowledge/multi-tenant-patterns.md`, and `knowledge/automapper-patterns.md` in this kit for guidance on what to cover and how to structure each file.

### How to adapt the knowledge files

For each file:

1. Read 2–3 representative examples from the target project (a controller, a service, a repository)
2. Identify where the project's actual patterns match the kit's templates — keep those sections
3. Identify where they differ — rewrite those sections with the project's real patterns
4. Remove sections that do not apply (e.g., if the project has no multi-tenancy, omit that file)

**For `oneadvancedlegal-pcms-api` specifically**, the three files already exist at:
- `<TARGET_PATH>/.claude/knowledge/controller-api-patterns.md`
- `<TARGET_PATH>/.claude/knowledge/multi-tenant-patterns.md`
- `<TARGET_PATH>/.claude/knowledge/automapper-patterns.md`

If these already exist with correct content, skip this step for that project.

---

## Step 6 — Update `CLAUDE.md`

If `CLAUDE.md` does not exist, create it with the block below as the full content.

If `CLAUDE.md` already exists, insert the block below **after any existing title or note block** and **before the first `## ` section** (e.g., before `## Project Overview` or `## Architecture`). Do not remove or replace any existing content.

**Block to add:**

```markdown
## Pattern References

@.claude/knowledge/controller-api-patterns.md
@.claude/knowledge/multi-tenant-patterns.md
@.claude/knowledge/automapper-patterns.md

## MCP Tools

Use `cwm-roslyn-navigator` before reading source files. The solution is pre-loaded at session start via `.mcp.json`.

| Task | MCP tool |
|---|---|
| Find a type or method | `find_symbol("<Name>")` |
| Find all callers of a method | `find_callers("<MethodName>")` |
| Find all implementations of an interface | `find_implementations("<IName>")` |
| Check project dependencies before adding a reference | `get_project_graph` |
| Verify no errors after a change | `get_diagnostics` |
| Check for anti-patterns | `detect_antipatterns` |

```

> Only include `@` imports for knowledge files that actually exist in `.claude/knowledge/`. Remove any lines for files you did not create in Step 5.

---

## Step 7 — Report

After completing all steps, report:

```
Setup complete for <TARGET_PATH>

Files created:
  ✓ .mcp.json  (solution: <relative-solution-path>)
  ✓ .claude/settings.local.json
  ✓ .claude/knowledge/controller-api-patterns.md
  ✓ .claude/knowledge/multi-tenant-patterns.md
  ✓ .claude/knowledge/automapper-patterns.md
  ✓ CLAUDE.md  (updated — Pattern References and MCP Tools sections added)

Next step for the developer:
  Open <TARGET_PATH> in a new Claude Code session.
  The MCP server will pre-load the solution at startup.
  Type / to see available skills.
  Try: find_symbol("") via MCP to confirm Roslyn is connected.
```

If any step was skipped (e.g., knowledge files already existed, CLAUDE.md already had the block), note that in the report.

---

## Reference

Full manual instructions with troubleshooting: `docs/manual-setup-guide.md`
