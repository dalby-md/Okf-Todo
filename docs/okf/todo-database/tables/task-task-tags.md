---
type: SQLite Table
title: TaskTaskTags
description: Associates tasks and tags through a composite primary key.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-13T00:00:00Z
---


# TaskTaskTags

## Purpose

Associates tasks and tags through a composite primary key.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `TaskId` | `INTEGER` | No | `-` | primary key position 1; foreign key to TaskItems.Id |
| `TaskTagId` | `INTEGER` | No | `-` | primary key position 2; foreign key to TaskTags.Id |

## Relationships

- `TaskId` references [TaskItems](task-items.md).`Id`; delete `CASCADE`, update `NO ACTION`.
- `TaskTagId` references [TaskTags](task-tags.md).`Id`; delete `CASCADE`, update `NO ACTION`.

## Indexes

- `IX_TaskTaskTags_TaskTagId` on `TaskTagId`: non-unique.
- `sqlite_autoindex_TaskTaskTags_1` on `TaskId`, `TaskTagId`: unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
