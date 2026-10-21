#!/usr/bin/env python3
"""Validate YAML frontmatter schemas for plugin agents and skills.

Usage: check_frontmatter.py {agents|skills}

Agents (agents/*.md) — strict schema:
  required : name, description
  optional : model, memory, tools, disallowedTools, isolation, maxTurns,
             effort, color
  forbidden: permissionMode, hooks, mcpServers (reserved for user/project-level
             agents; plugin agents must not declare them)
  model    : tier alias only (fable/opus/sonnet/haiku), never a pinned version

Skills (skills/*/SKILL.md) — light schema:
  required : name, description
  optional : disable-model-invocation (boolean) is explicitly accepted;
             other keys are not rejected here to keep the check forward-compatible
"""

from __future__ import annotations

import glob
import re
import sys

import yaml

AGENT_REQUIRED = {"name", "description"}
AGENT_OPTIONAL = {
    "model",
    "memory",
    "tools",
    "disallowedTools",
    "isolation",
    "maxTurns",
    "effort",
    "color",
}
AGENT_FORBIDDEN = {"permissionMode", "hooks", "mcpServers"}
MODEL_ALIASES = {"fable", "opus", "sonnet", "haiku"}

FRONTMATTER_RE = re.compile(r"\A---[ \t]*\n(.*?)\n---[ \t]*(?:\n|\Z)", re.DOTALL)


def load_frontmatter(path: str):
    with open(path, encoding="utf-8") as f:
        text = f.read()
    match = FRONTMATTER_RE.match(text)
    if match is None:
        return None, f"{path}: missing YAML frontmatter block (--- ... ---)"
    try:
        data = yaml.safe_load(match.group(1))
    except yaml.YAMLError as exc:
        return None, f"{path}: frontmatter is not valid YAML: {exc}"
    if not isinstance(data, dict):
        return None, f"{path}: frontmatter must be a YAML mapping"
    return data, None


def check_agents() -> list[str]:
    errors: list[str] = []
    files = sorted(glob.glob("agents/*.md"))
    if not files:
        return ["no agent files found under agents/"]
    for path in files:
        fm, err = load_frontmatter(path)
        if err:
            errors.append(err)
            continue
        keys = set(fm)
        for key in sorted(AGENT_REQUIRED - keys):
            errors.append(f"{path}: missing required frontmatter field '{key}'")
        for key in sorted(keys & AGENT_FORBIDDEN):
            errors.append(
                f"{path}: field '{key}' is reserved for user/project-level agents "
                "and must not appear in plugin agents"
            )
        unknown = keys - AGENT_REQUIRED - AGENT_OPTIONAL - AGENT_FORBIDDEN
        for key in sorted(unknown):
            allowed = ", ".join(sorted(AGENT_REQUIRED | AGENT_OPTIONAL))
            errors.append(
                f"{path}: unknown frontmatter field '{key}' (allowed: {allowed})"
            )
        model = fm.get("model")
        if model is not None and model not in MODEL_ALIASES:
            errors.append(
                f"{path}: model '{model}' must be a tier alias "
                f"({'/'.join(sorted(MODEL_ALIASES))}), never a pinned version ID"
            )
    print(f"checked {len(files)} agent(s)")
    return errors


def check_skills() -> list[str]:
    errors: list[str] = []
    files = sorted(glob.glob("skills/*/SKILL.md"))
    if not files:
        return ["no SKILL.md files found under skills/"]
    for path in files:
        fm, err = load_frontmatter(path)
        if err:
            errors.append(err)
            continue
        for key in ("name", "description"):
            if key not in fm:
                errors.append(f"{path}: missing required frontmatter field '{key}'")
        # disable-model-invocation is an accepted optional field — validate its
        # type but never reject its presence.
        dmi = fm.get("disable-model-invocation")
        if dmi is not None and not isinstance(dmi, bool):
            errors.append(
                f"{path}: 'disable-model-invocation' must be a boolean, got {dmi!r}"
            )
    print(f"checked {len(files)} skill(s)")
    return errors


def main() -> int:
    if len(sys.argv) != 2 or sys.argv[1] not in ("agents", "skills"):
        print("usage: check_frontmatter.py {agents|skills}", file=sys.stderr)
        return 2
    errors = check_agents() if sys.argv[1] == "agents" else check_skills()
    for error in errors:
        print(f"ERROR: {error}")
    if errors:
        print(f"{len(errors)} frontmatter error(s) found")
        return 1
    print("frontmatter validated successfully")
    return 0


if __name__ == "__main__":
    sys.exit(main())
