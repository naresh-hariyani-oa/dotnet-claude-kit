# Hooks

This directory contains three kinds of scripts. Only the first kind runs
automatically through Claude Code.

## Claude Code hooks (declared in `hooks.json`)

These receive the hook payload as JSON on stdin and run automatically while
Claude works:

| Script | Event | Purpose |
|---|---|---|
| `pre-bash-guard.sh` | PreToolUse (Bash) | Blocks destructive commands (force push, `git reset --hard`, unsafe `rm -rf`) |
| `post-edit-format.sh` | PostToolUse (Edit\|Write) | Runs `dotnet format` on edited `.cs` files |
| `post-scaffold-restore.sh` | PostToolUse (Edit\|Write) | Runs `dotnet restore` after `.csproj` changes |

## Git pre-commit hooks (install manually)

These are standard git hooks, not Claude Code hooks. Wire them into your
repo's pre-commit hook:

| Script | Purpose |
|---|---|
| `pre-commit-format.sh` | Fails the commit if `dotnet format --verify-no-changes` finds issues |
| `pre-commit-antipattern.sh` | Blocks commits that **add** `async void` (AP001), sync-over-async (AP002), `new HttpClient()` (AP003), or `DateTime.Now`/`UtcNow` (AP004) |

```bash
# One-time setup per clone — .git/hooks/pre-commit
#!/usr/bin/env bash
bash hooks/pre-commit-format.sh && bash hooks/pre-commit-antipattern.sh
```

`pre-commit-antipattern.sh` reads its rules from `lib/antipattern-scan.awk` —
keep the two files together.

**Options**

| | |
|---|---|
| `// cwm:ignore` | Suppress every rule on that line |
| `// cwm:ignore AP004` | Suppress only the named rule(s) |
| `CWM_ANTIPATTERN_WARN_ONLY=1` | Report findings without blocking the commit |

### Why does this hook scan text instead of using a Roslyn analyzer?

Because a git hook cannot reach the analyzer. The kit detects these same four
patterns in three tiers, and each tier is placed where it can actually run:

| Tier | Where it runs | Cost | Purpose |
|---|---|---|---|
| 1. `pre-commit-antipattern.sh` | git pre-commit, bare shell | milliseconds, no build | Last gate before history |
| 2. `detect_antipatterns` (Roslyn MCP) | Claude Code session | workspace load | The authoritative pass |
| 3. Analyzer packages in your build | IDE + CI | full compile | Team-wide enforcement |

Tier 2 is strictly more precise — it has a `SemanticModel`, so it knows whether
`.Result` sits on a `Task<T>` or on your own `Result<T>`. But a git hook runs in
a bare shell where no MCP server is reachable, and shelling out to `dotnet build`
in front of every commit costs seconds and fails outright on a red build, which
is exactly when work-in-progress gets committed.

So tier 1 scans text, and is written to deserve that spot rather than to
approximate tier 2:

- **Only the lines the commit adds are checked** — a legacy `DateTime.Now` in an
  untouched line never blocks an unrelated edit.
- **Comments and string literals are stripped**, tracking block comments,
  verbatim strings, and raw strings across line boundaries.
- **Generated, test, and migration sources are exempted** per rule, matching
  `SourceClassifier` in the MCP server exactly.
- **Ambiguous matches stay silent.** `.Result` is reported only when the receiver
  is visibly task-like; `new HttpClient(handler)` is never reported. Tier 1 takes
  false negatives to guarantee zero false positives — tier 2 catches the rest.

Run tier 2 (via `/verify`) for the exhaustive pass. Rationale and the
rule-addition process: [ADR-006](../knowledge/decisions/006-three-tier-antipattern-detection.md).

## Utility scripts (invoked by commands and workflows)

Run these directly or let kit commands (`/verify`, `/tdd`, `/health-check`)
invoke them:

| Script | Usage |
|---|---|
| `pre-build-validate.sh` | `bash hooks/pre-build-validate.sh [solution-dir]` — checks solution structure (sln file, Directory.Build.props, global.json, test projects) |
| `post-test-analyze.sh` | `dotnet test 2>&1 \| bash hooks/post-test-analyze.sh` — summarizes test results with actionable next steps |
