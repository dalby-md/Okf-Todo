---
type: SQLite Table
title: TaskItems
description: Stores the primary task records and lifecycle state.
resource: Okf-Todo/Data/AppDbContext.cs
tags:
  - sqlite
  - todo
timestamp: 2026-07-23T18:14:52Z
---


# TaskItems

## Purpose

Stores the primary task records and lifecycle state.

## Schema

| Column | SQLite type | Null | Default | Role |
| --- | --- | --- | --- | --- |
| `Id` | `INTEGER` | No | `-` | primary key position 1 |
| `Title` | `TEXT` | No | `-` | value |
| `Body` | `TEXT` | Yes | `-` | value |
| `BodyFormatId` | `INTEGER` | Yes | `-` | foreign key to BodyFormats.Id |
| `TaskTypeId` | `INTEGER` | No | `-` | foreign key to TaskTypes.Id |
| `TaskStatusId` | `INTEGER` | No | `-` | foreign key to TaskStatuses.Id |
| `TaskPriorityId` | `INTEGER` | Yes | `-` | foreign key to TaskPriorities.Id |
| `TaskSourceId` | `INTEGER` | Yes | `-` | foreign key to TaskSources.Id |
| `SourceReference` | `TEXT` | Yes | `-` | value |
| `SourceUrl` | `TEXT` | Yes | `-` | value |
| `Deadline` | `TEXT` | Yes | `-` | value |
| `CreatedAt` | `TEXT` | No | `-` | value |
| `UpdatedAt` | `TEXT` | No | `-` | value |
| `ActivatedAt` | `TEXT` | Yes | `-` | value |
| `WaitingSince` | `TEXT` | Yes | `-` | value |
| `CompletedAt` | `TEXT` | Yes | `-` | value |
| `CancelledAt` | `TEXT` | Yes | `-` | value |
| `Owner` | `TEXT` | Yes | `-` | value |
| `Responsible` | `TEXT` | Yes | `-` | value |

## Relationships

- `BodyFormatId` references [BodyFormats](body-formats.md).`Id`; delete `RESTRICT`, update `NO ACTION`.
- `TaskPriorityId` references [TaskPriorities](task-priorities.md).`Id`; delete `RESTRICT`, update `NO ACTION`.
- `TaskSourceId` references [TaskSources](task-sources.md).`Id`; delete `RESTRICT`, update `NO ACTION`.
- `TaskStatusId` references [TaskStatuses](task-statuses.md).`Id`; delete `RESTRICT`, update `NO ACTION`.
- `TaskTypeId` references [TaskTypes](task-types.md).`Id`; delete `RESTRICT`, update `NO ACTION`.

## Indexes

- `IX_TaskItems_BodyFormatId` on `BodyFormatId`: non-unique.
- `IX_TaskItems_TaskPriorityId` on `TaskPriorityId`: non-unique.
- `IX_TaskItems_TaskSourceId` on `TaskSourceId`: non-unique.
- `IX_TaskItems_TaskStatusId` on `TaskStatusId`: non-unique.
- `IX_TaskItems_TaskTypeId` on `TaskTypeId`: non-unique.

## Integrity Rules

See [Database Integrity Rules](../references/integrity-rules.md) for cross-table policy.

## Application Semantics

Structural facts are generated from the inspected SQLite database. Application behavior is governed by the product data model and services.
- `Owner` is optional free text identifying the person or team accountable for the task.
- `Responsible` is optional free text identifying the person currently expected to perform or coordinate the work.
- The overview text search includes both values even when their independently controlled task-detail fields are hidden.

## Sources

- [Data model](../../../DATA_MODEL.md)
- `Okf-Todo/Data/AppDbContext.cs`
