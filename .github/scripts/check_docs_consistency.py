#!/usr/bin/env python3
"""Verify component counts claimed in docs match the actual repository contents.

Ground truth (computed dynamically from the working tree):
  skills     : skills/*/SKILL.md directories
  agents     : agents/*.md files
  rules      : .claude/rules/*.md (plus rules/**/*.md if present)
  templates  : templates/*/ directories
  mcp tools  : [McpServerTool(...)] attribute occurrences in mcp/**/Tools/*.cs
  workflows  : rows of the "## Slash Commands" table in README.md
               (each listed /name must also exist as a skill directory)

Claim sources scanned: README.md, docs/shorthand-guide.md,
.claude-plugin/plugin.json, .claude-plugin/marketplace.json.
Every numeric claim found (e.g. "45 skills", "15 MCP tools",
"## Slash Commands (14)") must equal the computed actual. Sources without
counts pass trivially.
"""

from __future__ import annotations

import glob
import os
import re
import sys

CLAIM_SOURCES = [
    "README.md",
    "docs/shorthand-guide.md",
    ".claude-plugin/plugin.json",
    ".claude-plugin/marketplace.json",
]

# category -> list of regexes whose single capture group is the claimed count
CLAIM_PATTERNS: dict[str, list[re.Pattern[str]]] = {
    "skills": [re.compile(r"\b(\d+)\s+skills\b", re.I)],
    "agents": [re.compile(r"\b(\d+)\s+(?:specialist\s+)?agents\b", re.I)],
    "rules": [re.compile(r"\b(\d+)\s+rules\b", re.I)],
    "templates": [re.compile(r"\b(\d+)\s+(?:project\s+)?templates\b", re.I)],
    "mcp tools": [
        re.compile(r"\b(\d+)\s+(?:read-only\s+)?(?:Roslyn\s+)?MCP\s+tools\b", re.I),
        re.compile(r"\((\d+)\s+tools\)", re.I),
    ],
    "workflows": [
        re.compile(r"\b(\d+)\s+slash[- ]command", re.I),
        re.compile(r"^##\s+Slash\s+Commands\s+\((\d+)\)", re.I),
    ],
}

MCP_TOOL_ATTR = re.compile(r"\[\s*McpServerTool\s*[(,\]]")
SLASH_HEADING = re.compile(r"^##\s+Slash\s+Commands\b", re.I)
SLASH_ROW = re.compile(r"^\|\s*`/([A-Za-z0-9_-]+)`")


def count_mcp_tools() -> int:
    total = 0
    for path in glob.glob("mcp/**/Tools/*.cs", recursive=True):
        with open(path, encoding="utf-8") as f:
            total += len(MCP_TOOL_ATTR.findall(f.read()))
    return total


def slash_commands_from_readme(errors: list[str]) -> int | None:
    """Return the number of /commands listed in README's Slash Commands table,
    verifying each maps to an existing skill directory."""
    if not os.path.isfile("README.md"):
        errors.append("README.md not found")
        return None
    with open("README.md", encoding="utf-8") as f:
        lines = f.readlines()
    names: list[str] = []
    in_section = False
    for line in lines:
        if SLASH_HEADING.match(line):
            in_section = True
            continue
        if in_section and line.startswith("## "):
            break
        if in_section:
            match = SLASH_ROW.match(line)
            if match:
                names.append(match.group(1))
    if not in_section:
        return None
    for name in names:
        if not os.path.isfile(f"skills/{name}/SKILL.md"):
            errors.append(
                f"README.md slash-command table lists /{name} "
                f"but skills/{name}/SKILL.md does not exist"
            )
    return len(names)


def compute_actuals(errors: list[str]) -> dict[str, int | None]:
    rules = set(glob.glob(".claude/rules/*.md")) | set(
        glob.glob("rules/**/*.md", recursive=True)
    )
    return {
        "skills": len(glob.glob("skills/*/SKILL.md")),
        "agents": len(glob.glob("agents/*.md")),
        "rules": len(rules),
        "templates": len([d for d in glob.glob("templates/*") if os.path.isdir(d)]),
        "mcp tools": count_mcp_tools(),
        "workflows": slash_commands_from_readme(errors),
    }


def scan_claims(actuals: dict[str, int | None], errors: list[str]) -> int:
    checked = 0
    for source in CLAIM_SOURCES:
        if not os.path.isfile(source):
            print(f"note: {source} not found, skipping")
            continue
        with open(source, encoding="utf-8") as f:
            lines = f.readlines()
        for lineno, line in enumerate(lines, start=1):
            for category, patterns in CLAIM_PATTERNS.items():
                for pattern in patterns:
                    for match in pattern.finditer(line):
                        checked += 1
                        claimed = int(match.group(1))
                        actual = actuals[category]
                        if actual is None:
                            errors.append(
                                f"{source}:{lineno} claims {claimed} {category} but "
                                "the actual count could not be computed "
                                "(no Slash Commands table found in README.md)"
                            )
                        elif claimed != actual:
                            errors.append(
                                f"{source}:{lineno} claims {claimed} {category}, "
                                f"actual is {actual} — '{match.group(0).strip()}'"
                            )
    return checked


def main() -> int:
    errors: list[str] = []
    actuals = compute_actuals(errors)
    print("actual counts:")
    for category, value in actuals.items():
        print(f"  {category}: {value if value is not None else 'n/a'}")
    checked = scan_claims(actuals, errors)
    print(f"checked {checked} numeric claim(s) across {len(CLAIM_SOURCES)} source(s)")
    for error in errors:
        print(f"ERROR: {error}")
    if errors:
        print(f"{len(errors)} docs-consistency error(s) found")
        return 1
    print("all documented counts match the repository contents")
    return 0


if __name__ == "__main__":
    sys.exit(main())
