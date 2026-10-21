# Text-mode anti-pattern scanner for staged C# lines.
#
# This is the no-build tier of the kit's three-tier detection model. It is a
# deliberately conservative mirror of the Roslyn detectors in
# mcp/CWM.RoslynNavigator/src/Analyzers/ — same rule IDs, same severities, same
# source-kind exemptions — implemented without a compiler so a git pre-commit
# hook can run in milliseconds. It is tuned for ZERO false positives: where a
# pattern cannot be resolved without a semantic model, it stays silent and
# leaves the finding to detect_antipatterns. See ADR-006.
#
# Usage:
#   awk -v kind=production -v seeder=0 -f antipattern-scan.awk ADDED_LINES SOURCE
#
#   ADDED_LINES  one line number per line — the lines this commit adds
#   SOURCE       the staged content of the file
#
# Output, one finding per line:
#   RULE|SEVERITY|LINE|MESSAGE|SUGGESTION|SNIPPET

BEGIN {
    FS = "\n"
    in_block = 0      # inside /* ... */
    in_verbatim = 0   # inside @"..."
    in_raw = 0        # inside """..."""
}

# First input file: the set of line numbers this commit touches.
FNR == NR {
    added[$1 + 0] = 1
    next
}

# Second input file: the staged source. Every line is fed through the sanitizer
# so that multi-line comments and strings stay tracked, but only added lines are
# tested against the rules — a pre-existing violation in an untouched line is
# not this commit's problem.
{
    raw = $0
    sub(/\r$/, "", raw)
    code = " " sanitize(raw) " "

    if (!(FNR in added))
        next

    if (applies("AP001") && !suppressed(raw, "AP001"))
        check_async_void(FNR, raw, code)

    if (applies("AP002") && !suppressed(raw, "AP002"))
        check_sync_over_async(FNR, raw, code)

    if (applies("AP003") && !suppressed(raw, "AP003"))
        check_http_client(FNR, raw, code)

    if (applies("AP004") && !suppressed(raw, "AP004"))
        check_datetime(FNR, raw, code)
}

# --- rules -----------------------------------------------------------------

# AP001: async void swallows exceptions and cannot be awaited. Applies
# everywhere including test and migration code — it is a defect in all of them.
function check_async_void(line, raw, code) {
    if (code !~ /[^A-Za-z0-9_]async[ \t]+void[ \t]+[A-Za-z_]/)
        return

    # (object sender, EventArgs e) is the one legitimate async void signature.
    if (code ~ /\([ \t]*object[?]?[ \t]/ && code ~ /EventArgs/)
        return

    report("AP001", "error", line, raw,
        "async void swallows exceptions and cannot be awaited",
        "Change the return type to Task")
}

# AP002: blocking on async work deadlocks ASP.NET Core and starves the thread
# pool. .Result and .Wait() are only reported when the receiver is visibly
# task-like (a call expression, or an identifier ending in Task) — a domain
# Result<T> or a DTO with a .Result property must never trip this hook.
function check_sync_over_async(line, raw, code) {
    if (code ~ /\.GetAwaiter\([ \t]*\)[ \t]*\.GetResult\([ \t]*\)/) {
        report("AP002", "error", line, raw,
            "Synchronous blocking via .GetAwaiter().GetResult()",
            "Await the call instead")
        return
    }

    if (code ~ /(\)|[Tt]ask)[ \t]*\.Result[^A-Za-z0-9_]/) {
        report("AP002", "error", line, raw,
            "Synchronous blocking via .Result",
            "Await the call instead")
        return
    }

    if (code ~ /(\)|[Tt]ask)[ \t]*\.Wait\([ \t]*\)/) {
        report("AP002", "error", line, raw,
            "Synchronous blocking via .Wait()",
            "Await the call instead")
    }
}

# AP003: only the parameterless form is reported. new HttpClient(handler) is the
# documented IHttpClientFactory composition pattern, which the Roslyn detector
# grades as medium confidence — too ambiguous to block a commit on.
function check_http_client(line, raw, code) {
    if (code !~ /new[ \t]+(System\.Net\.Http\.)?HttpClient[ \t]*\([ \t]*\)/)
        return

    report("AP003", "warning", line, raw,
        "Direct HttpClient instantiation causes socket exhaustion under load",
        "Inject IHttpClientFactory")
}

# AP004: the same member list the Roslyn detector forbids. DateTimeOffset.UtcNow
# is intentionally absent from that list, so it is absent here too.
function check_datetime(line, raw, code,    member) {
    member = ""

    if (code ~ /[^A-Za-z0-9_.](System\.)?DateTime\.Now[^A-Za-z0-9_]/)
        member = "DateTime.Now"
    else if (code ~ /[^A-Za-z0-9_.](System\.)?DateTime\.UtcNow[^A-Za-z0-9_]/)
        member = "DateTime.UtcNow"
    else if (code ~ /[^A-Za-z0-9_.](System\.)?DateTimeOffset\.Now[^A-Za-z0-9_]/)
        member = "DateTimeOffset.Now"
    else
        return

    report("AP004", "warning", line, raw,
        "Direct use of " member " is untestable and time-zone dependent",
        "Inject TimeProvider and call GetUtcNow()")
}

# --- applicability ---------------------------------------------------------

# Mirrors the AppliesTo flags on each IAntiPatternDetector. Generated files never
# reach this function — the hook filters them out before invoking awk.
function applies(rule) {
    if (rule == "AP001")
        return 1                                    # production | test | migration
    if (rule == "AP002" || rule == "AP003")
        return kind != "test"                       # production | migration
    if (rule == "AP004")
        return kind == "production" && !seeder      # production, minus data setup
    return 0
}

# An inline "// cwm:ignore" suppresses every rule on the line; naming rules
# ("// cwm:ignore AP004") suppresses only those.
function suppressed(raw, rule,    pos, rest) {
    pos = index(raw, "cwm:ignore")
    if (pos == 0)
        return 0

    rest = substr(raw, pos + 10)
    if (rest !~ /AP[0-9][0-9][0-9]/)
        return 1

    return index(rest, rule) > 0
}

function report(rule, severity, line, raw, message, suggestion,    snippet) {
    snippet = trim(raw)
    if (length(snippet) > 80)
        snippet = substr(snippet, 1, 77) "..."

    print rule "|" severity "|" line "|" message "|" suggestion "|" snippet
}

function trim(s) {
    sub(/^[ \t]+/, "", s)
    sub(/[ \t]+$/, "", s)
    return s
}

# --- sanitizer -------------------------------------------------------------

# Strips comments, string literals, and char literals, carrying block-comment,
# verbatim-string, and raw-string state across lines. This is what keeps a
# commented-out DateTime.Now or a "new HttpClient()" mentioned in a log message
# from blocking a commit.
function sanitize(line,    out, i, n, c, d) {
    out = ""
    n = length(line)
    i = 1

    while (i <= n) {
        c = substr(line, i, 1)

        if (in_block) {
            if (c == "*" && substr(line, i + 1, 1) == "/") {
                in_block = 0
                i += 2
            } else {
                i++
            }
            continue
        }

        if (in_raw) {
            if (substr(line, i, 3) == "\"\"\"") {
                in_raw = 0
                i += 3
            } else {
                i++
            }
            continue
        }

        if (in_verbatim) {
            # "" is an escaped quote inside a verbatim string, not a terminator.
            if (c == "\"") {
                if (substr(line, i + 1, 1) == "\"") {
                    i += 2
                } else {
                    in_verbatim = 0
                    i++
                }
            } else {
                i++
            }
            continue
        }

        if (substr(line, i, 2) == "//")
            break

        if (substr(line, i, 2) == "/*") {
            in_block = 1
            i += 2
            continue
        }

        if (substr(line, i, 3) == "\"\"\"") {
            in_raw = 1
            i += 3
            continue
        }

        if (c == "@" && substr(line, i + 1, 1) == "\"") {
            in_verbatim = 1
            i += 2
            continue
        }

        if (c == "\"" || c == "'") {
            d = c
            i++
            while (i <= n) {
                if (substr(line, i, 1) == "\\") {
                    i += 2
                    continue
                }
                if (substr(line, i, 1) == d) {
                    i++
                    break
                }
                i++
            }
            continue
        }

        out = out c
        i++
    }

    return out
}
