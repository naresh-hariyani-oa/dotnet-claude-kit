# 10 — Safe Development Workflow

This document describes the workflow you should follow before, during, and after making changes to the repository. Following this workflow prevents regressions and keeps the kit in a consistent state.

---

## Before Making Any Change

### 1. Understand what you're changing

Before touching any file, answer these three questions:

1. **What does this file do?** Read the relevant section of this guide (e.g., if editing a skill, see [01-repository-structure.md](01-repository-structure.md#skills) and [07-coding-standards.md](07-coding-standards.md)).

2. **What depends on it?** Use `AGENTS.md` to check which agents load the skill. Use `grep` to find command files that reference it.

3. **What will break if I get this wrong?** For MCP server changes: tests. For skill/rule changes: the guidance Claude gives to all users.

### 2. Establish a clean baseline

Run the build and tests before you start:

```bash
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

If the baseline is already broken, **fix it before adding your change**. Do not layer your change on top of an existing failure — you will not be able to distinguish your regression from the pre-existing one.

### 3. For non-trivial changes, plan first

If your change involves 3+ steps, new files, or architectural decisions: plan it before writing code.

Use the `/plan` command in Claude Code:

```
/plan Add a new MCP tool that returns method cyclomatic complexity
```

Iterate on the plan until it's solid. Writing detailed plans upfront reduces ambiguity and prevents the "I'm halfway through and realized this is the wrong approach" problem.

---

## Making Changes

### MCP Server Changes (C# code)

**Adding a new MCP tool:**

1. Create `mcp/CWM.RoslynNavigator/src/Tools/MyNewTool.cs`
2. Decorate the class with `[McpServerToolType]`
3. Add a static method with `[McpServerTool(Name = "my_new_tool")]`
4. Accept `WorkspaceManager workspace` as the first parameter
5. Call `EnsureReadyOrStatusAsync()` at the start
6. Add response record types to `Responses/ToolResponses.cs` if needed
7. Add a test file `tests/Tools/MyNewToolTests.cs`
8. Add test data to `SampleSolution` if the tool needs specific patterns to find

**Adding a new anti-pattern detector:**

1. Create `mcp/CWM.RoslynNavigator/src/Analyzers/MyDetector.cs` implementing `IAntiPatternDetector`
2. Register it in `DetectAntiPatternsTool`'s detector collection
3. Add intentional examples of the pattern to `SampleApi/AntiPatternExamples.cs`
4. Add assertions to `tests/Tools/DetectAntiPatternsTests.cs`

**Modifying existing tools:**

- Only change one tool at a time
- Run its specific test after the change:

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx \
  --filter "FullyQualifiedName~FindSymbolTests"
```

- Then run all tests to check for regressions:

```bash
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx
```

### Plugin File Changes (Skills, Agents, Commands, Rules)

**Adding a new skill:**

1. Create the directory: `skills/my-skill/`
2. Create `skills/my-skill/SKILL.md` with the required frontmatter:

```yaml
---
name: my-skill
description: >
  What this skill covers. Include trigger keywords.
  When should an agent load this skill?
---
```

3. Write the required sections: Core Principles, Patterns, Anti-patterns, Decision Guide
4. Check line count: `wc -l skills/my-skill/SKILL.md` — must be ≤400
5. Wire it into `AGENTS.md` if an agent should load it

**Adding a new agent:**

1. Create `agents/my-agent.md`
2. Define: role, skill dependencies (in load order), MCP tool preferences, boundaries
3. Add to the routing table in `AGENTS.md`

**Adding a new command:**

1. Create `commands/my-command.md` with YAML frontmatter:

```yaml
---
description: >
  What this command does in one or two sentences.
---
```

2. Write: What, When, How, Example, Related sections
3. Check line count: must be ≤200
4. Add to slash command table in `AGENTS.md`

**Modifying an existing rule:**

Rules are always in context — every line costs tokens in every conversation. Be conservative:

- Only add to a rule if the information is truly non-obvious and not covered elsewhere
- Prefer updating the corresponding skill instead of expanding the rule
- Verify line count stays ≤100: `wc -l .claude/rules/<rule>.md`

---

## Verifying Changes

### For MCP server changes

```bash
# 1. Build succeeds
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

# 2. No new warnings (treat warnings as errors)
dotnet build mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx -warnaserror

# 3. All tests pass
dotnet test mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx

# 4. Format is clean
dotnet format mcp/CWM.RoslynNavigator/CWM.RoslynNavigator.slnx --verify-no-changes
```

**Never mark a task complete without running all four of these.** Green build + green tests + clean format is the minimum bar.

### For plugin file changes

```bash
# 1. Skill frontmatter is valid
head -8 skills/my-skill/SKILL.md

# 2. Line count is within budget
wc -l skills/my-skill/SKILL.md          # must be ≤400
wc -l .claude/rules/my-rule.md          # must be ≤100
wc -l commands/my-command.md            # must be ≤200

# 3. All cross-references resolve
grep "my-skill" AGENTS.md               # verify agents reference the skill
grep "my-command" AGENTS.md             # verify command is in slash command table

# 4. No duplicate skill names
grep -r "^name:" skills/ | grep "my-skill"   # should appear exactly once
```

---

## Risk Assessment

Before making a change, classify it:

### Low Risk (proceed freely)

- Adding a brand-new skill with no existing dependents
- Adding a new test to an existing test file
- Fixing a typo in documentation
- Adding a new knowledge document
- Updating `CHANGELOG.md`

### Medium Risk (verify thoroughly before committing)

- Modifying an existing skill that agents depend on
- Adding/modifying a rule (affects every conversation)
- Adding a new MCP tool (must not break `WorkspaceManager`)
- Modifying test data in `SampleSolution`
- Changing `hooks.json`

### High Risk (discuss with maintainers first)

- Modifying `WorkspaceManager.cs` (central to all tools)
- Modifying `ToolResponses.cs` (breaking change for clients)
- Changing `Program.cs` startup sequence (MSBuild registration order is critical)
- Modifying `SolutionDiscovery.cs` (affects how all users find solutions)
- Changing `.editorconfig` naming rules (triggers reformatting of entire codebase)
- Changing `AGENTS.md` routing table (changes Claude's fundamental behavior)

### Tightly Coupled Components (extra care required)

| Component | What depends on it |
|-----------|-------------------|
| `WorkspaceManager` | All 15 MCP tools, all tests |
| `ToolResponses.cs` | All tools serialize to these types; clients deserialize them |
| `IAntiPatternDetector` | `DetectAntiPatternsTool` + all 9 detector implementations |
| `AGENTS.md` routing | Claude's intent-to-agent routing logic |
| `.claude/rules/` | Every Claude conversation when kit is active |
| `hooks.json` | Edit/Write/Bash automation for all users |
| `knowledge/common-infrastructure.md` | Referenced by multiple skills |

---

## Committing Changes

### Stage only related files

```bash
# CORRECT — stage the tool file and its test together
git add mcp/CWM.RoslynNavigator/src/Tools/GetMethodComplexityTool.cs
git add mcp/CWM.RoslynNavigator/tests/Tools/GetMethodComplexityTests.cs
git add mcp/CWM.RoslynNavigator/src/Responses/ToolResponses.cs

# WRONG — staging everything including unrelated changes
git add -A
```

### Write a conventional commit message

```bash
git commit -m "feat: add get_method_complexity tool"
```

For commits with a meaningful "why":

```bash
git commit -m "$(cat <<'EOF'
feat: add get_method_complexity tool

Cyclomatic complexity is a common code quality metric. This tool exposes
it via MCP so Claude can identify methods that need refactoring without
reading full files.

High complexity (>10) is flagged as a warning. Critical (>20) as an error.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>
EOF
)"
```

### Commit skill and agent changes atomically

If you add a skill AND wire it into an agent, commit both in one commit — they are one logical unit.

---

## Pull Request Process

1. Run the full verification checklist (build + tests + format + line counts)
2. Write a PR description that explains WHY, not just WHAT
3. Keep PRs focused on one concern — if you added a skill and fixed a tool bug, submit two PRs
4. The `validate.yml` workflow runs automatically on PR and checks frontmatter + lint

---

## What to Do If Something Goes Wrong Mid-Change

If you're halfway through a change and realize the approach is wrong:

**Stop. Don't push through.**

1. Run `git status` to see what's changed
2. `git stash` to set aside your work-in-progress
3. Re-read the relevant section of this guide
4. Plan the correct approach before resuming
5. `git stash pop` to restore your changes and adjust

Do not use destructive git operations (`git reset --hard`, `git clean -f`) without understanding what you're discarding. Those are one-way doors.

---

## Learning From Mistakes

If Claude Code corrects your approach during development, capture the lesson. After any correction:

1. Identify the pattern — what was the mistake?
2. Write it down in a way that prevents it from recurring
3. Update this guide or `09-common-pitfalls.md` if it's a new pitfall

The kit's philosophy: mistake rate should drop over time as patterns are captured and enforced.

---

## Summary Checklist

Before submitting any change:

- [ ] Established clean baseline (build + tests pass before my change)
- [ ] Planned non-trivial changes before implementing
- [ ] Build passes after my change
- [ ] Tests pass after my change (no regressions)
- [ ] Format check passes (`dotnet format --verify-no-changes`)
- [ ] Line counts within budget (skill ≤400, rule ≤100, command ≤200)
- [ ] New skills have all required sections (Core Principles, Patterns, Anti-patterns, Decision Guide)
- [ ] New commands have YAML frontmatter
- [ ] Cross-references in `AGENTS.md` are up to date
- [ ] Staged only related files (not `git add -A`)
- [ ] Conventional commit message used
- [ ] PR description explains WHY
