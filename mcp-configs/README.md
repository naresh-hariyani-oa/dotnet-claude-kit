# MCP Server Configuration

This directory contains template MCP (Model Context Protocol) server configurations for use with Claude Code and other MCP-compatible clients.

## Servers

### cwm-roslyn-navigator

**Purpose:** Roslyn-powered .NET code intelligence -- symbol lookup, reference finding, diagnostics, dependency graphs, antipattern detection, and more.

**When to use:** Any .NET project. This is the primary MCP server for dotnet-claude-kit and should always be configured.

**Prerequisites:**
- Install the tool globally: `dotnet tool install -g CWM.RoslynNavigator`
- Ensure a `.sln` or `.slnx` file exists in your workspace root (or within 3 levels of nesting)

### github

**Purpose:** GitHub API access -- issues, pull requests, repository metadata, file contents.

**When to use:** When working with GitHub-hosted repositories and you need to read issues, create PRs, or access repository data through MCP tools.

**Prerequisites:**
- Node.js installed (for `npx`)
- Set `GITHUB_TOKEN` environment variable with a GitHub Personal Access Token

### filesystem

**Purpose:** Direct filesystem read/write access scoped to the workspace.

**When to use:** When MCP tools need file access beyond what the default Claude Code tools provide.

**Prerequisites:**
- Node.js installed (for `npx`)

## How to Use

### Claude Code

**Preferred pattern:** the kit's root `.mcp.json` registers `cwm-roslyn-navigator` with no arguments — the server auto-discovers the `.sln`/`.slnx` in your workspace (searching up to 3 levels deep). Use that unless you need to pin a specific solution:

```json
{
  "mcpServers": {
    "cwm-roslyn-navigator": {
      "command": "cwm-roslyn-navigator"
    }
  }
}
```

To use this directory's fuller template instead, merge the server configurations into your project's `.mcp.json` file at the repository root:

```bash
# If .mcp.json does not exist yet, copy the template
cp mcp-configs/mcp-servers.json .mcp.json

# If .mcp.json already exists, merge the servers manually
```

Replace `${workspaceFolder}` with your actual workspace path — Claude Code does not expand this VS Code-style variable, so leaving it in place will break the server. Only keep the variable if your client (e.g. VS Code, Cursor) supports it.

Replace `${GITHUB_TOKEN}` with your token or set it as an environment variable.

### Cursor IDE

Add the server configurations to your Cursor MCP settings file (`.cursor/mcp.json` or global settings).

### VS Code (Copilot)

Add to `.vscode/mcp.json` in your project root using the same format.

## Customization

- Remove servers you do not need. Only `cwm-roslyn-navigator` is required for dotnet-claude-kit.
- Add additional MCP servers (database, cloud provider, monitoring) as needed for your project.
- The `--solution` argument for `cwm-roslyn-navigator` accepts a path to a specific `.sln`/`.slnx` file if you have multiple solutions.
