# ADR-006: Three-Tier Anti-Pattern Detection

## Status

Accepted

## Context

The kit detects the same four anti-patterns in two places, using two different
technologies:

| Rule | Pattern | Roslyn detector | Pre-commit hook |
|---|---|---|---|
| AP001 | `async void` | `AsyncVoidDetector` | text scan |
| AP002 | `.Result` / `.Wait()` / `.GetAwaiter().GetResult()` | `SyncOverAsyncDetector` | text scan |
| AP003 | `new HttpClient()` | `HttpClientInstantiationDetector` | text scan |
| AP004 | `DateTime.Now` / `UtcNow` | `DateTimeDirectUseDetector` | text scan |

Duplicating detection logic invites the obvious question, raised in [issue #23]:
if a Roslyn analyzer is strictly more precise than text matching, why does the
pre-commit hook match text at all?

The question is correct on precision. `SyntaxTree` and `SemanticModel` know
things a regex cannot: whether the receiver of `.Result` is actually a `Task<T>`
or a domain `Result<T>`; whether a token sits inside a comment, a verbatim
string, or a raw string literal; whether an `async void` method carries an
`(object, EventArgs)` event-handler signature. The kit's Roslyn detectors use
all of that, and commit `f22c03f` cut their false-positive rate from ~95% to
near zero by leaning on it.

The constraint is not precision — it is where each tier can run.

### Evaluation Criteria

1. **Reachability.** Can the tier run in the context where the check is needed?
2. **Latency.** Does it fit the interaction it gates?
3. **Setup cost.** What must a user install or configure before it works?
4. **Precision.** How many false positives does it produce?

### Alternatives Considered

**Roslyn analyzer package referenced by the user's build (`.csproj` +
`Directory.Build.props`).** Maximum precision, IDE squiggles, works in CI. But it
requires publishing and versioning a NuGet package, a `PackageReference` in
every consuming project, and a successful compile to produce any diagnostic —
which the broken-build case cannot supply. It is the strongest option and is not
ruled out; it is simply not a substitute for a commit gate.

**Invoke the Roslyn MCP tool from the git hook.** Not possible. `Program.cs` is
an MCP stdio server with no CLI entry point, and MCP servers are reachable only
from inside an MCP client such as Claude Code. Git hooks run in a bare shell.

**Shell out to `dotnet build` from the pre-commit hook.** Reachable, but an
`MSBuildWorkspace` load plus compile costs seconds to tens of seconds on a real
solution, in front of every single commit — and fails outright when the build is
red, which is exactly when a developer is committing work-in-progress.

**Drop the pre-commit hook entirely.** Leaves the window between "Claude wrote
it" and "it is in history" ungated for anyone not running `/verify` first.

## Decision

Keep all three tiers, each scoped to what it can actually reach, with a single
shared vocabulary: **rule IDs, severities, and source-kind exemptions are
identical across tiers.**

| Tier | Where it runs | Cost | Precision | Purpose |
|---|---|---|---|---|
| 1. `hooks/pre-commit-antipattern.sh` | git pre-commit, bare shell | milliseconds, no build | high, incomplete | Last gate before history |
| 2. `detect_antipatterns` (Roslyn MCP) | Claude Code session | workspace load | exhaustive | The authoritative pass |
| 3. Analyzer packages in the build | IDE + CI | full compile | exhaustive | Team-wide enforcement |

Tier 1 is text-based **because it must be**, and it is written to earn that
position rather than to approximate tier 2:

- **It scans only the lines the commit adds.** A pre-existing violation in an
  untouched line is not this commit's problem. Whole-file scanning made every
  edit to a legacy file un-committable.
- **It strips comments and string literals** with a state machine that tracks
  block comments, verbatim strings, and raw strings across line boundaries.
- **It mirrors `SourceClassifier`** — generated files are skipped, and each rule
  applies to the same `SourceKind` set as its Roslyn counterpart.
- **It stays silent when it cannot be certain.** `.Result` is reported only when
  the receiver is visibly task-like (a call expression, or an identifier ending
  in `Task`). `new HttpClient(handler)` is not reported, because the Roslyn
  detector grades it medium-confidence. Tier 1 accepts false negatives to
  guarantee zero false positives; tier 2 catches what tier 1 declines to guess.

### When to deviate

- **Adopting the kit on an existing codebase?** Set
  `CWM_ANTIPATTERN_WARN_ONLY=1` to report without blocking.
- **A finding is genuinely correct-by-design?** Suppress the line with
  `// cwm:ignore AP004`, not `--no-verify`.
- **Need enforcement your teammates cannot skip?** That is tier 3. A git hook
  lives in `.git/`, is not cloned, and is one `--no-verify` from irrelevant. CI
  is the only tier that binds a team.

### Adding a rule

Add it to tier 2 first, in `mcp/CWM.RoslynNavigator/src/Analyzers/`, with tests.
Port it to tier 1 only if it can be recognized from a single sanitized line with
no semantic model. Rules that cannot meet that bar stay in tier 2 — an incomplete
tier 1 is correct by design.

## Consequences

### Positive

- Every tier runs where it is actually reachable, at a latency that fits.
- Shared rule IDs mean a hook finding and an MCP finding are the same finding —
  `AP002` means one thing across the kit.
- Tier 1 needs no NuGet package, no build, and no Claude Code session.
- Diff-scoped scanning makes the hook adoptable on a legacy codebase.

### Negative

- Detection logic is expressed twice, in C# and in awk.
- Tier 1 has real blind spots: multi-line method signatures, `.Wait()` on a
  plainly-named `Task` variable, interpolation holes inside string literals.
- Three tiers is more surface area to document than one.

### Mitigations

- The awk scanner documents each rule's Roslyn counterpart inline, and both live
  under the same `APnnn` IDs, so drift is visible in review.
- Blind spots are deliberate and covered by tier 2, which `/verify` runs — the
  hook is a gate, never the audit.
- `hooks/README.md` carries the tier table for users who never read ADRs.

[issue #23]: https://github.com/codewithmukesh/dotnet-claude-kit/issues/23
