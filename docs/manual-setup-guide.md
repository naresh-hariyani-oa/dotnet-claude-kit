# Manual Setup Guide

> Steps to install and wire up dotnet-claude-kit on a new developer machine.
> Follow Parts 1–3 once per machine. Part 4 is per target project.

---

## Prerequisites

- .NET 10 SDK installed (required to build the MCP server)
- Claude Code installed (CLI or VSCode extension)
- Git repository cloned: `C:\Projects\dotnet-claude-kit` (or any local path)

---

## Part 1 — Build and Install the MCP Server

The MCP server (`cwm-roslyn-navigator`) gives Claude Roslyn-powered intelligence: symbol lookup, reference finding, dependency graphs, anti-pattern detection. It must be built from this repo's source — no NuGet install.

### Step 1: Build and test

Run these from the repo root:

```bash
dotnet restore mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --configuration Release
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

All tests must pass before continuing.

### Step 2: Pack to local artifacts folder

```bash
dotnet pack mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj \
  --configuration Release \
  --output ./artifacts
```

This creates `artifacts/CWM.RoslynNavigator.0.7.1.nupkg`.

### Step 3: Install as global .NET tool

**First time:**
```bash
dotnet tool install -g CWM.RoslynNavigator --add-source ./artifacts
```

**If already installed (upgrading):**
```bash
dotnet tool update -g CWM.RoslynNavigator --add-source ./artifacts
```

### Step 4: Verify installation

```bash
cwm-roslyn-navigator
```

Expected: MCP server starts and prints version `CWM.RoslynNavigator 0.7.1.0` in the log output before shutting down (no `--version` flag — just run it and check the log line).

### Re-install after code changes

```bash
dotnet pack mcp/CWM.RoslynNavigator/src/CWM.RoslynNavigator.csproj \
  --configuration Release --output ./artifacts
dotnet tool update -g CWM.RoslynNavigator --add-source ./artifacts
```

---

## Part 2 — Register the MCP Server with Claude Code

The tool is installed globally. Now tell Claude Code to use it in every project.

```bash
claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator
```

This writes to `~/.claude.json` (not `settings.json`). Claude Code reads it at session start.

To verify the entry was written, check `~/.claude.json`:

```bash
cat "C:/Users/<your-username>/.claude.json"
```

You should see a `mcpServers` entry like:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "type": "stdio",
      "command": "cwm-roslyn-navigator",
      "args": [],
      "env": {}
    }
  }
}
```

**Verify the server is reachable:**
```bash
claude mcp list
```

You should see `cwm-roslyn-navigator` listed with status `✓ Connected`.

> **Note:** The MCP server is registered without a `--solution` flag here. The solution path is supplied per-project via `.mcp.json` (see Part 4). This global registration just makes the server available everywhere.

---

## Part 3 — Install the Plugin Globally

The plugin contains all skills, rules, agents, and commands. Installing it globally makes skills like `/health-check`, `/logging`, `/code-review`, etc. available in every project you open — without needing `--plugin-dir` flags.

### Step 1: Copy the plugin to the Claude plugins directory

```bash
# PowerShell
Copy-Item -Path "C:\Projects\dotnet-claude-kit" `
          -Destination "C:\Users\<your-username>\.claude\plugins\dotnet-claude-kit" `
          -Recurse -Force
```

Replace `<your-username>` with your Windows username.

> **Why copy instead of symlink?** Claude Code reads the plugin from the plugins directory. Keeping the source at `C:\Projects\dotnet-claude-kit` for development and the installed copy at `~/.claude/plugins/dotnet-claude-kit` for use means changes to the source don't automatically affect the installed version — re-copy after updates.

### Step 2: Register the plugin in settings.json

Edit `C:\Users\<your-username>\.claude\settings.json` and add the two sections below. If the file already has content, merge carefully — do not overwrite existing keys.

```json
{
  "extraKnownMarketplaces": {
    "dotnet-claude-kit": {
      "source": {
        "source": "directory",
        "path": "C:/Users/<your-username>/.claude/plugins/dotnet-claude-kit"
      }
    }
  },
  "enabledPlugins": {
    "dotnet-claude-kit@dotnet-claude-kit": true
  }
}
```

> **Key format:** `enabledPlugins` entries use `"plugin-id@marketplace-id": true`. Both IDs are `dotnet-claude-kit` here (plugin name @ marketplace name defined above).
> **Path separator:** Use forward slashes `/` in the path, even on Windows.

### Step 3: Restart Claude Code

Close and reopen the VSCode extension (or restart the CLI session). Skills will be listed when you type `/` in the chat.

### Updating the plugin after changes

When you make changes to skills, rules, or agents in `C:\Projects\dotnet-claude-kit`:

```bash
# PowerShell — re-copy to installed location
Copy-Item -Path "C:\Projects\dotnet-claude-kit" `
          -Destination "C:\Users\<your-username>\.claude\plugins\dotnet-claude-kit" `
          -Recurse -Force
```

Then restart Claude Code to pick up the changes.

---

## Part 4 — Set Up a Target Project

This part is per project. It wires up the MCP server solution path and loads the project-specific knowledge files into Claude's context.

The example here is `oneadvancedlegal-pcms-api`. Apply the same pattern to any .NET project.

### Step 4a: Create `.mcp.json` at the repo root

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator",
      "args": ["--solution", "api/Legal.Pcms.Integration.Api.sln"]
    }
  }
}
```

The `--solution` path is relative to the repo root where `.mcp.json` lives. This pre-loads the solution at session start so the first MCP tool call is instant.

> If your solution is at the repo root, use `"args": ["--solution", "MyProject.sln"]`.

### Step 4b: Create project-specific knowledge files

Knowledge files contain the patterns and conventions that are specific to this codebase. They are loaded by Claude every session via `@` imports in `CLAUDE.md`.

Create the folder:
```bash
mkdir -p .claude/knowledge
```

Create three files. The content below is accurate for `oneadvancedlegal-pcms-api` — adapt for other projects.

**`.claude/knowledge/controller-api-patterns.md`** — controller conventions, service pattern, repository pattern, error handling, logging, pagination. See the file at `C:\Projects\dotnet-claude-kit\knowledge\controller-api-patterns.md` as a starting template, then adjust for your project's actual patterns.

**`.claude/knowledge/multi-tenant-patterns.md`** — tenant identity resolution, `IMultiTenantService`, DI lifetime rules, background job patterns. See `C:\Projects\dotnet-claude-kit\knowledge\multi-tenant-patterns.md`.

**`.claude/knowledge/automapper-patterns.md`** — profile organization, explicit member mapping, DTO conventions, common pitfalls. See `C:\Projects\dotnet-claude-kit\knowledge\automapper-patterns.md`.

> **Important:** These files must be self-contained inside the target project. Do not use absolute paths that reference `dotnet-claude-kit` — production repos must not depend on your local development machine layout.

### Step 4c: Add Pattern References to `CLAUDE.md`

Add the following block near the top of the project's `CLAUDE.md` (after any existing note/header, before the project overview):

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

The `@` paths are relative to the `CLAUDE.md` file location (repo root), so `.claude/knowledge/...` resolves correctly.

### Step 4d: Create `.claude/settings.local.json`

This file pre-approves the MCP tool calls and bash commands that skills like `/health-check` and `/code-review` use heavily. Without it, Claude prompts for permission on every tool call, which interrupts automated skill workflows.

Create `.claude/settings.local.json` at the repo root:

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
      "Bash(python3 *)",
      "Bash(dotnet build *)"
    ]
  },
  "enableAllProjectMcpServers": true,
  "enabledMcpjsonServers": [
    "cwm-roslyn-navigator"
  ]
}
```

**What each setting does:**

| Setting | Purpose |
|---|---|
| `permissions.allow` | Pre-approves specific MCP tools and bash commands — no prompt on each call |
| `enableAllProjectMcpServers` | Activates MCP servers defined in `.mcp.json` for this project automatically |
| `enabledMcpjsonServers` | Names which servers from `.mcp.json` to enable (must match the key in `.mcp.json`) |

> **`settings.local.json` vs `settings.json`:** `settings.local.json` is project-local and should be committed to the repo so every developer gets the same permissions automatically. `settings.json` (in `~/.claude/`) is your personal machine-wide config.

> **Extend as needed:** If skills prompt for other tool permissions during a session, add those tools to the `allow` array here. The permission name format is `mcp__<server-name>__<tool-name>` for MCP tools and `Bash(<command-pattern>)` for shell commands.

---

## Summary — What Each Part Delivers

| Part | What you gain |
|---|---|
| Part 1: MCP server built | `cwm-roslyn-navigator` binary available on PATH |
| Part 2: MCP server registered | Roslyn tools (`find_symbol`, `find_references`, `get_diagnostics`, etc.) active in every project |
| Part 3: Plugin installed globally | All skills (`/health-check`, `/logging`, `/code-review`, etc.) available in every project |
| Part 4: Target project wired | Solution pre-loaded at session start; project-specific patterns (controller conventions, multi-tenancy, AutoMapper) in Claude's context |

---

## Checklist

### Machine setup (once per developer)

- [ ] `dotnet restore` — MCP solution packages restored
- [ ] `dotnet build` — MCP server compiles without errors
- [ ] `dotnet test` — all MCP tests green
- [ ] `dotnet pack` — `artifacts/CWM.RoslynNavigator.*.nupkg` created
- [ ] `dotnet tool install -g` — tool installed from local artifacts
- [ ] `cwm-roslyn-navigator` — starts and logs version `0.7.1.0`
- [ ] `claude mcp add --scope user` — MCP server registered globally
- [ ] `claude mcp list` — shows `cwm-roslyn-navigator ✓ Connected`
- [ ] Plugin copied to `~/.claude/plugins/dotnet-claude-kit`
- [ ] `settings.json` updated with `extraKnownMarketplaces` and `enabledPlugins`
- [ ] Claude Code restarted — skills visible when typing `/`

### Per target project

- [ ] `.mcp.json` created at repo root with correct solution path
- [ ] `.claude/knowledge/` folder created with three knowledge files
- [ ] `CLAUDE.md` updated with `## Pattern References` and `## MCP Tools` sections
- [ ] `.claude/settings.local.json` created with MCP permissions and `enabledMcpjsonServers`
- [ ] New session opened in project — ask "what controller attributes are required?" — Claude answers from the knowledge files
- [ ] Try `find_symbol("AccountsController")` via MCP — returns file path directly
- [ ] Run `/health-check` — completes without permission prompts

---

## Troubleshooting

**Skills not showing after restart**
- Confirm `~/.claude/plugins/dotnet-claude-kit/CLAUDE.md` exists (the plugin needs a root `CLAUDE.md`)
- Confirm `settings.json` uses forward slashes in the path and `"source": "directory"` (not `"local"`)
- Confirm the `enabledPlugins` key format is `"dotnet-claude-kit@dotnet-claude-kit": true` (boolean, not an object)

**MCP server not connecting in a project**
- Check `claude mcp list` in that project — server should show `✓ Connected`
- If `✗ Failed`: run `cwm-roslyn-navigator` manually to see error output
- Common cause: .NET 10 SDK not on PATH when Claude Code starts

**`.mcp.json` solution path not found**
- The path in `--solution` is relative to the repo root (where `.mcp.json` lives)
- Verify the `.sln` / `.slnx` file exists at that relative path: `ls api/Legal.Pcms.Integration.Api.sln`

**Knowledge files not loaded**
- `@` paths in `CLAUDE.md` are relative to `CLAUDE.md` location (repo root)
- Verify files exist: `ls .claude/knowledge/`
- Check for typos in the filename — paths are case-sensitive on some systems

**MCP tools prompt for permission on every call**
- Create `.claude/settings.local.json` with the `permissions.allow` list from Step 4d
- Confirm `enabledMcpjsonServers` includes `"cwm-roslyn-navigator"` — this activates `.mcp.json` servers
- Without `enableAllProjectMcpServers: true`, the `.mcp.json` server may not auto-start
