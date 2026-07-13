#!/usr/bin/env python3
"""Validate the strict OKF subset used by the Todo database bundle."""

from __future__ import annotations

import argparse
import re
from datetime import datetime
from pathlib import Path


FRONTMATTER = re.compile(r"\A---\s*\n(.*?)\n---\s*\n(.*)\Z", re.DOTALL)
INDEX_ENTRY = re.compile(r"^\* \[([^]]+)]\(([^)]+\.md)\) - (.+)$")
MARKDOWN_LINK = re.compile(r"\[[^]]+]\(([^)]+\.md(?:#[^)]*)?)\)")
REQUIRED = ("type", "title", "description", "timestamp")


def parse_frontmatter(value: str) -> dict[str, object]:
    """Parse the flat keys used by this bundle without a third-party YAML dependency."""
    result: dict[str, object] = {}
    active_list: str | None = None
    for line_number, line in enumerate(value.splitlines(), 1):
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if line.startswith("  - ") and active_list:
            items = result.setdefault(active_list, [])
            assert isinstance(items, list)
            items.append(line[4:].strip().strip("'\""))
            continue
        match = re.match(r"^([A-Za-z][A-Za-z0-9_-]*):(?:\s+(.*))?$", line)
        if not match:
            raise ValueError(f"unsupported frontmatter syntax on line {line_number}")
        key, raw = match.groups()
        if key in result:
            raise ValueError(f"duplicate key {key}")
        if raw is None or raw == "":
            result[key] = []
            active_list = key
        else:
            result[key] = raw.strip().strip("'\"")
            active_list = None
    return result


def validate_timestamp(value: object) -> bool:
    if isinstance(value, datetime):
        return True
    if not isinstance(value, str):
        return False
    try:
        datetime.fromisoformat(value.replace("Z", "+00:00"))
        return True
    except ValueError:
        return False


def resolve_link(source: Path, target: str, root: Path) -> Path:
    clean = target.split("#", 1)[0]
    if clean.startswith("/"):
        return root / clean.lstrip("/")
    return source.parent / clean


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=Path)
    args = parser.parse_args()
    root = args.bundle.resolve()
    errors: list[str] = []
    files = sorted(root.rglob("*.md")) if root.is_dir() else []
    if not (root / "index.md").is_file():
        errors.append("missing root index.md")

    for path in files:
        relative = path.relative_to(root)
        text = path.read_text(encoding="utf-8")
        is_index = path.name == "index.md"
        if is_index:
            if text.startswith("---"):
                errors.append(f"{relative}: index files must not have frontmatter")
            for line_number, line in enumerate(text.splitlines(), 1):
                if line.startswith("* "):
                    match = INDEX_ENTRY.match(line)
                    if not match:
                        errors.append(f"{relative}:{line_number}: invalid index entry")
                    elif not resolve_link(path, match.group(2), root).resolve().is_file():
                        errors.append(f"{relative}:{line_number}: missing target {match.group(2)}")
        else:
            match = FRONTMATTER.match(text)
            if not match:
                errors.append(f"{relative}: missing YAML frontmatter")
                continue
            try:
                metadata = parse_frontmatter(match.group(1))
            except ValueError as error:
                errors.append(f"{relative}: invalid YAML: {error}")
                continue
            for field in REQUIRED:
                if field not in metadata or metadata[field] in (None, ""):
                    errors.append(f"{relative}: missing required field {field}")
            if "timestamp" in metadata and not validate_timestamp(metadata["timestamp"]):
                errors.append(f"{relative}: timestamp is not ISO-8601")
            if not match.group(2).strip():
                errors.append(f"{relative}: empty document body")

        for target in MARKDOWN_LINK.findall(text):
            if not resolve_link(path, target, root).resolve().is_file():
                errors.append(f"{relative}: missing internal link {target}")

    if errors:
        for error in errors:
            print(f"ERROR: {error}")
        print(f"Validation failed with {len(errors)} error(s).")
        return 1
    print(f"Validated {len(files)} Markdown file(s) in {root}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
