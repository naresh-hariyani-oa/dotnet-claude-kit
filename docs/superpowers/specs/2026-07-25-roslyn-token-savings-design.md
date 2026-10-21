# Roslyn Navigator Token Savings & Insights — Design

**Date:** 2026-07-25
**Status:** Approved (design), pending implementation plan
**Scope:** Measure and report the token savings and usage insights delivered by the `cwm-roslyn-navigator` MCP server, per repository.

---

## Problem

The Roslyn Navigator MCP server exists on the premise that structured symbol queries cost far fewer tokens than reading and grepping source files. That premise is currently unmeasured. We need a per-repo number we can trust — one that holds up when a skeptical reader asks how it was calculated — plus the usage insights needed to decide which of the 20 tools earn their place in the next release.

## The Counterfactual Problem

"Tokens saved versus not using Roslyn" is a counterfactual: we cannot measure what did not happen. There are two ways to handle this and only one is acceptable.

**Rejected:** hardcoding per-tool constants ("a grep costs ~8k tokens") and multiplying by call count. This produces a large number and no defensible methodology.

**Adopted — the file-read floor:** the server has the workspace loaded, so at call time it knows exactly which source files an answer was derived from. The baseline is the real on-disk token size of those files. Without the tool, Claude would have had to read *at minimum* those files to answer the same question.

This deliberately understates savings. It ignores the false-positive files a text search would have surfaced and the wrong files read before the right one. Understating is the correct direction of error for a public claim.

## Goals

1. Per-repo token savings, computed from a defensible baseline.
2. Tool leaderboard — which tools carry value, which are dead weight.
3. Adoption rate — in repos where the MCP is configured, what share of code navigation actually went through it.
4. Miss rate and failures — zero-result calls, not-ready calls, errors.
5. Cost in dollars and repo-scale context.
6. Two surfaces: in-session (dev feedback loop) and cross-repo aggregate (public claim).

## Non-Goals

- No network transmission. All data stays on the local machine.
- No dashboard UI. Table and markdown output only.
- No user identity or cross-machine correlation.
- No historical trend charts.

---

## Architecture

Four components. Two data sources feed one reporting engine.

```
┌─ MCP server (per repo, live) ─────────────┐
│  20 tools ──> ToolTelemetry ──> calls.jsonl│  precise per-call counterfactual
└────────────────────────────────────────────┘
                                    │
┌─ Claude Code transcripts ─────────┐│
│  ~/.claude/projects/**/*.jsonl    ││  real measured spend, retroactive
└───────────────────────────────────┘│
                    │                │
                    ▼                ▼
            ┌─ CWM.RoslynNavigator.Stats ─┐
            │  join, aggregate, report     │
            └──────────────────────────────┘
                    │                │
         get_token_savings      /token-report
         (MCP tool, this repo)  (skill, all repos)
```

### Component 1 — `ToolTelemetry` (in-server)

A singleton service reached through `WorkspaceManager`, which every tool method already receives.

Each of the 20 tool methods gains one line at its return site:

```csharp
return telemetry.Record("find_symbol", json, baselineFiles: results.Select(r => r.File));
```

`Record` returns the JSON string unchanged, so tool logic is untouched and the change is mechanical and reviewable. Writes are pushed onto a bounded `Channel<CallRecord>` drained by a background writer — a tool call never blocks on disk I/O.

**If ModelContextProtocol 1.4.1 exposes a tool-invocation filter or middleware hook, use it instead of the per-tool line.** Verify this during planning; the one-line approach is the fallback, not the preference.

#### Baseline calculation

Baseline computation must be effectively free, because it runs on every call.

Since the response already carries the file paths the answer came from, the baseline is:

```
baseTok = Σ FileInfo.Length(path) / bytesPerToken   for each distinct, not-yet-counted path
```

No file is read. One `stat` per path, cached by `(path, lastWriteUtc)`. Cost is microseconds.

#### Session-level baseline dedup

The single most important honesty guard.

If `find_symbol` is called five times for `OrderService`, naively summing counts that file's baseline five times. But without the MCP, Claude would have read the file once and it would have remained in context for the rest of the session. Counting it five times is inflation.

**Rule:** a file path that has already contributed to a baseline earlier in the session contributes zero to every later baseline. Implemented as a per-process `HashSet<string>` — the MCP server process lifetime corresponds to the session.

This materially reduces the headline number. That is the intended trade.

#### Token estimation

`bytesPerToken` is a single documented constant, not an unexplained magic number. C# source is denser than prose, so it sits near 3.5 rather than the ~4 commonly cited for English text.

**Calibration method:** a one-off Stats CLI subcommand (`stats calibrate`) samples N `.cs` files from the repos on disk, submits each to the Anthropic `count_tokens` endpoint, and regresses byte length against exact token count. The resulting divisor is committed as a constant with the sample size and R² recorded next to it. `count_tokens` is free and returns exact counts, which makes this reproducible by anyone who wants to check the claim.

The calibration subcommand requires an API key and runs manually — it is never invoked by the MCP server, which stays fully offline. The derived constant plus its sample size are printed in every report footer.

### Component 2 — Storage

```
%LOCALAPPDATA%/cwm-roslyn-navigator/repos/<sha256(solutionPath)[..12]>/
  repo.json     # solution path, display name, project count, total .cs bytes
  calls.jsonl   # append-only, one line per call
```

On Linux and macOS the root resolves via `Environment.SpecialFolder.LocalApplicationData`.

Storing outside the user's repository means no `.gitignore` churn, no accidental commits of usage data, and survival across a re-clone.

One record per call, roughly 120 bytes:

```json
{"ts":"2026-07-25T10:22:01Z","tool":"find_symbol","ok":true,"results":3,
 "respTok":180,"baseTok":4200,"files":3,"ms":42,"state":"ready"}
```

**Rotation:** roll at 50 MB, retain 90 days.

**Opt-out:** `CWM_ROSLYN_TELEMETRY=off` disables collection entirely. Documented prominently in the MCP server README alongside an explicit statement that no data leaves the machine.

### Component 3 — `CWM.RoslynNavigator.Stats` (new console project)

A sibling to `src/` and `tests/` in the existing `.slnx`. Chosen over a bash or Python script because it must process on the order of a gigabyte of JSONL, must run cross-platform, and should be testable with the xUnit v3 setup already in the repo.

Two readers:

1. **Telemetry reader** — per-repo `calls.jsonl` → savings, leaderboard, miss rate.
2. **Transcript miner** — `~/.claude/projects/**/*.jsonl` → adoption rate, real measured token spend, session counts, dollar cost.

Output modes: human-readable table (default), `--json`, `--markdown`.

#### Transcript schema (verified 2026-07-25)

Each line is a JSON object. Relevant fields:

- `type: "assistant"` with `message.usage` → `input_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`, `output_tokens`. Real accounting, not estimates.
- `message.content[]` entries of `type: "tool_use"` → tool name and input.
- `type: "user"` entries carrying `toolUseResult` → the result payload.
- `cwd`, `gitBranch`, `sessionId`, `timestamp`, `version` → per-repo attribution comes free.
- `isSidechain: true` marks subagent turns.

#### The repo join

Transcript directories encode the working directory (`C--Users-mukesh-repos-codewithmukesh-dotnet-claude-kit`); telemetry keys on a solution-path hash. The join normalizes both to a repository root path.

This is the most fragile part of the design and the piece most likely to need adjustment during implementation. Where a join fails, the repo is reported under transcript-only metrics rather than silently dropped.

### Component 4 — Surfaces

**MCP tool `get_token_savings`** — read-only, consistent with the server's no-mutation rule. Reads this repo's `calls.jsonl` and returns a compact summary. Answers "how much have we saved in this repo?" mid-session with no context switch.

**Skill `/token-report`** — a workflow orchestrator (≤200 lines per the kit's standard) that shells out to the Stats CLI and renders the cross-repo aggregate. This is the surface that produces the public number.

---

## Metric Definitions

### Tokens saved

```
saved(call) = max(0, baseTok − respTok)
```

Counted only when the call returned at least one result and the workspace state was `ready`. Baseline paths are deduplicated within a call and across the session (see session-level dedup above). Per-call baseline is capped at the repo's total token size so that broad tools such as `get_public_api` cannot claim more than the repository contains.

### Adoption rate

```
adoption = roslyn_calls / (roslyn_calls + Read/Grep/Glob calls targeting .cs files)
```

The `.cs` filter is what makes this number meaningful — reading a markdown file is not work Roslyn could have replaced. Repos with no MCP configured are excluded from the denominator entirely; including them would produce a misleadingly low aggregate.

Low adoption in a configured repo is a signal that a tool description is failing to attract the right calls — an actionable finding, and arguably more valuable than the savings figure.

### Miss rate and failures

Reported as three separate figures, never conflated:

- **Zero-result rate** — calls returning no results. Indicates a misleading tool description.
- **Not-ready rate** — calls hitting the loading state. Indicates a startup-timing problem.
- **Error rate** — exceptions and failures.

These have different root causes and different fixes; a combined number would point at the wrong one.

### Tool leaderboard

Per tool: call count, tokens saved, median latency, zero-result percentage. A tool with zero calls across all repos is flagged as dead weight and becomes a candidate for removal or a description rewrite.

### Cost and scale

Savings converted to dollars at a configurable input-token rate, with the rate disclosed in the report. Repo scale is total `.cs` bytes ÷ divisor, supporting statements of the form: "this repo is 2.4M tokens; 340 queries pulled 180k into context instead of 3.1M."

---

## Error Handling

- **Telemetry write failure** — swallowed and logged at debug level. A disk problem must never break a tool call. This is absolute.
- **Baseline calculation failure** (missing file, permission denied) — record the call with `baseTok: 0` rather than dropping it.
- **Workspace not ready** — recorded with `state: "loading"`, excluded from savings, counted in the not-ready bucket.
- **Zero results** — recorded, contributes 0 saved, counts toward the miss rate.
- **Malformed transcript line** — skipped. A transcript being written live will have a partial trailing line; this is expected, not an error.
- **Unparseable transcript directory name** — reported as unattributed rather than dropped.

## Testing Strategy

- **Unit** — `BaselineCalculator`: per-call dedup, session dedup, repo-total cap, missing-file handling.
- **Unit** — `TokenEstimator`: divisor application, boundary sizes.
- **Unit** — telemetry record serialization round-trip.
- **Unit** — transcript parser against small handcrafted fixture JSONL. Real transcripts are never committed; they contain PII.
- **Integration** — rides the existing `TestSolutionFixture`: invoke `find_symbol` against the sample solution, assert a telemetry record lands with the expected shape and a non-zero baseline.
- Telemetry is disabled by default under test. The existing suite must remain green.

## Implementation Order

1. `ToolTelemetry` + `BaselineCalculator` + storage, wired into two tools only. Verify records land correctly.
2. Roll out the one-line change to the remaining 18 tools.
3. `get_token_savings` MCP tool — closes the loop with a visible number early.
4. `CWM.RoslynNavigator.Stats` telemetry reader — savings, leaderboard, miss rate.
5. Transcript miner — adoption rate, cost, real spend.
6. `/token-report` skill.
7. Divisor calibration and README documentation of the methodology.

Steps 1–3 deliver a working in-session number. Steps 4–7 deliver the aggregate.

## Open Questions for Planning

- Does ModelContextProtocol 1.4.1 expose a tool-invocation filter? If so it replaces the per-tool `Record` line.
- Which input-token rate to default to, given tool results enter as fresh input and are then cached.
