---
type: SQLite Table
title: TaskLogEntries
description: Stores automatic append-oriented task history entries.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---


# TaskLogEntries

## Purpose

Stores automatic append-oriented task history entries.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `TaskId` | `INTEGER` | No | `-` | foreign key to TaskItems.Id |
| `TaskLogTypeId` | `INTEGER` | No | `-` | foreign key to TaskLogTypes.Id |
| `Message` | `TEXT` | No | `-` | value |
| `OldValue` | `TEXT` | Yes | `-` | value |
| `NewValue` | `TEXT` | Yes | `-` | value |
| `CreatedAt` | `TEXT` | No | `-` | value |

## Relationships

- `TaskId` references [TaskItems](task-items.md).`Id`; delete `CASCADE`, update `NO ACTION`.
- `TaskLogTypeId` references [TaskLogTypes](task-log-types.md).`Id`; delete `RESTRICT`, update `NO ACTION`.

## Indexes

- `IX_TaskLogEntries_TaskId` on `TaskId`: non-unique.
- `IX_TaskLogEntries_TaskLogTypeId` on `TaskLogTypeId`: non-unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
