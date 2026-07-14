---
name: compile-okf-context
description: Compile the OKF-Todo SQLite and EF Core database design into a compliant Open Knowledge Format (OKF) context graph bundle. Use whenever database design changes, including entities, mappings, tables, columns, keys, foreign keys, indexes, constraints, delete behavior, lookup schema, database initialization, or docs/DATA_MODEL.md; also use when asked to generate, refresh, validate, or explain the Todo database OKF bundle.
---

# Compile OKF Context

Generate and maintain `docs/okf/todo-database` as the navigable knowledge graph for the intended Todo SQLite design.

## Required References

Read `references/okf/SPEC.md` completely before generating. Read `references/upstream.md` for commit-pinned online examples. Open those examples progressively only when resolving a formatting ambiguity or comparing output with the upstream reference bundles. If network access is unavailable, use the local specification and the formatting contract below; do not block database documentation updates.

## Workflow

1. Read `docs/PRD.md`, `docs/DATA_MODEL.md`, and `docs/IMPLEMENTATION_PLAN.md`.
2. Inspect `Okf-Todo/Data/AppDbContext.cs`, entity classes, committed EF Core migrations and model snapshot, lookup seed configuration, database initialization, and relevant integrity tests.
3. Locate the configured SQLite database without modifying it. If it exists, extract its structural schema with:

   ```text
   python scripts/extract_sqlite_schema.py --database <database-path> --out <temporary-json-path>
   ```

4. Treat EF Core mappings, the model snapshot, committed migrations, and current source as authoritative for intended schema and upgrade history. Treat extracted SQLite structure as evidence of deployed state. Explicitly document mismatches; never make a stale database authoritative merely because it exists.
5. Generate the structural bundle, then review and preserve accurate hand-authored application semantics:

   ```text
   python scripts/generate_todo_bundle.py --schema <temporary-json-path> --out docs/okf/todo-database
   ```
6. Validate with:

   ```text
   python scripts/validate_okf_bundle.py docs/okf/todo-database
   ```

7. Include bundle changes in the same change as the database design. Report mismatches and validation results.

## Bundle Layout

Use this stable progressive-disclosure layout:

```text
docs/okf/todo-database/
|-- index.md
|-- databases/
|   |-- index.md
|   `-- okf-todo.md
|-- tables/
|   |-- index.md
|   `-- <sqlite-table-slug>.md
`-- references/
    |-- index.md
    |-- relationships.md
    |-- integrity-rules.md
    `-- schema-lifecycle.md
```

Create one concept document per physical application table. Do not create documents for SQLite internal tables.

## Formatting Contract

Concept documents are UTF-8 Markdown with YAML frontmatter. Use this key order:

```yaml
---
type: SQLite Table
title: TaskItems
description: Stores the primary task records and their lifecycle state.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---
```

Require nonempty `type`, `title`, `description`, and ISO-8601 `timestamp`. Use `resource` and `tags` when useful. Keep descriptions concise and identical between a concept's frontmatter and all index entries that point to it.

Index files have no frontmatter. Group entries beneath `# <group>` headings and use exactly:

```markdown
* [Title](relative/path.md) - Description
```

Keep indexes one level deep where practical. Use relative Markdown links and deterministic alphabetical ordering. Links between concepts are directed graph edges; link table pages to related tables and reference concepts.

## Table Document Content

Use these sections when applicable:

1. `# <SQLite table name>`
2. `## Purpose`
3. `## Schema` with columns for name, SQLite type, nullability, default, and role
4. `## Relationships`
5. `## Indexes`
6. `## Integrity Rules`
7. `## Application Semantics`
8. `## Sources`

State primary keys, alternate keys, foreign-key targets, delete behavior, unique constraints, check constraints, and meaningful indexes precisely. Distinguish database-enforced integrity from service-level rules.

## Safety And Scope

- Open SQLite databases read-only and inspect structure only. Never read task content or BLOB values for this workflow.
- Never delete, migrate, or rewrite a database while compiling knowledge.
- Document the earliest supported migration and startup migration behavior in the schema lifecycle reference.
- Do not invent relationships that are not enforced or documented.
- Do not add REST, web-server, export, or Markdown-format concerns to this bundle unless they directly affect database structure.
- Keep generated links internal to the bundle except for explicit source references.

## Validation

Run the validator after every regeneration. It checks required frontmatter, timestamps, index syntax, root index presence, and internal link targets. A successful application build does not prove that the OKF bundle is current.
