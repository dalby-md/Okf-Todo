---
type: Database Integrity Rules
title: Database Integrity Rules
description: Defines the supported command path for agents that consume the OKF bundle and need to read or mutate tasks.
resource: docs/DATA_MODEL.md
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---

# Database Integrity Rules

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
