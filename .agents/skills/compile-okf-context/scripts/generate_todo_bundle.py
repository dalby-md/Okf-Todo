#!/usr/bin/env python3
"""Generate the structural Okf-Todo OKF bundle from extracted SQLite schema JSON."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path


DESCRIPTIONS = {
    "BodyFormats": "Defines selectable body formats referenced by tasks.",
    "Images": "Stores issue and task image bytes with exactly one owning record.",
    "Issues": "Stores the legacy rich-text issue records.",
    "TaskAttachments": "Stores task-owned attachment metadata and content BLOBs.",
    "TaskChecklistItems": "Stores ordered checklist items owned by tasks.",
    "TaskComments": "Stores human-written comments owned by tasks.",
    "TaskItems": "Stores the primary task records and lifecycle state.",
    "TaskLogEntries": "Stores automatic append-oriented task history entries.",
    "TaskLogTypes": "Defines stable types for automatic task history entries.",
    "TaskPriorities": "Defines selectable task priorities.",
    "TaskRelations": "Stores directed typed relationships between distinct tasks.",
    "TaskRelationTypes": "Defines forward and reverse labels for task relationships.",
    "TaskSources": "Defines selectable origins for tasks.",
    "TaskStatuses": "Defines stable task lifecycle statuses.",
    "TaskTags": "Stores case-insensitively unique tag strings.",
    "TaskTaskTags": "Associates tasks and tags through a composite primary key.",
    "TaskTypes": "Defines selectable task categories.",
    "TaskWaitingFors": "Stores task wait-target history with at most one active target per task.",
}

APPLICATION_SEMANTICS = {
    "TaskItems": [
        "`Owner` is optional free text identifying the person or team accountable for the task.",
        "`Responsible` is optional free text identifying the person currently expected to perform or coordinate the work.",
        "The overview text search includes both values even when their independently controlled task-detail fields are hidden.",
    ],
}


def slug(value: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "-", value).lower()


def frontmatter(kind: str, title: str, description: str, resource: str, timestamp: str) -> str:
    return (
        "---\n"
        f"type: {kind}\n"
        f"title: {title}\n"
        f"description: {description}\n"
        f"resource: {resource}\n"
        "tags:\n"
        "  - sqlite\n"
        "  - todo\n"
        f"timestamp: {timestamp}\n"
        "---\n\n"
    )


def index(group: str, entries: list[tuple[str, str, str]]) -> str:
    lines = [f"# {group}", ""]
    lines.extend(f"* [{title}]({target}) - {description}" for title, target, description in entries)
    return "\n".join(lines) + "\n"


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def table_document(table: dict[str, object], table_names: set[str], timestamp: str) -> str:
    name = str(table["name"])
    description = DESCRIPTIONS.get(name, f"Stores records for the {name} application concept.")
    content = [frontmatter("SQLite Table", name, description, "Okf-Todo/Data/AppDbContext.cs", timestamp)]
    content.extend([f"# {name}", "", "## Purpose", "", description, "", "## Schema", ""])
    content.append("| Column | SQLite type | Null | Default | Role |")
    content.append("| --- | --- | --- | --- | --- |")
    foreign_columns = {str(item["from"]): str(item["table"]) for item in table["foreign_keys"]}
    for column in table["columns"]:
        column_name = str(column["name"])
        roles = []
        if int(column["pk"]):
            roles.append(f"primary key position {column['pk']}")
        if column_name in foreign_columns:
            roles.append(f"foreign key to {foreign_columns[column_name]}.Id")
        role = "; ".join(roles) if roles else "value"
        default = str(column["dflt_value"]) if column["dflt_value"] is not None else "-"
        nullable = "No" if int(column["notnull"]) or int(column["pk"]) else "Yes"
        content.append(f"| `{column_name}` | `{column['type'] or 'ANY'}` | {nullable} | `{default}` | {role} |")

    content.extend(["", "## Relationships", ""])
    if table["foreign_keys"]:
        for relationship in sorted(table["foreign_keys"], key=lambda item: str(item["from"])):
            target = str(relationship["table"])
            target_text = f"[{target}]({slug(target)}.md)" if target in table_names else target
            content.append(
                f"- `{relationship['from']}` references {target_text}.`{relationship['to']}`; "
                f"delete `{relationship['on_delete']}`, update `{relationship['on_update']}`."
            )
    else:
        content.append("No foreign keys originate from this table.")

    content.extend(["", "## Indexes", ""])
    if table["indexes"]:
        for item in sorted(table["indexes"], key=lambda value: str(value["name"])):
            columns = ", ".join(f"`{column['name']}`" for column in item["columns"])
            properties = ["unique" if int(item["unique"]) else "non-unique"]
            if int(item["partial"]):
                properties.append("partial")
            content.append(f"- `{item['name']}` on {columns}: {', '.join(properties)}.")
    else:
        content.append("No secondary indexes are defined.")

    create_sql = str(table["sql"] or "")
    checks = re.findall(r'CONSTRAINT\s+"([^"]+)"\s+CHECK\s+\((.*?)\)(?:,|\r?\n)', create_sql, re.DOTALL)
    content.extend(["", "## Integrity Rules", ""])
    content.append("See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.")
    for check_name, expression in checks:
        normalized = " ".join(expression.split())
        content.append(f"- Check `{check_name}` enforces `{normalized}`.")

    content.extend(
        [
            "",
            "## Application Semantics",
            "",
            "Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.",
        ]
    )
    content.extend(f"- {semantic}" for semantic in APPLICATION_SEMANTICS.get(name, []))
    content.extend(
        [
            "",
            "## Sources",
            "",
            "- [Data model](../../../DATA_MODEL.md)",
            "- `Okf-Todo/Data/AppDbContext.cs`",
            "",
        ]
    )
    return "\n".join(content)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--schema", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--timestamp", default=datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"))
    args = parser.parse_args()
    schema = json.loads(args.schema.read_text(encoding="utf-8"))
    tables = sorted(
        (
            table
            for table in schema["tables"]
            if str(table["name"]) != "__EFMigrationsHistory"
            and not str(table["name"]).startswith("sqlite_")
        ),
        key=lambda item: str(item["name"]),
    )
    table_names = {str(table["name"]) for table in tables}

    root = args.out
    table_entries = [
        (str(table["name"]), f"{slug(str(table['name']))}.md", DESCRIPTIONS.get(str(table["name"]), f"Stores records for the {table['name']} application concept."))
        for table in tables
    ]
    write(root / "index.md", index("Todo Database", [
        ("OKF-Todo SQLite Database", "databases/okf-todo.md", "Describes the local SQLite database and its ownership boundary."),
        ("Tables", "tables/index.md", "Indexes the physical application tables."),
        ("Database References", "references/index.md", "Indexes relationships, integrity rules, and schema lifecycle guidance."),
    ]))
    write(root / "tables" / "index.md", index("SQLite Tables", table_entries))
    for table in tables:
        write(root / "tables" / f"{slug(str(table['name']))}.md", table_document(table, table_names, args.timestamp))

    database_description = "Describes the local SQLite database and its ownership boundary."
    write(root / "databases" / "index.md", index("SQLite Databases", [
        ("OKF-Todo SQLite Database", "okf-todo.md", database_description),
    ]))
    write(root / "databases" / "okf-todo.md", frontmatter("SQLite Database", "OKF-Todo SQLite Database", database_description, "Okf-Todo/Data/DatabasePathProvider.cs", args.timestamp) + f"""# OKF-Todo SQLite Database

## Purpose

The application owns one local SQLite database under the operating system's per-user application-data directory. It stores tasks, controlled lookups, history, relationships, tags, attachments, and image BLOBs.

The path is resolved by `DatabasePathProvider` using these platform rules:

- Windows: `%LOCALAPPDATA%\\Okf-Todo\\okf-todo.db`.
- macOS: `~/Library/Application Support/Okf-Todo/okf-todo.db`.
- Linux: `$XDG_DATA_HOME/Okf-Todo/okf-todo.db` when `XDG_DATA_HOME` is an absolute path; otherwise `~/.local/share/Okf-Todo/okf-todo.db`.

## Contents

The database currently contains {len(tables)} physical application tables. Browse them through the [table index](../tables/index.md).

## Integrity

SQLite foreign-key enforcement is enabled in every application connection. See [Database Integrity Rules](../references/integrity-rules.md) and [Database Relationships](../references/relationships.md).

## Lifecycle

See [Database Schema Lifecycle](../references/schema-lifecycle.md).
""")

    reference_entries = [
        ("Task Application Command Interface", "application-command-interface.md", "Defines the supported command path for agents that consume the OKF bundle and need to read or mutate tasks."),
        ("Database Integrity Rules", "integrity-rules.md", "Summarizes database-enforced and service-level integrity rules."),
        ("Database Relationships", "relationships.md", "Summarizes the foreign-key graph and delete behavior."),
        ("Database Schema Lifecycle", "schema-lifecycle.md", "Explains fresh-database creation and development reset behavior."),
    ]
    write(root / "references" / "index.md", index("Database References", reference_entries))
    relationships = []
    for table in tables:
        for relation in table["foreign_keys"]:
            relationships.append(
                f"- [{table['name']}](../tables/{slug(str(table['name']))}.md).`{relation['from']}` -> "
                f"[{relation['table']}](../tables/{slug(str(relation['table']))}.md).`{relation['to']}`; delete `{relation['on_delete']}`."
            )
    write(root / "references" / "relationships.md", frontmatter("Database Relationships", "Database Relationships", reference_entries[1][2], "Okf-Todo/Data/AppDbContext.cs", args.timestamp) + "# Database Relationships\n\n" + "\n".join(sorted(relationships)) + "\n")
    write(root / "references" / "integrity-rules.md", frontmatter("Database Integrity Rules", "Database Integrity Rules", reference_entries[0][2], "docs/DATA_MODEL.md", args.timestamp) + """# Database Integrity Rules

- Every application connection enables SQLite foreign-key enforcement.
- Required relationships use non-null foreign keys.
- Task-owned rows cascade when the owning task is deleted.
- Lookup and retained-history references use restricted deletion where configured.
- Lookup `Code` values are unique per lookup table.
- Tag `Value` is unique with SQLite `NOCASE` collation.
- `TaskTaskTags` uses `(TaskId, TaskTagId)` as its composite key.
- `TaskWaitingFors` has a filtered unique index allowing at most one unresolved row per task.
- `Images` requires exactly one owner: an issue or a task.
- `TaskRelations` prohibits a task from relating to itself.

Lifecycle transitions and append-oriented history behavior are service-level rules; they are not fully expressible as SQLite constraints.
""")
    write(root / "references" / "schema-lifecycle.md", frontmatter("Database Schema Lifecycle", "Database Schema Lifecycle", reference_entries[2][2], "Okf-Todo/Program.cs", args.timestamp) + """# Database Schema Lifecycle

The application calls EF Core `Database.Migrate()` at startup before seeding or normal data access. EF Core creates a missing database and applies every migration not recorded in `__EFMigrationsHistory`.

Normal desktop startup uses the personal database resolved by `DatabasePathProvider`. The [Task Application Command Interface](application-command-interface.md) may instead receive an absolute database file through `--okf-database-path`. This override is restricted to `--okf-command`; the selected database follows the same migration-before-seeding startup sequence as the personal database.

`InitialCreate` is the earliest supported database version. Every future physical schema change must include a reviewed migration. Builds and normal startup must not delete the database automatically.

EF Core mappings, the model snapshot, and committed migrations define intended structure. A live database may lag until startup applies pending migrations and remains evidence of deployed state, not the authority for intended state.
""")
    print(f"Generated {len(tables)} table concepts in {root.resolve()}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
